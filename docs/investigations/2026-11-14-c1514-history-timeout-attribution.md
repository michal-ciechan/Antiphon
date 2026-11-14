# C1514 history timeout attribution

Debug task `7bd634da`, 2026-11-14. This is diagnostic evidence, not a release qualification or an ordinary Review approval.

Commissioning evidence (reported by Review `23794b24`, not re-created here): reviewed tip `25fe10cebde5e8e17530d579a7c4e0b57f080515` passed 16/21 in a combined run and 20/21 on an isolated retry. `HistoryFailureDoesNotStopLiveSamples` hit its 10 s limit twice. The four first-run timeouts that passed on retry were the derive-before-save and derive-after-write recovery arguments, `IdentityConflictsNeverOverwriteStoredSamples`, and `RawAndRollupsSurviveRetentionAndLateReplay`. Reported host load was about 26; no base run had completed. Those historical failures remain failures; this comparison does not rewrite them or fabricate their missing stacks.

## Outcome

**Incomplete: the required PostgreSQL EXPLAIN diagnostic was not admitted before the time box.** Its build-slot log records `BUILD SLOT waiting label=C1514-debug-7bd634da-explain position=3 occupied=4/4 elapsed=0m` then `BUILD SLOT timeout after 15s position=3`. No SQL process/container was launched. The gate timeout is exit 4 by the gate contract; the outer shell displayed the log afterward, so its final status was that display command, not a retained native driver status. No unleased retry was made.

Completed 9 class runs: **189 executed, 184 passed, 5 failed, 0 skipped**. HistoryFailure passed 8/9; its original 10 s timeout was reproduced in 0 runs. All three builds passed. The class is **not clean**.

Master alone is red in the same HistoryFailure method: A3 failed during seed preparation with `host_load_history_busy` at 5.8500869 s, before its persistence scenario. A3 also reproduced a 10 s RawAndRollups timeout. This proves pre-existing baseline instability, while the exact original HistoryFailure timeout remains a separate, unreproduced failure mode. B/C do not show a demonstrated worsening of completed HistoryFailure timings; **three runs are not statistics**, and no significance claim is justified.

C2 Identity and B3/C3 PrimaryEpoch timeouts are additional findings with unresolved product-versus-fixture attribution. Their late exceptions demonstrate continuing timed-out tasks; do not discard those failures or grant a clean Review from these comparisons.

Raw HistoryFailure samples:

- A: 8.6584864 s (Passed); 3.6091813 s (Passed); 5.8500869 s (Failed).
- B: 5.2071191 s (Passed); 4.2526599 s (Passed); 5.2888865 s (Passed).
- C: 4.3388418 s (Passed); 5.4608461 s (Passed); 3.5305434 s (Passed).

## Source and experiment identity

- Master was verified with `git ls-remote origin refs/heads/master` and pinned at `8c06c7e11f40cc224235ecbc68c2389913cc67ee` before any runs.
- Reviewed branch was pinned at `25fe10cebde5e8e17530d579a7c4e0b57f080515` (`feat/card-task-56fefc87`). No source on that branch was changed.
- `git cherry origin/master origin/feat/card-task-56fefc87` returned `4510b8ee700e71ce084e932a2d8e9e8b0ea291f9`, `8aa0333f00a956a3b945e0ec84727537d7ffe92d`, and `25fe10cebde5e8e17530d579a7c4e0b57f080515`.
- A: pinned master only; tree `a6642c562e3a5343574979afbfb6415c2e983e2b`.
- B: pinned master plus the exact source patches from `4510b8ee700e71ce084e932a2d8e9e8b0ea291f9` and `8aa0333f00a956a3b945e0ec84727537d7ffe92d`; tree `7a756cd7b946b7eec7e328d1d1e00777603a779c`.
- C: pinned master plus only `server/Infrastructure/HostLoad/HostLoadIngestService.cs` from `8aa0333f00a956a3b945e0ec84727537d7ffe92d`; tree `a21dc801f961e21e2ca31f073832bc0100e0a4d0`. This split applies cleanly and requires no unrelated clock changes.
- B deliberately excludes the generated impact-index-only third commit. The one selected class does not consume that index; no regeneration, driver build, or nightly re-pin was performed. This is a source-arm comparison rather than a byte-for-byte checkout of the reviewed tip.
- B/C were staged diagnostic patches in detached scratch worktrees based at the same pinned master. The task's assigned branch is the only publication branch. No branch was created, switched, rebased, amended, reset, merged or force-pushed.
- Same Linux x64 host/container `0c7fc078dc23`, kernel `4.15.0-213-generic`, 24 reported CPUs, SDK `10.0.404` selected by the pinned SDK's `latestMinor` roll-forward; test target `net9.0`.
- One build per arm, native default outputs, `UseAppHost=false` for both build and run. No source edits during any build or test. Each build/test takes its own host build-slot lease.
- Class filter is exactly `/*/*/C1514HostLoadHistoryTests/*`; Integration category, `NotInParallel("C1514HostLoadServer")`, `ParallelLimiter<ProcessSpawnLimit>`. No other test class or own test process was co-scheduled. Foreign host activity remained uncontrolled.
- Class order is A1,B1,C1,A2,B2,C2,A3,B3,C3. Each process uses the normal shared test PostgreSQL fixture and per-world cloned databases; image/schema key `00dff8e528e322e6270bde83f8049466`.
- Test limits were untouched: this method has a 10,000 ms whole-test limit, and its explicit INSERT-entry wait is five seconds. Production ingest and history-poll deadlines remain three seconds.

Raw task-local evidence is under `.antiphon/debug-7bd634da/`: per-run TRX, output log, source tree, start/end UTC, full `/proc/loadavg`, wall seconds and opt-in phase journal. Generated evidence is ignored. The tables and failure stacks below preserve the essential findings in this pushed report.

## Builds

Wall includes slot admission and driver startup; slot-held time and MSBuild elapsed are separate in the retained log. All durations are seconds.

| Arm | Exit | Wall | Slot wait | Slot held | Start load | End load |
|---|---:|---:|---:|---:|---|---|
| A | 0 | 350.988 | 0 | 349 | `20.27 15.24 17.64 9/4150 27400` | `31.69 27.39 22.47 35/4327 29632` |
| B | 0 | 510.954 | 285 | 224 | `16.80 20.69 21.26 33/4558 3578` | `21.01 23.52 22.51 14/4430 27016` |
| C | 0 | 438.627 | 150 | 287 | `9.21 14.63 18.93 22/4396 29806` | `30.33 28.38 24.05 7/4811 10545` |

Each build reported 0 errors and 2,091 warnings (including existing TUnit missing-cancellation-token warnings and analyzer warnings); these warnings are not a new failure attributed to B/C.

## Class results in execution order

Wall includes slot admission. TRX duration is the framework's test-run duration. The History column is that test's TRX duration, not class wall time. Load columns preserve the full five fields from `/proc/loadavg`.

| Run | Executed | Passed | Failed | Skipped | History seconds | History outcome | Wall seconds | Slot wait seconds | Start load | End load |
|---|---:|---:|---:|---:|---:|---|---:|---:|---|---|
| A-1 | 21 | 21 | 0 | 0 | 8.6584864 | Passed | 261.203 | 0 | `32.35 26.04 22.65 24/4790 13677` | `17.48 20.89 21.33 9/4558 1650` |
| B-1 | 21 | 21 | 0 | 0 | 5.2071191 | Passed | 284.967 | 90 | `20.13 23.30 22.44 9/4479 29948` | `9.32 14.74 18.99 9/4350 27387` |
| C-1 | 21 | 21 | 0 | 0 | 4.3388418 | Passed | 331.235 | 15 | `27.64 27.87 23.95 19/5063 12389` | `11.86 18.56 21.16 6/4323 7826` |
| A-2 | 21 | 21 | 0 | 0 | 3.6091813 | Passed | 272.511 | 45 | `11.86 18.56 21.16 10/4354 8325` | `9.96 14.00 18.62 9/4397 25676` |
| B-2 | 21 | 21 | 0 | 0 | 4.2526599 | Passed | 272.306 | 15 | `9.96 14.00 18.62 11/4391 25833` | `12.33 13.05 17.06 4/4275 17629` |
| C-2 | 21 | 20 | 1 | 0 | 5.4608461 | Passed | 246.703 | 0 | `12.33 13.05 17.06 13/4278 17731` | `14.98 13.04 16.00 6/4378 10072` |
| A-3 | 21 | 19 | 2 | 0 | 5.8500869 | Failed | 307.38 | 15 | `12.96 12.72 15.80 8/4496 19169` | `17.90 21.25 19.03 6/4348 10673` |
| B-3 | 21 | 20 | 1 | 0 | 5.2888865 | Passed | 291.298 | 0 | `17.90 21.25 19.03 8/4361 11861` | `34.72 30.31 23.14 6/4741 21061` |
| C-3 | 21 | 20 | 1 | 0 | 3.5305434 | Passed | 393.758 | 15 | `34.72 30.31 23.14 8/4723 21223` | `21.25 28.04 24.94 5/4554 27507` |

Three runs per arm are descriptive observations, **not statistics**. There is no randomized or controlled host-load experiment here. A difference between these samples cannot establish statistical significance or certify the absence of a flake. A failed case's duration is not a completed-work latency: A3's History duration ends at its early fixture IOException. Start load is measured before slot admission; the exact test-body-start load after a long queue wait is unknown.

### A-1: slowest five

| Test | Seconds | Outcome |
|---|---:|---|
| `RecoveryAtEveryHandoffPreservesReceipt(ingest-before-enqueue)` | 24.9374361 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(ingest-before-commit)` | 20.4238014 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(derive-after-commit)` | 18.0390363 | Passed |
| `ReadyRecipientReadsDurableSample` | 17.2220525 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(ingest-after-enqueue)` | 16.9692196 | Passed |

HistoryFailureDoesNotStopLiveSamples: **Passed**, 8.6584864 s.
Failure message and stack: none; this run passed.
### B-1: slowest five

| Test | Seconds | Outcome |
|---|---:|---|
| `ReadyRecipientReadsDurableSample` | 27.3560087 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(ingest-after-enqueue)` | 16.2355586 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(derive-before-call)` | 13.9451232 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(derive-before-save)` | 12.6511418 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(ingest-before-commit)` | 12.3797563 | Passed |

HistoryFailureDoesNotStopLiveSamples: **Passed**, 5.2071191 s.
Failure message and stack: none; this run passed.
### C-1: slowest five

| Test | Seconds | Outcome |
|---|---:|---|
| `ReadyRecipientReadsDurableSample` | 53.578456 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(derive-before-call)` | 22.0059523 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(derive-before-save)` | 21.2834504 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(derive-after-commit)` | 17.2218032 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(ingest-before-enqueue)` | 16.3324986 | Passed |

HistoryFailureDoesNotStopLiveSamples: **Passed**, 4.3388418 s.
Failure message and stack: none; this run passed.
### A-2: slowest five

| Test | Seconds | Outcome |
|---|---:|---|
| `ReadyRecipientReadsDurableSample` | 41.3220809 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(derive-before-call)` | 16.1361905 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(ingest-after-commit)` | 14.8213639 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(derive-after-write)` | 14.6320398 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(ingest-before-save)` | 13.5621448 | Passed |

HistoryFailureDoesNotStopLiveSamples: **Passed**, 3.6091813 s.
Failure message and stack: none; this run passed.
### B-2: slowest five

| Test | Seconds | Outcome |
|---|---:|---|
| `ReadyRecipientReadsDurableSample` | 38.0298209 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(derive-after-write)` | 22.3616964 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(ingest-before-enqueue)` | 19.5345905 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(derive-before-save)` | 18.2175669 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(derive-after-commit)` | 17.9686838 | Passed |

HistoryFailureDoesNotStopLiveSamples: **Passed**, 4.2526599 s.
Failure message and stack: none; this run passed.
### C-2: slowest five

| Test | Seconds | Outcome |
|---|---:|---|
| `RecoveryAtEveryHandoffPreservesReceipt(derive-before-save)` | 23.0343544 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(ingest-after-enqueue)` | 20.7362104 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(ingest-before-enqueue)` | 20.0525572 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(derive-after-commit)` | 17.2456761 | Passed |
| `ReadyRecipientReadsDurableSample` | 17.0794822 | Passed |

HistoryFailureDoesNotStopLiveSamples: **Passed**, 5.4608461 s.
Failure message and stack: none; this run passed.

Other non-passing result: `IdentityConflictsNeverOverwriteStoredSamples` (11.0646414 s, Failed).
```text
TimeoutException: Test 'IdentityConflictsNeverOverwriteStoredSamples' timed out after 00:00:10

--- Task Status: WaitingForActivation ---

--- Timeout Handler Stack Trace ---
Note: This is the timeout handler's stack trace, not the blocked test's stack.
   at System.Environment.get_StackTrace()
   at TUnit.Engine.Helpers.TimeoutDiagnostics.AppendStackTraceDiagnostics(StringBuilder sb)
   at TUnit.Engine.Helpers.TimeoutDiagnostics.BuildTimeoutDiagnosticsMessage(String baseMessage, Task executionTask)
   at TUnit.Engine.Helpers.TimeoutHelper.ExecuteWithTimeoutAsync(Func`2 taskFactory, TimeSpan timeout, CancellationToken cancellationToken, String timeoutMessage)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.ExecutionContextCallback(Object s)
   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext(Thread threadPoolThread)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext()
   at System.Threading.Tasks.AwaitTaskContinuation.RunOrScheduleAction(IAsyncStateMachineBox box, Boolean allowInlining)
   at System.Threading.Tasks.Task.RunContinuations(Object continuationObject)
   at System.Threading.Tasks.Task.FinishSlow(Boolean userDelegateExecute)
   at System.Threading.Tasks.Task.TrySetException(Object exceptionObject)
   at System.Threading.Tasks.Task.CancellationPromise`1.<>c.<.ctor>b__3_1(Object state)
   at System.Threading.TimerQueueTimer.Fire(Boolean isThreadPool)
   at System.Threading.TimerQueue.FireNextTimers()
   at System.Threading.ThreadPoolWorkQueue.Dispatch()
   at System.Threading.PortableThreadPool.WorkerThread.WorkerThreadStart()
   at TUnit.Engine.Helpers.TimeoutHelper.ExecuteWithTimeoutAsync(Func`2 taskFactory, TimeSpan timeout, CancellationToken cancellationToken, String timeoutMessage)
   at TUnit.Engine.TestExecutor.ExecuteAsync(AbstractExecutableTest executableTest, TestInitializer testInitializer, CancellationToken cancellationToken, Nullable`1 testTimeout)
   at TUnit.Engine.TestExecutor.ExecuteAsync(AbstractExecutableTest executableTest, TestInitializer testInitializer, CancellationToken cancellationToken, Nullable`1 testTimeout)
   at TUnit.Engine.Services.TestExecution.TestCoordinator.ExecuteTestAsync(AbstractExecutableTest test, CancellationToken cancellationToken)
   at TUnit.Engine.Services.TestExecution.TestCoordinator.ExecuteTestAsync(AbstractExecutableTest test, CancellationToken cancellationToken)
```
### A-3: slowest five

| Test | Seconds | Outcome |
|---|---:|---|
| `ReadyRecipientReadsDurableSample` | 39.3515598 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(ingest-after-commit)` | 26.2691321 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(derive-before-call)` | 25.804314 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(ingest-before-commit)` | 18.67258 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(derive-before-save)` | 17.7249298 | Passed |

HistoryFailureDoesNotStopLiveSamples: **Failed**, 5.8500869 s.

Failure message:
```text
IOException: host_load_history_busy
```

Stack:
```text
   at Antiphon.SessionRunner.HostLoadSpool.ReadHistory(String after, Int32 limit) in /work/worktrees/task-7bd634da-arm-a/src/Antiphon.SessionRunner/HostLoadSpool.cs:line 113
   at Antiphon.SessionRunner.HostLoadCaptureService.ReadHistory(String after, Int32 limit) in /work/worktrees/task-7bd634da-arm-a/src/Antiphon.SessionRunner/HostLoadCaptureService.cs:line 56
   at Antiphon.Tests.TestHelpers.C1514HostLoadWorld.ProduceAsync(DateTimeOffset at, Double cpu) in /work/worktrees/task-7bd634da-arm-a/tests/Antiphon.Tests/TestHelpers/C1514HostLoadFixture.cs:line 237
   at Antiphon.Tests.TestHelpers.C1514HostLoadWorld.SeedPrefixAsync() in /work/worktrees/task-7bd634da-arm-a/tests/Antiphon.Tests/TestHelpers/C1514HostLoadFixture.cs:line 241
   at Antiphon.Tests.Application.C1514HostLoadHistoryTests.HistoryFailureDoesNotStopLiveSamples() in /work/worktrees/task-7bd634da-arm-a/tests/Antiphon.Tests/Application/C1514HostLoadHistoryTests.cs:line 175
   at Antiphon.Tests.Application.C1514HostLoadHistoryTests.HistoryFailureDoesNotStopLiveSamples() in /work/worktrees/task-7bd634da-arm-a/tests/Antiphon.Tests/Application/C1514HostLoadHistoryTests.cs:line 185
   --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

Other non-passing result: `RawAndRollupsSurviveRetentionAndLateReplay` (11.0112877 s, Failed).
```text
TimeoutException: Test 'RawAndRollupsSurviveRetentionAndLateReplay' timed out after 00:00:10

--- Task Status: WaitingForActivation ---

--- Timeout Handler Stack Trace ---
Note: This is the timeout handler's stack trace, not the blocked test's stack.
   at System.Environment.get_StackTrace()
   at TUnit.Engine.Helpers.TimeoutDiagnostics.AppendStackTraceDiagnostics(StringBuilder sb)
   at TUnit.Engine.Helpers.TimeoutDiagnostics.BuildTimeoutDiagnosticsMessage(String baseMessage, Task executionTask)
   at TUnit.Engine.Helpers.TimeoutHelper.ExecuteWithTimeoutAsync(Func`2 taskFactory, TimeSpan timeout, CancellationToken cancellationToken, String timeoutMessage)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.ExecutionContextCallback(Object s)
   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext(Thread threadPoolThread)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext()
   at System.Threading.Tasks.AwaitTaskContinuation.RunOrScheduleAction(IAsyncStateMachineBox box, Boolean allowInlining)
   at System.Threading.Tasks.Task.RunContinuations(Object continuationObject)
   at System.Threading.Tasks.Task.FinishSlow(Boolean userDelegateExecute)
   at System.Threading.Tasks.Task.TrySetException(Object exceptionObject)
   at System.Threading.Tasks.Task.CancellationPromise`1.<>c.<.ctor>b__3_1(Object state)
   at System.Threading.TimerQueueTimer.Fire(Boolean isThreadPool)
   at System.Threading.TimerQueue.FireNextTimers()
   at System.Threading.ThreadPoolWorkQueue.Dispatch()
   at System.Threading.PortableThreadPool.WorkerThread.WorkerThreadStart()
   at TUnit.Engine.Helpers.TimeoutHelper.ExecuteWithTimeoutAsync(Func`2 taskFactory, TimeSpan timeout, CancellationToken cancellationToken, String timeoutMessage)
   at TUnit.Engine.TestExecutor.ExecuteAsync(AbstractExecutableTest executableTest, TestInitializer testInitializer, CancellationToken cancellationToken, Nullable`1 testTimeout)
   at TUnit.Engine.TestExecutor.ExecuteAsync(AbstractExecutableTest executableTest, TestInitializer testInitializer, CancellationToken cancellationToken, Nullable`1 testTimeout)
   at TUnit.Engine.Services.TestExecution.TestCoordinator.ExecuteTestAsync(AbstractExecutableTest test, CancellationToken cancellationToken)
   at TUnit.Engine.Services.TestExecution.TestCoordinator.ExecuteTestAsync(AbstractExecutableTest test, CancellationToken cancellationToken)
```
### B-3: slowest five

| Test | Seconds | Outcome |
|---|---:|---|
| `ReadyRecipientReadsDurableSample` | 37.9245038 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(derive-before-call)` | 27.0445093 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(derive-after-write)` | 23.9350183 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(derive-before-save)` | 23.0866617 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(derive-after-commit)` | 23.0374773 | Passed |

HistoryFailureDoesNotStopLiveSamples: **Passed**, 5.2888865 s.
Failure message and stack: none; this run passed.

Other non-passing result: `PrimaryEpochsDoNotDoubleCount` (11.0199204 s, Failed).
```text
TimeoutException: Test 'PrimaryEpochsDoNotDoubleCount' timed out after 00:00:10

--- Task Status: WaitingForActivation ---

--- Timeout Handler Stack Trace ---
Note: This is the timeout handler's stack trace, not the blocked test's stack.
   at System.Environment.get_StackTrace()
   at TUnit.Engine.Helpers.TimeoutDiagnostics.AppendStackTraceDiagnostics(StringBuilder sb)
   at TUnit.Engine.Helpers.TimeoutDiagnostics.BuildTimeoutDiagnosticsMessage(String baseMessage, Task executionTask)
   at TUnit.Engine.Helpers.TimeoutHelper.ExecuteWithTimeoutAsync(Func`2 taskFactory, TimeSpan timeout, CancellationToken cancellationToken, String timeoutMessage)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.ExecutionContextCallback(Object s)
   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext(Thread threadPoolThread)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext()
   at System.Threading.Tasks.AwaitTaskContinuation.RunOrScheduleAction(IAsyncStateMachineBox box, Boolean allowInlining)
   at System.Threading.Tasks.Task.RunContinuations(Object continuationObject)
   at System.Threading.Tasks.Task.FinishSlow(Boolean userDelegateExecute)
   at System.Threading.Tasks.Task.TrySetException(Object exceptionObject)
   at System.Threading.Tasks.Task.CancellationPromise`1.<>c.<.ctor>b__3_1(Object state)
   at System.Threading.TimerQueueTimer.Fire(Boolean isThreadPool)
   at System.Threading.TimerQueue.FireNextTimers()
   at System.Threading.ThreadPoolWorkQueue.Dispatch()
   at System.Threading.PortableThreadPool.WorkerThread.WorkerThreadStart()
   at TUnit.Engine.Helpers.TimeoutHelper.ExecuteWithTimeoutAsync(Func`2 taskFactory, TimeSpan timeout, CancellationToken cancellationToken, String timeoutMessage)
   at TUnit.Engine.TestExecutor.ExecuteAsync(AbstractExecutableTest executableTest, TestInitializer testInitializer, CancellationToken cancellationToken, Nullable`1 testTimeout)
   at TUnit.Engine.TestExecutor.ExecuteAsync(AbstractExecutableTest executableTest, TestInitializer testInitializer, CancellationToken cancellationToken, Nullable`1 testTimeout)
   at TUnit.Engine.Services.TestExecution.TestCoordinator.ExecuteTestAsync(AbstractExecutableTest test, CancellationToken cancellationToken)
   at TUnit.Engine.Services.TestExecution.TestCoordinator.ExecuteTestAsync(AbstractExecutableTest test, CancellationToken cancellationToken)
```
### C-3: slowest five

| Test | Seconds | Outcome |
|---|---:|---|
| `ReadyRecipientReadsDurableSample` | 59.2734125 | Passed |
| `BusyRecipientEventuallyReadsSameSample` | 45.4684637 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(derive-before-call)` | 25.2352919 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(derive-after-write)` | 23.7576843 | Passed |
| `RecoveryAtEveryHandoffPreservesReceipt(ingest-after-enqueue)` | 22.3571491 | Passed |

HistoryFailureDoesNotStopLiveSamples: **Passed**, 3.5305434 s.
Failure message and stack: none; this run passed.

Other non-passing result: `PrimaryEpochsDoNotDoubleCount` (10.7995692 s, Failed).
```text
TimeoutException: Test 'PrimaryEpochsDoNotDoubleCount' timed out after 00:00:10

--- Task Status: RanToCompletion ---

--- Timeout Handler Stack Trace ---
Note: This is the timeout handler's stack trace, not the blocked test's stack.
   at System.Environment.get_StackTrace()
   at TUnit.Engine.Helpers.TimeoutDiagnostics.AppendStackTraceDiagnostics(StringBuilder sb)
   at TUnit.Engine.Helpers.TimeoutDiagnostics.BuildTimeoutDiagnosticsMessage(String baseMessage, Task executionTask)
   at TUnit.Engine.Helpers.TimeoutHelper.ExecuteWithTimeoutAsync(Func`2 taskFactory, TimeSpan timeout, CancellationToken cancellationToken, String timeoutMessage)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.ExecutionContextCallback(Object s)
   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext(Thread threadPoolThread)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext()
   at System.Threading.Tasks.AwaitTaskContinuation.RunOrScheduleAction(IAsyncStateMachineBox box, Boolean allowInlining)
   at System.Threading.Tasks.Task.RunContinuations(Object continuationObject)
   at System.Threading.Tasks.Task.CancellationPromise`1.System.Threading.Tasks.ITaskCompletionAction.Invoke(Task completingTask)
   at System.Threading.Tasks.Task.RunContinuations(Object continuationObject)
   at System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder.SetResult()
   at TUnit.Engine.TestExecutor.ExecuteTestAsync(AbstractExecutableTest executableTest, CancellationToken cancellationToken)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.ExecutionContextCallback(Object s)
   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext(Thread threadPoolThread)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext()
   at System.Threading.Tasks.AwaitTaskContinuation.RunOrScheduleAction(IAsyncStateMachineBox box, Boolean allowInlining)
   at System.Threading.Tasks.Task.RunContinuations(Object continuationObject)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.SetExistingTaskResult(Task`1 task, TResult result)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder.SetResult()
   at TUnit.Core.ExecutableTest.InvokeTestAsync(Object instance, CancellationToken cancellationToken)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.ExecutionContextCallback(Object s)
   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext(Thread threadPoolThread)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext()
   at System.Threading.Tasks.AwaitTaskContinuation.RunOrScheduleAction(IAsyncStateMachineBox box, Boolean allowInlining)
   at System.Threading.Tasks.Task.RunContinuations(Object continuationObject)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.SetExistingTaskResult(Task`1 task, TResult result)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder.SetResult()
   at TUnit.Engine.Discovery.ReflectionTestMetadata.<>c__DisplayClass6_0.<<CreateExecutableTest>b__1>d.MoveNext()
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.ExecutionContextCallback(Object s)
   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext(Thread threadPoolThread)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext()
   at System.Threading.Tasks.AwaitTaskContinuation.RunOrScheduleAction(IAsyncStateMachineBox box, Boolean allowInlining)
   at System.Threading.Tasks.Task.RunContinuations(Object continuationObject)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.SetExistingTaskResult(Task`1 task, TResult result)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder.SetResult()
   at Antiphon.Tests.Application.C1514HostLoadHistoryTests.PrimaryEpochsDoNotDoubleCount() in /work/worktrees/task-7bd634da-arm-c/tests/Antiphon.Tests/Application/C1514HostLoadHistoryTests.cs:line 136
   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext(Thread threadPoolThread)
   at System.Threading.Tasks.AwaitTaskContinuation.RunOrScheduleAction(IAsyncStateMachineBox box, Boolean allowInlining)
   at System.Threading.Tasks.Task.RunContinuations(Object continuationObject)
   at System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder.SetResult()
   at Antiphon.Tests.TestHelpers.C1514HostLoadWorld.DisposeAsync() in /work/worktrees/task-7bd634da-arm-c/tests/Antiphon.Tests/TestHelpers/C1514HostLoadFixture.cs:line 296
   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext(Thread threadPoolThread)
   at System.Threading.Tasks.AwaitTaskContinuation.RunOrScheduleAction(IAsyncStateMachineBox box, Boolean allowInlining)
   at System.Threading.Tasks.Task.RunContinuations(Object continuationObject)
   at System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder.SetResult()
   at Antiphon.Tests.TestHelpers.IsolatedTestSchema.DisposeAsync() in /work/worktrees/task-7bd634da-arm-c/tests/Antiphon.Tests/TestHelpers/TestDbFixture.cs:line 146
   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext(Thread threadPoolThread)
   at System.Threading.Tasks.AwaitTaskContinuation.RunOrScheduleAction(IAsyncStateMachineBox box, Boolean allowInlining)
   at System.Threading.Tasks.Task.RunContinuations(Object continuationObject)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.SetExistingTaskResult(Task`1 task, TResult result)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder.SetResult()
   at Antiphon.Tests.TestHelpers.TestDbFixtureLifecycle.DropClonedDatabaseAsync(String databaseName) in /work/worktrees/task-7bd634da-arm-c/tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs:line 483
   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext(Thread threadPoolThread)
   at System.Threading.Tasks.AwaitTaskContinuation.RunOrScheduleAction(IAsyncStateMachineBox box, Boolean allowInlining)
   at System.Threading.Tasks.Task.RunContinuations(Object continuationObject)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.SetExistingTaskResult(Task`1 task, TResult result)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder.SetResult()
   at Antiphon.Tests.TestHelpers.TestDbOperations.DropAsync(TestDbReadyState ready, String databaseName) in /work/worktrees/task-7bd634da-arm-c/tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs:line 194
   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext(Thread threadPoolThread)
   at System.Threading.Tasks.AwaitTaskContinuation.RunOrScheduleAction(IAsyncStateMachineBox box, Boolean allowInlining)
   at System.Threading.Tasks.Task.RunContinuations(Object continuationObject)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.SetExistingTaskResult(Task`1 task, TResult result)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder.SetResult()
   at Antiphon.Tests.TestHelpers.TestDbFixtureLifecycle.ExecuteNonQueryAsync(NpgsqlConnection connection, String sql) in /work/worktrees/task-7bd634da-arm-c/tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs:line 848
   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext(Thread threadPoolThread)
   at System.Threading.Tasks.AwaitTaskContinuation.RunOrScheduleAction(IAsyncStateMachineBox box, Boolean allowInlining)
   at System.Threading.Tasks.Task.RunContinuations(Object continuationObject)
   at Npgsql.NpgsqlCommand.ExecuteNonQuery(Boolean async, CancellationToken cancellationToken)
   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext(Thread threadPoolThread)
   at System.Threading.Tasks.AwaitTaskContinuation.RunOrScheduleAction(IAsyncStateMachineBox box, Boolean allowInlining)
   at System.Threading.Tasks.Task.RunContinuations(Object continuationObject)
   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)
   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext(Thread threadPoolThread)
   at System.Threading.Tasks.AwaitTaskContinuation.RunOrScheduleAction(IAsyncStateMachineBox box, Boolean allowInlining)
   at System.Threading.Tasks.Task.RunContinuations(Object continuationObject)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.SetExistingTaskResult(Task`1 task, TResult result)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.SetResult(TResult result)
   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)
   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext(Thread threadPoolThread)
   at Npgsql.Internal.NpgsqlConnector.ReadMessageLong(Boolean async, DataRowLoadingMode dataRowLoadingMode, Boolean readingNotifications, Boolean isReadingPrependedMessage)
   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext, ContextCallback callback, Object state)
   at Npgsql.Internal.NpgsqlReadBuffer.<Ensure>g__EnsureLong|55_0(NpgsqlReadBuffer buffer, Int32 count, Boolean async, Boolean readingNotifications)
   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Net.Sockets.SocketAsyncEventArgs.FinishOperationAsyncSuccess(Int32 bytesTransferred, SocketFlags flags)
   at System.Net.Sockets.SocketAsyncEventArgs.TransferCompletionCallbackCore(Int32 bytesTransferred, Memory`1 socketAddress, SocketFlags receivedFlags, SocketError socketError)
   at System.Net.Sockets.SocketAsyncEngine.System.Threading.IThreadPoolWorkItem.Execute()
   at System.Threading.ThreadPoolWorkQueue.Dispatch()
   at System.Threading.PortableThreadPool.WorkerThread.WorkerThreadStart()
   at TUnit.Engine.Helpers.TimeoutHelper.ExecuteWithTimeoutAsync(Func`2 taskFactory, TimeSpan timeout, CancellationToken cancellationToken, String timeoutMessage)
   at TUnit.Engine.TestExecutor.ExecuteAsync(AbstractExecutableTest executableTest, TestInitializer testInitializer, CancellationToken cancellationToken, Nullable`1 testTimeout)
   at TUnit.Engine.TestExecutor.ExecuteAsync(AbstractExecutableTest executableTest, TestInitializer testInitializer, CancellationToken cancellationToken, Nullable`1 testTimeout)
   at TUnit.Engine.Services.TestExecution.TestCoordinator.ExecuteTestAsync(AbstractExecutableTest test, CancellationToken cancellationToken)
   at TUnit.Engine.Services.TestExecution.TestCoordinator.ExecuteTestAsync(AbstractExecutableTest test, CancellationToken cancellationToken)
```

## What the ten seconds actually covers

Read `tests/Antiphon.Tests/Application/C1514HostLoadHistoryTests.cs`, method `HistoryFailureDoesNotStopLiveSamples`, together with `tests/Antiphon.Tests/TestHelpers/C1514HostLoadFixture.cs`, `C1514HostLoadSqlProbe.cs`, and the production `HostLoadIngestPump`, `HostLoadIngestService`, `HostLoadCaptureService` and `HostLoadSpool`.

1. The 10 s attribute bounds the whole test body, including its `await using` teardown. It is not a timeout on a single product query, progress notification or hosted-service tick. The test has no cancellation-token parameter; TUnit's whole-test deadline does not become a token passed into these operations. Numerous calls explicitly use `CancellationToken.None`.
2. `WorldAsync` takes a cloned database, initializes private spool/services, connects a scripted phone-home peer and warms a real Npgsql connection/cursor query. The class-level readiness hook warms the shared fixture separately.
3. `SeedPrefixAsync` produces **100** samples sequentially. Every `ProduceAsync` calls the live sampler and polls `Capture.LastAppendedAt` with a real `Task.Delay(5)` until the background append worker has persisted the sample. Each such fixture poll has its own 15 s cancellation bound. That is repeated scheduling, disk append/flush and polling work inside the test's unrelated 10 s total. Then seeding ingests and derives the prefix before producing sample 101.
4. The SQL probe **holds**, rather than throws on, the first `INSERT INTO "HostLoadSamples"`. `gate.Entered.Task.WaitAsync(5 s)` waits for the command interceptor to reach that INSERT. The query replacing `IdentityPredicate` has already completed at this point. Probe `Fail` is never set in this test.
5. While the INSERT is held, the test produces sample 102, waits for its spool append, then asserts the live timestamp and durable spool receipt. It starts the background pump and asserts `StartAsync` completes immediately, as promised by `ExecuteAsync` yielding before work. It does not wait for the five-second periodic tick.
6. The `finally` releases the probe, joins the held ingest and stops the pump; it then recreates services, transfers/derives the remaining sample, checks two durable rows, and disposes the world/database/spool.

There is **no expected wall-clock retry/backoff wait in this scenario**. The ingest pump consumes immediately and polls immediately before its periodic tick; cancellation wakes its waits during stop. The capture worker's five-second retry delay is for spool I/O failures, which this scenario does not inject. Three-second production connection/transaction/poll deadlines can still fire if those individual operations are delayed, but they are distinct from the 10 s whole-test watchdog.

### Failure-path coverage gap

The test name overstates its witness: this method proves live sampling continues while history persistence is blocked, then proves recovery after releasing the block. It does **not** inject a history persistence failure, observe `history_commit_unavailable`, or prove replay after a failed save. In production `ConsumeAsync` catches a failed ingest, sets that status and preserves the committed frontier; later polling replays from the unchanged cursor. No exponential backoff is introduced by the unnest change. This coverage gap predates the reviewed branch and is a separate test-quality finding, not evidence of a new product regression.

### Query round trips and plan

Both arms perform: open/begin transaction; advisory transaction lock; cursor lookup; **one** identity lookup; save pending changes; commit. `MatchingIdentities` replaces that one identity lookup with a single SQL statement. `unnest` is a PostgreSQL set-returning function inside that statement, not another client round trip. Its three arrays are zipped key components; `EXISTS` returns each stored identity once, even for repeated requested keys. The table being searched is `HostLoadSampleIdentities`, not the raw sample payload table `HostLoadSamples`.

The original query builds a different OR expression containing literal store/boot/sequence constants for every key set. The new statement parameterizes three arrays and keeps one SQL shape for 0..120 keys. Matching uses the composite primary key `(RunnerStoreId, BootId, Sequence)`. The full branch's unrelated clock-hoisting changes are outside this method's ingest/poll path; C isolates the replacement lookup.

## PostgreSQL EXPLAIN experiment

Not run: build-slot admission timed out after 15 seconds at queue position 3 (all 4 slots occupied). No EXPLAIN, realistic-table measurement or PostgreSQL plan-performance claim is available. Complete only this exact missing diagnostic once under a granted lease; the nine class comparison runs do not need repeating merely to fill this gap.

## Repair recommendation and limits

Route a separate test-fixture Code repair; do not alter the product lookup based on this evidence. The original backoff hypothesis is false for this scenario, and the original HistoryFailure timeout was not reproduced, so this Debug task does not introduce an unproved deadline change or increase a timeout.

Concrete test-side repair scope:

1. Make `ProduceAsync` wait for the complete durable receipt it will return, not just `LastAppendedAt` followed by a separate fallible `ReadHistory`. The baseline stack witnesses that gap. Preserve the final identity/sequence and spool assertions; a callback at `after-flush` alone is too early because the ledger replacement and capture progress assignment follow it.
2. Give each named setup/ingest/live/replay step an observable completion receipt and a bounded progress deadline (the `CheckpointStepDeadline` pattern), with an injected monotonic clock for deterministic deadline tests. Keep the product's three-second ingest/poll contract independent from fixture setup. Measure loaded phases before deriving a revised outer watchdog; three samples do not supply a trusted p95.
3. Propagate the TUnit cancellation token through fixture operations and join an expired test body's workers before allowing the shared fixture's mutable `Current` to advance. Preserve the C2 invalid-cursor and B3 late-count witnesses as separate post-timeout evidence, not as replacements for the TRX timeout causes.
4. Reduce unnecessary prefix work in the specific stalled-write witness, while retaining an already committed frontier, or design proper once-per-run seed snapshots. The current 100 real durable appends per case need a budget and explicit setup ownership. Simply moving an early after-flush callback ahead of the final receipt would weaken the witness.
5. Add a distinct real persistence-exception/replay witness: inject `Fail`, observe commit failure, prove that live sampling continues and the durable cursor stays unchanged, then prove replay commits the missing samples. The present `Hold` witness should retain its stalled-write meaning.

No test-side repair was implemented from these incomplete causal measurements. In particular, the narrower conditional instruction to fix an observed wall-clock backoff does not apply: this test awaits no expected backoff, and its baseline red is an early spool-read exception. A new Code task can implement and validate the concrete fixture findings without changing the reviewed product branch. The existing HistoryFailure timeout and additional branch-arm timeouts stay unresolved until that repair/instrumentation supplies evidence.

The existing phase journal records container start, shared model setup, clone/drop and crash-worker initialization. It does not place individual phases around all 100 seed appends, the held-ingest boundary, live-sample production and final replay. Their internal split remains **unknown/remainder**, not zero. Class TRX timings include costs that cannot all be assigned from the existing recorder. No unmeasured phase is presented as a measured product latency.

The initial wrapper had two diagnostic-only failures, preserved separately: (a) PowerShell remained busy before launching a driver when given the wildcard filter through native invocation; only that owned wrapper, with no children, was terminated; (b) its first ProcessStartInfo replacement omitted WorkingDirectory and `dotnet run --no-build` refused the unbuilt task checkout as non-runnable. Neither executed a test or counts as a red sample. The corrected wrapper uses literal ArgumentList and the explicit arm WorkingDirectory. All rows above have real TRX and their own granted build-slot lease.

### Baseline A3 failure and C2 continuing task

A3 is pinned master with no branch source changes. Its `RawAndRollupsSurviveRetentionAndLateReplay` hit the 10 s watchdog at 11.0112877 s; the following HistoryFailure case then failed at 5.8500869 s with `IOException: host_load_history_busy`. The History stack points to `HostLoadSpool.ReadHistory` line 113, called from `ProduceAsync` line 237 within `SeedPrefixAsync` line 241 and HistoryFailure line 175. This happens during the 100-sample prefix, before this case installs the INSERT-holding probe or executes the persistence scenario being attributed. It proves a pre-existing baseline red in the same method, with a different failure mode from the commissioning Review's timeout. A causal cascade from the preceding uncancelled timed-out body is plausible but not proved by that stack.

C2's real TRX reports `IdentityConflictsNeverOverwriteStoredSamples` Failed at 11.0646414 s with a 10 s timeout. Its timeout message explicitly labels the captured stack as the **timeout handler's stack, not the blocked test's stack**. HistoryFailure passed at 5.4608461 s. C2's process then emitted an unobserved `invalid_host_load_cursor` exception from the timed-out Identity method, at `HostLoadSpool.Decode` -> `ReadHistory` -> `HostLoadCaptureService.ReadHistory` -> `IdentityConflictsNeverOverwriteStoredSamples` lines 104/110. The test has no cancellation-token parameter and the fixture routes through mutable `Current`; allowing a timed-out body to continue into the next world is a fixture-isolation risk. That mechanism is an inference from the code and late exception, not a measured blocked-thread stack. C2 exited 2, so the shell loop stopped; the planned A3,B3,C3 sequence was then resumed without repeating C2 or altering source.

The full C2 TRX failure message and engine stack are retained in its result section above. The later exception is additional diagnostic evidence; it is not relabelled as the TRX failure cause. This also reproduces one of the *other* timeout names from the commissioning Review. Its product-versus-fixture attribution remains separate from the HistoryFailure question.


B3's TRX timeout was PrimaryEpochsDoNotDoubleCount at 11.0199204 s. Its later unobserved assertion expected two rows at T0 and observed one (lines 123/135), after the timeout had been declared. The late assertion is not the TRX failure cause and is not proof that the new identity query dropped a row.

## Reproduction

Use detached scratch worktrees at the pinned master, then apply the exact source deltas with `git diff 4510b8ee7^ 8aa0333f0` for B and `git diff 8aa0333f0^ 8aa0333f0 -- server/Infrastructure/HostLoad/HostLoadIngestService.cs` for C. Do not change the reviewed branch. Record the resulting tree IDs above before building. From each scratch root:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label C1514-arm-build -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label C1514-arm-run -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/*/C1514HostLoadHistoryTests/*' --report-trx --report-trx-filename run.trx --results-directory /absolute/unique/results
```

Capture `/proc/loadavg` and UTC immediately before and after each driver, distinguish admission from execution, and retain the full TRX. Run A,B,C,A,B,C,A,B,C serially. A PowerShell orchestrating wrapper should use `ProcessStartInfo.ArgumentList` so the slash/wildcard filter is passed literally, and set `WorkingDirectory` explicitly.

## Restoration and publication

The staged B/C experimental patches were reversed with their exact retained patches after every test process exited. A/B/C then each had empty git status. All three task-owned detached scratch worktrees were removed with exact literal absolute git-worktree paths; no force/recursive shell delete or git clean was used. No EXPLAIN container was created because the gate denied admission. The assigned task worktree retains ignored raw evidence and the only deliverable is this report.

Only this investigation is a deliverable change. No product/test timeout, production implementation, test assertion, impact index or nightly pin was changed. Protocol commit: `595d77b11725172b8e7fd24a533e1690f4d3c93b`. Final report publication is recorded in the task's completion message.

## Additional C3 evidence

C3 PrimaryEpochsDoNotDoubleCount timed out at 10.7995692 s. Its diagnostic says Task Status RanToCompletion, and the captured continuation stack includes world disposal, IsolatedTestSchema.DisposeAsync, DropClonedDatabaseAsync and the database drop command. This is a timeout-handler snapshot during completion/teardown, not a blocked-body stack. It adds evidence that the whole 10 s budget includes teardown; its query-versus-fixture attribution remains unresolved.

## Prepared missing diagnostic (original report; executed in continuation below)

The following SQL was prepared but **never executed**. It targets the actual migrated table in a fresh container from test schema image 00dff8e528e322e6270bde83f8049466. Use the same volume-copy entrypoint as TestDbBakedImages.RunFromVolume, max_connections=500, no host ports and a fresh task-owned container. Do not use a live database. Record start/end load and SQL plans, then remove only that owned container. This is the sole missing requested execution.

```sql
\set ON_ERROR_STOP on
\timing on
SET statement_timeout='30s';
SELECT version();
DO $$ BEGIN
 IF EXISTS (SELECT 1 FROM "HostLoadSampleIdentities") THEN RAISE EXCEPTION 'expected empty test-image identity table'; END IF;
END $$;
INSERT INTO "HostLoadSampleIdentities"
 SELECT md5('store0')::uuid, md5('boot0')::uuid, g, repeat('a',64), now() FROM generate_series(1,100) g;
ANALYZE "HostLoadSampleIdentities";
PREPARE identities(uuid[],uuid[],bigint[]) AS
 SELECT i."RunnerStoreId", i."BootId", i."Sequence", i."PayloadHash", i."IngestedAt"
 FROM "HostLoadSampleIdentities" AS i
 WHERE EXISTS (SELECT 1 FROM unnest($1,$2,$3) AS k(store,boot,seq)
 WHERE i."RunnerStoreId"=k.store AND i."BootId"=k.boot AND i."Sequence"=k.seq);
SELECT ARRAY[md5('store0')::uuid] AS stores,ARRAY[md5('boot0')::uuid] AS boots,ARRAY[101::bigint] AS seqs \gset
\echo FIXTURE_100_MISSING_101
EXPLAIN (ANALYZE,BUFFERS,SETTINGS) EXECUTE identities(:'stores'::uuid[],:'boots'::uuid[],:'seqs'::bigint[]);
INSERT INTO "HostLoadSampleIdentities"
 SELECT md5('store'||((g-1)/120960))::uuid,md5('boot'||((g-1)/120960))::uuid,(g-1)%120960+1,repeat('a',64),now()
 FROM generate_series(101,1000000) g;
ANALYZE "HostLoadSampleIdentities";
SELECT count(*),pg_size_pretty(pg_total_relation_size('"HostLoadSampleIdentities"')) FROM "HostLoadSampleIdentities";
\echo MILLION_1_MATCH
EXPLAIN (ANALYZE,BUFFERS,SETTINGS) EXECUTE identities(:'stores'::uuid[],:'boots'::uuid[],:'seqs'::bigint[]);
SELECT ARRAY[md5('store8')::uuid] AS stores,ARRAY[md5('boot8')::uuid] AS boots,ARRAY[121000::bigint] AS seqs \gset
\echo MILLION_1_MISS
EXPLAIN (ANALYZE,BUFFERS,SETTINGS) EXECUTE identities(:'stores'::uuid[],:'boots'::uuid[],:'seqs'::bigint[]);
SELECT array_agg(md5('store0')::uuid) AS stores,array_agg(md5('boot0')::uuid) AS boots,array_agg(g::bigint) AS seqs FROM generate_series(1,100) g \gset
\echo MILLION_100_MATCHES
EXPLAIN (ANALYZE,BUFFERS,SETTINGS) EXECUTE identities(:'stores'::uuid[],:'boots'::uuid[],:'seqs'::bigint[]);
SELECT array_agg(md5('store0')::uuid) AS stores,array_agg(md5('boot0')::uuid) AS boots,array_agg(g::bigint) AS seqs FROM generate_series(120901,121020) g \gset
\echo MILLION_120_MIXED
EXPLAIN (ANALYZE,BUFFERS,SETTINGS) EXECUTE identities(:'stores'::uuid[],:'boots'::uuid[],:'seqs'::bigint[]);
SET plan_cache_mode=force_generic_plan;
\echo MILLION_120_GENERIC
EXPLAIN (ANALYZE,BUFFERS,SETTINGS) EXECUTE identities(:'stores'::uuid[],:'boots'::uuid[],:'seqs'::bigint[]);
\echo MILLION_120_REPEATED
EXPLAIN (ANALYZE,BUFFERS,SETTINGS) EXECUTE identities(:'stores'::uuid[],:'boots'::uuid[],:'seqs'::bigint[]);

```

## Reopened task: EXPLAIN completed, 2026-11-14 05:06 UTC

The caller accepted the original attribution and commissioned a test-side repair, with a new 60-minute hard stop. The historical original-report statements about missing EXPLAIN and no test changes describe that earlier outcome; this continuation supersedes them.

The wrapper accepts any double for SlotWaitMinutes and defines no finite maximum. This execution used the documented 45-minute admission budget, bounded by the caller deadline. A real lease was granted after 301 seconds; held 42 seconds; authoritative driver exit 0. No unleased command ran. The task-owned, portless container used the shared test-fixture image antiphon-test-pg:00dff8e528e322e6270bde83f8049466, PostgreSQL 16.15, the migrated identity table and its real primary key. It copied the baked fixture volume in the usual fixture manner, created one million identities across runner/store epochs (239 MB including indexes), ANALYZEd them, and executed the exact prepared EXISTS/unnest statement. The owned container and anonymous volume were removed after psql exited. No live database was touched.

SQL/cleanup wall: 41.357298 s, 05:05:48.7028510–05:06:30.0844484 UTC. Start load: 35.32 32.42 27.85; end load: 29.10 30.92 27.52. Admission is separate from this wall duration.

| Case | Plan | Returned | Execution ms | Execution shared hit/read/written | Planning shared hit/read |
|---|---|---:|---:|---|---|
| 100 identities, missing 101 | Hash semi join, tiny sequential scan | 0 | 0.205 | 2/0/0 | 125/1 |
| 1M, one match | Nested loop + composite PK index | 1 | 0.249 | 3/1/1 | 9/0 |
| 1M, one miss | Nested loop + composite PK index | 0 | 0.131 | 3/0/0 | not reported |
| 1M, 100 matches | Nested loop + memoized PK probes | 100 | 1.106 | 399/1/1 | not reported |
| 1M, 120 keys mixed | Nested loop + memoized PK probes | 60 | 1.288 | 416/4/4 | not reported |
| 1M, 120 keys generic | Nested loop + composite PK index | 60 | 0.971 | 420/0/0 | not reported |
| 1M, repeated generic | Nested loop + composite PK index | 60 | 0.655 | 420/0/0 | not reported |

These are top-node execution buffer totals; nested node counts are inclusive and must not be added again. This sample supplies no evidence of a long plan on the identity lookup. It does not measure production network latency, lock contention or the blocked INSERT. The query still makes one identity SELECT, not a second round trip. Full EXPLAIN output follows (trailing display padding trimmed) (psql timings include statement/client overhead).

``text
Timing is on.
SET
Time: 15.755 ms
                                         version
------------------------------------------------------------------------------------------
 PostgreSQL 16.15 on x86_64-pc-linux-musl, compiled by gcc (Alpine 15.2.0) 15.2.0, 64-bit
(1 row)

Time: 2.184 ms
DO
Time: 13.876 ms
INSERT 0 100
Time: 24.333 ms
ANALYZE
Time: 4.255 ms
PREPARE
Time: 1.104 ms
Time: 0.225 ms
FIXTURE_100_MISSING_101
                                                            QUERY PLAN
----------------------------------------------------------------------------------------------------------------------------------
 Hash Semi Join  (cost=0.04..3.84 rows=1 width=113) (actual time=0.119..0.120 rows=0 loops=1)
   Hash Cond: ((i."RunnerStoreId" = k.store) AND (i."BootId" = k.boot) AND (i."Sequence" = k.seq))
   Buffers: shared hit=2
   ->  Seq Scan on "HostLoadSampleIdentities" i  (cost=0.00..3.00 rows=100 width=113) (actual time=0.008..0.023 rows=100 loops=1)
         Buffers: shared hit=2
   ->  Hash  (cost=0.02..0.02 rows=1 width=40) (actual time=0.042..0.043 rows=1 loops=1)
         Buckets: 1024  Batches: 1  Memory Usage: 9kB
         ->  Function Scan on k  (cost=0.01..0.02 rows=1 width=40) (actual time=0.032..0.032 rows=1 loops=1)
 Planning:
   Buffers: shared hit=125 read=1
 Planning Time: 1.256 ms
 Execution Time: 0.205 ms
(12 rows)

Time: 1.699 ms
INSERT 0 999900
Time: 25696.319 ms (00:25.696)
ANALYZE
Time: 319.538 ms
  count  | pg_size_pretty
---------+----------------
 1000000 | 239 MB
(1 row)

Time: 77.844 ms
MILLION_1_MATCH
                                                                             QUERY PLAN
--------------------------------------------------------------------------------------------------------------------------------------------------------------------
 Nested Loop  (cost=0.45..8.48 rows=1 width=113) (actual time=0.113..0.115 rows=1 loops=1)
   Buffers: shared hit=3 read=1 written=1
   ->  HashAggregate  (cost=0.03..0.04 rows=1 width=40) (actual time=0.039..0.040 rows=1 loops=1)
         Group Key: k.store, k.boot, k.seq
         Batches: 1  Memory Usage: 24kB
         ->  Function Scan on k  (cost=0.01..0.02 rows=1 width=40) (actual time=0.029..0.030 rows=1 loops=1)
   ->  Index Scan using "PK_HostLoadSampleIdentities" on "HostLoadSampleIdentities" i  (cost=0.42..8.45 rows=1 width=113) (actual time=0.068..0.069 rows=1 loops=1)
         Index Cond: (("RunnerStoreId" = k.store) AND ("BootId" = k.boot) AND ("Sequence" = k.seq))
         Buffers: shared hit=3 read=1 written=1
 Planning:
   Buffers: shared hit=9
 Planning Time: 0.728 ms
 Execution Time: 0.249 ms
(13 rows)

Time: 1.393 ms
Time: 0.254 ms
MILLION_1_MISS
                                                                             QUERY PLAN
--------------------------------------------------------------------------------------------------------------------------------------------------------------------
 Nested Loop  (cost=0.45..8.48 rows=1 width=113) (actual time=0.053..0.054 rows=0 loops=1)
   Buffers: shared hit=3
   ->  HashAggregate  (cost=0.03..0.04 rows=1 width=40) (actual time=0.025..0.026 rows=1 loops=1)
         Group Key: k.store, k.boot, k.seq
         Batches: 1  Memory Usage: 24kB
         ->  Function Scan on k  (cost=0.01..0.02 rows=1 width=40) (actual time=0.021..0.021 rows=1 loops=1)
   ->  Index Scan using "PK_HostLoadSampleIdentities" on "HostLoadSampleIdentities" i  (cost=0.42..8.45 rows=1 width=113) (actual time=0.026..0.026 rows=0 loops=1)
         Index Cond: (("RunnerStoreId" = k.store) AND ("BootId" = k.boot) AND ("Sequence" = k.seq))
         Buffers: shared hit=3
 Planning Time: 0.239 ms
 Execution Time: 0.131 ms
(11 rows)

Time: 0.574 ms
Time: 1.086 ms
MILLION_100_MATCHES
                                                                                 QUERY PLAN
----------------------------------------------------------------------------------------------------------------------------------------------------------------------------
 Nested Loop  (cost=2.19..850.00 rows=819 width=113) (actual time=0.202..0.983 rows=100 loops=1)
   Buffers: shared hit=399 read=1 written=1
   ->  HashAggregate  (cost=1.76..2.76 rows=100 width=40) (actual time=0.138..0.177 rows=100 loops=1)
         Group Key: k.store, k.boot, k.seq
         Batches: 1  Memory Usage: 24kB
         ->  Function Scan on k  (cost=0.01..1.01 rows=100 width=40) (actual time=0.063..0.085 rows=100 loops=1)
   ->  Memoize  (cost=0.43..8.46 rows=1 width=113) (actual time=0.007..0.007 rows=1 loops=100)
         Cache Key: k.store, k.boot, k.seq
         Cache Mode: logical
         Hits: 0  Misses: 100  Evictions: 0  Overflows: 0  Memory Usage: 25kB
         Buffers: shared hit=399 read=1 written=1
         ->  Index Scan using "PK_HostLoadSampleIdentities" on "HostLoadSampleIdentities" i  (cost=0.42..8.45 rows=1 width=113) (actual time=0.005..0.005 rows=1 loops=100)
               Index Cond: (("RunnerStoreId" = k.store) AND ("BootId" = k.boot) AND ("Sequence" = k.seq))
               Buffers: shared hit=399 read=1 written=1
 Planning Time: 0.584 ms
 Execution Time: 1.106 ms
(16 rows)

Time: 2.058 ms
Time: 0.694 ms
MILLION_120_MIXED
                                                                                 QUERY PLAN
----------------------------------------------------------------------------------------------------------------------------------------------------------------------------
 Nested Loop  (cost=2.54..1020.00 rows=982 width=113) (actual time=0.324..1.144 rows=60 loops=1)
   Buffers: shared hit=416 read=4 written=4
   ->  HashAggregate  (cost=2.11..3.31 rows=120 width=40) (actual time=0.159..0.211 rows=120 loops=1)
         Group Key: k.store, k.boot, k.seq
         Batches: 1  Memory Usage: 48kB
         ->  Function Scan on k  (cost=0.01..1.21 rows=120 width=40) (actual time=0.071..0.098 rows=120 loops=1)
   ->  Memoize  (cost=0.43..8.46 rows=1 width=113) (actual time=0.007..0.007 rows=0 loops=120)
         Cache Key: k.store, k.boot, k.seq
         Cache Mode: logical
         Hits: 0  Misses: 120  Evictions: 0  Overflows: 0  Memory Usage: 22kB
         Buffers: shared hit=416 read=4 written=4
         ->  Index Scan using "PK_HostLoadSampleIdentities" on "HostLoadSampleIdentities" i  (cost=0.42..8.45 rows=1 width=113) (actual time=0.005..0.005 rows=0 loops=120)
               Index Cond: (("RunnerStoreId" = k.store) AND ("BootId" = k.boot) AND ("Sequence" = k.seq))
               Buffers: shared hit=416 read=4 written=4
 Planning Time: 0.650 ms
 Execution Time: 1.288 ms
(16 rows)

Time: 2.345 ms
SET
Time: 0.128 ms
MILLION_120_GENERIC
                                                                              QUERY PLAN
----------------------------------------------------------------------------------------------------------------------------------------------------------------------
 Nested Loop  (cost=0.61..84.77 rows=82 width=113) (actual time=0.259..0.868 rows=60 loops=1)
   Buffers: shared hit=420
   ->  HashAggregate  (cost=0.18..0.28 rows=10 width=40) (actual time=0.192..0.238 rows=120 loops=1)
         Group Key: k.store, k.boot, k.seq
         Batches: 1  Memory Usage: 56kB
         ->  Function Scan on k  (cost=0.01..0.11 rows=10 width=40) (actual time=0.085..0.112 rows=120 loops=1)
   ->  Index Scan using "PK_HostLoadSampleIdentities" on "HostLoadSampleIdentities" i  (cost=0.42..8.45 rows=1 width=113) (actual time=0.004..0.004 rows=0 loops=120)
         Index Cond: (("RunnerStoreId" = k.store) AND ("BootId" = k.boot) AND ("Sequence" = k.seq))
         Buffers: shared hit=420
 Settings: plan_cache_mode = 'force_generic_plan'
 Planning Time: 0.653 ms
 Execution Time: 0.971 ms
(12 rows)

Time: 2.017 ms
MILLION_120_REPEATED
                                                                              QUERY PLAN
----------------------------------------------------------------------------------------------------------------------------------------------------------------------
 Nested Loop  (cost=0.61..84.77 rows=82 width=113) (actual time=0.203..0.577 rows=60 loops=1)
   Buffers: shared hit=420
   ->  HashAggregate  (cost=0.18..0.28 rows=10 width=40) (actual time=0.165..0.189 rows=120 loops=1)
         Group Key: k.store, k.boot, k.seq
         Batches: 1  Memory Usage: 56kB
         ->  Function Scan on k  (cost=0.01..0.11 rows=10 width=40) (actual time=0.080..0.100 rows=120 loops=1)
   ->  Index Scan using "PK_HostLoadSampleIdentities" on "HostLoadSampleIdentities" i  (cost=0.42..8.45 rows=1 width=113) (actual time=0.003..0.003 rows=0 loops=120)
         Index Cond: (("RunnerStoreId" = k.store) AND ("BootId" = k.boot) AND ("Sequence" = k.seq))
         Buffers: shared hit=420
 Settings: plan_cache_mode = 'force_generic_plan'
 Planning Time: 0.238 ms
 Execution Time: 0.655 ms
(12 rows)

Time: 1.231 ms

````
