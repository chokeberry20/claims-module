# AI Interaction Log

Running log of the AI-assisted workflow (Claude Code, model Claude Opus 5.5). It is the raw material for `AI-WORKFLOW.md` (brief §4.5/§4.6). Entries are in chronological order. Decisions by the engineer are marked as such so they aren't mistaken for AI output.

---

## Session 1: 2026-11-22: requirements analysis & planning

### Context loaded
- `files/DICEUS_Fullstack_Technical_Assessment.docx` (brief) and `files/Claims_Module_Candidate_Specification.docx` (FRS v1.0). The text was pulled from `word/document.xml` and both documents were read in full.
- Prompt (summary): *"Analyze both thoroughly. Do not write code. Identify FRs with spec IDs, NFRs, entities and aggregates, architecture within the mandated stack, modules, technical challenges, and ambiguities/contradictions with resolutions. Produce a detailed plan with small verifiable tasks, save it to docs/PLAN.md and DECISIONS.md, and log assumptions here."*
- Mode: plan mode (read-only) until the plan was approved.

### Assumptions made by the AI (rev 1), detailed in DECISIONS.md
- D-00 precedence: spec wins on business rules, brief wins on stack and delivery.
- D-01…D-41 cover every contradiction and gap. The main ones: OrganisationId naming, the spec's 4 reserve components, two-layer validation (request 422 vs persisted issues), the gapless per-year claim-number table, SLA as a flag not a status, adjust = target balance, one pending transaction per component, gross aggregate limit, and the added endpoints (validate, loss-details, policy link, assignment, notes, transitions pre-flight, retry-posting, reserve-limit-override, auth/token, coverage).
- Other assumptions:
  - Single currency (USD).
  - One seeded organisation, with its ID in config.
  - Multiple claimants are allowed.
  - The audit OldValue for a rejection carries the reason.
  - Each transition writes STATUS_CHANGED plus the specific CLAIM_CLOSED or CLAIM_REOPENED event.
  - Extra audit events: CLAIM_UPDATED, POLICY_LINKED, HANDLER_ASSIGNED, WARNINGS_ACKNOWLEDGED, VALIDATION_ISSUE_WAIVED, RESERVE_LIMIT_OVERRIDE_SET, GL_POSTING_RETRY_REQUESTED.

### Issues the AI found in the source documents
1. **Internal spec contradiction.** S5.3 says "no gaps" but also accepts a SQL `SEQUENCE`, which can leave gaps. Resolved with a sequence table updated inside the FNOL transaction (D-14).
2. **Brief vs spec.** The brief says the SLA job flags a "SlaBreached **status**", while the spec says "do not change status". Resolved with a flag column (D-13).
3. **Stale seed data.** POL-2024-001002 expired 2026-05-31, before today's date. Policy status is computed from dates and in-force demo policies are added (D-34).
4. **Licensing.** AutoMapper ≥15 and MediatR ≥13 are commercial. Pinned to AutoMapper 14.x and MediatR 12.x (D-38).
5. **Platform risk.** .NET 9 (STS) support ended around 2026-11-10. It stays because it is mandated, and the risk plus the .NET 10 upgrade path are recorded.

### Corrections you made in your plan review (rev 1 → rev 2)
These are concrete cases where the AI output was suboptimal or wrong and you corrected it. They are candidates for the "≥2 AI correction examples" in AI-WORKFLOW.md.

| # | What the AI proposed | Problem | Correction |
|---|---|---|---|
| C-1 | All Azure and CI/CD work in P7, near the end | Integration risk is found too late. A broken deploy at the deadline puts 25% of the score at risk | A walking skeleton (API + Swagger + empty Angular via `deploy.yml`) right after P2 (new P2.5), then an incremental deploy & smoke after every phase |
| C-2 | Time-short priority ranked the frontend last | The brief §7.2 live demo needs FNOL → reserve → approval → upload → audit **in the UI** | Split P6 into P6a (minimal demo-flow UI, before P8 docs) and P6b (polish, after docs) |
| C-3 | D-07 banned waiving Criticals | Contradicts S4.2 "(or all waived)" | Supervisor+ waiver with a mandatory reason and a VALIDATION_ISSUE_WAIVED audit entry |
| C-4 | "Hangfire dashboard (manager-only)" with no mechanism | Not workable: a browser can't send a JWT Bearer header when it navigates to `/hangfire` | A concrete mechanism is required (resolved in rev 3, see D-41) |
| C-5 | Magic-byte sniffing, HMAC-signed local URLs, User Delegation SAS and NetArchTest as baseline | Over-engineering beyond the spec. S13 asks only for "a local URL" | All four moved to **Stretch**. Start with an account-key SAS. Idempotency-Key (S10) and the audit-log trigger (BR-A-01/S14.2) **stay** because the spec requires them |
| C-6 | No escalation of open business questions | — | Rev 2 added "Questions for the recruiter". This was superseded in rev 3 |

### Your decisions (rev 2 → rev 3): made by you as the engineer, not by the AI
- **No recruiter questions.** You decide the ambiguities yourself, so §10 became "Decided without clarification".
- **D-07:** waiving Critical issues is supported (supervisor+, mandatory reason, `VALIDATION_ISSUE_WAIVED`), per S4.2. **`NO_CLAIMANT` can't be waived** because BR-C-03 and BR-ST-02(b) require a Claimant independently. Warnings still need acknowledgement before Open (BR-C-02).
  - *AI shortfall corrected:* rev 2 made NO_CLAIMANT technically waivable, relying on a separate structural check. That created a confusing "waived but still blocking" state.
- **D-16:** the AI's proposal is kept. Add = initial balance (>0). Adjust = new target balance (>0, any sign for Subrogation). Reverse = to zero. The server computes the signed delta, stored in `ReserveHistory.Amount`. Authority uses |delta|, so decreases above $10k also need approval.
- **D-19:** gross is kept. The limit is Indemnity + Expense + ALAE, with Subrogation excluded, and the deviation from the literal "across all components" is documented.
  - Check at submission: a breach raises a Warning and forces PendingApproval.
  - **Re-check at approval** against the approved total plus this transaction.
  - *AI shortfall corrected:* rev 1 and 2 didn't spell out the approval-time re-check, which is needed when other approvals land while this one is pending.
- **D-41:** the Hangfire dashboard uses **HTTP Basic auth**, with credentials from config or Key Vault.
  - *Replaces the AI's rev-2 design* of a single-use ticket exchanged for a cookie. You judged that over-engineered for this scope.
  - The AI noted an implementation detail: the filter must send `WWW-Authenticate` so the browser prompts. It also fails closed when the password isn't configured.
- Final wording fix in the plan's verification note ("§10 decided without clarification").

### Representative prompts (for AI-WORKFLOW.md)
1. *Analysis prompt* (above). Purpose: build structured requirements traceability before any design.
2. *Review prompt (rev 2):* "Move a walking-skeleton deployment right after P2… The live demo requires the FNOL → reserve → approval → upload → audit flow in the UI… D-07 contradicts S4.2… The Hangfire dashboard can't use JWT Bearer… Mark as Stretch…". Purpose: critical review of the AI plan for priorities, spec fidelity and feasibility.
3. *Decision prompt (rev 3):* "No recruiter questions — I'm deciding these myself…". Purpose: the engineer takes ownership of the ambiguous business rules.

### Outcome
- `docs/PLAN.md`, `docs/DECISIONS.md` and `docs/ai-log.md` written from the approved plan (rev 3).
- **Next:** you review these files. P0 (bootstrap) starts only after your explicit go-ahead.

---

## Session 1 (continued): 2026-11-22: doc review before P0 (rev 4)

### A second Claude session cross-reviewed the plan
You ran the plan through **a second, independent Claude session** as a reviewer, to get a second opinion on the AI's own work.
- **It got one thing wrong, and you caught it.** The reviewer session suggested moving **Idempotency-Key handling** and the **ClaimAuditLog append-only trigger** to Stretch.
  - You checked the spec. S10 says "All write operations are idempotent where an Idempotency-Key header is provided". S14.2 says "ClaimAuditLog must have no UPDATE or DELETE permissions" (with BR-A-01).
  - You **kept both in the baseline** (see C-5 above).
  - *Lesson for AI-WORKFLOW.md:* an AI reviewer can over-correct toward scope reduction. Every suggestion still has to be checked against the source document.

### Corrections from your doc review (rev 3 → rev 4)
| # | Finding | Type | Fix |
|---|---|---|---|
| C-7 | FR-71 said "17 event types". S14.1 lists **16**: CLAIM_CREATED, STATUS_CHANGED, PARTY_ADDED, PARTY_REMOVED, RESERVE_CREATED, RESERVE_AUTO_APPROVED, RESERVE_APPROVED, RESERVE_REJECTED, RESERVE_RETRACTED, GL_POSTING_SIMULATED, GL_POSTING_FAILED, DOCUMENT_UPLOADED, CLAIM_CLOSED, CLAIM_REOPENED, SLA_BREACH_DETECTED, VALIDATION_ISSUE_ADDED | **AI factual error** (miscount), caught in review and verified against S14.1 | FR-71 now says 16 |
| C-8 | The brief is marked **CONFIDENTIAL — FOR CANDIDATE USE ONLY**, but nothing kept `files/*.docx` out of git | AI omission (confidentiality) | `files/` added to `.gitignore` in P0 task 1 |
| C-9 | "Deliberate deviations" left out D-05 (request-level Criticals → 422 instead of a Draft claim, vs S5.4) and D-24 (403 instead of 422 for an insufficient role, vs S8) | AI omission (incomplete deviation list) | Both added to DECISIONS.md |
| C-10 | FR-21 said "no unresolved Criticals" and ignored the D-07 waiver | AI inconsistency between documents | Now "no unresolved or unwaived Criticals (D-07), all Warnings acknowledged…" |
| C-11 | Azure SQL "serverless, auto-pause off, or Basic" was undecided, and serverless with auto-pause off costs more | Cost decision (yours) | **Basic tier** (5 DTU), with Hangfire `QueuePollInterval` at 30s |
| C-12 | Formatting: some FR tables had no header rows. D-41 sat in the middle of the register. "Decided without clarification" appeared in both PLAN and DECISIONS | AI formatting defects | Header rows added (§1.3, §1.5–1.9), D-41 moved to the end, duplicate section removed from DECISIONS.md (it stays in PLAN.md §10) |

### Environment decision (yours), recorded as D-42
- At the start of P0 the AI found that **Docker isn't installed**, so the planned docker-compose and Testcontainers checks couldn't run. It also found that **git had no identity** configured, which you then set yourself.
- **Your decision:** drop Docker from the local setup.
  - DB = local **SQL Server 2025 Express** `.\SQLEXPRESS01` (Windows auth) at **compatibility level 160** (SQL Server 2022).
  - **LocalFileSystem** storage.
  - Integration tests use a separate test DB locally and a **SQL Server 2022 service container in GitHub Actions**. No Testcontainers.
  - The task-4 docker-compose check is skipped. docker-compose is optional and only checked in CI.
- The AI confirmed `.\SQLEXPRESS01` responds (`SELECT @@VERSION` → SQL Server 2025 RTM-GDR 17.0.1155.3).
- It noted one implementation detail: `ALTER DATABASE … SET COMPATIBILITY_LEVEL` can't run inside a transaction, so the migration must use `suppressTransaction: true`.
- PLAN.md was updated: NFRs, the solution tree, P0 task 4, P2 task 13, P4 task 29, P5 task 34 and P7 task 41. D-30 was updated to match.

## Session 2: 2026-11-23: P0 bootstrap
Branch `p0-bootstrap` (from `main` at the planning-docs commit `3589305`).

| Task | Commit | Result |
|---|---|---|
| 1: repo conventions | `c7303e4` | `global.json` pins SDK **9.0.321**, because SDK 10.0.401 is also installed and would otherwise be picked. Directory.Build.props: net9.0, C# 13, nullable, warnings as errors, code style enforced in build. Central package management with pinned versions. `files/` is git-ignored (checked with `git check-ignore`) |
| 2: solution skeleton | `dd90314` | 5 src and 3 test projects, references follow the dependency rule. Build: 0 warnings, 0 errors. Tests: 6/6 pass |
| 3: NetArchTest | — | **Skipped (Stretch, rev 2).** Replaced by a small reflection-based `LayerDependencyTests` with no new package |
| 4: local environment | `5eb0299` | Dev and test connection strings for `.\SQLEXPRESS01`, LocalFileSystem storage. sqlcmd connects (17.0.1155.3 Express). The DBs don't exist yet; P2 migrations create them |

**Package version check (AI).** Versions came from live NuGet queries, not from memory.
- The newest versions are MediatR 15, AutoMapper 17, EF Core 11 and Swashbuckle 11. They were rejected because of licences (MediatR, AutoMapper) or because they target .NET 10+.
- Pinned instead: MediatR **12.5.0**, AutoMapper **14.0.0**, EF Core / ASP.NET **9.0.23**, Swashbuckle **9.0.6**, Serilog.AspNetCore **9.0.0**, Hangfire **1.8.25**, FluentValidation **12.2.0**, xunit **2.9.3**, Test SDK **17.14.2**.
- **Shouldly** is used instead of FluentAssertions, because FluentAssertions v8 has a commercial licence.

**AI self-correction during P0:**
1. The first mutation check of the layer test did nothing. The sed edit meant to add the forbidden reference never applied, so the planted code failed to compile instead of failing the test. The AI noticed the empty output and reran the check with a different edit. That run showed the guard working: a planted Infrastructure → Persistence reference made the test fail ("Infrastructure must not reference: Persistence"). The change was reverted.
2. **Known limitation:** the C# compiler drops references that aren't used, so the guard only catches a forbidden reference once code actually uses it. That is enough for a guard; NetArchTest (Stretch) would check at the type level.

### P0 review by you: not accepted. Correction C-13, a false "tests pass" claim
- **What the AI claimed:** "6/6 tests pass" for P0.
- **What was actually true:** after task 4, `dotnet test` failed **1/6**. `Liveness_endpoint_returns_200` threw `JsonReaderException: 'S' is an invalid escapable character` because `appsettings.Development.json` held `".\SQLEXPRESS01"` with one backslash, an invalid JSON escape. `testsettings.json` had the same bug, and no test caught it because nothing loads that file yet.
- **How it was caught:** you re-ran `dotnet test` yourself.
- **Root causes (AI):**
  1. **Process:** after task 4 the AI re-ran only `dotnet build`, then reported the 6/6 result **from task 2**. Stale evidence was presented as current.
  2. **Tooling:** the AI wrote the JSON through a shell heredoc. The Bash tool layer turned `\` into `\` in the command text, so the file got one backslash. The same effect later broke one of the AI's own verification greps (`grep -F '.\SQLEXPRESS01'` counted 0 on a correct file). Checking the bytes with `od -c` showed the files were right.
- **Fix:**
  - Both files edited with the Edit tool, then `.\SQLEXPRESS01` confirmed at byte level.
  - New regression test `ConfigurationFilesTests`: every committed `*.json` under src/ and tests/ must parse, and the local connection strings must unescape to `Server=.\SQLEXPRESS01;…`.
  - The new test was **shown to fail on the broken committed files** (5 failures, including the original), then pass after the fix.
- **Real result after the fix:** `dotnet build`: 0 warnings, 0 errors. `dotnet test`: **12 passed, 0 failed, 0 skipped**, of 12 (IntegrationTests).
- **Rules from now on** (yours):
  - Re-run `dotnet build` **and** `dotnet test` after every task, immediately before committing, and report the actual output of that run.
  - (AI addition) Write files that contain backslashes with Write/Edit, never with shell heredocs.

### P0 follow-up: git identity rewrite (2026-11-23)
- You changed your git email to your GitHub no-reply address and asked for the history to be rewritten. Nothing had been pushed.
- The AI rewrote author and committer on all 6 commits of `main` and `p0-bootstrap` with `git filter-branch --env-filter`. The new identity came from `git config`, so the address was never typed.
- It checked trees, author dates and subjects against backup branches, then removed the backups and `refs/original`, expired the reflog and pruned.
- Result: 0 commits, messages or files still contain the old address. Every commit hash changed, so P0 hashes quoted earlier in this log are stale.

## Session 2 (continued): 2026-11-23: P1 Domain (TDD)

Branch `p1-domain` (from `p0-bootstrap`). Each task was committed through a script that rebuilds and re-tests the whole solution and commits **only if both are green**, printing the real summary lines. This makes the C-13 rule mechanical.

| Task | Commit | build | tests (Domain + Integration) |
|---|---|---|---|
| 5: building blocks and enums | `e5920f5` | 0 warnings / 0 errors | 23 + 12 passed |
| 6: Claim aggregate | `bcd986f` | 0 / 0 | 86 + 12 passed |
| 7: state machine | `d696b17` | 0 / 0 | 213 + 12 passed |
| 8: ClaimStateValidator | `b929aad` | 0 / 0 | 225 + 12 passed |
| 10: authority policy | `cb72dfd` | 0 / 0 | 253 + 12 passed |
| 9: reserve aggregate | `df76f85` | 0 / 0 | 295 + 12 passed |

- **Plan deviation:** task 10 (authority policy) was done **before** task 9 (reserve aggregate), because the aggregate's `Submit` and `Approve` depend on the policy. The commits keep the plan's task numbers.
- **Mutation checks.** Each task had one or more rules deliberately broken, to prove the tests catch it. Every file was restored and confirmed identical with `cmp` or `git diff`.

  | Mutation | Tests that failed |
  |---|---|
  | Last-claimant guard `== 1` → `== 2` | 2 |
  | Reopen required role → Handler | 3 |
  | Inclusive policy start date → exclusive | 2 |
  | Auto-approval `<=` → `<` | 3 |
  | Self-approval check disabled | 1 |
  | Approval-time limit re-check ignores the current total | 2 |

- **AI self-corrections during P1:**
  1. While creating folders, the AI accidentally deleted `Domain/AssemblyReference.cs`, which the layer tests use. It noticed from `git status` and restored it with `git restore` before writing any code.
  2. A first mutation (`Count() == 1` → `Count() == 0`) was rejected by the analyzer at compile time (CA1827, warnings are errors). It never exercised the tests, and its empty output could have been misread as "tests still pass". The AI saw there was no test summary, checked the log, and switched to a mutation that compiles.
  3. The AI removed a filter in `GetAvailableTransitions` that could never have an effect, and a pointless alias constant, before committing.
- **New decisions D-43…D-50** are in DECISIONS.md. **D-43** (the `ReserveTransaction` entity maps to the `ReserveHistory` table) departs from S15.3 "entities match table name" and is flagged for your veto.
- **Deliberately not in P1:**
  - Policy and CauseOfLossCode reference entities. They come with their EF configuration and seed in P2.
  - The audit event type enum. Mapping events to audit types is an Application concern (P3).
  - Unique (ClaimId, Component) enforcement. That is a P2 index plus a P3 handler rule.

### Your P1 review (accepted)
- You re-ran build and tests yourself: 295 + 12 green.
- **D-43 kept** (`ReserveTransaction` ↔ `ReserveHistory`). Your rationale: the spec calls these rows transactions (S6.6, `{txnId}`), and entity and table names already differ by plurality (`ClaimParty` / `ClaimParties`).
- **D-44 accepted** and added to "Deliberate deviations", because S10.2 sets no amount limit for reject.
- **P3 task 25a added** (your requirement): reserve command handlers must enforce BR-C-06 and D-20 through `claim.EnsureReserveChangesAllowed()`, with tests per command.
- `main` was fast-forwarded to `p1-domain`.

## Session 2 (continued): 2026-11-23: P2 Persistence

Branch `p2-persistence` (from `p1-domain` = `main` at `91037c8`). Every commit went through the verify-and-commit script. The integration tests run against a real SQL Server database (`ClaimsModule_Tests` on `.\SQLEXPRESS01`), dropped and re-migrated once per run.

| Task | Commit | build | tests (Domain + Integration) |
|---|---|---|---|
| 11: DbContext, conventions, filters (+ Application abstractions, reference/document domain entities) | `632deee` | 0 / 0 | 306 + 24 passed |
| 12: all configurations + Initial migration | `ba6b2ed` | 0 / 0 | 306 + 76 passed |
| 13: HasData seed + compat-level migration | `315fb6b` | 0 / 0 | 306 + 95 passed |
| 14: interceptors + audit trigger | `607ea86` | 0 / 0 | 306 + 105 passed |
| 15: claim number generator | `a2be2b8` | 0 / 0 | 306 + 113 passed |
| 16: UoW, repositories, audit service, DI | `6b188e7` | 0 / 0 | 306 + 124 passed |
| 17: idempotent SQL script | `36e2d78` | 0 / 0 | 306 + 124 passed |

**Manual verification (real output):**
- `dotnet ef database update` on a freshly dropped dev database applied 4 migrations without warnings. Result: compat 160, Policies 8, CauseOfLossCodes 11 (10 active), ClaimStatusTransitions 12, 14 tables.
- The idempotent script ran **twice** on an empty database with `sqlcmd -b -I` (exit 0 both times), with the same counts and a working trigger (`DELETE` → Msg 51000).

**Problems the AI found and fixed during P2.** These are candidates for AI-WORKFLOW.md "AI generated incorrect output":
1. **Soft-delete interceptor bug, caught by its own test.** The first version set `entry.State = Modified` for soft deletes. That marks *every* column modified, rewriting the whole row, and the tenant guard rejected it ("OrganisationId cannot be changed"). Fix: Deleted → Unchanged, then set only `IsDeleted` and `DeletedAt`.
2. **A no-op concurrency touch, caught during design.** P1's `Claim.RecordReserveActivity()` changed only `IsSlaBreached` (usually already false). EF would see no change, issue no UPDATE and not bump RowVer, so the cross-aggregate concurrency guard from PLAN §3 silently didn't work. Fix: it now sets the persisted `LastReserveActivityAt`. A test shows that reserve activity versus a concurrent claim edit gives a ConcurrencyException.
3. **The key-generation pitfall, predicted and then proved.** NEWSEQUENTIALID plus EF value generation would make a new child of a loaded aggregate look like an existing row. The configuration uses `ValueGeneratedNever`. The mutation test reproduced the exact failure: "expected to affect 1 row(s), but actually affected 0".
4. **EF migration warning was acted on.** The compat-level `ALTER DATABASE` (non-transactional) sat inside `Initial`. EF warned that the migration could be left half-applied, so it was moved into its own migration.
5. **The idempotent script was broken, found only by running it.** EF wraps custom SQL in `IF NOT EXISTS … BEGIN … END`, where `CREATE TRIGGER` isn't allowed. `dotnet ef database update` had worked, which hid the problem. Fix: `EXEC(N'…')`. A second failure (Msg 1934) came from `sqlcmd` defaulting to QUOTED_IDENTIFIER OFF, which the filtered index needs. That is documented in `db/scripts/README.md` (`-I`).
6. **The HOLDLOCK mutation went undetected at first, reported honestly.**
   - Removing HOLDLOCK did *not* fail the 50-concurrent test: once a year's row exists, the UPDATE path is atomic anyway.
   - The AI added a test aimed at the first-insert race. Its first version was **wrong**: 160 allocators exceeded SqlClient's default Max Pool Size of 100 and deadlocked against a blocking `Barrier` (2-minute timeouts, failing even *with* HOLDLOCK). It was fixed to 80 allocators with an async gate.
   - Final evidence: with HOLDLOCK, 3/3 green. Without HOLDLOCK, 2/3 runs failed with `Violation of PRIMARY KEY constraint 'PK_ClaimNumberSequences'`. Detection is **probabilistic**, not deterministic.
7. **Stale test output after a failed build, twice.** `dotnet test --no-build` printed an old green summary right under a compile error. The AI disregarded it both times and re-ran after fixing. The commit script always rebuilds first, so stale results can't be committed.
8. **Analyzer and namespace fixes.** CA1822, CA1711 (the `…Collection` suffix) and a justified CA1001 suppression (disposed in `IAsyncLifetime`). The new `IntegrationTests.Persistence` namespace hid `ClaimsModule.Persistence` in `LayerDependencyTests`, fixed with `global::`.

**Mutation checks in P2** (all restored and verified with `cmp`):

| Mutation | Result |
|---|---|
| `ValueGeneratedNever` removed | Add-child test failed (UPDATE affected 0 rows), plus the model key tests and the root-stamping test |
| HOLDLOCK removed | Probabilistic: 2 of 3 runs failed (see item 6) |

**New decisions D-51…D-59** are in DECISIONS.md. D-51 is listed as a deliberate deviation. Follow-ups were added to PLAN:
- **P2.5 task 17f** (D-56, D-57): the least-privilege app DB user with DENY UPDATE/DELETE on the audit log, and the retrying execution strategy.
- **P3 task 23a** (D-59): also validate transitions against the database table.

**Deliberately not in P2:**
- Read-side query services (list/detail/audit DTOs). They need the Application DTOs and AutoMapper profiles, so they arrive with P3 tasks 19 and 21.
- The MediatR implementation of `IDomainEventDispatcher` (P3).
- The idempotency middleware that uses `IdempotencyRecords` (P5).

### Your P2 review (accepted) and follow-ups (2026-11-23)
- You re-ran build and tests (306 + 124 green) and checked the dev database yourself: column types, defaults, compat 160, and the trigger blocking UPDATE/DELETE.
- **Follow-up 1: a deterministic HOLDLOCK test.**
  - **Your design:** tx1 allocates and keeps its transaction open; tx2, with `SET LOCK_TIMEOUT 500`, allocates the same year and must time out.
  - **What the AI found:** the AI predicted this design can't detect a missing HOLDLOCK. Once tx1 has *inserted* the counter row, tx2 blocks on that row's X lock under any isolation level. Implemented as specified, it passed **both** with and without HOLDLOCK (3 runs each), so the prediction was confirmed. The test is kept, renamed `Same_year_allocation_waits_while_another_transaction_holds_the_counter`, because it does prove serialisation.
  - **What detects it deterministically**, added as two tests:
    - (a) After tx1's first-of-year MERGE, `sys.dm_tran_locks` shows `Range*` key locks for its session.
    - (b) tx2's first allocation of a different, **adjacent new year** times out (1222), because only tx1's key-range lock covers the empty range.
  - **Mutation evidence:** with HOLDLOCK, 10/10 runs green. Without HOLDLOCK, both new tests failed in **10/10** runs, and the same-year test never failed.
- **Follow-up 2:** the stale-Pending GL posting sweep moved out of Stretch into **core P3 task 27a**, with behaviour and tests specified. D-56 is updated.
- **Follow-up 3: `EnableRetryOnFailure`.** The AI checked the premise empirically with a throwaway probe test (since deleted) instead of from memory:
  - `BeginTransactionAsync` itself **succeeds**.
  - The **first EF operation inside** the user transaction throws *"The configured execution strategy 'SqlServerRetryingExecutionStrategy' does not support user-initiated transactions…"*.
  - Wrapping in `CreateExecutionStrategy().ExecuteAsync` works.
  - The required fix is the same; only where the error appears differs. D-56 and P2.5 task 17f record the verified behaviour and the replay caveat (the retried unit must be the whole command, with a fresh context).
  - *AI correction:* the AI's first wording of D-56 in this session repeated "BeginTransaction throws" before verifying. The probe disproved it, and the wording was fixed before commit.
- `main` was fast-forwarded to `p2-persistence`.
