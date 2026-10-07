# CARD-1125 / CARD-1128 Code report (task 8e1adcbb)

S1 and S2 are implemented and ordinary CP-1..CP-9 are green. The Unit lane was not run: the brief defines ordinary scope as CP-1..CP-9, the plan's named regression classes (already those rows), and the registry guard. No AppHost restart. Landing owner is this Code task.

## Placement

- Branch `feat/card-task-8e1adcbb`, worktree `/work/worktrees/task-8e1adcbb`.
- S1 `9567f70e4610aa750b3a331d02fe35f9481fa759` test(CARD-1125): pin Pending and Unavailable mirror-removal SHAs.
- S2 `82a5217c3a34c2e91fadb28b9c66eda80006984a` test(CARD-1128): prove Program injects both sync-debt sweeps. Ordinary rows ran at this SHA.
- origin/master `d28c7caf4cb3f3df422a90a3bb9c20738ae9d054`. `git merge-tree --write-tree --name-only origin/master HEAD` exit 0 (clean). Land rebases; this branch was not rebased.
- Evidence guard `scripts/check-evidence-diff.ps1 -BaseRef 4d3a80b456550d198c41082103a816687c852b42 -HeadRef 82a5217c3a34c2e91fadb28b9c66eda80006984a`: commits=2 entries=0 violations=0 exit 0.

## Production edit

Only `server/Application/Services/AgentTaskDispatcher.cs` lines 118-120, beside `Db`:

```csharp
/// <summary>CARD-1128: reports the dependencies received by this dispatcher; tests only.</summary>
internal (bool BlockedTaskSync, bool SettlementSync) SyncDebtSweepsWired =>
    (_blockedTaskSync is not null, _settlementSync is not null);
```

The getter reads `_blockedTaskSync` and `_settlementSync`. No other production edit. No migration. Existing assertions were not weakened or deleted.

## Files

- `tests/Antiphon.Tests/Application/PhoneHomeRollingRunnerTests.cs`: `C1125_PendingRemovalUsesObservedSha`, `C1125_UnavailableRemovalUsesWorktreeBaseSha`, `C1125_PendingRemovalPrefersConfirmedSha`. Helper takes the expected SHA and does not recompute the fallback. Existing residue test unchanged.
- `tests/Antiphon.Tests/Application/DispatcherSweepLifetimeRegistrationTests.cs`: `Program_wires_both_sync_debt_sweeps_into_the_dispatcher`. Two factory scopes, each flag asserted with a message naming the missing service, then both services resolved in those scopes. Existing singleton test unchanged.

## Checkpoints

Driver bootstrap: `scripts/build-slot.ps1 -Label c1125-c1128-checkpoint-bootstrap`, slot=granted waited=0s held=10s, `dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1125-driver/ --property:UseAppHost=false`, exit 0, 9s. Runs used `dotnet exec` of that DLL and were not wrapped in a second slot. Each row slot=granted waited=0s dirty=0 buildSource=verified. Fresh TRX counters matched the lines (total=1 executed=1 passed=1 failed=0 notExecuted=0) and each roster was the one named method.

Run `20261007-161534-67b8` `--after S1` expected SHA `9567f70e4610aa750b3a331d02fe35f9481fa759`, wall 9m07s, 7 green 0 red, exit 0.

CHECKPOINT CP-1 commit=9567f70e4610aa750b3a331d02fe35f9481fa759 build=ok filter=/*/*/PhoneHomeRollingRunnerTests/C1125_PendingRemovalUsesObservedSha* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-8e1adcbb/.antiphon/checkpoints/20261007-161534-67b8/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=9567f70e4610aa750b3a331d02fe35f9481fa759 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=9567f70e4610aa750b3a331d02fe35f9481fa759 build=reused filter=/*/*/PhoneHomeRollingRunnerTests/C1125_UnavailableRemovalUsesWorktreeBaseSha* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-8e1adcbb/.antiphon/checkpoints/20261007-161534-67b8/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=9567f70e4610aa750b3a331d02fe35f9481fa759 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=9567f70e4610aa750b3a331d02fe35f9481fa759 build=reused filter=/*/*/PhoneHomeRollingRunnerTests/C1125_PendingRemovalPrefersConfirmedSha* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-8e1adcbb/.antiphon/checkpoints/20261007-161534-67b8/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=9567f70e4610aa750b3a331d02fe35f9481fa759 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=9567f70e4610aa750b3a331d02fe35f9481fa759 build=reused filter=/*/*/PhoneHomeRollingRunnerTests/Retirement_mirror_remove_uses_confirmed_sha_and_keeps_unpublished_tip_as_residue* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-8e1adcbb/.antiphon/checkpoints/20261007-161534-67b8/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=9567f70e4610aa750b3a331d02fe35f9481fa759 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=9567f70e4610aa750b3a331d02fe35f9481fa759 build=reused filter=/*/*/PhoneHomeRollingRunnerTests/Settlement_publishes_unpushed_mirror_through_operation_32_and_runner_dispatcher* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-8e1adcbb/.antiphon/checkpoints/20261007-161534-67b8/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=9567f70e4610aa750b3a331d02fe35f9481fa759 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=9567f70e4610aa750b3a331d02fe35f9481fa759 build=reused filter=/*/*/PhoneHomeRollingRunnerTests/Runner_without_publish_feature_never_receives_operation_32* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-8e1adcbb/.antiphon/checkpoints/20261007-161534-67b8/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=9567f70e4610aa750b3a331d02fe35f9481fa759 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=9567f70e4610aa750b3a331d02fe35f9481fa759 build=reused filter=/*/*/PhoneHomeRollingRunnerTests/Input_to_a_session_on_server2_reaches_server2_while_a_new_launch_goes_to_server2_temp* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-8e1adcbb/.antiphon/checkpoints/20261007-161534-67b8/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=9567f70e4610aa750b3a331d02fe35f9481fa759 sourceState=clean buildSource=verified

Run `20261007-162514-d34c` `--after S2` expected SHA `82a5217c3a34c2e91fadb28b9c66eda80006984a`, wall 4m04s, 2 green 0 red, exit 0.

CHECKPOINT CP-8 commit=82a5217c3a34c2e91fadb28b9c66eda80006984a build=ok filter=/*/*/DispatcherSweepLifetimeRegistrationTests/Program_wires_both_sync_debt_sweeps_into_the_dispatcher* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-8e1adcbb/.antiphon/checkpoints/20261007-162514-d34c/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=82a5217c3a34c2e91fadb28b9c66eda80006984a sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=82a5217c3a34c2e91fadb28b9c66eda80006984a build=reused filter=/*/*/DispatcherSweepLifetimeRegistrationTests/Program_registers_the_sweep_in_flight_state_as_one_singleton* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-8e1adcbb/.antiphon/checkpoints/20261007-162514-d34c/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=82a5217c3a34c2e91fadb28b9c66eda80006984a sourceState=clean buildSource=verified

## Registry guard (brief, outside the closed CP table)

One isolated `tests/Antiphon.Tests` build to `bin-c1125-registry/` through build-slot, label `c1125-registry-build`, slot=granted waited=0s held=187s, exit 0, 3m06s. Then `--no-build` filter `/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/*`, label `c1125-registry-guard`, slot=granted waited=0s held=14s, exit 0. TRX `/tmp/c1125-registry/run.trx`: total 3, succeeded 3, failed 0, skipped 0.

## Local mutation proof (reverted, not a SourceLanding discharge)

Each control was applied, rebuilt through build-slot into the same isolated output, run with the plan's method filter, seen red (1 executed, 1 failed, 0 skipped), then `git checkout` restored the file. Working tree matches `82a5217c3`. These runs used `dotnet run` directly, so the testing platform exit was 2 rather than the checkpoint driver's exit 1. PC-1..PC-5 stay pending for method-scoped SourceLanding Mutation.

| PC | Red assertion | Build slot | Run slot |
|---|---|---|---|
| PC-1 | PublishedSha expected 40 `b`, actual 40 `a` | waited=0s held=147s | waited=15s held=41s |
| PC-2 | PublishedSha expected 40 `a`, actual 40 `b` | waited=0s held=102s | waited=0s held=43s |
| PC-3 | PublishedSha expected 40 `c`, actual 40 `b` | waited=0s held=104s | waited=0s held=48s |
| PC-4 | `BlockedTaskSync` ShouldBeTrue, "BlockedTaskSyncRecoveryService was not injected" | waited=0s held=124s | waited=15s held=57s |
| PC-5 | `SettlementSync` ShouldBeTrue, "SettlementSyncRecoveryService was not injected" | waited=45s held=54s | waited=15s held=49s |

## Restart

none. S1 is test-only. S2's probe is inert. The test build contains it. No server or runner activation.
