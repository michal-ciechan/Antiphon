# C1349 lost-wakeup: same-adapter Register race

Debug task 5e090a01. Windows Full run `06863906697748fc8354bcbe527119e9` on `rc/2026-11-24` (master `870a93d21f2b18c85a8b0905ab689b28e3099813`), chunk `antiphon-all-group-36`, failed 1 of 67 at 2026-11-17T06:08:27Z. The failing result lasted 1.22s.

`ConflictException: Agent session <guid> is already registered` at `AgentSessionRuntime.Register` (`server/Application/Services/AgentSessionRuntime.cs:113` on that tree), called from `QueuedReceiptAssertions.ConfirmQueuedReceiptAsync` (`tests/Antiphon.Tests/TestHelpers/QueuedReceiptAssertions.cs:32`), called from `DispatcherSiblingInspectionSchedulingTests.C1349_LaterTaskReceivesBriefBeforeInspectionCompletes(lost-wakeup)` (`tests/Antiphon.Tests/Application/DispatcherSiblingInspectionSchedulingTests.Delivery.cs:105`).

The same scenario passed in Windows Full run `a21d8174` (1.19s). Chunk `antiphon-all-group-36` had 0 failed results in `ae84478d`, `0b292fa4`, `97150a0b`, `34d41b7e`, `b7812833`, and `160200cb`. `eligible`, `busy`, and `queue-inserted` passed in the failing run.

## Determination

This is a test-seam race. Production `AgentSessionService.StartAsync` does not call `AgentSessionRuntime.Register`. It calls `adapter.StartAsync` and `_launchOwnership.TryRegister`, which is a different method (`server/Application/Services/AgentSessionService.cs:221`).

Two test callers register the same `FakeAgentProtocolAdapter` instance on the harness runtime:

1. The fake launch. C1349 sets `harness.Adapter.RegisterOnStart = harness.Runtime`. `StartAsync` sets `StartedSessionId`, then calls `RegisterOnStart.Register` with no catch (`tests/Antiphon.Tests/Agents/FakeAgentProtocolAdapter.cs`).
2. The receipt helper. `ConfirmQueuedReceiptAsync` calls `Register` when `ListLiveSessions` does not already contain the id (`QueuedReceiptAssertions.cs:31-32`).

The dispatcher tick enqueues the launch and returns. `ReadyHold` blocks `WaitForReadyAsync`, which runs after `StartAsync` returns, so the hold is after `Register`. For `queue-inserted` and `lost-wakeup` the helper runs while that launch can still be inside or before `StartAsync`. The helper awaits dialog observation and transcript bind first, which usually lets the launch `Register` finish, so `ListLiveSessions` already contains the id and the helper skips. The failing interleaving is one preemption after `Contains` returns false and before `TryAdd`: the launch wins, and the helper throws. That matches the Windows stack (helper line 32) and the 1.22s duration.

The reverse interleaving is the helper's `TryAdd` winning, then the launch's `Register` throwing. `AgentSessionLaunchQueue.LaunchAsync` catches that exception, marks the session Failed, and rethrows (`server/Application/Services/AgentSessionLaunchQueue.cs`). `WaitForIdleAsync` swallows a faulted launch. `StartedSessionId` is set before `Register`, and the `queue-inserted` / `lost-wakeup` flush uses a recovered runtime, so this direction can reach the empty-`SubmittedBodies` assertion at `QueuedReceiptAssertions.cs:80` without the test stack showing `ConflictException`.

`recovered.Runtime.Register` (helper line 57) is a different runtime object. It stays strict. It is not the Windows stack.

A real production double launch is the launch-queue claim. `LaunchAsync`'s catch records the session Failed and rethrows. `WaitForIdleAsync` does not rethrow a faulted launch. `ConflictException` from `AgentSessionRuntime.Register` is the test seam.

## Linux stress

One leased `Antiphon.Tests` build with `UseAppHost=false` and `AntiphonCheckpointRepeat=20`, then `--no-build` of `/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_LaterTaskReceivesBriefBeforeInspectionCompletes`. Each repeat process is 20 iterations, 80 scenario results. Host load was recorded before each start and a batch started only while the 1-minute load was below 16. Burners were bash busy-loops except where noted. The named `ConflictException` did not appear.

| Run | Iterations | Result |
|---|---:|---|
| One pass, no working burner (`python3` absent; `taskset -c 0,1`) | 1 | 4 passed |
| Repeat processes r20 and r20-2 through r20-7 | 140 | 560 passed |
| Repeat processes r20-11 through r20-19 | 180 | 720 passed |
| r20-8, test pinned to CPUs 0,1 plus two burners | 20 | 79 passed, 1 `eligible` `TimeoutException` at `Delivery.cs:83`, `:85`, and `:128` |
| r20-20, burners, load 32.40 at start | 20 | 79 passed, 1 `queue-inserted` empty `SubmittedBodies` at `QueuedReceiptAssertions.cs:80` (685ms, repeat ordinal 6) |

361 iterations of the four-scenario method, 1444 scenario results. Lost-wakeup did not fail. The `eligible` timeout is the 10-second git-entered and tick waits expiring under a 2-core pin. The `queue-inserted` failure has no `ConflictException` in its TRX. It is the only load-induced functional miss and matches the reverse direction above: a swallowed launch fault, then a flush that submits nothing. The remaining iterations up to 400 were not run. Further loaded repeats of this method were already producing those other artifacts, and the window that throws from the helper is one statement after awaits that usually let the launch finish first.

The reproducible red witness is `AgentSessionRuntimeTests.Same_adapter_register_is_idempotent_and_a_different_adapter_conflicts`, committed before the fix. On the base `Register` it failed once, in 185ms, with `ConflictException: Agent session '<guid>' is already registered` at `AgentSessionRuntime.Register` line 113, from the second call with the same adapter. Total 1, failed 1.

## Fix

`AgentSessionRuntime.Register` holds `_testAdapterRegistration` across the insert and the `OnTextDelta` subscription. A second call with the same instance returns after that subscription exists, so it does not subscribe again. A different adapter still throws `ConflictException`. A `TryRemove` that lands between the failed insert and the lookup retries. The helper's `ListLiveSessions` pre-check stays, so a session that is live only on the runner list is not given a new test adapter.

`FakeAgentProtocolAdapter.AttachAsync` still catches `ConflictException`. That catch is the other-adapter case. The same instance returns from `Register`.

After the fix, the witness passed 1 of 1, and the four C1349 scenarios passed 4 of 4 in 33.5s (leased `--no-build`, `UseAppHost=false`, no `LINUX COMPILER` line).

## WHY-MISSED

introducing commit/card: `dee14d2c4ad46ef2841572aad86e720fc9cda30f` (CARD-0499) added the helper's check-then-`Register`. `999a4c6db645dcdb56c005d1c570da346b8b96fc` set `RegisterOnStart = harness.Runtime` on C1349. Both are ancestors of the failing SHA.

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- This Debug task did not recover a later Full review whose selection included `DispatcherSiblingInspectionSchedulingTests`. The class is Integration and Slow.

why the failing test was outside that selection (lane/category/path): Windows qualification group 36, Integration/Slow, `tests/Antiphon.Tests/Application/DispatcherSiblingInspectionSchedulingTests.Delivery.cs`. The fixed Merge high-radius subset (`docs/orchestration-loop.md`, CARD-1783/1801) is `TestTimingOperationLabelTests`, `AppDbContextModelTests`, `ContractSnapshotTests`, `CheckpointReceiptSafetyTests`, `HostCleanupCheckpointReceiptTests`, the land-notification family, and the `ReleaseGatePlatformTests` A5 pair. The Code/Review guard union in `docs/testing-and-build.md` does not name this class. A Linux one-off and 361 loaded Linux iterations did not hit the helper's `ConflictException`. A Merge `--repeat` of the class would not be a reliable catch for that window.

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`server/Application/Services/AgentSessionRuntime.cs` -> `/*/*/AgentSessionRuntimeTests/Same_adapter_register_is_idempotent_and_a_different_adapter_conflicts`

same cause as an already written note: none

Native Windows proof is owed to the next rc qualification. This Linux run does not replace that chunk.

## Checkpoints

Closed list for task 5e090a01. One `--serial` checkpoint-tool run, no `--row-timeout`. The tool adds `UseAppHost=false` on this host. CP-1's 15 minutes is the cold-build budget. The class row's floor is 49 argument expansions in source (the Windows chunk's 67 is the whole group). `C1210_Windows_linux_only_outcomes_are_exact` skips on Linux. Build `scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj` before CP-5; `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged` runs that driver with `-NoBuild`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c1349-witness/` | register-witness | `/*/*/AgentSessionRuntimeTests/Same_adapter_register_is_idempotent_and_a_different_adapter_conflicts` | same-adapter Register | 1 method, 0 failed | 1 | 15 | true |
| CP-2 | all | CP-1 | sibling-class | `/*/*/DispatcherSiblingInspectionSchedulingTests/*` | C1349 class | 49 results, 0 failed | 49 | 12 | true |
| CP-3 | all | CP-1 | timing-labels | `/*/*/TestTimingOperationLabelTests/*` | timing labels | 2 methods, 0 failed | 2 | 3 | true |
| CP-4 | all | CP-1 | selector | `/*/*/ImpactedClassesSelectorTests/*` | selector | 31 methods, 0 failed | 31 | 5 | true |
| CP-5 | all | CP-1 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | eleven-class guard union | 114 passed and the Linux C1210 skip, 0 failed | 114 | 8 | true |
