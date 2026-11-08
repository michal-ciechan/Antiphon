# Run 4e0d1159 groups 89 and 95: Windows failure extract (2026-11-08)

Read-only extract for Debug tasks 521d50c9 (group 89) and 5049a6c0 (group 95), which cannot
read the desktop logs from server2. No build or test was run. Messages and stacks are copied
unedited from the run's own files; timestamps are UTC.

- Run: 4e0d1159, Windows Full qualification on `rc/2026-11-11`. The checkout
  `C:\Antiphon\releases\checkout-0297fa9c` is at `3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e`.
- Log root: `C:\Antiphon\releases\candidates\rc-2026-11-11-3364197bf1cb\logs\2026-11-08-0936-4e0d1159\`
- Host (diag `Machine info`): DESKTOP-KTLKPIF, Windows 10.0.19045, ProcessorCount 8, 34 GB.
  Runtime: TUnit 1.44.0, MTP 2.2.2, .NET 9.0.22, win-x64, Debug build.

## Group 89: `Card1761RunnerReadPollTests.Card1761_Pty_output_wait_refreshes_the_frame_when_the_sequence_moves`

Sources: `antiphon-all-group-89-tests.log`, `antiphon-all-group-89.trx`,
`antiphon-all-group-89-execution\log_261108113736743.diag` (diag lines 352 and 1306-1334),
`antiphon-all-group-89.timing.json`.

- Outcome: Failed. 1 of 367 tests failed in the chunk. Chunk duration 2m 24s 376ms; runWallSeconds 145.625.
- Duration: 00:00:01.4357709. Start 11:37:52.2326837, end 11:37:53.6684546. The diag
  in-progress message is at 11:37:52.2316963 and the failed message at 11:37:53.6808437.
- Phase id: `Antiphon.Tests.Application.Card1761RunnerReadPollTests.1.1.Card1761_Pty_output_wait_refreshes_the_frame_when_the_sequence_moves.1.1.0`

Message:

```
ShouldAssertException: matched
    should be
True
    but was
False
```

Stack trace (TRX, complete as recorded):

```
   at Antiphon.Tests.Application.Card1761RunnerReadPollTests.Card1761_Pty_output_wait_refreshes_the_frame_when_the_sequence_moves() in C:\Antiphon\releases\checkout-0297fa9c\tests\Antiphon.Tests\Application\Card1761RunnerReadPollTests.cs:line 252
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

Elapsed values: the assertion prints no elapsed or expected time, only `matched` True versus
False. The test's own limit is `TimeSpan.FromMilliseconds(400)` (test source line 247). The
recorded test duration is 1435.77 ms, which includes `StartAsync`.

What the test needs (rc source, read-only):
- The test is at `tests/Antiphon.Tests/Application/Card1761RunnerReadPollTests.cs:232-255`.
  The fake runner moves `Sequence` to 9 and `Raw` to "new" only on the 3rd `GetAsync`.
- `server/Infrastructure/Agents/SessionRunner/RunnerTerminalSession.cs:237-278`
  (`WaitForOutputAsync`) sets `deadline = DateTime.UtcNow + timeout`. It loops
  `while (DateTime.UtcNow < deadline)` and calls `await Task.Delay(50, ct)` between polls.
- The fake returns `Task.FromResult` (test file lines ~445-466), so each poll completes at once.
  The only real waits are the two `Task.Delay(50)` continuations before the 3rd session get.
- Wall time between `WaitForOutputAsync` entry and the 3rd poll must therefore stay under
  400 ms. Nominal need is about 100-125 ms (Windows default timer tick 15.6 ms rounds each
  50 ms delay up to about 62 ms).

Concurrency and load around the failure (TRX start/end overlap):
- 137 tests in the chunk overlap the failing test's 1.44 s window, on 8 logical processors.
  By class: Card1761RunnerReadPollTests 19, CompactionContinuationPolicyTests 19,
  ClaudeEffortByRoleSettingsTests 18, DiagnosticsRedactorTests 18,
  CommitRecoveryObligationsTests 17, AgentTaskDecisionQuestionTests 12,
  LandConflictFileSummaryTests 9, AgentTaskListStatusFilterTests 5, HostStatsEndpointTests 5,
  AgentSessionRuntimeBoundedReadTests 4, OrchestratorInvestigationDetectorTests 4,
  DefaultRunnerEligibilityTests 3, CheckCompactionContinuationTests 2, and others.
- All 19 Card1761RunnerReadPollTests started between 11:37:52.2297 and 11:37:52.2362, so the
  class runs in parallel.
- Sibling wait tests in the same class show about a 1 s stall that ended near 11:37:53.6:

| Test | Own wait limit | Duration | End |
|---|---|---|---|
| Card1761_Herdr_output_wait_reads_each_frame_without_a_session_get | (frame wait) | 0.0810 s | 52.3149 |
| Card1761_Unknown_backend_output_wait_reads_each_frame_without_a_session_get | (frame wait) | 0.0807 s | 52.3146 |
| Card1761_Pty_output_wait_snapshots_only_when_the_sequence_moves | 250 ms | 1.3584 s | 53.5931 |
| Card1761_Codex_turn_wait_takes_one_snapshot_and_no_ansi_tail | CodexDoneMaxWaitMs 800 | 1.3987 s | 53.6345 |
| **Card1761_Pty_output_wait_refreshes_the_frame_when_the_sequence_moves** | **400 ms** | **1.4358 s** | **53.6685** |
| Card1766_Herdr_quiet_follows_the_buffer_sequence_not_a_flat_session_record | (quiet wait) | 1.5802 s | 53.8164 |

- Heavier tests that started in the same 0.2 s burst were integration tests:
  AgentTaskReuseEnqueueTests `both_reuse_messages_enqueue_when_nothing_throws` (7.35 s),
  HostStatsEndpointTests `Hosts_stats_answers_the_cache_projection_with_counters` (6.32 s),
  CardFilePrivacyPathTests `Reparse_alias_refuses_...` (5.18 s), and
  CheckCompactionContinuationTests (2 tests, about 4.1 s each).
- No CPU or idle figure is logged. The diag has no heartbeat, CPU, idle or thread-pool line.
  A case-insensitive search for `cpu|idle|heartbeat|timer resolution|timeBeginPeriod|thread pool|starv|hang`
  matched only `TESTINGPLATFORM_DEFAULT_HANG_TIMEOUT: ''`.

## Group 95: `C1514TaskLoadWindowTests.LongTasksKeepStatisticsAfterRawExpiry`

Sources: `antiphon-all-group-95-tests.log`, `antiphon-all-group-95.trx`,
`antiphon-all-group-95-execution\log_261108115317547.diag` (lines 993-1043),
`antiphon-all-group-95.timing.json`.

- Outcome: Failed with a TUnit timeout. 1 of 204 tests failed in the chunk. Chunk duration
  2m 19s 137ms; runWallSeconds 140.588; processWaitSeconds 54.475.
- Duration: 00:00:11.0195722. Start 11:53:43.5006930, end 11:53:54.5202652.
- Phase id: `Antiphon.Tests.Application.C1514TaskLoadWindowTests(Antiphon.Tests.TestHelpers.C1514HostLoadFixture).1.1.LongTasksKeepStatisticsAfterRawExpiry.1.1.0`
- Attributes: class-level `[Category("Integration"), Timeout(10_000), NotInParallel("C1514HostLoadServer")]`
  and `[ClassDataSource<C1514HostLoadFixture>(Shared = SharedType.PerTestSession)]`
  (test file lines 14-15). The test body is at lines 91-104.

Message:

```
TimeoutException: Test 'LongTasksKeepStatisticsAfterRawExpiry' timed out after 00:00:10

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
```

Stack trace (TRX, complete as recorded):

```
   at TUnit.Engine.Helpers.TimeoutHelper.ExecuteWithTimeoutAsync(Func`2 taskFactory, TimeSpan timeout, CancellationToken cancellationToken, String timeoutMessage)
   at TUnit.Engine.TestExecutor.ExecuteAsync(AbstractExecutableTest executableTest, TestInitializer testInitializer, CancellationToken cancellationToken, Nullable`1 testTimeout)
   at TUnit.Engine.TestExecutor.ExecuteAsync(AbstractExecutableTest executableTest, TestInitializer testInitializer, CancellationToken cancellationToken, Nullable`1 testTimeout)
   at TUnit.Engine.Services.TestExecution.TestCoordinator.ExecuteTestAsync(AbstractExecutableTest test, CancellationToken cancellationToken)
   at TUnit.Engine.Services.TestExecution.TestCoordinator.ExecuteTestAsync(AbstractExecutableTest test, CancellationToken cancellationToken)
```

The blocked test's own stack was not captured. TUnit records only the timeout handler's stack.
`WaitingForActivation` means the body was suspended on an `await` at 10 s, not spinning.

Elapsed values: the limit was 10 000 ms (`timed out after 00:00:10`). The recorded duration
is 11 019.57 ms, which includes the 10 s timeout plus about 1 s of cancellation and teardown. The
log does not show which await in the body was pending.

Class siblings (serial under `NotInParallel("C1514HostLoadServer")`, same 10 s limit):

| Test | Duration | Start | End |
|---|---|---|---|
| KnownBoundariesWithMissingInteriorAreIncomplete | 6.5621 s | 53:33.1727 | 53:39.7348 |
| BackwardsTerminalClosesWithoutLeavingTheWindowOpen | 3.7626 s | 53:39.7373 | 53:43.4999 |
| **LongTasksKeepStatisticsAfterRawExpiry** | **11.0196 s (timeout)** | 53:43.5007 | 53:54.5203 |
| ReusedSessionOpensAtAcceptance | 0.6577 s | 53:54.5298 | 53:55.1874 |
| ShortAndOverlappingTasksKeepAmbientMeaning | 5.9873 s | 53:55.1888 | 54:01.1760 |
| BoundarySamplesRespectFifteenSeconds | 5.5322 s | 54:01.1769 | 54:06.7091 |
| AttemptsAndMovesKeepSeparateWindows | 5.9954 s | 54:06.7101 | 54:12.7055 |
| RestartReconcilesMissedEventsWithoutClosingOpenTask | 5.1370 s | 54:12.7069 | 54:17.8439 |

- The chunk p95 is 6.562 s and the max is 14.37 s. timing.json lists this test as the
  2nd slowest at 11.02 s.
- Concurrency in the failure window: 9 overlapping tests in total.
  - `C0824DesktopLegacyReleaseNativeTests.Windows_aliases_and_junctions_are_refused(junction-inside)`
    ran 14.37 s from 53:33.17 to 53:47.54.
  - `Windows_aliases_and_junctions_are_refused(junction-leaf)` ran 7.01 s from 53:47.56 to 53:54.57.
  - Five CompactionRecoveryTests ran serially, about 1.2-1.6 s each, from 53:43.36 to 53:50.87.
- The chunk's timing records processSpawnSeconds 2.461 and processWaitSeconds 54.475. Real
  process or junction work ran beside the failed test.
- No CPU or idle figure is logged. During 11:53:43-11:53:55 the diag has only
  `Microsoft.Testing.Platform.Messages.AsynchronousMessageBus TRACE` lines (23) besides the
  TestNode updates. There is no heartbeat, CPU, idle or thread-pool line.

## Windows timer facts visible in the logs

None. Neither diag nor either tests.log records the timer resolution, a `timeBeginPeriod`
call, or the clock source. The only platform facts are the machine-info block (8 logical
processors, Windows 10.0.19045, .NET 9.0.22). The 15.6 ms default tick in the group 89
analysis is the Windows default, not a logged value.

## Diagnosis (no code change proposed)

**Group 89: a wall-clock deadline was too short under a parallel burst.**
- `WaitForOutputAsync` measures its 400 ms limit with `DateTime.UtcNow`. The fakes complete
  synchronously, so only the two `Task.Delay(50)` continuations can consume time before the
  3rd poll.
- Every other timed wait in the class also ran about 1.0-1.2 s past its own limit and ended
  between 53.59 and 53.82:
  - the 250 ms pty wait took 1.358 s;
  - the 800 ms Codex wait took 1.399 s;
  - the failing 400 ms wait took 1.436 s.
- The Herdr and unknown-backend frame waits finished at 52.31, before the stall.
- So delay continuations were not dispatched for about 1 s while 137 tests were in flight on
  8 cores. That points to thread-pool or CPU starvation from the class-wide parallel start
  plus the integration tests that started at the same moment.
- The deadline expired before the 3rd `GetAsync`, so `matched` was False at line 252.
- The other wait tests assert only `ShouldBeFalse` or `> 1`. This is the only one that needs
  a minimum number of polls inside the window, so it is the only one that failed.
- This is a timing/budget flake in the test, not a product regression. Nothing in the
  failure points at the CARD-1761/1766 poll-routing logic.
- Why missed before the rc run: the same test passes when the host is not under burst load,
  because it needs only about 125 ms of 400 ms. Linux and narrow-filter runs do not
  reproduce a 1 s pool stall.

**Group 95: the 10 s per-test timeout was exhausted on a loaded host. The data cannot show
whether the cause was slowness or a hang.**
- The body suspended on an await (`WaitingForActivation`), and the blocked stack was not captured.
- This test does the most work in the class: seed, produce, start, transfer, five
  `RetainAsync` passes with 8/91/+89/+2-day clock jumps, one API read, finish, derive, and
  two DB counts.
- Its serial siblings already use 3.8-6.6 s of the same 10 s budget, and the chunk p95 is 6.56 s.
- It ran alongside the 14.4 s and 7.0 s Windows junction/alias tests and the
  CompactionRecovery series. The chunk spent 54.5 s in process waits.
- The likeliest reading is budget exhaustion. The class's 10 s limit is about 1.5x its
  siblings' normal duration, under the 2x-p95 standard in
  `docs/test-performance-standards.md`.
- A true hang in one retention or derive await is not excluded: the next test started
  normally 10 ms after teardown, but there is no blocked-test stack.
- Why missed before the rc run: the test has no other recorded run in the candidate logs.
  A grep of `*/logs/*/*.trx` under `C:\Antiphon\releases\candidates` found it only in this run.
  It was added on 2026-11-07 (commits 067d5da7a, a608eaf15, 02473f4fb). Its first Windows
  Full-suite run is this one, beside process-spawning neighbours.

Next for Debug:
- 521d50c9 (group 89): confirm the starvation reading by re-running the class alone and
  under parallel load.
- 5049a6c0 (group 95): time each await in the body under the class's 10 s timeout on Windows,
  or capture a dump, to tell budget from hang.
