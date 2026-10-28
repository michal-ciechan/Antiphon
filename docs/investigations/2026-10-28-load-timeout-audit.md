# Load-timeout audit of tests under parallel load (2026-10-28)

Task a4f12188, Investigate, server2/Linux. Source: `origin/rc/2026-10-27` at
`aa6dfd24a51db7c02e8722a05a21817bf4df407a`. Static analysis only: no build and no test ran.

## Failure class

In Windows qualification run 28bbb58e, group 24 `TerminalRunnerSeatReleaseTests.Answer_racing_release_preserves_one_owner`
failed at a 10 s wall wait. The class had no limiter, so all 39 rows started in the same
millisecond (per-test p95 61.2 s, max 94.8 s). The same shape appeared earlier at three other bounds:

| Bound | Class | Repair on rc |
|---|---|---|
| 10 s | `TerminalRunnerSeatReleaseTests` (cost source: chunk all-group-32) | d077c2087, 5e1821b73, 11496d792: test clock + 122 s wall cap + `TerminalSeatReleaseLimit` (4) |
| 30 s | `GrokSignInRuntimeTests` launch-idle wait | 7de8b8a06, 5e1821b73: fake clock + 64 s wall cap |
| 1 ms | `CheckpointSlotExecutorTests` hold-child `Process.StartTime` stamp | ecc0fe49f, 8f4e9e630 |

The mechanism is the same in all three. A test asserts that a background step finishes within a
short real-time bound, and the bound was sized on an idle host. When every row of a class starts
at once, and other classes in the same launch share the host, thread-pool and Postgres queueing
alone can use up that bound before the code under test runs.

## Method

1. **Scanner** (node; Linux has no python). It walks every `.cs` file under `tests/Antiphon.Tests`, `tests/Antiphon.Agents.Pty.Tests`,
   `tests/Antiphon.SessionRunner.Tests` and the other `tests/*` projects. It records any literal bound under 60 s on a
   line with `WaitAsync`, `CancellationTokenSource(..)`, `CancelAfter`, `WhenAny(.., Task.Delay)`,
   `WaitForIdleAsync`/`WaitFor*Async`/`WaitUntil`, `[Timeout(n)]`, `Elapsed.ShouldBeLessThan`, a
   `Stopwatch` loop, `Process.StartTime` or a `for (i < N) Task.Delay(M)` poll. It follows a named
   `static readonly TimeSpan` constant to its uses. It drops lines that only advance a fake clock (`Advance(`, `SetUtcNow`)
   and timer-equality lines. Per class it counts `[Test]` methods (with expanded `[Arguments]` rows) and records any
   class or method `ParallelLimiter<T>` and any `[NotInParallel]`. Totals for Antiphon.Tests: 1,775 files and
   1,744 raw hits, of which 1,405 are TimeSpan waits, 72 Stopwatch asserts, 46 `[Timeout]`, 56 StartTime and 77 named-constant uses.
2. **Costs.** `scripts/lib/nightly-class-costs.json` has 771 rows (712 `measured`). A row is serial
   test-seconds over nodes, which gives a **mean**. The file has no per-test max, so I ranked on
   mean seconds per node and on class total.
3. **Packing.** I rebuilt the packing from `scripts/lib/nightly-tests-impl.ps1:136-428`
   (`ConvertTo-NightlyProfileLaunchGroups`). The planner works per namespace. It packs largest first into first-fit
   bins with a 900 s target and at most 30 classes per bin. A class at or over 900 s, or a class whose name is a prefix of another, runs alone. A class over 2,400 s
   splits into exact-method chunks of 900 s or less. An unmeasured class is priced at 0.5 s/node, doubled for Land, Delivery and Check.
   My rebuild gives 134 Antiphon.Tests groups (the real run had 232 launches across all suites and applied profile exclusions).
   For each group, "burst" is the number of rows in unlimited, non-`NotInParallel` classes, which can all start at once.
4. **Limiters.** `ProcessSpawnLimit` = 1 (`TestHelpers/ProcessSpawnLimit.cs`) serialises those
   classes. `GitProcessSpawnLimit` = Clamp(cores/2, 2, 4). `TerminalSeatReleaseLimit` = 4. The nightly
   driver passes no `--maximum-parallel-tests` (grep of `scripts/`), so TUnit's default width applies.
   In run 28bbb58e that width was at least 39.

**Excluded as the safe direction.** These are negative probes that load can only make pass: `WhenAny(x, Delay(10..400)).ShouldNotBe(x)`, and
expected-timeout deadlines. Examples: `RunnerSyncLeaseFairnessTests.cs:402,555`, `RunnerSettlementSyncTests.cs:831`,
`BlockedTaskParkReclaimTests.cs:1462`, `DispatcherSiblingInspectionSchedulingTests.cs:421`,
`SessionMessageQueueDeliveryVerificationTests.cs:2052`, `AgentTaskCheckSweepTests.cs:810`,
`AgentTaskLandReceiptWatermarkSafetyTests.cs:147`, `DockerDeliveryBarrierTests.cs:213` (30 ms, expects Incomplete),
and every `WaitCommand(.., 25 ms)` row that expects `StillRunning`. Under load they can pass when they should fail, but they cannot fail when they should pass.

## Top 15 candidates (ranked)

Rank uses four factors: whether the class is unlimited (all rows start together), its row count, its measured mean s/node,
how short its positive bound is, and its group burst. Paths are relative to `tests/Antiphon.Tests/`.

| # | Class (nodes / measured s, s/node; limiter) | Positive wall bounds (file:line) | Smallest fix |
|---|---|---|---|
| 1 | `DelegationDispatchRecoveryBoundaryTests` (381 / 3505 s, 9.2; **none**; >2400 s, so split into about 4 method chunks of about 95 rows each that start together) | `Application/DelegationDispatchRecoveryBoundaryTests.Budgets.cs:421` CTS 30 s; `.Budgets.cs:485`, `.RunningRecovery.cs:444`, `.Watchdog.cs:551` `WaitForIdleAsync(30 s)`; `.AbsentEvidence.cs:1152,1154,1178` `HttpClient.Timeout` 20 s | Class concurrency limit (4) plus a wall cap of 2x the measured max (helper H1) |
| 2 | `AgentTaskPipelineStatusTests` (69 / 730 s, 10.6; **none**) | `Application/AgentTaskPipelineStatusC1076Tests.cs:189,254` `PushEntered.WaitAsync(15 s)`; `:208,277` `WhenIdleAsync().WaitAsync(20 s)` | Limit (4) + H1 |
| 3 | `AgentTaskLandSourcePersistenceTests` (35 / 395 s, 11.3; **none**) | `Application/AgentTaskLandSourcePersistenceTests.cs:199` `monitor.WaitAsync(10 s)` | H1 (cap about 2x max) + limit |
| 4 | `ExpectationObservationAdapterTests` (12 / 167 s, 13.9; **none**) | `Application/ExpectationObservationAdapterTests.cs:239` `Stopwatch` < 8 s around one Postgres read | Assert the bounded work (rows read or query count), not elapsed time; or `[NotInParallel]` on that method |
| 5 | `SessionMessageQueueDeliveredSpillRecoveryTests` (5 / 160 s, 32.0; **none**) | `Application/SessionMessageQueueDeliveredSpillRecoveryTests.cs:110,147` CTS 5 s budget for `CatchUpTranscriptAsync` | H1 (logical budget on the test clock, wall cap of 2x max) |
| 6 | `RemoteWorkspacePreparerTests` (27 / 162 s, 6.0; **none**) | `Application/RemoteWorkspacePreparerTests.cs:34` `TickBound = 15 s`, used at :44, 52, 69, 79, 94, 99, 106, 131 and more | Raise the one constant to 2x the measured max, plus a limit |
| 7 | `SpecialistQualificationTests` (15 / 130 s, 8.7; **none**) | `Application/SpecialistQualificationTests.cs:255,281,321` `WaitAsync(10 s)` | H1 |
| 8 | `BootStallDetectionTests` (59 / 247 s, 4.2; **none**) | `Application/BootStallDetectionTests.cs:1102` `RunOverdueSweepAsync().WaitAsync(30 s)` | H1 or a limit |
| 9 | `WorktreeRetirementRaceTests` (54 / 119 s, 2.2; **none**) | `Application/WorktreeRetirementRaceTests.cs:292,325,940,962` `WaitForIdleAsync(15 s)` | Shared `WaitForIdleAsync` cap (H2) |
| 10 | `SessionRuntimeActionTests` (28 / 103 s, 3.7; **none**) | `Application/SessionRuntimeActionTests.cs:196,218,390,512` 5 s; `:342,580,589,720` 10 s | H1 |
| 11 | `OutputCleanupTests` (49 / 271 s, 5.5; **none**) | `Checkpoints/OutputCleanupTests.cs:799` `Elapsed` < 25 s (hung fake git); `:770` `[Timeout(45_000)]` | Put the hang row on a limiter or `[NotInParallel]`; set its cap from the product kill budget |
| 12 | `StandingBootWatchdogTests` (31 / 57 s; **none**) | `Application/StandingBootWatchdogTests.cs:177,181,186` 20 s; `:508` 30 s | H1 |
| 13 | `SessionStateWarmupTests` (16 / 76 s; **none**) | `Application/SessionStateWarmupTests.cs:59,75,91,266,274` 10 s | H1 |
| 14 | `PhoneHomeBackpressureTests` (6 / 62 s, 10.3; **none**; rebuilt group burst 142) | 26 sites of `WaitAsync(5 s)`, e.g. `Application/PhoneHomeBackpressureTests.cs:24,29,52`, `.Bounds.cs:29,57,133`, `.Protocol.cs:168,169`, `.Scheduling.cs:82,129` | One class constant at 2x max (H1) |
| 15 | Checkpoint `WaitCommand` 1 s positive waits: `CheckpointLaunchCleanupTests` (11, **none**), `HostCleanupCheckpointAuthorityTests` (17, **none**), `CheckpointToolCopyCleanupTests` (16, **none**), `CheckpointRecoveryLinuxTests`/`WindowsTests` (PSL) | `Checkpoints/CheckpointLaunchCleanupTests.cs:201`, `Infrastructure/HostCleanupCheckpointAuthorityTests.cs:239`, `Checkpoints/CheckpointToolCopyCleanupTests.cs:231`, `Checkpoints/CheckpointRecoveryLinuxTests.cs:89,129`, `Checkpoints/CheckpointRecoveryWindowsTests.cs:82,119,397`: each must reach exit 0 or `ExecutorCrashed` within 1 s, including a liveness/StartTime probe | Shared timeout-argument constant for `WaitCommand` test calls (H3), about 10 s; keep 25 ms only for `StillRunning` probes |

**Next tier** (the same defect, but smaller exposure, or serialised by `ProcessSpawnLimit` so only chunk-mates add load):
`PhoneHomeRestartResumeTests.cs:39` (`WaitForIdleAsync` 3 s, unlimited);
`RunnerCapacityEndpointTests.cs:44,60` (3 s, unlimited); `AgentTaskLandQueueAgingTests.cs:314` (5 s);
`PhoneHomeEventPumpTests.cs:96` (5 s); `DispatcherAdmissionSchedulingTests.cs:59,99` (5 s);
`Checkpoints/CheckpointTaskOwnershipTests.cs:58-87,315,322` (5 s, 24 unlimited rows);
`DispatcherSiblingInspectionSchedulingTests.cs:429-430` (`Elapsed` < 48 s, progress gap < 15 s; PSL, 44 rows at 13.1 s/node);
`GrokSignInRuntimeTests.cs:214` (500 ms for a continuation after `clock.Advance`; residual after the fix);
`Checkpoints/CheckpointFixtureProcessTests.cs:37,79` (`WhenAny(.., Delay(8_000))` against a 2 s limit; PSL);
`AgentTui/RunnerProcessProbeRedactionTests.cs:262` (`Elapsed` < 2.5 s; 46 unlimited rows, no cost row);
`Agents/CodexAdapterLocalShellTests.cs:206` (< 2.5 s, PSL); `Infrastructure/GitDiffSpikeTests.cs:182` (< 5 s);
`Scripts/RemoteScriptContractTests.cs:1960-1970` (10 s; method-level PSL).
Other assemblies are not in the cost file or this planner rebuild, so they are listed but not ranked:
`Antiphon.SessionRunner.Tests` `HerdrPaneDisposalConcurrencyTests` (44 rows, unlimited, 5 s),
`PhoneHomeConnectionServiceTests` (17 rows, 2 s), `CodexTranscriptTailerTests` (17 rows, 5 s);
`Antiphon.Agents.Pty.Tests` `CodexReadyWaitTests` (19 rows, 2 s), `TerminalScreenTests` (41 rows, 5 s).

## Shared helpers (fix shape only)

- **H1**: lift `TerminalRunnerSeatReleaseTests.WaitForBeforeDispatchAsync` (`Application/TerminalRunnerSeatReleaseTests.cs:733`)
  into `TestHelpers`. It takes a logical budget on the test clock plus a wall cap of 2x the measured max and throws a named `TimeoutException`.
  It covers #1-3, 5, 7, 8, 10, 12-14.
- **H2**: give every `AgentSessionLaunchQueue.WaitForIdleAsync(15 s)` test call one shared cap constant. It covers #9, #1 and the next tier.
- **H3**: one constant for positive `WaitCommand.WaitAsync` timeouts in test code. It covers #15.
- **Limit**: one reusable `IParallelLimit` of width 4 for heavy unlimited Postgres classes, the same pattern as `TerminalSeatReleaseLimit`.
  It covers #1, 2, 3, 6 and 11. Budget it as in that file: ceil(nodes/4) x the method timeout must stay under the 2,400 s lone-class ceiling.
  For #1 (381 nodes) the method chunks already split the class, so price per chunk.

## Windows-only conditions (only a native run can confirm)

- `Process.StartTime` resolution and comparison: `Checkpoints/CheckpointSlotExecutorTests.cs:64,110` (already fixed),
  `Scripts/ScriptHarnessCancellationTests.cs:279` (within 1 s), `TestHelpers/TestOwnedPtyHost.cs:284,325`,
  `TestHelpers/WindowsFileLockOwners.cs:34,58`, `Infrastructure/RepositoryMutationLeaseTests.cs`,
  `Infrastructure/RepositoryChildJournalInspectorTests.cs`, `Infrastructure/LandVerificationWindowNativeTests.cs:84,151`,
  and the #15 `ExecutorCrashed` liveness probe.
- ConPTY / named pipe: `Agents/DirectSessionRunnerClientDisposalTests.cs:44,65,186` (connect 15 s, cleanup < 10 s).
- File locking / junctions: `TestHelpers/DirectoryLinkFixtureWindowsTests.cs:122` (`Elapsed` < 10 s), `:150` (2 s),
  `Infrastructure/FileUseWindowsTests.cs`, `Checkpoints/CheckpointRecoveryWindowsTests.cs` (#15).
- The rest of the top 15 (#1-14) are in-process thread-pool and Postgres waits. Their trigger (one burst of concurrent rows on a
  loaded host) is not Windows-specific, but only the desktop has produced it so far.

## Remaining uncertainty

- The cost file gives means, not per-test max or p95. A class with a high mean and a short bound is likely to fail, but this is not measured.
- The packing is rebuilt from the planner code. It does not apply profile exclusions and does not use the run's planned manifest.
- I did not verify TUnit's default width on the desktop. The 39-in-one-millisecond start shows it was at least 39.
- The scanner sees literal bounds only. It misses bounds passed through variables into private helpers, and it misreads `8_000` as 8.
  Nothing was reproduced. A Linux burst repro would mean running one unlimited class at full width with a CPU hog beside it.

Not done, noted: implementation of H1-H3/limit (Debug/Code); native confirmation of the Windows-only rows.
