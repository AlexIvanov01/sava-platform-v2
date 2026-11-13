# Microservices-Based Platform for Distribution Company Systems

A scalable and modern platform designed for distribution companies, built using a microservices architecture. The system includes independent services for managing inventory, orders, and customers, ensuring flexibility and efficiency.

## Features

- **Inventory Service**: Manages product and batch information.
- **Order Service**: Handles customer orders (with snapshots of their products, batches and customer) and invoices.
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

### Order events (outbox and inbox)

OrderService tells InventoryService (stock) and CustomerService (which customer an order belongs to) about order changes through RabbitMQ, without losing events:

1. Saving an order also writes an `OutboxMessages` row in the same transaction, so the change and its event are stored together or not at all.
2. A background publisher in OrderService sends the row right away to the `order_events` exchange and marks it `Sent` once the broker confirms it. If RabbitMQ is down or the pod restarts, the row stays `Pending` and is sent by a sweep every 15 minutes (or sooner, by the next order save). A MySQL lock lets only one pod send at a time, which keeps events in order. A send that keeps failing is retried with backoff and marked `Failed` after 10 attempts.
3. Each consumer receives one message at a time (single active consumer), processes it and records it in its `InboxMessages` table in one transaction, then acknowledges it. A message already recorded is skipped. If processing fails, the message goes back to the queue and is retried; after 5 attempts, or straight away for a message that can't be processed, it is recorded as `Failed`.

Each event carries the order's full state, so when several unsent events of one order are waiting, only the newest is sent (the others become `Superseded`). A nightly cleanup (01:00 UTC) deletes sent and completed rows older than 7 days; `Failed` rows stay until handled. The `Messaging` section of each service's `appsettings.json` holds these limits.

Both tables keep the payload, attempts and last error, and the outbox `Id` is the inbox `Id` (the message's `MessageId`), so an event can be followed from OrderService to each consumer. To replay a failed event: in the outbox, `UPDATE OutboxMessages SET Status='Pending', Attempts=0, NextAttemptAt=NULL WHERE Id='<id>'`; for the inbox, delete its row and resend it from the outbox the same way.

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

Each business service has a test project in `backend/tests/<Service>.Tests` (xUnit v3, Shouldly, NSubstitute). Unit tests cover controllers, mappings, and event processing with substituted dependencies. Integration tests, marked with the `Category=Integration` trait, run repositories and the HTTP API (`WebApplicationFactory`) against a real MySQL 9.7 database. [Testcontainers](https://dotnet.testcontainers.org) starts the database in Docker. The RabbitMQ and gRPC clients are always substituted; consumer tests hand messages to the inbox the way the subscriber does.

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

   One MySQL instance is enough because each service uses its own database (`savadb_auth`, `savadb_inv`, `savadb_cust`, `savadb_order`). Migrations are applied automatically on startup. In the Development environment, empty databases are also filled with sample data from each service's `dummy_data.json`:

   - **Authentication:** the first administrator is created from `Seed:AdminUserName`/`Seed:AdminPassword` (`admin`/`admin123` in `appsettings.Development.json`) in every environment when the Users table is empty; Development also adds `user`/`user123` from `dummy_data.json`.
   - **Customers:** 10 Bulgarian companies.
   - **Inventory:** 8 categories and 15 products with 21 batches, including expired, near-expiry, low-stock, inactive and unpublished cases.
   - **Company settings:** sample supplier details from `dummy_company.json`.
   - **Orders:** 10 orders: 5 `Completed` (with invoices 0000000001–0000000005, paid, unpaid and overdue), 3 `InProcess`, 1 `Pending` and 1 `Cancelled`.

   Other environments only migrate and never seed. Orders reference the seeded customers, products and batches, so Order snapshots them from Customer and Inventory over gRPC when it seeds. If either was down, start it and restart Order.

2. Run each service in its own terminal with `dotnet run`. Start Inventory and Customer before Order, because Order seeds its sample orders from them over gRPC, and saving an order needs both.

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

   The API gateway routes to Kubernetes service names (see `ocelot.json`), so it only works in the cluster. When running locally, call the services directly. They don't check authorization themselves; the gateway does: any valid token can read (`GET`), only `Administrator` can create, update or delete, and `User` is read-only.

   AuthenticationService signs tokens with `Jwt:Key`. For `dotnet run` it comes from `appsettings.Development.json`; in the cluster it comes from the Secret. The service fails at startup if the key is missing or shorter than 32 characters. Access tokens live 15 minutes (`Jwt:AccessTokenMinutes`); `POST /api/Account/refresh` rotates the 7-day refresh token (`Jwt:RefreshTokenDays`) and `POST /api/Account/logout` revokes it. Administrators manage accounts at `/api/Users`; any user changes their own password with `PUT /api/Account/password`. Logs go to the console, plus `logs/` files when running locally.

### Deploy with Helm

The Helm chart in `deploy/helm/sava` deploys every service, RabbitMQ and, optionally, MySQL. Pick an environment with a values file:

| File | Target | Environment | MySQL | Secrets | API exposure |
|---|---|---|---|---|---|
| `values-dev.yaml` | Docker Desktop Kubernetes (localhost) | `Development` (sample data, Swagger UI) | In-cluster StatefulSet | Generated by the chart | `http://localhost:80` (LoadBalancer) |
| `values-prod.yaml` | Hosted Kubernetes cluster | `Production` (no seeding) | In-cluster StatefulSet | Pre-created Secret | Ingress with TLS |

Every service exposes `/healthz` (liveness) and `/readyz` (readiness; checks the database for Customer, Inventory and Order), which the chart uses for its probes. `appsettings.json` only holds environment-neutral defaults; the chart passes hosts and credentials as environment variables. Only one release fits in a namespace, because `ocelot.json` routes to fixed service names.

#### Development (localhost)

Requires Docker Desktop with Kubernetes enabled and [Helm](https://helm.sh/docs/intro/install/).

`deploy/dev.sh` runs all of the steps below. It builds the images, installs or upgrades the release, restarts the rebuilt services and waits until they're ready:

```bash
deploy/dev.sh                  # build and deploy everything
deploy/dev.sh order customer   # rebuild and redeploy only these services
deploy/dev.sh --reset          # also wipe MySQL and RabbitMQ, so the databases are re-seeded
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

4. To remove everything, run `helm uninstall sava -n sava`. The MySQL and RabbitMQ volumes survive an uninstall; delete them with `kubectl delete pvc data-mysql-0 data-rabbitmq-0 -n sava` to start from an empty database and broker.

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

2. Create the Secret once. Use a random JWT key of at least 32 characters, a password for the first administrator (`admin`; only used while the Users table is empty), a MySQL root password, and connection strings that use it for the in-cluster MySQL (`server=mysql`). The hex password needs no escaping in a connection string:

   ```bash
   DB_PASSWORD="$(openssl rand -hex 24)"
   kubectl create namespace sava
   kubectl create secret generic sava-secrets -n sava \
     --from-literal=jwt-key="$(openssl rand -base64 48)" \
     --from-literal=admin-password="$(openssl rand -base64 24)" \
     --from-literal=rabbitmq-user=sava \
     --from-literal=rabbitmq-password="$(openssl rand -base64 24)" \
     --from-literal=mysql-root-password="$DB_PASSWORD" \
     --from-literal=auth-db="server=mysql;port=3306;database=savadb_auth;user=root;password=$DB_PASSWORD" \
     --from-literal=customer-db="server=mysql;port=3306;database=savadb_cust;user=root;password=$DB_PASSWORD" \
     --from-literal=inventory-db="server=mysql;port=3306;database=savadb_inv;user=root;password=$DB_PASSWORD" \
     --from-literal=order-db="server=mysql;port=3306;database=savadb_order;user=root;password=$DB_PASSWORD" \
     --from-literal=backup-writer-key-id=<writer key id> \
     --from-literal=backup-writer-key=<writer key> \
     --from-literal=backup-reader-key-id=<reader key id> \
     --from-literal=backup-reader-key=<reader key>
   ```

   The databases are created and migrated on startup. MySQL keeps its data on a 10Gi PersistentVolumeClaim (`data-mysql-0`) from the cluster's default StorageClass; set `mysql.storageClassName` or `mysql.storage` to change it. Helm keeps the claim on uninstall. The root password is set only when the volume is first initialized, so keep `mysql-root-password` and the connection strings unchanged afterwards.

   The backup keys come from the bucket setup in [Backups](#backups). To add keys to an existing Secret, use `kubectl patch secret sava-secrets -n sava --type merge -p '{"stringData":{"<key>":"<value>"}}'`.

3. Install or upgrade:

   ```bash
   helm upgrade --install sava deploy/helm/sava -f deploy/helm/sava/values-prod.yaml -n sava \
     --set image.tag=$TAG --set ingress.host=<your domain>
   ```

   Roll back with `helm rollback sava -n sava`. RabbitMQ keeps its queues and undelivered messages on its own 1Gi PersistentVolumeClaim (`data-rabbitmq-0`; `rabbitmq.storage`, `rabbitmq.storageClassName`), so a restart loses nothing. The exchange and queues come from the chart (`templates/rabbitmq.yaml`) and are imported when the broker starts.

#### Backups

`values-prod.yaml` backs up the in-cluster MySQL to an S3-compatible bucket from another provider, so losing the cluster or the hosting account doesn't take the backups with it. Each run dumps the four schemas with `mysqldump --single-transaction`, which doesn't lock tables, into one gzipped file per schema in a folder named after the UTC time, for example `daily/2026-11-13T000012Z/savadb_order.sql.gz`.

| Job | When (Europe/Sofia) | Writes to | Kept for |
|---|---|---|---|
| `mysql-backup` CronJob | Daily at 02:00 | `daily/`; also `monthly/` on the 1st and `yearly/` on 1 January | 30 days, 365 days, 10 years (invoices fall under the Accountancy Act) |
| `mysql-backup-pre-deploy` Helm hook | Before every `helm upgrade`; a failed dump stops the upgrade | `pre-deploy/` | 30 days |
| `mysql-backup-verify` CronJob | Sundays at 04:00 | Nothing | Restores the newest daily dump into a throwaway MySQL. Fails if the dump is older than 26 hours, a restore fails, or a schema has no tables or migration history |

A restore can lose up to a day of data (everything since the last nightly or pre-deploy dump). The jobs only upload: the bucket deletes expired dumps and locks them against earlier deletion, and the writer key has no delete permission where the provider allows it. Nothing alerts on failures yet, so check `kubectl get jobs -n sava`. The dumps aren't encrypted by the jobs; both providers below encrypt stored data. Keep the Secret's values (MySQL root password, JWT key, bucket keys) in a password manager, not with the backups.

**Set up the bucket** once, with expiry and lock periods matching the table: `daily/` and `pre-deploy/` 30 days, `monthly/` 365 days, `yearly/` 3650 days.

- **Backblaze B2**
  1. Create a private bucket with Object Lock enabled.
  2. Add a lifecycle rule per prefix that hides files after the period and deletes hidden files after 1 day.
  3. Create two application keys restricted to the bucket (`b2 key create --bucket <bucket> <name> <capabilities>`): the writer with `listBuckets,writeFiles,writeFileRetentions`, the reader with `listBuckets,listFiles,readFiles`.
  4. In `values-prod.yaml`, set `backup.s3.endpoint` to `https://s3.<region>.backblazeb2.com`, `region` to the bucket's region (e.g. `eu-central-003`) and `bucket`. `backup.objectLock.mode: COMPLIANCE` makes every upload locked for its prefix's period (`backup.objectLock.retention`), and nobody, including the account owner, can delete it earlier. Use `GOVERNANCE` while testing if you may need to remove test files.
- **Cloudflare R2**
  1. Create a bucket.
  2. Under its settings, add an object lifecycle rule per prefix that deletes objects after the period, and a bucket lock rule per prefix with the same period.
  3. Create two R2 API tokens scoped to the bucket: **Object Read & Write** for the writer and **Object Read only** for the reader. R2 tokens can't exclude deletion; the bucket lock rules prevent it.
  4. In `values-prod.yaml`, set `backup.s3.provider: Cloudflare`, `endpoint: https://<account id>.r2.cloudflarestorage.com`, `region: auto`, the `bucket`, and `backup.objectLock.mode: ""` (R2 locks by bucket rule, not per upload).

Then add the key ids and secrets to `sava-secrets` (`backup-writer-key-id`, `backup-writer-key`, `backup-reader-key-id`, `backup-reader-key`) and upgrade the release.

**Run a job by hand**, for example before a risky manual change, and read its output:

```bash
kubectl create job -n sava --from=cronjob/mysql-backup backup-manual-$(date +%s)
kubectl create job -n sava --from=cronjob/mysql-backup-verify verify-manual-$(date +%s)
kubectl logs -n sava job/<job name> --all-containers
```

**Restore.** Restore all four schemas from the same folder: the services refer to each other's data (orders hold customer and batch IDs, Inventory's stock ledger counts orders), so mixing points in time leaves them out of step. Restore a single schema only to undo damage in that schema alone.

1. Configure [rclone](https://rclone.org/install/) locally with the reader key, then pick and download a dump:

   ```bash
   rclone config create backup s3 provider=<Other|Cloudflare> endpoint=<endpoint> region=<region> \
     access_key_id=<reader key id> secret_access_key=<reader key>
   rclone lsf backup:<bucket>/daily/            # also monthly/, yearly/, pre-deploy/
   rclone copy backup:<bucket>/daily/<folder> ./restore
   ```

2. If the cluster is gone, install the chart first (same Secret values from the password manager) and wait for `mysql-0` to be ready.
3. Stop the services so nothing writes during the restore:

   ```bash
   kubectl scale deploy -n sava --replicas=0 authenticationservice-depl customerservice-depl inventoryservice-depl orderservice-depl
   ```

4. Replace each schema with its dump. Dropping it first also removes tables added after the dump was taken:

   ```bash
   for f in restore/*.sql.gz; do
     db=$(basename "$f" .sql.gz)
     kubectl exec -n sava mysql-0 -- sh -c "mysql -uroot -p\"\$MYSQL_ROOT_PASSWORD\" -e 'DROP DATABASE IF EXISTS \`$db\`'"
     gunzip -c "$f" | kubectl exec -i -n sava mysql-0 -- sh -c 'mysql -uroot -p"$MYSQL_ROOT_PASSWORD"'
   done
   ```

5. Start the services again (`--replicas=1`). They apply any migrations newer than the dump on startup.

## Usage

1. Register and authenticate users via the Authentication Service.
2. Use the API Gateway to interact with inventory, order, and customer services.
3. Monitor logs and events using Serilog and RabbitMQ.


