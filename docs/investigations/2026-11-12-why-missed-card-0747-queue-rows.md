# Why the first CARD-0747 design missed aged queue rows

Date: 2026-11-12. Debug task 3b0adade. This follows
[2026-11-12-card-0747-sweep-dry-run.md](2026-11-12-card-0747-sweep-dry-run.md), which is on
`feat/card-task-eeb60643` at 89fef3ece. That run found that the orchestrator replay comes from
Pending queue rows, not from open land notes.

## What was missed

The operator dry run (2026-11-12T14:40Z) scanned 17 aged open notes and found 0 to settle. Session
`b2f1a869` still held 1,937 Pending rows, and 855 of them were older than seven days. Only 6 of
those 855 were linked to an open land note. Of the 849 with no open note, 767 had no land note at
all. Both the hosted cleanup and the sweep start from `AgentTaskLandNotifications`, so neither one
could reach those rows. The session was an adopted session ("Claude dialog observation
unavailable: adopted session"), and each adopted period built a new backlog. The queue then typed
that backlog oldest first, one row a minute, after the next restart.

## Why the first design missed it

1. **The problem was framed as a note problem.** The replay looked like "old land, task and check
   notes", and CARD-0747's first investigation (2026-11-11) counted land notes. The rule was then
   placed on the land-note outbox (`AgentTaskLandNotificationService.ReconcileAsync`, and
   `LandNoteExpirySweepService` on top of it). Queue rows were reached only through a note
   (`note.QueueMessageId`, `SourceLandNotificationId`), as a side effect of settling that note.
2. **Most of the queue has no outbox note.** Plain `[task … done/blocked/failed]` completion
   notices, Check notes (`SUPERSEDED — captured …`) and supervision notes are enqueued directly as
   `Delegation`, `Check` or `System` rows, with `SourceTaskId` and no land note. The outbox kinds
   (Held, Aged, Conflict, Outcome, TaskCompletion, DispatchBase) are only part of what a caller
   receives. A design that reaches rows only through notes skips the rest of the queue.
3. **The witnesses seeded from notes.** Every CARD-0747 witness seeded a land note first, using
   `SeedLinkedNoteAsync` or `AgentTaskLandReceiptTests.SeedAsync`, and then checked that note's
   row. No witness seeded a Pending row without a note. A row that has no note was therefore never
   a case anyone checked.
4. **The live census counted notes, not rows.** The design-time census reported note states:
   Confirmed, LegacyUnverified and open. It did not report Pending queue rows per caller by age.
   The 2026-11-12 dry run found the gap only because it also read
   `GET /api/sessions/{id}/messages` and grouped the rows by whether a note linked them.
5. **The off-session case was out of view.** The ordinary flush types a row when its caller next
   goes idle, so a row normally never ages. A row ages only while its caller cannot take input
   (adopted, withheld or stopped). That is exactly when no turn-end runs the queue. So no cleanup
   that depends on delivery could ever reach these rows.

## What changed (this task)

`AgedQueueRowExpiry` (`server/Application/Services/AgedQueueRowExpiry.cs`) cancels a queue row when
all of the following hold:

- the row is Pending and was never typed (`DeliveryAttempts == 0`);
- its origin is `Delegation` or `Check`, and it names a source task (for a Check row, the checked task);
- that task is Succeeded, Failed or Canceled;
- the row was created at or before the cutoff;
- no open land note links the row (in either direction), no provider-wait note links it in any
  state, and it is not a capacity wait.

The rule cancels the row under the session's queue lock. It never deletes a row. A typed row is
left to the queue, as the note rule already does.

- **Automatic cleanup.** `AgentTaskLandNotificationHostedService` runs the rule on its first pass
  after boot and then once an hour. The cutoff is `now - LandNoteReceiptExpiryDays` (default 7),
  and 0 or less turns the rule off, as it does for the note rule. A held queue lock skips that
  session for the pass, using the same 1 s bound as CARD-1705, and retries after a minute.
- **Operator sweep.** `POST /api/agent-tasks/land-notifications/expire` runs the note rule first and
  then the queue-row rule with the same cutoff, so no row is counted twice. The operator-token
  gate on apply, the 409 off switch, the 7-day minimum and the dry-run default are unchanged.
- **Response shape (additive).** The response gains a `queueRows` object:
  `{ scanned, outcomes{WouldCancel|Canceled|TypedPending|Busy|Changed: n}, eligibleByKind{Delegation|Check: n},
  oldest, newest, failures }`. `oldest` and `newest` are the creation times of the rows that were
  canceled, or would be. All existing fields are unchanged.

## Expected effect on b2f1a869 (from the 14:40Z census, not re-measured)

The aged rows with no open note include 785 Delegation rows and 38 Check rows, and every source
task is terminal (Succeeded 732, Failed 22, Canceled 69). That is about 822 `WouldCancel` and
1 `TypedPending` (the one Check row that was already typed). The 6 rows linked to an open note stay
with the note rule.

## Not covered (decisions for the caller)

- **26 aged `System` rows** (system notes, runner unavailable/recovered, keep-six). These rows have
  no source task, and the brief limits the rule to rows whose source task or check is terminal.
  They still replay.
- **1,082 Pending rows newer than seven days** on the same session (519 TaskCompletion, 84
  DispatchBase, 479 unlinked). These are never touched, by design. If the session's adoption
  ends before they age, they replay in order behind the aged rows, which are now canceled. An
  adopted-session hold or batch policy would cover them. Expiry is not the tool for it.
- The 11 `ReceiptPresent` TaskCompletion notes noted in the dry run stay with the
  carried-row/generation follow-up.
