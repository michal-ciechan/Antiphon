# Backlog follow-ups triage, 2026-11-02 (Investigate 9aef59e9)

Base: `5376ac529987b9e7b2019773cb4d784cba8007bf` (origin/master). Host: server2 Linux.
Scope: the Low/Normal rank-13 review follow-ups CARD-1467, 1470, 1476, 1482, 1484, 1487, 1488,
1497, 1498, 1501, 1503, plus CARD-1583 (rc incident), CARD-1623 and CARD-1625 (land
verification cost). Same method and verdict classes as the first triage
(`docs/investigations/2026-11-02-backlog-high-normal-triage.md` on `feat/card-task-81e40a23`).
The work was read-only apart from this note. No card was edited, closed or filed.
Database evidence comes from read-only `BEGIN READ ONLY` queries against the desktop Postgres
(`antiphon.desktop.codeperf.net:17280`).

## Outcome

Unlike the first batch, most of these are **still true on master**, because they are review
follow-ups that nobody picked up. Nothing has touched their code since 2026-10-22/23.

- **1 already fixed:** CARD-1498 (covered by the CARD-1483 S5 witness).
- **1 obsolete:** CARD-1583. This is a rolling rc incident from the 2026-10-30 run. It carries
  no failure body. The rc/2026-11-01 qualification loop replaces it.
- **2 need an operator decision on Windows evidence:** CARD-1625 and CARD-1623. They are one
  decision about the land-verifier build on the desktop.
- **10 still reproducible**, by code path. In value order: 1503, 1488, 1497, 1470 (merge 1467
  into it), 1487, 1476 (merge 1484 item 1 into it), 1482, 1501. 1484's other items are
  obsolete or verified.
- **New defect found while checking CARD-1487:** the desktop data-retention sweep has failed
  before its residue, usage and audit passes on all but one of 36 runs since 2026-10-26. This
  is not one of the 14 cards. It needs its own card (see "New finding").

## Probes

| # | What | Result |
|---|---|---|
| D1 | `WorktreeResidueRunCandidates` by Outcome and day | 122,466 Held rows from 2026-09-28 to 2026-11-02. About 2-6k/day up to 2026-10-26, then 87-201/day from 2026-10-27, which shows the S5 dedupe working. **14,508 rows are older than 30 days.** |
| D2 | `pg_stat_statements` (reset 2026-10-26 02:10Z) for the retention passes | `DELETE RemoteControlModalEpisodes` (pass 7, line 64) **36 calls**. The PruneTasks root-eligibility `SELECT ... GROUP BY RootTaskId` (pass 8, line 65) **1 call, 28.0 s**. `DELETE SubscriptionUsageSamples` 1 call. `DELETE WorktreeResidueRunCandidates` 1 call (1,884 rows). |
| D3 | `GET /api/version` (desktop) | `8edda999623a473be2d1970727c172bfb5088d59` (rc, 2026-10-29). It has the S5 prune. It predates the CARD-1610 supervision clause in PruneTasksAsync. |
| B | `dotnet build tests/Antiphon.Tests -p:UseAppHost=false` | First attempt: the slot wait (585 s) ran into my outer `timeout 590`, which killed the build (MSB4166 child-node exits; no orphans left). The retry is recorded under "Build and test probe" below. |

## Table (ordered by value)

| Card | Verdict | Evidence at master 5376ac529 | Disposition |
|---|---|---|---|
| CARD-1503 question turn overwrites retained report | (b) STILL REPRODUCIBLE | `AgentTaskReplyService.cs:4054` `if (LooksLikeAQuestion(body)) return (Blocked, QuestionHeuristic, body, null)` runs **before** the S2 guard at `:4074` `if (RetainedReportStands(task))`. The settlement keep-retained branch (`:1242`) only applies to `evidence == Marked`, so a QuestionHeuristic result replaces `Result`. rc/2026-11-01 has the same order (`:4054`). | Keep. Code brief below. Correctness (a delegate's marked report is lost). Highest value. |
| CARD-1488 stale settlement waiters | (b) STILL REPRODUCIBLE | `RemoteWorkspaceService.cs:808` registers a `WorktreeSettlement` waiter. It is cleared only on success (`:837`) or on a non-`LeaseWaiting` attempt end (`:445`). `RepositoryLeaseWaiters.ReconcileDispatch` (`RepositoryLeaseWaiters.cs:149-167`) drops only `Purpose == Dispatch`. `AgentTaskService.CancelCoreAsync` (`:3013-3047`) never calls `Clear`. A task cancelled or deleted while its sync waits keeps the waiter until restart, so each later land on that repository spends its 90 s yield. | Keep. Code brief below. |
| CARD-1497 nudge/wait note survive cancel | (b) STILL REPRODUCIBLE | `RetireObsoleteClosingNudgeAsync` (`AgentTaskReplyService.cs:4457`) has one caller, the settled-report path (`:1595`). `CancelAsync`/`CancelCoreAsync` (`AgentTaskService.cs:2997-3047`) does not cancel `ReportNudgeMessageId` or the parent's `task-wait:<id>` note (written only at `:4439`). No process-exit path does either. | Keep. Code brief below. |
| CARD-1625 land build 3x slower (memory policy) | (d) NEEDS DECISION + WINDOWS | Premise unchanged: `AgentTaskLandService.cs:1976` applies `BuildDriverEnvironment` to every verifier child. `scripts/lib/BuildDriverEnvironment.cs:30-36` sets `UseSharedCompilation=false`, `DOTNET_gcServer=0`, `DOTNET_gcConcurrent=0`, MSBuild server off and node reuse off on Windows. The file is unchanged since `462dfdd8e` (2026-10-23) and the same on rc. CARD-1506 (InProgress, High/Soon) names land verification explicitly as a target. | Decide. Exempting the verifier, or only shared compilation, reverses part of CARD-1506 and is the operator's call. Evidence needed: the card's own single desktop A/B (wall time and peak memory, same SHA), run after a qualification run, never beside one. |
| CARD-1623 land builds contend with qualification | (d) NEEDS DECISION + WINDOWS | Nothing in the verifier path knows about the native qualification lock or process priority (no `PriorityClass`/native-lock reference in `AgentTaskLandService.cs`, `LandingVerifier.cs`, `SessionRunnerBuildSlotGate.cs`, `BuildSlotBroker.cs`). The verifier builds with the grant's `-maxcpucount` (`AgentTaskLandService.cs:1940-1942`). The causal claim (C467/C488 stalls) is marked "unproven" on the card itself. | Decide together with 1625 (same verifier, same host). The options (below-normal priority, defer while the native lock is held, verify on server2 = CARD-1462) are design choices. Measurement needs a live Windows run. |
| CARD-1470 memory-floor fallback follow-ups | (b) STILL REPRODUCIBLE | `SessionRunnerBuildSlotGate.cs` is unchanged since `da0665f64` (2026-10-22). (1) The fallback line goes only to `observer.Line` → `LandingVerifier.Observer.Line` → `logger.LogInformation` (`LandingVerifier.cs:50-54`). It is not on the land operation, events or status; the land still reports `build OK, tests n/n`. (2) `Math.Max(1, …)` (`:40`) clamps only the floor. There is no upper bound or validator (`LandingSettings.cs:38`). (3) `LandVerificationBuildSlotTests` has 10 methods. None is for the clamp-to-1, the missed-answer restart, or a window ≥ the wait. (4) No global bound is stated. Fallback is default-off. | Keep, **merge CARD-1467 into it**. |
| CARD-1467 "waiting for memory" label | (b) STILL REPRODUCIBLE | `AgentTaskLandMonitorService.cs:108-115` `DescribeWaitingAsync` says `no progress since …; reason=<HoldReasonCode>`, and `AttentionService.cs:1541-1544` says `Land …; no progress for Ns`. Neither sees the build-slot wait reason, because it is only logged (as above). | Merge into CARD-1470. Both need the same "record build-slot wait or fallback on the land" seam. |
| CARD-1487 residue sweep S5 follow-ups | (b) STILL REPRODUCIBLE, D6 premise changed | D1: `ShouldWriteRow` (`WorktreeResidueSweepService.cs:405-421`) writes action rows always. No Execute=true cooldown-0 two-retry witness exists. D2: the key is `<Lane>\|<Outcome>\|<Code>` (`:425-429`), without authorization. D4: no overlap lock. D6: `PruneWorktreeResidueHeldRowsAsync` is still one `ExecuteDeleteAsync` (`DataRetentionService.cs:618-620`). The "95k rows on first pass" did not happen: the prune ran once since 2026-10-26 (1,884 rows, probe D2), because the sweep fails earlier (see "New finding"). Once that is fixed, about 14.5k rows plus 3-6k/day aged out from 2026-10-03..26 become eligible, so batching still matters. | Keep D1 (witness) and D6 (batch) as one small slice, sequenced after the retention-sweep fix. Treat D2/D4 as operator/design choices. D3/D5 are by design, close those parts. |
| CARD-1476 CARD-1430 S3 follow-ups | (b) STILL REPRODUCIBLE (items 1, 2); (c) item 3 | (1) `DispatcherLeaseSpanTests` has 3 methods (`:30`, `:68`, `:120`). None is a Shared claim, so dropping `claimed.Workspace == WorkspaceMode.Worktree` at `AgentTaskDispatcher.cs:6025` has no red test. (2) The four `PinBaselineAsync` calls in the baseline capture (`AgentTaskDispatcher.cs:7934-7941`) discard their `ProgressPinResult`. (3) The CARD-1430 plan carrying D-4 is not on master (no `docs/superpowers/plans/*1430*`), so there is nothing to amend. | Keep (1) and (2) as one test-first slice, **merged with CARD-1484 item 1** (same `PinBaselineAsync`). Close item 3. |
| CARD-1484 CARD-1462 S4 follow-ups | (b) item 1; (c) items 2, 3; item 4 see probe | (1) `TaskProgressGitTests.cs:227-228` covers only success. The repeat/collision witnesses exist for `LandingGit.PinAsync` (`LandingGitTests.cs:79`, `LandingGitBatchingTests.cs:133`), not for `TaskProgressGit.PinBaselineAsync` (`TaskProgressGit.cs:221-247`). (2)/(3) are text in a finished plan (`2026-10-22-card-1462-option-a-desktop-land-cost-plan.md`), a historical record. (4) CARD-1463's fix `6559f7400` re-judges the owner row first, and its plan CP-5 (`2026-10-22-card-1463-source-worktree-residue-plan.md:99`) runs `C475_Task_*` 7/7. The "CP-13a exclusion" exists only in the finished CARD-1462 report. | Merge item 1 into CARD-1476. Close 2-4. |
| CARD-1482 CARD-1463 follow-ups | (b) STILL REPRODUCIBLE | (1) `LandVerificationLeaseWindowTests.cs:152` asserts only `ShouldNotBe(originSha)`, not the exact landed commit. Unchanged since `ffda80a8e`. (2) `docs/orchestration-loop.md:164-170` still says a task-worktree finding is "never a refusal", with no note that a failed probe fails closed as `landing_io_error` (`AgentTaskLandingProtocol.cs:383-385`, `LandFailureDiagnostic.cs:20`). The rc port note is history. | Keep as a tiny test+docs slice. It can ride with any land-protocol Code task. |
| CARD-1501 crash-worker witness strength | (b) STILL REPRODUCIBLE (test strength only) | `CheckCompactionWorkerLifetimeTests.cs:47-83` asserts liveness only **after** `await using` disposal returns (`:72-75`), with no time limit on the disposal itself. No test hands an unowned root. The busy=True produce case still ends the turn before the scanner starts (`a01b38a2c`). Unchanged since 2026-10-22. | Keep at lowest priority, or close as accepted test debt (operator choice). No product defect. |
| CARD-1498 imported props `$(MSBuildProjectDirectory)` | (a) FIXED / superseded | CARD-1483 S5 witness `22c8d94857cbfae3cdac923f47b7b55b78a8a604` (2026-10-23): `LandGraphEvaluationScriptTests.C1483_S5_UpdateRefusesWhenEvaluationRefuses("imported-directory")` (`:80-93`) imports `eng/deep/shared.props` with `<None Include="$(MSBuildProjectDirectory)/../docs/flag.md" />` and asserts real evaluation exits 1 with the resolved path `docs/flag.md`, and `-Update` refuses. The S5 amendment names CARD-1498 at `2026-10-23-card-1483-s5-amendment.md:15-19`. | Close as fixed. |
| CARD-1583 rc incident 2026-10-30 | (c) OBSOLETE (superseded) | Rolling incident written by `scripts/nightly-report.ps1` (rc lane, `:712-723`, red path `:895-920`). Revisions: 2026-10-24, 26, 28 and 30 runs. Discussion: three `still red on …: TESTS` entries, with **no description and no failing test names** on any revision (description length 0). Nothing has been written since 2026-10-31 10:29Z. Current rc qualification is the Windows Full run on rc/2026-11-01 (`10a826b9d`), whose failures go to per-group Debug tasks. | Close as superseded. A later red rc job files a new incident: auto-reopen applies only to auto-closed cards, `nightly-report.ps1:835-840`. |

## New finding (not one of the 14): retention sweep stops at PruneTasksAsync

`DataRetentionService.RunOnceAsync` (`server/Application/Services/DataRetentionService.cs:56-75`)
runs its passes in sequence inside one try. `DataRetentionHostedService` logs any exception as
`Data retention sweep failed` (`DataRetentionHostedService.cs:70`) and the later passes are
skipped. Since the `pg_stat_statements` reset (2026-10-26 02:10Z), passes 1-7 completed 36 times.
The PruneTasksAsync root-eligibility query (pass 8, `:432-460`) completed **once**, taking
**28.0 s**. The server uses Npgsql's default 30 s command timeout: no `CommandTimeout` in
`Program.cs:129-138` and none in the appsettings connection string. The usage-sample delete, the
residue prune and (by order) the audit FullContent archive also completed once. The result:
14,508 Held residue rows are older than the 30-day retention, AgentTasks (22,684 rows) are never
pruned, and audit FullContent is not archived. Mechanism status: **reconstructed, not
confirmed**. pg_stat_statements does not record cancelled statements, so the timeout
exceptions themselves need the desktop server log (`antiphon-*.log`, "Data retention sweep
failed"), which server2 cannot read. Master's `2bf104be4` (CARD-1610, 2026-10-31) adds
another correlated `supervision.Any(...)` clause to this query. It is already on
rc/2026-11-01, so it will only get slower once deployed. No existing card covers this:
`card.ps1 search` for "retention sweep", "PruneTasksAsync", "Data retention" and
"TaskRetentionDays" found only unrelated Done cards.

## Code briefs for the reproducible cards

- **CARD-1503** (Normal). Invariant: once a Reply reopens an attempt whose row holds a MARKED
  report, no unmarked turn (ack or question-shaped) replaces `Result`/`ReportEvidence`. Witness
  (commit red first): reopen via Reply, then an unmarked turn ending in `?` → task Blocked,
  `ReportEvidence == Marked`, `Result` byte-equal to the retained report, and the caller warning
  carries the retained-report line. Smallest slice: move the `RetainedReportStands(task)`
  check ahead of `LooksLikeAQuestion(body)` in `AgentTaskReplyService` (`:4054`/`:4074`) and reuse
  the S2 Blocked tuple. File: `server/Application/Services/AgentTaskReplyService.cs` plus the
  CARD-1486 S2 witness class.
- **CARD-1488** (Normal). Invariant: a `WorktreeSettlement` waiter exists only while its task is
  open (Dispatched/Working/Blocked with a pending sync). Witness: register a settlement waiter
  with a `LeaseWaiting` result, cancel the task, run one dispatcher tick → `Snapshot(common)` is
  empty and the next land's `DecideYield` does not yield. Smallest slice: a
  `ReconcileSettlement(openTaskIds)` in `RepositoryLeaseWaiters` called from the dispatcher
  tick, which already loads open tasks. Alternatively call `Clear(task.Id)` in
  `CancelCoreAsync`, but that does not cover delete/restart races. Files:
  `RepositoryLeaseWaiters.cs`, `AgentTaskDispatcher.cs`, `RunnerSyncLeaseFairnessTests`.
- **CARD-1497** (Low). Invariant: a terminal task (Canceled, Failed by process exit) leaves no
  Pending untyped `ReportNudgeMessageId` in its own session and no Pending untyped
  `task-wait:<id>` note in its parent's queue. Witness: issue a nudge and a parent wait note,
  cancel → both rows Canceled. Repeat for the process-exit failure path. Smallest slice: call
  the existing `RetireObsoleteClosingNudgeAsync` logic, extracted to a shared helper, from
  `AgentTaskService.AfterCancelCommitAsync` and from the process-exit failure writer. Keep the
  queue-lock order as in CARD-1474.
- **CARD-1470 (+1467)** (Low). Invariant: a land whose verifier waited on the memory floor or
  fell back records that on the land (event detail or a request field). Land status and the
  aged/attention text then say `waiting for memory (available X MB, floor F MB, waited N min)`,
  or `memory-floor fallback at -maxcpucount:n`, instead of only `no progress`. Witnesses: the
  three missing gate cases (clamp to 1, missed-answer restart, window ≥ wait never fires), plus
  an upper clamp of `BuildSlotMemoryFloorFallbackMaxCpuCount` to `Environment.ProcessorCount`
  with its witness. Smallest slice: the gate witnesses and the clamp first (pure
  `SessionRunnerBuildSlotGate`). Surfacing on the land is the second slice, through
  `BuildSlotHold` → `LandVerification` description → land event detail.
- **CARD-1476 (+1484 item 1)** (Low, test-first). Witnesses: a Shared-claim
  `DispatcherLeaseSpanTests` case that holds R through the claim commit (red under PC "drop the
  Worktree guard" at `AgentTaskDispatcher.cs:6025`). Also `TaskProgressGitTests` cases for a
  same-SHA repeat (Succeeded) and another-SHA collision (`recovery_ref_collision`) on
  `PinBaselineAsync`. Production: surface a failed baseline pin from `AgentTaskDispatcher.cs:7934-7941`
  as a warning on the capture result (do not fail the claim, which keeps today's behaviour).
- **CARD-1487** (Low, after the retention fix). D1 witness (Execute=true, cooldown 0, two
  retries → two Queued rows → cap reached) and D6: batch the prune in bounded pages, for
  example 5,000 ids per `ExecuteDeleteAsync`, with the same cutoff.
- **CARD-1482** (Low). Assert `after == world.OwnerSha`-derived landed commit (as the C448_V10
  cells do) and reword `docs/orchestration-loop.md:168-170` to say that a failed task-worktree
  probe fails closed as `landing_io_error`.

## Closing reasons (ready to paste)

- **CARD-1498**: Fixed by CARD-1483 S5. 22c8d94857cbfae3cdac923f47b7b55b78a8a604 adds `LandGraphEvaluationScriptTests.C1483_S5_UpdateRefusesWhenEvaluationRefuses("imported-directory")`. An imported `eng/deep/shared.props` with `<None Include="$(MSBuildProjectDirectory)/../docs/flag.md" />` is evaluated by real MSBuild, which reports the resolved `docs/flag.md` and exits 1, and the land-audit `-Update` refuses without writing records. The S5 amendment (docs/superpowers/plans/2026-10-23-card-1483-s5-amendment.md:15-19) names this card as a V-14 regression witness. The emulated D-25 pre-check is no longer the authority. Investigate 9aef59e9 at master 5376ac529.
- **CARD-1583**: Superseded. This is the rolling release-gate incident from the rc Windmill job (runs 2026-10-24, 26, 28 and 30). It carries no run body or failing test names, and nothing has been written to it since 2026-10-31. The 2026-10-30 run tested an earlier rc line. rc qualification now runs as the Windows Full run on rc/2026-11-01 (10a826b9d), with per-group Debug repairs (docs/orchestration-loop.md, rc run-group repair loop), and those runs are the authority for rc reds. A later red rc job files a fresh incident with its own run body. Investigate 9aef59e9.
- **CARD-1484** (if not merged): Items 2-3 are text in the finished CARD-1462 plan, a historical record. Item 4 is settled by CARD-1463 (6559f7400), whose CP-5 runs the seven `LandingProtocolGuardTests.C475_Task_*` green. The exclusion it mentions lives only in the finished CARD-1462 report. Item 1, the `TaskProgressGit.PinBaselineAsync` repeat/collision witnesses, moves to CARD-1476. Investigate 9aef59e9.
- **CARD-1467** (merge): Merged into CARD-1470. Both need the land verifier's build-slot memory-floor wait or fallback recorded on the land, because today it reaches only the server log (LandingVerifier.cs:50-54), so land status and attention show `no progress` (AgentTaskLandMonitorService.cs:108-115, AttentionService.cs:1541-1544). Investigate 9aef59e9.

## Remaining uncertainties

- The retention-sweep mechanism (command timeout in PruneTasksAsync) is reconstructed from
  statement counts and one 28.0 s completion. Confirmation needs the desktop server log lines
  `Data retention sweep failed` with a `TimeoutException`/`57014`. It might instead be an
  exception thrown before pass 8's query is sent (`LoadOutboundTaskIdsAsync` runs first).
  Either way the residue, usage and audit passes are skipped.
- CARD-1583: which rc SHA the 2026-10-30 job tested, and whether a 2026-11-01 rc job ran, are
  not visible from server2. The card body is empty on every revision. Why `nightly-report.ps1`
  wrote an empty description was not investigated.
- CARD-1623/1625 need a native Windows A/B. No Windows run was made.
- The CARD-1484 item 4 C475 status relies on the CARD-1463 plan's CP-5 and on the fix's code
  shape. The direct probe result is below.

## Not done, noted

- File a card for the retention sweep: make the passes independent (per-pass try) and the
  PruneTasks eligibility query bounded (or with an explicit timeout/paging), with a witness that
  a failing pass 8 still runs passes 9-11.
