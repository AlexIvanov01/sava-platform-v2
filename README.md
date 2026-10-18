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
- [Helm](https://helm.sh/docs/intro/install/) 3 or later, to deploy to Kubernetes

### Build

`backend/Sava.slnx` contains every service and test project. Build everything from the repository root:

```bash
dotnet build backend/Sava.slnx
```

Each service also keeps its own `.sln`, so you can build one service from its directory:

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

### Tests

Each business service has a test project in `backend/tests/<Service>.Tests` (xUnit v3, Shouldly, NSubstitute). Unit tests cover controllers, mappings, and event processing with substituted dependencies. Integration tests, marked with the `Category=Integration` trait, run repositories and the HTTP API (`WebApplicationFactory`) against a real MySQL 9.7 database. [Testcontainers](https://dotnet.testcontainers.org) starts the database in Docker. RabbitMQ and gRPC clients are always substituted.

Run from the repository root:

```bash
dotnet test --solution backend/Sava.slnx                                          # all tests; Docker must be running
dotnet test --solution backend/Sava.slnx --filter-not-trait "Category=Integration" # unit tests only, no Docker
dotnet test --project backend/tests/OrderService.Tests                             # one service
```

Test data is generated with [Bogus](https://github.com/bchavez/Bogus) through the builders in each test project, so every run uses different values. Each run picks a random seed and failing tests print it in their output (`Test data seed: 123. Set SAVA_TEST_SEED=123 to replay this run.`). To reproduce a failure with the same data, set the variable and rerun:

```bash
SAVA_TEST_SEED=123 dotnet test --solution backend/Sava.slnx
```

To produce a coverage report, collect Cobertura files with coverlet and then render them with ReportGenerator. Both tools are pinned in `dotnet-tools.json`, so run `dotnet tool restore` first. `backend/tests/testconfig.json` excludes `Program.cs`, migrations, and generated gRPC code from coverage.

```bash
dotnet test --solution backend/Sava.slnx --coverlet --results-directory "$PWD/TestResults"
dotnet reportgenerator -reports:"TestResults/*.cobertura*.xml" -targetdir:TestResults/coverage-report -reporttypes:"Html;TextSummary"
open TestResults/coverage-report/index.html
```

### Continuous integration

`.github/workflows/ci.yml` runs on pushes to `main` and on pull requests. It checks formatting with CSharpier, builds `backend/Sava.slnx`, and runs all tests with coverage. The coverage summary goes to the job summary, and the HTML report is uploaded as the `coverage-report` artifact.

### Formatting

C# code is formatted with [CSharpier](https://csharpier.com). It is pinned as a local .NET tool in `dotnet-tools.json` at the repository root. Restore it once after cloning, then format or check the backend:

```bash
dotnet tool restore
dotnet csharpier format backend   # rewrites files in place
dotnet csharpier check backend    # fails if any file isn't formatted
```

A pre-commit hook in `.githooks/` formats staged C#, `.csproj`, and `.props` files and re-stages them. For partially staged files, it formats only the staged version and leaves your unstaged edits alone. Enable it once per clone:

```bash
git config core.hooksPath .githooks
```

Use `git commit --no-verify` to skip it for a single commit. CSharpier also has editor extensions for VS Code, Rider, and Visual Studio that can format on save.

### Run services locally

Running locally uses `appsettings.Development.json`, which points at MySQL and RabbitMQ on `localhost`.

1. Start the infrastructure. Use the MySQL password from the services' `appsettings.Development.json`:

   ```bash
   docker run -d --name sava-mysql -p 3306:3306 -e MYSQL_ROOT_PASSWORD=<password> mysql:9.7
   docker run -d --name sava-rabbit -p 5672:5672 -p 15672:15672 rabbitmq:4.3-management
   ```

   One MySQL instance is enough because each service uses its own database (`savadb_inv`, `savadb_cust`, `savadb_order`). Migrations are applied automatically on startup. In the Development environment, empty databases are also filled with sample data from each service's `dummy_data.json`:

   - **Customers:** 10 Bulgarian companies.
   - **Inventory:** 15 products with 21 batches, including expired, near-expiry, low-stock and inactive cases.
   - **Orders:** 10 orders in several statuses; 5 have invoices.

   Other environments only migrate and never seed. Orders reference the seeded customers and batches, so Order seeds them only after it has synced from Customer and Inventory. If either was down, start it and restart Order.

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

   AuthenticationService signs tokens with `Jwt:Key`. For `dotnet run` it comes from `appsettings.Development.json`; in the cluster it comes from the Secret. The service fails at startup if the key is missing or shorter than 32 characters. Logs go to the console, plus `logs/` files when running locally.

### Deploy with Helm

The Helm chart in `deploy/helm/sava` deploys every service, RabbitMQ and, optionally, MySQL. Pick an environment with a values file:

| File | Target | Environment | MySQL | Secrets | API exposure |
|---|---|---|---|---|---|
| `values-dev.yaml` | Docker Desktop Kubernetes (localhost) | `Development` (sample data, Swagger UI) | In-cluster StatefulSet | Generated by the chart | `http://localhost:80` (LoadBalancer) |
| `values-prod.yaml` | Hosted Kubernetes cluster | `Production` (no seeding) | External managed server | Pre-created Secret | Ingress with TLS |

Every service exposes `/healthz` (liveness) and `/readyz` (readiness; checks the database for Customer, Inventory and Order), which the chart uses for its probes. `appsettings.json` only holds environment-neutral defaults; the chart passes hosts and credentials as environment variables. Only one release fits in a namespace, because `ocelot.json` routes to fixed service names.

#### Development (localhost)

Requires Docker Desktop with Kubernetes enabled and [Helm](https://helm.sh/docs/intro/install/).

`deploy/dev.sh` runs all of the steps below. It builds the images, installs or upgrades the release, restarts the rebuilt services and waits until they're ready:

```bash
deploy/dev.sh                  # build and deploy everything
deploy/dev.sh order customer   # rebuild and redeploy only these services
deploy/dev.sh --reset          # also wipe MySQL, so the databases are re-seeded
```

It refuses to run unless the kubectl context is `docker-desktop`. The manual steps are:

1. Build the images with the `dev` tag. Docker Desktop's cluster pulls them from the local image store:

   ```bash
   for s in AuthenticationService CustomerService InventoryService OrderService OcelotApiGateway; do
     docker build -t alexivanov01/$(echo $s | tr '[:upper:]' '[:lower:]'):dev backend/$s
   done
   ```

2. Install the chart:

   ```bash
   helm upgrade --install sava deploy/helm/sava -f deploy/helm/sava/values-dev.yaml -n sava --create-namespace
   kubectl get pods -n sava -w
   ```

   Order waits until Customer and Inventory are ready, so all three seed their sample data on a fresh database.

   | What | Where |
   |---|---|
   | API gateway | `http://localhost:80` (e.g. `POST /api/Account` with `admin`/`admin123`) |
   | MySQL | `localhost:3307`, user `root`, password `sava-dev-password` |
   | RabbitMQ management | `http://localhost:15673`, `guest`/`guest` |

   The ports differ from the `dotnet run` containers, so both setups can run side by side. If a LoadBalancer service stays `<pending>`, another process holds the host port. Docker Desktop can also leave behind hidden `kindccm-*` proxy containers from deleted services; `docker rm -f` them.

3. After changing code, rebuild the image and restart its deployment (for example `kubectl rollout restart deploy/orderservice-depl -n sava`).

4. To remove everything, run `helm uninstall sava -n sava`. The MySQL volume survives an uninstall; delete it with `kubectl delete pvc data-mysql-0 -n sava` to start from an empty database.

#### Production (hosting provider)

The cluster needs an ingress controller (the chart defaults to the `nginx` class) and [cert-manager](https://cert-manager.io/) with a `ClusterIssuer` for TLS. Set the host and issuer in `values-prod.yaml`, or override them with `--set`.

1. Build and push the images, tagged with the commit:

   ```bash
   TAG=$(git rev-parse --short HEAD)
   for s in AuthenticationService CustomerService InventoryService OrderService OcelotApiGateway; do
     image=alexivanov01/$(echo $s | tr '[:upper:]' '[:lower:]'):$TAG
     docker build -t $image backend/$s && docker push $image
   done
   ```

   Build for the cluster's architecture (for example `docker buildx build --platform linux/amd64 --push`) if it differs from your machine.

2. Create the Secret once. Use a random JWT key of at least 32 characters, and connection strings for your MySQL server:

   ```bash
   kubectl create namespace sava
   kubectl create secret generic sava-secrets -n sava \
     --from-literal=jwt-key="$(openssl rand -base64 48)" \
     --from-literal=rabbitmq-user=sava \
     --from-literal=rabbitmq-password="$(openssl rand -base64 24)" \
     --from-literal=customer-db="server=<host>;port=3306;database=savadb_cust;user=<user>;password=<password>" \
     --from-literal=inventory-db="server=<host>;port=3306;database=savadb_inv;user=<user>;password=<password>" \
     --from-literal=order-db="server=<host>;port=3306;database=savadb_order;user=<user>;password=<password>"
   ```

   The databases are created and migrated on startup, so the user needs permission to create them.

3. Install or upgrade:

   ```bash
   helm upgrade --install sava deploy/helm/sava -f deploy/helm/sava/values-prod.yaml -n sava \
     --set image.tag=$TAG --set ingress.host=<your domain>
   ```

   Roll back with `helm rollback sava -n sava`. RabbitMQ runs in the cluster without persistence; Order's periodic gRPC sync repairs anything lost on a restart.

## Usage

1. Register and authenticate users via the Authentication Service.
2. Use the API Gateway to interact with inventory, order, and customer services.
3. Monitor logs and events using Serilog and RabbitMQ.


