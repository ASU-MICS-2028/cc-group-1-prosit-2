# Backend

ASP.NET Core 10 (C#) API for HealthConnect Ghana. Owned by Bernard (API) and Liza (database, sync, analytics). The stack follows the team's Prosit 1 backend: services are class libraries behind a thin API host.

What the frontend expects is in [`../docs/frontend-needs.md`](../docs/frontend-needs.md) and [`../docs/TEAM_HANDBOOK.md`](../docs/TEAM_HANDBOOK.md). In short: JSON under `/api`, bearer tokens, `GET /health` returning 200, `PUT /api/{patients|vitals|appointments}/{id}` with ids made on the phone, and port **8080** inside the container (nginx in the frontend image forwards `/api` to the `backend` service; `/api/health` becomes `/health`).

```
APIs/healthconnect-api/    thin host: Program.cs, logging, errors, OpenAPI, MapFeatures()
Services/
  PlatformService/         health: the pattern every service follows
    Features/              one class per use case
    Models/                request and response types
    PlatformServiceExtension.cs   AddPlatformService()
  Libs/SharedLibrary/      IFeature and what every service shares
  Libs/Data/               the PostgreSQL connection: password from Secrets Manager, TLS verify-full, health check
tests/
  Shared/                  [UnitTest] / [IntegrationTest] traits, a real PostgreSQL (Testcontainers)
  <Service>.Tests/         unit tests, mirror the service folders
  Api.Tests/               the whole API in memory
../docs/openapi/healthconnect.json   generated on build and committed: the contract the frontend reads
```

## Add a feature

A feature is one class. It maps its route and its handler takes what it needs as parameters, so a test can call the handler directly.

```csharp
public sealed class GetThing : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/things/{id:guid}", Handle).WithName("GetThing").WithTags("Things");

    public static async Task<Ok<ThingDto>> Handle(Guid id, ...) => ...
}
```

Register it in the service's `AddXService()` with `.AddFeature<GetThing>()`.

## Run it

Needs the .NET 10 SDK and Docker.

```bash
cd backend
docker compose -f docker-compose.dev.yml up -d db          # PostgreSQL on localhost:5433
dotnet run --project APIs/healthconnect-api --launch-profile http   # http://localhost:8000
```

Check it: <http://localhost:8000/health> returns 200 with `"database": "Healthy"`; <http://localhost:8000/swagger> lets you try every endpoint (Development only).

Or run everything in containers: `docker compose -f docker-compose.dev.yml --profile api up --build`. Copy `.env.example` to `.env` to change the compose settings.

## How it finds the database

| Where | How |
|---|---|
| Laptop and tests | One connection string, `ConnectionStrings:Default` (set in `appsettings.Development.json`). |
| The server | The deploy gives the container `DB_HOST`, `DB_NAME`, `DB_SECRET_ARN`, `DB_SSL_CA` and `AWS_REGION`. The API reads the password from AWS Secrets Manager with the server's own role (no password in any file), connects with TLS and checks the database's certificate (`sslmode=verify-full`), and re-reads the password every 30 minutes so a rotation needs no restart. |

`GET /health` is 200 only when the API can really reach the database (it opens a connection and runs `select 1`, giving up after 4 seconds) **and** every migration has been applied. It says nothing about why it failed; the reason is in the log.

## Sign-in

Phone number plus SMS code, then a 7-day token. The endpoints, rules, who-may-call-what table, the demo workers and their fixed demo code (`123456`, only for the five synthetic accounts, switch off with `Demo__Enabled=false`) are in [docs/auth.md](../docs/auth.md). To use it from the app locally: `VITE_AUTH_MODE=api npm run dev` in `frontend/`.

## The database and migrations

The tables, roles, row-level security and the audit log are defined by plain SQL migrations in `Services/Libs/Data/Migrations/Sql/`, which EF Core runs (it keeps the list of what was applied). They are SQL rather than EF models because row-level security, triggers, roles and the audit hash chain cannot be written in EF's model. The API applies pending migrations when it starts, so a deploy needs no extra step, and `/health` stays red until they are done (so a failed migration rolls the release back).

- The diagram, the FHIR mapping, who can do what and how to add a migration: [docs/database](../docs/database/README.md).
- List them (from `backend/`): `dotnet tool restore`, then `dotnet ef migrations list --project Services/Libs/Data --startup-project APIs/healthconnect-api --configuration Release`. (Use `--configuration`; in `dotnet ef`, `-c` means "context".)
- Load the sample data into a local database: see [db/README.md](../db/README.md).
- The database tests run every rule as each of the four database roles, including as a login that is not a superuser, like RDS's.

## Check it (same as CI)

```bash
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --no-restore -c Release
dotnet test --no-restore --no-build -c Release -p:CollectCoverage=true -p:CoverletOutputFormat=cobertura -p:CoverletOutput=./coverage/ -p:Threshold=70 -p:ThresholdType=line -p:ThresholdStat=total
```

Only the fast tests (no Docker): `dotnet test --filter Category=Unit`. Integration tests start a PostgreSQL container, so Docker must be running (the first run pulls the image).

## Conventions

- NuGet versions live only in `Directory.Packages.props`; shared compiler settings in `Directory.Build.props` (warnings are errors, nullable on). Assemblies and namespaces are `HealthConnect.<folder name>`.
- Tests: xUnit 2.9.3, NSubstitute for fakes. Name tests as sentences (`Is_unhealthy_when_the_database_is_not_configured`).
- Logs are structured JSON outside Development and never contain personal data or secrets (ids only).
- A service never references another service. Shared code goes in SharedLibrary.
- Change the database only through migrations, additive and never edited once merged (see [docs/database](../docs/database/README.md)).
- Synthetic data only, never real patient data (see [CONTRIBUTING.md](../CONTRIBUTING.md)).
