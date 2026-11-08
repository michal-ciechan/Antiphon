# Why missed: CARD-1773 canceled-row receipt counts (C1121)

This note covers the master red that stopped the rc/2026-11-13 cut (Merge task 02550ff6, report
`.antiphon/task-02550ff6.md` on `origin/feat/card-task-02550ff6` at `8e1138619`). The Debug task
is 94b17435, and its base was master `cad7766950ee96edc13e6ebf805ec5b4bbebbd6f`.

## Failure

`AgentTaskLandReceiptWatermarkTests.C1121_UnchangedTranscriptSkipsOnlyReceiptSelect(dispatch-base-canceled-row)`
(Integration) failed on every run at master `cad7766950`:

```
ShouldAssertException: passes.Select(p => p.Commands)
    should be [5, 4, 4]
    but was   [4, 1, 1]
```

Linux reproduction at `cad7766950` on server2, method filter
`/*/*/AgentTaskLandReceiptWatermarkTests/C1121_UnchangedTranscriptSkipsOnlyReceiptSelect`:
total 6, failed 1. The other five shapes passed.

## Cause and classification

The test expectation is out of date. The product is not wrong.

The introducing commit is `ae5b545d9ac9091f42847734b88bf8e202cf2814`, "fix(card-1773): park prune,
canceled terminal, pull dedupe, missing-queue park, kind index". It is the master pick of task
commit `26d5d4c0d4` (Code task d53f3fba). Its red-witness parent is `e0505cf8c8`. The Merge
bisect found that `e0505cf8c8` is 6/6 and `ae5b545d9a` is 5/6.

`AgentTaskLandNotificationService.ReconcileAsync` now treats Canceled as terminal:

- The reconcile that first sees a Canceled queue row sets the note Canceled
  (`queue_canceled_unconfirmed`), saves it and returns. It returns before the catch-up pull and the
  receipt scan. That pass runs 4 commands: note, session status, queue row, update.
- The early return now includes `LandNotificationState.Canceled`, so passes 2 and 3 load the note
  and stop. Each runs 1 command.

The change was deliberate:

- CARD-1773 A-4 ("Canceled can no longer late-confirm ... align the two").
- `docs/session-runtime-invariants.md` ("Canceled, missing and attempt-exhausted authoritative queue
  rows remain unconfirmed and are never replaced").
- The Review round 1 advisory A-1 (below).
- `C1773_CanceledNoteDoesNotLateConfirm` in `AgentTaskLandNotificationParkFollowUpTests` pins the
  new behaviour.

The C1121 canceled-row case was written for the old flow, which marked the note Canceled and then
still scanned and cached the miss. It now tests a path that no longer exists. A canceled
dispatch-base row owes no receipt, because Canceled is terminal and must not late-confirm. Option
(b), scanning that row anyway, would undo the CARD-1773 intent.

## Repair (test-only, master)

Branch `feat/card-task-94b17435`, fix `79f8db3b02cf17ff0b9ece4e57aafa65f2556b9c`. Only the
`dispatch-base-canceled-row` case changed:

- It now expects commands `[4, 1, 1]`, receipt selects `[0, 0, 0]`, no pull on any pass, and cache
  proofs, publishes and hits of 0/0/0. A comment names the CARD-1773 terminal intent.
- The Canceled state and `queue_canceled_unconfirmed` are still asserted.
- The other five shapes are unchanged. They still assert that the receipt select runs once and is
  skipped on unchanged passes (`[1, 0, 0]`, rows `[48, 0, 0]`), a pull on every pass, and 1/1/2
  cache metrics.

There is no separate red commit, because the red is the existing test at the base. Results on
Linux server2:

| Run | Result |
|---|---|
| C1121 method at `cad7766950` | 6 total, 1 failed (red reproduced) |
| C1121 method at `79f8db3b02` | 6/6 |
| `(AgentTaskLandReceiptWatermark*)\|(AgentTaskLandNotification*)\|(AgentTaskLandHeldNotificationTests*)\|(ReceiptScanDestinationIntegrationTests*)` | 161/161 in 4m34s. Watermark 36, WatermarkSafety 18, Held 12, Notification Recovery 49, Persistence 12, ParkFollowUp 6, ReconcileLoad 4, Rescan 1, ReceiptScanDestination 23 |
| Guard batch, own process | 52/52 |

## Reviews

- **Round 1: `3e56dbb0`**, Final, NOT CLEAN on B-1 (the migration retry). The brief named
  `3e76de6d`, but that is a CARD-1787 review. The reviewed subject was `4f20588c8c`, whose fix
  commit is `26d5d4c0d4`. The checkpoint run went through an ignored plan extract:
  - CP-1 `(AgentTaskLandNotification*)|(AgentTaskLandHeldNotificationTests*)`: 84/84.
  - CP-2: migration and debt classes, 28/28.
  - CP-3 `DispatchBaseNotificationTests`: 57/57.
  - CP-4: guard batch, 50/50.

  `AgentTaskLandReceiptWatermarkTests` was not selected. The `AgentTaskLandNotification*` prefix
  does not match `AgentTaskLandReceiptWatermark*`. Advisory A-1 described this exact behaviour
  change ("Canceled no longer confirms in the same pass ... returns before that scan"), but the
  review did not look for other tests that pin the Canceled-then-scan flow. A
  `grep -rn "QueuedMessageStatus.Canceled" tests/` lists the C1121 case.
- **Round 2: `eb0f53d8`**, Final, diff only, CLEAN. Its range covered only the migration repair:
  - CP-1 `Card1773TranscriptPromptStampMigration` and `TranscriptHotPathMigration`: 14/14.
  - CP-2 C0824/C1514 migrations: 12/12.
  - CP-3: guard batch, 50/50.

  Under the diff-only rule it did not re-run the reconcile family. A-1 stood as an advisory.

## Why Merge batch 132 did not select it

- The batch final-tree run used the class list the brief named. That list carried the land
  notification classes and omitted `AgentTaskLandReceiptWatermarkTests*`. That class is in the same
  family and calls the same `ReconcileAsync`. It had been repaired that morning for groups 67/68:
  `879a0327df` retargeted its CARD-1759 command budgets, which is the same `expected` array that is
  red here.
- The CARD-1782 impacted-classes selector was already on master (`ac1a77971d`, 11:48) before
  `ae5b545d9a` (12:17). Its six kinds are migrations, contracts, timing-labels, remote-scripts,
  statement-budgets and checkpoint-tool. None of them matches
  `server/Application/Services/AgentTaskLandNotification*.cs`. The migrations kind matched this
  commit's `AppDbContext.cs`, but it selects only migration callers.
- The CARD-1783 fixed Merge subset has no land-family row either.
- The rc cut smoke at the batch tip was the first run to take
  `AgentTaskLandReceiptWatermark*`, and it found the red at once.

## Cheapest check

Add a CARD-1782 selector row (a new kind in `tests/impacted-classes.json`):

- glob `server/Application/Services/AgentTaskLandNotification*.cs`, which should also cover
  `server/Infrastructure/Orchestration/AgentTaskLandNotification*.cs`
- filter `/*/*/(AgentTaskLandNotification*)|(AgentTaskLandReceiptWatermark*)|(AgentTaskLandHeldNotification*)/*`

On server2 that filter is about 150 Integration tests and takes about 4 to 5 minutes. With it,
Code, both Reviews and Merge batch 132 would have selected the red mechanically. A cheaper
alternative is to add the same family to the CARD-1783 fixed Merge subset whenever the batch diff
touches those files. Either one needs a card. This Debug task does not change the selector.
