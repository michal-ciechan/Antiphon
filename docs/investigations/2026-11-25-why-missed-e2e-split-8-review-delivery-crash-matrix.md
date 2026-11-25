# Why missed: rc run b8cbfb24 e2e-all-split-8 Review delivery crash matrix

Windows Full rc run b8cbfb24 (rc/2026-12-05 pin 2fd61b643ea4231fa732cd4969b5284d43b33f39), chunk `e2e-all-split-8.trx`: 1 failed row.

- `AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix_Batch3(queue-committed-before-wakeup)`, 6 min 2 s: `TimeoutException: complete native Review receipt` from `LandDeliveryFixture.WaitForReviewReceiptAsync` (`LandDeliveryFixture.Review.cs:120`).
- Recorded state at the timeout: one `TaskCompletion` note `AwaitingReceipt`, `lastErrorCode` null, `enqueueAttempts` 1; its queue row `Sent`, `deliveryAttempts` 1, `lastDeliveryStartedAt` 21:17:11.814926Z; the Review `StageOutcome` Clean, `recordedAt` 21:17:10.748775Z; `completionScanCount` 0.
- The same method passed in the previous run acca11ba, same chunk.

## Verdict

(b) with a timing trigger: a fixture isolation bug, not a product defect. The cut is meant to model a crash after the queue commit and before any wakeup, but the held child process still woke the caller itself. Under qualification load, the kill landed after the row was claimed and before the prompt reached the caller. That leaves the CARD-1892 unknown attempt, which a Grok caller holds and never retypes, so no receipt was ever owed. The fix is test-only.

## Facts

- The cut (`review-before-wakeup` in `LandDeliveryOptions.FileBoundary`) parks `AgentTaskLandNotificationService.ReconcileAsync` at `queue-committed-before-wakeup`. That is after the keyed queue insert has committed and the note link has been saved, and before the reconcile's own `flushes.TryEnqueue(session)`. The test then sees the barrier file, asserts the held state, and kills the child.
- `SessionMessageQueueService.EnqueueCoreAsync` publishes its own wakeup for every sourced `Delegation` row once the insert commits: `_completionNotes.Recovery.ScheduleFlush(sessionId, holdUntil ?? MinValue)`, behind `DropWakeup("completion-schedule")` (CARD-1360). The Review note is `Delegation`, sourced and digested, and the `raw` form has no hold, so that hint is due at once.
- `CompletionNoteWorkHostedService` takes that hint and runs `FlushIfIdleAsync` on the idle caller. The enqueue has returned by the time the producer parks, so the session's queue lock is free, and the flush claims the row (Sent, attempt charged, `LastDeliveryStartedAt`) and starts typing while the producer is still parked. The 5 s notification scan can also wake a linked, unattempted row (`DropWakeup("completion")`) in the same window.
- The Windows timestamps show it. The attempt started 1.07 s after the Review outcome was recorded, in the cut child: a restarted child takes many seconds just to start. The row kept attempt 1 with a null verdict, and the note had no receipt. The kill had landed between the claim and the prompt.
- For a caller with no composer reader (the fixture's caller is native FakeGrok), CARD-1892 holds a Sent/null-verdict attempt for a complete UserPrompt or an orchestrator decision. It never retypes it (see `AssertUnknownPreTypingAttemptHeldAsync`, and the `before-typing` cut, which expects exactly this). The restarted child therefore had nothing to deliver, and `WaitForReviewReceiptAsync` timed out after 180 s.
- `AssertReviewHeldAsync` checked only `ConfirmedAt == null` and the link for this cut. It never checked that the row was still unattempted, so a pass that won the race never revealed it. The `queue-inserted-before-outbox-link` cut does check `DeliveryAttempts == 0`. That check holds because its barrier runs inside `EnqueueCoreAsync` under the session queue lock, so the flush waits for the lock. Only `queue-committed-before-wakeup` parks after the lock is released.
- Whether a run passes depends on where the kill lands. If it lands before the flush claims the row, the restarted child delivers. If it lands after the prompt is typed, the restarted child late-confirms from the transcript. Only a kill that lands inside the claim-to-prompt window fails. Load on the qualification host widens that window (typing through ConPTY, the delivery verification poll) and delays the kill. acca11ba passed. b8cbfb24 hit the window.

## Product behaviour (not changed)

A host crash between the claim and the prompt on a composer-less caller leaves an unknown delivery that is held for an orchestrator decision. That is the CARD-1892 contract: the stranded-queue watchdog records `input-delivery-unknown`, and nothing retypes the attempt. The queue-side hint is a deliberate second wakeup (CARD-1360). `C467_V28_LostFlushWakeupRecoversOnIdleCaller` relies on it. Neither is a defect. The test was wrong to treat its cut as "no wakeup has fired yet".

## Fix

- `tests/Antiphon.E2E/Fixtures/LandDeliveryOptions.cs`: the `review-before-wakeup` cut also drops the held child's `completion-schedule` and `completion` wakeups, each with a `wakeup-dropped-*` record. The child is killed at the barrier, so this is the crash the cut describes. The restarted child (cut `none`) recovers through its boot sweep (`completion-scan`) and its 5 s scan wakeup, as for every other crash cut.
- `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.Review.cs` (`AssertReviewHeldAsync`): at this cut the row must be `Pending` with `DeliveryAttempts == 0`, and a `completion-schedule` drop record must name that row. A future in-child wakeup now fails at the hold with a clear message. Before, it showed up as a 180 s receipt timeout three steps later.
- No product code changed. No timeout was widened, no retry was added and no assertion was loosened.

## Linux witness

The fixture is Windows-only (`NativeWindowsGate`: the native `fakegrok.exe` through ConPTY), so this method skips on Linux and cannot go red there. The mechanism is reproduced deterministically on Linux with the real queue, notification service and completion worker in `TurnEndGapDeliveryHarness`: `TurnEndGapRestartDeliveryTests.C488_Wakeup_cut_holds_the_row_only_when_in_process_hints_are_dropped`.

- `(False)`: hints live, as the old cut ran. While the producer is parked at `queue-committed-before-wakeup`, the idle caller is typed. The row is Sent with attempt 1, which is the precondition of the Windows failure.
- `(True)`: `completion` and `completion-schedule` dropped, as the fixed cut runs. The row is still Pending with 0 attempts at the cut, and nothing is typed. After release, the producer's own wakeup delivers it once.

## Notes fix (feat/card-task-550744a3) does not overlap

That branch changes caller-report receipt closure (`CompletionNoteStamp.RecordReceiptsAsync` / `RecoverReceiptsAsync`, the `ReceivedReport*` columns, `CallerWakeService`). `IsCallerCompletionNote` requires `SourceLandNotificationId == null`, so the land-notification TaskCompletion row in this test is outside its scope. It does not touch `AgentTaskLandNotificationService`, `CompletionNoteWorkHostedService`, the `completion-schedule` hint, the CARD-1892 hold or the E2E fixtures. There is no double fix, and this repair does not depend on it.

## Why Linux and the short Merge tests missed it

- **Windows-only fixture.** `LandDeliveryFixture` calls `NativeWindowsGate.Require` (CARD-0588: native FakeGrok through the shipped ConPTY pair). Every C467/C488 land-delivery E2E method skips on Linux and the server2 lanes, so only the Windows qualification runs it.
- **Short Merge tests.** Merge runs narrow witness rows only (2026-11-18). This E2E class is never in that set, and no master change touched the fixture or the delivery path. The pin's fixture is the one that passed in acca11ba.
- **A race that only load exposes.** The cut was never isolated. Whether a run passes depends on where the kill lands relative to an in-child flush (see Facts). An idle desktop tends to finish typing before the kill, or to kill before the claim. The live qualification load stretches the claim-to-prompt window.
- **A blind hold assertion.** The held-state check for this cut did not check the row's attempts, so the passing runs never showed that the row had already been claimed.

## Same pattern elsewhere

Any E2E crash cut that parks after `EnqueueCoreAsync` has released the session lock can race the queue-side hint. In `FileBoundary` that is only `queue-committed-before-wakeup` (`review-before-wakeup`). `queue-inserted` and `queue-before-commit` park under the lock. `idle-lost-wakeup` drops only `completion` and deliberately leaves the queue-side hint as its recovery source, like V28.

## Cheapest pre-land check

`tests/Antiphon.E2E/Fixtures/LandDeliveryOptions.cs` or `LandDeliveryFixture*.cs` → Windows lane `/*/*/AgentTaskLandDeliveryE2ETests/C488_ReviewDeliveryCrashMatrix_Batch3`. Linux: `/*/*/TurnEndGapRestartDeliveryTests/C488_Wakeup_cut_holds_the_row_only_when_in_process_hints_are_dropped` plus an E2E compile.

## Exemption

none

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c488-wake/` | c488-wake | `/*/*/TurnEndGapRestartDeliveryTests/C488_Wakeup_cut_holds_the_row_only_when_in_process_hints_are_dropped` | mechanism witness, both arms | 2 executed, 0 failed, 0 skipped | 2 | 15 |

Owed on the Windows lane: `/*/*/AgentTaskLandDeliveryE2ETests/C488_ReviewDeliveryCrashMatrix_Batch3` (both cases).

## Verification

Checkpoint run `20261125-213033-2216` (server2, Linux), commit `4d4567efc8057dc649eac6002cb4c47bb858e766`, sourceState=clean, buildSource=verified, `validate` → `CHECKPOINT SOURCE VALID rows=1`:

- CP-1 `/*/*/TurnEndGapRestartDeliveryTests/C488_Wakeup_cut_holds_the_row_only_when_in_process_hints_are_dropped`: 2 executed, 2 passed, 0 failed, 0 skipped. `(False)` shows the row claimed (Sent, attempt 1) and typed while the producer is parked at the cut. No scan service runs in the harness, so only the queue-side `completion-schedule` hint could have done it. `(True)` shows it Pending and unattempted until release.

Unlisted build, with the reason: `dotnet build tests/Antiphon.E2E -p:UseAppHost=false` through `build-slot.ps1` (lease granted, 127 s), 0 errors. This is a compile check only, because the changed fixture's method skips on Linux. No new warnings on changed lines.

Owed on the Windows lane: `C488_ReviewDeliveryCrashMatrix_Batch3` (both cases). It cannot be run from server2.
