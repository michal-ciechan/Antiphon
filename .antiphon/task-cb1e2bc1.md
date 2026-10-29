# Review cb1e2bc1: group 105 hang fix round 2 (repeat dispose)

Outcome: clean, with no defects. Round 1's D-1 is closed. The first `DisposeAsync` still surfaces the sink fault, and later disposes join without rethrowing. Both new tests are red on the unchanged writer and green at the tip. Scope is **Interim**: I did not run the whole Unit lane.

Subject: task 132f88be-6cb7-4591-ac2f-f74bbf87520d, branch feat/card-task-132f88be, tip e4c4a89a2ed972215a25dea1ac11d756348c077e. The branch is built on 24101d6a 7b5525972 and base rc/2026-10-27 34a7d0b8f. I read the author's report from the task row (`result`). Its claims match what I observed.

## Defects

None.

## Checks

1. **D-1 closed** (`tools/Antiphon.Checkpoints/Execution/ExecutorLogWriter.cs:82-113`). Under `_admit`, exactly one caller enqueues the Dispose item and gets the raw `_disposed` task. Every later caller gets `JoinWithoutFault`, which waits for that disposal and swallows its fault.
   - **Publication failure, then a log-close fault** (`CheckpointApp.cs:345/351`, then `RetainPublicationFailure`, then `:339`). The first dispose throws an IOException, which is printed as "checkpoint log disposal failed". The method returns Invalid. The `await using` at `:150` repeat completes, so the exit stays Invalid. This is pinned by `publication_failure_plus_log_close_failure_stays_invalid`, which checks exit 2, sink disposes=1, state not ExecutorCrashed, and report Invalid.
   - **Log-close fault on the normal path, with no publication failure** (`:537`). The first dispose rethrows. `Finish` at `:539` is never reached, so state is not published as done. The `await using` repeat completes and the original IOException reaches the outer catch at `:85-90`, giving ExecutorCrashed. My probe confirmed this: exit 6, state done/6, reason and report.md contain "injected log dispose", sink disposes=1. The fix does not fail open: a log that did not close never reports success. No committed test pins this order; see A-3.
   - **Dispose succeeded, then `Finish` publication fails** (`:539`, then `:541`). `Note` and `FlushAsync` throw ObjectDisposedException synchronously, and both are caught. The repeat `DisposeAsync` completes. The exit is Invalid.
   - **Exception escapes the body before `:537`.** The `await using` is then the first disposer, so a sink fault replaces the body exception. This matches the base, and the outer catch still selects Canceled from the caller token.
2. **Dispose concurrency.** My probe ran 200 iterations of two racing first disposers against a gated, faulting sink:
   - Exactly one disposer faulted, with the IOException.
   - Neither completed before the sink was released. A repeat now waits for the in-flight disposal; the base returned at once.
   - Sink disposes=1.
   - `Note` during and after disposal threw ObjectDisposedException at once. `FlushAsync` after dispose threw ObjectDisposedException. None hung.

   I also ran 50 iterations where the first caller discarded its ValueTask and a repeat awaited, followed by GC and finalizers. That gave 0 unobserved writer faults. An earlier probe draft did report 200 unobserved faults. They came from my own un-awaited `Task.WhenAll`; with it observed, the count was 0. Self-join is advisory A-2 below.
3. **Tests.**
   - Red build: the tip tree with `ExecutorLogWriter.cs` swapped for its 7b5525972 version, in a scratch copy. The worktree was not touched.
     - `repeat_dispose_completes_after_the_sink_dispose_faults` failed 1/1 with IOException "injected log dispose" at test line 86, which is the second dispose.
     - `publication_failure_plus_log_close_failure_stays_invalid` failed 1/1 with "exit should be 2 but was 6".
   - Tip: both 1/1 green.
   - CheckpointExecutorLogTests (6 tests): 20/20 runs alone and 10/10 runs under 8 sha256sum burners, every run 6/6 and rc=0 under a 60 s cap. The 30 runs took 136 s in total.
   - The new tests are bounded: `[Timeout(15_000)]` and `[Timeout(30_000)]`, measured at 2-5 s. The concurrent test keeps `[Timeout(60_000)]`. Both controls are deterministic. There are no sleeps, and the injected faults are synchronous.
4. **A-1:** I agree it is unreachable today. The outer `WriteLoop` catch can be reached only if `Finish` throws inside `Run`'s catch, through `Gate.Set` on a gate that `Note` already disposed. That needs `Note`'s `gate.Wait()` to throw, which in practice means `Thread.Interrupt`. `tools/Antiphon.Checkpoints` contains no `Interrupt()` call. `GetConsumingEnumerable` takes no token, and the collection is never disposed. It stays an advisory.

## Advisories

- **A-2** (latent self-join, newly possible): suppose a sink calls `writer.DisposeAsync()` from inside its own `DisposeAsync` on the writer thread and waits for it, synchronously or by awaiting before returning. The repeat dispose now waits on the disposal it is running inside, so it deadlocks. At the base, a repeat was a completed no-op. No production sink calls back (`FileExecutorLogSink`), so this cannot happen today. Re-entrant `Note` from a sink was already a deadlock at the base.
- **A-3** (coverage): nothing pins the normal-path "dispose fault, no publication failure, ExecutorCrashed" order. `log_io_failure_publishes_a_terminal_error_without_recursive_logging` covers open, append and flush, but not dispose. Adding `"dispose"` there, with `GatedSink.ThrowOnDispose`, would pin that the first dispose never fails open. My probe body is in the Runs section below.
- **A-4:** if the first caller discards its ValueTask, the fault is swallowed by any repeat joiner. All production callers await, so this is acceptable.

## Runs

All runs used `scripts/build-slot.ps1`, Linux server2, at tip e4c4a89a2. Each build used `UseAppHost=false`, followed by `dotnet exec`. I did not use checkpoint-tool receipts.

| Run | Result |
|---|---|
| tip build bin-rvcb (lease 0ce98403) | 0 errors |
| red build, scratch copy with the 7b5525972 writer (lease 3836763a) | 0 errors |
| probe builds, scratch copy, tip writer plus 2 probe tests (leases 5a14fe30, then rebuilt) | 0 errors |
| CheckpointExecutorLogTests | 6/6 x 20 alone, 6/6 x 10 burned |
| CheckpointSlotExecutorTests | 13/13 |
| CheckpointReceiptStallBudgetTests | 6/6 |
| CheckpointTaskOwnershipTests | 24/24 |
| WaitCommandTests | 12/12 |
| C1307CliCancellationTests | 5/5 |
| RcRosterRepairTests | 3/3 |
| C1514LandingSplitBudgetTests (C1404, C1577, C1584, C1410, G2865) | 5/5, TEMP=/tmp |
| CheckpointRecoveryLinuxTests | 5 passed, 5 failed (inherited) |
| 2 new tests on the red build | 0/1, 0/1 (red as expected) |
| review probes (normal-path crash; race, post-dispose and unobserved) | 2/2 |

- The 5 CheckpointRecoveryLinuxTests failures are `done_live_executor_retains_image`, `executor_death_after_ack_before_progress_recovers`, `starter_death_before_launch_reclaims_partial_copy`, `native_identity_keeps_live_then_removes_dead` and `launch_unknown_survives_until_late_ack`. This is the same set round 1 proved red at the base. I did not rerun the base this round.
- Not run: the whole Unit lane and group 105 itself.

Probe body (scratch only, not committed): the normal-path probe is `NewRun()`, then `sink.ThrowOnDispose = true`, then `ExecuteAsync`. It asserts ExecutorCrashed, Disposes==1, state done/6 with reason "injected log dispose", and report.md containing it.

## Other checks

- `check-evidence-diff -BaseRef 34a7d0b8f -HeadRef HEAD`: commits=5, violations=0.
- `merge-tree` against origin/rc/2026-10-27 (fffa9e954) is clean.
- Against origin/master (2fb1c5a65), the whole-branch merge-tree conflicts through rc/master divergence. Picking the range onto master with `--merge-base 34a7d0b8f` is clean. `ExecutorLogWriter.cs`, `CheckpointExecutorLogTests.cs` and `CheckpointApp.cs` are byte-identical on master and at the base.
- Cleanup: I removed 33 `bin-rvcb` directories one at a time by literal path, plus my scratch copy. The worktree is clean.
