# Claims Module: FNOL and Reserve Management

A vertical slice of an insurance claims system: **first notice of loss (FNOL) intake**, the **claim lifecycle**, **reserve management with an approval workflow**, simulated **general-ledger posting** in background jobs, **documents** in Azure Blob Storage, and an append-only **audit log**.

- **Backend:** .NET 9, ASP.NET Core, Clean Architecture, MediatR 12 (CQRS), FluentValidation, AutoMapper 14, EF Core 9 on SQL Server / Azure SQL, Hangfire.
- **Frontend:** Angular 22 (standalone components, lazy routes), Angular Material, reactive forms.
- **Azure:** App Service, Static Web Apps, Azure SQL, Blob Storage, Key Vault, Application Insights. CI/CD with GitHub Actions and Bicep.

| Document | What it covers |
|---|---|
| [ARCHITECTURE.md](ARCHITECTURE.md) | Solution structure, data model, CQRS flow, domain events, jobs, Azure, key decisions |
| [AI-WORKFLOW.md](AI-WORKFLOW.md) | How the work was done with Claude, prompts, what the AI got wrong and how it was corrected |
| [docs/DECISIONS.md](docs/DECISIONS.md) | Every ambiguity, contradiction and design choice (D-00…D-76), with the rationale |
| [docs/PLAN.md](docs/PLAN.md) | Requirements traceability (FR → spec section), phases and status |
| [docs/ai-log.md](docs/ai-log.md) | The chronological log of the AI-assisted work, including every mistake and correction |

---

## Deployed application

| | URL |
|---|---|
| Frontend | https://kind-forest-09a79310f.8.azurestaticapps.net |
| API | https://claims7316-api-obyl3n.azurewebsites.net |
| Swagger UI | https://claims7316-api-obyl3n.azurewebsites.net/swagger |
| Health | https://claims7316-api-obyl3n.azurewebsites.net/health (database, migrations, storage) |
| Hangfire dashboard | https://claims7316-api-obyl3n.azurewebsites.net/hangfire (HTTP Basic, user `admin`) |

### Test credentials

| User | Role | What it can do |
|---|---|---|
| `handler1` (Hannah Handler) | Handler | FNOL, claim changes, reserve submissions, uploads; can't approve reserves or reopen claims |
| `handler2` (Harry Handler) | Handler | Same as handler1 (useful to show "only the submitter can retract") |
| `supervisor1` (Sam Supervisor) | Supervisor | Everything a handler can, plus approving reserves up to $100,000, reopening claims, waiving Critical issues |
| `manager1` (Mia Manager) | Manager | Everything, approvals above $100,000, the $10M reserve-limit override |

**The passwords are not in this repository** (decision D-73). They are delivered with the submission. In Azure they live in Key Vault (`DemoUserPassword-<user>`), and the Hangfire dashboard password is the `HangfireDashboardPassword` secret.

In the UI you sign in by picking a user and typing the password. The role switcher in the header signs you in as another user. A user you already signed in as in the same tab is switched to without a password. Tokens are held in memory only, so a page reload signs you out.

---

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | **9.0.x** | `global.json` pins 9.0.321 (roll-forward to the latest 9.0 feature band) |
| SQL Server | 2022 or later (Express is fine) | The database runs at compatibility level 160 (SQL Server 2022), which a migration sets |
| Node.js | 22.22+, 24.15+ or 26+ (Angular 22's engines; developed with 26) | With npm |
| Angular CLI | 22 (installed locally by `npm ci`) | Use `npx ng …` |
| `dotnet-ef` | 9.0 or later | `dotnet tool install -g dotnet-ef`, for migrations |
| Azure CLI, GitHub CLI | recent | Only to deploy |

Docker is **not** required. The integration tests use a real SQL Server database (a local one, or a SQL Server 2022 service container in CI).

## Local development

### 1. Database

The development connection string in `src/ClaimsModule.API/appsettings.Development.json` points at a local SQL Server Express instance with Windows authentication:

```
Server=.\SQLEXPRESS01;Database=ClaimsModule;Trusted_Connection=True;TrustServerCertificate=True
```

To use another server, set `ConnectionStrings__ClaimsDb` in the environment (or in user secrets), for example `Server=localhost;Database=ClaimsModule;User Id=sa;Password=…;TrustServerCertificate=True`.

Create the database, the schema and the reference data:

```bash
# ConnectionStrings__ClaimsDb is used if set; otherwise the local default above
dotnet ef database update --project src/ClaimsModule.Persistence --startup-project src/ClaimsModule.Persistence
```

The migrations create the schema, seed the reference data (10 active cause-of-loss codes plus an inactive one, 8 simulated policies, the claim status transition table), set compatibility level 160 and add the trigger that makes the audit log append-only. Seeding is done with EF Core `HasData`, so a fresh database is complete after `database update`.

Without the EF tools, run the generated **idempotent SQL script** instead: `db/scripts/migrate-and-seed.sql`. See [db/scripts/README.md](db/scripts/README.md) (use `sqlcmd -I`).

### 2. API secrets (local only)

The API needs a JWT signing key and the demo user passwords. Locally these are .NET user secrets, stored in your profile and never in the repository:

```powershell
./scripts/set-dev-secrets.ps1     # random JWT key; asks for one password used by all four demo users
```

### 3. Run the API

```bash
dotnet run --project src/ClaimsModule.API --no-launch-profile --urls http://localhost:5180
```

- Swagger: http://localhost:5180/swagger
- Health: http://localhost:5180/health
- Hangfire dashboard: http://localhost:5180/hangfire (in Development, local requests are allowed without a password)
- Uploaded documents are stored under `src/ClaimsModule.API/uploads` and served at `/uploads/...` (the local storage provider).

### 4. Run the frontend

```bash
cd web/claims-web
npm ci
npx ng serve            # http://localhost:4200
```

The frontend reads the API address at start-up from `public/app-config.json` (`{"apiBaseUrl": "http://localhost:5180"}`), so the same build runs locally and in Azure. The API allows `http://localhost:4200` through CORS in Development.

### 5. Tests

```bash
dotnet test                                  # domain 318, application 44, integration 327
cd web/claims-web && npx ng test --watch=false   # Angular 123
```

The integration tests run against the database `ClaimsModule_Tests` (`tests/ClaimsModule.IntegrationTests/testsettings.json`; override with `ConnectionStrings__ClaimsDb`). They create and migrate it themselves, and include an end-to-end HTTP scenario with a real Hangfire server.

### 6. Try the API without the UI

`src/ClaimsModule.API/ClaimsModule.API.http` walks through the whole demo flow (sign-in, FNOL, reserve, 403 for the handler, supervisor approval, GL posting, upload, close, reopen). Use it from Visual Studio, Rider or VS Code (REST Client), or from PowerShell:

```powershell
$env:CLAIMS_HANDLER1_PASSWORD = '…'; $env:CLAIMS_SUPERVISOR1_PASSWORD = '…'
./scripts/run-http.ps1 -File src/ClaimsModule.API/ClaimsModule.API.http
```

## Configuration

All keys can be set in `appsettings*.json`, as environment variables (`Section__Key`) or as user secrets. **Secrets** (marked 🔒) never go in a file in the repository: user secrets locally, Key Vault references in Azure.

| Key | Example | Purpose |
|---|---|---|
| `ConnectionStrings:ClaimsDb` | `Server=.\SQLEXPRESS01;Database=ClaimsModule;Trusted_Connection=True;TrustServerCertificate=True`; in Azure `Server=tcp:<server>.database.windows.net;Database=ClaimsModule;Authentication=Active Directory Managed Identity` | Application and Hangfire database |
| `Tenancy:OrganisationId` | `3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64` | The organisation this deployment serves (tenant filter, D-01) |
| `Jwt:SigningKey` 🔒 | 48 random bytes, base64 | HS256 key for the demo tokens (at least 32 bytes) |
| `Jwt:Issuer`, `Jwt:Audience`, `Jwt:Lifetime` | `claims-module`, `claims-module-api`, `08:00:00` | Token settings |
| `DemoUsers:Users` | see `appsettings.json` | Demo identities (ids, names, roles); no passwords |
| `DemoUserPasswords:<user>` 🔒 | `DemoUserPasswords:handler1` | Each demo user's password |
| `RateLimits:SignInPerMinute` | `10` | Sign-in attempts per client address per minute |
| `Cors:AllowedOrigins` | `["http://localhost:4200"]` | Frontend origins |
| `Storage:Provider` | `AzureBlob` or `LocalFileSystem` | Document storage (LocalFileSystem is refused in Production) |
| `Storage:AzureBlob:ConnectionString` 🔒 | `DefaultEndpointsProtocol=https;AccountName=…;AccountKey=…` | Needs an account key, to sign the SAS links |
| `Storage:AzureBlob:ContainerName` | `claim-documents` | Blob container |
| `Storage:LocalFileSystem:RootPath`, `…:PublicBaseUrl` | `uploads`, `http://localhost:5180/uploads` | Local storage folder and its URL |
| `Storage:ReadLinkLifetime` | `01:00:00` | Lifetime of a document download link |
| `Hangfire:ServerEnabled` | `true` | Run the job server in this process |
| `Hangfire:RetryAttempts`, `Hangfire:RetryDelaysSeconds` | `3`, `[10, 30, 60]` | Retries of the GL posting job |
| `Hangfire:QueuePollInterval`, `Hangfire:WorkerCount` | `00:00:05`, `2` | Job polling and workers |
| `Hangfire:Dashboard:Username` / `:Password` 🔒 | `admin` / … | Dashboard Basic auth; without a password the dashboard is not mapped (outside Development) |
| `GlPosting:SimulatedFailureRate` | `0` (0–1) | Makes the simulated GL call fail, to show retries and the Failed state |
| `GlPosting:StalePendingAfter` | `00:10:00` | When the sweep re-enqueues a posting whose job was lost |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | from App Insights | Turns on Azure Monitor OpenTelemetry |
| `Telemetry:EnableLiveMetrics` | `true` | Live Metrics (off in tests) |

Frontend: `web/claims-web/public/app-config.json` with `apiBaseUrl`. The deploy pipeline rewrites it with the API URL.

## Azure deployment

### Resources (`infra/main.bicep`)

| Resource | SKU | Role |
|---|---|---|
| App Service plan + Web App (Linux, .NET 9) | B1, Always On | The API and the Hangfire server; system-assigned managed identity |
| Static Web App | Free | The Angular app (global edge) |
| Azure SQL server + database | Basic (5 DTU) | Application data and Hangfire storage; **Entra-only authentication**; the API connects with its managed identity as a least-privilege user (data access only on the application schema, no UPDATE/DELETE on the audit log) |
| Storage account + `claim-documents` container | Standard LRS | Documents; private container, 1-hour read-only SAS links |
| Key Vault (RBAC) | Standard | JWT key, storage connection string, dashboard and demo passwords; App Service reads them through Key Vault references |
| Log Analytics + Application Insights | Pay-as-you-go (1 GB/day cap) | Requests, dependencies, logs, job activities |

See [ARCHITECTURE.md](ARCHITECTURE.md#azure-architecture) for how they interact.

### Pipelines

- **`.github/workflows/ci.yml`**, on every push and pull request: .NET build with warnings as errors, all tests against a SQL Server 2022 service container, a pending-model-changes check, the Angular build and tests, Bicep lint.
- **`.github/workflows/deploy.yml`**, run manually (`workflow_dispatch`, input `deploy_infra`), environment `production`, Azure login through GitHub OIDC (no stored Azure secrets):
  1. Build the API package, an EF Core migration bundle and the Angular app.
  2. Deploy the Bicep template (only when `deploy_infra=true`).
  3. Open a temporary SQL firewall rule for the runner, run the migration bundle, grant the API's managed identity its least-privilege database role (go-sqlcmd with `-b`, so SQL errors fail the job), close the rule.
  4. Deploy the API (run-from-package; the commit SHA is in its version) and the Static Web App, writing `app-config.json`.
  5. Check that every Key Vault reference is Resolved, then run `scripts/smoke.sh`, which waits until `/health/live` reports the deployed commit and checks 15 things (health, Swagger, 401 body, CORS, the SPA fallback and more).

### Reproducing the deployment

1. Create a resource group, an Entra group to be the SQL administrator (put yourself in it), and an Entra app registration for GitHub OIDC with federated credentials for the `production` environment and pull requests. Give the app **Contributor** on the resource group plus **Role Based Access Control Administrator** limited to granting *Key Vault Secrets User*, and add it to the SQL admin group.
2. Set the repository **variables**: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_RESOURCE_GROUP`, `NAME_PREFIX`, `SQL_ADMIN_LOGIN`, `SQL_ADMIN_OBJECT_ID`.
3. Run the deploy workflow once with `deploy_infra=true`: `gh workflow run deploy.yml -f deploy_infra=true`.
4. Give yourself *Key Vault Secrets Officer* on the vault and add the secrets `JwtSigningKey`, `HangfireDashboardPassword` and `DemoUserPassword-{handler1,handler2,supervisor1,manager1}` (the storage connection string is written by Bicep). Run the workflow again; it checks that all references resolve.

Later deploys: `gh workflow run deploy.yml --ref <branch> -f deploy_infra=false`.

## Application walkthrough

The brief's live-demo flow, all in the UI:

1. **Sign in** as `handler1`. The **claims dashboard** lists claims with claim number, policy, client, loss date, cause, a colour-coded status badge and total reserves. It filters by status (multi-select), loss date range, assigned handler (text) and cause of loss, and shows the total count with a page size selector. **Log New Claim** opens FNOL.
2. **FNOL**, three steps:
   - **Policy and loss:** policy typeahead with an in-force badge (or "Unknown policy"), loss date and time (not in the future), a searchable cause of loss, and a description with a 20-character minimum counter.
   - **Parties and risk objects:** inline forms shown as chips; a Claimant is required.
   - **Initial reserve and review:** an optional reserve with a live authority indicator (auto-approved up to $10,000, supervisor up to $100,000, manager above), and a review table.
   
   Warnings (no policy, loss outside the policy period, no risk objects) are confirmed in a dialog. The claim is created as **Draft** with the next gapless number `CLM-YYYY-0000000`.
3. **Claim detail:** a header with the status and a **Change status** menu showing only valid next statuses. Disabled options explain why, and the dialog asks for exactly what's needed: acknowledging warnings, a reason, or a closure justification. Supervisors can **waive** an open Critical issue with a reason (not the missing-Claimant one). Move the claim **Draft → Open**.
   - **Overview tab:** loss event details, severity badge, estimated amount and editable **notes**.
   - **Parties tab:** every party with role, contact and active/removed status; **add a party** inline; Remove is disabled for the last Claimant.
4. **Reserves tab:** the 50,000 initial reserve is **pending supervisor approval**. The handler sees no Approve button and can retract it. **Switch to `supervisor1`** and **approve**: the GL posting badge goes **Pending → Posted** when the background job runs (a Failed posting shows **Retry** for supervisors). **Add reserve** opens a slide-in panel (open, adjust to a new balance, or reverse) with the authority indicator.
5. **Documents tab:** upload a PDF (with progress) and **download** it through a 1-hour SAS link that opens in a new tab.
6. **Audit log tab:** every change, newest first: CLAIM_CREATED, STATUS_CHANGED, RESERVE_APPROVED, GL_POSTING_SIMULATED (with the DR/CR journal), DOCUMENT_UPLOADED and more.
7. **Close** the claim: the **close checklist** shows CC-01 to CC-04 and asks for a justification while reserves remain open, then **reopen** it as a supervisor. A handler sees Reopen disabled.

Behind the scenes: every write accepts an `Idempotency-Key`, every error uses one JSON body (S10.4) with a correlation id, an SLA job flags Draft/Open claims idle for 48 hours every 15 minutes, and a sweep re-enqueues GL postings whose job was lost.

**API-only operations** (in Swagger, not in the UI): editing loss details of a Draft, linking a policy later, reassigning the handler, changing severity, risk-object add/remove after FNOL, and the $10M reserve-limit override.

## Repository layout

```
src/
  ClaimsModule.Domain          entities, value objects, domain events, domain services (no dependencies)
  ClaimsModule.Application     commands, queries, validators, pipeline behaviours, DTOs, AutoMapper profiles
  ClaimsModule.Persistence     EF Core DbContext, configurations, migrations, seed, interceptors, Unit of Work
  ClaimsModule.Infrastructure  Blob/local storage, Hangfire jobs, JWT issuer, demo user directory
  ClaimsModule.API             controllers, error handling, auth, Swagger, Hangfire dashboard, composition root
tests/                         Domain.Tests, Application.Tests, IntegrationTests
web/claims-web                 Angular frontend
infra/                         Bicep
db/scripts                     idempotent migrate-and-seed SQL script and the database grant scripts
scripts/                       smoke test, local secrets, .http runner
docs/                          plan, decisions, AI log, handoff
```
