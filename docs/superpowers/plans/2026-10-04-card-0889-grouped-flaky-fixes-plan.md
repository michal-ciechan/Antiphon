# CARD-0889 grouped flaky fixes: current-state plan

Date: 2026-10-04. Plan task: `632da924-1414-49ed-bdf2-11f7c5903ba5`.
Inspected source: `bb18064ba647e0ddb03cae4da437ab60ed447d98`.
Branch: `feat/card-task-632da924` (fast-forward-only).

## Outcome and stage boundary

Five of the seven fix groups already have their intended implementation in this
checkout. The remaining source work is the three crash-worker launchers (CARD-0890)
and the C578 readiness handshake (CARD-0900). Start reconciliation with CARD-0742
and the shared CARD-0820/CARD-0751 clock driver, then preserve their implementation
while establishing which Linux/Windows evidence and post-land controls remain due.

The brief's assumption that all seven fixes still need planning from scratch is
outdated. **Next: Investigate**, narrowly to reconcile source, publication, ordinary
receipts and pending controls at the actual implementation SHAs. This is a complete
Plan deliverable; it is not authorization to implement the five fixes again. After
that reconciliation, use a separate TestDesign refresh before Code. The brief did
not fold TestDesign into this dispatch. The checkpoint table below is the proposed
ordinary roster for that refresh, not a claim that verification design is complete.

The starting [investigation](../../investigations/2026-10-02-flaky-test-root-causes.md)
matches the supplied ref `f811ba1c8c2a23be8d7a654e1f74b08b4529fda3`: its Git blob
is `e00bf61de41802b7f1564d92b9d031644eb6634a` at both that ref and inspected HEAD.
The existing [2026-10-02 Plan/TestDesign](2026-10-02-flaky-test-fixes-plan.md)
already records authorized system-default clock seams, seven slices, and 54
guard/control pairs. Preserve that historical artifact and its evidence attribution.
This plan updates present implementation status and dispatch guidance; its smaller
repeat scope supersedes the old stress prescription for new commissions. It does
not erase outstanding controls or certify any prior run.

## Ground truth

All paths and facts below were read at the inspected source. Historical failure
counts belong to the investigation, not this task. No build or test ran here.

| Group | Card/investigation assumption | What this checkout does | Consequence |
|---|---|---|---|
| S1, CARD-0742 | Snapshot assertion still sleeps 20 ms; adapter needs a clock seam. | `tests/Antiphon.Tests/Agents/RunnerCodexAdapterReadyTests.cs` uses `ControlledTimeProvider`, a registered 50 ms poll, and a held second snapshot; checks attempts/completions independently. `server/Infrastructure/Agents/SessionRunner/RunnerCodexAdapter.cs` already accepts an optional provider and defaults to System. | Implemented in `c345371e2aa0ed5c127b27f71d3199a29421122f`. Reconcile receipts first; retain one-snapshot-per-decision assertions. |
| S2, CARD-0820/CARD-0751 | Both named tests still overshoot deadlines with an asynchronous fake-clock pump. | `ResilienceBudgetTests.Slow_first_attempt_consumes_the_same_budget` records cancellation at 10 s, holds completion, and stamps the next request with the original 30 s budget. `HttpResilienceRegistrationTests.Runner_list_and_git_connectivity_keep_their_short_deadlines` uses separate controlled clocks for 3/10 s owner limits. Shared `ResilienceTestHost.AdvanceAfterAsync` checks registered timers and waits at the same virtual instant, including zero-duration advances. Legacy `Pump` remains for other tests. | Same `c345371e2` implementation. Verify the shared driver's evidence, without claiming all Pump users were repaired. |
| S3, CARD-0882 | Two Windows argv-refusal fixtures fail on Linux before reaching the policy. | `tests/Antiphon.Tests/Application/GrokRulesLaunchRefusalTests.cs` already skips off Windows at the start of `AssertNamedGrokRefused`, before fixture creation. Pure `GrokRulesArgvPolicyTests` retains explicit non-Windows behavior. | Guard exists at `da9edf0b837b81e8fdf4ccc19c8f80be3b78de75`. Linux skips do not replace required Windows passes. |
| S4, CARD-0890 | PowerShell reflection fails to load the test assembly's System.Text.Json dependency on Linux. | `AgentTaskLandRecoveryTests.C448_V15_RealWorkerDeathRecoversDurableBoundaries`, `PostLandMutationDeliveryTests.PublicationCommitCrashAsync`, and `WorktreeResidueRecoveryTests.StartRetirementWorker` still generate `pwsh`/`Assembly.LoadFrom` scripts. Two callers still mutate argument index 6 for resume. `TestWorkerModes.All` contains six modes; no land-protocol or retirement mode. | Remaining implementation. Historical C448 Linux 0/7 is sufficient motivation, but a new arbitrary failure is not proof of the dependency cause. Migrate all three callers together. |
| S5, CARD-0889 | Real-time submit polling exhausts the two-second fixture budget after three Enters. | `server/Infrastructure/Agents/CodexSubmitConfirmation.cs` already resolves `CodexSubmitOptions.Clock` to System by default and uses it for deadlines, poll delays and blind settle. All eight submit tests have controlled-clock coverage, including four-Enters-before-deadline and expired-budget-stops-repress checks. | Same `c345371e2` implementation. Preserve legitimate early termination at the deadline and the 20 s production default. |
| S6, CARD-0900 | Failed-build child can exit before the parent sees readiness; fixed ten-second gate adds load sensitivity. | `scripts/test-run-checkpoint.ps1` still constructs `New-C578Case -FailBuild` without `-Hold build`; `Wait-C578Ready` still has a ten-second deadline and tests wrapper exit first. The shim already supports hold/release. `RunCheckpointScriptTests.C578_FailedBuildKeepsLogAndExit` still expects five internal labels. | Remaining implementation. Need an owned readiness/stream/release handshake and deterministic schedule assertions, not a larger timeout. |
| S7, CARD-0757 | Scaled clock samples Stopwatch/System timers and its tests compare wall-time ratios. | `tests/Antiphon.Tests/TestHelpers/ScaledTimeProvider.cs` takes one source provider for UTC, timestamps, frequency and timers. All six tests in `ScaledTimeProviderTests.cs` use a fake source. | Helper at `6cff76eae6326654794ed68d6da8564adabcd9f2`; tests at `07b1e8fee1e43d64ffb8688bc612da7e6ea9d8a6`. Reconcile evidence; no new clock abstraction needed. |

Additional current facts affecting implementation:

- `PostLandMutationDeliveryTests.ConfirmLandReceiptAsync` can return when the queue
  ID is absent after asserting RetryPending. The old TestDesign already requires
  a positive receipt path for the selected success cases. Preserve that obligation
  when migrating the child; worker readiness alone cannot satisfy delivery coverage.
- The old collision list is dated 2026-10-02. Current read-only card responses show
  CARD-0788 and CARD-0886 in Review, CARD-0885 Done, and CARD-0890/CARD-0900 Backlog.
  Columns alone prove neither source containment nor an absence of active writers.
  CARD-0885's close record names publication `808677658cc418dc439d906de4526aaea32e231a`
  and separately disclosed CARD-0968 obligations. Do not reimpose its old blanket
  wait, or assume the other dependencies are resolved from their columns.
- Current testing policy caps optional repeat proof at three normal plus two
  loaded executions per unchanged selection, with none required after green.
  The old plan's 30/10-round rows are not the default for this continuation.

## Decisions

**D-1 — Reconcile existing work before authoring.** Keep the old S1–S7 identities
so controls and card links remain traceable. Treat S1/S2/S3/S5/S7 as implemented,
with qualification status unknown in this task. Rejected: copying the earlier
seven-slice implementation brief verbatim, equating commit presence with accepted
Windows evidence, or changing card status as a side effect of this Plan.

**D-2 — Preserve the existing shared clock seam.** S1/S2/S5 share
`tests/Antiphon.Tests/TestHelpers/ControlledTimeProvider.cs`; keep one source owner
if reconciliation exposes a defect. Wait for timer registration and causal phase
signals before advancing; stop at the asserted boundary while continuations run.
Keep System defaults, readiness decisions, four-press maximum, retry semantics,
and separate terminal Enter unchanged. Rejected: wider assertion windows, retries
to conceal failures, a new parallel clock driver, global test serialization as a
fix, or broad migration of unrelated Pump consumers.

**D-3 — Use normal dotnet dependency binding for all three crash callers.** Add
two owned modes and a shared test process helper. Launch `dotnet` with the current
test assembly's `Assembly.Location` using `ProcessStartInfo.ArgumentList`, following
`CheckCompactionFixture.Start`. The built assembly's adjacent runtimeconfig/deps
must be used. Rejected: PowerShell assembly resolution hooks, copied dependency
DLLs, Linux skips, selecting a different installed test assembly, or migrating
only the seven C448 cases while leaving adjacent callers broken.

**D-4 — Preserve child and fixture ownership.** Requests carry root/task/cut/ready;
validate root, fixture owner, confined ready path and allowed cut before opening
the parent connection. Clear all inherited owned-worker marker keys in the child
environment, then set exactly one mode. Connection strings stay in that child's
environment, never argv, logs or request JSON. Dispatch remains before shared-store
warmup; parent owns schema/repository teardown. Drain both pipes from process start.
Observe ready cut and PID against captured process identity, replace positional
resume edits with typed requests, assert resume exit, and join every owned child
in finally. The intentional crash-cut kill stays root-only; final cleanup may kill
the remaining owned tree. Rejected: global process-name cleanup, premature parent
disposal, or suppressing child failures as readiness timeouts.

**D-5 — Give C578 an acknowledged readiness handshake.** Enable the existing
failed-build hold. The parent acknowledges matching live child identity and both
flushed stream markers in the wrapper's log before releasing. Readiness, release,
log-change and exit observations use subscribed events plus immediate state rechecks
to avoid lost events; complete file contents, not notifications, are the evidence.
Use the existing outer ScriptHarness cancellation as a hang guard. Remove the local
ten-second decision from the touched readiness path; preserve elapsed/phase/identity
diagnostics. Retain all five labels and add the two old TestDesign handshake labels.
Rejected: increasing ten seconds alone, accepting log text as live-child ownership,
or modifying the production checkpoint driver to accommodate its test.

**D-6 — Keep deterministic witnesses tied to the code being tested.** Exact clock
boundaries and held-child schedules carry acceptance. Logical late-ready and
fast-exit cases must call the same test-script decision path as the real held shim;
a separate reducer never called by that path is insufficient. Existing outer
timeouts diagnose infrastructure and are not valid mutation reds. Preserve the old
guard inventory; TestDesign reconciles its current labels and scope, including
delivery receipt guards. Rejected: a full Unit stress run as the sole proof, constant
or self-comparing assertions, or treating historical failures as new baseline runs.

**D-7 — Route by lane, not by machine.** Both required platform endpoints were
read on 2026-10-04: `GET /api/runner-defaults` revision 2 has a global preference
and no kind-specific overrides; `GET /api/session-runners` showed eligible,
accepting Linux and Windows capacity and one unavailable/draining entry. These are
observations, not reservations. Normally omit `-Runner` and `-Platform`; use
`-Platform Linux`/`Windows` only for the respective explicit qualification lane,
and `-Platform Any` only to remove an inherited pin. No fleet address or host ID
is embedded in this plan. Re-read defaults/inventory, scoped pipeline occupancy and
host limits at dispatch. Windows evidence must run on Windows at the same source;
this checkout cannot supply it by inference.

## Implementation slices and files

S1/S2 remain first in the reconciliation order. The old numbering is retained,
although only S4 and S6 presently require new implementation. Commit and push
each meaningful slice; freeze source before any checkpoint run.

| Slice | Files and action | Required tests / acceptance |
|---|---|---|
| S1 CARD-0742, preserve | `tests/Antiphon.Tests/Agents/RunnerCodexAdapterReadyTests.cs`, `tests/Antiphon.Tests/Agents/ScriptedCodexRunnerClient.cs`, `tests/Antiphon.Tests/TestHelpers/ControlledTimeProvider.cs`, `server/Infrastructure/Agents/SessionRunner/RunnerCodexAdapter.cs`; verify existing first/second snapshot barriers and default seam. | `One_snapshot_is_used_for_each_startup_decision`: one completion before registered poll; two attempts/one completion while second held; exactly two completions after release. Whole class 12 results. |
| S2 CARD-0820/CARD-0751, preserve | `tests/Antiphon.Tests/Infrastructure/Resilience/ResilienceTestSupport.cs`, `tests/Antiphon.Tests/Infrastructure/Resilience/ResilienceBudgetTests.cs`, `tests/Antiphon.Tests/Infrastructure/Resilience/HttpResilienceRegistrationTests.cs`; retain explicit phase driver and retained cancellation registrations. | Named slow-first test: exact 10 s attempt and original absolute 30 s total, one terminal first attempt. Named short-owner test: 3 s runner and 10 s git cancellation, token not canceled just before each boundary. Two classes: 14 results. |
| S3 CARD-0882, preserve | `tests/Antiphon.Tests/Application/GrokRulesLaunchRefusalTests.cs`; inspect `tests/Antiphon.SessionRunner.Tests/GrokRulesArgvPolicyTests.cs` as regression coverage. | Three applicable Linux launch tests; two Windows-only refusals remain mandatory on Windows, five class results total there. Pure policy: 26 results per OS. |
| S4 CARD-0890, implement as one slice | Change `tests/Antiphon.Tests/Application/AgentTaskLandRecoveryTests.cs`, `tests/Antiphon.Tests/Application/PostLandMutationDeliveryTests.cs`, `tests/Antiphon.Tests/Application/WorktreeResidueRecoveryTests.cs`, `tests/Antiphon.Tests/TestHelpers/TestWorkerModes.cs`, and registry assertion messages in `tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycleTests.cs`. Add `tests/Antiphon.Tests/TestHelpers/LandProtocolCrashWorker.cs`, `tests/Antiphon.Tests/TestHelpers/WorktreeRetirementCrashWorker.cs`, `tests/Antiphon.Tests/TestHelpers/CrashWorkerProcess.cs`. Read `LandingSafetyHarness.cs`, `LandingRemovalCrashWorker.cs`, `CheckCompactionFixture.cs`, `TestDbFixture.cs` and `SharedStoreWarmup.cs`; do not transfer their parent lifecycle to children. | Seven C448 cuts, 12 land-delivery matrix cases, 13 retirement cuts, one registry census and eight post-migration warmup cases on each OS. Validate ownership, ready identity, durable phase, same operation/recovery state, resume exit and complete child cleanup. Receipt-required cases must reach queue and transcript assertions. |
| S5 CARD-0889, preserve | `server/Infrastructure/Agents/CodexSubmitConfirmation.cs`, `server/Infrastructure/Agents/SessionRunner/RunnerCodexAdapter.cs`, `tests/Antiphon.Tests/Agents/RunnerCodexAdapterSubmitConfirmTests.cs`, shared scripted client/clock. | Eight tests. First Enter precedes confirmation driving; registered 250 ms polls put subsequent Enters at 250/500/750 ms, then stop at 2 s. Expiry-after-third-Enter case must still forbid a fourth. Blind settle and System/null/omitted defaults remain covered. |
| S6 CARD-0900, implement | `scripts/test-run-checkpoint.ps1`, `tests/Antiphon.Tests/Scripts/RunCheckpointScriptTests.cs`. Keep script ASCII. Use hold/release and owned events, dispose registrations/watchers and release/join in finally. No source edit in `scripts/run-checkpoint.ps1`. | Existing 24 class results; failed-build method remains one result with seven internal labels (five retained plus `c578-child-held-until-observed`, `c578-late-ready-event-accepted`). Real shim exits 37, wrapper reports 2, streams persist and tests never start. Preserve streaming and interruption cases. |
| S7 CARD-0757, preserve | `tests/Antiphon.Tests/TestHelpers/ScaledTimeProvider.cs`, `tests/Antiphon.Tests/TestHelpers/ScaledTimeProviderTests.cs`. | Six results: source +100 ms at speed 10 gives exact +1 s; timer before/at boundary, offset-only Advance, speed one, cancellation and invalid speed assertions remain. |

For S4 retain the exact C448 cut set `C03,C05,C09,C12,C14,C16,C17` and `resume`.
Retirement retains `release-before,release-after,claim-before,claim-after,intent-before,
intent-after,git-exit,directory-result,registration-result,branch-cas,terminal-before,
terminal-after,run-projection` plus `resume`. Whitelists differ by mode. Parent-ready
observation subscribes before launch, immediately rechecks after subscription, and
re-reads complete JSON after notifications; also observe child exit. Readiness alone
does not certify publication, cleanup or caller receipt.

The S4 delivery chain remains real landing transaction -> durable notification ->
real queue -> fake recipient I/O -> persisted complete UserPrompt -> confirmation.
Join task, operation, notification/queue, recipient session and sequence above the
attempt floor. Require the queue ID on success-required paths instead of the current
early return. Preserve busy-caller withholding, complete-body/session/floor rejection
and no-retyping checks from the old PC-27..32 design. An inherited product failure
exposed here requires its own diagnosis, not a weakened fixture assertion.

## Reconciliation and TestDesign handoff

Investigate should measure the following, then report a bounded continuation:

1. For S1/S2/S5, S3 and S7, match contained commits to Code/Review/landing records,
   exact source SHA, Linux/Windows checkpoint receipts and any post-land companion.
   Validate source/build provenance and counts; a commit message saying reviewed
   is not a receipt. Report implemented, ordinary-qualified, published and
   mutation-qualified as separate facts. Start with the shared clock group.
2. Refresh CARD-0788/CARD-0886 publication/source containment and active writers in
   landing fixtures; refresh CARD-0885 tooling and the S6 script-fixture contract.
   Check CARD-0888 delivery changes if they now affect the S4 receipt path. Use
   board/project-scoped occupancy and exact paths, not the old blanket waits.
3. Read the remaining worker and C578 evidence at its recorded SHA. If current-base
   reproduction is needed, commission one exact C448 method filter (seven results)
   and one C578 method filter (one result). Capture whether failure occurs before
   ready, child exit/exception, log markers and PID/start identity. C448 dependency
   failure and C578 observation race must be distinguished from DB/build failure.
   These are diagnostic runs, not final-green checkpoints; pin and report them
   explicitly instead of silently expanding the ordinary table.
4. Refresh TestDesign's 54 guard/control mappings against existing source. Keep
   method-scoped original-green/compiling-defect-red/restored-green controls for
   post-land Mutation. Record pending/accepted/obsolete-with-reason per old PC ID;
   no implicit cancellation of S1/S2/S3/S5/S7 controls. Bind S4/S6 finite failure
   witnesses, early-exit/error cleanup, event-before-subscription schedules and
   updated assertion labels to the actual helper paths before Code.

TestDesign must promote the accepted roster below into a `## Verification design`
section, attach ordinary V/R and exact mutation obligations, recount dynamic marker
cases and resolve the dependency snapshot. No substantive default awaits human
approval here; the next-stage choice is driven by stale task state and missing
qualification evidence, not an invented permission gate.

### Checkpoints

Proposed ordinary scope, to be frozen by TestDesign. IDs in this table are local to
this new plan, not replacements for historical CP receipts. Each row names a lane
in Group/Expect; no unsupported `Lane` column is added to the importer schema.
`all` means the final reconciled implementation SHA including S4/S6. Linux selects
CP-1,2,3,4,5,7,8,9,10,11,12,13. Windows selects the same list with CP-6 replacing
CP-5. Run one resolved selection per OS, serially. Expected-result text does not
automatically filter platforms.

All Antiphon.Tests rows can reuse CP-1 only within the same run, OS, `After` group
and committed source, with verified build stamp; CP-7 builds its distinct project.
For a separately commissioned S4 or S6 slice, TestDesign must first give that
subset its own build row and `After` value. Never run a reuse row without its
matching build. No whole Unit/namespace lane is required by this bounded change.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c889-final/` | both-ready | `/*/*/RunnerCodexAdapterReadyTests*/*` | S1 | Linux and Windows: 12, 0 failed/skipped | 12 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | all | CP-1 | both-resilience | `/*/*/(ResilienceBudgetTests*)\|(HttpResilienceRegistrationTests*)/*` | S2 | Linux and Windows: 14, 0 failed/skipped | 14 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | all | CP-1 | both-submit | `/*/*/RunnerCodexAdapterSubmitConfirmTests*/*` | S5 | Linux and Windows: 8, 0 failed/skipped | 8 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | all | CP-1 | both-scaled | `/*/*/ScaledTimeProviderTests*/*` | S7 | Linux and Windows: 6, 0 failed/skipped | 6 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | all | CP-1 | linux-grok-launch | `/*/*/GrokRulesLaunchRefusalTests*/(Cold_grok_delegate_keeps_full_composed_bundles_in_typed_payload)\|(Named_grok_agent_with_a_single_line_append_composes_the_rendered_line_byte_identical)\|(Over_budget_single_line_composition_still_throws_invalid_operation)` | S3 | Linux only: 3, 0 failed/skipped | 3 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | all | CP-1 | windows-grok-launch | `/*/*/GrokRulesLaunchRefusalTests*/*` | S3 | Windows only: 5, 0 failed/skipped | 5 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | all | `tests/Antiphon.SessionRunner.Tests -> bin-c889-policy/` | both-grok-policy | `/*/*/GrokRulesArgvPolicyTests*/*` | S3 | Linux and Windows: 26, 0 failed/skipped | 26 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | all | CP-1 | both-land-worker | `/*/*/AgentTaskLandRecoveryTests*/C448_V15_RealWorkerDeathRecoversDurableBoundaries` | S4 | Linux and Windows: all 7 cuts, 0 failed/skipped | 7 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-9 | all | CP-1 | both-land-delivery | `/*/*/PostLandMutationDeliveryTests*/C478_V09a_LandCrashMatrix` | S4 | Linux and Windows: 12, 0 failed/skipped | 12 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-10 | all | CP-1 | both-retirement | `/*/*/WorktreeResidueRecoveryTests*/C459_WorkerDeathAtEveryRetirementHandoff` | S4 | Linux and Windows: all 13 cuts, 0 failed/skipped | 13 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-11 | all | CP-1 | both-worker-registry | `/*/*/TestDbFixtureLifecycleTests*/Worker_mode_list_names_every_owned_child_worker` | S4 | Linux and Windows: 1, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-12 | all | CP-1 | both-worker-warmup | `/*/*/TestDbFixtureLazyInitializationTests*/A_worker_child_exits_before_the_shared_store_warmup` | S4 | Linux and Windows: 8 modes after migration, 0 failed/skipped | 8 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-13 | all | CP-1 | both-c578-harness | `/*/*/RunCheckpointScriptTests*/*` | S6 | Linux and Windows: 24, 0 failed/skipped; named internal labels also required | 24 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Source census at this Plan: readiness 10 methods/12 results; resilience 7+7;
submit 8; scaled 6; Grok launch 5, policy 26; C448 selected 7; delivery selected 12;
retirement selected 13; registry 1; warmup currently 6, projected 8; script class 24.
Linux final roster is **134 results**, Windows **136**. C578's internal labels and
worker cleanup checks do not add TUnit results. The two deliberate Linux policy
skips may be inspected diagnostically; they are neither passing executions nor
part of the final zero-skip Linux certificate.

### Execution constraints and cost

After TestDesign freezes its table, Code uses the checkpoint tool's
`run --plan <this-plan> --rows <resolved-selection> --expected-source-sha <sha>`
once per committed group, then waits through exit 75 until completion. A tool
bootstrap build, if needed, takes `scripts/build-slot.ps1`; checkpoint rows obtain
their own slots. All other build/test drivers also take that gate. Slot timeout
exit 4 is not permission to run unleased. Use TUnit via `dotnet run`, never
`dotnet test`; preserve the per-assembly process limiter and do not co-schedule
Antiphon.Tests with Antiphon.Agents.Pty.Tests. No actual provider is required.

Preserve unedited CHECKPOINT lines, source/build provenance, platform, roster and
passed/failed/skipped counts. Validate strict receipts against their exact SHA.
No source edits during a run. If a row fails, establish the same exact-method
failure at the recorded base before attributing it; do not run an assembly to
establish inheritance. New changes/failures may justify rerunning affected rows;
otherwise stop after the frozen ordinary scope is green. Generated logs/TRX/JSON
stay ignored. Code/Review run `scripts/check-evidence-diff.ps1` over the full task
range. Remove only task-owned alternate outputs through the documented checkpoint
cleanup after evidence is retained; use forward-slash `bin-.../` paths.

No new 24-burner battery is required. Any extra loaded proof needs a named unresolved
flake, method filter, recorded ambient load, owned-process cleanup and the current
repeat limit (at most three normal plus two loaded total proof executions per
unchanged selection). Do not resurrect the old 30-round table or synthetic baseline
adapters as clean-source evidence. Post-land Mutation remains separately commissioned
at its landed SHA, with method-scoped controls and external restoration evidence.

Planning estimates, not measurements: the literal table is **70 minutes**, with
**67 minutes per resolved OS**, **134 minutes** across both. S4's selected tests
account for 40 minutes per OS before any dedicated subset build; S6 accounts for
7. Source authoring for the two remaining groups is approximately 6–12 engineer
hours. Reconciliation and TestDesign refresh add approximately 2–4 hours. Optional
baseline/stress and Mutation are excluded from that ordinary floor. TestDesign must
recompute a separate Mutation floor from unresolved controls; the old estimate
cannot be silently reused after evidence reconciliation. No build/test duration
was measured in this Plan task.

## Completion criteria and exclusions

The continuation is ready for Code when the two remaining slice scopes are collision
free, existing qualification obligations are reconciled, and TestDesign has frozen
ordinary and mutation coverage. Final ordinary acceptance requires both OS receipts,
all seven C448 cuts, all thirteen retirement cuts, real land-queue receipt assertions,
correct C578 failed-build exit/log behavior and no owned child residue. Existing
production seams need no further settings or transport change.

CARD-0820 temp-root contention, CARD-0863 Unix NUL transport, unrelated Pump users,
provider behavior, product landing algorithms, database migrations and checkpoint
driver redesign are excluded. There is no deployment or stack restart in this
task. Publication, activation and post-land verification remain separate outcomes.

## Appended TestDesign refresh (2026-10-04)

The [verification companion](2026-10-04-card-0889-grouped-flaky-fixes-test-design.md)
adds the inspection, delivery inventory, V/R assertions, guard/control inventory,
checkpoint selection and cost without rewriting this fix design. It preserves the
five published groups and all 54 outstanding controls. **Next: Plan**, to resolve
its N1–N4 finite-observation/cancellation seams before Code; this document's earlier
proposed checkpoint table is not an executable handoff. The companion records the
fresh exact-path occupancy check and the conditions for a subsequent Code dispatch.
