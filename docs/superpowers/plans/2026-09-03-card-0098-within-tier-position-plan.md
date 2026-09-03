# CARD-0098 — Explicit order within a rank cell: `position`, drag-to-reorder, and a relative-placement API that agents call

**Date:** 2026-09-03 (Plan pass, task 31bc9be7 — design only; no production code changed, no tests run)
**Card:** CARD-0098 "Cards need an explicit rank within their priority tier - drag-to-reorder and AI-driven reprioritization"
**Builds on:** [`2026-09-02-card-0039-importance-urgency-axes-plan.md`](2026-09-02-card-0039-importance-urgency-axes-plan.md)
(the shipped ranking model this card orders inside) and
[`2026-09-02-card-0094-backlog-by-quadrant-plan.md`](2026-09-02-card-0094-backlog-by-quadrant-plan.md)
(the Orchestrator page's Backlog boxes, the natural consumer).

**Sources (verified this pass, head `30dbb85e`, live 17202 on 2026-09-03):** CARD-0098 including the
operator's 2026-09-02 field-shape addendum, CARD-0039 (Done), CARD-0094 (Review, S1–S2 landed
`df43bed4`), CARD-0096 (Backlog, unplanned), CARD-0327 (importance provenance);
`server/Application/Services/{CardRanking,CardService,BoardService,OrchestratorService,HomeTaskService,
CardTaskFileRenderer,WorkflowDefinitionLoader,CardRevisionLog,AgentService,ExternalTrackerSyncService}.cs`,
`server/Domain/Entities/{Card,CardRevision,BoardColumn}.cs`, `server/Domain/Enums/{CardImportance,
CardUrgency,CardRevisionKind}.cs`, `server/Application/Dtos/{BoardDtos,AgentDtos,HomeTaskDtos}.cs`,
`server/Api/Endpoints/{CardEndpoints,BoardEndpoints,AgentEndpoints}.cs`,
`server/Infrastructure/Data/AppDbContext.cs`, `server/Migrations/20260902200000_*.cs`,
`client/src/features/board/{boardShapeModel,cardRanking}.ts`, `{CardListSection,CardRow,MoveMenu,
BoardPage,CardEditModal}.tsx`, `client/src/features/orchestrator/backlogModel.ts`,
`client/src/api/boards.ts`, `client/src/hooks/useSignalRInvalidation.ts`, `client/package.json`,
`scripts/card.ps1`, `docs/{orchestration-loop,agent-card-lifecycle,antiphon-api,testing-and-build}.md`,
`tests/Antiphon.Tests/Application/{CardRankingOrderTests,OrchestratorServiceIntegrationTests,
CardCorrectionIntegrationTests}.cs`; live `GET /api/cards?boardId=` (Antiphon, 328 cards) and
`GET /api/cards?status=Backlog` (55 cards on 2 boards).

---

## Verdict up front

**One nullable integer, `Card.Position`, that orders cards which share a rank cell; dense
renumbering of that cell on every reorder; tier stays where CARD-0039 put it.** Every sort site
reads one key — `(rank, position ?? last, dueAt, createdAt)` — so a drag on the board, a
`card.ps1 reorder`, and an agent's bulk reprioritisation all change **dispatch order, the Home
rail, the Orchestrator Backlog boxes and the `docs/cards/` index**, not just the board's display.
The API is *relative placement* (`before` / `after` a named card, or `top` / `bottom` of the
card's own cell); absolute positions never cross the wire, which is what lets the storage be
the plainest possible shape.

**The operator's 0–999 encoded-tier model is not adopted, for one structural reason and two
practical ones.** Structural: since CARD-0039 there is no single tier axis. The board's bands and
CARD-0094's boxes are **quadrants** (importance × effective urgency), the sort is by `rank`, and
`rank` deliberately interleaves importance levels — `Low/Now` (9) beats `Normal/Normal` (10),
`Normal/Now` (6) beats `High/Normal` (7). A single integer whose sub-ranges are importance levels
cannot express "dragged into the *Clear* band" (an urgency change) at all, and sorting by it would
silently make urgency and `dueAt` display-only, reversing CARD-0039 §Decision 6. Practical: (a)
effective urgency moves with the clock (`dueAt` escalation), so any stored value that encodes the
cell goes stale exactly the way a persisted `rank` would — the reason `CardRanking.cs` has no such
column; (b) with 250 slots per tier and midpoint insertion, the most common gesture — "put this
one first" — exhausts the gap in about seven consecutive drags (log₂ 125), so "rebalance on
exhaustion" would be the routine path, not the rare one. When the routine path is "rewrite the
cell", make that the only path: the cell is ≤ 44 rows today.

**Tier is a separately stored field, and there is nothing to keep in sync.** `Importance` and
`Urgency` remain the record (with CARD-0327 provenance, revisions, tracker labels and workflow
variables all reading them). `Position` carries **no** tier information: it only breaks ties among
cards that are in the same cell *at read time*. A drop that crosses a cell boundary is an explicit,
revision-logged change to the axes made in the same request — the operator's "crossing a
boundary reclassifies the tier" — never an inference from a number.

### The card's five questions, answered

1. **Field shape** — `Position int?` on `Cards`, dense `1..n` per (column, rank cell) rewritten on
   each reorder; `null` = never placed, sorts after every placed card in its cell, then `dueAt`,
   then `CreatedAt` (today's order, unchanged for every card until someone drags one). Comparison
   in §Field shape below.
2. **Scope** — per board column, per rank cell. Any change of column (`ApplyColumnMove`, reopen,
   the CARD-0040 sweep) or of the axes (content edit, tracker sync while `Auto`) **clears it**: the
   card lands at the bottom of its new cell like a new card. A promotion that should also go to
   the top is one call (`placement: "top"` alongside `importance`).
3. **How AI reprioritises** — the same two endpoints a human's drag uses, called by whatever
   agent the operator dispatches (a `delegate.ps1` Custom/Plan role, or a `ScheduleKind.Prompt`
   schedule), with `editedBy` naming the agent and a reason on every write. There is no new role,
   no tick side effect, and no server-side "AI" component: AGENTS.md's rule that tracker writes are
   explicit actions applies here too. §AI reprioritisation gives the contract and the prompt recipe.
4. **CARD-0094** — inherits it for free: `groupBacklog` orders each box with `orderCards`, which
   gains `position` in S3. Drag inside a box is S5, gated to a single-board list (a cross-board box
   has no server-side order to write). No re-plan of CARD-0094.
5. **Dispatch** — **switches.** `LoadEligibleCandidatesAsync` sorts by the same key. Display-only
   would make Q3 a lie, and CARD-0039 §Decision 10 ("one function owns the order") is the rule
   that keeps the board, the tick, the Home rail and CARD-0096's "top N" from ever disagreeing.

---

## Ground truth (checked, not guessed)

| Claim in the card | Today (`30dbb85e`) | Consequence |
|---|---|---|
| `OrderByDescending(c => c.Priority).ThenBy(c => c.CreatedAt)` in the tick | Gone. `OrchestratorService.cs:786-789`: materialise, then `rank` asc, `DueAtSortKey`, `CreatedAt` | The tie-break to replace is `CreatedAt` inside a rank cell |
| `CardRanking` "already computes a `rank`, this may replace or extend that" | `rank` is **derived at read time** from `(Importance, Urgency, DueAt, now)`; there is no column (`CardRanking.cs:7-10`) | `position` is a separate stored ordinal, as CARD-0039 §"What this card does not do" already said; the name `rank` is taken |
| Tiers are P0–P3 | Four `CardImportance` levels × three `CardUrgency` levels → 12 distinct `rank` values (the formula is injective); UI groups by 4 quadrants | "Within a tier" must mean within a rank cell; a quadrant band can hold two cells |
| `BoardColumn.ColumnOrder` is the precedent | It is create-only (`BoardService.cs:509`); the live reorder precedent is `AgentService.ReorderQueueAsync` (`:792-846`): full-list renumber `1..n` in a `ReadCommitted` transaction, rotating every changed card's `ConcurrencyToken` | Renumbering is proven here; the token rotation is the part not to copy |
| Drag-and-drop exists on the board | Removed on purpose (`MoveMenu.tsx:31-40`): column drag had nowhere for a Reason and spawned an agent as a side effect. No dnd library in `client/package.json` | A reorder drag has no spawn side effect and records its neighbour as the reason; an axis change is surfaced with Undo (§Decision 7) |
| ~15–75 cards per tier | Antiphon Backlog 51 = 44 `Normal/Normal` + 7 `Low/Normal`; Review 47 = 20 High + 25 Normal + 2 Low; 0 Critical, 0 `Soon`/`Now`, 0 `dueAt` anywhere; second board (Gym Stat) 4 Backlog | Largest cell 44 rows; a full-cell renumber is trivially cheap |
| CARD-0094 is the consumer | Shipped (`df43bed4`): boxes per quadrant, rows via `orderCards` (`backlogModel.ts:41`) | One function to extend |
| CARD-0096 "reuse `OrderByDescending(c => c.Priority)`" | Stale; the tick's order is `CardRanking` | Its selection order becomes this key; note added to CARD-0096 (this pass) |

---

## Field shape: the four candidates, compared on this codebase

| | Plain reindex (`AgentQueuePosition` pattern) | **Sparse int, no tier encoding, renumber cell on write (chosen)** | Operator's 0–999 encoded tiers, midpoint insert, rebalance on exhaustion | LexoRank (string) |
|---|---|---|---|---|
| Column | `int` dense per list | `int?` dense per (column, cell); null = unplaced | `int` 0–999, one per card | `varchar` |
| Rows touched per drag | whole list (column: ≤ 51) | the cell (≤ 44) | usually 1; whole tier every ~7 top-inserts | 1 |
| Tier source of truth | separate field | **separate field (`Importance`/`Urgency`, unchanged)** | the range the number falls in, or a duplicate field kept in sync | separate field |
| Expresses the UI's bands (quadrants) | via the separate axes | **via the separate axes** | **no** — one axis only; an urgency change has no range | via the separate axes |
| Survives `dueAt` moving a card between cells | yes (ties fall through) | **yes** — position is relative only inside the cell at read time | no — the stored range disagrees with the effective cell | yes |
| "Middle of the tier" default | n/a | n/a — unplaced sorts last, as today | yes | n/a |
| Exhaustion / rebalance | never | never | routine at this board's gesture pattern | practically never |
| Wire shape | ordered id list | **relative: before/after/top/bottom; ordered list for bulk** | absolute integers (an agent must read neighbours' numbers to write one) | opaque strings; relative anyway |
| Migration | int, backfill required | **nullable, no backfill** | int + a backfill assigning tier midpoints | string, backfill |
| Fits `CardRanking`'s "no persisted, clock-dependent value" | yes | **yes** | no | yes |

The chosen shape is the plain-reindex pattern scoped to the cell rather than the column, made
nullable so that nothing has to be backfilled and no create path (`CardService.CreateAsync`,
`ExternalTrackerSyncService` import, seeds, tests) has to learn about it. Dense `1..n` is preferred
over "gaps of 1000" because the wire never carries absolute values, so gaps would buy nothing but
a second code path.

---

## Decisions

1. **Sort key, everywhere.** `CardRanking` gains
   `OrderKey(importance, urgency, dueAt, position, createdAt, now)` returning a comparable
   `(int rank, int position, DateTime due, DateTime created)` with `position ?? int.MaxValue`, and
   an overload taking a `Card`. The four server sites (`BoardService.ToDetailDto`,
   `OrchestratorService.LoadEligibleCandidatesAsync`, `HomeTaskService.Compare` for `Next`,
   `CardTaskFileRenderer.RenderIndex`) and the client's `orderCards` use it and nothing else. The
   existing `CardRankingOrderTests` grows one case: same rank, later `CreatedAt`, `position = 1`
   sorts first; `position` is ignored across different ranks.

2. **`Position` is cleared by anything that changes the card's column or cell.** Set to `null` in
   `ApplyColumnMove` (covers `MoveAsync`, `ReopenAsync`, the CARD-0040 sweep and the tracker
   sync's moves), in `UpdateContentAsync` when `Importance` or `Urgency` changes, and in the tracker
   sync's importance refresh. A `dueAt` change does **not** clear it: effective urgency may not
   change, and if it does the card simply ties in its new cell until placed. Pinned by
   `CardReorderIntegrationTests`.

3. **A cell is a rank value.** `CardRanking.Rank` is injective over the 12 `(importance, effective
   urgency)` pairs, so "the neighbour's cell" is exactly one stored importance and one *effective*
   urgency. Placing a card among cards of another cell sets `Importance` to the neighbour's and
   `Urgency` to the neighbour's **effective** urgency (a `dueAt`-escalated neighbour is `Now`;
   adopting its stored `Normal` would not land the card where it was dropped). `UrgentSince` and
   `ImportanceProvenance = Human` follow the existing `UpdateContentAsync` rules. If the moved
   card's own `dueAt` makes its effective urgency *higher* than the target cell, the placement is
   unreachable → **422 `card_position_unreachable`** naming the date ("CARD-0012 is due in 2 days
   and cannot sort below CARD-0015; clear or move its due date first"). Not reachable on the live
   board today (no `dueAt` anywhere); it exists so the drag never silently lands somewhere else.

4. **Relative placement is the single-card contract.** `PATCH /api/cards/{id}/position`, body
   `{ concurrencyToken, before?, after?, placement?, reason?, editedBy? }` — exactly one of `before`
   (a card ref: `CARD-nnnn`, `#n`, or guid, resolved on the moved card's board), `after`, or
   `placement: "top" | "bottom"` (of the card's *current* cell, no axis change). The server loads
   the column's unarchived cards, orders them with the key at `now`, validates that `before`/`after`
   are adjacent in that order once the moved card is removed (the client sends both neighbours
   when it has them; either alone is accepted) — otherwise **409 `card_order_stale`** and the
   client refetches — chooses the cell (the neighbour whose cell matches the card's own if one
   does, else the card *below*, so dragging within your own cell to its edge keeps your cell and a
   drop on a boundary from elsewhere never inflates importance), applies the axis change if any,
   inserts the card, renumbers that cell `1..n`, and saves in one `ReadCommitted` transaction.
   Returns the `CardDto`. The moved card's `concurrencyToken` is required, checked, and rotated;
   **neighbours' tokens are not touched** — the one deliberate departure from `ReorderQueueAsync`,
   because a 44-row renumber must not 409 an operator mid-edit on an unrelated card. One
   `CardChanged` event for the moved card; the board-detail query invalidates on it
   (`useSignalRInvalidation.ts:69-80`) and the neighbours' new numbers arrive with the refetch.

5. **Ordered list is the bulk contract, and it is what an agent should call.**
   `POST /api/boards/{id:guid}/card-order`, body
   `{ cards: [{ id, importance?, urgency? }, …], reason, editedBy?, overrideHumanRatings: false }`.
   `reason` is **required** (bulk is a scripted act; CARD-0019's discipline). Semantics: apply each
   listed axis change (skipping cards whose `ImportanceProvenance` is `Human` unless
   `overrideHumanRatings`, and reporting them), then, per cell, the listed cards come **first, in
   list order**, and unlisted cards follow in their existing order; every touched cell is
   renumbered. One transaction; every listed card's token is rotated and it gets one `Reorder`
   revision. Response `{ cards: CardDto[], skippedHumanRated: [{ id, identifier, importance }] }`.
   This is the shape an agent wants — "here is my order for the top of the backlog" — not twelve
   `before:` calls with twelve reasons. It is S4 and can ship after S3; an agent can loop S2's
   endpoint in the meantime.

6. **Reorders are revision-logged as their own kind.** `CardRevisionKind.Reorder = 5`, carrying the
   superseded `Position` (new nullable column on `CardRevisions`) and, when the axes changed, the
   superseded `Importance`/`Urgency` in the existing columns, so `CardHistory` renders "Placed
   before CARD-0094 · importance Normal → High". The reason is the request's reason, suffixed by
   the server with the fact it knows (`"… (placed before CARD-0094)"`, `"… (top of cell)"`), or
   just that fact when the caller gave none. Renumbered neighbours get no row: their order relative
   to each other did not change, and the *order* is the record, not the number. `revisionCount`
   therefore bumps on every drag — accepted; the History tab's count is "not an edited marker"
   already (`boards.ts:84-88`).

7. **Drag on the board page, with the MoveMenu objection answered in the design.** A grip handle on
   `CardRow` (desktop pointer; touch with a 200 ms delay on the phone layout; keyboard via
   dnd-kit's `KeyboardSensor` — space, arrows, space), one `SortableContext` per rendered list (the
   quadrant band when the column is banded, the whole column otherwise; terminal columns get no
   handle). The two objections that removed column drag do not apply: a reorder starts nothing,
   and its reason is the fact the server records (Decision 6). The one side effect a drop *can*
   have — crossing a cell — is shown, not hidden: a notification "CARD-0098 placed above
   CARD-0094 · importance Normal → High" with **Undo** (one call: `after` the previous neighbour
   plus the previous axes, sent through the same endpoint's optional `importance`/`urgency`
   overrides, which the bulk contract already needs). Optimistic `arrayMove` on the board-detail
   cache, revert on error, invalidate on success (the `useMoveCard` pattern, `boards.ts:581-614`).
   `MoveMenu` gains *Move to top* / *Move to bottom* (`placement`), which is the phone's cheap path
   and the accessible one. Cross-**band** drag (Someday → Schedule) is not in v1: bands collapse
   independently and it is a plain importance edit the modal already does; S5 may add it as a
   multi-container drop if the single-container version proves itself.

8. **`card.ps1 reorder`.** `card.ps1 reorder CARD-0098 (-Before CARD-0094 | -After CARD-0094 | -Top |
   -Bottom) [-Reason r | -ReasonFile p] [-By name] [-Token g]`; the same fresh-token default as
   every other write. `get` prints `pos n` after the rank when placed. Bulk from the CLI is
   `card.ps1 order -Board <b> -OrderFile <path> -Reason …` where the file is one card ref per line,
   optionally `CARD-0098 High` / `CARD-0098 High Soon` to carry axis changes — a file, because a
   list of twelve refs with reasons is exactly the "fifteen throwaway scripts" case the header warns
   about. ASCII only.

9. **No backfill, no default placement write.** The migration adds two nullable columns and
   nothing else. Every card is unplaced on day one and the order is byte-for-byte today's. The
   operator's "middle of the tier on creation/promotion" is replaced by "unplaced = bottom of the
   cell, oldest first", which is what the board does now; the promotion-to-top case is one call
   with `placement: "top"`. Stated as a deviation, deliberately: mid-tier was a property of the
   gap mechanism, not a product intent.

10. **Where it does *not* go.** `AgentQueuePosition` (a per-agent dispatch-queue slot) is
    untouched and unrelated; the `docs/cards/` front matter does not gain a `position:` line (the
    index order carries it; a value that renumbers on every drag would churn 300 generated files
    for nothing); `GET /api/cards` summary order stays `UpdatedAt desc` (its consumers re-sort).
    Workflow prompt variables gain `card.position` (empty when unplaced) because a workflow prompt
    that says "you are card 3 of 44 in this cell" is cheap and true.

---

## AI reprioritisation: the concrete shape

**Mechanism.** An agent that can call the API — a `delegate.ps1` dispatch with `-Role Custom` (or
Plan) and a goal, or a `ScheduleKind.Prompt` schedule that runs one weekly — reads the backlog and
writes order and ratings through the two endpoints above, via `card.ps1 reorder` / `card.ps1 order`
with `-By <agent name>` and a reason on every write. Nothing runs on the orchestrator tick. The
resulting revisions (`Reorder`, actor = the agent) are the audit trail; `CardChanged` refreshes
every open view.

**The recipe** (lands in `docs/orchestration-loop.md` §Picking as "Reprioritising the backlog"):

1. Read `GET /api/cards?status=Backlog&boardId=<b>` (55 rows today, one request) and, for cards it
   intends to move, `GET /api/cards/{id}/thread` for the plan/task context.
2. Leave `importanceProvenance: Human` ratings alone unless the reason says why
   (`overrideHumanRatings` stays false; the response lists what was skipped).
3. Prefer one `card.ps1 order` with an ordered file and one reason over N `reorder` calls; put the
   argument for the order in the reason ("unblocks CARD-0331; CARD-0301 waits on 0323").
4. Never move a card between columns; reprioritising is not dispatching. A card in an active column
   is the tick's business.
5. Report the before/after top-ten in the delegate report so the operator can undo from history.

**Why not a role, a tick action, or a signal-driven rule.** A new `AgentTaskRole` would need a
launch spec, prompt template and settle rules for a job that is one prompt; a tick side effect is
what AGENTS.md forbids for tracker writes and would be worse here (an unattended reorder of the
dispatch queue); age/incident-driven automatic ordering is the "aging scheme that makes a backlog
lie" CARD-0039 §Decision 4 rejected. If a signal-driven rule is ever wanted, it is a scheduled
prompt that *proposes* an order via the same endpoint, with its reasoning in the reason.

---

## Data model after this card

```
Card
  Position      int?        -- null = never placed; dense 1..n inside (column, rank cell) after a reorder
CardRevision
  Kind          Reorder = 5 -- new kind; Reason carries the placement fact
  Position      int?        -- the SUPERSEDED position on a Reorder row
```

`CardDto` adds `position: int?` (full and summary representations). `CardRevisionDto` adds
`position`. `HomeTaskItemDto` is unchanged (it sorts, it does not show). Workflow variables add
`card.position`. New request records:

```csharp
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PlaceCardRequest(
    Guid ConcurrencyToken,
    string? Before = null,            // card ref on the same board
    string? After = null,
    CardPlacement? Placement = null,  // Top | Bottom of the current cell
    CardImportance? Importance = null, // optional overrides; used by Undo and by scripts
    CardUrgency? Urgency = null,
    string? Reason = null,
    string? EditedBy = null);

public sealed record ReorderBoardCardsRequest(
    IReadOnlyList<ReorderBoardCardEntry> Cards,
    string Reason,
    string? EditedBy = null,
    bool OverrideHumanRatings = false);

public sealed record ReorderBoardCardEntry(string Id, CardImportance? Importance = null, CardUrgency? Urgency = null);
public sealed record ReorderBoardCardsResult(IReadOnlyList<CardDto> Cards, IReadOnlyList<SkippedHumanRatedDto> SkippedHumanRated);
```

Validation: exactly one of `Before`/`After`/`Placement` unless both `Before` and `After` are given
(then they must be adjacent); a ref that resolves off the moved card's board is 422; the moved card
named as its own neighbour is 422; an archived card as neighbour is 422; `Reason` within
`MaxReasonLength`; `EditedBy` within `MaxActorLength`.

---

## Slices

Sequential, one worker at a time, **`-Worktree`** (other execute-stage workers are active on
master today and S1–S2 touch `CardService`/`BoardDtos`). Server before client; the 17203 bundle
must be rebuilt before any browser check. Estimates are verification floor + authoring.

### S1 — Domain, migration, sort key, every sort site (server only)

- `Card.Position`, `CardRevision.Position`, `CardRevisionKind.Reorder`, `CardPlacement` enum;
  `AppDbContext` mappings (`Position` nullable, no index — every sort is in memory per column).
- Migration `20260903xxxxxx_AddCardPosition`: hand-written in the CARD-0327 style
  (`server/Migrations/20260902200000_*.cs` — daemons hold `bin/`), two `AddColumn<int>` nullable,
  no `Sql`; update `AppDbContextModelSnapshot.cs` by hand to match.
- `CardRanking.OrderKey(...)` + `Card` overload; `BoardService.ToDetailDto`,
  `OrchestratorService.LoadEligibleCandidatesAsync` (`DispatchCandidate` gains `Position`),
  `HomeTaskService` (`CardRow`/`RankedItem` carry it; `Compare` for `Next` uses it after `Rank`,
  before `CreatedAt`), `CardTaskFileRenderer.RenderIndex`, `WorkflowDefinitionLoader`
  (`card.position`), `BoardService.ToCardDto` / summary DTO (`position`), `CardRevisionDto`.
- Clear-on-change (Decision 2): `ApplyColumnMove`, `UpdateContentAsync`, tracker-sync importance
  refresh. `CardRevisionLog.AppendContentEdit` snapshots `Position` too (a promotion clears it; the
  history should say what it was).
- Tests: `CardRankingOrderTests` (+2 cases), `OrchestratorServiceIntegrationTests`
  (`PollTick_dispatches_the_placed_card_ahead_of_an_older_same_rank_card`),
  `HomeTaskServiceIntegrationTests` (Next honours position), `CardTaskFileRendererTests` (index
  order), `WorkflowDefinitionLoaderTests` (`card.position`), `CardCorrectionIntegrationTests`
  (importance edit clears position; move clears position), `ContractSnapshotTests` seeding
  compiles.
- Verify: `--treenode-filter "/*/*/CardRankingOrderTests/*"` and the named classes, not the
  namespace (`docs/testing-and-build.md:10`).
- Estimate: 2–3 h.

### S2 — `PATCH /api/cards/{id}/position`, `card.ps1 reorder`, API docs

- `CardService.PlaceAsync(Guid id, PlaceCardRequest, ct)`: Decisions 3, 4, 6. Neighbour refs
  resolve through `ResolveCardIdAsync` with a `CardScopeContext` fenced to the moved card's board.
  Renumber helper `RenumberCell(List<Card> cellInOrder, DateTime now)` shared with S4.
- `CardEndpoints`: the route, mapped after `/{id}/content`; error codes `card_order_stale` (409)
  and `card_position_unreachable` (422) through the existing exception middleware shapes.
- `card.ps1`: `reorder` verb, `-Before/-After/-Top/-Bottom`, `pos n` in `Write-CardLine`, header
  comment. ASCII.
- `docs/antiphon-api.md`: route row + a "Position (CARD-0098)" paragraph under "Importance,
  urgency, and rank"; `docs/agent-card-lifecycle.md` "Importance and urgency": a **Position**
  bullet (what clears it, that dispatch reads it).
- Tests: `CardReorderIntegrationTests` (new; `[NotInParallel("CardReorder")]`, temp-root scoped
  like `CardCorrectionIntegrationTests`): before/after/top/bottom; dense `1..n` after each; stale
  neighbours → 409; token mismatch → 409; neighbours' tokens unchanged; cross-cell adoption writes
  `Importance`/`Urgency`/`UrgentSince`/provenance and one `Reorder` revision with superseded
  values; own-cell edge rule; `dueAt` unreachable → 422; archived neighbour → 422; off-board ref →
  422; `Reorder` revision reason text. `CardReorderApiTests` (endpoint shapes, `CARD-nnnn` and `#n`
  refs). `CardCliE2ETests` (`reorder` round trip, `pos` in `get`).
- Estimate: 3–4 h.

### S3 — Client: drag on the board, Undo, history

- `npm install @dnd-kit/core @dnd-kit/sortable @dnd-kit/utilities` at their current stable
  versions (check `npm view <pkg> version` at build time; do not pin from this document).
- `boards.ts`: `CardDto.position`, `CardRevisionDto.position`, `PlaceCardRequest`,
  `usePlaceCard(boardId)` with optimistic `arrayMove` on both board-detail keys and revert on
  error; `boardShapeModel.orderCards` adds `position ?? MAX` after `rank` (test).
- `SortableCardList.tsx` (new): wraps a list of `CardRow`s in `DndContext` + `SortableContext`
  (vertical strategy), sensors: Pointer, Touch (delay 200 ms), Keyboard (`sortableKeyboardCoordinates`);
  `onDragEnd` computes the new neighbours and calls `usePlaceCard`. `CardListSection` renders it
  for each band (banded) or the column (not banded); `enabled={!state.isTerminal}`.
- `CardRow`: a `TbGripVertical` handle (left of the identifier; `aria-label="Reorder CARD-nnnn"`),
  `useSortable` styles; hidden on terminal columns and under the archived filter.
- Cross-cell drop notification with **Undo** (Decision 7); `MoveMenu`: *Move to top* / *Move to
  bottom*.
- `CardHistory`: render `Reorder` entries ("Placed before CARD-0094", axis change when present).
- Tests (`pwsh -File scripts/test-client.ps1 features/board`): `boardShapeModel.test.ts`,
  `SortableCardList.test.tsx` — drive the **KeyboardSensor** (focus handle, Space, ArrowDown,
  Space) and assert the PATCH body's `before`/`after`; jsdom cannot do pointer drags and the
  suite must not pretend it can — `CardRow.test.tsx` (handle present/absent),
  `CardHistory.test.tsx` (Reorder row), `MoveMenu` items. `npm run build` for the typecheck.
- Estimate: 4–6 h.

### S4 — Bulk order, `card.ps1 order`, the recipe

- `CardService.ReorderBoardCardsAsync` (Decision 5), `POST /api/boards/{id:guid}/card-order` in
  `BoardEndpoints`, `overrideHumanRatings` skip list.
- `card.ps1 order -Board <b> -OrderFile <p> -Reason … [-By …] [-OverrideHumanRatings]`.
- `docs/orchestration-loop.md` §Picking: "Reprioritising the backlog" recipe (§AI reprioritisation
  above); `docs/antiphon-api.md` route row.
- Tests: `BoardCardOrderIntegrationTests` (listed-first-per-cell, unlisted order preserved,
  axis changes + skip list, atomicity on a bad ref, revisions per listed card),
  `BoardCardOrderApiTests`, `CardCliE2ETests` (`order` from a file).
- Estimate: 2–3 h. Ships after S3; droppable to a follow-up card without breaking S1–S3.

### S5 — Backlog boxes drag (CARD-0094), droppable

- `BacklogSection`/`BacklogRow`: `SortableCardList` per box, enabled only when
  `boardsPresent(cards) === 1`; otherwise the handle is absent and the box header carries "reorder
  on the board" — a mixed-board box has no single server order to write.
- Tests: `BacklogSection.test.tsx` (single-board → handles; two boards → none).
- Estimate: 1–2 h.

### S6 — Rollout, browser check, close

- From the **main checkout** (worktree restarts refuse, exit 3): `dev-backup.ps1`, then
  `scripts/restart-apphost.ps1`; the migration applies on startup; confirm no `[ERR]`/`[FTL]`, and
  `GET /api/cards?boardId=` shows `position: null` on every card and today's order.
- Rebuild the client bundle (`client-mode.ps1 -Status` until current), browser-check through the
  browser-harness lane at 1280 and 375 px: drag within Someday, keyboard reorder, Move to top, a
  cross-cell drop with Undo, the History entry, and `card.ps1 reorder … -Top` reflected on the
  board without reload (SignalR).
- Close the card with the verdict and the before/after of one real reorder.
- Estimate: 1–2 h.

Total: roughly 13–20 h of agent time across six dispatches; S1–S3 are the card's floor (~9–13 h).

---

## CARD-0094 and CARD-0096

- **CARD-0094** (Review): no change to its plan. `groupBacklog` → `orderCards` picks up `position`
  in S3; box drag is S5 here, single-board only. Its card gets no edit.
- **CARD-0096** (Backlog): its selection order is this key, including `position`, and its batch
  must **not** re-sort or dedupe by anything else; "top N" is then exactly what the operator or an
  agent placed at the top. Its text still cites `OrderByDescending(c => c.Priority)`. A short
  addendum saying so is appended to CARD-0096 in this pass; CARD-0096's own Plan should read
  Decision 1 and the `OrderKey` signature before designing the countdown.

---

## Test matrix

| Slice | Server (TUnit, `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c98/ --treenode-filter …`) | Client (`pwsh -File scripts/test-client.ps1 …`) | E2E |
|---|---|---|---|
| S1 | `CardRankingOrderTests`, `OrchestratorServiceIntegrationTests`, `HomeTaskServiceIntegrationTests`, `CardTaskFileRendererTests`, `WorkflowDefinitionLoaderTests`, `CardCorrectionIntegrationTests` | — | `ContractSnapshotTests` compile |
| S2 | `CardReorderIntegrationTests` (new), `CardReorderApiTests` (new) | — | `CardCliE2ETests` |
| S3 | — | `boardShapeModel`, `SortableCardList` (new), `CardRow`, `CardHistory`, `MoveMenu` | `BoardE2ETests` open card unchanged |
| S4 | `BoardCardOrderIntegrationTests`, `BoardCardOrderApiTests` (new) | — | `CardCliE2ETests` |
| S5 | — | `BacklogSection`, `backlogModel` | — |
| S6 | — | — | browser check on the built bundle |

Run the named classes per slice; the namespace-wide chunked run once, after S4, and never as a
narrow slice's verify (`docs/testing-and-build.md:10`, CARD-0307).

## Sequencing and risks

- **Concurrent drags** on one cell are last-writer-wins on the renumber inside a transaction, with
  the adjacency check catching a stale client; there is no board-level version. Acceptable for one
  operator and an occasional agent; if two agents ever reprioritise the same board at once, the
  second sees 409s and re-reads.
- **`ImportanceProvenance = Human` on a drag-promoted card** means the tracker sync stops refreshing
  it from the issue label — the same thing a modal edit does today (CARD-0327). Stated in the
  History entry; not a new behaviour.
- **dnd-kit under jsdom**: pointer drags cannot be simulated; the tests drive the keyboard sensor.
  A test that "passes" a pointer drag in jsdom is testing nothing.
- **Bands collapse independently** (`CardListSection.tsx:56-62`): a v1 drag is scoped to its band,
  so a card cannot be dropped into a closed band. Cross-band is the modal's job until S5 proves the
  gesture.
- **Migration + snapshot by hand** (daemons lock `bin/`): copy the CARD-0327 file's shape; a
  mismatch between the migration and the snapshot surfaces as a phantom pending migration on the
  next `dotnet ef` run, so diff the snapshot against the entity change before committing.
- **CARD-0331** (land queue not restart-safe) is live: land each slice and confirm the land
  finished before any `restart-apphost.ps1` (memory: verify land completion before restart).

## Execution notes

- Build to an alternate output while the daemons hold `bin/`: `--property:OutputPath=bin-c98/`
  (forward slash), delete the `bin-c98` directories before finishing.
- Sort **ascending** by the key everywhere; a reorder never touches a neighbour's
  `ConcurrencyToken`; `Position` is cleared, never carried, across columns and cells. If a reviewer
  sees a `Position` written on create or import, that is scope creep — unplaced is the default.
- `card.ps1` stays ASCII.
