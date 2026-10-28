# Windows-only qualification groups 32, 58, and 61

Investigate task `4d14ae68`, 2026-10-28. Tree `4563e52529b23052856cb0f1e0d01b383cf7590f` on `feat/card-task-4d14ae68`, the `origin/rc/2026-10-27` tip named in the brief. Read-only. No product code changed. No Antiphon test or slot build was run. Live native qualification `28bbb58e` (candidate `rc-2026-10-27-4563e52529b2`, supervisor PID 243364) was not stopped, restarted, or locked.

## Verdict

The 79b742de TRX was not read. This host has no `/mnt/c` mount, so `C:\Antiphon\releases\candidates\rc-2026-10-27-f84228f285dd\` is unreachable. Exact failing assertion lines are not established. Each class has a reconstructed mechanism from the code at this SHA, plus a cost-file row that does not name the assertion. All three are likeliest test defects: a wall-clock barrier, a two-tick script against ConPTY teardown, and a live process-start identity check. No product defect is established.

## Evidence

- Failed run named by the brief: `79b742de44354190bb55fd35914496f3`, candidate `rc-2026-10-27-f84228f285dd`, SHA `f84228f285dd3e4ad7b5dc8260cbc9f819256f50`. Group numbers are planner partitions, not stable identities.
- Cost rows in `scripts/lib/nightly-class-costs.json` (investigation id `658b8049233b1d9b75ef2b2fdeda70fd183e8426`). `may-understate` means a failed test can end early, so the stored seconds can be shorter than a finished run.
- Scratch probe, this session, against `Microsoft.Extensions.TimeProvider.Testing` 9.5.0 (the package at `tests/Antiphon.Tests/Antiphon.Tests.csproj:14`) plus `Microsoft.Extensions.DependencyInjection` 9.0.0. `FakeTimeProvider.Advance(2 minutes)` makes `GetElapsedTime` of a stamp taken before the advance return 120 seconds (frequency `10000000`). `ActivatorUtilities.CreateInstance<TakesClock>(sp, clock)` with `TimeProvider.System` also registered returns the explicit `FakeTimeProvider` (`activator-got-fake=True`, `got-system=False`).

## Group 32 — `Answer_racing_release_preserves_one_owner`

Cost row (`nightly-class-costs.json` around line 3486): run `79b742de`, chunk `all-group-32`, 39 nodes, 917 serial seconds, completed 39 of 39, journal `rc-2026-10-27-f84228f285dd/logs/timing-79b742de44354190bb55fd35914496f3/cases-143596-f5ae8bde8dee4c0e957e8a71466fa925.jsonl`, `may-understate: 1 failed tests`. The row does not name the method.

The only timeout inside the method is `entered.Task.WaitAsync(TimeSpan.FromSeconds(10))` at `tests/Antiphon.Tests/Application/TerminalRunnerSeatReleaseTests.cs:628`. The hook is installed by `RunnerSeatReleaseFixture.ReleaseAsync` (`RunnerSeatReleaseFixture.cs:437-442`), which sets `BoundaryAsync` and calls `RegisterAndReleaseAsync`. `TerminalRunnerSeatReleaseService.AdvanceAsync` reaches `BoundaryAsync("BeforeDispatch")` only after a non-blocking `gate.WaitAsync(0)` (`TerminalRunnerSeatReleaseService.cs:620`) and the reserved-state and source reads (`:632-639`). If that wait returns false, `AdvanceAsync` returns without the hook, `entered` never completes, and line 628 throws `TimeoutException` after 10 seconds. The same happens when the pre-hook database work on a loaded host exceeds 10 seconds. The class is `[Category("Integration")]` only (`TerminalRunnerSeatReleaseTests.cs:33-34`): no `NotInParallel`, no `ProcessSpawnLimit`. `SessionMessageQueueService.GetLock` is a per-session `SemaphoreSlim(1,1)`, so a different test's session does not hold this gate. The answer path takes the same gate with an unbounded `WaitAsync` (`AgentTaskReplyService.cs:407-408`). The test starts `TryAnswerAsync` at line 629 and does not await it before the status read at line 630. The `finally` at lines 632-636 always releases the hook and joins, so a timeout at 628 does not by itself deadlock disposal.

Second Windows-only mode, separable by the exception type: `RunnerSeatReleaseFixture.DisposeAsync` (`:651-656`) deletes the fixture directory and, on failure, calls `FixtureFileDiagnostics.WriteLandingLocks` then rethrows. `WriteLandingLocks` (`FixtureFileDiagnostics.cs:32-51`) walks up to 4096 entries, logs files named `landing.lock`, and states that holder identity needs a native handle capture. A delete failure after a passing body fails the test. A hung delete is a timeout with a different stack than line 628. The brief's word is timeout, which matches line 628 first.

Product versus test: test timing budget, unless the TRX shows a lost answer (`Attempt` other than 2 at line 640, or a queued message at line 644) rather than `TimeoutException` at line 628. The answer service blocking on the same gate is the behaviour the test is trying to observe.

## Group 58 — `A_sign_in_mode_fakegrok_pool_task_never_types_the_brief`

Linux cannot execute the body. Line 44 throws `SkipTestException("ConPTY only on Windows")`. A skip is not a green run.

Cost row (around line 2136): run `8f055f87`, not `79b742de`, chunk `all-group-58`, 1 node, 32 serial seconds, completed 1 of 1, journal `rc-2026-10-25-105792f7d22f/logs/2026-10-26-0946-8f055f87/timing/cases-330220-a6e7501109d446ecb783a02d523d64ff.jsonl`, `may-understate: 1 failed`. `79b742de` did not replace this row. The 32 seconds are the older failed duration. They match setup plus the 30-second idle bound, and they are shorter than a path that returned from idle after an ~8 second sign-in and then failed the grace assertion.

The grace clock question is closed by the probe above. `BuildHarness` registers `TimeProvider.System` at `GrokSignInRuntimeTests.cs:182` and constructs the dispatcher with the fake clock at line 244. `ActivatorUtilities` binds that explicit clock. `clock.Advance(TimeSpan.FromMinutes(2))` at line 134 moves `GetElapsedTime` by 120 seconds. Grace in the test is `DeadSessionFailGraceMinutes = 1` (line 197). Product default is 3 (`DelegationSettings.cs:644`). The sweep compares `_timeProvider.GetElapsedTime` to that grace (`AgentTaskDispatcher.cs:3160-3165`) and does not read wall time. A two-minute advance is past a one-minute grace. If the runner list does not still show the session `Running`, the second tick fails the task. A grace assertion cannot fail because `Advance` is ignored.

Likeliest line, given that stored 32-second failure: `WaitForIdleAsync(TimeSpan.FromSeconds(30))` at `GrokSignInRuntimeTests.cs:109`. `AgentSessionLaunchQueue.WaitForIdleAsync` throws `OperationCanceledException` when any launch is still incomplete at the deadline (`AgentSessionLaunchQueue.cs:218-220`). The launch method awaits ConPTY teardown before it returns, so a teardown that outlasts 30 seconds throws at line 109 and the grace ticks never run. A prior note on a different candidate (`db2c2088`) said sign-in detection succeeded and then queue-idle cancelled. That note is not the `79b742de` TRX.

Second hypothesis, the one the brief names grace: gate 2 at `AgentTaskDispatcher.cs:3150-3157`. If the first tick after idle still sees runner status `Running`, `Forget` drops the stamp, the two-minute advance is wasted, and the second tick observes a fresh stamp of age about zero. The task stays `Dispatched`, and line 140 `failed.Status.ShouldBe(AgentTaskStatus.Failed)` fails. The harness runner forwards `ListAsync` (`GrokDelegateEndToEndTests.cs:1245`). The in-process runtime sets status `Running` after start and `Exited` on handle-exit (examples: `SessionRunnerRuntime.cs:2884-2903`). Gate 2 blocks only while the list still says `Running`. That withhold is the CARD-0056 brake documented on `DelegationSettings.cs:633-638`. The two-tick script assumes the runner has already dropped `Running` when the database row is `Failed`.

Third race: fakegrok exit and ready-wait are both 8000 ms (`GrokSignInRuntimeTests.cs:206` and `:222`). `GrokStartupReadiness` checks `isExited` before it classifies (`GrokStartupReadiness.cs:216`), and a sign-in observation returns without writing (`:220-225`). If the process exits before that classification, `OnSignIn` never runs and lines 114-116 fail (`SessionStatus.Failed`, `ProviderSignInRequired`, reason containing `grok login`). That path is about 8 seconds plus setup, shorter than the stored 32 seconds, so it fits `79b742de` only if that run's duration differed. The comment at line 181 says ready-wait and launch timers stay on real time.

A 40P01 deadlock between the launch catch and the exit observer is described as already ordered session-before-agent (`AgentSessionService` comment near the launch catch). It is not established for this failure.

Product versus test: test timing and the two-tick script. The grace formula and gate 2 are the specified product behaviour.

## Group 61 — `CheckpointSlotExecutorTests`, exit 2

Cost row (around line 3756): run `8f055f87`, chunk `all-group-61`, 10 nodes, 17 seconds, completed 10 of 10, no failure note. `79b742de` did not replace this row, so a failure of this class in `79b742de` is not in the cost file. An earlier intentional red at commit `a02389ca` (reachable-broker and row-rejection) later went 10 of 10 at `cd37aad2` (`docs/superpowers/evidence/2026-10-14-card-1254-code.md`). That pair is a different commit from `f84228f2`. It is not the `79b742de` pair.

`ExitCodes.Invalid` is 2 (`tools/Antiphon.Checkpoints/Execution/ExitCodes.cs:7`). A failed TUnit class run also exits 2. "Two rows plus the class, exit 2" can mean either the two-row manifest's `ExecuteAsync` returned 2, or two test methods failed and the test host exited 2.

Likeliest Windows mechanism that turns a success test into domain exit 2: `ProcessLeaseHolder.Start` reads `Process.StartTime` on a short-lived `dotnet hold` child (`LeaseHolder.cs:44-64`). A throw disposes the child and rethrows. `BuildSlotClient.AcquireCoreAsync` maps that, and an empty start string, to `holder_identity_unavailable` (`BuildSlotClient.cs:183-193`). The executor then returns `ExitCodes.Invalid`. Windows `Process.StartTime` is local `DateTimeKind` and throws `InvalidOperationException` or `Win32Exception` when the child has already exited or is not yet queryable. The throw path is already pinned on Linux by `failed_start_time_capture_disposes_new_holder` (`CheckpointSlotExecutorTests.cs:329-348`), which expects exit 2 and `holder_identity_unavailable`. That pin injects the throw. It does not force Windows to throw from the real `StartTime` getter under load.

The two-row success test is `executor_reachable_broker_grants_build_and_rows` (`:22-92`). It expects exit 0 at line 56. Lines 80-81 and 91 compare a fresh `GetProcessById().StartTime` with the cached `"O"` stamp within 1 millisecond. A miss there fails the test after the executor returned 0, so the process exit is the test host's. The holder caches one stamp (`LeaseHolder.cs:62-64`). Broker idempotence is exact `(pid, processStartUtc)` equality (`BuildSlotBroker.cs:82` and `:89`), so a retry of the same cached stamp matches. A second live read that is not the cached string can miss the 1 millisecond tolerance without changing the broker key.

The other load-sensitive success row is `executor_renews_until_driver_finishes_then_releases` (`:95-112`): a 2400 ms driver, `renewEvery` 1 second, and at least two renews. Scheduling slip leaves one renew and fails the test while the executor returned 0. `CheckpointManifest.EffectiveMaxRows` defaults to 1 on Windows, and these tests set `Parallel = 2`, so width is 2 on both operating systems. The tests pass an in-process `SlotHandler`. `BuildSlotClient.DefaultEndpoint(OperatingSystem.IsWindows())` is used only when that handler is omitted (`CheckpointApp.cs:184-188`). A `state.json` `IOException` or `UnauthorizedAccessException` becomes `StatePublicationException` and exit 2 (`CheckpointApp.cs:179-180` and `:90`). That would fail more than two rows.

Product versus test: the 1 millisecond tolerance and the live `StartTime` read are test and fixture timing against Windows process semantics. The broker's exact `DateTime` key is the specified product behaviour when the cached stamp is stable. `holder_identity_unavailable` after a thrown `StartTime` is the client refusing a lease it cannot identify.

## Not done, noted

- Group 32 witness, not implemented: hold the session semaphore, or delay the pre-hook reads past 10 seconds, and expect `TimeoutException` from `TerminalRunnerSeatReleaseTests.cs:628`; replace the wall cap with a test scheduler.
- Group 58 witness, not implemented: a launch or `KillAsync` that stays incomplete past 30 seconds expects the idle throw at line 109; a runner list that is `Running` on sweep 1 and `Exited` on sweep 2, with a two-minute `FakeTimeProvider` advance between them, expects the task to stay `Dispatched`.
- Group 61 witness, not implemented: the throw path already exists (`failed_start_time_capture_disposes_new_holder`); the unforced Windows condition is a second `StartTime` read more than 1 millisecond off the cached stamp at lines 80 and 91.

## What resolves the assertion lines

Read the 69 TRX files under the `f84228f2` candidate, or rerun the three methods on a native Windows host. Do not stop live run `28bbb58e` to do it. Another Linux read of this tree cannot name which hypothesis fired. A green Linux run of groups 32 and 61 would not disprove a load failure, and group 58 skips.

## Uncertainties

- Group 32: `TimeoutException` at line 628, versus a later assertion, versus `DisposeAsync` rethrowing a `landing.lock` delete failure.
- Group 58: whether `79b742de` died at line 109, line 140, or lines 114-116. The 32-second figure is run `8f055f87`.
- Group 61: which methods failed, and whether exit 2 is `ExitCodes.Invalid` (`holder_identity_unavailable` or state publication) or the test-host exit after an assertion. The `79b742de` cost row was not updated.
