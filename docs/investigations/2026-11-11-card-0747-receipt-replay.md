# CARD-0747: stale land and completion notes replayed into orchestrator b2f1a869

Date: 2026-11-11/12. Debug task 3593eb2f. Evidence comes from read-only `BEGIN READ ONLY` SELECTs against the
desktop `antiphon` database (Postgres on 17280, from the server2 runner mirror), and from code at base
`3db1a43b0`. There was no SQL write, no restart and no live apply.

## Verdict

**This is not a replay.** No note was typed twice. Orchestrator session `b2f1a869` was an *adopted* session
from 2026-10-24 to 2026-11-11. The queue cannot type into an adopted Claude session, so every note for it
was withheld (`LastWithholdReason = "Claude dialog observation unavailable: adopted session"`,
`WithholdCount` up to 4). That ran for 17 days, and about 1,900 keyed land and completion rows piled up
`Pending`, never typed. The session restarted under the server at **2026-11-11T17:49:46Z**
(`AgentSessions.StartedAt`). The adoption, and the withhold with it, ended then. From that point the
queue started typing the backlog oldest-first, one WhenIdle row per idle turn: 2, 38, 33, 40 and 12
sends in the hours 18Z to 22Z. Every note body was dated 2026-10-25 or 2026-10-26, so it read as a
replay. Two checks over the session transcript confirm this. 117 distinct `[land <id>` headers in the
last three days were each typed exactly once. 85 distinct `[task <id> done]` headers over the whole
session were also each typed once. `Report withheld — you already read it` is the normal
TaskCompletion rendering for a result the caller had already polled.

Request `b8858374` has **46** Held notes (one per lease-holder change). Its request is `Completed`
(`IsPending=false`), yet each Held snapshot is still typed in turn.

Underneath that sits the original CARD-0747 and CARD-0513 defect: a delivered note that never reaches a
terminal confirmed state, so an outcome keeps generating LandAged notices. Two shapes still exist:

1. **Carried completion rows.** When a standing agent restarts fresh, `AgentControlService` moves its
   Pending queue rows onto the new session (`message.AgentSessionId = accepted.Id`), and the note keeps
   the old `ParentSessionId`. CARD-1157 made the receipt scan follow such a row for the non-legacy
   Held/Aged/Conflict/Outcome shapes. TaskCompletion was left out. A profiled completion stops at
   `completion_queue_identity_mismatch` (11 live notes). An unprofiled one stops at
   `completion_delivery_generation_changed`, because the generation is compared against the *parent*
   session's start. All 11 live profiled notes do have a receipt on the session they were typed to.
2. **No end state for old notes.** A note whose caller is gone (`DestinationUnavailable`), or whose row
   was never typed, stays open forever. It is re-paged every 5 s (CARD-1121 and CARD-1759 measured this
   cost), it stays an `LandOutcomeUnconfirmed` attention row, and its row can still be typed weeks later.

## Census (2026-11-11 ~22:15Z, open = State not in Confirmed/NotRequired/LegacyUnverified)

| State | Kind | Open | Older than 7 d | Linked |
|---|---|---:|---:|---:|
| AwaitingReceipt | Held | 71 | 71 | 71 |
| AwaitingReceipt | Aged | 149 | 149 | 149 |
| AwaitingReceipt | Conflict | 3 | 3 | 3 |
| AwaitingReceipt | Outcome | 46 (+5 legacy) | 46 (+5) | all |
| AwaitingReceipt | DispatchBase | 425 | 309 | 425 |
| AwaitingReceipt | DeliveryFailure | 2 | 2 | 2 |
| AwaitingReceipt | TaskCompletion | 1,124 | 582 | 1,124 |
| AwaitingReceipt | ProviderInputWait | 4 | 4 | 4 |
| DestinationUnavailable | Held/Aged/Conflict/Outcome/DispatchBase/TaskCompletion | 6/90/1/33/2/10 = 142 | 142 | 0 |
| Canceled | Aged/Outcome/DispatchBase/TaskCompletion | 4/2/1/6 = 13 | 13 | 13 |

By destination, `b2f1a869` held **1,787** of the linked AwaitingReceipt notes. All were `Pending` and
withheld as an adopted session; between them they had 7 delivery attempts. The next largest
destinations were Stopped sessions with 12, 10 and 7 typed (`Sent`) notes. 18 linked notes have a queue
destination that differs from their parent, all carried rows on Stopped or Failed sessions. Eleven of
those are profiled TaskCompletions with the receipt present on the carried session; seven are
2026-09-13 Aged and Outcome rows from the CARD-0513 sighting, with no matching prompt.

Older than 7 days and terminal (request not pending, or task Succeeded/Failed/Canceled): **1,305** in
the first count (1,122 untyped Pending, 41 Sent, 142 unlinked), plus 3 typed-Pending notes that are left
alone.

## Fix (branch feat/card-task-3593eb2f)

- **Carried completion** (`ReceiptScanTarget.FollowsCarriedCompletion`). It applies to a TaskCompletion
  whose keyed row (same note id, same digest, Sent, one attempt, generation recorded) sits on another
  session. That session supplies the identity check, the delivery generation and the receipt scan. The
  note keeps its parent. A prompt on the parent does not confirm it.
- **Aged terminal expiry** (`AgentTaskLandNotificationService.ExpireOnReconcileAsync`, threshold
  `Supervision:DeliveryVerification:LandNoteReceiptExpiryDays`, default 7, 0 disables). The rule covers a
  terminal note in `AwaitingReceipt` or `DestinationUnavailable` that was created more than that long ago.
  It settles as `LegacyUnverified` with `receipt_expired_aged`. A never-typed row is canceled first, under
  the session queue lock, which the hosted scan bounds to 1 s (CARD-1705). Two cases are left alone. A row
  typed once and still Pending stays on the ordinary path, where its receipt still confirms it. A Sent row
  whose receipt is already stored (row session or parent; note body, typed body or frozen wire text) is
  confirmed by the ordinary reconcile. Fresh notes behave as before.
- **Held supersession**: a Held snapshot whose request is no longer pending is settled `NotRequired`
  (`held_superseded_by_terminal_outcome`) before it is enqueued, or its never-typed row is canceled. A
  typed Held row keeps its receipt scan.
- **Attention**: a settled `LegacyUnverified` note is no longer an `LandOutcomeUnconfirmed` row.
- **Operator sweep**: `POST /api/agent-tasks/land-notifications/expire` `{ apply?, olderThanDays? }`
  runs the same rule on demand. It is a dry run by default, `olderThanDays` must be at least 7, a child
  task token cannot apply, it never deletes, and a rerun is idempotent ([ops-http](../ops-http.md)).

No migration: `LegacyUnverified`, `NotRequired` and `Canceled` already exist.

## Dry run against the live database (2026-11-12T00:53Z)

The live server does not have the endpoint yet. These counts come from a read-only SELECT that mirrors the
sweep's predicate (open, created ≤ now − 7 d, terminal as above, row classification):

| Sweep outcome | Notes |
|---|---:|
| WouldSettle, untyped Pending row (cancel, then settle) | **1,082** (Held 41, Aged 139, Conflict 1, Outcome 44, DispatchBase 308, DeliveryFailure 2, TaskCompletion 547) |
| WouldSettle, unlinked DestinationUnavailable | **142** |
| Sent: receipt check first, then WouldSettle if no stored receipt | **46** (Held 1, Aged 5, Outcome 6, DispatchBase 2, TaskCompletion 32) |
| TypedPending (left alone) | 2 |
| NonTerminal (ProviderInputWait, left alone) | 4 |
| **Scanned** | **1,276** |

For `b2f1a869` that is 1,085 of its 1,091 aged open notes. Another **663** of its notes are younger than
7 days. They are untouched by design, so at the observed 25–40 rows per hour they will still be typed
for about 17–27 h after deploy. They expire on their own as each one turns 7 days old.

**Apply: not done.** The supported path exists only on this branch, and production runs the rc line.
The before and after counts above are owed after the server runs a build that contains it, through
`POST .../land-notifications/expire {"apply":false}` and then `{"apply":true}`. With the hosted
reconcile rule deployed, the scan itself settles these within about 5 minutes of start, so the apply
call mostly confirms the count.

## Why this was missed

- **Selection.** CARD-1121 (2026-10-08 census, line 96) found the parent-versus-destination scan
  mismatch and recorded it as "the orchestrator should decide whether that is a separate defect card".
  CARD-1157 then fixed it only for the four `AcceptsQueuedPrompt` kinds. Its whitelist (D-2 item 4)
  excludes TaskCompletion by construction. `ReceiptScanDestinationTests` and
  `C1121_ForeignDestinationFollowsKeyedRow` only arrange Outcome notes, so no witness ever carried a
  completion row.
- **No age dimension.** Every receipt test seeds `CreatedAt = now`. None asks what a note owes once its
  destination has been undeliverable for days, and the hosted page (CARD-1759) treats every linked
  note as due forever. The CARD-1759 census (2026-11-07: about 1,540 AwaitingReceipt, oldest
  2026-09-17) saw the cohort but measured cost, not end state.
- **Withheld is invisible as debt.** `PendingNoteAlert*` raises one attention row per destination,
  but nothing compared the backlog's age with its eventual delivery. Adopted sessions withhold quietly
  and resume delivery FIFO.
- **Held fixtures used terminal requests.** `AgentTaskLandReceiptTests.SeedAsync` creates a Completed
  request, and the queued-receipt fixture switched the kind to Held on top of it. Tests therefore
  treated typing a Held snapshot after the outcome as normal. The fixture now makes the request pending
  first.

## Verification

- Red witnesses `C0747LandReceiptReplayTests` at `785d5db11e44a2c47459ee4e728391995c35b4fd`: 8 red,
  6 controls green.
- Guard union: 55 classes, 1,785 tests over 8 shards at `448e1e59`. The only red is
  `AgentTaskWorktreeLockOutcomeTests` C443 ×20, which is inherited: the same `PtyDeliveryProfile`
  `SingleWriteMaxBytes 1024 vs 86400` assertion is red at base `3db1a43b0` (20/21). There was one load
  flake, `C508_GuardRefMismatchWarnedOnce(2, True)`, green alone.
- Mutants: 15 runs (17 edits), all killed. The TypedPending-guard mutant was killed only after the
  typed-Pending control gained a receipt.

## Owed

- Native (Windows desktop) proof of the touched classes. Every run here was Linux server2.
- After deploy: the sweep dry-run and apply counts, and the `b2f1a869` backlog draining to its younger
  than 7 days remainder.
- Not pinned: the bounded lock wait on the cancel (the busy-session path) and `LandNoteReceiptExpiryDays <= 0`.
  `TerminalRunnerSeatReleaseService.HasPendingDeliveryAsync` and the dispatcher's DeliveryFailure
  recovery still count a `LegacyUnverified` note as open. That is unchanged behaviour, a candidate
  follow-up.
- The adopted-session withhold itself (weeks of silent backlog) is a separate defect, and this card does
  not change it.
