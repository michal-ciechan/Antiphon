# Review c19fce8b: groups 32/58/61 test-timing (subject 0a1a9ea7)

Reviewed `a5a3921e81c114e31f718b5f5e2da90655e5a3c2` (origin/feat/card-task-0a1a9ea7) against base
`4563e52529b23052856cb0f1e0d01b383cf7590f` (rc/2026-10-27). The reviewed tree was a detached scratch worktree at
that tip; the base was this worktree. Read-only for the subject. Nothing was deleted.

Verdict: the product change preserves behaviour. Three test-side defects block merge.

## (1) Product: AgentSessionLaunchQueue.WaitForIdleAsync. Preserved, no defect

Production callers: `OperatorShutdownCoordinator.StopAsync` (`TimeSpan.FromSeconds(ShutdownDrainSeconds)`, only when
`seconds > 0`, catches OCE) and `AgentTaskDispatcher` brief recovery (`30s`, catches OCE `when !ct.IsCancellationRequested`).
Both go through the unchanged 2-argument `ILaunchDrain` overload, which now passes `TimeProvider.System`.

- Timer: `new CancellationTokenSource(delay, TimeProvider.System)` builds the same `TimerQueueTimer`
  (uint ms, truncated `TotalMilliseconds`) that `CancelAfter` used. The granularity is the same.
- Range: both validate `-1 <= ms <= Timer.MaxSupportedTimeout` and throw `ArgumentOutOfRangeException`. Both throw
  inside an async method, so the exception is carried by the returned Task as before (the new outer methods are not
  async, but `WaitForPendingAsync` is). `Timeout.InfiniteTimeSpan` arms no timer in either version.
- `TimeSpan.Zero` is the only difference. The new source is born cancelled, so with pending launches the wait throws
  at once. The old `CancelAfter(0)` fired on a timer thread, so a launch finishing in that gap could win. No
  production caller can pass zero: shutdown is guarded `seconds > 0` and the dispatcher passes 30s. Not a defect.
- Caller cancellation and timeout both rethrow an OCE carrying the linked token, as before (it was `cts.Token`, now
  `linked.Token`; neither is the caller's `ct`). A launch's own OCE is still swallowed by the `when` filter plus the
  bare catch, and the loop still re-snapshots launches added during the wait.
- Lock: `PendingLaunches()` takes the snapshot under `_gate`, and the `!IsCompleted` filter is kept. It is applied
  twice now, which does no harm. `ILaunchDrain` is unchanged.

## (2) Test honesty

### D-1 (group 61): the cached holder stamp is no longer compared to the real process start
- Where: `tests/Antiphon.Tests/Checkpoints/CheckpointSlotExecutorTests.cs` `AssertCachedHolderStart`
  (`_ = readLiveStart().ToUniversalTime();`), used by `executor_reachable_broker_grants_build_and_rows`.
- Failure: the base asserted `captured.UtcDateTime.ShouldBe(child.StartTime.ToUniversalTime(), 1ms)`. The helper
  now reads the live start and discards it even when the read succeeds, so a wrong stamp passes. Suppose
  `ProcessLeaseHolder.Start`'s default reader (`tools/Antiphon.Checkpoints/Slots/LeaseHolder.cs`,
  `child => child.StartTime`) is mutated to `_ => DateTime.UtcNow`, or to the parent's start. The test stays green
  as long as the string is a UTC "O" round-trip.
- Why: this was the only assertion that ties the default reader's broker key to the real hold-child identity.
  `outbound-start-equals-holder` compares the wire stamp only with an injected reader's value.
  `BuildSlotClientTests.holder_process_stays_alive...` checks liveness only. The executor itself is intact:
  `BuildSlotClient.cs:183-193` and `:235-240` still refuse with `holder_identity_unavailable` on a throw or an empty
  stamp. The test, however, is now blind to an identity mismatch. The two new tests
  (`a_thrown_start_time_does_not_fail_the_cached_stamp` and `an_exited_hold_child_does_not_fail_the_cached_stamp`)
  test only that the test helper swallows exceptions. They cannot go red against any product change.
- Fix: when the live read succeeds, compare it to the cached stamp, exactly or with a tolerance taken from measured
  Windows data. Tolerate only the documented unavailable exceptions. Add a helper self-test in which a mismatched
  stamp fails.

### D-2 (group 61): the roster and renew tests pass on the refusal branch
- Where: `CheckpointSlotExecutorTests.executor_reachable_broker_grants_build_and_rows` and
  `executor_renews_until_driver_finishes_then_releases`, at the `if (startUnavailable > 0) { exit.ShouldBe(2); ...; return; }` blocks.
- Failure: when `StartTime` throws, the test returns as passed without running any roster, occupancy, renew or
  release assertion. On a host where the read always throws (the suspected Windows condition), the success-path
  contract is never exercised and the run still reports green. A partial throw also contradicts the branch's own
  assertions: if only a row holder throws, the build already ran, so `driver.Calls.ShouldBeEmpty()` and
  `Builds.ShouldAllBe(holder_identity_unavailable)` fail.
- Why: an alternate pass outcome hides lost coverage. Fail-closed refusal is already pinned by
  `failed_start_time_capture_disposes_new_holder`.
- Fix: on that branch, throw `SkipTestException` with the cause so the count shows it, or retry the acquisition.
  Do not return as passed.

### D-3 (groups 32 and 58): wall-clock caps replaced by unbounded waits
- Where: `TerminalRunnerSeatReleaseTests.Answer_racing_release_preserves_one_owner` uses
  `WaitForBeforeDispatchAsync(entered.Task, new FakeTimeProvider(), 10s)`, on a fresh clock that nothing advances.
  `GrokSignInRuntimeTests.A_sign_in_mode_fakegrok_pool_task_never_types_the_brief` calls
  `WaitForIdleAsync(30s, cancellationToken, clock)` before any `Advance`, followed by an unbounded
  `while (RunnerListsRunningAsync(...)) Task.Delay(50)`. Neither method has a `[Timeout]`, and TUnit's
  `cancellationToken` fires only on a test timeout.
- Failure: the investigation's first group-32 mechanism is `gate.WaitAsync(0)` returning false, after which
  `AdvanceAsync` returns without the hook. In that case the test now hangs until an external harness deadline,
  where it used to fail in 10s with `TimeoutException`. Group 58 behaves the same way if the ConPTY teardown or the
  runner's `Running` row never settles. The `finally` comment "A boundary timeout must release..." is now dead.
- Why: docs/test-performance-standards.md requires timeouts near 2x measured p95, and git-common forbids widening a
  timeout to make red go green. These waits are widened to infinite. `BeforeDispatch_budget_follows_the_test_clock`
  tests only the test-local helper, not the race.
- Fix: keep a finite wall bound with a diagnostic. Use a method `[Timeout]`, or a wall cap of about 2x the measured
  Windows p95 (for example 60-120s), and bound the group-58 polling loop. The fake clock may still drive the grace
  arithmetic.

What remains honest: group 32 still asserts Blocked under the gate, `Attempt == 2`, the answer fields, and zero
queued messages. Group 58 still asserts session Failed with `ProviderSignInRequired` and "grok login", no typed brief,
one incident, a task Failed with `AuthenticationRequired`, and the parent note. The extra tick only waits out gate 2.

Witnesses:
- `idle_budget_follows_the_test_clock_while_a_launch_is_incomplete` is real. It discriminates because ignoring
  `clock` leaves the 500ms `WhenAny` on the delay. At the pre-fix commit it is a compile failure only, not an
  assertion red, since the API is new. It is close to deterministic: the only wall bound is 500ms of continuation
  scheduling.
- `a_running_runner_on_the_first_sweep_does_not_spend_the_grace_advance` characterises unchanged dispatcher gate 2.
  It is green at base by construction, so it pins the premise rather than witnessing a fix.
- The group 32 and 61 additions test test-local helpers only (see D-1 and D-3).
- PCs stay pending for the method-scoped Mutation stage.

## (3) The receipt-read stall: a pre-existing, load-sensitive flake that this change did not cause

Two runs of the same 6-class selection at each SHA, all through build-slot:

| SHA | run | total | pass | fail | skip | failure |
|---|---|---|---|---|---|---|
| tip a5a3921e | 1 | 89 | 87 | 1 | 1 | `executor_busy_then_rejection_retains_exact_wait_in_state_report_and_merge`: receipt-read-refused host.txt `read-stalled: 0 of 167 bytes; no progress within 3s` |
| tip a5a3921e | 2 | 89 | 88 | 0 | 1 | none |
| base 4563e525 | 1 | 84 | 83 | 0 | 1 | none |
| base 4563e525 | 2 | 84 | 82 | 1 | 1 | `executor_cancellation_releases_lease_and_holder`: receipt-read-refused .checkpoint-output-owner.json `read-stalled: 0 of 32 bytes; no progress within 3s` |

The skip each time is `A_sign_in_mode_fakegrok_pool_task_never_types_the_brief` (ConPTY, Windows only). The tip adds
5 tests (84 + 5 = 89).

The stall comes from `src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs` `Consume`:
`stream.ReadAsync(...).AsTask().WaitAsync(deadline).GetAwaiter().GetResult()` with a 3s stall window. On Linux,
`FileStream.ReadAsync` queues the pread to the thread pool, so under pool saturation 0 bytes arrive within 3s (the
same class as the Review 7ba417d9 D-1 finding). This subject diff does not touch that file, and the stall
reproduced at base in a different method. The class is a real product defect that predates this change. It is not
made worse here, and it is not a regression from this change. It belongs on a separate card, not in this Code
round.

## Runs
- Builds: one slot build each, `dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj --property:UseAppHost=false --property:OutputPath=bin-review/`
  (tip 211s, base 156s, both exit 0).
- Tests: `dotnet exec tests/Antiphon.Tests/bin-review/Antiphon.Tests.dll --treenode-filter "/*/*/(TerminalRunnerSeatReleaseTests*)|(GrokSignInRuntimeTests*)|(AgentTaskDeadSessionReconciliationTests*)|(CheckpointSlotExecutorTests*)|(AgentSessionLaunchQueueOwnershipTests*)|(OperatorShutdownCoordinatorTests*)/*" --report-trx`,
  each run under `scripts/build-slot.ps1`, with no slot wait.
- `check-evidence-diff.ps1 -BaseRef 4563e525 -HeadRef a5a3921e`: commits=3 entries=0 violations=0, exit 0.
- Scope: Interim. The whole Unit lane was not run, as the brief directs.
- bin-review outputs are left in place under this worktree and the scratch tip worktree, because the brief forbade
  cleanup.
