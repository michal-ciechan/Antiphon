# CARD-0005: Card identifier allocation — parse-max+1, retry on the unique index

**Status:** **Implemented** (`ce48f504` parse-max+1 as CARD-0042's server-scope item; CARD-0019
archive occupies the number). **Card:** CARD-0005. **Date:** 2026-08-13; closed 2026-08-19.
**Prerequisite for:** feature 011 (board state graph, CARD-0042) and interacts with CARD-0019
(card correction) — see § Interactions.

**Close-out notes (2026-08-19):** the § Slices "hard-delete the highest, create again → not
reused" pin was withdrawn — incompatible with parse-max+1 (the row is the only record the
number was taken; archive is the production pin instead:
`Archiving_the_highest_card_does_not_free_its_identifier`). Unique-index retry in `CreateAsync`,
rollover/unparseable-identifier tests, and dropping the unused `Include(b => b.Cards)` were
not done and are not required to close this card (concurrent-create 500 is a different bug).

## The mechanism, verified in code

`CardService.NextIdentifierAsync` (`server/Application/Services/CardService.cs:271-275`, called
from `CreateAsync` at `:63`):

```csharp
private async Task<string> NextIdentifierAsync(Guid boardId, CancellationToken ct)
{
    var count = await _db.Cards.CountAsync(c => c.BoardId == boardId, ct);
    return $"CARD-{count + 1:0000}";
}
```

Count-based, scoped per board. Two verified consequences:

1. **Delete ⇒ reuse.** Remove any card row and the count drops by one, so the next created card
   takes an identifier that has already named a different card. There is currently **no
   single-card DELETE endpoint** (`CardEndpoints.cs` exposes none, and no service method removes
   a single card) — the hard-delete paths that exist today are `ProjectCascade`
   (`ProjectCascade.cs:126`, deletes *all* of a board's cards when the project/board dies, so no
   surviving board can reuse) and direct DB surgery. The reuse hazard is therefore: past manual
   deletes, tests, DB maintenance, and any future delete/move-off-board feature. The bug is real
   but latent in the API surface; the card's own description records it happening.
2. **Concurrent creates collide today.** Two `CreateAsync` calls that both read count *N* both
   compute `CARD-{N+1}`. The unique index `IX_Cards_BoardId_Identifier`
   (`AppDbContext.cs:639-641`) makes the second `SaveChangesAsync` throw `DbUpdateException`,
   which nothing catches in `CreateAsync` ⇒ **500**. So the count-based allocator is not merely
   reuse-prone; it is already race-broken, just with a loud failure instead of a silent one.

The unique index is also the load-bearing good news: **live rows cannot silently share an
identifier**. Reuse only ever recycles the identifier of a *removed* row.

## Decision: parse-max+1, no schema change

Rewrite `NextIdentifierAsync` to take the maximum parsed numeric suffix over the board's
existing identifiers, plus one:

```csharp
private async Task<string> NextIdentifierAsync(Guid boardId, CancellationToken ct)
{
    var identifiers = await _db.Cards
        .Where(c => c.BoardId == boardId)
        .Select(c => c.Identifier)
        .ToListAsync(ct);
    var max = identifiers
        .Select(ParseCardNumber)   // "CARD-0041" -> 41; unparseable -> 0
        .DefaultIfEmpty(0)
        .Max();
    return $"CARD-{max + 1:0000}";
}
```

`ParseCardNumber`: `^CARD-(\d+)$` (case-sensitive, matching what the allocator has ever
produced) → `int`; anything unparseable → 0, i.e. ignored rather than fatal. Parsing happens
in memory — a board holds dozens of cards (42 on the Antiphon board today), so a
`Select(Identifier)` projection and an in-memory max is simpler and more portable than SQL
substring arithmetic, and it keeps the parse rule in one testable C# function.

### Why not a real sequence

A per-board counter column on `Board` (or dynamic Postgres sequences per board) was considered
and rejected:

- **Both dependents already assume max+1.** Feature 011's proposal scopes its server work to
  exactly "the CARD-0005 fix only (parse-max+1 in `NextIdentifierAsync`). No schema, no new
  identifier column" (`docs/features/011-board-state-graph/proposal.md`, §4), and CARD-0019's
  archive design already reasons about the allocator reading card rows. A schema-bearing fix
  here forces both plans to re-open.
- **Dynamic per-board sequences are unmanageable** in EF/migrations (DDL at board-create time,
  orphan cleanup at board-delete time) for a problem whose whole domain is four-digit numbers.
- **A counter column on `Board`** turns every card create into a `Board` row write — a new
  contention point and concurrency-token churn on an entity other services update — to buy a
  guarantee the unique index already provides more cheaply (see § Concurrency).
- Max+1 over rows is **self-healing**: it derives from the data that exists, needs no backfill,
  and cannot drift from reality the way a stored counter can (e.g. after a manual row restore).

Revisit only if identifier allocation ever becomes hot-path (it is one create per human/agent
decision; it will not).

## What happens to identifiers that already exist

**Nothing.** Max+1 reads the existing identifiers; it never rewrites one. All live rows are
already mutually distinct (the unique index guarantees it), so the new allocator continues the
sequence from the true high-water mark instead of the row count — on any board where deletes
have happened, the next allocated number *jumps* past the count, which is the fix working, not
a bug. Format is unchanged (`CARD-{n:0000}`); past 9999 the format widens naturally to five
digits (`CARD-10000`), well inside the column's `HasMaxLength(100)`, and feature 011's parser
already accepts variable-width digits.

What cannot be repaired: any identifier that was **already recycled** before this fix names two
cards across time, and the earlier card's row is gone — there is no record to detect or
disambiguate with. Citations (docs, commit messages, task files) that predate the fix are
trustworthy only where the cited card still exists. The fix draws a line from its ship date
forward; it cannot redraw history. This is precisely why feature 011 calls it a prerequisite
for `#41`-style citations: stability starts when this lands.

## Per-board scoping

Kept exactly as-is: the max is computed `Where(c => c.BoardId == boardId)`, and the unique
index is `(BoardId, Identifier)`. `CARD-0041` remains unambiguous only within a board — feature
011 already states and designs for that caveat (board name adjacent on cross-board surfaces).
Nothing in this fix makes identifiers globally unique, and nothing should: global uniqueness is
a different feature with a migration, and no dependent asks for it.

## Concurrency: the unique index is the arbiter, retry is the policy

Max+1 has the same read-compute-write race as count+1. The fix does not try to serialize the
read — it lets `IX_Cards_BoardId_Identifier` arbitrate and **retries on that specific
violation**:

- In `CreateAsync`, wrap allocate+`SaveChangesAsync` in a bounded retry (3 attempts). On
  `DbUpdateException`, classify by the constraint named in the Postgres error
  (`IX_Cards_BoardId_Identifier`) — following the `AgentService.DescribeDbFailure` precedent
  and the house rule that a DB failure is never reported without the DB's own message. Matching
  violation ⇒ detach the failed entity, reallocate, retry. Any other `DbUpdateException`, or
  attempts exhausted ⇒ rethrow wrapped with the inner exception attached (never discarded), so
  `ExceptionMiddleware.BuildStackTrace` surfaces the real constraint.
- Two cards created at once therefore both succeed with consecutive identifiers; today one of
  them 500s. The retry is a strict behavioral improvement independent of the reuse fix.

Note `CreateAsync` currently does `Include(b => b.Cards)` on the board load (`:46`) — with the
projection query above that include looks unused; verify and drop it in the same change if
nothing else reads `board.Cards` (optional cleanup, not load-bearing).

## Interactions

### CARD-0019 (archive instead of delete)

- **Ordering: this lands first.** It is one method plus a retry; CARD-0019 is a schema + four
  endpoints + client work. Nothing in CARD-0019 blocks it.
- CARD-0019's plan justified archive partly as "archived rows keep the count monotonic". After
  this fix that particular rationale is stale — max+1 is monotonic even across a hard delete —
  but the archive decision itself is unaffected (its real ground is that the record must never
  be destroyed). The 2026-08-13 amendment to that plan records this.
- **The rule "no global EF query filter on archived cards" survives unchanged, and this plan
  restates it**: `NextIdentifierAsync` must compute its max over ALL rows including archived
  ones. Under max+1 an archived-row-blind allocator no longer *silently reuses* (the archived
  row still holds the identifier, so the unique index turns the collision into a retry that
  recomputes… still blindly, i.e. an infinite-ish retry burning attempts) — it fails loudly
  instead of corrupting, but it still fails. Keep the allocator's query filter-free.
- CARD-0019's planned identifier-guard test (create A, archive A, create B ⇒ B ≠ A) remains
  exactly right and becomes doubly guaranteed: max+1 sees the archived row, and the unique
  index backstops.

### Feature 011 (board state graph, CARD-0042)

- 011 declares this fix its only server-scope item and a prerequisite for stable citations.
  Land this before (or as the first commit of) 011's implementation.
- 011's test list includes the same regression pin this plan ships ("create, delete highest,
  create again → identifier not reused"). **This plan owns that test**; 011 references it
  rather than duplicating it.
- The shared client parser (`cardIdentifier.ts` in 011's plan) assumes a numeric suffix in all
  accepted forms — max+1 preserves the format byte-for-byte, including the natural widening
  past 9999.

## Slices and tests

One slice, server-only:

1. Rewrite `NextIdentifierAsync` to parse-max+1 (projection query, in-memory parse, filter-free).
2. Bounded retry in `CreateAsync` keyed on the named unique-constraint violation, inner
   exception preserved.
3. Tests (`Antiphon.Tests`, all scoped to boards the test creates per the shared-Postgres rule):
   - **Regression pin (the CARD-0005 case):** create cards, hard-delete the highest via the
     DbContext (the API has no delete — the test uses the context directly, which also documents
     that fact), create again ⇒ identifier is max+1, not a reuse.
   - Gap tolerance: seed identifiers with holes (CARD-0001, CARD-0003) ⇒ next is CARD-0004.
   - Rollover: seed CARD-9999 ⇒ next is CARD-10000; parser round-trips five digits.
   - Unparseable identifiers ignored: seed a row with a non-standard identifier ⇒ allocation
     unaffected, no throw.
   - Empty board ⇒ CARD-0001.
   - Concurrency: N parallel `CreateAsync` on one test-local board ⇒ all succeed, N distinct
     identifiers (races are probabilistic on the shared testcontainer, but the assertion —
     no failures, all distinct — is deterministic); plus a direct test of the retry path by
     inserting a conflicting row between allocation and save via a second context if a seam
     makes that cheap, otherwise the classifier gets its own unit test.
   - The archived-row case ("archived rows still count toward max") ships with CARD-0019's
     migration — `ArchivedAt` does not exist yet. Noted here so it is not forgotten there.

Out of scope: any delete endpoint (CARD-0019 decides delete-is-archive), global identifier
uniqueness, changing the identifier format, backfilling or repairing pre-fix citations.
