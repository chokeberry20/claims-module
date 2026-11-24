# Handoff: Claims Module (DICEUS assessment)

_Written 2026-11-24 at the end of P5, before compacting the session. It is for continuing without the chat history. The detailed record is in [PLAN.md](PLAN.md) (phases and tasks), [DECISIONS.md](DECISIONS.md) (D-00…D-72) and [ai-log.md](ai-log.md) (every session, run ID, mistake and correction). If this file and those disagree, they win; fix this file._

## 1. Where things stand

| Phase | State | Branch / last commit | Evidence |
|---|---|---|---|
| P0 Bootstrap | done, reviewed | `p0-bootstrap` | ai-log |
| P1 Domain | done, reviewed | `p1-domain` | 318 domain tests |
| P2 Persistence | done, reviewed | `p2-persistence` | ai-log |
| P2.5 Walking skeleton on Azure | done, reviewed | `p2_5-walking-skeleton` | first green deploy |
| P3 Application | done, reviewed | `p3-application` | deploy 45233506838, smoke 7/7 |
| P4 Infrastructure | done, reviewed | `p4-infrastructure` (`921601d`) | deploy 45267640714, smoke 9/9, 7/7 Key Vault refs |
| P5 API | done, **accepted 2026-11-24** | `p5-api` = `main` (`a3a7237`) | CI 45308154094; deploy 45308552280, smoke 13/13; `.http` flow 25/25 on Azure |
| **P6a Minimal demo-flow UI** | **next, not started** | (new branch `p6a-demo-ui`) | |
| P8 Docs & demo, P7 local extras, P6b polish, Stretch | not started | | order: P6a → P8 → P7 → P6b → Stretch |

Tests at `a3a7237`: domain 318, application 44, integration 326, Angular 13. Build 0 warnings, 0 errors (TreatWarningsAsErrors is on).

Open items carried forward:
- **The test-run hang is unexplained** (ai-log P5). Two telemetry-enabled test hosts in one process sometimes hung. The workaround: a single telemetry host test, with a 120 s timeout. The user's decision: don't install `dotnet-stack` unless the hang comes back (installing it was denied once by the permission check).
- **A demo claim exists in Azure:** `CLM-2026-0000001`, created by the 35a `.http` run.
- The Live Metrics service is called from Azure (intended), never from tests (`Telemetry:EnableLiveMetrics=false` in the test factory).

## 2. Next: P6a (minimal demo-flow UI)

Goal: the brief §7.2 live demo in the browser: FNOL → reserve → approval → upload → audit. Tasks come from PLAN.md P6a. Read that section before starting.

1. **Branch:** `git checkout main && git checkout -b p6a-demo-ui`.
2. **Task 36, core:**
   - TypeScript models and typed API services for the P5 endpoints. Shapes: Swagger at `/swagger/v1/swagger.json`, or `src/ClaimsModule.API/Contracts/Requests.cs` plus the Application DTOs. Enums travel as **names**.
   - **Mock auth:** `POST /api/auth/token {userName, password}` returns `{accessToken, tokenType, expiresAt, userId, displayName, role}`. `GET /api/auth/users` (anonymous) lists the 4 demo users for the role switcher (handler1, handler2, supervisor1, manager1). `GET /api/auth/me`.
   - **Interceptors:**
     - auth: Bearer token on every API call;
     - error: snackbar with the S10.4 body's `title`, `errors` and `codes`. Field-less errors use the key `General`; 400 means `MalformedRequest`; 401, 403, 404, 409, 422 and 429 share one shape with a `correlationId`;
     - loading: a spinner or skeleton for every async call (B3.7.4);
     - idempotency: an `Idempotency-Key` (a GUID per submit, reused on retry) on writes. The server accepts 1–200 visible ASCII characters.
   - *Verify:* interceptor unit tests; switching roles changes the header.
   - How the demo passwords reach the UI is a **user decision** (a sign-in form, or prefilled locally). Ask; don't commit any password.
3. **Task 37, shared components:** status badge (7 colours), money pipe, authority indicator (auto ≤ 10,000; supervisor ≤ 100,000; manager above, BR-R-02..04), confirm dialog. *Verify:* component tests at the 10,000 and 100,000 boundaries.
4. **Task 38, claims list:** 7-column table, paginator, status filter, row navigation, "Log New Claim". API: `GET /api/claims?status=&dateFrom=&dateTo=&assignedHandlerId=&assignedHandler=&causeOfLossCode=&policyId=&search=&sortBy=&descending=&page=&pageSize=` returns `{items, page, pageSize, totalCount}`.
5. **Task 39, FNOL form:** a 3-step reactive form.
   - The policy typeahead uses `GET /api/policies/search?q=`.
   - The **reserve body uses `component`** (not `componentType`). Reject takes `rejectionReason`.
   - A 201 returns `{claimId, claimNumber, status, validationIssues, initialReserve}`.
6. **Task 40, claim detail:**
   - Header and transitions: `GET /api/claims/{id}/transitions` returns `targetStatus`, `canProceed`, `blockingConditions` and `confirmationsRequired`. `PUT /status {targetStatus, reason?, closureJustification?, acknowledgeWarnings?}`.
   - Reserves tab: approve, reject and retract are gated by role and submitter. The GL badge shows `postingStatus` (Pending → Posted).
   - Documents tab: multipart `file`, `documentType`, `notes`. The `downloadUrl` is a 1 h SAS link to Blob storage in Azure, or `/uploads/...` locally.
   - Audit tab: `GET /api/claims/{id}/audit?page=&pageSize=&eventType=`.
7. **Task 40a, deploy & smoke** (approval needed): CI, then deploy (`deploy_infra=false` unless Bicep changed). Run the §7.2 script on the Azure URLs, and check that the browser console shows no errors. The smoke test already checks `index.html`, `app-config.json` and CORS.
8. Stop after P6a for the user's review.

Frontend facts:
- Angular **22.3** with standalone components and lazy routes (D-37), Material with a custom palette, and Vitest via `ng test`. It lives in `web/claims-web`.
- Existing pieces: `core/config/app-config.ts` (runtime `public/app-config.json` with `apiBaseUrl`, D-61), `core/health/health.service.ts`, `layout/api-status`, `features/home`.
- Local API: `http://localhost:5180`. CORS allows `http://localhost:4200` in Development.

## 3. Working rules (the user's, standing)

- **Phases:** work through them in order. **Stop after each phase** for the user's review, and don't start the next phase until told to.
- **Commit after each task.** Immediately before each commit, re-run `dotnet build` and `dotnet test` (plus `ng test` when the frontend changed), and report **this run's** real output, never an earlier one.
- **Approval:** every outward-facing action needs explicit approval for that phase: push, deploy, CI runs that deploy, any Azure write (role, secret, setting), fast-forwarding `main`. Approval doesn't carry over. Read-only Azure queries are fine. When a phase reaches its deploy task, prepare everything and ask.
- **Decisions:** the user decides business and architecture questions. Log the user's decisions as *theirs*, and log AI mistakes and corrections honestly in ai-log.md (it feeds AI-WORKFLOW.md and the chat export). Record decisions as `D-nn` rows in DECISIONS.md, plus "Deliberate deviations" entries where the spec isn't followed literally.
- **Confidentiality:**
  - `files/` (the brief and spec .docx) are **confidential** and must never be committed (`.gitignore` covers them). Extract their text only into the session scratchpad, never into the repo.
  - **No secrets in the repo.** No personal email anywhere. Commits use the GitHub no-reply identity from the global git config.
  - Secret values never appear in command text or output: generate or read each one inside the same command into a variable. On an RBAC 403, wait and retry; don't change roles.
- **Commit messages:** a conventional prefix (`feat(p5-32..34): …`, `fix(…)`, `docs(…)`), a body, and the attribution line `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Pass them with `git commit -F <file>`, never `-m` with quotes in PowerShell 5.1.
- **Branches:** one per phase (`pN-name`). `main` only moves by fast-forward after the user accepts a phase.

## 4. Architecture conventions (summary; details in DECISIONS)

- **Layers:** Clean Architecture: Domain ← Application ← Persistence / Infrastructure ← API. **Infrastructure never references Persistence.** Job classes are thin MediatR senders.
- **MediatR pipeline:** Logging → Authorization (`[RequiresRole]`, `[SystemRequest]`) → Validation (FluentValidation, a fresh context per validator) → Transaction (commands; the EF execution strategy; one save at the end) → Idempotency (commands; the result is stored in the same transaction, D-66) → handler.
- **Domain events:** `BeforeCommit<T>` writes the audit entry (atomic with the change); `AfterCommit<T>` enqueues jobs.
- **Roles are checked only in the pipeline** (D-72). ASP.NET has a fallback policy requiring an authenticated user. Anonymous: `/api/auth/token`, `/api/auth/users`, `/health`, `/health/live`, Swagger, `/hangfire` (Basic auth, D-41).
- **Error mapping:**

  | Status | Cause |
  |---|---|
  | 400 | unparsable JSON (`MalformedJsonFilter`; the user's P5 decision) |
  | 401 | no or invalid token |
  | 403 | role, or a domain forbid |
  | 404 | not found |
  | 409 | concurrency, or an idempotency conflict |
  | 422 | validation, rules, type or enum mismatch |
  | 429 | sign-in rate limit |
  | 500 | anything else; no details in the body |

  The body is S10.4 plus `codes`, `detail` and `correlationId`, as `application/problem+json`. ASP.NET's own `ExceptionHandlerMiddleware` log category is silenced (Fatal) so that handled 4xx aren't logged as errors.
- **Optimistic concurrency:** any change inside an aggregate bumps its root's RowVer (D-68).
- **AutoMapper:** 14.0.0 with three advisories suppressed (D-67). Keep maps flat; reads use ProjectTo.
- **Telemetry:** Azure Monitor OpenTelemetry, only when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set. Job runs get an activity (`ClaimsModule.Jobs`). Parentless SQL is dropped (D-71).
- **Startup:** the warm-up is a hosted service (D-65, D-71).

## 5. Local environment

- Windows 10. **No Docker.** SQL Server Express `.\SQLEXPRESS01`, compatibility level 160. Dev DB `ClaimsModule`, test DB `ClaimsModule_Tests`.
- .NET SDK 9.0.321 is pinned by `global.json` (SDK 10 is also installed). Node 26 with Angular 22.3. `dotnet-ef` 10.0.15 is a global tool. No Python.
- **The console output is Russian:** test summaries read `Пройден!` / `Не пройден!` and build summaries `Ошибок` / `Предупреждений`. Match those strings, not the English ones.
- Tools: `az` = `C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd`, `gh` = `C:\Program Files\GitHub CLI\gh.exe`, Git Bash = `C:\Program Files\Git\bin\bash.exe` (runs `scripts/smoke.sh`).
- **Run the API locally:**
  1. Run `scripts/set-dev-secrets.ps1` once (user secrets: a random JWT key and one demo password for all four users).
  2. Then `dotnet run --project src/ClaimsModule.API`.

  For a throwaway run, pass `Jwt__SigningKey` and `DemoUserPasswords__<user>` as environment variables of that process only. Use `ASPNETCORE_URLS=http://localhost:5180`; there is no launch profile named `http`.
- **Demo flow without an IDE:** `scripts/run-http.ps1 -File src/ClaimsModule.API/ClaimsModule.API.http [-BaseUrl …]`, with `CLAIMS_HANDLER1_PASSWORD` and `CLAIMS_SUPERVISOR1_PASSWORD` set in the **same** command. Environment variables don't survive between tool calls.
- **Tests:** the integration tests use `ClaimsApiFactory` (Testing environment, local storage in a temp folder, Hangfire server off unless enabled, test JWT key, passwords `{user}-test-password`, sign-in limit 1000, Live Metrics off). Run with `--blame-hang-timeout 200s --blame-hang-dump-type none` to catch hangs.

## 6. Azure and GitHub

| What | Name / URL |
|---|---|
| GitHub repo | `chokeberry20/claims-module` (default branch `main`) |
| Workflows | `ci.yml` (on push to any branch and on PRs to main: .NET with a SQL Server 2022 service container, Angular, Bicep lint); `deploy.yml` (`workflow_dispatch`, input `deploy_infra`, environment `production`, GitHub OIDC) |
| Deploy command | `gh workflow run deploy.yml --ref <branch> -f deploy_infra=true\|false`, then `gh run watch <id> --exit-status` |
| Subscription / RG | resource group `rg-claims-assessment`, name prefix `claims7316` |
| Region | `swedencentral`. The Static Web App is in `eastus2` (westeurope wasn't accepting new customers) |
| API (App Service B1, Linux) | `claims7316-api-obyl3n`: https://claims7316-api-obyl3n.azurewebsites.net (`/swagger`, `/health`, `/health/live`, `/hangfire`) |
| Frontend (Static Web App) | `claims7316-web`: https://kind-forest-09a79310f.8.azurestaticapps.net |
| Azure SQL (Basic, Entra-only, D-60) | server `claims7316-sql-obyl3n`, database `ClaimsModule` |
| Storage | `claims7316stobyl3nugvcpk`, container `claim-documents` |
| Key Vault (RBAC) | `claims7316-kv-obyl3nugvc`. Secrets: `JwtSigningKey`, `HangfireDashboardPassword`, `DemoUserPassword-{handler1,handler2,supervisor1,manager1}`, `StorageConnectionString`. Each has exactly **one enabled version**: the P4 all-zero versions are disabled |
| Monitoring | Log Analytics `claims7316-logs` (1 GB/day cap), App Insights `claims7316-appi` |
| Pipeline identity | `claims7316-github-oidc`: Contributor, plus an RBAC administrator role restricted to granting Key Vault Secrets User (D-63). Federated subjects use the numeric-ID format (`repo:chokeberry20@92028683/claims-module@1468194031:environment:production` and `…:pull_request`) |
| Role assignments on the vault | the API's managed identity has Secrets User; the user has Secrets Officer (vault scope only) |

Deploy pipeline steps:
1. Bicep (only with `deploy_infra=true`).
2. Open a temporary SQL firewall rule for the runner.
3. EF bundle migrations.
4. The grant script, run with go-sqlcmd v1.10.0 (SHA-256 verified, `-b`).
5. Close the firewall rule.
6. Deploy the API: `WEBSITE_RUN_FROM_PACKAGE=1`; the commit SHA is in the version, `1.0.0+<sha>`.
7. Deploy the Static Web App and write its `app-config.json`.
8. Check that every Key Vault reference shows Resolved (7 or more).
9. Run `scripts/smoke.sh <api> <web> <sha>`. It waits until `/health/live` reports the SHA.

## 7. Gotchas (each cost time at least once)

- **Windows PowerShell 5.1 (.NET Framework):**
  - Missing APIs: there is no `RandomNumberGenerator.Fill` (use `RandomNumberGenerator.Create().GetBytes()`). Without `$ErrorActionPreference='Stop'` a failed call continues: in P4 this wrote six all-zero secrets.
  - No `&&`, `??` or ternaries.
  - Native arguments containing `"` get split, so use `git commit -F file`. `"…`"…"` inside PowerShell strings turns into bare `"` in written files (it broke a C# file in P5).
  - `Set-Content` without `-Encoding` writes ANSI; write files with `[IO.File]::WriteAllText(path, text, (New-Object Text.UTF8Encoding $false))`.
  - Backticks are PowerShell's escape character: a Markdown backtick inside `"…"` disappears, so use `''` single-quoted strings.
  - The range `$a[$i..($n-1)]` counts **backwards** when `$i -ge $n`.
  - Files in the repo use **LF**. Match edit anchors with `` `n ``, not `` `r`n ``. The scratchpad helper `edit.ps1` (Edit-File: exact, unique, line-ending aware) is lost with the session; recreate it if needed.
- **Backslash escaping:** the Bash tool collapses `\\`, and Bash heredocs and the Edit tool turn `\uXXXX` into the character. `.\SQLEXPRESS01` in PLAN.md lost its backslash twice. Write anything containing backslashes with the **Write tool** (or a script file written with it), then check the raw bytes (`od -c` or Select-String).
- **KQL:** `last` and `first` are reserved words. Never use them as column names (`lastSeen` / `firstSeen` instead). A query that doesn't parse returns an error, not "no rows"; read the error before waiting. `Type` is the table-name column; use `DependencyType`. Run queries with `az rest` against `https://api.loganalytics.io/v1/workspaces/<customerId>/query`; the `log-analytics` az extension isn't installed, and its install prompt fails without a terminal.
- **Runner sign-in:** `DefaultAzureCredential` tries managed identity first. A GitHub runner on AWS answered the metadata address with 401 and broke migrations (deploy 45306958443). The database tools are therefore pinned to the az login session: `AZURE_TOKEN_CREDENTIALS=dev` for the EF bundle (needs Azure.Identity ≥ 1.14, which is why Persistence references 1.21.0) and `ActiveDirectoryAzCli` for go-sqlcmd. Don't revert either.
- **ASP.NET:**
  - The authorization fallback policy also applies to requests with **no endpoint**, so middleware such as the Hangfire dashboard must come **before** `UseAuthorization`.
  - `[Produces("application/json")]` on controllers overrides an `ObjectResult`'s content type, so errors go through `ApiErrorResult`.
  - The exception handler middleware logs handled exceptions as errors; its category is silenced.
- **Azure Monitor exporter:**
  - A log line written before the host starts is never exported, so logging during start-up belongs in a hosted service.
  - The exporter stores failed batches on disk per instrumentation key and resends them later, so tests use a fresh key.
  - The rate-limited sampler skips the first request after start-up.
  - The SqlClient instrumentation is vendored in the distro: activity source `Azure.Monitor.OpenTelemetry.AspNetCore`, operation `….Execute`.
- **Hangfire:** filters are process-global, so the final-failure filter is one instance attached to the latest host. Its own tables in schema `HangFire` use cascades, so schema tests are scoped to `dbo`.
- **Deploy timing:** right after a deploy the old process can still answer. The smoke test waits for the SHA in `/health/live`; never test before that.

## 8. Documents and where to update them

- **PLAN.md:** each phase has a `*Status …*` line under its tasks, and the header line summarises them. Update both when a phase moves. Check them against the real runs and commits.
- **DECISIONS.md:** add a `| D-nn |` row after the last one, plus "Deliberate deviations" bullets where needed.
- **ai-log.md:** append a section per task or phase. It records the user's approvals and decisions (attributed to the user), run IDs, real results, and the AI's mistakes in plain words.
- **This file:** update it at the end of each phase, or before a compaction.
