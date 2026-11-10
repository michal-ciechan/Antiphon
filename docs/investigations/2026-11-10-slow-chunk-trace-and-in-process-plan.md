# Slow RC chunk trace and in-process plan (2026-11-10)

Run `dce78ee0a35a4b08be30d8e0e8ef0295`, candidate `rc-2026-11-14-7bee43614495`, source `7bee43614495293afbae1b0f4add019d11dacb9c`. Timing root `C:\Antiphon\releases\candidates\rc-2026-11-14-7bee43614495\logs\timing-dce78ee0a35a4b08be30d8e0e8ef0295`. Chunk logs `logs\2026-11-09-1306-dce78ee0`. 1141 timing sidecars, 1139 planned groups, 25889 summary tests. Summary `complete=false` with quality `stale-input-not-joined`, `conflicting-duplicate`, `torn-tail`, `missing-terminal-remains-incomplete`, `outcome-disagreement`, `missing-phase-unknown`. Class names were joined from `groups-planned-*.json` on the planned test id. A few e2e rows below have an empty class because the TRX method text did not match a planned id; the C488 matrices belong to `AgentTaskLandDeliveryE2ETests`.

Chunk wall is the sidecar `runWallSeconds` (stopwatch around the chunk process). `buildSeconds=131.056` is the same suite qualification build copied onto all 1141 chunks (`buildSlot=not-used` on the chunk sidecar), so it is not per-chunk waste. Waste is wall minus the sum of test durations. Parallel tests make that sum larger than the wall, so waste is negative. Negative waste is left negative. `discoverySeconds` is null on every sidecar (unknown). `fixtureTeardownSeconds` is null on all 1141. `fixtureSetupSeconds` is null on 866/1141. `processSpawnSeconds` is null on 903/1141. A null phase is unknown, not zero. Process-spawn and process-wait figures are sums of nested spans. They are not a partition of the wall.

## Top 15 chunks by wall

| # | Chunk | Wall s | Waste s | Test sum s | n | Notable phases |
|---|---|---:|---:|---:|---:|---|
| 1 | session-runner/all-group-4 | 1801.3 | 1262.3 | 539.0 | 119 | why=timeout. setup/spawn/discovery unknown. p50 0.154, p95 10.782, max 175.879 (`WorkspaceSourceVerificationWireTests.C1065_LocalSourceModesVerifyFreshAtRelease`) |
| 2 | antiphon/all-group-19 | 1288.8 | 15.7 | 1273.1 | 17 | setup 4.475. spawn unknown. max 180.7. `ChannelOutboundUnifiedTransportTests` |
| 3 | antiphon/all-group-130 | 1232.8 | -46.7 | 1279.5 | 396 | setup 3.873. spawn unknown. max 72.6 (`C1336_NightlyCandidateIdentity`). Scripts harness |
| 4 | antiphon/all-group-108 | 1153.9 | -1688.0 | 2841.9 | 190 | spawn 936.094, wait 353.848. setup unknown. p50 4.501, p95 66.621, max 110.849. Checkpoint fixtures |
| 5 | antiphon/all-group-28 | 904.7 | 14.7 | 890.0 | 84 | setup 4.692. spawn unknown. max 93.4. Park fingerprint, channel crash, Codex startup |
| 6 | antiphon/all-profile-1474 | 863.6 | 9.3 | 854.3 | 1 | spawn/setup unknown. The single test below at 854.265s |
| 7 | antiphon/all-group-125 | 826.6 | 6.6 | 820.0 | 26 | max 119.2. `RunCheckpointScriptTests` |
| 8 | antiphon/all-group-132 | 785.7 | 12.3 | 773.4 | 128 | setup 3.801, spawn 142.035. max 100.9. Script classes |
| 9 | antiphon/all-group-128 | 738.8 | 5.4 | 733.4 | 24 | max 141.7. `RunCheckpointSourceScriptTests` and siblings |
| 10 | antiphon/all-group-131 | 719.7 | -20.9 | 740.6 | 218 | spawn 23.449 |
| 11 | antiphon/all-group-45 | 685.6 | 13.1 | 672.5 | 15 | setup 3.971. max 182.8 (`DelegateBranchDeliveryTests.C1213_QueuedBranchRecovery`) |
| 12 | antiphon/all-profile-2156 | 651.8 | 4.0 | 647.7 | 4 | max 170.3. Four `RollingVolumeRecycleScriptTests` C1008 rows |
| 13 | antiphon/all-group-126 | 639.1 | 5.7 | 633.4 | 37 | max 109.8. Docker test command, evidence deletion, selection |
| 14 | session-runner/all-profile-15 | 566.5 | 1.3 | 565.2 | 1 | `C1514HostLoadNativeOverheadTests.Windows_SamplerOverheadStaysWithinBudget` 565.2s |
| 15 | antiphon/all-group-48 | 530.3 | -183.0 | 713.3 | 47 tests, sidecar testCount 0 | why=timing-read-refused. `C0824RefOnlyDeliverableTests`, `CheckpointSourceApprovalTests`, `PipelineSpeedIntegrationTests` |

## Top 5 phase split

Startup is not a sidecar field. Discovery and teardown are unknown for all five. The body column is the sum of test durations.

| Chunk | Wall s | Fixture setup s | Discovery | Test body sum s | Teardown | Other recorded |
|---|---:|---:|---|---:|---|---|
| session-runner/all-group-4 | 1801.3 | unknown | unknown | 539.0 | unknown | phase=timeout. The 1262s waste is the watchdog around a hung group, not a measured startup |
| antiphon/all-group-19 | 1288.8 | 4.475 | unknown | 1273.1 | unknown | spawn unknown. Body is the wall |
| antiphon/all-group-130 | 1232.8 | 3.873 | unknown | 1279.5 | unknown | spawn unknown. Body overlaps the wall |
| antiphon/all-group-108 | 1153.9 | unknown | unknown | 2841.9 | unknown | nested process-spawn 936.094s, process-wait 353.848s |
| antiphon/all-group-28 | 904.7 | 4.692 | unknown | 890.0 | unknown | spawn unknown. Body is the wall |

## Top 30 slowest tests

| s | Suite/group | Class | Method |
|---:|---|---|---|
| 854.26 | antiphon/all-profile-1474 | CheckpointReceiptSafetyTests | malformed_collection_and_string_shapes_return_named_refusals |
| 565.24 | session-runner/all-profile-15 | C1514HostLoadNativeOverheadTests | Windows_SamplerOverheadStaysWithinBudget |
| 397.79 | antiphon/all-group-107 | CheckpointRepeatBuildIsolationTests | repeat_two_then_default_then_three_rebuilds_the_same_project |
| 303.68 | e2e/all-split-9 | (join missed; C488 matrix) | C488_ReviewDeliveryCrashMatrix_Batch4(before-typing) |
| 226.17 | e2e/all-split-54 | DispatchBaseWarningDeliveryE2ETests | C540_PreTypingCrashRecoversWarnings |
| 222.30 | e2e/all-split-7 | (join missed; C488 matrix) | C488_ReviewDeliveryCrashMatrix_Batch2(enqueue-exception) |
| 220.13 | e2e/all-split-35 | (join missed; C488 matrix) | C488_ReviewDeliveryCrashMatrix_Batch5(prompt-before-verdict) |
| 218.31 | e2e/all-split-53 | DispatchBaseWarningDeliveryE2ETests | C540_PreEnqueueWaitSurvivesSlowMaterialization |
| 216.31 | e2e/all-split-12 | (join missed; C488 matrix) | C488_ReviewToLandReceiptMatrix_Batch3(busy, Apply) |
| 214.77 | e2e/all-split-11 | (join missed; C488 matrix) | C488_ReviewToLandReceiptMatrix_Batch2(idle, spill) |
| 210.19 | e2e/all-split-10 | (join missed; C488 matrix) | C488_ReviewToLandReceiptMatrix_Batch1(idle, raw) |
| 209.71 | e2e/all-split-36 | AgentTaskLandDeliveryE2ETests | C488_ReviewEvidenceCrashRecovers |
| 209.02 | e2e/all-split-8 | (join missed; C488 matrix) | C488_ReviewDeliveryCrashMatrix_Batch3(queue-inserted-before-outbox-link) |
| 207.33 | e2e/all-split-6 | (join missed; C488 matrix) | C488_ReviewDeliveryCrashMatrix_Batch1(settlement-before-save) |
| 198.09 | e2e/all-split-11 | (join missed; C488 matrix) | C488_ReviewToLandReceiptMatrix_Batch2(busy, raw) |
| 196.28 | e2e/all-split-9 | (join missed; C488 matrix) | C488_ReviewDeliveryCrashMatrix_Batch4(idle-lost-wakeup) |
| 189.08 | antiphon/all-group-57 | CodexCliObservationGapTests | C1029_Per_kind_receipts |
| 187.59 | e2e/all-split-51 | DispatchBaseWarningDeliveryE2ETests | C540_PostPromptCrashDoesNotRetype |
| 187.58 | e2e/all-split-6 | (join missed; C488 matrix) | C488_ReviewDeliveryCrashMatrix_Batch1(settlement-before-commit) |
| 186.54 | e2e/all-split-31 | (join missed; C488 matrix) | C488_ApprovalOutcomeReceiptMatrix(stale-A) |
| 184.65 | antiphon/all-group-109 | PlanCoverageBindingTests | historical_plans_bind_without_suppression |
| 184.09 | e2e/all-split-16 | (join missed; C494 matrix) | C494_ReviewReceiptRejectsFalseEvidence_Batch4(old-sequence) |
| 182.95 | e2e/all-split-8 | (join missed; C488 matrix) | C488_ReviewDeliveryCrashMatrix_Batch3(queue-committed-before-wakeup) |
| 182.79 | antiphon/all-group-45 | DelegateBranchDeliveryTests | C1213_QueuedBranchRecovery |
| 181.12 | e2e/all-split-34 | AgentTaskLandDeliveryE2ETests | C488_ApprovalReceiptSaveFailureNeverRetypes |
| 180.72 | antiphon/all-group-19 | ChannelOutboundUnifiedTransportTests | C519_Queue_to_adapter |
| 179.99 | e2e/all-split-13 | (join missed; C494 matrix) | C494_ReviewReceiptRejectsFalseEvidence_Batch1(Sent-only) |
| 179.94 | e2e/all-split-7 | (join missed; C488 matrix) | C488_ReviewDeliveryCrashMatrix_Batch2(queue-before-commit) |
| 179.46 | e2e/all-split-14 | (join missed; C494 matrix) | C494_ReviewReceiptRejectsFalseEvidence_Batch2(wrong-session) |
| 177.93 | e2e/all-split-10 | (join missed; C488 matrix) | C488_ReviewToLandReceiptMatrix_Batch1(idle, Apply) |

## Where the processes are

JSONL complete records for the high-spawn chunks name the exe. Across every chunk directory, recorded git spawn is 301.6s over 32894 processes (about 9ms each). The slow git bucket is all-group-108.

all-group-108 (`chunk-8ccf38d553257889512ab3729b85ed16e506a2597fa8bcf31c281dc2b56237c4`, 190 completes, 0 failures) joined each `process-spawn` phase to the process record in order:

| Exe | Count | Spawn seconds | What it is |
|---|---:|---:|---|
| pwsh | 66 | 851.2 | `scripts/run-checkpoint.ps1` and `validate-checkpoint-receipt.ps1` from the shape and baseline fixtures. argc 17 is 790.9s |
| git | 371 | 41.3 | `checkpoint-fixture` only. argc 3 is 19.1s, argc 2 is 13.1s, argc 10 (the fixture commit) is 8.2s |
| dotnet | 5 | 38.7 | Output-confinement builds |
| dotnet.exe | 1 | 4.9 | Output-confinement build |

Per class, spawn seconds: `CheckpointShapeConflictTests` pwsh 329.1 + git 24.7; `CheckpointTrackedBaselineTests` pwsh 326.4 + git 7.8; `CheckpointOutputConfinementTests` pwsh 170.3 + dotnet 43.6 + git 3.3. The pwsh and dotnet rows are the script-versus-tool comparison and the build under test. They stay out of process. The 41.3s of fixture git is real and is the DesktopLegacyWorld shape, and it is a few seconds of a chunk whose wall is the build.

Other spawn-heavy chunks, from the same join:

- all-group-132: pwsh 142.0s, category `cli` plus six `checkpoint-fixture`. Volume retirement, live lane, and rc journal scripts.
- all-profile-2153 and the neighbouring C1008/C1087 profiles: pwsh `cli`, 80.6s of an 84.5s test (`RollingVolumeRecycleScriptTests.C1008_Busy_routed_and_land_in_flight_refuse`). The script is the subject.
- all-group-109: dotnet 70.2s over 24 processes, plus one `cmd.exe`. The 184.7s row is `PlanCoverageBindingTests.historical_plans_bind_without_suppression`.
- all-group-117: git 806 processes, 8.3s, category `process`, plus process-wait 170.6s on the sidecar. Landing and repository-recovery tests. Git there is the behaviour under test.
- all-group-11: git 1285 processes, 9.4s, no other exe.

all-profile-1474 JSONL has one complete record, `CheckpointReceiptSafetyTests.malformed_collection_and_string_shapes_return_named_refusals`, duration 859.9s, `processes` empty. The diag `log_261109205413487.diag` shows the body at 14m14.265s and three `CHECKPOINT TEMP` lines (examined 458/512/512, elapsed 421/471/403 ms). Those sweeps are about 1.3s.

## Desktop trace of the 854s test

The test loops 620 times (45 fields x 3 shapes x 4 verbs, plus the empty-required-string subset, plus 24 collection variants). Each iteration calls `Fixture()`, which calls `TempDir()` (new `c723-*` root, marker, sweep index, `SweepOnce`) and then `MarkRun`, `RunStateStore.Write`, and `ReportWriter.WriteFiles`, then mutates one JSON field and calls `Antiphon.Checkpoints.Program.RunAsync` in-process. The sibling `malformed_state_shape_by_verb_table_matches_documented_exits` (163.16s, all-profile-1475) uses `Child()`, which is `dotnet exec` of the checkpoints assembly with a 4s kill bound. That process is the subject.

Scratch probe under `C:\Users\lndco\AppData\Local\Temp\e1fa7e11-trace` (not committed), quiet desktop, TEMP `C:\Users\lndco\AppData\Local\Temp`, no `.git` ancestor of TEMP:

- 20 fresh rebuilds of the same receipt, then one `status` call: ownership about 15-26ms, state write about 34-50ms, report write about 26-43ms, fresh build p50 about 93ms, `status` p50 11ms, exit 0.
- Restoring the five golden files (`ownership.json`, `report.json`, `report.md`, `state.history.jsonl`, `state.json`, 9372 bytes) took 1ms, and the `status` output matched the fresh tree on all 20 iterations (exit 0, `same=True`).
- An earlier 24-iteration refusal loop without `TempDir` took 5319ms (about 220ms per call, exit 2). A WMI sample during 8 iterations saw git processes, and their command lines were another agent's `git ls-remote` of `feat/card-task-a60a4a5e`, not children of the probe (pid 354608).
- `git rev-parse --show-toplevel` on this worktree was about 150-190ms. The same command on TEMP exited 128 in about 110-140ms.
- `Get-MpPreference` failed with "Must be an administrator to view exclusions". Defender exclusions were not read and were not changed.

So on this quiet desktop one iteration is about 0.1s of receipt construction plus about 0.02s of in-process `RunAsync`, and it does not spawn git. The RC's 1.38s per call (854.265 / 620) was about six times the quiet loop. The three printed sweeps do not account for the gap. Rebuilding the tree 620 times, each time also registering a new sweep root, is the work this test repeats. `SafeDelete.PreflightRoots` walks parents for `.git` and spawns `git ls-files` only when it finds an owner. TEMP has no `.git` ancestor, which matches the empty process list.

## Choice

Optimize `CheckpointReceiptSafetyTests.malformed_collection_and_string_shapes_return_named_refusals` (filter `/*/*/CheckpointReceiptSafetyTests/malformed_collection_and_string_shapes_return_named_refusals`, chunk antiphon/all-profile-1474).

It is the slowest individual test, it runs in every full RC (its own profile because it is slow), and the repeated work is a fixture tree. Build that tree once inside one `TempDir()`, snapshot the file bytes, and restore those bytes at the start of each iteration before the mutation. The assertions stay: exit 2, output contains `refused:`, and `state.json` is unchanged by `RunAsync`. No timeout, skip, or assertion change.

Expected saving on this desktop: drop about 619 rebuilds at about 90ms each (about 56s) plus 619 `TempDir` registrations. `RunAsync` remains (about 11ms on a healthy `status`, higher on a refusal). A quiet full run is therefore a small number of minutes rather than the RC's 14m14s, if this desktop's per-call cost holds for all 620 shapes. The RC's extra 6x is not reproduced here, so the before/after numbers below are this desktop's wall, not a promise about the next RC's multiplier. Fewer new temp roots also keeps the sweep index from growing by 620 entries per run.

Rejected for this change, with the reason:

- session-runner/all-group-4: the wall is a watchdog timeout.
- `Windows_SamplerOverheadStaysWithinBudget`: the duration is the measurement.
- client/all and scripts/all: no TRX, so there is nothing to attribute.
- e2e C488/C540 matrices and `RollingVolumeRecycleScriptTests`: the process or the script is the subject.
- `ChannelOutboundUnifiedTransportTests`: the body is the wall and spawn is unknown.
- all-group-108 fixture git: 41.3s summed, inside tests whose 45-80s rows are `run-checkpoint.ps1` and `dotnet build`. Converting those launches would change the test. `CheckpointFixtureProcess` keeps installed Git on purpose (CARD-1199, git hardening off).
- `Child()` on the 163s sibling: `dotnet exec` is the refusal path under test.

## Phase 2 plan

1. Witness `reused_receipt_fixture_matches_a_fresh_fixture_refusal` in the same class. It applies one mutation (`report.json` / `runId` / `null` / `status`) to a fresh `Fixture()` and to a snapshot restored onto a second fixture, and fails when the exit or the refusal text differs, or when `RunAsync` changes `state.json` on either side. A restore that drops a required receipt file fails this test.
2. Switch the 620-iteration method to one `Fixture()`, one snapshot, and a restore before each mutation. Delete files the previous iteration added that were not in the snapshot, then write the golden bytes back, then mutate. Subdirectories created by a refusal are removed the same way.
3. Leave `Child()`, `CheckpointFixtureProcess`, pwsh checkpoint scripts, dotnet builds, pty hosts, and real git CLI parity on the process path.
4. Measure the same filter three times before the code change and three times after, through `scripts/build-slot.ps1`. Report p50 and p95 of the wall, and the process-spawn count (expected 0 both ways, because this method already calls `RunAsync` in-process). Phase timing root for those runs is a scratch directory, not the RC tree.

### Checkpoints

| Id | Build | Filter |
|---|---|---|
| CP-1 | `dotnet build tests/Antiphon.Tests` via build-slot, default output | (build only) |
| CP-2 | same output, `--no-build` | `/*/*/CheckpointReceiptSafetyTests/reused_receipt_fixture_matches_a_fresh_fixture_refusal` |
| CP-3 | same output, `--no-build` | `/*/*/CheckpointReceiptSafetyTests/malformed_collection_and_string_shapes_return_named_refusals` |

No `-o`, `--output`, or `OutputPath`. TUnit via `dotnet run --project tests/Antiphon.Tests`. The test host must not target the production runner. This class does not launch a runner.

## Next card-sized chunks

| Chunk | What to change | Expected saving | Leave alone |
|---|---|---|---|
| antiphon/all-group-108 fixture git inside `CheckpointFixtureGitEnvironment.Git` for init/add/commit only | LibGit2Sharp commit of the fixture tree, with a witness that `git rev-parse HEAD^{tree}` and `git status --porcelain` match the CLI under the isolated empty global/system config | About 41s of summed git spawn (24.7s of it in `CheckpointShapeConflictTests`). Chunk wall moves by less than that, because the rows run together and the pwsh build remains | `run-checkpoint.ps1`, validate-receipt pwsh, dotnet build, and any git whose stdout the test asserts |
| antiphon/all-group-109 dotnet rows | Trace the 24 dotnet processes (70.2s) inside `PlanCoverageBindingTests` / census. Cache only a restore that the assertion does not observe | Up to about 70s if they are repeated restores of one tree. Zero if each build is the assertion | The 184s historical-plan bind if its time is reading plans, which the spawn total says is most of it |
| antiphon/all-group-11 | 1285 recorded git processes at 9.4s | About 9s, and a large drop in spawn count if the calls are rev-parse/status already covered by `InProcessGitReads` | Recovery and worktree-add commands |
| antiphon/all-profile-1475 | The 163s sibling can restore one fixture snapshot before each `Child()` | The fixture slice only (about 90ms x 90 calls, under 10s). The `dotnet exec` stays | The process, the 4s kill bound, and the documented exits |
