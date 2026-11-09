# Run 231f573c group-71: PreDispatchFailureDeliveryTests(canceled) failure

Run 231f573c, rc/2026-11-13 at `653f1145de5b72c2617a018cd606e0e8ba3cfe6e`, desktop (Windows).
Checkout `C:\Antiphon\releases\checkout-0b36ae35` (HEAD = `653f1145de`). Read-only extraction from
`C:\Antiphon\releases\candidates\rc-2026-11-13-653f1145de5b\logs\2026-11-09-0440-231f573c\`:
`antiphon-all-group-71.trx`, `antiphon-all-group-71-tests.log`, `antiphon-all-group-71.timing.json`,
`antiphon-all-group-71-execution\log_261109071845887.diag`.

## Verdict

**Deterministic. The test expectation is out of date; the product is behaving as CARD-1773 intends.**
Same class as the CARD-1773 C1121 red already written up in
`docs/investigations/2026-11-08-why-missed-card1773-canceled-receipt-count.md`, a second test
that still pins the pre-CARD-1773 "Canceled can late-confirm" flow. Not load, not timing, not
Windows-specific. Still unfixed on `origin/master` (`a0e14b3e1`): the `canceled` row and its final
`AssertReceiptAsync` are unchanged.

## Failed test

- Test: `Antiphon.Tests.Application.PreDispatchFailureDeliveryTests.Pre_dispatch_recovery_retains_authoritative_row(canceled)`
  (categories Integration, Slow; TUnit node `...Pre_dispatch_recovery_retains_authoritative_row(System.String).1.1.0`)
- Outcome: Failed. Duration `00:00:01.2593326` (07:20:34.551Z to 07:20:35.810Z).
- Siblings of the same method passed: `(exhausted)` 1.294 s, `(missing)` 1.191 s.

Message (TRX `ErrorInfo/Message`):

```
ShouldAssertException: confirmed.State
    should be
LandNotificationState.Confirmed
    but was
LandNotificationState.Canceled
```

Stack (TRX, complete as recorded; TUnit internals omitted by the runner):

```
   at Antiphon.Tests.Application.PreDispatchFailureDeliveryTests.AssertReceiptAsync(BridgeQueueHarness parent, AgentTaskLandNotification debt, Guid rootTaskId, CancellationToken ct) in C:\Antiphon\releases\checkout-0b36ae35\tests\Antiphon.Tests\Application\PreDispatchFailureDeliveryTests.cs:line 473
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.PreDispatchFailureDeliveryTests.AssertReceiptAsync(BridgeQueueHarness parent, AgentTaskLandNotification debt, Guid rootTaskId, CancellationToken ct) in C:\Antiphon\releases\checkout-0b36ae35\tests\Antiphon.Tests\Application\PreDispatchFailureDeliveryTests.cs:line 474
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.Tests.Application.PreDispatchFailureDeliveryTests.Pre_dispatch_recovery_retains_authoritative_row(String state) in C:\Antiphon\releases\checkout-0b36ae35\tests\Antiphon.Tests\Application\PreDispatchFailureDeliveryTests.cs:line 418
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.PreDispatchFailureDeliveryTests.Pre_dispatch_recovery_retains_authoritative_row(String state) in C:\Antiphon\releases\checkout-0b36ae35\tests\Antiphon.Tests\Application\PreDispatchFailureDeliveryTests.cs:line 418
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.PreDispatchFailureDeliveryTests.Pre_dispatch_recovery_retains_authoritative_row(String state) in C:\Antiphon\releases\checkout-0b36ae35\tests\Antiphon.Tests\Application\PreDispatchFailureDeliveryTests.cs:line 418
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.PreDispatchFailureDeliveryTests.Pre_dispatch_recovery_retains_authoritative_row(String state) in C:\Antiphon\releases\checkout-0b36ae35\tests\Antiphon.Tests\Application\PreDispatchFailureDeliveryTests.cs:line 418
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

Standard output: only `ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Application.PreDispatchFailureDeliveryTests.1.1.Pre_dispatch_recovery_retains_authoritative_row(System.String).1.1.0`.

No timeout, process, git, pwsh or path text appears in the failure beyond the source paths above.
The diag file has no timeout or timed-out line for any test.

## Chunk summary

- TUnit v1.44.0.0, .NET 9.0.22, MTP v2.2.2, win-x64, Windows 10.0.19045, `ProcessorCount: 8` (diag).
- total 166, failed 1, succeeded 165, skipped 0; run wall 1m 58s (07:18:46Z to 07:20:44Z). Exit code 2
  (the TRX `RunInfo outcome="Error"` line is only that exit code, not a second failure).
- timing.json: build 468.6 s, fixture setup 3.6 s, process spawn 0.305 s, process wait 7.0 s, p50 4.1 s,
  p95 15.2 s, max 45.6 s (`C1259_commit_and_effect_admission_are_atomic(replay-after-ack)`).

Neighbouring tests (TRX, serial in this part of the chunk, no overlap with the failure):

| Start (Z) | Duration | Outcome | Test |
|---|---|---|---|
| 07:20:28.218 | 1.294 s | Passed | `Pre_dispatch_recovery_retains_authoritative_row(exhausted)` |
| 07:20:29.514 | 1.191 s | Passed | `Pre_dispatch_recovery_retains_authoritative_row(missing)` |
| 07:20:30.706 | 1.105 s | Passed | `Pre_dispatch_receipt_requires_complete_user_prompt` |
| 07:20:31.812 | 2.383 s | Passed | `Pre_dispatch_snapshot_survives_task_retry` |
| 07:20:34.196 | 0.354 s | Passed | `Pre_dispatch_excluded_routes_create_no_obligation(Session)` |
| 07:20:34.551 | 1.259 s | **Failed** | `Pre_dispatch_recovery_retains_authoritative_row(canceled)` |
| 07:20:35.821 | 1.773 s | Passed | `Preflight_failure_obligation_commits_with_its_claim(True)` |
| 07:20:39.172 | 1.124 s | Passed | `Pre_dispatch_failed_insert_recovers_with_checks_disabled(True)` |
| 07:20:40.297 | 1.056 s | Passed | `Pre_dispatch_failed_insert_recovers_with_checks_disabled(False)` |
| 07:20:41.354 | 1.284 s | Passed | `Pre_dispatch_failed_commit_keeps_the_caller_obligation` |
| 07:20:42.641 | 0.225 s | Passed | `Pre_dispatch_excluded_routes_create_no_obligation(None)` |
| 07:18:56.937 | 19.076 s | Passed | `Pre_dispatch_recovery_waits_for_destination` |

Host load hints: none. The failing case ran alone, at the same ~1.2 s as its passing siblings, with no
waits or retries; the 2026-10-29 timing census lists this file as strict (excluded from the width-2 process).

## Diagnosis

Test, `tests/Antiphon.Tests/Application/PreDispatchFailureDeliveryTests.cs` at `653f1145de`:

- 384-385: for `canceled`, `parent.Queue.CancelAsync(...)` cancels the keyed queue row.
- 397-407: two `RecoverAsync` passes; asserts `ConfirmedAt` null and `LastErrorCode ==
  "queue_canceled_unconfirmed"`. **These pass**, so the note was moved to Canceled here.
- 411-417: fixture restores the same row to `Pending`, `DeliveryAttempts = 0`, ends the turn, flushes,
  recovers once more.
- 418 -> 457-474 `AssertReceiptAsync`: lines 461-471 pass (row delivered, `DeliveryAttempts > 0`, one
  complete UserPrompt after the baseline, adapter submitted exactly once). **Line 473 fails**: the note
  is still `Canceled`, not `Confirmed`.

Product, `server/Application/Services/AgentTaskLandNotificationService.cs` at `653f1145de`:

- 65-70 `ReconcileAsync`: an early return for `Confirmed`, `NotRequired`, `LegacyUnverified` **or
  `Canceled`**: a Canceled note is never reconciled again.
- 220-226: on a Canceled queue row: `note.State = LandNotificationState.Canceled;
  note.LastErrorCode = "queue_canceled_unconfirmed";` with the comment "CARD-1773. Canceled is
  terminal. A later prompt must not confirm the note."

Both come from `ae5b545d9ac9091f42847734b88bf8e202cf2814` (2026-11-08 12:17Z, "fix(card-1773): park
prune, canceled terminal, pull dedupe, missing-queue park, kind index"), which is an ancestor of the rc
cut and touched no `PreDispatchFailureDeliveryTests` file. `C1773_CanceledNoteDoesNotLateConfirm`
(`AgentTaskLandNotificationParkFollowUpTests.cs:129`) pins the new terminal intent; this test's
`canceled` row still requires the old late confirm after the fixture revives the row. With Canceled
terminal, no amount of time or load makes line 473 pass. The `missing` and `exhausted` rows are
unaffected because their notes stay non-terminal (`keyed_queue_row_missing`, `queue_parked_unconfirmed`)
and confirm after the restore.

## Why it was missed before the rc run

Same gap as the C1121 write-up: CARD-1773's Code/Review checkpoints and Merge batch 132 selected the
`AgentTaskLandNotification*` family (plus, after the C1121 repair, `AgentTaskLandReceiptWatermark*`),
but not `PreDispatchFailureDeliveryTests`, which reaches the same `ReconcileAsync` through
`AgentTaskDispatcher.RemindUnacknowledgedFailuresAsync` (`AgentTaskDispatcher.cs:5172`, reconcile at ~5245) and also cancels a queue row and expects a later
confirm. The CARD-1782 impacted-classes selector has no kind that maps
`server/Application/Services/AgentTaskLandNotification*.cs` to callers of the reconcile. A
`grep -rn "QueuedMessageStatus.Canceled\|Queue.CancelAsync" tests/` over the land-reconcile callers would
have listed this case alongside C1121.

## Repair direction (for the Debug task; no code changed here)

Test-only, mirroring the C1121 repair: for `state == "canceled"`, after restore and delivery assert the
note stays `Canceled` with `queue_canceled_unconfirmed` and `ConfirmedAt` null (no late confirm), and
keep the full `AssertReceiptAsync` for `missing` and `exhausted`. Filter:
`/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_recovery_retains_authoritative_row*`.
Fixes for rc code go to both rc and master. Add `PreDispatchFailureDeliveryTests*` to the CARD-1782
selector row proposed in the C1121 write-up.
