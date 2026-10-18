# Microservices-Based Platform for Distribution Company Systems

A scalable and modern platform designed for distribution companies, built using a microservices architecture. The system includes independent services for managing inventory, orders, and customers, ensuring flexibility and efficiency.

## Features

- **Inventory Service**: Manages product and batch information.
- **Order Service**: Handles customer orders and associated order items.
- **Customer Service**: Maintains customer data and interactions.
- **Authentication Service**: Secures the platform with user authentication and authorization.
- **Centralized Logging**: Uses Serilog for structured and asynchronous logging.
- **API Gateway**: Ocelot API Gateway for routing and aggregating service calls.
- **Inter-Service Communication**: gRPC and RabbitMQ for asynchronous and synchronous communication.

## Technologies Used

- **ASP.NET Core**: Framework for building web APIs and services.
- **Entity Framework Core**: ORM for database access and management.
- **MySQL**: Relational database for storing structured data.
- **RabbitMQ**: Message broker for event-driven communication.
- **gRPC**: High-performance RPC framework for inter-service communication.
- **Docker**: Containerization for consistent environments.
- **Kubernetes**: Orchestration for scaling and managing containerized services.
- **Serilog**: Logging for tracking application behavior and issues.

## System Architecture

The platform follows a microservices architecture, with each service having its own database and clear responsibilities. Communication between services is achieved through gRPC for synchronous operations and RabbitMQ for asynchronous messaging.


## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Docker (Docker Desktop with Kubernetes enabled for the full stack)

### Build

There is no root solution. Each service in `backend/` is a standalone project, so run commands from the service directory:

```bash
cd backend/InventoryService   # or CustomerService, OrderService, AuthenticationService, OcelotApiGateway
dotnet build
```

A Debug build also regenerates the service's OpenAPI document in `backend/openapi/<Service>.json`. Commit those files together with API changes.

### Static analysis

`backend/Directory.Build.props` adds the [SonarAnalyzer.CSharp](https://www.nuget.org/packages/SonarAnalyzer.CSharp) analyzer to every backend project. It reports issues as build warnings (rule IDs `S####`) and doesn't fail the build. To list every warning, not only those in changed files, run a full rebuild:

```bash
dotnet build --no-incremental 2>&1 | grep -E "warning [A-Z]+[0-9]+" | sort -u
```

### Formatting

C# code is formatted with [CSharpier](https://csharpier.com). It is pinned as a local .NET tool in `dotnet-tools.json` at the repository root. Restore it once after cloning, then format or check the backend:

```bash
dotnet tool restore
dotnet csharpier format backend   # rewrites files in place
dotnet csharpier check backend    # fails if any file isn't formatted
```

CSharpier also has editor extensions for VS Code, Rider, and Visual Studio that can format on save.

### Run services locally

Running locally uses `appsettings.Development.json`, which points at MySQL and RabbitMQ on `localhost`.

1. Start the infrastructure. Use the MySQL password from the services' `appsettings.Development.json`:

   ```bash
   docker run -d --name sava-mysql -p 3306:3306 -e MYSQL_ROOT_PASSWORD=<password> mysql:9.7
   docker run -d --name sava-rabbit -p 5672:5672 -p 15672:15672 rabbitmq:4.3-management
   ```

   One MySQL instance is enough because each service uses its own database (`savadb_inv`, `savadb_cust`, `savadb_order`). Migrations and seed data (`dummy_data.json`) are applied automatically on startup.

2. Run each service in its own terminal with `dotnet run`. Start Inventory and Customer before Order, because Order syncs items and customers from them over gRPC.

   | Service | REST and Swagger UI | gRPC |
   |---|---|---|
   | InventoryService | `http://localhost:80/swagger` | `666` |
   | CustomerService | `http://localhost:83/swagger` | `668` |
   | OrderService | `http://localhost:82/swagger` | `667` |
   | AuthenticationService | `http://localhost:80/swagger` | — |

   Ports come from the `Kestrel` section in appsettings, not from `launchSettings.json`. If port 80 is taken (for example by Docker Desktop, or by another service that also uses it), override it:

   ```bash
   Kestrel__Endpoints__Http__Url=http://localhost:5000 dotnet run   # AuthenticationService
   ```

   The API gateway routes to Kubernetes service names (see `ocelot.json`), so it only works in the cluster. When running locally, call the services directly. They don't check authorization themselves; the gateway does.

### Run on Kubernetes

1. Build the images. The deployments in `backend/K8S` use `alexivanov01/<service>:latest`:

   ```bash
   for s in AuthenticationService CustomerService InventoryService OrderService OcelotApiGateway; do
     docker build -t alexivanov01/$(echo $s | tr '[:upper:]' '[:lower:]') backend/$s
   done
   ```

2. Create the MySQL secret that the MySQL deployments read:

   ```bash
   kubectl create secret generic mysql --from-literal=SA_PASSWORD=<password>
   ```

   The password must match the connection strings in the services' `appsettings.json`.

3. Apply the manifests:

   ```bash
   kubectl apply -f backend/K8S/
   ```

   The API gateway is exposed at `http://localhost:80` and the RabbitMQ management UI at `http://localhost:15672`. The MySQL databases are on ports `33061` (inventory), `33062` (customer) and `33063` (order).

## Usage

1. Register and authenticate users via the Authentication Service.
2. Use the API Gateway to interact with inventory, order, and customer services.
3. Monitor logs and events using Serilog and RabbitMQ.


