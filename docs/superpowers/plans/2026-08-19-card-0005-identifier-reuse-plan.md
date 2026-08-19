# CARD-0005 — Card identifier reuse: plan

**Date:** 2026-08-19
**Status:** planned (not implemented — close-out only; the allocator bug already shipped)
**Card:** CARD-0005 (`6c44df90-5022-4d37-9d88-39ca48d85f91`) — identifiers reused after a
delete, so `CARD-0007` can name two different cards over time.
**Precedent:** the card's own stated fix (`max+1` over existing suffixes, not `count+1`) landed
in `ce48f504` as CARD-0042's only server-scope item. CARD-0019 then shipped archive-instead-of-
delete (`POST /api/cards/{id}/archive`, closed Done). CARD-0019's close note left this card
open on purpose because `docs/superpowers/specs/2026-08-13-card-0005-identifier-allocation.md`
was still marked "planned, not implemented".

This is a planning document only. Do not write the close-out in the Plan pass.

## Verdict

**The reuse bug the card describes is not live.** Do not rewrite `NextIdentifierAsync`. Do not
add a sequence, a `Board` counter, or a delete endpoint. Close CARD-0005 after a Docs slice that
stops the board, `TODO.md`, and the 2026-08-13 spec from claiming count-based allocation.

Two independent facts, both already on master:

1. **Allocation is parse-max+1**, not count+1. Deleting a *middle* card leaves a gap; the next
   create continues from the highest remaining suffix. A live identifier cannot be handed out
   again while its row exists (`IX_Cards_BoardId_Identifier`).
2. **A single card cannot be hard-deleted through any reachable path.** The API has no DELETE.
   `card.ps1` has no delete verb. The client archives (`useArchiveCard` POSTs; a test pins
   "never DELETE"). Archive keeps the row, so the allocator still sees the number — pinned by
   `Archiving_the_highest_card_does_not_free_its_identifier`.

The remaining hole is real in the arithmetic and unreachable in production: a hard DELETE of
the *current highest* card (DbContext/`DELETE FROM "Cards"`) still frees that number, because
the row is the only record it was taken. That is why archive exists. It is a defensive
property of SQL surgery, not a live bug.

One Docs slice. No Code.

## 1. Current shape (verified 2026-08-19)

### 1.1 Allocator — `CardService.NextIdentifierAsync`

`server/Application/Services/CardService.cs:777-795`, called only from `CreateAsync` at `:114`:

```csharp
var identifiers = await _db.Cards
    .Where(c => c.BoardId == boardId)
    .Select(c => c.Identifier)
    .ToListAsync(ct);

var highest = 0;
foreach (var identifier in identifiers)
{
    // skip empty; parse the span after the last '-'; keep the max
}
return $"CARD-{highest + 1:0000}";
```

No global EF query filter on `ArchivedAt` (`HasQueryFilter` is absent from the repo). The
projection sees archived rows. That is load-bearing — `BoardService.ToDetailDto` filters
archived cards at the *read site* (`BoardService.cs:174-180`) precisely so this query does not
shrink.

The card's line citation (`:253-257`) and `TODO.md`'s copy of it are the May 2026 count-based
method. That body is gone.

`CreateAsync` still `Include(b => b.Cards)` (`:97`). Nothing in the method reads `board.Cards`;
allocation is the separate projection. Harmless; not this card.

### 1.2 Why a delete used to reuse, and what still would

| Mechanism | What happens | Status |
|---|---|---|
| `count + 1` (original) | Remove *any* row, count drops, next create can collide with a **live** identifier (delete CARD-0002 from a 3-card board → next is CARD-0003, already taken). | **Gone.** `ce48f504`. |
| parse-max+1 + hard-delete of a *middle* card | Gap stays. Next is one past the remaining max. | **Pinned.** `Card_identifier_allocation_skips_a_freed_number_instead_of_reusing_it` deletes CARD-0002 via a second DbContext and asserts the next create is CARD-0004. |
| parse-max+1 + hard-delete of the *current highest* | Remaining max is the previous number; next create reuses the deleted identifier. No live collision; historical citations now point at a new card. | **Still true of the arithmetic.** Unreachable via API. Archive pins the production path instead. |
| Archive | Row stays, `ArchivedAt` set, allocator still sees the suffix. | **Pinned.** `Archiving_the_highest_card_does_not_free_its_identifier`. |

The 2026-08-13 spec's regression pin ("hard-delete the highest, create again → not reused") is
internally inconsistent with parse-max+1. Max+1 *cannot* preserve a number whose only record
was the row. Feature 011's departure #6 already recorded this (`proposal.md:543-546`): max+1
stops the live collision; closing the highest-card hole "needs CARD-0019's archive". Archive
shipped. The 2026-08-13 spec was not updated.

### 1.3 Hard-delete paths, exhaustively

| Path | Deletes a card row? | Can the surviving board reuse that identifier? |
|---|---|---|
| `DELETE /api/cards/{id}` | **Does not exist.** `CardEndpoints.cs:99-109` says so in the archive comment: "hard delete deliberately does not exist for cards." | n/a |
| `scripts/card.ps1` | Verbs are get/history/new/edit/move/close/reopen/archive/unarchive. No delete. | n/a |
| Client | `POST /api/cards/{id}/archive`. `boards.test.tsx` pins "never DELETE". Move menu is Archive… | n/a |
| `ProjectCascade.DeleteBoardsAsync` (`ProjectCascade.cs:126`) | Yes, `ExecuteDeleteAsync` on every card of the board. | **No.** The board is deleted in the same cascade (`:131`). A new board is a new identifier space (`IX_Cards_BoardId_Identifier` is per-board). |
| Tests | `db.Cards.Remove` / `ExecuteDeleteAsync` in cleanup and in the CARD-0005 pin itself. | Test-only. |
| Raw SQL / a future DELETE endpoint | Yes. Highest-card reuse returns. | The hole. Not reachable today. |

### 1.4 What already pins this

- `tests/Antiphon.Tests/Application/BoardServiceIntegrationTests.cs:117-164` — middle-card
  hard-delete does not collide with a live identifier.
- `tests/Antiphon.Tests/Application/CardCorrectionIntegrationTests.cs:816-851` — archiving the
  highest card does not free it (and, by construction, pins "no global query filter").

Empty board → CARD-0001 falls out of `highest = 0`. Format `CARD-{n:0000}` widens past 9999
(`CARD-10000`) with no extra work; `HasMaxLength(100)` on the column.

### 1.5 Leftover from the 2026-08-13 spec, not reuse

`CreateAsync` is still allocate-then-`SaveChanges` with no retry (`:114-125`). Two concurrent
creates that both read max *N* both compute `CARD-{N+1}`; the unique index makes the second a
`DbUpdateException` → **500**. That is a loud collision, not a silent reuse. The spec designed
a 3-attempt retry keyed on `IX_Cards_BoardId_Identifier`, following
`AgentService.IsGeneratedNameCollision`. It never shipped.

Also never shipped: rollover / unparseable-identifier tests; tightening the parser from
"span after last `-`" to `^CARD-(\d+)$` (today a synced `JIRA-100` on the same board would
jump the native sequence to CARD-0101 — a skip, not a reuse). `ExternalTrackerSyncService`
writes `issue.ExternalKey` directly (`:180`) and does not go through `NextIdentifierAsync`.

None of this is CARD-0005 as filed. Do not expand the close-out to absorb them.

## 2. Decisions

| Option | Decision | Why |
|---|---|---|
| Rewrite `NextIdentifierAsync` again | **Reject** | Already parse-max+1. The card's stated fix is the body on master. |
| Per-board sequence / `Board.NextCardNumber` | **Reject** | The 2026-08-13 spec rejected this (contention on `Board`, drift vs reality, EF/DDL cost) and feature 011 scoped server work to "max+1, no schema". Archive occupies the number without a counter. Revisit only if a DELETE endpoint is added. |
| Never physically delete card rows | **Already the policy** | Archive is what "delete" means. `ProjectCascade` is the exception, and it deletes the board too. |
| Add a DELETE endpoint that tombstones instead | **Reject** | Out of scope; archive exists. |
| Unique-index retry in `CreateAsync` | **Out of scope** | Real, small, different bug (concurrent create 500). File it separately if it fires; do not hold this card open for it. |
| Global EF query filter on `ArchivedAt` | **Forbidden** | Would hide archived rows from the allocator and resurrect reuse of every archived number. The existing test is the lock. |

## 3. The slice (one Docs)

No production code. The Code agent (or a Docs agent) updates the records that still lie, then
closes the card.

### 3.1 `TODO.md` — delete the Bugs entry

The "Card identifiers are reused after a delete" paragraph (`TODO.md:21-26`) still quotes
count+1 at `:253-257`. Git history is the record; the card is the record. Delete the entry,
do not rewrite it.

### 3.2 Spec status

`docs/superpowers/specs/2026-08-13-card-0005-identifier-allocation.md`: change the header
**Status** from "planned, not implemented" to **implemented** (`ce48f504` + CARD-0019 archive),
and add a short note that:

- the highest-card hard-delete pin in § Slices was withdrawn (incompatible with max+1; archive
  is the production pin);
- unique-index retry, rollover/unparseable tests, and the unused `Include(b => b.Cards)` were
  not done and are not required to close this card.

Do not silently "finish" those leftovers in the same PR.

### 3.3 Feature 011 departure #6

`docs/features/011-board-state-graph/proposal.md:543-546` still says "CARD-0005 is fixed but
not closed" and "Closing that needs CARD-0019's archive-instead-of-delete". CARD-0019 is
Done. Rewrite the departure to: max+1 shipped in this feature; archive shipped in CARD-0019;
CARD-0005 closed by the close-out this plan names. Do not re-open 011.

### 3.4 Close CARD-0005

`pwsh -File scripts/card.ps1 close CARD-0005 -ReasonFile <file>`. Reason: allocator is
parse-max+1 since `ce48f504`; archive occupies the number since CARD-0019; no reachable
hard-delete of a single card remains; the 2026-08-13 spec's leftover retry is a different
bug and is not this card.

Do not edit the card's historical description to pretend it always described max+1 — the
stale diagnosis is the record of what was wrong in May. Closing is the correction.

## 4. What the Docs agent runs

No test run is required to *prove* the allocator; the two pins above already exist. If the
agent wants a cheap confirmation:

```
dotnet run --project tests/Antiphon.Tests --treenode-filter /*/*BoardServiceIntegrationTests/*Card_identifier_allocation_skips_a_freed_number_instead_of_reusing_it --property:OutputPath=bin-card0005/
dotnet run --project tests/Antiphon.Tests --treenode-filter /*/*CardCorrectionIntegrationTests/*Archiving_the_highest_card_does_not_free_its_identifier --property:OutputPath=bin-card0005/
```

Forward slash on `OutputPath`. Delete the `bin-card0005/` directories after.

## 5. Commit

`docs(cards): CARD-0005 - mark identifier allocation implemented; archive occupies the number`
