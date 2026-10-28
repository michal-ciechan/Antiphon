# Review 7e117ffd: PhoneHome round 2 (task c790cd94), observe-time liveness

Outcome: one defect (D-1). The round-1 stall (D-1 of review 8bb21219) is fixed. The observer no longer waits on the serial worker, and the transcript confirm runs off the worker. But the new `await` on the liveness read sits between the completed-line scan and the cancel of the pending debounce. A split mention can now be queued twice. A scratch witness reproduced this deterministically. Scope is Interim: the whole Unit lane was not run.

Subject `feat/card-task-c790cd94` at `1423c937e558bee37bde13434b25b8a404b37d77`. Round-1 tip `23d77eedacb0464bfe3307e7ec2d88ed0465bc2e`; base `68565db92359eadea14180c745c0a28711a36872` (rc/2026-10-27). Reviewed on server2/Linux in `/work/worktrees/task-7e117ffd` at the same SHA, with a clean tree.

## D-1 (defect, new regression): the liveness await lets the debounce route a line the completed scan also routes

- **Where:** `server/Application/Services/AgentMentionRouter.cs:81-109` (`ObserveCompletedAsync`).
  - Line 81 picks `ready` mentions, using `PendingRouteExists` for dedupe.
  - Line 88 then awaits `capture(ct)`, which is `AgentSessionRuntime.ReadMentionLivenessAsync`: a new scope plus a DB read.
  - Only after that await do line 103 (`_pendingStates.TryRemove`) and line 109 (`SchedulePendingMentions`, the version bump or `PendingCleared`) run. These are the two steps that make a scheduled debounce stale.
- **Failure:**
  1. Chunk `@helper please inspect` (no newline) is pending-ready (14 chars, at least 8). The debounce v1 is scheduled at t0.
  2. Chunk ` it\n` arrives before t0+300 ms. `ready` = [helper "please inspect it"], because `RoutedKeys` is still empty.
  3. The liveness read is in flight at t0+300 ms. The debounce still sees state version 1, adds `helper` to `RoutedKeys`, and enqueues "please inspect".
  4. The read returns, and the completed line enqueues "please inspect it".
  5. Result: two route commands with distinct occurrence ids, so two queue rows and two prompts to the target, one of them truncated.
- **Scratch witness:** uncommitted test, removed afterwards; build lease `5effee63-f196-4a7a-ae75-fdd930463c0d`, run lease `ece5fc45-a57f-4c80-90f7-bdc49453e1dc`.
  - `R7e11_liveness_read_vs_debounce(700)`: red, `enqueued=2`. Trail: `+3ms pending-scheduled version=1`, `+305ms debounce-fired`, `+305ms command-enqueued target=helper` (messageChars=14), `+710ms command-enqueued target=helper` (messageChars=17), `+710ms pending-cleared`.
  - Control `(0)`: green, `enqueued=1`.
- **Why it matters:**
  - At round 1 this span was synchronous, so the race window was microseconds.
  - Now the window is a DB round trip on the observer path. It is widest exactly when the DB or phone-home is loaded.
  - The result is a duplicate delivery to the recipient, which loses exactly-once (brief priority 1).
  - The ChannelHub split test no longer guards this (see A-2).
- **Fix (for Code):** before awaiting `capture`, synchronously claim the completed lines from the pending debounce. For example, under `state.Gate`, bump `Version` (or `TryRemove` the state) and add the `ready` targets to `RoutedKeys`. Then await the read and enqueue. Alternatively, take the read without leaving the pending state live.
- **Witness to add:** a held `capture` that spans the 300 ms debounce, asserting exactly one `CommandEnqueued` and one recipient queue row. It must be red at `1423c937e`.

## Checked and found sound (priority 1-3)

- **Stale-live:**
  - The stamp is taken inside the per-session observer, before that session's exit is observed. Exit events queue behind the observer.
  - A route accepts a Stopped, Stopping or Failed source only when `StartedAt` (`SessionGeneration.Equal`), `CardId` and `StandingAgentId` match.
  - A null stamp (`ObserveDelta`, the debounce, a failed read) or `WasLive=false` keeps the Starting/Running filter, so output replayed after stop does not route.
  - "Liveness" here is the status of the mention source session, not mirror liveness. No path reports a dead runner or mirror as alive.
- **Flush off the worker:**
  - Acceptance is still the durable queue row, written before the flush. A failed, cancelled or dropped flush leaves that row Pending for queue recovery, the same outcome as round 1's inline catch.
  - `FlushDetachedAsync` catches every exception and logs or swallows it, so none goes unobserved.
  - Concurrent flushes for one target serialize on `SessionMessageQueueService`'s per-session `SemaphoreSlim`. The service is a singleton, and the flush opens its own scope.
- **Queue:** `FullMode.Wait` with `TryWrite` refuses at 1024 and logs `CommandDropped`, as round 1 did.
- **Starvation and lock-held IO:** no new sync-over-async. `Dispose`'s `_worker.Wait(2s)` is pre-existing. The per-session lock held across the confirm wait is pre-existing design, now off the worker.

## Advisories

- **A-1:** Under 8 CPU burners, `Finite_stall_preserves_connection_order_and_delivery` failed once in 15 runs.
  - The brief's set of 5 (lease `7862df07-c724-483e-ae17-7f5f527e09d7`) gave 4/5, failing in run 2 at 19.8 s. My 10 reruns (lease `06cd2cf8-9d32-4ff7-88b1-e387a3f7ad9d`) were 10/10.
  - I did not capture the message, because the summary-only grep dropped it.
  - The test body is unchanged this round, and its waits are the fixed 5 s `PhoneHomeHostedRunner.UntilAsync` (`tests/Antiphon.Tests/TestHelpers/PhoneHomeHostedRunner.cs:180-193`). Round 1 and the author each saw 5/5 under burners.
  - Not attributed and not shown red at base under load. Code could check whether the new observer DB read adds to it.
- **A-2:** `Channel_mention_split_across_pty_chunks_routes_once_after_pending_line_settles` (`AgentChannelServiceIntegrationTests.cs:195-199`) dropped its post-route `Task.Delay(450)`. It now asserts exactly-once the instant the first body is submitted, so a late duplicate (the D-1 shape) cannot turn it red. The D-1 witness should restore a settle or a row-count check.
- **A-3:** No shared `LoadTimeoutWait` helper exists under `tests/` (grep finds nothing). The new witnesses use fixed 3, 5 and 8 s waits and a `< 20 s` wall-clock total assertion.
- **A-4:** `StartFlush` (`AgentMentionRouter.cs:138`) reads `_cts.Token` after the `_disposed` check. If `Dispose`'s 2 s worker wait times out and disposes `_cts`, an accepted route logs `Failed to route mention` with an `ObjectDisposedException`. This is benign: the row stays queued.
- **A-5:** Detached flushes are fire-and-forget, one per accepted mention, each waiting up to the 30 s confirm. They are not capped, but this is acceptable at mention rates.
- **A-6:** If the pump's `ct` is cancelled during `capture`, the call rethrows before enqueue, and the runtime catch (`AgentSessionRuntime.cs:149`) swallows it. That chunk's mentions are lost and `_pendingText` is not updated. This affects shutdown only.
- **A-7 (docs):** The two docs commits touch only `.antiphon/task-*.md`, and no living doc describes the mention router. The author's report matches the code.

## Runs (tip `1423c937e`, server2/Linux, narrow build-slot runs, no checkpoint-tool receipts)

- **Build:** lease `3cd9005a-0410-47f9-9adc-f461e106ff93`, `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-r7e11/ --property:UseAppHost=false`, 0 errors. Tests ran via `dotnet exec bin-r7e11/Antiphon.Tests.dll`.
- **`/*/*/PhoneHomeBackpressureTests/*` x10:** lease `3afa2be2-c857-4140-8522-f71bb32ec5bd`, every run total 7, failed 0. The class has 7 tests, not the 6 the brief says.
- **Same class x5 under 8 bash busy-loops (24 cores):** 4/5 passed, as in A-1. Rerun x10 under burners: 10/10.
- **Neighbour classes:** lease `6a98e7d0-be47-4c15-9886-623292a88081`, filter `/*/*/(PhoneHome*)|(AgentChannelServiceIntegrationTests*)|(AgentSessionRuntime*)|(RcRosterRepairTests*)|(C1514LandingSplitBudgetTests*)/*` with `--maximum-parallel-tests 4`.
  - Total 340, succeeded 334, failed 6. These are the same counts as the author and round 1. C1404, C1577, C1584, C1410 and G2865 live in `C1514LandingSplitBudgetTests`.
  - The 6 failures are `PhoneHomeMirrorJoinTests.C1032_Join*`: StaleBinding, IdleRecipient, StoreMismatch and ForeignOwner fail fast, and CrashRetry and BusyRecipient time out at 2 m.
  - Not confirmed at base by me. I relied on round 1's base run at `68565db92` (lease `e4ca7975-7351-41b1-bf6b-0d1bfb6b29b7`, 6/6 failed, same signatures). This is a budget disclosure.
- **Not rerun by me:** witness `687a378a2` red at the round-1 tip, and red of the three new channel witnesses at `23d77eed`. I am relying on the author's statement.
- **`scripts/check-evidence-diff.ps1 -Repository . -BaseRef 23d77eed... -HeadRef 1423c937e...`:** commits=4, entries=1, violations=0.
- **Cleanup:** the scratch test file was removed by its literal path, and `git status` is clean. I left the 32 `bin-r7e11` output directories in place, per the brief's cleanup rule.
