# CARD-0019: Card correction — edit with history, archive, and the 4000-char ceiling

**Status:** planned, not implemented. **Card:** CARD-0019 (priority 0, `bug/api/cards/blocker`).
**Date:** 2026-08-11.

## Problem

Cards are the durable record of all outstanding work (decided 2026-08-09), but a card's text is
write-once: `CardEndpoints.cs` exposes move/spawn/diff/comments/pr and nothing else — no
title/description update, no delete. The moment a card records something wrong, it stays wrong
forever. This is not hypothetical:

- **CARD-0018** claims `POST /api/agent-tasks/{id}/retry` beats the cold-launch prompt race. That
  is false (attempt 2 of task a6e163fe spawned a new session and lost its prompt the same way),
  and the correction currently lives awkwardly in CARD-0019's own body because CARD-0018 cannot
  be edited.
- **CARD-0026** ends with a note calling
  `CodexAdapterLocalShellTests.Question_detection_ignores_question_mark_in_prompt_echo`
  "load-flaky, a separate and lesser problem". Since then the failure was root-caused as
  checkout-path dependent (prompt echo wrapping at certain terminal widths/paths) and fixed
  outright in `f078dd2` ("strip the prompt echo even when the terminal wraps it"). The note is
  now known-wrong and uncorrectable.

A second, adjacent defect: `Card.Description` is `HasMaxLength(4000)`
(`AppDbContext.cs:622`) and `CardService.ValidateCreateRequest` never checks length, so a
description over 4000 chars sails past validation into Postgres, which rejects the
`varchar(4000)` overflow; the `DbUpdateException` is not an `HttpException`, so
`ExceptionMiddleware` returns **500**. Long descriptions are the house style ("the detail is the
point"), so this ceiling is routinely within reach — and any correction mechanism that appends
context makes descriptions *grow*.

## How correction coexists with write-once (the headline decision)

The write-once convention is not overturned; it is **restated at the right layer**. Its purpose
was never "text can never change" — it was "the record can never be silently lost or quietly
rewritten". So:

> **The card *surface* is correctable; the card *record* is append-only.**
> Every edit supersedes the visible text but archives the prior text as an immutable revision,
> with a mandatory reason. Nothing is ever destroyed; hard delete does not exist.

Concretely:

1. **Edits require a `reason`** (non-empty, server-enforced). A correction says *why* the record
   changed, the same way CARD-0019 today carries the correction of CARD-0018 with its evidence.
2. **Every edit writes a `CardRevision` row first** — a snapshot of the superseded
   title/description/priority/labels plus `reason`, `editedBy`, timestamp. The full history is
   readable via the API. The old text is one GET away, forever.
3. **Delete is archive.** `DELETE /api/cards/{id}` sets `ArchivedAt` + `ArchivedReason`; the row
   stays. This also sidesteps the CARD-0005 trap: `NextIdentifierAsync` is count-based, so a hard
   delete would silently reuse the freed identifier; archived rows keep the count monotonic.
4. After this ships, the convention text (operator memory `project-cards-are-the-record`, and any
   doc that repeats "cards are write-once") is updated to: *"cards are append-only: correct the
   surface freely, with a reason; history is immutable and complete."*

Rejected alternatives, for the record:

- **Append-only comments/annotations instead of edits** — leaves the wrong text as the thing
  every reader (and every agent prompt: `CardService.BuildPrompt` injects `card.Description`
  verbatim into spawned sessions) sees first. A correction that trails below the falsehood does
  not stop the falsehood being dispatched to agents as work instructions.
- **Full event-sourcing of cards** — right shape, wrong cost; the revisions table gives the same
  auditability for two entities and one migration.
- **Edit without history** — quietly overturns the convention; explicitly out.

## API design

### 1. `PATCH /api/cards/{id}/content` — the correction endpoint

New endpoint (deliberately *not* overloading `PATCH /cards/{id}`, which is move-only with
`MoveCardRequest` and optimistic-concurrency semantics tied to column moves; mixing "move" and
"rewrite" in one verb invites partial-intent bugs).

```csharp
public sealed record UpdateCardContentRequest(
    Guid ConcurrencyToken,          // required, same 409 pattern as MoveAsync
    string Reason,                  // required, non-empty — stored on the revision
    string? Title = null,           // null = unchanged; provided = validated + replaced
    string? Description = null,
    int? Priority = null,
    IReadOnlyList<string>? Labels = null,
    string? EditedBy = null);       // free text (agent name / "operator"); no auth exists yet
```

Semantics:

- Null fields untouched; at least one non-null content field required (else 400).
- `ConcurrencyToken` mismatch → `ConflictException` (409), exactly `MoveAsync`'s pattern
  (`CardService.cs:89-92`); token rotates on success; `UpdatedAt` bumped.
- Before applying changes: insert a `CardRevision` snapshot of the *current* values (see below).
- Validation mirrors create: title non-blank if provided (≤300, the existing column), priority
  ≥ 0, description within the ceiling (§ 4000-char fix) — all as `ValidationException` → 400.
- Publish `CardChanged` (same event the move/create paths publish) so boards live-update.
- Editing is allowed in any column, including terminal ones — correcting the record of *done*
  work is precisely the CARD-0026 case.

### 2. `CardRevision` entity + `GET /api/cards/{id}/revisions`

```csharp
public class CardRevision
{
    public Guid Id { get; set; }
    public Guid CardId { get; set; }            // FK, cascade with card (cards are never hard-deleted)
    public int RevisionNumber { get; set; }     // 1..n per card, assigned server-side
    public string Title { get; set; }           // the SUPERSEDED values
    public string Description { get; set; }
    public int Priority { get; set; }
    public string LabelsJson { get; set; }
    public string Reason { get; set; }          // why the edit happened
    public string? EditedBy { get; set; }
    public DateTime CreatedAt { get; set; }     // when the edit superseded these values
}
```

Storing the *superseded* snapshot (not the new values) means revision n + current card =
complete history with no duplication, and revision 0 never needs backfilling for the thousands
of existing never-edited cards. `GET /api/cards/{id}/revisions` returns them newest-first.
Config: `Title` max 300, `Reason` max 1000 required, `LabelsJson` jsonb, `Description` same type
as the card's (see below). Index on `(CardId, RevisionNumber)` unique.

`CardDto` gains `RevisionCount` (int) so the UI can show an "edited" affordance without a second
query; `BoardService.ToCardDto` picks it up via a counted include or a windowed subquery —
implementer's choice, but keep the board GET single-query.

### 3. `DELETE /api/cards/{id}` — archive, not delete

- Adds `ArchivedAt`/`ArchivedReason` (+ `ArchivedBy`) to `Card`. Request body carries
  `ConcurrencyToken` + required `Reason`.
- Board detail GET excludes archived cards by default; `?includeArchived=true` shows them.
  Use an explicit `Where(c => c.ArchivedAt == null)` in `BoardService`, **not** a global EF
  query filter — `NextIdentifierAsync` counts `_db.Cards` and a global filter would silently
  shrink the count and resurrect the CARD-0005 identifier-reuse bug for every card ever archived.
  A test pins this (below).
- Refuse archive (409) while the card has a live owner session or an active workflow run —
  archiving the record out from under a working agent is the one genuinely destructive shape here.
- Unarchive: `POST /api/cards/{id}/unarchive` (trivial, same token/reason contract). Cheap to
  include; mistakes in archiving need correcting too, and the whole card is about correctability.

### The 4000-char 500: fixed here, in two parts (not carded separately)

Decision: **fix it inside this card.** Rationale: the update endpoint ships new write paths that
would otherwise ship with the same landmine; the fix shares this card's migration and touches the
same validation methods; and "the correction mechanism must not assume unlimited description
length" is a contract of *this* feature. A separate card would just be a second trip through the
same files.

Two parts:

1. **Never 500 on length (the bug):** length checks in `ValidateCreateRequest` *and* the new
   update validation → `ValidationException` → 400 with a message naming the limit and the actual
   length. Applies to title (300), description (ceiling below), reason (1000).
2. **Move the ceiling to where it belongs (the policy):** migrate `Cards.Description` (and
   `CardRevisions.Description`) from `varchar(4000)` to `text`, with an application-level cap of
   **20,000 characters** enforced only by the validation above. Postgres `text` costs nothing;
   the cap becomes a single constant (`CardService.MaxDescriptionLength`) instead of a schema
   fact, so raising it later is a one-line change with no migration. 4,000 was already too small
   for the house style (CARD-0019's own body is ~1,900 chars *before* any correction is appended);
   20,000 is comfortable but still small enough to keep `BuildPrompt` injection and board GET
   payloads sane.

The API surface must document the cap (constant surfaced in the 400 message), because callers
composing corrections programmatically — exactly the delegation flows that produced this card —
need a deterministic pre-check.

## Server changes (slice 1)

| File | Change |
|---|---|
| `server/Domain/Entities/Card.cs` | + `ArchivedAt`, `ArchivedReason`, `ArchivedBy`, `Revisions` nav |
| `server/Domain/Entities/CardRevision.cs` | new |
| `server/Infrastructure/Data/AppDbContext.cs` | CardRevision config; drop `HasMaxLength(4000)` on Description |
| `server/Migrations/…_AddCardRevisionsAndArchive.cs` | one migration: revisions table, archive columns, description → text |
| `server/Application/Dtos/BoardDtos.cs` | `UpdateCardContentRequest`, `ArchiveCardRequest`, `CardRevisionDto`; `CardDto.RevisionCount` |
| `server/Application/Services/CardService.cs` | `UpdateContentAsync`, `ArchiveAsync`, `UnarchiveAsync`, `GetRevisionsAsync`; shared length validation used by create + update |
| `server/Application/Services/BoardService.cs` | exclude archived from board detail; map `RevisionCount` |
| `server/Api/Endpoints/CardEndpoints.cs` | map the four routes |

## Client changes (slice 2)

- `client/src/api/boards.ts`: `UpdateCardContentRequest`/`CardRevisionDto` types,
  `useUpdateCardContent(boardId)`, `useArchiveCard(boardId)`, `useCardRevisions(cardId)` —
  invalidation mirrors `useMoveCard`.
- `CardModal.tsx`: an Edit action (pencil next to the title block) opening a dialog prefilled
  with title/description/priority/labels plus the required **Reason** field; a client-side
  character counter against the 20k cap. A "History" tab (or section under Details) listing
  revisions with reason/author/time and the superseded text. Archive behind the modal's
  overflow, with reason prompt and confirm.
- Description length guard on the *create* dialog too (`BoardPage.tsx` create card path) — same
  constant, exported from one place.

## Tests (slice 1/2, following house rules: scope every assertion to rows the test created)

Integration (`Antiphon.Tests`):
- Update happy path: fields changed, token rotated, `UpdatedAt` bumped, `CardChanged` published,
  revision row holds the *old* values with the reason.
- Token mismatch → 409; empty/missing reason → 400; all-null content fields → 400.
- Description of 20,001 chars → **400** (not 500) on create *and* update; 19,999 → succeeds
  (pins the varchar→text migration actually ran).
- Sequential edits: revision numbers 1,2,3; `GET /revisions` newest-first; `RevisionCount` on DTO.
- Archive: hidden from board detail, visible with `includeArchived`, 409 while owner session
  live; unarchive restores.
- **Identifier guard:** create card A, archive it, create card B ⇒ B's identifier ≠ A's
  (pins "no global query filter" against CARD-0005 regression).
- Edit allowed on a card in a terminal column.

Client: `CardModal` edit dialog submits with reason and renders history; mutation hook
invalidations.

## Correct the record (slice 3 — the acceptance test in the real sense)

Using the shipped endpoint, on the live board:
1. **CARD-0026** — replace the final paragraph: the `Question_detection…` failure was *not*
   load-flaky; it was checkout-path dependent (prompt echo wrapped under long worktree paths) and
   was fixed outright by `f078dd2` → reason: "correcting a diagnosis disproven by the CARD-0027
   investigation; fix landed in f078dd2".
2. **CARD-0018** — replace the false retry-workaround claim with the finding already recorded in
   CARD-0019's body; reason cites task a6e163fe attempt 2.
3. Update operator memory `project-cards-are-the-record` and any doc stating "cards are
   write-once" to the append-only restatement (§ above), including the new endpoints.

CARD-0019 itself then moves to done with `TerminalReason` pointing at this spec.

## Out of scope

- **CARD-0005** (count-based identifiers) — this plan only *guards* against making it worse.
- Hard delete — deliberately does not exist.
- Auth/actor identity — `EditedBy` is honest free text until the server has principals.
- Card *comments as annotations* (the `/comments` route stays session-messaging; renaming it is
  a separate cleanup).

---

## Caller decisions (2026-08-11, orchestrator)

Both open questions are answered; one carries a condition.

### 1. The 20,000-character cap — accepted, but NOT for the spawn path

Accepted as a storage and API policy. `text` plus an application constant is the right shape, and
4,000 is demonstrably too small for the house style.

**The condition:** raising it must not widen an existing live defect. `CardService.BuildPrompt`
embeds `card.Description` verbatim, and that prompt reaches the session via
`adapter.SendPromptAsync` — it is TYPED INTO THE PTY. Per CARD-0027 the receiving TUI keeps one
~1024-byte read chunk per event-loop turn and silently discards the rest, so a description over
roughly 1 KB is already at risk of reaching a spawned agent clipped, with no error and no sign on
either side. The agent then works from a mangled instruction that reads as complete.

This is not introduced by this plan — it is broken at the current 4,000 ceiling too — but going to
20,000 makes the exposure five times larger. So:

> Ship the cap only together with spill-to-pointer on the spawn prompt, the same treatment briefs
> got in `8c42ebd`/`21743a5`: over `BriefInlineMaxBytes` (UTF-8 BYTES, not chars), write the prompt
> to a file and type a short pointer.

If that is not in this card's scope, the cap stays at 4,000 until the spawn path is fixed, and the
spawn-prompt clipping is carded separately. Either is fine; shipping 20,000 over a typed prompt is
not.

### 2. `EditedBy` as free text — accepted

Correct call while the server has no principals. An honest unauthenticated string beats inventing
an identity model this card does not need. It must never be presented as an authenticated actor in
the UI or API docs — label it as self-reported.

### Also worth doing here

Slice 3 corrects CARD-0026 and CARD-0018. Add a third: **CARD-0026's note is wrong in a specific
way that should be recorded accurately** — the test was checkout-path dependent (the cmd prompt
prefix is the cwd, so the echoed prompt wrapped at 129 columns from a worktree and 110 from
`C:\src\Antiphon`), not load-flaky, and `f078dd2` fixed the underlying `CodexResponseAnalyzer`
defect rather than the test. The correction should say that, not merely retract the old note.

---

## Addendum 2026-08-13 (re-plan, task 74f4de94)

The base plan and the caller decisions above are kept verbatim — this addendum only records what
has moved in the two days since, and amends the design where the codebase has grown new
dependants. Verified against current master (`288ab95`): **nothing from this spec is implemented
yet** — `CardEndpoints.cs` still maps exactly the five original routes, `AppDbContext.cs:622`
still has `HasMaxLength(4000)`, and `CardRevision` appears nowhere outside docs. Everything in
the base plan's file table stands.

### Delta 1 — `CardRevision` must record MOVES, not only content edits

Since 2026-08-11, two shipped/near-shipped consumers wait on CardRevision for something the base
design cannot give them, because a content-snapshot row only exists when *text* is superseded and
a move supersedes no text:

- **Move reasons are accepted and dropped.** `MoveCardRequest` now carries `Reason`
  (`BoardDtos.cs:92-106`); it persists only into `TerminalReason` on a terminal move and is
  deliberately dropped otherwise, with doc comments in both `BoardDtos.cs` and
  `CardService.ApplyColumnMove` naming CardRevision as its future home
  (`docs/product/card-workflow-decisions.md`, 2026-08-13 entry).
- **The board state graph (feature 011) needs transition history.** Its proposal refuses
  time-in-state and per-edge flow counts "until CARD-0019's `CardRevision` provides
  entered-state-at" — i.e. it expects rows for column transitions.

**Amendment:** one table, `CardRevisions`, with a `Kind` discriminator:

- `Kind = ContentEdit` — exactly the base plan's shape: snapshot of the superseded
  title/description/priority/labels, required `Reason`, `EditedBy`.
- `Kind = Move` — `FromColumnId`, `ToColumnId`, `FromStatus`, `ToStatus`, optional `Reason`,
  `EditedBy`; content-snapshot columns null. Written by `ApplyColumnMove` for **every** move
  (spawn- and workflow-driven moves included, since they all pass through it); a terminal move
  additionally keeps stamping `TerminalReason` as the cheap-to-read summary it is today.

`RevisionNumber` stays one per-card monotonic sequence across both kinds, so
`GET /api/cards/{id}/revisions` returns one interleaved history — which is also what the
CardModal history tab wants. Entered-state-at = the card's latest `Move` row; per-edge counts are
a group-by. One table over two (a separate `CardTransitions`) because the UI, the API and the
revision numbering all want a single ordered history, at the cost of nullable snapshot columns on
Move rows. No backfill: existing cards simply have no history, which feature 011 already accepts.

Slice-3 gains a step: fold the parked entries of `docs/product/card-workflow-decisions.md` back
into their cards, as that file's own header promises, and shrink it to decisions that genuinely
live outside any card.

### Delta 2 — the 20k-cap condition, re-resolved after CARD-0037

The caller's condition ("shipping 20,000 over a typed prompt is not fine") was written in the
CARD-0027 world where every pty delivery was typed and clipped at ~1 KB. CARD-0037 has since
shipped and **this deployment runs the modern conpty backend**: `PtyDeliveryProfile` resolves
backend-conditional ceilings (inbox: brief 900 B; modern: 43,200 B, single-write, bracketed
paste delivered intact), requiring agreement between the server's own policy and the runner's
`GET /capabilities`.

The condition stands in spirit — never hand the pty a body it may clip — but its resolution is
now concrete and no longer blocks the cap:

> The spawn prompt (`CardService.BuildPrompt` → launch queue → `SendPromptAsync`) must go through
> the same ceiling-aware delivery as delegation briefs: measure the prompt in **UTF-8 bytes**
> against the `PtyDeliveryProfile`-resolved brief ceiling, deliver inline when under, and
> **spill to file + typed pointer** when over.

With that in place the 20,000-char cap ships unconditionally: on modern, a mostly-ASCII 20k
description (~20-22 KB) goes inline under 43,200 B; a worst-case multibyte one (up to 80 KB) or
any inbox-fallback machine spills. The cap needs no knowledge of the backend; the delivery layer
already owns that decision. Note this is the same gap **CARD-0025** tracks ("only delegation
paths spill oversized bodies") — implement the spawn-path spill once and settle both cards'
claims on it, rather than building it twice.

### Minor API correction

`DELETE /api/cards/{id}` with a request body (token + reason) is hostile to proxies and some
clients. Ship archive as `POST /api/cards/{id}/archive` (mirroring the already-planned
`POST /unarchive`); a bodyless `DELETE` alias is optional and not load-bearing.

---

## Two amendments exist, and they agree

`2026-08-13-card-0019-amendment-1.md` (task 1857c5d9) and the "Addendum 2026-08-13 (re-plan, task
74f4de94)" above were written independently on the same day and reached the **same** central
conclusion: `CardRevision` needs a `Kind` discriminator so that MOVES are recorded, not only content
edits. That agreement is the strongest evidence the design is right, and it is what slice 1 shipped
(commits `1d99c60`..`b2499dd`).

Amendment 1 carries two things the addendum does not, both still OPEN after slice 1:

- **A reopen endpoint.** `CardStateMachine` still maps `Done`/`Canceled` to `[]`, so a closed card
  cannot be reopened. Slice 2's planner confirmed this independently and left the client's
  `canMoveTo` lockstep untouched because of it.
- **CARD-0040 folded into slice 3**, alongside folding the parked entries of
  `docs/product/card-workflow-decisions.md` back into their cards.

Amendment 1 was stranded on `feat/card-task-1857c5d9` and never merged; it is recovered here
verbatim alongside `2026-08-13-card-0005-identifier-allocation.md`, a 187-line plan for CARD-0005,
which is still open.
