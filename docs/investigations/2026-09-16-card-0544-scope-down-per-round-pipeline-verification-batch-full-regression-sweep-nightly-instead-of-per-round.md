# CARD-0544: Scope down per-round pipeline verification; batch full regression sweep nightly instead of per-round

Date: 2026-09-16. Stage: Investigate. **Confirmed from source, stored execution evidence and live read-only database censuses. Next: Plan.**

The repeated verification is prescribed by automatically composed role instructions, the plan, and successive dispatch goals. It is not a server-enforced completeness calculation. Code must run every ordinary V/R and Unit plus the affected integrations; Review must independently rerun them. There is no current interim/final-round distinction in that contract. The nightly infrastructure exists, but the named jobs remain unregistered and unqualified. Nine completed CARD-0527 repair-stage tasks spent **233.90 minutes of runner-active wall time in their first ordinary matrices**, within **459.43 minutes of dispatched task time**, at **$158.70 recorded task cost**. This establishes the repeated cost mechanism, not an exact counterfactual saving.

No production changes, policy activation, new schedules, test executions, builds, service restarts, or deliberate mutations were performed in this investigation.

## Evidence scope

- Inspected repository: `C:\Antiphon\worktrees\card-task-1dd2f569`, source `7a7dac4ac4f9c4d1477dfbc094861dd990bd87f9`.
- Live `/api/version`: `8ccdb1c93d6dc288b05738ee0f7faded35548b4a`, with `land-v2`. This is an ancestor of the inspected source. The Code ordinary-scope paragraphs, Review/Mutation bundles, bundle selector and SourceLanding admission are unchanged between these revisions. The additional Code paragraph concerns alternate-source progress; settlement has intervening progress/recovery changes, not a V/R completeness gate. Source citations below refer to the inspected source, not an assertion that every current file is deployed.
- CARD-0544: `49a5f968-c14b-43a8-bf0d-d85dd60169e3`; CARD-0527: `75f13b1a-649c-4b4b-a034-e1b16663b591`; board: `8988ca03-7414-47ad-b0b6-51556c701703`.
- Read the full commissioned brief and `card.ps1 get CARD-0544`. Card requirements include lighter interim Code/Review, a final pre-land sweep, periodic broad regression, and an explicit configurable delayed-detection tradeoff. Exact ask retained in [card-0544.json](2026-09-16-card-0544-evidence/card-0544.json).
- Durable evidence: [2026-09-16-card-0544-evidence](2026-09-16-card-0544-evidence). It contains normalized task rows/goals/reports, SQL and outputs, derived timings, incremental method results, source hashes, and the analysis script. Original TRX/command logs remain under `C:\Antiphon\evidence\code-<task>` and `review-<task>`; their exact paths and hashes are retained.

## 1. Where ordinary verification is expressed and enforced

| Layer | Current behavior | Evidence |
|---|---|---|
| Automatic role composition | Every Worker stage receives its `stage-*` bundle and `delegate-basics`; this is not just a convention in an orchestrator's goal text. | `server/Application/Services/InstructionBundles.cs:184`, particularly 211-230; dispatcher composition at `server/Application/Services/AgentTaskDispatcher.cs:4018` and 4517. |
| Code scope | Unit plus the plan's named affected integration classes, fresh nonzero TRX, filters/counts, and each V-n/R-n. All PCs remain pending after ordinary work. | `server/Bundles/stage-code.md:3`, 5, 7, 11-13. |
| Review scope | Rerun claimed scoped ordinary checks and claimed ordinary tests; reject missing evidence. No distinction between a narrow repair round and a final land recommendation. | `server/Bundles/stage-review.md:3`, 5. |
| Human-readable owners | Build once, execute Unit and named integrations; report coverage-to-class and counts. Every ordinary V/R precedes Review/land. | `docs/testing-and-build.md:69`, 71, 73; `docs/orchestration-loop.md:1068`. |
| Plan/brief | The plan supplies V/R identities, affected classes, filters and costs; the brief supplies current failures, prior work and exact handoff. | `docs/orchestration-loop.md:425`, 439-450; CARD-0527 plan `docs/superpowers/plans/2026-09-15-card-0527-commit-on-settle-plan.md:609`. |
| Text-contract tests | Current tests require Code's `Unit plus the named affected` wording and retain inactive-policy language. These protect the instructions, not whether an individual task actually executed its tests. | `tests/Antiphon.Tests/Application/ScopedVerificationInstructionTests.cs:14`, 19-21, 24-37. |
| Generic inherited instruction | `RUN THE FULL SUITE ONCE, THEN TARGET` is also composed. This is broader/less precise than the stage-specific affected-class policy and does not define a repair-sequence boundary. | `server/Bundles/delegate-basics.md:68`; automatic composition above. |

**Settlement does not audit ordinary coverage.** `AgentTaskReplyService.ClassifyReportAsync` accepts an explicit `done` after the Code/Worktree progress check (`server/Application/Services/AgentTaskReplyService.cs:2490`, 2504-2508). That progress check concerns attributable post-dispatch work, not TRX or a V/R checklist (`:2412`). Settlement stores the report, cost and parsed next-stage handoff (`:632`, 645-668). The stage outcome comes from the finding line (`:3113`); clean Review evidence binds an authorized subject and reviewed SHA/ref/repository (`:3144`). These methods do not compare expected versus executed ordinary tests, or require every V/R row before success.

This distinction is visible in stored rows: initial Code **2853f966** settled Succeeded while its report omitted the full Unit lane and explicitly left R-17 unexecuted. Review **89bfbbdf** then reported that omission as F7, ran Unit, and found two new regressions. Success meant that the delegate finished its reported work; it was not proof of complete verification. See [task-rows.json](2026-09-16-card-0544-evidence/task-rows.json), rows keyed by those full task IDs, and the corresponding DB rows in [task-census.txt](2026-09-16-card-0544-evidence/task-census.txt).

**Landing is a separate machine gate.** A fresh land requires an expected SHA; optional Review evidence checks clean status and matching subject/SHA/ref/repository (`server/Application/Services/AgentTaskLandService.cs:122`, 129-137; `LandApproval.cs:28`). It does not import a test manifest from Code or Review. `AgentTaskLandingProtocol.cs:235` can skip verification when the base is unchanged and no filter was requested. Otherwise `AgentTaskLandService.cs:886` builds; a missing filter means build-only (`:894`). A provided filter runs `Antiphon.Tests` and requires one fresh TRX with nonzero passing counters (`:895-914`). Therefore `-Verify`/`LandVerifyFilter` controls the explicit landing operation, not the per-round role contract or a comprehensive nightly sweep.

No typed interim/final verification profile or ordinary test selection was found in the inspected `CreateAgentTaskRequest` (`server/Application/Dtos/AgentTaskDtos.cs:1` through 169), `AgentTask`, or `Card`. Existing task `Scope` is a write/collision bookkeeping field, not a test-completeness field; its documented non-enforcement is at `docs/orchestration-loop.md:951`. This negative finding is bounded to these create/settle/review/land paths and entities.

## 2. What PC deferral actually does

The existing pattern splits **work ownership and source identity**; it does not periodically schedule a general regression suite.

1. Code implements/tests ordinary behavior and reports every PC pending; a separate ordinary Review judges the code and pending controls. This also applies to zero-PC plans (`stage-code.md:7`, 11; `stage-review.md:3`).
2. The caller records a same-board companion before land, with original Code/Review identities and pending inventory (`docs/orchestration-loop.md:1075`). The companion's stable text key is discoverability, not database uniqueness.
3. Confirmed structured publication supplies operation O and landed source L. A queued land, task success, or pushed branch is insufficient (`docs/orchestration-loop.md:1085`).
4. **Explicit dispatch:** `delegate.ps1 -Role Mutation -Worktree -SourceLanding <O> -Card <companion>` after publication and any required deployment (`docs/orchestration-loop.md:1099-1105`). The owner explicitly says no tick creates cards or spends quota (`:1115`).
5. Admission requires fresh Worker/Mutation/Worktree, a distinct companion on the same board, the same project/repository, and confirmed publication. Open attempts for the same O are serialized, including Blocked (`server/Application/Services/SourceLandingAdmission.cs:23-72`; create path `AgentTaskService.cs:1001-1010`). The managed snapshot is cut from `O.VerifiedSourceSha`, not current master (`DelegationWorktreeService.cs:229-238`).
6. Mutation runs exact-method controls and discovery; the snapshot cannot be landed (`AgentTaskLandService.cs:78`), and its bundle forbids commits/pushes and requires external restoration evidence (`server/Bundles/stage-mutation.md:3-15`).

Thus there is real tooling enforcement of the post-land **identity, custody and admission** contract, while the choice and completeness of each PC remains commissioned work/report evidence. Nothing inspected automatically turns ordinary regression omissions into SourceLanding work. SourceLanding accepts Mutation and one confirmed operation; a multi-land periodic sweep is not its existing input shape.

## 3. Windmill and the current nightly backstop

The referenced memory was read at `C:\Users\lndco\.claude\projects\C--src-Antiphon\memory\reference_windmill_cleanup_schedule.md:13`: desktop-tagged bash jobs run in the Linux worker and SSH to Windows PowerShell. Repository owner: `docs/bootstrap.md:93-98`. The investigation used the existing SSH read-only SQL path; no token creation, job registration or execution was performed.

**Live census, 2026-09-16 08:41:09 and 08:48:04 UTC, Windmill database on server2:**

| Observation | Exact evidence/result |
|---|---|
| Cleanup schedule | Workspace `mc`, `u/lndcobra/antiphon_build_junk_cleanup`, enabled, cron `0 0 9 * * 1`, `Europe/London`. Active script is `desktop` / `bash`, not archived/deleted. |
| Other expected registrations | Claude cleanup, Codex residue cleanup and Antiphon GitHub sync present. The census reached the expected deployment. |
| Nightly schedules in any workspace | **0 rows** for schedule path or target containing `nightly`. |
| Nightly script versions | **0 rows** for paths containing `antiphon_nightly`, including archived/deleted versions. |
| Retained nightly jobs | **0** for runnable paths containing `antiphon_nightly`. This is retention-bounded historical evidence. |
| Local native last attempt | `C:\Antiphon\nightly\last-run.json`: September 4 failed feature-ref run, SHA `829e516a797044a354894790c06d6f8ebb7944e8`, exit 1. |
| Qualification/readiness | `last-complete-green.json` absent; the actual monitor path `last-monitor.json` absent; no `docs/investigations/*0487*qualification*` artifact in the inspected checkout. |

Sources: [windmill-census.txt](2026-09-16-card-0544-evidence/windmill-census.txt), [explicit nightly census](2026-09-16-card-0544-evidence/windmill-nightly-explicit-census.txt), [nightly-last-run.json](2026-09-16-card-0544-evidence/nightly-last-run.json), [observations.json](2026-09-16-card-0544-evidence/observations.json). The underlying queries are `BEGIN READ ONLY` and select specific operational columns, not credentials.

**Existing implementation already uses this scheduling primitive.** `scripts/windmill/antiphon-nightly-tests.schedule.json:2-10` defines 00:30 Europe/London, empty arguments and desktop routing; its script calls the native bootstrap through the SSH bridge (`antiphon-nightly-tests.json:11`). The separate health definition runs every 30 minutes with `default` routing, not desktop (`antiphon-nightly-health.schedule.json:2-10`). Registration remains an operator step (`scripts/windmill/README.md:8`). The native bootstrap owns a dedicated clone and state root (`scripts/nightly-run.ps1:5-9`, 48-58); its complete-green predicate requires coverage, tests, reporting, and scheduled master identity (`scripts/lib/nightly-run-impl.ps1:29`).

The current execution universe includes Antiphon, SessionRunner, PtyHost, Agents.Pty, Messaging, client and scripts (`tests/test-execution-policy.json:4-12`), with disposable broker coverage and guarded child environments. The suite loop is in `scripts/lib/nightly-tests-impl.ps1:306`. **E2E is explicitly manual**, not in the default suites (`tests/test-execution-policy.json:75-82`); headed/live cases also have eligibility rules. The card's phrase “any native/E2E lanes” therefore exceeds the currently unattended universe. A scheduled green cannot be credited for manual cases it did not execute.

The readiness gate is already written: reduced dispatch stays inactive until S4 proves a manual full unattended green, a subsequent real scheduled green, independent monitoring and notification receipt (`docs/testing-and-build.md:219-236`). CARD-0487 being Done does not waive this: its close revision **27**, `2026-09-12T12:30:05.006802Z`, explicitly says infrastructure shipped but the Windmill job does not exist and remaining dispatch-scoping work was not completed. Exact revision: [card-0487-close-revision.json](2026-09-16-card-0544-evidence/card-0487-close-revision.json).

**Scheduling design space, as found:** Windmill already supplies recurring native commands and checked-in nightly definitions; no new scheduling primitive is needed merely to express a periodic full test command. Deployment/qualification and execution scope are unresolved. Antiphon's scheduler separately supports standing-agent prompts and card actions (`server/Domain/Enums/ScheduleKind.cs:4`), with Once/Interval/Daily recurrence (`ScheduleService.cs:756-771`). It has no direct scheduled `AgentTaskRole`/SourceLanding payload. Spending card actions require explicit acceptance (`ScheduleService.cs:785-791`). An agent-driven periodic sweep would therefore use prompt/card orchestration, not an existing self-dispatching Mutation job. These are available boundaries, not a selected implementation.

## 4. CARD-0527 case study: measured work, not “nine pairs”

At the **08:46:35 UTC** task census, there were **14 directly bound Code/Review rows**: 11 Succeeded, two Canceled, one active Review titled “round 9.” There were six successful Code tasks and five completed Reviews, not nine completed Code/Review pairs. Thread search also returns other cards whose goals mention CARD-0527; those were excluded. DB enum values are Code=2, Review=3, Succeeded=4, Canceled=6 (`server/Domain/Enums/AgentTaskEnums.cs:30`, 81).

The initial implementation and first Review consumed 89.82 and 19.06 minutes, at recorded costs $148.55 and $9.12. All 11 successful tasks total **568.31 minutes / $316.37**. These are summed dispatched durations, excluding queue time, overnight gaps and canceled work. One canceled Code ran 33.68 minutes with a stored $0 cost; the other never dispatched. Neither zero is evidence of free resource use. Active Review **1d0539bc** likewise has no settled cost or end time and is excluded from completed totals.

The nine completed repair-stage tasks after the first Review provide the repeat-round comparison below. Full IDs, timestamps, session IDs and stored costs are in [task-census.txt](2026-09-16-card-0544-evidence/task-census.txt). Counts and timings are reconstructed from 304 retained TRX files across the wider census, command metadata and per-ID verification mappings; 646 source file hashes are retained.

### Measurement definitions

- **Task min:** `CompletedAt - DispatchedAt`, not the estimate supplied at dispatch.
- **Matrix min:** union of TRX `Times.start..finish` intervals for the first `unit`, `classes`, `reply`, `outbox`, `distillation`, `worktree`, `cardreview`, and `r17` runs. Concurrent intervals are counted once. Includes runner/fixture time inside those intervals; excludes builds, npm, client runs, base comparisons, later repair/recheck runs, analysis and cleanup. It is a conservative observed portion of task time, not all verification overhead.
- **336-case min:** union for the unchanged outbox/persistence/receipt/attention/retention selection (258 cases) and distillation selection (78). Their test source paths did not change between repair commits `4d2a9550` and `1ef62967`; see [repair-test-paths.txt](2026-09-16-card-0544-evidence/repair-test-paths.txt). Their production dependencies still changed, so unchanged test text alone is not proof that exclusion is safe.
- **Incremental cases/body min:** latest recorded expanded results for the newly assigned V ranges in that repair, using each row's exact class/method and TRX duration. This includes new or specifically changed regressions. Body durations are summed, may overlap, and exclude assembly startup/build/setup outside the body. **They are not measured durations of standalone method-only invocations.** No subtraction of these numbers from matrix wall time is presented as a promised saving.

| Task / repair | Role | Task min | Task cost | Matrix min | % of task | 336-case min | Incremental cases / body min |
|---|---|---:|---:|---:|---:|---:|---:|
| 360e223e / F1-F7 | Code | 98.34 | $38.10 | 18.39 | 18.7% | 10.49 | not isolated |
| 4d210064 / F1-F7 | Review | 42.64 | $6.20 | 26.85 | 63.0% | 16.69 | not isolated |
| 0d48a707 / F8-F9 | Code | 37.77 | $12.66 | 19.17 | 50.7% | 10.75 | 13 / 1.04 |
| 32c1f938 / F8-F9 | Review | 33.88 | $11.89 | 25.32 | 74.7% | 13.65 | 13 / 1.99 |
| b23741b2 / F10-F12 | Code | 67.87 | $23.86 | 22.66 | 33.4% | 10.52 | 37 / 3.01 |
| 7ef54b1c / F10-F12 | Review | 48.42 | $18.66 | 41.40 | 85.5% | 20.68 | 37 / 3.65 |
| ca4eef9c / F13-F14 | Code | 46.02 | $17.92 | 20.97 | 45.6% | 10.17 | 15 / 1.00 |
| 80155ada / F13-F14 | Review | 39.22 | $13.22 | 31.98 | 81.5% | 12.83 | 15 / 0.96 |
| 151b4e13 / F15-F16 | Code | 45.27 | $16.19 | 27.17 | 60.0% | 12.16 | 12 / 1.06 |

Incremental mappings are V-56..61, V-62..67, V-68..74, and V-75..79 respectively. F10-F12's 37 include 28 added cases plus nine existing non-repository/storage cases mapped to the repaired V-67, not 37 new tests. The substantial F1-F7 repair changed existing V definitions without allocating new V ranges; it is not represented by a fabricated zero incremental cost. All 142 incremental expanded results in the seven completed narrow-stage rows passed; the retained additional 12 results belong to the active final Review and are excluded from this table.

Detailed inputs and calculations: [summary-measured.json](2026-09-16-card-0544-evidence/summary-measured.json), [runs-measured.json](2026-09-16-card-0544-evidence/runs-measured.json), [incremental-cases.json](2026-09-16-card-0544-evidence/incremental-cases.json), [measure.py](2026-09-16-card-0544-evidence/measure.py). Every retained TRX interval was checked against its task's dispatch/settlement window; none was outside it.

### What these numbers establish

- Nine repair-stage tasks: **459.43 task minutes / $158.704755**, first matrices **233.90 wall minutes (50.9%)**. Across all retained .NET invocations, including base/recheck/repair runs, the per-task interval unions total **263.33 minutes**. The four completed repair Reviews alone repeat **125.55 matrix minutes** within 164.16 task minutes, at $49.974767 total task cost.
- The repeated 336-case group accounts for **117.93 wall minutes** across those nine tasks. The first Unit invocations total **24.08 process-minutes**. Thus the measured cost is not explained solely by a large Unit test count.
- Final distinct reports grew from **3,056** cases after F1-F7 to **3,069**, **3,097**, **3,112**, and **3,124** after subsequent repairs. Most newly assigned narrow regressions are 12-37 expanded cases, rather than another 3,000 new behaviors.
- The first matrices understate work. Retained build logs alone report **45.16 process-minutes** across eight of these tasks; the F1-F7 Review's build elapsed logs were not retained at that root. Build sums include repeated/base/E2E builds and may overlap other work, so they cannot simply be added to the matrix union. F10-F12 Code has another 9.27 TRX process-minutes of repeats/targeted work; the F1-F7 Code has another 13.77. Client and npm costs are not included in these numbers.
- Exact inherited .NET checks are small once built: the five base checks in F8/F9 Code total **4.90 seconds of TRX intervals**, **13.66 seconds** including their recorded command wrappers. Base build and client checks are additional. Later rounds encountered extra process timeouts and therefore larger recheck sets; these are not all fixed costs or all avoidable duplication.
- Stored costs are token-price estimates, computed for the entire task window (`AgentTaskReplyService.cs:645-658`), not CPU charges or per-command bills. Waiting and reasoning are not billed proportionally to test wall time. **There is no support here for claiming a dollar saving by multiplying $158.70 by 50.9%.** Command-attributed usage and paired scoped runs would be needed for that claim.

The behavior was explicitly commissioned as well as bundled: task **0d48a707** ends its goal with “Rerun Unit plus the named affected integration classes”; **151b4e13** says “Rerun the full plan V/R filters”; Review **80155ada** says “Verify independently - rebuild, rerun scoped selections.” Exact goals are retained in `task-rows.json`. Thus the loop was repeatedly resetting a broad ordinary-verification obligation even when each new handoff named only two defects.

### Detection benefit and churn are separate facts

Initial Review **89bfbbdf** found two new Unit regressions after Code omitted Unit: `SpecialistRoleContractTests.no_Check_comparison_survives_outside_the_allowlist` and `DelegationHarnessCensusTests.RuleD_GitWorkspaceService_one_liner_only_in_the_helper_and_its_pin`. The wider policy did catch real omissions.

Subsequent repair Reviews found F8/F9, F10/F11/F12, F13/F14, and F15/F16 even when claimed ordinary V/R passed and broad failures matched baseline. Their reports/probes identify uncovered recovery or path-handling behavior. They do not demonstrate that repeating the same broad passing matrix found those defects. The caller's F15 brief identifies the fifth recurrence of the recovery-identity-loss bug class. Semantic review and new failure-combination probes contributed value independent of rebuilding/rerunning all prior cases.

The repeated baseline failures were the classification/lane/policy guards, stale `ScopedVerificationInstructionTests.C487_G142` stage wording (`:141` still expects next: mutation), WorktreeHealth output, and the duplicate client label. A temporary seventh PDF failure and script-process timeouts were separately rechecked. This investigation did not change tests or relabel failures as flaky.

## Confirmed mechanism and remaining uncertainty

**Confirmed:** automatically attached policy plus plan/goal instructions require a broad ordinary matrix again on every repair dispatch, including independent Review. Settlement records self-reported completion and source identity without measuring verification completeness; no typed final-round boundary changes that obligation. PC deferral is a separate explicitly commissioned post-publication source contract. The proposed periodic backstop is not operating, despite checked-in infrastructure and CARD-0487's Done status. Stored CARD-0527 runs reconstruct the substantial repeated time burden.

Remaining uncertainty relevant to Plan:

1. Exact wall and token-cost savings from an interim selection are unmeasured. Stored individual body times exclude startup, fixture hooks, retained smoke coverage and final/nightly amortization. A paired method-selection run at the same SHA/environment would resolve the timing gap; command-correlated usage would resolve dollar attribution.
2. No full unattended or scheduled nightly was run here. Live registration, bridge/worker behavior, coverage completeness, monitoring and actual receipt still need the existing S4 qualification evidence before any backstop credit.
3. The current manual E2E/headed/live exclusions do not satisfy an unconditional all-lanes nightly claim. Their intended eligibility is a Plan decision within the card's scope, not established by this census.
4. “Round 9” was still active at the fixed census. Its partial measured results are retained separately, not counted as a completed round or charged at its in-flight $0 value.
5. Test selection cannot be inferred safely just from unchanged test files. This report identifies measured repeated work and current control surfaces; it does not establish which guards may be omitted or which risk-sensitive cards should permit deferral.

## Not done, noted

Plan can evaluate an explicit configurable interim/final verification contract across role bundles, plans/briefs and any required tooling, with reuse and qualification of the existing Windmill nightly as the periodic backstop.

## Reproduction and handoff

On this evidence host, `python docs/investigations/2026-09-16-card-0544-evidence/measure.py` reconstructs the derived JSON from committed task rows and the original absolute TRX paths; it performs no tests or service writes. Original files may later be removed or, for the active task, extended; the committed hashes and measured rows preserve the observation. DB census SQL is supplied beside its output. No test pass claim is made for this investigation; verification consisted of source tracing, read-only censuses, interval/count reconciliation and artifact checks.

Plan has the actual instruction/tooling boundaries, existing post-land commissioning pattern, deployed scheduling census, CARD-0487 qualification dependency, and measured per-task costs. The investigator has not selected scoping heuristics, designed a new scheduler or implemented a policy change.

--- next stage ---
next: plan
handoff: Use CARD-0544's confirmed bundle/brief repetition and CARD-0527 measurements to plan configurable interim versus final verification. Reconcile CARD-0487's inactive policy and absent Windmill jobs; retain explicit SourceLanding ownership and decide native/manual E2E coverage without claiming unmeasured dollar savings.
artifact: docs/investigations/2026-09-16-card-0544-scope-down-per-round-pipeline-verification-batch-full-regression-sweep-nightly-instead-of-per-round.md
