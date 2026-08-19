# CARD-0025 — Generalize message spill-to-file beyond delegation: plan

**Date:** 2026-08-19
**Status:** implemented
**Card:** CARD-0025 (`e626c3f4-96ad-475e-bbba-8693d6cc903a`) — only delegation paths spill
oversized bodies to a file; channel and UI messages are still typed whole
**Incident:** task `c7151848` (2026-08-10). Investigation "Still open" #1:
`docs/investigations/2026-08-10-mangled-delegate-report-c7151848.md`. Mechanism:
`docs/investigations/2026-08-11-pty-chunk-loss-root-cause-CARD-0027.md`.
**Precedent:** `AgentTaskDispatcher.FitBriefForTyping` (`AgentTaskDispatcher.cs:1075`) and
`AgentTaskReplyService.FitRefinementForTyping` (`AgentTaskReplyService.cs:348`) — file write
with API-URL fallback, pointer keeps the correlation marker. CARD-0037 made the ceiling
backend-conditional via `PtyDeliveryProfile`. CARD-0024 (`6c2fbc2`, shipped today) detects
a splice after the fact; this card is the prevention for the paths spill does not yet cover.
CARD-0019 Delta 2 named the spawn prompt as the same gap.

This is a planning document only. Do not write the fix in the Plan pass.

## Verdict

**This still needs a code change.** Delegation briefs, refinements, and (excerpted) reports
already spill or shrink *before* they reach the queue. Everything else that
`SessionMessageQueueService` types — UI, Channel, System, Check, Supervision, and a
channel-batch whose concatenated bodies blow the envelope — is handed to `DeliverAsync` whole.
The size tripwire at `SessionMessageQueueService.cs:934` raises
`OversizedTerminalDelivery` and **types anyway**. CARD-0024 will then park a splice as
`Truncated`; it cannot put the lost middle back.

On this deployment (modern ConPTY) a single Telegram message (max 4 096 chars) sits under
`SingleWriteMaxBytes` (86 400 B) and will not clip. The live exposures are: an **inbox
fallback** (ceiling 1 024 B — every long Telegram/UI body), an **uncapped Channel batch**
(`DeliverNextLockedAsync` budget `int.MaxValue` at `:579`), a **huge UI paste**, and the
**card-spawn prompt** (`CardService.BuildPrompt` → `SendBootPromptWithRetryAsync`, which never
enters the queue). Inbox `FitReport` excerpts at 3 000 chars (~3 clip-quanta) also still type.

Do not rewrite `FitBriefForTyping`. Copy its file-write-with-fallback *shape* into a small
origin-agnostic helper and call it at the two edges that actually type non-delegation bodies:
the queue (WhenIdle flush + Now-mode) and the spawn boot prompt. One Code slice.

Spill and CARD-0024's `Truncated` are independent: prevention vs detection. After this ships,
oversize deliveries become short pointers (completeness should pass); `Truncated` still
catches under-ceiling clips. They must not share an incident kind.

## 1. Current shape (verified against the files, 2026-08-19)

### 1.1 What already spills, and what does not

| Path | Gate | Threshold | Reaches `DeliverAsync` as |
|---|---|---|---|
| Delegation brief / refinement | `FitBriefForTyping` / `FitRefinementForTyping` | `BriefInlineMaxBytes` after `ForAgentKind` (inbox 900 B, modern 43 200 B, Grok 0) | pointer, or the brief if under |
| Delegation report | `DelegationReportFormatter.FitReport` | `ReplyInlineMaxChars` (inbox 3 000, modern 14 400) | excerpt, still typed; inbox excerpt can exceed 1 024 B |
| Channel / UI / System / Check / Supervision | none | — | original body |
| Channel batch | size cap `int.MaxValue` (`:579`) | none | concatenated envelope(s) |
| Card-spawn work prompt | none | — | `VerifiedPromptSubmitter`, **not the queue** |

`QueuedMessageOrigin` is not consulted by any spill code. The existing helpers are
delegation-shaped (task id, `.antiphon/task-<id>-brief.md`, `BuildBriefPointer` with the
`[antiphon-task:]` marker and reporting-contract stub). They are not "almost origin-agnostic
and never invoked"; they cannot type a Channel/UI body without inventing a task.

`DeliverAsync` (`SessionMessageQueueService.cs:898`) is the one typer every **queued** origin
shares. Boot/spawn does not go through it (`AgentSessionService.SendBootPromptWithRetryAsync`
→ `adapter.SendPromptAsync` → `VerifiedPromptSubmitter`). A helper used only inside
`DeliverAsync` would miss spawn; a helper used only in the dispatcher would miss Channel/UI.

### 1.2 The tripwire that still sends

```
var bodyBytes = Encoding.UTF8.GetByteCount(trimmed);
if (bodyBytes > ceilings.SingleWriteMaxBytes) {
    LogError(... "Give this path a spill file.");
    await RecordOversizeAsync(...);   // OversizedTerminalDelivery, Warning
}
// then type `trimmed` anyway
```

Tests without a `PtyDeliveryProfile` get inbox ceilings (1 024 B) — documented at
`SessionMessageQueueService.cs:62-70`. That is the number a new spill test should drive
unless it constructs a profile.

### 1.3 Channel matching will break if the typed body changes and the row does not

`ChannelReplyDispatcher.PromptsMatch` (`:742-749`): first 120 chars of the queued row's
`Body`, contained in the turn's `UserPrompt`. Comment at `:242`: "the row's Body IS the
string the bridge enqueued and the queue typed." CARD-0067's durable correlation is
`(Origin=Channel, ConversationKey, Body)`.

If the queue types a pointer and leaves `Body` as the original Telegram envelope, the turn
matches nothing and the reply is owed until TTL → `ChannelReplyLost` (Critical). A batched
turn today matches every constituent by containment of each original `Body` in the composed
paste (`ChannelPromptFormat.FormatBatch`). A single pointer does not contain those probes.

**Required:** after a successful spill, persist the *pointer* as each delivered row's `Body`
(same SaveChanges as the Sent stamp, before typing). ConversationKey is the route; Body is
only the match key. The original text lives in the spill file. No new column.

Now-mode (`POST /api/sessions/{id}/messages` with `Mode=Now`) persists no row; nothing to
rewrite.

### 1.4 CARD-0024 interaction

`DeliverAsync` confirms against the `body` argument it was given. CARD-0024 then requires
`IsCompleteIn(body, recordText)`. If we type a pointer but confirm against the original, every
spill is `Truncated` and parks. Confirm against the pointer. Late-confirm reads stored `Body`,
which is why §1.3's rewrite has to happen before typing.

`OversizedTerminalDelivery` (14, size-before-send) and `TruncatedTerminalDelivery` (25,
identity-without-completeness) stay distinct. A successful spill fires neither.

### 1.5 Spawn is the CARD-0019 leftover, and it is not the queue

`CardService.BuildPrompt` (`:797`) embeds `card.Description` (cap 20 000 chars, documented
exposure on `MaxDescriptionLength` at `:32-36`). `SpawnAsync` (`:613-628`) puts that in
`StartAgentSessionRequest.Prompt`. `AgentSessionService` types it via
`SendBootPromptWithRetryAsync` (`:482`) — before the message queue exists. CARD-0019 Delta 2
(`docs/superpowers/specs/2026-08-11-card-0019-card-correction.md:332-342`) already assigned
this work to CARD-0025: measure UTF-8 bytes against the **brief** ceiling, inline under, spill
over. Arithmetic on modern: ~20-22 KB ASCII fits in 43 200 B; a worst-case multibyte 20k
description is ~80 KB and does not; inbox 900 B spills essentially every card.

## 2. Decisions

### 2.1 New helper, do not generalize `FitBriefForTyping`

New static helper, e.g. `TypedBodySpill` in `server/Application/Services/`. Input: body,
absolute spill path, optional `AgentKind`, optional API-fallback sentence. Output: the string
to type (original if under the caller-supplied byte ceiling, else a pointer). File write
copied from `FitBriefForTyping`: `Directory.CreateDirectory` + `File.WriteAllText`, catch
`IOException`/`UnauthorizedAccessException`, return original on failure so the caller can
keep today's type-anyway + `OversizedTerminalDelivery` behaviour.

Pointer wording copies the brief pointer's contract, without a task marker:

```
YOUR MESSAGE IS NOT IN THIS MESSAGE. It is {N} characters — too long to type
into a terminal without the transport dropping part of it, so it was written out
instead. Read it in full before you do anything else:

    {path}

Everything you need is there. Do not start from this summary.
```

For Channel, prefix the existing envelope line (first line of the original body, or the
newest row's envelope on a batch) so the agent still sees provider/chat/author. For Grok
(`PtyDeliveryCeilings.ComposerJoinsTypedLines`) run the pointer through
`DelegationReportFormatter.FlattenForJoiningComposer` and quote the path — same as
`BuildBriefPointer`. Pin that the pointer's UTF-8 size is well under inbox `SingleWriteMaxBytes`.

Do not merge this with `BuildBriefPointer`. The brief pointer carries `[antiphon-task:]` at
both ends and a reporting-contract stub; those must not appear on a Telegram message.

### 2.2 Queue ceiling = `SingleWriteMaxBytes`. Spawn ceiling = `BriefInlineMaxBytes`.

| Edge | Ceiling | Why |
|---|---|---|
| Queue (every origin, composed body after batching) | `Ceilings.SingleWriteMaxBytes` (inbox 1 024 B, modern 86 400 B) | This is the measured "arrives whole" envelope and the existing tripwire. Spilling here means the tripwire becomes a fallback for write failure, not the steady state. |
| Spawn / boot work prompt | `Ceilings.BriefInlineMaxBytes` (inbox 900 B, modern 43 200 B) | CARD-0019 Delta 2, and a spawn prompt is an instruction. 20k ASCII inline on modern; multibyte or inbox spills. |

Do **not** apply Grok's `BriefInlineMaxBytes = 0` always-spill to Channel/UI. That narrowing is
for structured briefs/refinements (CARD-0084). A short Telegram line joining is not this card.
Grok pointers still flatten when a Channel/UI/spawn body *does* spill.

### 2.3 Call sites: composed-body edge, not per-origin callers

Three production calls, one helper:

1. `DeliverNextLockedAsync` (`:593-616`) — after `body` is composed (single or
   `FormatBatch`), before the Sent stamp. If spilled, set **every** row in `run` to
   `Body = pointer` in the same `SaveChanges` that stamps Sent (`:603-611`). Then
   `DeliverAsync(sessionId, pointer, ...)`. Confirmation, late-confirm, and
   `PromptsMatch` all see the pointer. A batch of N Channel rows all match the same
   pointer turn and all settle — that is today's batch semantics.
2. `EnqueueAsync` Now-mode (`:99`) — spill, then `DeliverAsync`. No row to rewrite.
3. `SendBootPromptWithRetryAsync` — spill against the brief ceiling into
   `{session.Cwd}/.antiphon/inbox/spawn-{sessionId:N8}.md` (overwrite on relaunch is
   correct: the new prompt replaces the old). Keep `RunAttempt.Prompt` as the full
   original (that is the work record). Type the pointer. C4 / `SessionInputLog` bind
   on what was actually written, so they see the pointer; that is enough
   (`MinMatchChars = 12`). File-write failure: pointer names
   `GET /api/cards/{identifier}` (description is on the card), matching the brief
   helper's API fallback. If even that cannot be composed, type original.

`DeliverAsync` itself does **not** spill. It keeps the tripwire as the backstop for a
caller that still hands it an oversized body (write failure, or a future typer). A
successful spill must not raise `OversizedTerminalDelivery`.

Do not spill inside `ChannelBridgeService.FlushLaneAsync` or the UI endpoint. Those
would miss batches (composed later) and Now-mode respectively, and would re-create
the per-caller hole this card exists to close.

### 2.4 Spill file location

`{session.Cwd}/.antiphon/inbox/{id}.md`. Same directory `ChannelBridgeService.ResolveInboxDirAsync`
already uses for inbound attachments, so a bound agent already has the folder. `{id}` is the
queued message Guid (head of the run), or `now-{yyyyMMddHHmmss}` for Now-mode. Pointer quotes
the **cwd-relative** path `.antiphon/inbox/{id}.md` so the agent's Read tool does not depend
on a Windows absolute path.

Empty/missing `Cwd`: cannot write → type original + existing oversize incident.

### 2.5 What not to do

- No new `SessionQueuedMessage` column. Original text is the file; Body becomes the match key.
- No DELETE, no Board counter, no change to `FitReport` excerpt arithmetic (queue spill
  covers an inbox excerpt that is still over 1 024 B).
- No raising of any ceiling. Spill does not retire CARD-0037's numbers.
- No generic spill inside `VerifiedPromptSubmitter` — that class does not know cwd or
  origin. Spawn stays a caller of the helper.
- Do not delete the oversize tripwire.

## 3. One Code slice

New helper + three call sites + tests. Suggested commit subject:
`fix(queue): CARD-0025 - spill oversized channel/UI/spawn bodies instead of typing them`.

### 3.1 Helper tests (no pty)

- Under the ceiling → original returned, no file.
- Over the ceiling → file contains the original, returned pointer is under inbox
  `SingleWriteMaxBytes`, contains the relative path, contains "YOUR MESSAGE IS NOT IN THIS
  MESSAGE".
- Write throws → original returned (caller keeps type-anyway).
- Grok kind → pointer is a single line, path quoted, no adjacent-token smash.
- Channel envelope prefix survives on the pointer.

### 3.2 Queue tests (`BridgeQueueHarness`, inbox ceilings by default)

Drive a body of ~2 000 bytes (over 1 024 B, under modern 86 400 B so a mistakenly-modern
profile would still fail the assertion).

- UI WhenIdle oversize → file at `{cwd}/.antiphon/inbox/{id}.md` holds the original; adapter
  submitted the pointer; row `Body` is the pointer; `Sent`; no `OversizedTerminalDelivery`;
  no `TruncatedTerminalDelivery`.
- Under-ceiling UI body unchanged (existing `A_complete_long_body_still_marks_sent` is ~850
  chars — keep it).
- Channel origin oversize → after delivery, `PromptsMatch(Normalize(row.Body), typedPointer)`
  is true. Add a `ChannelReplyDispatcher` case: a spilled Channel row still settles a reply
  on the pointer turn (ConversationKey route unchanged).
- Two Channel rows batched over the ceiling → one file of the composed batch, both rows'
  `Body` set to the same pointer, both would match one turn.
- File-write failure (read-only inbox dir, or helper stub) → original typed,
  `OversizedTerminalDelivery` still fires. That is the documented fallback.
- Now-mode oversize → pointer typed, no queue row, file named with the timestamp form.

Existing CARD-0024 clip tests use `LongQueuedBody()` (~850 chars) and must stay red-on-clip /
green-on-complete. They are under the inbox tripwire; spill must not swallow them.

### 3.3 Spawn tests

- `BuildPrompt` with a description whose UTF-8 size exceeds inbox `BriefInlineMaxBytes` and
  is under 20 000 chars → `SendBootPromptWithRetryAsync` types a pointer; spill file holds
  the full prompt; `RunAttempt.Prompt` still has the original.
- Same description against modern ceilings (~20 KB ASCII) → typed inline, no file. Pin both
  backends the way `PtyDeliveryCeilingsTests.The_same_brief_spills_on_the_inbox_backend_and_is_typed_inline_on_the_modern_one`
  already does for briefs.
- No pty-host integration required for spawn if the helper is unit-tested and the call site
  is asserted with the fake adapter (same style as `AgentSessionLaunchFailureTests`).

### 3.4 Optional pty pin (same lane as `DelegationBriefCeilingPtyTests`)

One `SessionMessageQueuePtyIntegrationTests` case with `ANTIPHON_FAKE_STDIN_CLIP=1`: a 2 KB
UI body without spill would lose a chunk; with spill the child receives the pointer whole and
the file is on disk. Skip if the slice is already covered by the harness tests above; do not
block on headed Claude.

## 4. Out of scope

- Merging `FitBriefForTyping` / `FitRefinementForTyping` onto the new helper (different
  pointer, different directory, task API fallback). A later tidy, not this card.
- Always-spill Grok Channel/UI (CARD-0084's composer-join rule stays brief/refinement-only).
- Retiring spill-and-pointer on modern (ADR 0002 / CARD-0037 step 4 leftover).
- Changing Telegram/UI input limits, `MaxDescriptionLength`, or `FitReport`.
- Schema / new incident kind.
- `VerifiedPromptSubmitter` completeness (CARD-0024 already left boot out of scope).

## 5. Close-out after the slice is green

- Mark this plan implemented.
- Close CARD-0025. Reason: queue + spawn now spill over the backend-resolved envelope;
  `OversizedTerminalDelivery` remains only for write-failure fallback; CARD-0024 still
  detects under-ceiling clips.
- Drop the "Known exposure" paragraph on `CardService.MaxDescriptionLength` (`:32-36`) or
  retarget it at the helper.
- Strike investigation "Still open" #1
  (`docs/investigations/2026-08-10-mangled-delegate-report-c7151848.md:156-159`).
- CARD-0019 Delta 2's spawn-path claim is settled by the same slice; no second card.
