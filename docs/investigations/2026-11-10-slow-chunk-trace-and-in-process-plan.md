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
- An earlier 24-iteration refusal loop without `TempDir` took 5319ms (about 220ms per call, exit 2). A WMI sample during 8 iterations saw git processes, and their command lines were another agent's `git ls-remote` of `feat/card-task-a60a4a5e`, not children of the probe (pid 354608). A later 596-iteration scratch with no `TempDir` and no WMI sampler finished in 69.2s (p50 100ms, p95 170ms, max 973ms, all exit 2). A 12-root `TempDir` replica outside the test host was tens of milliseconds per root, and its sweep examined 0 because the 5-minute interval skipped.
- `git rev-parse --show-toplevel` on this worktree was about 150-190ms. The same command on TEMP exited 128 in about 110-140ms.
- `Get-MpPreference` failed with "Must be an administrator to view exclusions". Defender exclusions were not read and were not changed.

The scratch is not the test host. Three unmodified runs of this filter, before any snapshot change, took 14m11.428s, 14m52.806s, and 15m19.254s (TRX `UnitTestResult` on DESKTOP-KTLKPIF). A 30-iteration early return inside the real host, deleted before the witness commit, took 47.087s, about 1.57s per `Fixture()` plus `RunAsync`. Six hundred and twenty of those calls are the 14 minute body. The cost is the per-iteration host fixture, not the 90ms `ReportWriter` and not disposing 620 roots at the end. `SafeDelete.PreflightRoots` walks parents for `.git` and spawns `git ls-files` only when it finds an owner. TEMP has no `.git` ancestor, which matches the empty process list. The local before-runs did not record a phase journal: `ANTIPHON_TEST_PHASE_ROOT` alone leaves `TryOpen` disabled unless run id, group id, attempt, and suite are all set. The RC JSONL `processes` array for this test was empty.

## Choice

Optimize `CheckpointReceiptSafetyTests.malformed_collection_and_string_shapes_return_named_refusals` (filter `/*/*/CheckpointReceiptSafetyTests/malformed_collection_and_string_shapes_return_named_refusals`, chunk antiphon/all-profile-1474).

It is the slowest individual test, it runs in every full RC (its own profile because it is slow), and the repeated work is a fixture tree. Build that tree once inside one `TempDir()`, snapshot the file bytes, and restore those bytes at the start of each iteration before the mutation. The assertions stay: exit 2, output contains `refused:`, and `state.json` is unchanged by `RunAsync`. No timeout, skip, or assertion change.

Expected saving on this desktop: drop 619 of the host `Fixture()` calls at about 1.5s each. `RunAsync` and the byte restore remain. The measured result is in the section below. Fewer new temp roots also keeps the sweep index from growing by 620 entries per run.

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
| antiphon/all-profile-1475 | The 163s sibling can restore one fixture snapshot before each `Child()` | The fixture slice only. On this desktop a host `Fixture()` is about 1.5s, so about 90 calls is roughly two minutes if that sibling pays the same host cost. The `dotnet exec` stays | The process, the 4s kill bound, and the documented exits |

## Measured result

Same filter, `/*/*/CheckpointReceiptSafetyTests/malformed_collection_and_string_shapes_return_named_refusals`, on DESKTOP-KTLKPIF through `scripts/build-slot.ps1`. Before is commit `0077c8df8b047d8f910babd519d002f9f9ce18ce` (the test still called `Fixture()` every iteration). After is `1f9273f75a8da7c353c708c08f4bee2eb8a12590`. Each run executed 1 test, 0 failed. The duration is the TRX `UnitTestResult` duration. p50 is the middle of three. p95 is the slowest of three (nearest rank).

| Run | Before | After |
|---|---:|---:|
| 1 | 851.428s | 21.613s |
| 2 | 892.806s | 13.577s |
| 3 | 919.254s | 11.701s |
| p50 | 892.806s | 13.577s |
| p95 | 919.254s | 21.613s |

Process-spawn count after, from the phase journal (`processes` on the complete record, and `exclusive.process`): 0, 0, 0. Fixture teardown on those journals was 30ms, 12ms, and 15ms. Before had no local journal. The RC record for this test also had an empty `processes` array. The optimized method calls `Program.RunAsync` in-process. `Child()` on the other methods of the class still launches `dotnet exec`.

`reused_receipt_fixture_matches_a_fresh_fixture_refusal` failed at `ee77f13ae81e3ec3c588029e4bd1671ae6775f9e` because the restore omitted `report.md` (1 failed, duration 6s 981ms) and passed at `1f9273f75a8da7c353c708c08f4bee2eb8a12590` (1 passed, duration 8s 494ms).

## Round 2 (task 5301664b)

Branch `feat/card-task-5301664b` starts at the round-1 tip `9e9fed149b0ec96987df57455afe4a1ba55aab4a`. The same timing root and the same chunk TRX were read again. The top-15 chunk order and the top-30 test order above are unchanged. Host for the new timings: DESKTOP-KTLKPIF, Windows 10.0.19045, .NET 9.0.22. Every build and run went through `scripts/build-slot.ps1` with `-p:UseAppHost=false` and the worktree's default output. This class does not launch a runner.

### Setup that is paid per iteration

Of the top 30, the rows whose wall is the work under test stay as they are: the e2e crash matrices, `Windows_SamplerOverheadStaysWithinBudget`, `CheckpointRepeatBuildIsolationTests.repeat_two_then_default_then_three_rebuilds_the_same_project` (a real FilterHost build plus three stub `run-checkpoint.ps1` launches), and `PlanCoverageBindingTests.historical_plans_bind_without_suppression` (in-process `CoverageCommand.Run` plus Roslyn). The round-1 method is already one fixture.

The remaining repeated-setup rows are both in `CheckpointReceiptSafetyTests`, chunk antiphon/all-profile-1475, and both call `Child()` (`dotnet exec` of `Antiphon.Checkpoints.dll`, 4s kill bound). That process stays. The repeated cost is `Fixture()` (`TempDir`, `MarkRun`, `RunStateStore.Write`, `ReportWriter.WriteFiles`) once per mutation.

| Test | RC s | Iterations | Estimated fixture saving |
|---|---:|---:|---|
| `malformed_state_shape_by_verb_table_matches_documented_exits` | 163.16 | 90 `Child()` (2 collections x 5 shapes x 9 verbs) | about 89 x 1.5s, roughly two minutes |
| `receipt_row_and_build_identities_refuse_blank_without_writing` | (inside the same profile; four theory rows) | 34 `Child()` per row (2 receipts x 2 collections x 9 verbs, skipping `validate` on `state.json`) | about 33 x 1.5s, roughly 50s per row |
| `symlink_and_directory_receipts_refuse_within_four_seconds` | 33.03 | about 18 `Fixture()` + `Child()` | about 27s, under the 30s stop |

`Child()` does not call `TestTiming.BeginProcess`, so the phase journal `processes` array stays empty. Spawn count is a 30ms sample of `dotnet` command lines containing `Antiphon.Checkpoints.dll` and ` exec `. The source loop count is the authority when the sample misses a short process.

### Choice

Fix the two sibling loops, one at a time. Build the receipt pair once, snapshot the top-level file bytes, and `Restore` before each mutation. Assertions, documented exits, the 4s kill bound, and `ParallelLimiter<CheckpointToolSpawnLimit>` stay. `reused_state_fixture_matches_a_fresh_child_refusal` compares a fresh `Fixture()` with two restores of a second fixture (debris file and subdirectory wiped by `Restore`). It checks exit, output with the temp root replaced by `<root>`, and whether the pre-child file bytes stayed unchanged. Both sides may write on a successful verb; they have to write the same way.

Covered verbs: `status`, `wait`, and `validate` with `rows` null on `state.json`; `clean` with a blank row id; `report` rewriting `state.json`; `report-merge` rewriting `report.json`; `status` with a blank row id on `report.json`.

### State-shape result

Filter `/*/*/CheckpointReceiptSafetyTests/malformed_state_shape_by_verb_table_matches_documented_exits`. Before is `9e9fed149b0ec96987df57455afe4a1ba55aab4a` (one `Fixture()` per iteration). After is `966f20c5a69cdd64b3bbfadd469ad5ef0bd0dba7`. Each run executed 1, failed 0. Duration is the TRX `UnitTestResult`. p50 is the middle of three. p95 is the slowest of three.

| Run | Before | After |
|---|---:|---:|
| 1 | 172.931s | 52.391s |
| 2 | 178.187s | 50.782s |
| 3 | 179.275s | 52.027s |
| p50 | 178.187s | 52.027s |
| p95 | 179.275s | 52.391s |

p50 saving 126.160s. Sampled checkpoint exec 90, 90, 90 before and 90, 90, 90 after. Journal `processes` empty both ways. Before run 1 exclusive fixture teardown 863ms, remainder 172955ms. After teardown 28ms, 31ms, 32ms (remainder 52412, 50807, 52053). The witness at `70677bbe4d3533c2f8e0bfe540e4f56e3f38e7c0` passed 1, failed 0, before the loop change.

### Receipt-row result

Filter `/*/*/CheckpointReceiptSafetyTests/receipt_row_and_build_identities_refuse_blank_without_writing*`. Before is the same `966f20c5a69cdd64b3bbfadd469ad5ef0bd0dba7` binary (still one `Fixture()` per iteration). After is `78be31af5e703e10b2d88b5648d465195b05f9be`. Four theory rows, each run failed 0. Sampled checkpoint exec before 134, 136, 135 (source count 136). After 136, 136, 136.

| Argument | Before runs s | After runs s | Before p50 / p95 | After p50 / p95 | p50 saving |
|---|---|---|---|---|---:|
| `""` | 64.692, 63.011, 67.839 | 23.802, 24.049, 41.978 | 64.692 / 67.839 | 24.049 / 41.978 | 40.643s |
| `" \t\r\n"` | 65.571, 63.123, 67.322 | 27.592, 23.787, 29.439 | 65.571 / 67.322 | 27.592 / 29.439 | 37.979s |
| null | 62.457, 63.105, 73.388 | 27.385, 26.245, 29.206 | 63.105 / 73.388 | 27.385 / 29.206 | 35.720s |
| `CP-fixture` | 76.650, 76.021, 88.039 | 40.131, 42.271, 47.970 | 76.650 / 88.039 | 42.271 / 47.970 | 34.379s |

The empty-string after p95 is run 3 (41.978s) against two runs near 24s. All three passed. Journal `processes` empty. Exclusive fixture teardown before was 239-1368ms per row (34 roots). After it was 9-105ms (one root). The extended witness, including the successful `report` and `report-merge` paths, passed 1, failed 0, duration 48s 868ms, on the bytes committed as `78be31af5e703e10b2d88b5648d465195b05f9be`.

Scratch journals and TRX stay under `C:\Users\lndco\AppData\Local\Temp\5301664b-trace`. They are not committed.

### Stop

Each remaining candidate saves under 30s on one run, so round 2 stops. No timeout, skip, assertion, or coverage change. `Child()` still spawns: 90 execs in the state-shape method, 34 per receipt theory row.

## Next card-sized chunks after round 2

| Chunk | What to change | Expected saving | Leave alone |
|---|---|---|---|
| `symlink_and_directory_receipts_refuse_within_four_seconds` in the same class | One snapshot restored before each `Child()`, with the existing child witness extended to a symlink refusal | About 27s if the local measure matches the 1.5s `Fixture()`. Under the 30s line until a trace shows more | `Child()`, the 4s kill bound, the refusal text |
| antiphon/all-group-108 fixture git inside `CheckpointFixtureGitEnvironment.Git` for init/add/commit | LibGit2Sharp commit of the fixture tree, witnessed by `git rev-parse HEAD^{tree}` and `git status --porcelain` matching the CLI | 41.3s summed across the chunk, a few seconds inside each 45-80s row. Per-test saving is under 30s | `run-checkpoint.ps1`, validate-receipt pwsh, dotnet build, git whose stdout the test asserts |
| antiphon/all-group-109 | Trace the 24 dotnet processes (70.2s) before caching anything | Unknown until that trace. The 184.65s historical-plan bind is the work | `CoverageCommand.Run` and the Roslyn bind |
| antiphon/all-group-11 | In-process reads already covered by `InProcessGitReads` | 9.4s over 1285 git processes | Recovery and worktree-add commands |
