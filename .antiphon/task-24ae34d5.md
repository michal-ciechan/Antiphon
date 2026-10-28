# Review 24ae34d5: group 105 hang fix (ExecutorLogWriter writer thread)

Outcome: the hang fix holds. The witness is red at the base and green at the tip, with 30/30 class runs green and bounded. I found one regression (D-1): a second `DisposeAsync` now rethrows the first disposal fault. Scope was Interim; the whole Unit lane was not run.

Subject 24101d6a-20d8-4423-98bf-0d2b934b01e4. Its server row shows status `Canceled`, role `Custom`. The author's report is uncommitted (`.antiphon/` is gitignored and was not force-added). I read it from the author's mirror at /work/worktrees/task-24101d6a/.antiphon/task-24101d6a.md. Reviewed tip 7b5525972cc6c27feaeff1161c4ab193e35dcfc5, base rc/2026-10-27 34a7d0b8f5e1ff2aaab6e1fa178258c4f29c8ba8. The diff touches 2 files.

## Defects

### D-1: a second DisposeAsync rethrows the first disposal fault (regression)
- **Where:** `tools/Antiphon.Checkpoints/Execution/ExecutorLogWriter.cs:86-87`. Consumers are `tools/Antiphon.Checkpoints/CheckpointApp.cs:150` (`await using var logWriter`) and `CheckpointApp.cs:339-341` (`RetainPublicationFailure`).
- **Failure:** the base set `_disposed = true` before it awaited the sink, so a repeat `DisposeAsync` was a no-op. The tip returns the stored `_disposed` task. If the sink's dispose threw, that task is faulted, so every later dispose rethrows the IOException. In `RetainPublicationFailure`, a state-publication failure such as a full disk is followed by a log disposal IOException. That IOException is caught and printed ("checkpoint log disposal failed") and the method returns `ExitCodes.Invalid`. The `await using` at :150 then disposes again and rethrows the IOException out of `ExecuteCoreAsync`. The outer catch at :85-90 turns the exit into `ExecutorCrashed` and rewrites state.json with "executor.log or executor failure". The report/stdout already written say Invalid, so receipt and report now disagree.
- **Evidence:** a probe that calls the public `ExecutorLogWriter` with a sink whose `DisposeAsync` throws IOException:
  - base: `dispose#1 threw IOException`, `dispose#2 ok`, sink disposes=1
  - tip: `dispose#1 threw IOException`, `dispose#2 threw IOException`, sink disposes=1

  No test covers a log-dispose failure on the publication-failure path. A grep for "log disposal failed" in tests found nothing.
- **Why:** this path has to report Invalid and keep the receipt it already wrote. The author's "A second DisposeAsync returns the same task" made the fault sticky for idempotent callers.
- **Fix:** a repeat `DisposeAsync` must not rethrow a fault the first caller already saw. Either return a completion that never faults, or keep the base flag-before-await semantics while still joining the writer thread. Add a test: a sink whose dispose throws, dispose twice, the second dispose completes. Ideally also a CheckpointApp-level test: publication failure plus dispose failure gives exit Invalid. Commit it red before the fix.

## Priority answers (advisories unless marked)

1. **Writer thread.**
   - **Ordering and exactly-once:** items are admitted under `_admit` into a FIFO `BlockingCollection` and drained by a single consumer, so each line is written once, in admission order. Admission after close throws ObjectDisposedException under the same lock, so nothing can be queued after the Dispose item.
   - **Errors:** each caller's error is its own. `Run` catches everything and calls `Finish`, which sets Error, then the TCS, then the Gate. Every queued waiter is signalled. The TCS uses RunContinuationsAsynchronously, so awaiting callers never resume on the writer thread.
   - **Advisory A-1:** the outer `WriteLoop` catch drains the queue but never calls `CompleteAdding`. If the loop died outside `Run`, for example because `Finish` threw from `Gate.Set` on a disposed event after a `Thread.Interrupt` on a `Note` caller, any later `Admit` would enqueue to a dead thread and its `Note` would block forever. This is not reachable in production today. Mark the collection complete, or set `_closed` in that catch.
   - **Re-entrancy:** a sink that calls `Note` from inside the sink deadlocks the writer thread. The base deadlocked the same way on its non-reentrant semaphore, so this is not a regression. No production sink calls back.
   - **Unbounded waits:** `Note`'s `gate.Wait()` is unbounded. So was the base `GetResult`. A stuck disk still blocks callers indefinitely. Neither version honours tokens inside `Note`, so a stuck sink can only be ended by killing the executor process (checkpoint `stop`/`wait` and the slot lease), not by the budget clock.
   - **Advisory A-2:** `AppendAsync` and `FlushAsync` now check their token only before the item runs. They no longer cancel while queued behind a stuck sink; the base `_gate.WaitAsync(ct)` did. No production caller passes a token (`CheckpointApp.cs:335`, `:536`), so this is not observable today.
   - **Thread lifetime:** one background thread per writer and one writer per `ExecuteCoreAsync`. The thread exits after the Dispose item and is not joined. It leaks only if the writer is never disposed or a sink hangs, and as a background thread it does not hold up process exit.
   - **Post-dispose calls:** `Note` after dispose throws ObjectDisposedException as before. `AppendAsync`/`FlushAsync` after dispose now throw synchronously instead of returning a faulted task. The only caller after dispose, `CheckpointApp.cs:335`, wraps it in try with an ObjectDisposedException catch, so this is fine.
2. **Hot path.** Flushing every line is unchanged from the base, which flushed after each `WriteLineAsync`. The tip writes synchronously. The order "done exit=" Note, then Flush, then Dispose, then `Finish(state)` publish is unchanged at `CheckpointApp.cs:535-539`. In a probe of 5000 Notes through the real `FileExecutorLogSink` (pwsh driver), the tip measured 36-39 us/note against 44-46 us/note at the base over 3 runs. There is no slowdown.
3. **Witness.**
   - `note_returns_when_the_sink_resumes_on_the_caller_context` is red at the base (failed 1/1 in 2.975 s, exit 2, the process exited) and green at the tip (1/1 in 1.294 s). The base binary was the base `ExecutorLogWriter.cs` swapped in temporarily; I restored it, and the tree is clean.
   - The class ran green 20/20 alone and 10/10 under 8 `sha256sum` burners, 4 tests each, 1.6-3.1 s, with a 60 s cap per run.
   - The witness is bounded at 2 s by `WhenAny` plus `[Timeout(15_000)]`. On failure it leaves only a background caller thread behind. The 60 s cap on the concurrent test is fine.
   - At the tip the sink runs on the writer thread with no context, so it takes the pool branch. The witness pins "Note does not block the caller context", not the old mechanism.
4. **Spot checks.**
   - `CheckpointApp.cs:607` `Start` is a synchronous wrapper with no callers in tools, src or tests. Every await in `StartAsync` uses `ConfigureAwait(false)`. It does not have the same defect.
   - `RunBudgetClock.cs:186` sync `Dispose`: production uses `await using` (`CheckpointApp.cs:202`, `RunScheduler.cs:60`). A sync `Dispose` from inside its own timer callback would self-join. The known callback case is witnessed through `DisposeAsync` (`TimeoutTests.budget_expiry_can_join_its_timer_from_a_cancellation_continuation`). It is latent only (A-3).

## Runs

These ran at tip 7b5525972 under build-slot. I built once with UseAppHost=false into bin-rvtip, then ran `dotnet exec` with no rebuild.

| Run | Result |
|---|---|
| tip build (lease 59c6da64) | 0 errors |
| base build (lease 11e31a32) | 0 errors |
| CheckpointExecutorLogTests | 4/4 x 30 |
| CheckpointSlotExecutorTests | 13/13 |
| CheckpointReceiptStallBudgetTests | 6/6 |
| CheckpointTaskOwnershipTests | 24/24 |
| WaitCommandTests | 12/12 |
| C1307CliCancellationTests | 5/5 |
| CheckpointToolCopyCleanupTests | 16/16 |
| HostCleanupCheckpointCompatibilityTests | 54/54 |
| RcRosterRepairTests | 3/3 |
| C1514LandingSplitBudgetTests census (C1404, C1577, C1584, C1410, G2865) | 5/5, TEMP=/tmp |
| CheckpointRecoveryLinuxTests | 5 passed, 5 failed |
| CheckpointRecoveryWindowsTests | skipped 14/14 on Linux, not evidence |

- The 5 CheckpointRecoveryLinuxTests failures are `done_live_executor_retains_image`, `executor_death_after_ack_before_progress_recovers`, `starter_death_before_launch_reclaims_partial_copy`, `native_identity_keeps_live_then_removes_dead` and `launch_unknown_survives_until_late_ack`. They are inherited: the same 5 fail at the base binary, so they are not caused by this change.
- The census needed the `(Name*)` filter form. A `(FullName)|(FullName)` filter ran 0 tests.
- I did not use checkpoint-tool receipts. Not run: the whole Unit lane and group 105 itself.

## Other checks

- `check-evidence-diff` over base..HEAD: commits=3, violations=0.
- `merge-tree` against rc/2026-10-27 (070d35b80) is clean. Against master (2fb1c5a65) it conflicts only through rc/master divergence. Picking the base..HEAD range onto master merges clean, and ExecutorLogWriter.cs and CheckpointApp.cs are byte-identical on master and at the base.
- I removed 66 bin-rv* directories.
