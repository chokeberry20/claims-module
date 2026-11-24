# AI Workflow Report

How this project was built with AI assistance (brief §4.5). The full, chronological record (every session, prompt summary, run id, mistake and correction) is [docs/ai-log.md](docs/ai-log.md); this report summarises it. Where a decision was mine rather than the AI's, it is marked as mine in DECISIONS.md and ai-log.md as well.

## 1. Tools

| Tool | Used for |
|---|---|
| **Claude Code** (CLI, model Claude Opus 5.5) | Everything that touched the repository: requirements analysis, planning, all code and tests, Bicep and the GitHub Actions workflows, running builds, tests and deployments, reading Azure logs, and drafting the documentation (including this report, which I then reviewed and edited) |
| **A second, independent Claude session** (Claude desktop app, Code tab) | **The reviewer**, for the plan and then for every phase. It had its own context and no stake in the first session's answers. It verified independently: it re-ran the build and tests, inspected the database, and called the live Azure endpoints. Its findings came back to the writer session through me (see §2) |
| Headless Chrome, driven by the AI through `playwright-core` | Checking the UI end to end, locally and against Azure, with the console and network logs kept |

## 2. How the workflow was structured

**Three roles: writer, reviewer, decider.**

| Role | Who | What it did |
|---|---|---|
| **Writer** | Claude Code (CLI) | Planned, implemented, tested, deployed and documented each phase, then stopped and reported its results |
| **Reviewer** | A second Claude session (desktop app, Code tab) | Checked each phase's claims instead of reading them. It re-ran build and tests (several times, to catch flakiness), looked at the database schema and data, called `/health`, Swagger and the Hangfire dashboard on Azure, and compared behaviour with the spec |
| **Decider** | Me | Read both sides, checked what mattered against the spec, accepted or rejected each finding, and sent the accepted ones back to the writer as corrections or decisions. Only I could approve pushes, deploys and phase acceptance |

Why two sessions: a session that wrote the code tends to confirm its own story, and a fresh one doesn't.

The reviewer **caught real problems**:
- the false "6/6 pass" in P0;
- a flaky integration test and an exact-only claim-number search in P3;
- the "cold start" misdiagnosis in P2.5;
- in P6a, it pushed for a root cause of the intermittent sign-in failure instead of accepting a passing re-run.

It was also **wrong twice**:
- it suggested moving Idempotency-Key handling to Stretch;
- it designed a lock test that couldn't detect a missing `HOLDLOCK`.

So its findings were inputs to my decision, not instructions. In [docs/ai-log.md](docs/ai-log.md) these findings appear in the phase reviews ("your review", "found by you"), because they reached the writer through me.

**Context loading.**
- Both source documents (the brief and the functional specification, both confidential) were extracted to plain text *outside the repository* and loaded in full at the start.
- The first session ran in **plan mode** (read-only): the AI could analyse but not write code.
- From then on, the context that mattered was kept **in the repository, not in the chat**:

| File | Holds |
|---|---|
| [docs/PLAN.md](docs/PLAN.md) | Requirements traced to spec sections, and phases of small tasks, each with a *Verify* step |
| [docs/DECISIONS.md](docs/DECISIONS.md) | Every ambiguity and decision as a numbered entry (D-00…D-76), so a rule is never re-decided by accident |
| [docs/ai-log.md](docs/ai-log.md) | What happened in each step, including mistakes |
| [docs/HANDOFF.md](docs/HANDOFF.md) | Written before each context compaction: current state, working rules, Azure names, known gotchas. The next context starts from it, not from a lossy summary |

**Prompt sequencing: phases with review gates.**
- Work went phase by phase: P0 bootstrap → P1 domain → P2 persistence → P2.5 a deployed walking skeleton → P3 application → P4 infrastructure → P5 API → P6a demo UI → P8 docs.
- Each phase ended with **"stop for my review"**. The reviewer session verified it independently, I decided on its findings and checked the key points myself, and then either accepted the phase or sent corrections. Only an accepted phase was merged to `main`.

**Standing rules I gave the AI** (in ai-log and HANDOFF §3):
- Re-run build and **all** tests immediately before every commit and report *that* run's output. This was introduced after the AI reported a stale "6/6 pass"; see example 1.
- Every outward-facing action needs my explicit approval for that phase: push, deploy, any Azure write, moving `main`.
- Secret values never appear in commands or output: they are generated or read into a variable inside the same command.
- The confidential source documents are never committed.

**Iteration: the AI verifies, then I verify.**
- The AI didn't just write code; it had to prove it.
- **Mutation checks:** break the code on purpose and show that the test fails.
- **Probes before claims:** a throwaway test to check how EF actually behaves before writing it down.
- **Real deployments with smoke tests:** a script that waits for the deployed commit and checks 15 things.
- **Real browser runs** against Azure.
- Then the reviewer session and I checked the important parts independently: the database schema, `/health`, Swagger, the Key Vault role assignments, the Hangfire dashboard.

## 3. Representative prompts

| # | Prompt (verbatim or close summary) | Purpose |
|---|---|---|
| 1 | *"Analyze both thoroughly. Do not write code. Identify FRs with spec IDs, NFRs, entities and aggregates, architecture within the mandated stack, modules, technical challenges, and ambiguities/contradictions with resolutions. Produce a detailed plan with small verifiable tasks, save it to docs/PLAN.md and DECISIONS.md, and log assumptions here."* | Build requirements traceability and surface contradictions **before** any design. It found, among others, that "no gaps" contradicts "a SEQUENCE is acceptable" in the spec, and that one seeded policy had already expired |
| 2 | *"Move a walking-skeleton deployment right after P2… The live demo requires the FNOL → reserve → approval → upload → audit flow in the UI… D-07 contradicts S4.2… The Hangfire dashboard can't use JWT Bearer… Mark as Stretch…"* | A critical review of the AI's plan: priorities, fidelity to the spec, feasibility. It produced plan rev 2 |
| 3 | *"No recruiter questions — I'm deciding these myself…"* followed by my decisions on waivers (D-07), adjust semantics (D-16), the $10M limit (D-19) and the dashboard (D-41) | I take ownership of the ambiguous business rules instead of letting the AI pick |
| 4 | *"P5 verified locally on my side: 318 + 44 + 322 green, 0 warnings. Decisions: accept the MediatR-only role checks and the 401 fallback. Change one thing: syntactically malformed JSON (unparsable body) should return 400, not 422 — keep 422 for type/enum mismatches and validation. Add a test for both."* | A phase review: independent verification, explicit acceptance of the AI's choices, one targeted correction with a test requirement |
| 5 | *"Before I accept P6a, run 2 needs a root cause — a sign-in form that sometimes doesn't work is exactly what could fail in the live review. Run only the sign-in step against Azure at least 10 times, with full browser console and network logs kept (don't filter the failure lines), including at least 2 runs after the API has been idle for 20+ minutes… If it's a real UI race, fix it with a test that reproduces a delayed users response. If it was only the test script, prove that with the logs."* | Refusing to accept "it passed on the third try". It demands evidence, specifies the experiment, and leaves the conclusion open |

## 4. What was AI-generated and what was mine

**AI-generated, then reviewed:**
- all production code, tests, migrations, Bicep and workflows;
- the first drafts of PLAN, DECISIONS (D-01…D-41 in rev 1) and all documentation;
- the diagnoses of build, deploy and test failures.

**Designed or decided by me:**
- **Plan structure:** deploy early (a walking skeleton after P2, a deploy after every phase); the demo-flow UI before the docs; the over-engineered items moved to Stretch.
- **Business rules where the spec was ambiguous:**
  - Critical issues can be waived by a supervisor with a reason, but a missing Claimant can't be (D-07).
  - The $10M limit is re-checked at approval time (D-19).
  - The Hangfire dashboard uses Basic auth instead of the AI's ticket-and-cookie design (D-41).
- **Environment and cost:**
  - no Docker; local SQL Server Express (D-42);
  - Azure SQL **Basic** instead of serverless (C-11);
  - the stricter numeric-ID OIDC subjects (D-63).
- **API behaviour:** unparsable JSON is 400, not 422 (D-72).
- **Security of the demo sign-in:** a form only, with no password in the frontend; tokens in memory for the tab; readable passwords rotated in before submission (D-73).
- **Which reviewer findings to act on:** e.g. keeping Idempotency-Key in the baseline against the reviewer, and keeping its HOLDLOCK test but adding the writer's lock-level tests, which can actually detect the bug (§5, example 3).
- **Scope:** the Overview and Parties tabs are must-have before submission, not polish.

## 5. Where the AI was wrong, and how it was corrected

The ai-log records many more cases. These are the most instructive. Each says who caught it: the writer's own checks, the reviewer session, or me.

**1. A false "tests pass" claim (P0, C-13).**
- **What the AI did:** the writer session reported "6/6 tests pass" for P0.
- **Caught by the reviewer session:** it re-ran `dotnet test` independently: 1 test failed. I rejected the phase.
- **Process cause:** after the last task the AI had only rebuilt, then quoted the test result from an earlier task.
- **Technical cause:** the AI wrote a JSON file through a shell heredoc, which collapsed `.\SQLEXPRESS01`'s backslash into an invalid JSON escape.
- **Corrections:**
  - My rule: build and run all tests right before every commit, and report that run's real output. This became a script that refuses to commit unless both are green.
  - A regression test that parses every committed JSON file. It was shown to fail on the broken files first.
  - The AI's own rule: never write backslashes through a shell.

**2. The plan contradicted the spec (plan rev 1–2, D-07).**
- **What the AI did:** first banned waiving Critical validation issues, although S4.2 says "(or all waived)". In rev 2 it then allowed waiving everything, including the missing-Claimant issue, while a separate structural check still blocked opening. That would have produced a confusing "waived but still blocking" state.
- **Correction:** I specified the rule: supervisor+ with a mandatory reason and an audit event, and NO_CLAIMANT not waivable, because BR-C-03 requires a Claimant independently.

**3. The reviewer was wrong too, twice (plan rev 4 and P2).**
- **Scope reduction (plan rev 4):** the reviewer session suggested moving Idempotency-Key handling and the append-only audit trigger to Stretch, to save time. I checked the spec: both are explicit requirements (S10; S14.2/BR-A-01). Both stayed in the baseline.
- **A test that couldn't fail (P2 review):** to prove the claim-number generator needs `HOLDLOCK`, the reviewer designed a deterministic test: tx1 allocates and keeps its transaction open, and tx2 must time out allocating the same year. The writer session **predicted this can't detect a missing `HOLDLOCK`**: once tx1 has inserted the counter row, tx2 blocks on that row's lock under any isolation level. Implemented as designed, the test passed **both with and without** `HOLDLOCK`, three runs each, so the prediction held. What does detect it: `Range*` key locks in `sys.dm_tran_locks` after the first-of-year MERGE, and a timeout when tx2 allocates an *adjacent new year*. Without `HOLDLOCK` those two tests failed 10 out of 10 times; with it they passed 10 out of 10. The reviewer's test was kept, renamed for what it really proves (serialisation).
- **Lesson:** a reviewer is another model, not an oracle. Its findings were inputs to my decision, and the writer's evidence could overrule them.

**4. Unverified claims about tooling (P2.5).** Three separate cases:
- **The OIDC subject format.** The AI used the documented classic format for the GitHub OIDC federated credentials and assumed it was right; the first deploy failed at Azure login. This repository sends numeric owner and repo IDs. Fixed to the subject actually presented, which is also stricter.
- **The grant step "passed".** The database grant step succeeded although its SQL had failed, because the GitHub action ran `sqlcmd` without `-b`.
- **The claimed `-b` fix.** The AI then *claimed* to have fixed this through the action's `arguments` input and committed that claim. It hadn't checked the command line the action really ran; the action ignores that input for `.sql` files. The AI found this itself while reading the next run's log and reported it.
- **Fix:** a pinned, checksum-verified go-sqlcmd with `-b`.
- **Proof:** a throwaway branch with a deliberate `THROW` made the deploy fail at exactly that step.

**5. A wrong diagnosis (P2.5).**
- **What the AI did:** proposed raising the smoke test's health timeout to 90 s for a "cold Basic SQL connection".
- **Caught by the reviewer session, confirmed by me:** Basic is a DTU tier and never auto-pauses; the ~60 s more likely came from EF's retry strategy retrying a failing connection inside the health check.
- **The AI verified this locally:** 17 s and 86 s for the old checks against a dead database.
- **Fix:** made the health checks fail fast (D-65) instead of hiding the problem with a longer timeout.

**6. A race in the sign-in form (P6a, D-75).**
- **What happened:** during the deployment check on Azure, one browser run timed out at sign-in, and the AI re-ran without reading the failure lines its own filter had hidden.
- **Pushed back by the reviewer session:** a sign-in form that passes "on the third try" isn't acceptable for a live demo. I sent the request for a root cause (prompt 5) and held P6a acceptance until it was answered.
- **The evidence:**
  - 12 runs against Azure with full logs: 2 failed, both exactly the runs in which the user picker was clicked milliseconds before the user list arrived.
  - A Material select with no options doesn't open.
  - Locally, a delayed user list reproduced it every time, and a failed one left the picker empty for good.
- **Fix:** loading and error states, a retry, and an inline error with "Try again", with tests that fail on the old form.
- **After the fix:** 12 of 12 runs on Azure signed in, clicking immediately on page load.

**7. A flaky test and a spec shortfall, from the P3 review.**
- **Flaky test.** The reviewer ran the whole suite 5 times; the first run failed 1 of 267 integration tests. The writer's first guess (two timing assertions) was wrong. Ten normal runs were all green, so it reproduced the failure under CPU load: 1 in 3 runs.
  - **Root cause:** a startup-failure test raced `WebApplicationFactory` disposing the service provider.
  - **Fix:** the test calls the same start-up validator directly, with no threads. After the fix, 10 out of 10 runs were green.
- **Exact-only search.** The writer had made claim-number search exact-only, claiming a value-converted column needs raw SQL for substrings. S10.1 asks for partial search. The column is a plain NVARCHAR, so EF can translate it. Fixed with a mapped database function, so `.Contains` becomes one SQL LIKE. Tests cover a middle substring, lowercase input and an escaped `%`.

**Smaller ones, caught by the writer's own tests or checks:**
- a soft-delete interceptor that rewrote the whole row;
- a concurrency "touch" that changed nothing, so EF didn't bump the row version;
- a test for a lock race that deadlocked on the connection pool;
- the KQL reserved word `last` used twice as a column name;
- a dialog that returned `''` instead of `undefined` on Cancel;
- a reserve panel that sent "Adjust" for a component that didn't exist yet.

## 6. Where AI assistance helped most, honestly

**Most valuable:**
- **Reading two long documents against each other.** The AI found contradictions and gaps between the brief and the spec that I would have found late or not at all (D-01…D-41): gapless numbering against a `SEQUENCE`, the SLA "status", stale seed dates, licence changes in MediatR and AutoMapper.
- **Volume with consistency.** EF configurations for 14 tables following one set of conventions, 37 commands and queries with their validators, 689 backend and 123 frontend tests, Bicep, two workflows. All of it was produced in hours, and kept consistent because decisions lived in DECISIONS.md.
- **Proving instead of asserting.** Mutation checks, throwaway probes, lock-level SQL evidence, deploy runs with a deliberate failure, browser runs with full logs. The AI was good at designing these experiments once asked for evidence.
- **Azure and CI plumbing.** OIDC, Key Vault references, managed-identity SQL users and the runner sign-in issue. The AI diagnosed these from logs much faster than I would have.
- **A second model as reviewer.** The independent session found the problems the writer was least likely to see in its own work: a stale test result, a flaky test, a spec shortfall, a wrong diagnosis, a race accepted too early. Every phase's claims were checked by a model that hadn't made them.

**Where it needed the most supervision:**
- **Claims about its own results.** "Tests pass" and "the step now fails on errors" were stated before they were checked. The fix was process (rules, scripts that refuse to commit), not trust.
- **Assumptions about external systems:** the OIDC subject format, an action's inputs, timing in Azure. These were right in the documentation and wrong in practice.
- **Priorities.** Left alone, the plan put deployment and the UI last. Scope and order were my decisions.

**Overall.** The AI wrote nearly all of the code, and a second AI checked it. The engineering judgment came from deciding between them and demanding proof:
- what to build first;
- which spec reading to follow;
- what counts as done.

That's where the time was best spent.

## 7. AI interaction history

- **In the repository:** [docs/ai-log.md](docs/ai-log.md), the running log of every session: prompts (summarised or verbatim), the AI's outputs and mistakes, my reviews and decisions, run ids and real results.
- **Conversation export:** the Claude Code session transcripts are provided with the submission (brief §4.6).
