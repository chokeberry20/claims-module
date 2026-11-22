# Claims Module (FNOL + Reserve Management): Plan

_Status: approved plan, revision 4 (2026-11-22). Implementation has not started._
_Related: [DECISIONS.md](DECISIONS.md) (D-01…D-50), [ai-log.md](ai-log.md)._
_Section numbering follows the approved plan: §7 (ambiguities and contradictions) is in DECISIONS.md, and §9 (AI log) is in ai-log.md._

`files/` has two documents:
- **Brief**: `DICEUS_Fullstack_Technical_Assessment.docx`. It sets the mandated stack, Clean Architecture layout, deliverables and evaluation weights (Architecture 35 / AI workflow 25 / Delivery 25 / Domain 15).
- **Spec**: `Claims_Module_Candidate_Specification.docx` (FRS v1.0). It holds the business rules, entities, API, UI, jobs and conventions.

The repo is empty: only these two files, and no git yet. This is a greenfield build. This planning step produces the analysis and the plan. **No code is written yet.**

**Precedence rule (D-00).** The spec wins on business rules. The brief wins on stack, architecture, deliverables and deployment. Additions from the brief are adopted. Real conflicts are resolved in DECISIONS.md.

## 1. Functional requirements (with citations)

S = Spec section or rule ID. B = Brief section.

### 1.1 FNOL intake
| ID | Requirement | Source |
|---|---|---|
| FR-01 | Create a claim in one API call: claim, loss event, parties, risk objects and an optional initial reserve, all in one transaction. Returns 201 with the ClaimNumber | S5.2, S10.1, B3.3.1 |
| FR-02 | ClaimNumber is `CLM-{YYYY}-{0000000}`. It is unique per organisation, generated atomically, has no gaps, and is never reused (soft-deleted claims keep their number) | S5.3, BR-C-04, B BR-C-04 |
| FR-03 | Loss date must not be in the future (Critical) | BR-C-01, S8 |
| FR-04 | Loss date outside the policy period is a Warning. The claim is still created, the warning is audited, and it must be acknowledged before Open | BR-C-02, S8, B BR-C-02 |
| FR-05 | At least one Claimant is needed before Open (Critical, blocks the transition) | BR-C-03, BR-P-01, CC-03 |
| FR-06 | The cause-of-loss code must exist and be active for the organisation (Critical) | BR-C-05, S8, S5.6 |
| FR-07 | No linked policy is a Warning, and reserves stay blocked until a policy is linked | BR-C-06 (spec), S5.2 step 1, S8 |
| FR-08 | Loss description is required and at least 20 characters (Critical) | BR-C-07, S8 |
| FR-09 | No risk objects is a Warning | S5.4 |
| FR-10 | Validation also runs on demand through a validate endpoint. Results are Critical or Warning | S5.4 |
| FR-11 | Party roles: Claimant, Insured, ThirdParty, Witness, Attorney. Duplicates are allowed. Party type is Person or Company | BR-P-02, S9.3 |
| FR-12 | Risk object asset types: Vehicle, Property, Person, Equipment, Other | S5.2, S9.4 |
| FR-13 | Seed the 5 policies and 10 cause-of-loss codes | S5.5, S5.6, S15.4 |
| FR-14 | Policy search by number or client name. Coverage lookup per policy | S10.3, B3.3.2 (`GET /api/policies/{id}/coverage`) |

### 1.2 Claim lifecycle
| ID | Requirement | Source |
|---|---|---|
| FR-20 | Seven statuses with a fixed transition table. Any other transition returns 422 listing the valid next statuses | S4.1, S4.2, BR-ST-01 |
| FR-21 | Draft→Open needs: no unresolved or unwaived Criticals (D-07), all Warnings acknowledged, at least one Claimant, and a handler assigned | S4.1, S4.2, BR-ST-02 |
| FR-22 | →PendingPayment needs at least one approved reserve transaction | S4.2 |
| FR-23 | →Closed needs CC-01…CC-03, otherwise 422 with the blocking list. CC-04: open reserve balances return a warning and need a justification note | S4.3, BR-ST-03 |
| FR-24 | Closed→Reopened needs a supervisor (or above) and a reason, then moves straight to Open | S4.2, BR-ST-04 |
| FR-25 | →Withdrawn needs a reason. Allowed only from Open or UnderInvestigation | S4.2 |
| FR-26 | No direct Draft→Closed | B BR-C-06 |
| FR-27 | Transitions are stored in a `ClaimStatusTransitions` table with a RequiredPermission per row. GET claim-statuses returns the valid next statuses | B3.2, B3.3.4, S10.3 |

### 1.3 Parties
| ID | Requirement | Source |
|---|---|---|
| FR-30 | Add a party. Soft-remove a party (IsActive=false). Removing the last active Claimant returns 422 | S10.1, BR-P-01 |

### 1.4 Reserves
| ID | Requirement | Source |
|---|---|---|
| FR-40 | Components: Indemnity, Expense, ALAE, SubrogationRecoverable. A claim can have several at once | S6.2 |
| FR-41 | Amount must be > 0, except SubrogationRecoverable, which may be negative | BR-R-01, S8 |
| FR-42 | Authority is checked per transaction: ≤10k auto-approved; ≤100k needs Supervisor or Manager; >100k needs Manager | S6.3, BR-R-02, B BR-R-02..04 |
| FR-43 | No self-approval (422 "Self-approval is not permitted.") | S6.3, BR-R-03 |
| FR-44 | Reject needs a reason. Re-submitting creates a new record and the rejected one stays in history | S6.4, BR-R-04, B BR-R-06 |
| FR-45 | A PendingApproval transaction can't be changed. The submitter can retract it (→Cancelled) | S6.4, S10.2 |
| FR-46 | If the total approved reserves on a claim would go over $10M: raise a Warning, and require a Manager override flag on the claim before approval | BR-R-05, B BR-R-07, B A.3 |
| FR-47 | Reserves are event-sourced: INSERT a new transaction, never UPDATE an amount. Balance = sum of approved transactions. History keeps previous/new balance, reason, user, time, approval reference and posting status | S6.6, S9.6 |
| FR-48 | Endpoints: open/adjust (POST, plus PUT adjust from the brief), list, approve, reject, retract | S10.2, B3.3.3 |

### 1.5 Background jobs
| ID | Requirement | Source |
|---|---|---|
| FR-50 | `PostGLReserveChangeJob` is enqueued on approval (auto or manual) with idempotency key `Reserve:{ReserveId}:Change:{Seq}`. It is re-entrant, writes GL_POSTING_SIMULATED (DR/CR journal), sets PostingStatus=Posted and PostingJobId. After the last retry fails it sets Failed and writes GL_POSTING_FAILED | S6.5, S12.1, BR-R-06 |
| FR-51 | `SlaMonitoringJob` runs every 15 min (`*/15 * * * *`). It finds Draft/Open claims not updated in 48h and writes SLA_BREACH_DETECTED, at most once per 24h per claim. It does not change the status | S12.2, B3.5 |
| FR-52 | Both jobs are registered at startup | S12 |

### 1.6 Documents
| ID | Requirement | Source |
|---|---|---|
| FR-60 | Multipart upload to blob path `claim-documents/{org}/{claim}/{file}`, with the filename sanitised | BR-D-01, S13 |
| FR-61 | List and download use a SAS URL with a 1h TTL. File bytes are never sent through the API | BR-D-02, S13 |
| FR-62 | `IStorageService` has AzureBlob and LocalFileSystem implementations, chosen in appsettings | BR-D-03, S13, B3.6 |
| FR-63 | 50 MB limit. MIME allowlist: PDF, JPEG, PNG, DOCX, XLSX, TXT, CSV. Each upload writes DOCUMENT_UPLOADED | S13 |

### 1.7 Audit
| ID | Requirement | Source |
|---|---|---|
| FR-70 | `ClaimAuditLog` is append-only: no UPDATE or DELETE. All writes go through `IAuditLogService`. Timestamps are UTC. Every entry carries the CorrelationId | BR-A-01, S14.2 |
| FR-71 | 16 event types are audited (see S14.1). There is a paginated, reverse-chronological GET | BR-A-02, S14.1, S10.1 |

### 1.8 Security / roles
| ID | Requirement | Source |
|---|---|---|
| FR-80 | Three roles: handler < supervisor < manager. Each role inherits the one below. Mock auth that issues a JWT Bearer token | S3, S10, S11.4, B3.7.4 |
| FR-81 | Backend checks the role before approval. The UI hides approve/reject from handlers | S3 |
| FR-82 | Test accounts: at least one handler and one supervisor (we add a manager and a second handler) | B4.3 |

### 1.9 Frontend
| ID | Requirement | Source |
|---|---|---|
| FR-90 | Dashboard: paginated table (7 columns), colour-coded status badges, filters (status multi-select, loss date range, handler text, cause), rows that open the claim, a "Log New Claim" button, total count, page size selector, empty state | S11.1, B3.7.1 |
| FR-91 | FNOL as a 3-step reactive form: typeahead policy search, in-force badge, Unknown Policy toggle, character counter, inline party and risk forms shown as chips, real-time authority indicator, review summary, a warning confirmation dialog, and a success snackbar that opens the detail page | S11.2, B3.7.2 |
| FR-92 | Claim detail: header (copyable number, status chip, transition menu, close checklist) plus 5 tabs. Covers editable notes, party removal rules, reserve cards and history, approve/reject/retract gating, GL status with retry, document upload with progress, and a paginated audit log | S11.3, B3.7.3 |
| FR-93 | Lazy routes, mock auth with a role switcher, Bearer interceptor, Material theme with a custom palette, snackbar errors, loading states, reactive forms, works at 1280px or wider. All HTTP goes through typed services | S11.4, B3.7.4 |

### 1.10 Missing operations the spec implies (added; see §7)
- Validate claim. Acknowledge warnings.
- Update draft loss details. Link a policy.
- Assign a handler. Edit notes.
- Set the reserve limit override.
- Retry a failed GL posting.
- Pre-flight check for transitions.
- Mock login and user directory.
- Policy coverage lookup.

## 2. Non-functional requirements
- **Stack (fixed)**: .NET 9/C# 13, ASP.NET Core, MediatR 12+, EF Core 9, FluentValidation, AutoMapper, Hangfire, SQL Server 2022/Azure SQL, Azure Blob, Angular 18+, Material, Reactive Forms, Azure App Service + Static Web Apps, GitHub Actions (B2.3).
- **Architecture**: Clean Architecture in 5 named projects. Every write is a Command and every read is a Query. Domain events for significant transitions. Unit of Work. Validation in the MediatR pipeline. AutoMapper profiles in Application (B2.4, B6.1).
- **Data conventions** (S15):
  - Sequential GUID PKs with `NEWSEQUENTIALID()`, DECIMAL(19,4), DATETIMEOFFSET(7) in UTC, NVARCHAR sizes.
  - Soft delete with DeletedAt. Audit columns. OrganisationId on every business table. ROWVERSION on Claims and ClaimReserveComponents.
  - Fluent API only. Global filters for soft delete and tenant.
  - Seed data through HasData. Migrations rebuild a fresh DB. No hard-coded GUIDs in logic.
- **API**: REST/JSON, kebab-case routes, 422 with the error body from S10.4, JWT Bearer, `Idempotency-Key` honoured on all writes (S10), Swagger reachable when deployed (B4.3).
- **Consistency/concurrency**: atomic claim numbers, optimistic concurrency, job idempotency and re-entrancy, a single transaction for FNOL.
- **Observability**: structured logging, correlation IDs, consistent errors, error-handling middleware (B6.2).
- **Security**: role checks on the server, sanitised filenames, MIME allowlist, short-lived SAS, secrets in Key Vault or app settings (never in git), CORS.
- **Deployability**: public Azure URLs, a manual CI/CD pipeline (build, migrate, build FE, deploy), local setup on SQL Server Express with no Docker needed (D-42; docker-compose optional), a README that lets someone reproduce it (B3.8, B4).
- **UX**: works at 1280px+, loading and empty states, no console errors (B6.2).
- **Maintainability/testability**: interfaces around storage, clock and jobs. Unit and integration tests (not required by the brief, but they support the 35% engineering-quality score).
- **Docs**: README, ARCHITECTURE.md, AI-WORKFLOW.md and the AI chat export (B4.4–4.6).

## 3. Domain model & aggregate boundaries

| Aggregate / type | Members | Invariants owned |
|---|---|---|
| **Claim** (root, RowVer) | LossEvent (1:1, own table), ClaimParty*, ClaimRiskObject*, ClaimValidationIssue* (new) | State machine; Open/Close/Reopen/Withdraw preconditions; last-claimant guard; one primary risk object; policy link; handler assignment; `ReserveLimitOverride` flag; SLA breach marker |
| **ClaimReserveComponent** (root, RowVer) | ReserveHistory* (transactions) | Amount sign rule; authority level; no self-approval; one pending transaction per component; immutable pending; ChangeSequence is monotonic; CurrentAmount projection |
| **ClaimDocument** | — | Metadata only. Immutable after upload |
| **ClaimAuditLog** | — | Append-only log, not a DDD aggregate |
| Reference data | Policy (simulated), CauseOfLossCode, ClaimStatusTransition, ClaimNumberSequence | Read-mostly, seeded |

- **Domain services.** `ReserveAuthorityPolicy` maps an amount to an approval level and checks a role's limit. `ClaimClosurePolicy` evaluates CC-01..04. It needs facts from the reserves aggregate, which it gets as a read model.
- **Cross-aggregate rules** cover the aggregate limit, CC-01/CC-04 and PendingPayment. The command handler loads both aggregates. It also "touches" the Claim root (bumps RowVer and UpdatedAt) on every reserve command. Concurrent claim and reserve operations therefore conflict instead of racing.
- **Value objects**: `ClaimNumber`, `Money` (decimal, single currency USD), `PersonName`/`ContactInfo`, `IdempotencyKey`, `DateRange` (policy period).
- **Enumerations**, stored as strings: ClaimStatus, ClaimSeverity, PartyRole, PartyType, AssetType, ReserveComponentType, ReserveTransactionType (Add/Adjust/Reverse), ApprovalStatus (AutoApproved/PendingApproval/Approved/Rejected/Cancelled), PostingStatus (Pending/Posted/Failed/Cancelled), ValidationSeverity, AuditEventType, Role.
- **Domain events**:
  - ClaimCreated, ClaimStatusChanged, ClaimClosed, ClaimReopened, ClaimWithdrawn
  - PolicyLinked, HandlerAssigned
  - PartyAdded, PartyRemoved, ValidationIssueRaised, ValidationWarningsAcknowledged
  - ReserveTransactionSubmitted, ReserveTransactionApproved (with IsAuto), ReserveTransactionRejected, ReserveTransactionRetracted
  - ReserveLimitOverrideSet, DocumentUploaded

## 4. Architecture (fixed stack, Clean Architecture)

```
src/
  ClaimsModule.Domain          (no deps) entities, VOs, enums, domain events, domain services, exceptions
  ClaimsModule.Application     → Domain. MediatR cmds/queries/handlers, FluentValidation, DTOs, AutoMapper profiles,
                                  pipeline behaviours, abstractions (IUnitOfWork, repos, IAuditLogService, IStorageService,
                                  IJobScheduler, IClaimNumberGenerator, ICurrentUser, ICorrelationContext, IUserDirectory, TimeProvider)
  ClaimsModule.Persistence     → Application. DbContext, IEntityTypeConfiguration<T>, interceptors, migrations, seed,
                                  repositories, UnitOfWork, ClaimNumberGenerator, AuditLogService, read-side query services
  ClaimsModule.Infrastructure  → Application. Azure Blob / Local storage, Hangfire job classes + filters + IJobScheduler impl,
                                  JWT token issuer, mock user directory, correlation context
  ClaimsModule.API             → all (composition root). Controllers, middleware (exception→ProblemDetails, correlation, idempotency),
                                  auth policies, Swagger, Hangfire server/dashboard, CORS, health checks
tests/
  ClaimsModule.Domain.Tests, ClaimsModule.Application.Tests, ClaimsModule.IntegrationTests (WebApplicationFactory + a real SQL Server: a dedicated test DB on `.SQLEXPRESS01` locally, a SQL Server 2022 service container in GitHub Actions; no Testcontainers, D-42), [Stretch] ClaimsModule.ArchitectureTests (NetArchTest)
web/claims-web   Angular (standalone, lazy routes)
infra/           Bicep (App Service, SWA, Azure SQL, Storage, Key Vault, App Insights)
.github/workflows/  ci.yml (PR build+test), deploy.yml (workflow_dispatch: build, efbundle migrate, deploy API + SWA)
docker-compose.yml  [Optional, verified in CI only, or dropped] sqlserver:2022, api, web (D-42)
```

**How a command flows**
1. Controller → `ISender.Send(cmd)`.
2. Pipeline behaviours run: Logging → Validation (FluentValidation → `ValidationException`) → Authorization (role requirements declared on the command) → Idempotency (keyed by the `Idempotency-Key` header) → Transaction (UoW begins a DB transaction).
3. The handler loads aggregates through repositories and calls domain methods. Those methods raise domain events.
4. `UnitOfWork.SaveChangesAsync`:
   - **pre-commit** events are dispatched in the same transaction. These are mainly the audit handlers that call `IAuditLogService`, which makes audit entries atomic with the change.
   - The interceptors stamp audit, tenant and soft-delete columns, and block audit-log mutation.
   - Commit.
   - **post-commit** handlers run: they enqueue Hangfire jobs through `IJobScheduler`.
5. Read queries go through `IClaimsReadService` (AsNoTracking projections plus AutoMapper `ProjectTo`). Reads never touch the domain model.

**Jobs.** Job classes in Infrastructure are thin. They call `mediator.Send(SimulateGlPostingCommand)`, so all logic sits in Application and Infrastructure never references Persistence. A Hangfire `IApplyStateFilter` sends `MarkGlPostingFailedCommand` once the job reaches its final failed state.

**Frontend.**
- `core/`: auth (mock users and login), interceptors (auth, correlation, idempotency, error → snackbar), loading service.
- `shared/`: status badge, money pipe, authority indicator, confirm dialog.
- `features/`: claims-list, fnol-intake, claim-detail. Each is a lazy route group.
- Typed `*ApiService` classes generated by hand from the DTOs.
- State lives in signal-based stores held in services. No NgRx.

## 5. Modules & components

**Backend features** (Application, one folder each):
- `Claims`: Create, List, GetDetail, UpdateLossDetails, LinkPolicy, AssignHandler, UpdateNotes, Validate, AcknowledgeWarnings, TransitionStatus, GetAvailableTransitions, SetReserveLimitOverride
- `Parties`: Add, Remove
- `RiskObjects`: added at intake, plus Add/Remove
- `Reserves`: SubmitReserveTransaction (open/adjust/reverse), Approve, Reject, Retract, RetryGlPosting, GetReserves, SimulateGlPosting, MarkGlPostingFailed
- `Documents`: Upload, List (with SAS)
- `Audit`: GetAuditLog
- `Reference`: CauseOfLossCodes, ClaimStatuses
- `Policies`: Search, GetCoverage
- `Sla`: RunSlaScan
- `Auth`: mock login and a user list

**Cross-cutting**:
- `Result`/error model, `DomainRuleViolationException` (→422), `NotFoundException` (→404), `ForbiddenException` (→403), `ConcurrencyException` (→409)
- `ProblemDetails` factory in the S10.4 shape
- `ICurrentUser` built from JWT claims: sub, name, role, org
- Tenant provider

**Persistence**:
- One `IEntityTypeConfiguration` per table, plus shared conventions: base config for PK/audit/soft-delete/tenant shadow props, string enums, decimal precision.
- Interceptors: `AuditableEntityInterceptor`, `SoftDeleteInterceptor`, `TenantInterceptor`, `AppendOnlyAuditGuardInterceptor`.
- A migration that adds an INSTEAD OF UPDATE/DELETE trigger on ClaimAuditLog.
- An `IdempotencyRecords` table. A `ClaimNumberSequences` table.

**Frontend components**:
- Shell: header with user, role switcher and nav.
- `ClaimsListPage` (filter bar, table, paginator).
- `FnolIntakePage` with Step1PolicyLoss, Step2PartiesRisk and Step3ReserveReview, plus `PolicyTypeahead`, `PartyInlineForm`, `RiskObjectInlineForm`, `AuthorityIndicator`.
- `ClaimDetailPage` (header, `TransitionMenu`, `CloseChecklistDialog`) with OverviewTab, PartiesTab, ReservesTab (`ReserveSummaryCards`, `ReserveHistoryTable`, `AddReservePanel` as a Material sidenav slide-in), DocumentsTab (upload progress) and AuditLogTab.

## 6. Technical challenges (and mitigation)

1. **Gapless atomic claim numbers.** A SQL SEQUENCE can leave gaps, which contradicts "no gaps".
   - Use a `ClaimNumberSequences(OrganisationId, Year)` table with `UPDATE … SET Next=Next+1 OUTPUT inserted.Next` (MERGE HOLDLOCK for the first row of a year), run **inside the FNOL transaction**.
   - A rollback undoes the increment. The row lock serialises concurrent creates.
   - An integration test runs 50 parallel POSTs and expects distinct, consecutive numbers.
2. **Two-phase validation.** The spec says claims are created with Criticals and stay in Draft (S5.4). It also asks for FluentValidation on commands and 422 responses (S8, S10.4). Resolved in D-05.
3. **Audit atomicity versus Hangfire dual-write.**
   - Audit entries are written in the same transaction through pre-commit handlers.
   - Jobs are enqueued after commit. If an enqueue is lost, the transaction stays `PostingStatus=Pending` with no job.
   - A small recurring "re-enqueue stale Pending postings" sweep covers this, inside the SLA job or a separate job.
4. **GL job idempotency under retries.** In one DB transaction, a conditional `UPDATE ReserveHistory SET PostingStatus='Posted' … WHERE Id=@id AND PostingStatus<>'Posted'` runs first. The audit insert happens only if 1 row was affected. A unique index on `IdempotencyKey` backs this up. Re-running the job is a no-op, and so is running it concurrently.
5. **Final-failure detection** relies on a Hangfire state filter. Retries are set to 3 attempts with short delays (configurable) so the demo doesn't wait hours. An optional `GlPosting:SimulatedFailureRate` lets us show the Failed state and the retry.
6. **Cross-aggregate concurrency.** The $10M aggregate limit and CC-01 span two aggregates. Touching the Claim RowVer serialises them, and a `DbUpdateConcurrencyException` becomes a 409.
7. **Tenant and soft-delete filters with background jobs.** Jobs have no HTTP user. They get a system `ICurrentUser` and an explicit org scope, or use `IgnoreQueryFilters` with an org predicate in the SLA scan.
8. **NEWSEQUENTIALID versus GUIDs created in the domain.** Domain events need IDs before save. Domain code generates SQL-Server-ordered sequential GUIDs (`Guid.CreateVersion7` sorts badly in SQL Server). The DB default is still `NEWSEQUENTIALID()` to satisfy the convention.
9. **Append-only enforcement.** This is enforced in three places: the EF interceptor throws on Modified/Deleted, an INSTEAD OF trigger sits in the migration, and there is no update API.
10. **Event-sourced reserves.** The spec says "INSERT, don't UPDATE", but history rows carry approval and posting columns that change. Amounts and balances stay immutable. Only lifecycle columns change (D-22). `CurrentAmount` is a projection kept up to date in the same transaction.
11. **SAS.** Start with an account-key SAS from the connection string. *Stretch:* a User Delegation SAS for managed identity. Local fallback: a plain `/uploads/...` URL, with HMAC signing as a *stretch* (D-30).
12. **Upload safety.** Sanitise the filename (strip path separators, `..` and control characters). Check the allowlist against extension and declared MIME type. Enforce the 50 MB limit at Kestrel and with `RequestSizeLimit`. *Stretch:* magic-byte sniffing.
12a. **Hangfire dashboard auth** without a Bearer header: Basic auth with credentials from config or Key Vault. The browser needs a correct `WWW-Authenticate` challenge to prompt (D-41).
13. **Licensing.** AutoMapper ≥15 and MediatR ≥13 are commercial. Pin **AutoMapper 14.x** and **MediatR 12.x**, the last OSS releases.
14. **.NET 9 support ended about 2026-11-10** (STS). The stack is mandated, so we keep .NET 9. We record the risk and the upgrade path to .NET 10 LTS.
15. **Azure free tiers.**
    - App Service F1 has no Always On, so Hangfire recurring jobs stop while the app sleeps. Use B1.
    - Azure SQL uses the **Basic tier** (fixed low cost). Serverless was rejected: Hangfire's 15s polling stops auto-pause, so it costs more. Basic's 5 DTU is enough for the demo. Hangfire `QueuePollInterval` is raised to 30s to reduce load.
    - Pipeline migrations need a SQL firewall rule for the runner IP. Add and remove it with az CLI over GitHub OIDC.
16. **Date semantics.** The policy has DATE fields while the loss has a DATETIMEOFFSET in UTC. Compare UTC dates with inclusive bounds. The "future" check allows 5 min of clock skew (D-15).
17. **Stale seed data.** POL-2024-001002 expired 2026-05-31, which is before today. Policy status is computed from dates, and extra in-force policies are seeded so the demo stays stable (D-34).
18. **Angular 18+ "feature modules" versus standalone.** Use standalone components with lazy `loadChildren` route groups. They behave like lazy feature modules (D-37).

## 8. Implementation plan (small, verifiable tasks)

Each task ends with a **Verify** check. Commit after each task (git init in P0).

**P0 – Bootstrap**
1. `git init`, `.gitignore` (**including `files/`**: the brief is CONFIDENTIAL and the .docx files must never be committed), `.editorconfig`, `Directory.Build.props` (net9.0, nullable, TreatWarningsAsErrors, LangVersion 13), `Directory.Packages.props` with pinned versions. *Verify:* `dotnet --version` shows 9.x.
2. Solution plus the 5 src and 4 test projects, with references following the dependency rule. *Verify:* `dotnet build` passes.
3. **[Stretch]** Architecture tests (NetArchTest): Domain has no references, Application doesn't reference Persistence or Infrastructure, Infrastructure doesn't reference Persistence. *Verify:* `dotnet test` passes. The baseline relies on project references to enforce the dependency rule.
4. **Local environment (D-42, replaces docker-compose).** `appsettings.Development.json` in the API gets `ConnectionStrings:ClaimsDb = Server=.SQLEXPRESS01;Database=ClaimsModule;Trusted_Connection=True;TrustServerCertificate=True` (Windows auth, so no secret) and `Storage:Provider = LocalFileSystem`. The test settings point to `ClaimsModule_Tests`. *Verify:* `sqlcmd -S .SQLEXPRESS01 -E -C -Q "SELECT @@VERSION"` connects. The database itself is created by the migrations in P2.

**P1 – Domain** (TDD)
5. Building blocks: `Entity`, `AggregateRoot` (domain events), `IDomainEvent`, `SequentialGuid`, `DomainRuleViolationException`, `Money`, enums. *Verify:* unit tests.
6. `Claim` aggregate: create, parties (add/remove with the last-claimant guard), risk objects (one primary), link policy, assign handler, notes, validation-issue sync. *Verify:* tests for BR-P-01/02 and FR-12.
7. Claim state machine: transition table, the Open/PendingPayment/Close/Reopen/Withdraw preconditions, and the reason requirements. *Verify:* a parameterised test covers all 49 from→to pairs (valid set = S4.2), plus each CC-01..04.
8. `ClaimValidator` domain service (state rules from D-05): outside the period, no policy, no claimant, no risk objects. *Verify:* tests that include the expired-policy seed case.
9. `ClaimReserveComponent` aggregate: Add/Adjust/Reverse, sign rule, ChangeSequence, idempotency key, one pending, retract, approve/reject with the self-approval check and authority check, CurrentAmount projection. *Verify:* tests at the thresholds 10,000 / 10,000.01 / 100,000 / 100,000.01, plus self-approval and subrogation negatives.
10. `ReserveAuthorityPolicy` and the aggregate-limit check (D-19). *Verify:* tests.

**P2 – Persistence**
11. `ClaimsDbContext`, base conventions (PK default `NEWSEQUENTIALID()`, decimal(19,4), DATETIMEOFFSET, strings for enums, NVARCHAR sizes, shadow OrganisationId/UserCreated/UserModified/DeletedAt), and global soft-delete and tenant filters. *Verify:* a model snapshot test.
12. Entity configurations for all tables: Claims, LossEvents, ClaimParties, ClaimRiskObjects, ClaimValidationIssues, ClaimReserveComponents, ReserveHistory (unique IdempotencyKey, unique (Component, Sequence)), ClaimDocuments, ClaimAuditLog, CauseOfLossCodes, Policies, ClaimStatusTransitions, ClaimNumberSequences, IdempotencyRecords. RowVer goes on Claims and Components. *Verify:* `dotnet ef migrations add Initial`, then review the generated SQL types.
13. HasData seed: organisation constant from config-bound seed values, 8 policies, 10 cause codes, transition rows. *Verify:* `dotnet ef database update` on a fresh container, then count the rows. The Initial migration also sets `ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 160` (SQL Server 2022 behaviour on the local 2025 Express instance; also valid on Azure SQL). This runs via `migrationBuilder.Sql(..., suppressTransaction: true)` because ALTER DATABASE can't run inside a transaction (D-42). *Verify (also):* `SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()` returns 160.
14. Interceptors (auditable, soft delete, tenant, append-only guard) and the audit-log trigger migration. *Verify:* integration test where an UPDATE on the audit log throws, at both the EF and the SQL level.
15. `ClaimNumberGenerator` (MERGE/UPDATE OUTPUT). *Verify:* 50 parallel generations inside transactions give consecutive, distinct numbers. A rollback leaves no gap.
16. Repositories, `UnitOfWork` (transaction, pre/post-commit event dispatch), `AuditLogService`, read services. *Verify:* integration tests.
17. Export the idempotent SQL script to `db/scripts`. *Verify:* the file exists and runs on an empty DB.

**P2.5 – Walking skeleton on Azure** (moved forward from P7; you asked for this)
17a. Minimal API host: `Program.cs` with Serilog, Swagger, `/health` (with a DB check), and the DbContext registered. No business endpoints yet. *Verify:* it runs locally against the migrated DB, and `/swagger` and `/health` respond.
17b. Empty Angular shell: scaffold the latest 18+, Material with a custom palette, toolbar and router outlet, `environment.apiBaseUrl`, and a `/health` ping shown in the footer. This is P6 task 36 moved forward. *Verify:* `ng build` passes.
17c. Bicep (`infra/main.bicep`):
    - App Service B1 (Linux, .NET 9, Always On, system-assigned managed identity)
    - Static Web App (Free)
    - Azure SQL Database, **Basic tier** (5 DTU, 2 GB; cheapest fixed cost, no auto-pause)
    - Storage account with a `claim-documents` container
    - Key Vault (SQL and storage connection strings, JWT key) with Key Vault references in app settings
    - App Insights
    - CORS set to the SWA origin.
    *Verify:* `az deployment group what-if`, then deploy.
17d. `ci.yml` (PR or push: `dotnet build/test`, `ng build`) and `deploy.yml` (workflow_dispatch, GitHub OIDC). Deploy steps:
    1. Build and publish the API.
    2. Build `efbundle`.
    3. Add a SQL firewall rule for the runner IP.
    4. Run the bundle.
    5. Remove the firewall rule.
    6. `azure/webapps-deploy`.
    7. Run the SWA deploy action with the Angular dist.
    *Verify:* a green run. Public Swagger shows up, the SWA loads and its footer shows the API healthy, and Azure SQL has the seed rows.
17e. Add a smoke-test script `scripts/smoke.sh` (curl `/health`, `/api/reference/cause-of-loss-codes` once it exists). Run it as the last step of `deploy.yml`.

**Incremental deployment rule:** every phase from P3 onward ends with a **"Deploy & smoke"** task. Run `deploy.yml`, run the smoke script, extend it with that phase's endpoints or pages, and record the result in ai-log.

**P3 – Application**
18. Pipeline behaviours (Logging, Validation, Authorization, Idempotency, Transaction), the exception model, `ICurrentUser`/`ICorrelationContext`/`TimeProvider`. *Verify:* behaviour unit tests.
19. Reference and Policy queries plus AutoMapper profiles. Add `AssertConfigurationIsValid` as a test. *Verify:* tests.
20. `CreateClaimCommand`, its validator (all S8 claim rows) and the handler (number, children, initial reserve, issues, events). *Verify:* handler tests and an integration test for the transactional rollback when the reserve fails.
21. List and detail queries (filters, paging, totalReserves, search). *Verify:* integration tests with seeded claims.
22. Claim maintenance commands: loss-details, policy, assignment, notes, validate, acknowledge warnings, **waive critical issue (supervisor+, reason required, VALIDATION_ISSUE_WAIVED)**. *Verify:* tests. A handler waive gets 403. An empty reason gets 422. A waived issue is not re-raised on re-validation. Waiving `NO_CLAIMANT` gets 422 (non-waivable).
23. `TransitionClaimStatusCommand` plus `GetAvailableTransitionsQuery`. *Verify:* tests for each S4.2 row and the 422 bodies.
24. Party and risk-object commands. *Verify:* the last-claimant 422.
25. Reserve commands and queries: submit, approve, reject, retract, retry, get, override. *Verify:* tests for each BR-R rule.
26. `SimulateGlPostingCommand` and `MarkGlPostingFailedCommand`, idempotent. *Verify:* running twice gives exactly 1 audit row, and so do two concurrent runs.
27. `RunSlaScanCommand` (with the 24h dedupe) and the stale-posting re-enqueue sweep. *Verify:* tests using a fake TimeProvider.
28. Domain-event → audit handlers for every S14.1 event. *Verify:* a test asserts that each command emits the expected event types.
28a. Deploy & smoke. API endpoints arrive in P5, so for now this checks that migrations for any new tables apply on Azure.

**P4 – Infrastructure**
29. `LocalFileSystemStorageService` (plain `/uploads/...` URL) and `AzureBlobStorageService` (**account-key SAS**, 1h, read-only, single blob). Provider switch, filename sanitiser, extension and MIME allowlist, 50 MB limit. *Verify:* unit tests, plus a LocalFileSystem integration test. The AzureBlob SAS (downloads the file, 1h expiry) is checked against the real storage account in the post-deploy smoke test (D-42).
29s. **[Stretch]** Magic-byte sniffing, HMAC-signed expiring local URLs, and a User Delegation SAS via managed identity.
30. Hangfire: `IJobScheduler` implementation, `PostGLReserveChangeJob`, `SlaMonitoringJob`, the final-failure state filter, retry config, simulated failure rate. *Verify:* an integration test with an in-memory Hangfire setup.
31. JWT issuer, demo user directory, correlation context, and `BasicAuthDashboardFilter` (D-41). *Verify:* tests. Missing or wrong credentials get 401 with `WWW-Authenticate`. Correct credentials pass.
31a. Deploy & smoke. Check that the storage connection string and the Hangfire dashboard password from Key Vault resolve on App Service.

**P5 – API**
32. Program composition: auth (JWT Bearer plus role policies), CORS, Swagger with Bearer, Hangfire server, `RecurringJob.AddOrUpdate("sla", "*/15 * * * *")`. Then the dashboard at `/hangfire` behind Basic auth, mapped only when credentials are configured (D-41). *Verify:* the app starts on a migrated DB and `/swagger` loads. Anonymous `/hangfire` makes the browser prompt. The configured credentials open the dashboard.
33. Middleware: correlation ID, exception → S10.4 ProblemDetails (422/404/403/409/500), idempotency replay. *Verify:* integration tests on the error shapes and on replaying the same Idempotency-Key.
34. Controllers: claims, parties, risk-objects, reserves, documents, audit, reference, policies, auth. *Verify:* a WebApplicationFactory end-to-end (test DB on SQLEXPRESS01 locally, SQL 2022 service container in CI) scenario: login → FNOL → reserve 50k by handler1 → handler1 approve gives 403 → supervisor approve → job runs → audit shows GL_POSTING_SIMULATED → upload → close checklist → close → reopen.
35. A `.http` file with demo requests. *Verify:* run it manually.
35a. Deploy & smoke. Run the P5 end-to-end scenario against the **Azure** URL with the `.http` file or the smoke script.

**P6a – Minimal demo-flow UI** (covers the brief §7.2 live demo: FNOL → reserve → approval → upload → audit; you asked for this)
36. *(Scaffold done in 17b.)* Core: models and typed API services, mock auth with login, role switcher (handler1, handler2, supervisor1, manager1), auth/error/loading interceptors, snackbar service. *Verify:* unit tests for the interceptors. Switching roles changes the header.
37. Shared: status badge (7 colours), money pipe, authority indicator, confirm dialog. *Verify:* component tests (authority boundaries 10,000 / 100,000).
38. Claims list, minimal: table with the 7 columns, paginator, status filter, row navigation, "Log New Claim". *Verify:* manual run against the local API.
39. FNOL 3-step form, functional:
    - Step 1: typeahead, loss fields, unknown-policy toggle, char counter.
    - Step 2: party and risk inline forms shown as chips, with the claimant required.
    - Step 3: reserve and authority indicator, review summary, warning confirm dialog.
    - Submit with an Idempotency-Key. Server errors show inline. Success shows a snackbar, then navigates.
    *Verify:* create a claim manually end to end.
40. Claim detail, minimal: header with the transitions menu and reason dialog, plus three tabs:
    - **Reserves:** cards, history, add panel, approve/reject/retract gated by role and submitter, GL badge, refresh.
    - **Documents:** upload with progress, download through the SAS URL in a new tab.
    - **Audit Log:** paginated.
    *Verify:* the full demo script with role switching works locally.
40a. Deploy & smoke. Run the full brief §7.2 demo script on the **Azure** URLs and confirm the browser console has no errors.

**P7 – Local DevOps extras** (most of the Azure and CI/CD work moved to P2.5)
41. **[Optional]** Dockerfiles and `docker-compose.yml` (SQL 2022, API, web), checked only by a CI job (`docker compose build` plus a health check), since Docker isn't used locally (D-42). Drop this if time is short.
42. Harden the pipeline: cache NuGet and npm, publish test results, set environment protection on `deploy.yml`. *Verify:* a green run.

**P8 – Docs & demo**
43. README: prerequisites, local setup, config keys, migrations, Azure, walkthrough, test credentials, how to reach the Hangfire dashboard.
44. ARCHITECTURE.md (structure diagram, data model, CQRS flow, events, jobs, Azure, tradeoffs, linked to DECISIONS). AI-WORKFLOW.md built from ai-log.
45. Final check: run the brief §7.2 demo script on Azure, run `dotnet test` and `ng test`, and confirm Swagger schemas.

**P6b – Frontend completeness and polish** (after P8)
46. Dashboard: full filter bar (date range, handler text, cause), total count, page size selector, empty state.
47. FNOL polish: in-force badge, date-time cap, per-step error banners, risk advisory.
48. Detail polish: Overview tab (editable notes, severity, estimated amount), Parties tab (add/remove, last-claimant disabled), close pre-flight checklist from `/transitions`, waive-critical dialog (supervisor+; NO_CLAIMANT has no waive button), GL retry button, copyable claim number, skeleton loaders.
49. Deploy & smoke.

**Stretch backlog:** NetArchTest (3), magic bytes, HMAC local URLs, User Delegation SAS (29s), the stale-posting re-enqueue sweep (from 27 if time is short), risk-object edit endpoints in the UI.

**Priority if time runs short** (B8.3, adjusted to your review):
1. P0–P2, then the P2.5 walking skeleton. It stays deployed from here on.
2. P3–P5, deployed after each phase.
3. **P6a, the minimal demo-flow UI.** The live demo needs it.
4. P8 docs.
5. P7 local extras.
6. P6b polish.
7. Stretch.

## 10. Decided without clarification
Business ambiguities that could have gone to the recruiter (B8.4) were decided by you as the engineer instead. The full rationale is in DECISIONS.md.

| Decision | Outcome | Why |
|---|---|---|
| D-07 Waiving Critical issues | Supervisor+ can waive, with a mandatory reason and VALIDATION_ISSUE_WAIVED. NO_CLAIMANT can't be waived. Warnings must be acknowledged before Open | S4.2 "(or all waived)". BR-C-03 and BR-ST-02(b) need a Claimant independently. BR-C-02 requires acknowledgement |
| D-16 Adjust semantics | Add = initial (>0). Adjust = target (>0, any sign for Subrogation). Reverse = 0. Server stores the signed delta. Authority uses \|delta\| | BR-R-01 stays literally true. Decreases are possible (S11.3). Event-sourcing is intact. Large decreases are material too |
| D-19 Aggregate limit | Gross: Indemnity + Expense + ALAE, Subrogation excluded. Checked at submission and re-checked at approval | Recoveries are uncertain, so this is the conservative choice. The re-check covers approvals that happen while this one is pending. The deviation from the literal wording is documented |
| D-41 Hangfire dashboard | Basic auth, credentials from config or Key Vault | A browser can't send a Bearer header when navigating. Basic auth is simple and secure enough over HTTPS |

