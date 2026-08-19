# CARD-0068 — Same-batch channel-reply drop: plan

**Date:** 2026-08-19
**Status:** planned (not implemented)
**Card:** CARD-0068 (`4867147c-eacc-420a-8e78-bf4105236e5e`) — a channel reply is dropped when a
newer prompt lands in the same persistence batch as the text it concerns; an interim line wins
over the real answer
**Incident:** Family agent, 2026-08-17, task `faf7dda2` / CARD-0067 investigation. Transcript seq
**469** (1 849-character guest list, the genuine answer to prompt 406) was discarded because seq
471 (a newer `UserPrompt`) shared its `CreatedAt` `09:05:49.152571`.
**Precedent:** `ExtractTurnResponseAsync` already attributes assistant text to a prompt by
**sequence window** (after this prompt, before the next). `DispatchFollowUpAsync` must use that
same window. CARD-0067's `DispatchedTurn.PromptSeq` + remembered targets are the correlation;
do not re-open `ChannelReplySettledAt`.

This is a planning document only. Do not write the fix in the Plan pass.

## Verdict

**This is not a race a delay would fix.** After the batch commits, the state is always "trailing
text present AND a newer prompt present." There is no later moment at which the check could see
the text without the prompt. Waiting cannot invent a window that the persist made atomic.

The sequences in that batch are also not the bug: they are assigned in snapshot/file order
(`PersistTranscriptAsync`, `AgentSessionRuntime.cs:544-545`), so 469 < 471 is causally correct.
The defect is the **predicate**. `DispatchFollowUpAsync`
(`ChannelReplyDispatcher.cs:612-618`) asks "is `turn.PromptSeq` still the latest `UserPrompt` in
the whole session?" and, if not, drops the watermark and sends nothing. The question it must ask
is the one `ExtractTurnResponseAsync` (`:692-703`) already asks: "is there unsent
`AssistantText` that still belongs to this prompt's window (`PromptSeq < seq < nextPromptSeq`)?"

Text 469 sits in that window. The newer prompt 471 is the window's upper bound, not a reason to
throw the text away.

One Code slice.

## 1. Current shape (verified against the files, 2026-08-19)

### 1.1 The measured case

From the card, matching the code comment at `ChannelReplyDispatcher.cs:598-601` (the card's
`:295-299` citation has drifted; the bail is now `:612-618`):

1. TurnEnd 468 fires `DispatchAsync` while only seq 465 exists — *"Transcribed. Let me save it
   properly first."* Non-empty, so it settles prompt 406's correlation on that interim line and
   writes `_dispatched[session] = DispatchedTurn(PromptSeq: 406, MaxTextSeq: 465, targets)`.
2. Seq 469 (real answer), 470, and 471 (newer `UserPrompt`) persist in **one**
   `PersistTranscriptAsync` `SaveChanges`, identical `CreatedAt`. That is the catch-up / sync
   path (`AgentSessionRuntime.SyncTranscriptAsync` `:450-458`), not the live
   `ObserveTranscriptAsync` path which persists one entry at a time (`:204`) and would have
   dispatched 469 before 471 existed.
3. `DispatchFollowUpAsync` reads `latestPromptSeq = 471 != 406` and `TryRemove`s the record.

CARD-0067's durable correlations would have delivered a *later rewrite* of the list (09:08:49Z)
because that later turn still had an owed row. The user-visible "silence" is therefore gone.
This path is still wrong: every time an agent thinks out loud, the first non-empty fragment is
what the chat gets, and a same-batch (or otherwise already-present) next prompt permanently
suppresses the rest.

### 1.2 Why the live one-at-a-time path is not enough

`ObserveTranscriptAsync` (`:219-220`) re-triggers channel dispatch on every `AssistantText`.
That is the AZ Care 2026-07-29 follow-up path, and
`ChannelBridgeTests.Text_arriving_after_an_interim_reply_was_sent_still_reaches_the_chat`
pins it. It only works when follow-up **runs in the gap** between the trailing text landing and
the next prompt landing.

`SyncTranscriptAsync` (reconnect, server restart, stream-gap catch-up) persists the unseen
tail as one batch, then fires **one** `OnTurnEndAsync`. The gap does not exist. Same-batch is
the production shape of that missing gap, not a timing glitch around it.

### 1.3 The existing negative is the right rule, pointed at the wrong rows

`ChannelBridgeTests.Follow_up_stops_once_the_next_turn_starts` inserts the **next**
`UserPrompt` *then* assistant text, then dispatches. That text is after the new prompt, so it
must not go out as a follow-up. The current equality bail happens to pass that test, because it
drops *everything* the moment any newer prompt exists — including text that is still *before*
the new prompt.

`ExtractTurnResponseAsync` already implements the correct split:

```
nextPromptSeq = min(UserPrompt.Sequence > promptSeq)   // null if none
AssistantText where Sequence > promptSeq
                  and (no cap or Sequence < nextPromptSeq)
```

Follow-up needs the same cap, with the lower bound `turn.MaxTextSeq` instead of `promptSeq`
(the prefix of the window was already sent).

### 1.4 CARD-0067 already has the correlation; do not rebuild it

| Building block | What it is | Role in this fix |
|---|---|---|
| `SessionQueuedMessage.Body` / `ConversationKey` | Durable inbound half; `DispatchAsync` matches the turn's `UserPrompt` by containment | Already consumed by the time follow-up runs. Do not re-match. |
| `ChannelReplySettledAt` | Consume marker so a restart cannot re-answer | **Leave settled.** Re-opening is CARD-0067's duplicate-into-a-family-chat hazard. |
| `DispatchedTurn` (`PromptSeq`, `MaxTextSeq`, `Targets`) | In-memory follow-up watermark, written when the first fragment is sent (`:311`) | This *is* the specific-prompt correlation the brief asked about. Keep it. Attribute trailing text to `PromptSeq`'s window; send to the remembered `Targets`. |

`ReviewReplyDispatcher` has no follow-up path. Out of scope.

## 2. Decisions (the card's three questions)

### 2.1 Do not compare timestamps. Replace the predicate.

- `CreatedAt` is the **batch stamp** (`PersistTranscriptAsync` `:521` / `:572` — one `now` for
  every row in the foreach). In the measured case every row has the same `CreatedAt`, so it
  cannot order 469 against 471.
- `Timestamp` is the record's own time and is what CARD-0056 uses when **backfill reorders
  sequences**. This is not that: stored sequences in a live or catch-up batch are file order,
  which is causal order. 469 < 471 is already right.
- Main-path extraction is sequence-windowed. Follow-up must use the same definition of "this
  turn." A Timestamp-only follow-up would split the turn-window in two places, and `Timestamp`
  is nullable (the harness often leaves it null).

### 2.2 Do not skip "obvious interim lines" in `DispatchAsync`.

The first-non-empty-text rule is how a stop-marker-before-text turn still replies (2026-07-24)
and how a mid-stream stop still sends *something* (2026-07-29). Heuristics on "Transcribed. Let
me save it properly first." vs a short real answer are unknowable. The follow-up path is the
designed correction; this card makes that correction survive a next prompt that is already in
the table.

### 2.3 Do not re-open a settled correlation.

Settling before produce is what makes CARD-0067 restart-safe in the other direction. Follow-up
does not need the row: it has the targets. A better fragment of the **same** turn is another
produce to those targets, not a new consume of the inbound half.

Making `_dispatched` itself durable is **out of scope**. CARD-0067 left it process-memory on
purpose (comment `:593-596`): a restart currently costs a trailing fragment. In the measured
case that fragment *was* the answer, which is why this card exists for the live/sync path; a
durable watermark is a separate design (it has to define "already sent" for text a dead process
may or may not have produced) and is not required to fix same-batch.

## 3. The fix — one method, the window `ExtractTurnResponseAsync` already has

`ChannelReplyDispatcher.DispatchFollowUpAsync` (`:602-674`). Replace the `latestPromptSeq !=
turn.PromptSeq` bail (`:612-618`) with:

```
nextPromptSeq = min(UserPrompt.Sequence > turn.PromptSeq)     // null if none
late = AssistantText where Sequence > turn.MaxTextSeq
                       and (no nextPromptSeq or Sequence < nextPromptSeq)
```

Then the existing CARD-0071 stub withhold, `TryUpdate` claim, produce, and watermark advance.

After the window is drained:

- **`nextPromptSeq` is set** — no future row can land in a sequence gap that already has a
  later prompt. Drop the `_dispatched` record (today's `TryRemove`, but *after* sending
  in-window text, not before looking).
- **`nextPromptSeq` is null** — keep the advanced watermark so a later fragment of the same
  turn still follow-ups. This is today's `TryUpdate` path, unchanged.

A shared private helper with `ExtractTurnResponseAsync` is welcome if both call sites keep one
definition of the window; do not change `ExtractTurnResponseAsync`'s contract or
`DispatchAsync`'s first-non-empty behaviour.

Delete the "NOT FIXED HERE, still open" comment (`:598-601`) as part of the same edit. Update
the CARD-0067 gotcha in `CLAUDE.md` so the "Still open: `latestPromptSeq` bail" sentence
records this card as the close, not as outstanding work.

## 4. Tests — pin the window, not the batch stamp

Existing coverage to keep green (these are the negatives and the already-correct arms):

| Test | File | What it must still mean |
|---|---|---|
| `Follow_up_stops_once_the_next_turn_starts` | `ChannelBridgeTests.cs:159` | Text **after** the next prompt is not a follow-up |
| `Text_arriving_after_an_interim_reply_was_sent_still_reaches_the_chat` | `ChannelBridgeTests.cs:126` | Trailing text with **no** newer prompt still follow-ups |
| `A_stub_in_the_trailing_window_withholds_the_follow_up` | `ChannelReplyDurabilityTests.cs:277` | CARD-0071 withhold still applies to in-window trailing text |
| `A_restarted_process_does_not_answer_an_already_answered_turn_twice` | `ChannelReplyDurabilityTests.cs:94` | Settled stays settled |

New test, same file as the other follow-up pins (`ChannelBridgeTests.cs`):

**`Trailing_text_still_follow_ups_when_the_next_prompt_landed_in_the_same_batch`**

Reproduce the measured shape, not a delay:

1. Bind, inbound, insert `UserPrompt` + interim `AssistantText` + `TurnEnd`. `OnTurnEndAsync`.
   Assert one reply (the interim) and pending count 0.
2. Persist, in **one** `SaveChanges` with **identical** `CreatedAt` and consecutive sequences:
   `AssistantText` = the real answer, then `UserPrompt` = a newer prompt (terminal / next
   queued body — anything that does not match the settled correlation). This is
   `PersistTranscriptAsync`'s batch, not two harness inserts with a dispatch in between.
3. `OnTurnEndAsync` once.
4. Assert two replies; the second body is the real answer; conversation id unchanged.
5. (Same test or a one-liner sibling.) A third `AssistantText` *after* the newer prompt must
   not produce a third reply — `Follow_up_stops_once_the_next_turn_starts` still holds on this
   path.

The test is **red on current master**: step 3 hits `:615` and sends nothing. Confirm that by
stashing / running at the base commit before treating a later red as this slice.

A small harness helper (`InsertTranscriptEntriesInOneBatchAsync` on `BridgeQueueHarness`,
mirroring `PersistTranscriptAsync`'s one-`now` / one-`SaveChanges`) is in scope if it keeps
the test from inventing a private `DbContext` dance. Do not change `InsertEntryAsync`'s
per-row save; other tests rely on one-at-a-time sequences.

Do not add a delay, a retry, or a `CreatedAt` inequality. The assertion is on **sequence
window membership**.

## 5. Out of scope

- Re-opening `ChannelReplySettledAt` or adding a second consume column.
- Durable `_dispatched` / surviving follow-up across process restart.
- Interim-line heuristics in `DispatchAsync`.
- Timestamp-vs-sequence in the working rule, `IsWorkingAsync`, or `ExtractTurnResponseAsync`.
- `ReviewReplyDispatcher` (no follow-up path; still the in-memory `Track` map).
- `PersistTranscriptAsync` batching itself — the batch is correct; the reader is not.
- CARD-0071 withhold policy (keep; apply it to the in-window rows only, which the new query
  does naturally: a stub *after* the next prompt is the next turn's problem).
- Closing the card. This plan lands; a Code slice implements.

## 6. Slice

One Code slice, tests first:

1. New `ChannelBridgeTests` case (expect red at HEAD).
2. `DispatchFollowUpAsync` window query; drop the equality bail; keep stub withhold + claim.
3. CLAUDE.md CARD-0067 gotcha: the "Still open" sentence becomes this card's close.

Verify (alternate `OutputPath=bin-card0068/`, forward slash; delete the `bin-card0068`
directories afterwards):

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0068/ -- --treenode-filter "/*/*/ChannelBridgeTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0068/ -- --treenode-filter "/*/*/ChannelReplyDurabilityTests/*"
```

Stash and re-run the new test at the base commit before blaming the slice for a red that was
already there. Do not widen a timeout or loosen `Follow_up_stops_once_the_next_turn_starts`.
