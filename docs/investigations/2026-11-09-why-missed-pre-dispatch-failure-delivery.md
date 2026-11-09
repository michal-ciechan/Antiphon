# Why missed: PreDispatchFailureDeliveryTests canceled-row late confirm (rc 231f573c group-71)

Run 231f573c (rc/2026-11-13 `653f1145de5b72c2617a018cd606e0e8ba3cfe6e`, Windows) failed one test in
chunk `antiphon-all-group-71`. Debug task e4919459, base master `a0e14b3e1e5c83425ac070fac5d9e953b8d1be74`.
Windows read-out: `docs/investigations/2026-11-09-run-231f573c-pre-dispatch-failure-delivery-windows-failure.md`
(task 92798cff).

## Failure

`PreDispatchFailureDeliveryTests.Pre_dispatch_recovery_retains_authoritative_row(canceled)`
(Integration, Slow), at `AssertReceiptAsync` line 473:

```
ShouldAssertException: confirmed.State
    should be
LandNotificationState.Confirmed
    but was
LandNotificationState.Canceled
```

Linux at master `a0e14b3e1`, filter `/*/*/PreDispatchFailureDeliveryTests/*`: 13 total, 12 passed,
1 failed, the same assertion. It is red on every run: not load, not timing, not Windows-only.
The previous run a6410818 (rc/2026-11-12 `5fef2c23b`) predates the introducing commit.

## Classification

The test expectation is out of date. The product behaves as CARD-1773 intends.

The `canceled` case cancels the keyed queue row, so the DeliveryFailure note goes Canceled
(`queue_canceled_unconfirmed`). The fixture then restores the row to Pending, delivers it, and
expects the late prompt to confirm the note. Since CARD-1773 a Canceled note is terminal.
`ReconcileAsync` returns at the note load for `Canceled`, so a later prompt never confirms it.
`C1773_CanceledNoteDoesNotLateConfirm` pins that rule, and so does `docs/session-runtime-invariants.md`
("Canceled, missing and attempt-exhausted authoritative queue rows remain unconfirmed and are never
replaced"). Production cannot un-cancel a row, so only the fixture's revive reaches this path.

## Introducing commit/card

introducing commit/card: `ae5b545d9ac9091f42847734b88bf8e202cf2814` (CARD-1773), "fix(card-1773): park
prune, canceled terminal, pull dedupe, missing-queue park, kind index". It is the master pick of
`26d5d4c0d4` (Code task d53f3fba) and is an ancestor of `653f1145d`, not of `5fef2c23b`. It did not
touch `PreDispatchFailureDeliveryTests.cs`. The test reaches the changed code through
`AgentTaskDispatcher.RemindUnacknowledgedFailuresAsync` (`AgentTaskDispatcher.cs:5172`), which calls
`AgentTaskLandNotificationService.ReconcileAsync` (`:5246`).

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- 3e56dbb0 (round 1, `7ef90f1df`), scope Final, subject `4f20588c8c`, NOT CLEAN on B-1 (migration retry).
  Selection: CP-1 `(AgentTaskLandNotification*)|(AgentTaskLandHeldNotificationTests*)` 84/84; CP-2
  migration and debt classes 28/28; CP-3 `DispatchBaseNotificationTests` 57/57; CP-4 guard batch 50/50.
  Advisory A-1 described this exact change ("Canceled no longer confirms in the same pass") and named
  `C1773_CanceledNoteDoesNotLateConfirm`. It did not look for other tests that revive or confirm a
  canceled row.
- eb0f53d8 (round 2, `3d70598bf`), scope Final, diff only, CLEAN. Selection: the migration repair only
  (`Card1773TranscriptPromptStampMigration`, `TranscriptHotPathMigration` 14/14; C0824/C1514
  migrations 12/12; guard batch 50/50).

Later chances to see it:

- Merge batch 132 ran the class list in its brief: the land notification family, without this class.
- The rc/2026-11-13 cut smoke (`8e1138619`) ran 16 land, retention and Hangfire classes. It found the
  sibling C1121 red in `AgentTaskLandReceiptWatermarkTests`, not this class.
- Debug 94b17435 repaired C1121 (`79f8db3b0`, master `4b05e0e5c`) and wrote
  `2026-11-08-why-missed-card1773-canceled-receipt-count.md`. Its sweep for other victims was
  `grep -rn "QueuedMessageStatus.Canceled" tests/`. This test cancels through
  `parent.Queue.CancelAsync(...)`, so that grep did not list it. Its 161-test confirmation run used the
  `AgentTaskLand*`/`ReceiptScanDestination*` prefixes only.
- The CARD-1801 `land-notification` row (`c1f28848f`, 2026-11-09 06:18, after the cut) has a fixed
  nine-class list. At master `a0e14b3e1`,
  `scripts/impacted-classes.ps1 -Range ae5b545d9^..ae5b545d9` selects `land-notification,migrations`
  and does not select `PreDispatchFailureDeliveryTests`.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration lane, categories
Integration and Slow, path `tests/Antiphon.Tests/Application/PreDispatchFailureDeliveryTests.cs`. Every
selection was a class-name prefix of the land family (`AgentTaskLand*`). This class reaches the same
`ReconcileAsync` through the dispatcher's failure-reminder sweep, so its name shares no prefix with the
service, and nothing in it matched the migration derive patterns.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`server/Application/Services/AgentTaskLandNotification*.cs` -> `/*/*/(PreDispatchFailureDeliveryTests*)`

As a CARD-1801 selector row, keep the existing `land-notification` kind and add a derive block. The
derive selects every test class that asserts on the Canceled note state, whatever its name, so the
next cancel-path change catches the callers without anyone keeping a list:

```json
{
  "id": "land-notification",
  "globs": [
    "server/Application/Services/AgentTaskLandNotification*.cs",
    "server/Application/Services/LandNotificationReconcileQueries.cs"
  ],
  "classes": [
    "AgentTaskLandHeldNotificationTests",
    "AgentTaskLandNotificationParkFollowUpTests",
    "AgentTaskLandNotificationPersistenceTests",
    "AgentTaskLandNotificationReconcileLoadTests",
    "AgentTaskLandNotificationRecoveryTests",
    "AgentTaskLandNotificationRescanTests",
    "AgentTaskLandReceiptWatermarkSafetyTests",
    "AgentTaskLandReceiptWatermarkTests",
    "ReceiptScanDestinationIntegrationTests",
    "PreDispatchFailureDeliveryTests"
  ],
  "derive": {
    "patterns": [
      "LandNotificationState\\.Canceled\\b",
      "\"queue_canceled_unconfirmed\""
    ]
  }
}
```

Checked with a scratch copy of the table (`-Table`, `tests/impacted-classes.json` unchanged): for
`ae5b545d9^..ae5b545d9` the derive adds `AgentTaskLandReceiptTests`, `ExpectationDebtTests`,
`LandReceiptScanCacheTests` and `PreDispatchFailureDeliveryTests` to the current selection. The class
takes 45-65 s on server2 (13 tests). The literal `"queue_canceled_unconfirmed"` stays in the
repaired test, so the class stays selected after this fix. A table change needs its own card. This
Debug task does not edit the selector.

## Exemption

same cause as an already written note: same introducing commit as
[2026-11-08-why-missed-card1773-canceled-receipt-count.md](2026-11-08-why-missed-card1773-canceled-receipt-count.md)
(C1121). This is a second victim class that the C1121 sweep and the CARD-1801 row both missed, so this
note records the wider selector row.

## Repair

Test-only, master: `eba2444ac3f4fa5ab47d134b9535e8673507096f` on `feat/card-task-e4919459`. For
`state == "canceled"`, after the fixture restores and delivers the row, the test asserts the following:

- exactly one complete UserPrompt for the note body (the late prompt really exists)
- the note stays `Canceled`, with `ConfirmedAt` and `ConfirmingPromptSequence` null
- `LastErrorCode` stays `queue_canceled_unconfirmed`, on the same `QueueMessageId`
- there is still exactly one queue row (no replacement)

`missing` and `exhausted` keep the full `AssertReceiptAsync`. No other assertion changed. There is no
separate red commit, because the red is the existing test at the base. Linux results:

| Run | Result |
|---|---|
| `/*/*/PreDispatchFailureDeliveryTests/*` at `a0e14b3e1` | 13 total, 1 failed (red reproduced) |
| `/*/*/PreDispatchFailureDeliveryTests/*` at `eba2444ac` | 13/13 |

The fix is for master only. rc/2026-11-13 carries the same red until the fix is ported.
