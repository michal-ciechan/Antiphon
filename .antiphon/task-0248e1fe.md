# Load-timeout fix 1: shared wait helper and limits

Load-timeout waits on the six ranked classes now use a shared finite wall cap and a width-4 limit. Each class passed alone on Linux. The runs verified code commit 4c7cafd3277772688edd91633911170849e105a0.

Commits on feat/card-task-0248e1fe:

- 992808a38f05776acbf40339825e91ea22c627a2 helper, limiter, witness, and the seat-release factor
- 4c7cafd3277772688edd91633911170849e105a0 the six classes

## Helper

`tests/Antiphon.Tests/TestHelpers/LoadTimeoutWait.cs` takes a test-clock budget plus a finite wall cap. A hook that never runs throws `TimeoutException` containing "wall cap". A test-clock expiry throws `TimeoutException` containing "test-clock budget". No wait is infinite.

`LoadTimeoutWaitTests` passed 1/1. A `FakeTimeProvider` advance of 60 seconds still lets the hook pass, and the same hook fails when the fake wall reaches the cap. `BeforeDispatch_budget_follows_the_test_clock` and `BeforeDispatch_late_hook_passes_at_sixty_seconds_and_fails_past_the_cap` passed 2/2. `WaitForBeforeDispatchAsync` calls the helper and keeps the BeforeDispatch sentences. That path always passes the existing 122 second cap.

`LoadTimeoutParallelLimit` is width 4. TUnit keys the semaphore by limiter type, so classes that share a process share four slots. A class run alone, and a nightly chunk of one class, only limits that class.

Caps are 2x the `scripts/lib/nightly-class-costs.json` seconds/nodes mean, and they stay at the existing positive bound when 2x is lower. Class `[Timeout]` is above the cap and above the Linux before-run max row, so the helper reports before TUnit cancels. Methods without a `CancellationToken` keep the TUnit0015 warning.

## Per class

Walls are slot-held seconds for the class run alone. Before was unlimited concurrency. After is width 4. Passed counts are succeeded/executed, with zero failures.

| Class | Bound before | Cap | Limit | Wall before | Wall after | Passed |
|---|---|---|---|---|---|---|
| DelegationDispatchRecoveryBoundaryTests | 30s idle, 20s HTTP | 30s | yes | 83s | 57s | 382/382 |
| AgentTaskPipelineStatusTests | 15s and 20s | 22s | yes | 24s | 78s | 69/69 |
| AgentTaskLandSourcePersistenceTests | 10s | 23s | yes | 32s | 43s | 35/35 |
| SessionMessageQueueDeliveredSpillRecoveryTests | 5s catch-up | 64s | yes | 114s | 73s | 5/5 |
| SpecialistQualificationTests | 10s | 18s | yes | 66s | 41s | 15/15 |
| BootStallDetectionTests | 30s | 30s | yes | 27s | 35s | 59/59 |

Delegation pace is 3505s/381 nodes = 9.2s (basis pace, not a completed TRX). 2x is 18.4s, so the 30s idle bound stays and the 20s HTTP timeout rises to 30s. Class timeout is 90s, above the 48.71s max row. The inner `WaitForIdleAsync` follows an unadvanced test clock; the 30s wall cap is what fails a launch that never goes idle. Production `WaitForIdleAsync` is unchanged.

Pipeline mean is 730/69 = 10.58s, so the cap is 22s. That raises the 15s and 20s bounds. Class timeout is 120s, above the 9.63s max row. The 15s `DateTime` polls in the same two methods use this cap. The `BehindTaskId` assertion still runs if the waiter misses the cap.

Land mean is 395/35 = 11.29s, so the cap is 23s. That raises the 10s monitor bound. Class timeout is 120s, above the 8.51s max row.

Spill mean is 160/5 = 32s, so the positive catch-up cap is 64s. Class timeout is 180s, above the 59.50s max row and above the cap. `completed.ShouldBeTrue` still reports a miss. The lock-held flush cancel remains 5s, because the three entries would otherwise each wait out 64s on purpose.

Specialist mean is 130/15 = 8.67s, so the cap is 18s. That raises the 10s check bound. Class timeout is 180s, above the 36.65s max row.

Boot mean is 247/59 = 4.19s. 2x is 8.4s, so the 30s sweep bound stays. Class timeout is 120s, above the 13.23s max row.

Every after wall sits under that class's cost-file launch seconds (pipeline 730, land 395, boot 247, spill 160, specialist 130, and the delegation chunk price below).

## Delegation chunks and the cost file

Delegation is priced at 3505 serial seconds, so the planner splits it into about four chunks of ~95 rows. A chunk is about 3505 x 95/381 = 874 serial seconds, under the 900s bin. The cost file stores that serial sum. `Get-NightlyLaunchClassCost` prices seconds/nodes. Width 4 changes launch wall, about ceil(95/4) x 9.2s = 221s, under 874s and under 900s. A hung chunk is ceil(95/4) x 90s = 2160s, under the 2400s lone-class ceiling. The whole-class Linux wall moved from 83s to 57s, so the limit did not blow the launch budget.

`scripts/lib/nightly-class-costs.json` is unchanged. C1404, C1577, C1584, and C1410, and `RcRosterRepairTests`, read frozen fixtures or pin other classes. The new witness is outside those fixtures. The live filter executed 382 rows; the cost file still records 381.

## Build

One slot build of `tests/Antiphon.Tests` with `UseAppHost=false` and `OutputPath=bin-loadtimeout/`, then `dotnet exec --no-build` per class. The isolated `bin-loadtimeout` directories are removed.

--- next stage ---
next: review
handoff: Review the shared wait, the six caps, and the Linux counts on feat/card-task-0248e1fe. The verified code commit is 4c7cafd3277772688edd91633911170849e105a0.
artifact: tests/Antiphon.Tests/TestHelpers/LoadTimeoutWait.cs
