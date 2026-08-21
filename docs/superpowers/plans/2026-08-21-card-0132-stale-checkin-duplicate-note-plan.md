# CARD-0132 — stale SUPERSEDED check-ins and duplicate completion notes: plan

**Date:** 2026-08-21 · **Card:** CARD-0132 (`e43131a4-6e23-40b2-8a5e-21068a8e6c75`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** master `e95d393`, the live dev Postgres on 17280, and the caller session's own
JSONL (`~/.claude/projects/C--src-Antiphon/cefed08a-….jsonl`).

**Precedent this plan reverses:** CARD-0074 (`7d119ef`, 2026-08-19) decided *"mark, never suppress"*
for exactly these notes. CARD-0132 overrides that decision. §"The CARD-0074 reversal" says what
survives.
**Precedent this plan does NOT get to use:** CARD-0067's durable settled-marker is **not** the shape
of defect 2 (§2). CARD-0091 owns the question of a new `SessionQueuedMessage` terminal status —
this plan must not invent one, and does not (§S1.2).

---

## Verdict

**Two patterns, two root causes, two mechanisms, two slices. A single before-delivery
settled-status check cannot fix both — and would be actively wrong on the second.**

1. **Stale SUPERSEDED check-ins** are a *policy* defect, not a bug. Everything the card asks for is
   already computed; CARD-0074 deliberately chose to prepend a banner and deliver anyway, at two
   named points. The fix is to flip that choice at those same two points, plus a last-look inside
   the flush that closes the residual race neither of them can. One predicate, three call sites.
   **Measured: 10 of 25 check notes delivered since CARD-0074 shipped were typed into the caller's
   terminal after their task had already settled** (40%; 17/65 across all history).

2. **Duplicate completion notes** are a *blind spot in CARD-0055's delivery confirmation*, and have
   nothing to do with settled status, reconnects, or backfill. When a note is typed into a caller
   whose TUI is mid-turn, Claude Code **queues it in its own composer queue** and, on dequeue,
   writes it into the conversation as a `type: "attachment"` record carrying
   `attachment.type: "queued_command"` — **never** as a `user` record. `TranscriptNormalizer`
   drops `type: "attachment"` entirely (`src/Antiphon.SessionRunner/TranscriptNormalizer.cs:70-77`,
   default arm `_ => []`), so no `UserPrompt` row is ever stored. CARD-0055's confirm →
   grace-confirm → late-confirm chain all read the same query, all find nothing, and the message is
   returned to Pending and **re-typed byte-for-byte**. **Measured: 5 of 5 completion notes that
   landed as `queued_command` since CARD-0055 shipped were delivered to the caller twice.** The
   guard the card wondered about *exists* (CARD-0055's late-confirm is the anti-duplicate keystone,
   `SessionMessageQueueService.cs:692-696`) — it is simply blind to this record shape.

A settled-status check cannot help defect 2: at the moment a completion note is delivered the task
**is** settled — that is why the note exists. Suppressing Delegation-origin messages on settled
tasks would suppress every completion note there has ever been.

The two do share one *seam* — both land in the queue's delivery path — but they are different
questions asked of different data: *"is the subject of this message still live?"* (S1) versus
*"did this body already land?"* (S2). Two slices, shippable and reviewable independently, in
either order.

---

## Ground truth this plan stands on

All code reads on master `e95d393`. All DB facts from the live dev Postgres (17280) on 2026-08-21.

### Defect 1 — the check-in

- **The digest is generated before the settled test can possibly be current.**
  `AgentTaskCheckService.RunCheckAsync` refuses a settled task at `:136` (`CheckOutcome.AlreadySettled`),
  then runs `DelegateCheckProbe.GatherAsync`, then waits up to `CheckInterpreterWaitSeconds`
  (60 s default) for the specialist. It re-reads the status at `:157-166` **and enqueues anyway**
  with `SupersededBanner(...)` prepended (`:173`).
- **The queue window is closed the same way.** `AgentTaskDispatcher.ReconcileSupersededChecksAsync`
  (`:1133`, wired into the 5 s tick at `:180`) walks still-Pending `Origin = Check` rows with
  `DeliveryAttempts == 0` whose task has settled since enqueue, and **amends the body in place**
  through `SessionMessageQueueService.AmendPendingBodyAsync` (`:297`) — same per-session lock the
  flush uses, so it cannot race a mid-type. Its own doc comment says it: *"Status is unchanged —
  mark, never suppress."*
- **The banner is one function.** `AgentTaskCheckService.SupersededBanner` (`:653`), used by both
  windows; `SupersededMarker` (`:648`) is the idempotency key.
- **Both windows are real and neither dominates.** The four tasks the card names split exactly as
  CARD-0074's 3/4 measurement predicted:

  | Task | Check | Note created | Task settled | Note typed | Window |
  |---|---|---|---|---|---|
  | `5f7ba14d` | #1 | 18:08:30Z | 18:08:21Z | 18:41:16Z | interpreter (settled 9 s **before** enqueue) |
  | `7de41cf1` | #1 | 18:38:42Z | 18:38:35Z | 19:22:36Z | interpreter (settled 7 s before enqueue) |
  | `49e22333` | #2 | 18:12:32Z | 18:32:44Z | 18:41:16Z | queue |
  | `49e22333` | #3 | 18:22:34Z | 18:32:44Z | 18:56:21Z | queue |
  | `f4ba2985` | #4 | 18:47:50Z | 19:06:29Z | 19:22:49Z | queue (16 min stale on arrival) |

- **The queue window is enormous, and that is the real driver.** Across all 65 check notes:
  mean enqueue→typed **783 s**, max **7 478 s** (2 h 4 m). `MessageSendMode.WhenIdle` against a
  busy orchestrator is the whole story — the note waits for a turn boundary that may be an hour off.
- **Overall staleness:** 17/65 typed after their task settled (26%); since CARD-0074 shipped,
  **10/25 (40%)**, of which only 5 carried the banner. The 5 unbannered stale ones are all
  task `1768af90` on 2026-08-20 09:36–10:31Z, typed 11:40–11:47Z in one burst — consistent with
  either a server still running a pre-`7d119ef` build, or a reconnect catch-up flush firing before
  the next 5 s sweep. **Not established, and it does not need to be** — S1.3 closes that shape
  either way.

### Defect 2 — the duplicate

The card's example, `9b347270` (CARD-0103 review), traced end to end:

- **One queue row, two deliveries.** `SessionQueuedMessages` `3d600fb6-…`: `Origin = Delegation`,
  `ConversationKey = task:9b347270…`, `CreatedAt 17:54:42Z`, `DeliveryAttempts = 2`,
  `SentAt 18:14:04Z`. Not two rows, not two settlements — the same row typed twice.
- **One transcript record, at the second attempt.** `TranscriptEntries` for session `cefed08a`
  holds exactly one `UserPrompt` matching the note: seq 8382, `Timestamp 18:13:33Z` — the retry.
- **The first delivery DID land, 15 minutes earlier, in a shape we do not ingest.** In the caller's
  own JSONL:

  ```
  17:58:06.292Z  type=queue-operation  operation=enqueue   content=<the full 3 273-char note>
  17:58:37.774Z  type=user             (tool_result)       uuid 055b8c78
  17:58:37.967Z  type=queue-operation  operation=remove    content=<the same note>
  17:58:06.291Z  type=attachment       attachment.type=queued_command
                                       parentUuid=055b8c78  uuid=adc68faf   <- IN the conversation chain
  17:58:45.644Z  type=assistant        parentUuid=703ee9a5 (…-> adc68faf)
  ```

  The `attachment` record is linked into the conversation between the tool result and the model's
  next response: **the model received the note at 17:58:37Z.** Its own `timestamp` field is the
  *enqueue* time (17:58:06), not its position in the conversation — see §S2.4, the timestamp trap.
- **Why the retry happened.** `TranscriptNormalizer.Normalize` switches on the top-level `type` and
  handles only `assistant` / `user` / `ai-title` / `system` (`:70-77`); `attachment` and
  `queue-operation` fall to `_ => []`. So `TryFindConfirmingRecordAsync`
  (`SessionMessageQueueService.cs:1345`, the **single** query behind all three confirm paths —
  in-window `WaitForTranscriptConfirmAsync:1194`, `GraceConfirmAsync:1276`, and
  `LateConfirmAttemptedMessagesAsync:841`) filters `Kind == TranscriptKinds.UserPrompt` and finds
  nothing, forever. Verdict `NoTranscriptRecord` → `HandleDeliveryFailureAsync:1539` → revert to
  Pending (the always-on kill correctly withheld, the session *was* working) → next flush's
  late-confirm finds nothing again → re-typed.
- **It is not rare and it is not one-off.** Seven completion notes in this machine's Claude history
  landed as `queued_command`. Of the five since CARD-0055's retry loop shipped, **all five** were
  followed by a real `user` record with the same body — five duplicates, five for five. The two
  older ones (2026-08-09, 2026-08-11) predate the retry and were simply never confirmed.
- **Delivery-attempt distribution across the whole queue** (`Origin 3 = Delegation`, `4 = Check`):
  51 of 742 Delegation rows and 2 of 65 Check rows carry `DeliveryAttempts >= 2` — every one of
  those is a candidate re-type. `queued_command` is not the only cause of a failed confirmation,
  but it is the one that is *always* a false negative.

---

## Design

### S1 — never type a check note about a task that has already settled

**One predicate, three call sites.** The predicate is what `RunCheckAsync:157-166` already computes;
it moves into a shared helper so three call sites cannot drift:

```csharp
// AgentTaskCheckService (or a small CheckSupersession static beside it)
internal readonly record struct Supersession(bool Settled, AgentTaskStatus Status, DateTime SettledAt);
internal static Task<Supersession?> EvaluateAsync(AppDbContext db, Guid taskId, CancellationToken ct);
```

**S1.1 — interpreter window (`AgentTaskCheckService.RunCheckAsync`).** Where `:157-166` builds a
banner, it instead returns a new `CheckOutcome.SupersededBeforeDelivery` **without calling
`EnqueueAsync`**. Everything after the enqueue still runs: the `AgentTaskEventType.Check` event
(`:191`) is written exactly as today, with the banner text as its lead line above the digest, and
`AgentTaskChanged` is still published. *The digest is not lost — it stops being typed at a human.*

**S1.2 — queue window (`AgentTaskDispatcher.ReconcileSupersededChecksAsync`).** Where it calls
`AmendPendingBodyAsync`, it calls a new sibling on the queue instead:

```csharp
// SessionMessageQueueService — same lock, same scoped context, same re-check-under-the-lock
// shape as CancelAsync (:252) and AmendPendingBodyAsync (:297).
public Task<bool> CancelPendingIfUntypedAsync(Guid sessionId, Guid messageId, CancellationToken ct);
```

Returns false when the row is gone, no longer Pending, or `DeliveryAttempts != 0` — the same
guards `AmendPendingBodyAsync` already applies, and for the same reason (a row that has been typed
once carries a confirmation baseline). On success it sets `Status = Canceled` + `CanceledAt`, and
the sweep writes an `AgentTaskEventType.Check` event on the task recording the suppression with the
banner text.

> **Status choice, deliberately conservative.** This uses the **existing** `Canceled`, which
> CARD-0082 already uses for "the system decided not to deliver this" (cancel-not-park). It does
> **not** introduce a `Superseded`/`Discarded` status — CARD-0091 owns that question explicitly and
> CARD-0074's plan forbids inventing one here. If CARD-0091 later adds a status, these rows are
> trivially re-labelled; nothing keys on their `Canceled`-ness.

**S1.3 — last look, inside the flush (`SessionMessageQueueService.DeliverNextLockedAsync`).** Between
the deliverable filter (`:704`) and the `Sent` stamp (`:764`), drop any `Origin == Check` row whose
`ConversationKey` parses (`AgentTaskCheckService.TryParseCheckConversationKey`) to a task that is
settled — cancel it in the same `SaveChanges` and continue with the next Pending row. This is the
literal "before-delivery settled-status check" the card asks about, and it is the only point that
holds the per-session lock across the read *and* the write, so it has no race at all. It closes:
the ≤5 s gap between the sweep and a flush; a flush fired by a reconnect catch-up before the first
tick; and a check note that was typed once, failed verification and is about to be typed again into
a caller whose task finished in between (`DeliveryAttempts != 0`, which S1.2 deliberately will not
touch).

**S1.4 — the no-silent-drop guard (recommended; droppable with a one-line justification).**
CARD-0074 §B3 had one real argument for delivering: *"the caller is owed an explanation for a check
it was scheduled to receive and would otherwise silently never get."* That argument only holds if
the completion note actually reaches the caller — and completion notes do park (CARD-0091: 21
parked Delegation rows, all on finished tasks). So suppress **only when** a Delegation-origin row
with `ConversationKey = task:{RootTaskId}` exists for the same parent session in a non-`Canceled`
status; otherwise fall back to today's bannered delivery. In `RunCheckAsync` the settle→enqueue gap
in `AgentTaskReplyService` (status written at `:470`, `DeliverToParentAsync` at `:503`) can make
that row momentarily absent, so poll it for a small bounded grace (`CompletionNoteGraceSeconds`,
default 5 s) before falling back. Falling back is the safe direction and costs one bannered note.

**What S1 does NOT change.** `SupersededMarker`, `SupersededBanner` and the whole banner wording
stay, verbatim and load-bearing — S1.4's fallback still types them, and S1.1/S1.2 still write them
onto the task timeline. The card's *"do not weaken the SUPERSEDED self-labeling"* is honoured: the
label moves off the caller's terminal and onto the record; it is not softened or deleted.

**Tests to invert (they currently pin the behaviour being reversed) —
`tests/Antiphon.Tests/Application/AgentTaskCheckSweepTests.cs`:**

| Test | Becomes |
|---|---|
| `a_task_that_settles_during_interpretation_still_enqueues_a_superseded_note` (`:529`) | `…_enqueues_nothing_and_records_the_supersession_on_the_timeline` |
| `a_pending_check_note_is_amended_once_when_its_task_settles` (`:569`) | `…_is_canceled_when_its_task_settles` |
| `a_check_note_already_typed_once_is_not_amended` (`:605`) | unchanged for S1.2; **add** an S1.3 twin proving the flush cancels it instead of re-typing |
| `a_check_note_whose_task_is_still_working_is_not_amended` (`:625`) | unchanged in intent (`…_is_not_canceled`) |
| `amend_waits_on_the_same_lock_cancel_uses` (`:641`) | retarget to `CancelPendingIfUntypedAsync` |
| `a_banner_on_a_ceiling_sized_note_keeps_the_banner_and_trims_the_tail` (`:683`) | **keep** — S1.4's fallback still builds it |
| `tick_runs_the_superseded_check_reconcile` (`:713`) | keep; assert `Canceled`, not the banner |

**New tests:** the S1.4 fallback (settled with no completion row ⇒ bannered note still delivered);
S1.3 (settled task, Pending Check row, flush types the *next* message and cancels this one);
S1.3 on a `DeliveryAttempts == 1` row; the timeline event carries the digest when nothing was typed.

### S2 — teach delivery confirmation about Claude's own composer queue

**S2.1 — a new transcript kind.** `TranscriptKinds.QueuedUserPrompt` in
`src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:146`, documented as *"a prompt the
TUI accepted into its own composer queue while busy and later submitted; it entered the conversation
as an `attachment`/`queued_command` record, not as a `user` record."*
`TranscriptEntries.Kind` is `varchar(40)` and nothing whitelists it — **no migration.**

**S2.2 — normalize it.** An `"attachment" => FromAttachment(root)` arm in
`TranscriptNormalizer.Normalize` (`:70-77`). Emit exactly one part when
`attachment.type == "queued_command"` and `attachment.prompt` is non-empty; emit nothing for every
other attachment type (`total_tokens_reminder` and friends are pure metadata).
`uuid`/`parentUuid` are present on the record and carry through.

**Do NOT normalize `queue-operation`.** `operation: "enqueue"` fires the instant the TUI accepts the
text — attractive, because it is immediate — but it proves only that the body is *in Claude's
queue*, and `operation: "remove"` is written both when the queue is drained into the conversation
**and** (per its name) when an item is discarded. Confirming on `enqueue` would mark Sent a body
that a `/clear` or a queue-clear then threw away. The `attachment` record is written only on the
path that actually reaches the model, and is the only honest confirmation.

**S2.3 — widen the one query.** `TryFindConfirmingRecordAsync`
(`SessionMessageQueueService.cs:1345`) changes `t.Kind == TranscriptKinds.UserPrompt` to
`t.Kind == UserPrompt || t.Kind == QueuedUserPrompt`. **That single edit fixes all three confirm
paths** (in-window, grace, late) because they all call it. `CaptureTranscriptBaselineAsync` (`:1018`)
takes `MAX(Sequence)` over all kinds already, so the baseline needs no change.
`PromptSubmissionMatch.IsConfirmedBy` / `IsCompleteIn` are untouched — identity and completeness are
about text, and the text is the full prompt.

**S2.4 — the timestamp trap: the new kind must be inert everywhere else.** The `queued_command`
record's own `timestamp` is the **enqueue** time, which is *earlier* than the records that precede
it in file order (measured: `17:58:06.291Z` written after a `17:58:37.774Z` record). The server's
working/idle rule carries a timestamp override precisely because stored sequences are arrival-ordered
(CARD-0008/CARD-0041) — so a record that is late in sequence and early in wall-clock is exactly the
shape that override mis-ranks. Feeding it into working/idle could report **idle while a session is
mid-turn**, which types the next WhenIdle body into a busy composer — i.e. it would *manufacture*
more of the very state this defect arises from.

This is why the plan introduces a **new kind rather than reusing `UserPrompt`**. All three lockstep
working-rule implementations key on `Kind == UserPrompt` and therefore ignore `QueuedUserPrompt`
**by construction** — server `SessionMessageQueueService.IsWorkingAsync:1785`, client
`client/src/features/agents/transcriptModel.ts:25`, runner `TranscriptWorkingState`. So does
`TranscriptPromptSpan` (`:54`, the settlement marker gate and the delivery watchdog's
"did the brief land" test) and `TranscriptCandidateProbe`'s C4 binding evidence. **Nothing changes
except delivery confirmation, and that is the whole point of the slice.** Pin the inertness with
tests rather than leaving it to inference.

**S2.5 — client type.** Add `'QueuedUserPrompt'` to the kind union in `client/src/api/sessions.ts:12`
and render it like a user prompt with a "queued" affordance (`ConversationTimeline` /
`messages/index.ts`). Cosmetic; the transcript view already has to tolerate unknown kinds.

**Deployment note.** The normalizer lives in the **session runner**, so this needs
`pwsh -File scripts/restart-session-runner.ps1` — and per the transcript-format rule in `AGENTS.md`
a runner that predates a transcript-format change refuses launches rather than tailing the wrong
shape. **Not retroactive:** the tailer resumes from its stored offset, so already-skipped
`attachment` lines in live transcripts are not re-read. The 51 existing `DeliveryAttempts >= 2` rows
are history; this stops the next one.

**Tests:**
- `TranscriptNormalizerTests` — a new `Fixtures/queued-command.jsonl` capture (the real
  enqueue/remove/attachment triple from `cefed08a`, trimmed): the `queued_command` attachment
  becomes exactly one `QueuedUserPrompt` part with the full prompt text; `total_tokens_reminder`
  and `queue-operation` records produce nothing.
- `SessionMessageQueueDeliveryVerificationTests` — a body confirmed by a `QueuedUserPrompt` row is
  `Delivered`, not `NoTranscriptRecord`; the **regression that matters**: a message whose first
  attempt produced only a `QueuedUserPrompt` row is late-confirmed on the next flush and is
  **never typed a second time**.
- CARD-0024 completeness still applies to the new kind (a truncated `queued_command` parks, it is
  not promoted to Sent).
- Inertness: a `QueuedUserPrompt` row does not flip `IsWorkingAsync`, is not a turn prompt for
  `TranscriptPromptSpan`, and does not satisfy the marker gate — one test each, server-side, plus
  the client `isWorking` twin.

### Slices

| # | Scope | Files | Independently shippable |
|---|---|---|---|
| **S1** | Suppress a superseded check note before it is typed (3 call sites, 1 predicate, `Canceled` not a new status, the no-silent-drop fallback) | `AgentTaskCheckService.cs`, `AgentTaskDispatcher.cs`, `SessionMessageQueueService.cs`, `AgentTaskCheckSweepTests.cs` | yes |
| **S2** | `queued_command` → `QueuedUserPrompt` → delivery confirmation | `SessionRunnerContracts.cs`, `TranscriptNormalizer.cs`, `SessionMessageQueueService.cs` (one predicate), `client/src/api/sessions.ts`, normalizer + verification tests, new fixture | yes |

No ordering dependency; either can ship first. S1 is server-only (AppHost restart). S2 needs a
**session-runner** restart as well.

---

## The CARD-0074 reversal, stated plainly

CARD-0074 §B2 decided *"Marking, not suppressing"* and §B3 concluded the settled-task case was
*"already the shipped behaviour"* with only the 60 s interpreter window residual. CARD-0132 says the
opposite and is explicit about it. Both are right about their own evidence: CARD-0074 measured
**staleness** (the note describes a moment it does not name) and fixed it with a label; CARD-0132
measures **cost** (a full turn of orchestrator attention per inert note, times 14 concurrent tasks)
and observes that a label a reader still has to read has not saved them anything.

What survives from CARD-0074, unchanged: the `CAPTURED <iso>` stamp on the digest (it is what makes
any of this diagnosable), the banner text verbatim, `TryReadCapturedAt`, the two-window analysis,
and `AmendPendingBodyAsync`'s locking discipline — which `CancelPendingIfUntypedAsync` copies rather
than re-derives.

What CARD-0074 got right that this plan keeps honouring: **the digest is evidence and must not
disappear.** Under S1 it still lands on the task's timeline in full; it just stops being typed into
a human's terminal.

---

## The card's third question, answered

> *"Whether a single before-delivery settled-status check … can suppress delivery entirely for both
> cases, rather than adding two separate fixes."*

**No — and it would be a bug.** S1.3 *is* that single before-delivery settled-status check, and it
is the right fix for defect 1. Applied to defect 2 it inverts: a completion note's task is settled
**by definition**, so the same predicate would suppress every completion note in the system.
Defect 2's question is not "is this stale" but "did the caller already get this", and the answer
lives in the transcript, not the task row.

The two fixes do end up adjacent in `SessionMessageQueueService` — S1.3 in the flush's head
selection, S2.3 in the confirmation query — but they are different guards over different evidence
and should be reviewed as such.

---

## Rejected alternatives

- **Cancel the queued row from `AgentTaskReplyService` at settlement time (event-driven, no sweep).**
  Cheaper, but it only closes the queue window from the settling side and leaves the interpreter
  window and the flush race open, and it puts queue-mutation logic in the settlement path — which
  CARD-0055's live miss showed is exactly where a wrong write becomes a kill. The sweep + flush
  guard already exist and hold the right lock.
- **Reuse `UserPrompt` for `queued_command` rather than a new kind.** Semantically defensible — it
  *is* a user prompt — and it would incidentally fix the marker gate and C4 binding. Rejected for
  this slice: §S2.4's timestamp trap makes working/idle a live regression risk, and the same edit
  would silently change settlement correlation, channel reply dispatch, review reply dispatch,
  context-usage accounting and the delivery watchdog in one go. Do the narrow thing; revisit under
  a card that can measure the wider blast radius.
- **Suppress the check by shortening `CheckInterpreterWaitSeconds` or the WhenIdle window.** Treats
  a 2-hour p-max queue wait as a tuning problem. The wait is correct — the caller is busy.
- **Drop the `[check …]` note ceremony and let the orchestrator poll the board.** Out of scope and
  contrary to the card ("don't remove the check-in mechanism itself").
- **Confirm delivery on the `queue-operation: enqueue` record.** Immediate, but see §S2.2: `remove`
  is ambiguous between "drained into the conversation" and "discarded", so an `enqueue`-based
  confirmation can mark Sent a body that was thrown away.

---

## Deliberately not in scope

- **A new `SessionQueuedMessage` terminal status.** CARD-0091 owns it (its own §"New terminal status
  vs a real delete vs an existing one"). S1.2 uses the existing `Canceled` and keys nothing on it.
- **Sweeping the 21 already-parked Delegation rows.** CARD-0091.
- **A brief that lands as a `queued_command` is invisible to the delivery watchdog.** Real, latent,
  and a *different* severity class: `TranscriptPromptSpan.HasTurnPromptSinceAsync` (`:54`) is what
  `AgentTaskDispatcher.FailNeverStartedAsync` (`:424`) uses to decide "the brief is marked Sent, but
  the session never wrote a turn prompt for this task" — and it cannot see a `QueuedUserPrompt`
  either. Five briefs on this machine appear in JSONL history as `queued_command`; all five tasks
  hit the 10-minute delivery watchdog (2 Failed outright, 3 settled only via CARD-0085 recovery).
  **Causation is NOT established** — the JSONL files those briefs landed in are not those tasks'
  own session ids, which is CARD-0126/CARD-0127/CARD-0085 territory and may be the whole
  explanation. What *is* established is the mechanism: on a correctly-bound session, a queued brief
  would read as never-delivered and the watchdog would fail a live delegate and kill its session.
  **File a card**; do not widen S2 to cover it, because that change has to reason about settlement
  correctness, not just delivery confirmation.
- **CARD-0055's other false negatives.** 51 Delegation rows carry `DeliveryAttempts >= 2`;
  `queued_command` explains 5 of them. The rest (Grok never writing a `/compact` UserPrompt, per
  CARD-0091's debug findings, is one known family) are separate.
- **Batching interaction.** Check notes are deliberately unbatched (`QueuedMessageOrigin.Check`
  doc comment) and completion notes batch on `task:{RootTaskId}` — different keys, so S1 cannot
  strand a batch partner. Nothing to do; noted so a reviewer does not have to re-derive it.

---

## Card housekeeping

- CARD-0132 moves to Ready with this plan linked.
- CARD-0074 should carry a note that its §B2 "mark, never suppress" decision is superseded by
  CARD-0132 §S1, and that its banner and capture stamp survive — otherwise the next reader of
  `ReconcileSupersededChecksAsync` finds a doc comment that contradicts the code.
- New card to file: *"a brief delivered into a busy TUI's composer queue reads as never-delivered to
  the 10-minute watchdog"* (§Deliberately not in scope), citing this plan's §S2 for the record shape.
