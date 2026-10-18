# Copilot instructions

Microservices platform for a distribution company: ASP.NET Core 10 services in `backend/`, each with its own MySQL database, behind an Ocelot API gateway, deployed to Kubernetes.

## Build and run

There is no root solution. Each service is a standalone project with its own `.sln`, so run commands from the service directory, for example `backend/InventoryService`:

```bash
dotnet build
dotnet run                       # uses appsettings.Development.json (localhost MySQL/RabbitMQ)
dotnet ef migrations add <Name>  # EF Core tools are referenced in each csproj
```

Docker images are published as `alexivanov01/<service>:latest` and referenced by `backend/K8S/*-depl.yaml`:

```bash
docker build -t alexivanov01/inventoryservice .
kubectl apply -f ../K8S/
```

The repository has no test projects. Static analysis comes from `backend/Directory.Build.props`, which adds the SonarAnalyzer.CSharp Roslyn analyzer to every backend project. Its findings are build warnings (`S####`) and don't fail the build. Run `dotnet build --no-incremental` to see all of them. Docker builds use the service folder as the build context, so they don't import this file. C# formatting uses CSharpier, pinned in the root `dotnet-tools.json`. Run `dotnet tool restore`, then `dotnet csharpier format backend`. The `.githooks/pre-commit` hook does this for staged files once `git config core.hooksPath .githooks` is set.

## Architecture

| Service | Role | Exposes |
|---|---|---|
| `AuthenticationService` | Issues JWTs from a hard-coded user list (`JwtTokenHandler`) with a `Role` claim (`Administrator` / `User`) | `POST /api/Account` |
| `OcelotApiGateway` | Single entry point. Routes in `ocelot.json`; enforces auth and roles per route via `RouteClaimsRequirement` | — |
| `InventoryService` | Products and product batches | REST `:80`, gRPC `:666` (`GrpcInventory.GetAllItems`) |
| `CustomerService` | Customers | REST `:80`, gRPC (`customer.proto`) |
| `OrderService` | Orders, order items, invoices. Holds local copies of items and customers | REST `:80` |

The services don't enforce authorization themselves; the gateway does. If you add or rename an endpoint, update `ocelot.json` too. Read routes usually allow `Administrator` and `User`, while mutating `{id}` routes require `Administrator`.

### Inter-service data flow

OrderService keeps its own `Item` and `Customer` tables. They are kept consistent in two ways:

1. **Async events (RabbitMQ)**
   - Inventory and Customer publish to the **fanout** exchange `trigger`. OrderService subscribes. Inventory events are `Item_Published`, `Item_Updated`, `Item_Deleted`, `ProductName_Updated` and `Product_Deleted`. Customer events are `Customer_Published`, `Customer_Updated` and `Customer_Deleted`.
   - OrderService publishes to the **direct** exchange `order_exchange` with routing key `order.published`. InventoryService subscribes. Events are `Order_Published` (decrement batch quantities) and `Order_Deleted` (increment them back). An order update publishes `Order_Deleted` for the old items and then `Order_Published` for the new ones.
2. **Periodic gRPC sync**: OrderService's `GrpcSyncService` (a `BackgroundService`) runs every `GrpcSyncIntervalInMinutes` (default 5). It pulls all items from Inventory and all customers from Customer, then reconciles them through `PrepDB.SyncItems` and `PrepDB.SyncCustomers`.

Messages are JSON with a string `Event` discriminator. Each `EventProcessor` first deserializes into `GenericEventDto` to read `Event`, then switches to the concrete DTO. When you add an event, update the publisher's DTO and controller, plus the consumer's `DetermineEvent` switch and `EventType` enum.

### Duplicated contracts

Contracts aren't shared between services; each one keeps its own copy:

- `.proto` files: the server copy lives in `InventoryService/Protos` and `CustomerService/Protos` (`GrpcServices="Server"`). OrderService has client copies in `OrderService/Protos` (`GrpcServices="Client"`). Keep both copies in sync.
- Event DTOs are redefined in each service's `Dtos/`, sometimes under different class names. For example, OrderService's `OrderPublishedEvent` is read as `OrderPublishedDto` in InventoryService. Property names must match across services for `System.Text.Json` to deserialize them.
- Services also serve their proto over HTTP at `/protos/<name>.proto`.

## Conventions

- **Service layout**: `Controllers/`, `DataAccess/` (`DbContext`, `I*Repo`/`*Repo`, `PrepDB`), `Dtos/`, `Models/`, `Mappings/` (DTO ↔ entity extension methods), `AsyncDataServices/` (RabbitMQ), `EventProcessing/`, `SyncDataServices/Grpc/`, `Migrations/`.
- **Startup**: `Program.cs` uses minimal hosting. It ends with `await PrepDB.PrepPopulation(app)`, which waits 5 s, applies EF migrations, and seeds the database from `dummy_data.json` when it's empty. Migrations run automatically, so there's no manual `database update` step. `PrepDB` and hosted services are skipped when `isOpenApiGeneration` is true (see **OpenAPI**); keep new startup side effects behind that flag.
- **Configuration**: `appsettings.json` targets Kubernetes ClusterIP service names such as `inventory-clusterip-srv` and `rabbitmq-clusterip-srv`. `appsettings.Development.json` targets `localhost`. Kestrel endpoints are declared in config, with HTTP/1 on 80 and HTTP/2 gRPC on a separate port. Other config keys are `ConnectionStrings:Default`, `RabbitMQHost`/`RabbitMQPort`, and in OrderService, `GrpcInventoryService`/`GrpcCustomerService`.
- **C# style**: `ImplicitUsings` is **disabled**, so add explicit `using` directives. Nullable is enabled. Use file-scoped namespaces that match folders (`InventoryService.DataAccess`).
- **Persistence**: use `Guid` primary keys, nullable model properties, and `[MaxLength]` data annotations, with relationships configured in `OnModelCreating`. Repositories are scoped and async. They return `null` for not-found, and controllers map that to `404`.
- **Pagination**: list endpoints use keyset/cursor paging (`Guid? cursor`, `[Range(1, 100)] int pageSize = 10`), ordered by `Id` and filtered with `Id > cursor`. They return a `*PageReadDto` with the items, the next `Cursor` (last Id) and the total `Pages`.
- **Controllers**: each action is wrapped in `try/catch`. Errors are logged with `Log.Fatal` and return `StatusCode(500, "An internal server error occured.")`. Messages are published to the bus **after** the DB operation succeeds, in a separate `try/catch`, so bus failures don't fail the request.
- **Logging**: use the static Serilog `Log.*` API (not `ILogger<T>`) with structured templates (`{Id}`, `{@Model}`). Informational messages are prefixed with `--> `. Serilog is configured from the `Serilog` section in `appsettings.json`.
- **DI lifetimes**: `IMessageBusClient` and `IEventProcessor` are singletons, and `MessageBusSubscriber` is a hosted service. `EventProcessor` must use `IServiceScopeFactory` to resolve scoped repositories.
- **RabbitMQ client**: RabbitMQ.Client 7 has an async-only API (`IChannel`, `BasicPublishAsync`, `AsyncEventingBasicConsumer`). `MessageBusClient` connects lazily on the first publish and reconnects if the channel is closed; publishes are serialized with a `SemaphoreSlim` because channels aren't safe for concurrent use. If the broker is down, it logs a warning and drops the message. `MessageBusSubscriber` connects in `ExecuteAsync` and retries every 5 s until the broker is reachable. After that, the client's automatic recovery restores the connection and consumer.
- **Mapping**: no mapping library. Each service has hand-written static extension methods in `Mappings/` (`ToReadDto()`, `ToEntity()`, `ToEntity(id)` for updates, `ToGrpcModel()`, `ToPublishedDto()`). When you add a property to a model or DTO, update the matching mapping methods; the compiler won't flag a missing assignment. Protobuf string fields reject `null`, so map nullable strings with `?? string.Empty`. Mappings to protobuf models convert `Guid`, `decimal` and `DateTime` to `string`.
- **OpenAPI**: each service uses the built-in `Microsoft.AspNetCore.OpenApi` (`AddOpenApi()`/`MapOpenApi()`), with Swagger UI at `/swagger` in Development. `Microsoft.Extensions.ApiDescription.Server` regenerates `backend/openapi/<Service>.json` on every Debug build (Release builds skip it). Commit those files with API changes; they are the contract for API clients. Declare response types with `ActionResult<T>` plus `[ProducesResponseType]` for each status code an action returns.
