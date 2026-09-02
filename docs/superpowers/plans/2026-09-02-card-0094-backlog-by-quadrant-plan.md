# CARD-0094 — Backlog by quadrant on the Orchestrator page: four boxes, stacked on a phone, side by side on a desktop

**Date:** 2026-09-02 (Plan pass, task 567896e0 — design only; no production code changed, no tests run)
**Card:** CARD-0094 "Orchestrator screen: show outstanding Backlog cards grouped by priority, mobile
boxes vs a wider desktop layout"
**Builds on:** [`2026-09-02-card-0039-importance-urgency-axes-plan.md`](2026-09-02-card-0039-importance-urgency-axes-plan.md)
(the ranking model this card groups by), [`2026-09-02-card-0092-running-sessions-visibility-plan.md`](2026-09-02-card-0092-running-sessions-visibility-plan.md)
and [`2026-09-02-card-0093-delegations-active-board-history-plan.md`](2026-09-02-card-0093-delegations-active-board-history-plan.md)
(the two other changes to `/orchestrator` today), [`2026-09-02-card-0031-project-status-view-plan.md`](2026-09-02-card-0031-project-status-view-plan.md)
(the Home rail's *Up next*).

**Sources (verified this pass, head `1706af7c`, live 17202 on 2026-09-02):** CARD-0094, CARD-0039
(Done today, `eab26088` closes it), CARD-0092 S1–S2 (`bd589677`, `1706af7c`), CARD-0093's plan
(`dadc5d79`); `server/Application/Services/{CardRanking,CardService,OrchestratorService}.cs`,
`server/Domain/Enums/{CardImportance,CardUrgency,CardQuadrant}.cs`;
`client/src/features/orchestrator/{OrchestratorPage,OrchestratorPanel}.tsx` and their tests,
`client/src/features/attention/{DecisionsPanel.tsx,attentionVisuals.ts}`,
`client/src/features/delegations/{DelegationsBoard.tsx,DelegationsBoard.stories.tsx}`,
`client/src/features/board/{cardRanking.ts,boardShapeModel.ts,CardListSection.tsx,CardRow.tsx,
CardAxisBadges.tsx,BoardCard.tsx,MoveMenu.tsx,BoardPage.tsx}`, `client/src/api/boards.ts`,
`client/src/hooks/useSignalRInvalidation.ts`, `client/src/test/setup.ts`,
`client/src/features/home/tasks/{homeTasksModel.ts,TasksSection.tsx}`,
`tests/Antiphon.E2E/ContractSnapshotTests.cs`, `docs/antiphon-api.md`,
`docs/agent-card-lifecycle.md`; live `GET /api/cards?status=Backlog`, `GET /api/boards`,
`GET /api/boards/{id}/columns`, `GET /api/home/tasks`, `GET /api/orchestrator/state`.

---

## Verdict up front

**Build it, client-only, about one day: a third section at the bottom of `/orchestrator`'s
default *Cards* tab, "Backlog", holding one box per `CardQuadrant` — stacked on a phone, four
across on a desktop — with the cards inside each box in rank order.** The Home rail does not
answer this ask and neither does the board page:

| Surface | What it shows for "outstanding Backlog by priority" today | Why it is not this card |
|---|---|---|
| Home *Up next* (`GET /api/home/tasks`, CARD-0002/0031) | Backlog cards by rank, **cap 8** per project (`GROUP_CAP.Next`, `homeTasksModel.ts:49`), then `+N more → open board`. Live: 68 *Next* items, 8 visible. | Per-project, one flat list, no grouping; the cap is the point of a rail. |
| Board page `/boards/:id` | The Backlog column; over 20 cards (`BAND_THRESHOLD`) it bands by quadrant with the first two bands open (`CardListSection.tsx:38-84`). | One board at a time; bands are a scroll aid that vanish under 21 cards; the column sits beside four others. |
| `/orchestrator` Cards tab (`OrchestratorPanel.tsx`) | Metrics, Running Sessions, Retry Queue. **Zero** backlog rows. | The surface the card names, still empty of this. |

**The stale premise, corrected.** The card says `Card.Priority` is a plain integer with values
0, 1, 2. That field no longer exists: CARD-0039 landed today (S1–S6, closing at `eab26088`).
A card now stores `importance` (`Low | Normal | High | Critical`) and `urgency`
(`Normal | Soon | Now`) plus an optional `dueAt`, and every read derives `effectiveUrgency`,
`quadrant` (`DoFirst | Schedule | Clear | Someday`) and `rank` (`13 − (3·importance +
2·effectiveUrgency)`, lower first) in `CardRanking.cs`, mirrored in `cardRanking.ts`. A request
that still sends `priority` is a 400. "Grouped by priority" therefore has exactly one honest
reading, and CARD-0039's own plan wrote it down in advance (§Decision 6): *"`quadrant` is the
cell, for the phone views and CARD-0094's grouping"*. Rank is the order inside a cell, not a
grouping key — it has twelve values and is not monotone across cells (`Normal/Now` = 6 sits in
*Clear*, `High/Normal` = 7 in *Schedule*; `docs/agent-card-lifecycle.md:31`).

**Does the quadrant read sensibly for "grouped by priority"?** Yes, and it is what the requester
already sees: the board page's Backlog column bands by the same four names (*Do first ·
Schedule · Clear · Someday*, `quadrantLabel`), so the Orchestrator boxes and the board bands are
one vocabulary. Importance alone (four levels) would ignore the axis that actually moves
(`dueAt` escalating urgency); rank alone is a number, not a name. Live shape today, all boards:

| | count | importance / urgency behind it |
|---|---|---|
| Do first | **0** | nothing is rated both important and urgent |
| Schedule | 6 | six `High/Normal` cards, all on the Antiphon board (rank 7) |
| Clear | **0** | no card carries `Soon`/`Now` or a `dueAt` yet |
| Someday | 61 | 54 `Normal/Normal` (rank 10) + 7 `Low/Normal` (rank 13) |

Two of four boxes are empty today, and that is a finding, not a layout problem: on a page named
for dispatch, "nothing is both important and urgent" is the headline an operator wants to see
at a glance. The design keeps all four boxes always, in fixed order, and makes an empty one cost
one line (§Decision 3).

### The card's five questions, answered

1. **Desktop layout** — four equal columns side by side, one per quadrant, each a capped
   scrollable list; a phone gets the same four boxes stacked; a tablet gets a 2×2. One
   `SimpleGrid cols={{ base: 1, sm: 2, lg: 4 }}`, the exact split `DelegationsBoard.tsx:169`
   already uses for its lanes on the tab next door. Not a single sorted list with headers (that
   is the board column, and the ask says boxes); not a table (a table cannot be "one box per
   priority" on a phone without becoming a different component).
2. **Which boards** — every board, one request, no filter. `GET /api/cards?status=Backlog`
   (`useCards({ status: 'Backlog' })`) is fleet-wide by construction, exactly as the Decisions
   tab already fetches `status=NeedsDecision`. A row shows its board name when the list spans
   more than one board. Live: 67 cards on 2 of 10 boards (Antiphon 60, Gym Stat 7). "Boards the
   orchestrator can dispatch into" is not a meaningful narrowing: the tick dispatches only from
   **active** columns (`OrchestratorService.cs:750`), and on both live boards only *In Progress*
   is active — Backlog is a picking list for a human or an orchestrator, never the tick's queue.
3. **How many, and past that** — per box, the first `BACKLOG_BOX_CAP = 12` in rank order, then a
   `Show all N` toggle that expands that box into a `maxHeight` scroll; fleet-wide the server
   already caps the list at `Cards:MaxListResults` (500) and reports `truncated`, which the
   section header surfaces as one sentence. No pagination, no virtualisation: 61 plain rows is
   nothing, and 500 is the ceiling.
4. **Live updates** — free. The query key is `['cards','list',{status}]`, which SignalR's
   `CardChanged` and `RunAttemptChanged` handlers already invalidate
   (`useSignalRInvalidation.ts:75,89`), as do every card mutation in `boards.ts`. No polling,
   no new event, no SignalR work. A card moved anywhere refetches ~80 KB (measured 81,535 bytes
   for 67 summary rows); acceptable, and `updatedSince` exists if it ever is not.
5. **Relationship to the board view** — the same rows, a different cut: cross-board, one
   status, bands always on. No new endpoint. A row click opens the card **on its board**
   (`/boards/{boardId}?card={cardId}`, the `targetOf` convention `attentionVisuals.ts:301`;
   `BoardPage.tsx:97` reads `?card=`), so the modal, history and thread stay where they live.

---

## Decisions

1. **Group by `quadrant`, order by rank inside; four boxes, always, in `QUADRANT_ORDER`.** The
   group key is the card's own `quadrant` field (server-derived; the client mirror exists only
   for fixtures). The order inside a box is `orderCards` from `boardShapeModel.ts:121` —
   rank, then earliest `dueAt`, then oldest `createdAt`, the same tie-break the tick uses
   (`OrchestratorService.cs:787-789`). `orderCards` is module-private today; export it rather
   than write a second sort. `quadrantBands` is **not** reused: it drops empty cells on purpose
   (a scroll aid inside a column), and here an empty cell is the message.

2. **Location: the bottom of the *Cards* tab, rendered by `OrchestratorPage`, not inside
   `OrchestratorPanel`.** The user's words are "at the bottom" of the Orchestrator screen, and
   the default tab is that screen. The tab then reads top-down as one pipeline: running now →
   retrying → still to pick up. Not a fifth tab: CARD-0093 is about to add `?tab=history`, the
   page's contract is one question per tab, and "what is the fleet doing with cards" already
   owns this tab — a tab would hide the backlog behind a click the user did not ask for. The
   section mounts from the page (`<Tabs.Panel value="cards"><Stack><OrchestratorPanel />
   <BacklogSection /></Stack>`) for two reasons: its loading/error state must not blank the
   Running Sessions table, and `OrchestratorPanel.test.tsx` registers only
   `/api/orchestrator/state` under MSW's `onUnhandledRequest: 'error'`
   (`client/src/test/setup.ts:39`) — the section's requests would fail its three tests for no
   gain. `keepMounted={false}` already means the fetch happens only on the Cards tab.

3. **Every box renders; an empty box is a header and one hint line.** Box header: label,
   count badge, and a dimmed hint that says what the cell *means* — *Do first* "important and
   urgent", *Schedule* "important, not yet urgent", *Clear* "urgent, not important", *Someday*
   "neither, yet". Empty body: "Nothing here." On a phone that is ~48 px per empty box; on a
   desktop the empty column stays as the visible zero. The hint is what makes "Clear" (CARD-0039
   chose it over Eisenhower's "Delegate" because that word is taken here) legible without a
   legend.

4. **A `BacklogRow`, not `CardRow` and not `BoardCard`.** `CardRow` needs a `boardId` and the
   board's `columns` for its kebab and renders labels, stage and live markers — noise for a
   Backlog card (live: 0 with sessions, 0 assigned, 0 with a stage). `BoardCard` is the tile the
   all-boards page keeps for its status buckets. The row here is one line: short identifier
   (`displayIdentifier`), title (1 line; 2 lines with the title dropped under the identifier in
   the stacked phone layout, the `CardRow` pattern), `CardAxisBadges` (badges only when the axis
   is not the default — inside *Schedule*, `High` vs `Critical` is the one thing worth a chip),
   external-issue tag, board name chip (only when the list spans more than one board), age in
   days. Click and Enter/Space open the card on its board. `data-testid="backlog-row-CARD-nnnn"`.

5. **The verb is "move it", via `MoveMenu`, in S2 — droppable.** On the page named for dispatch
   the natural next action on a Backlog row is *Move to In progress* (with or without spawn).
   `DecisionsPanel.tsx` already shows the shape: `useBoardColumnsFor(boardIds, cards.isSuccess)`
   for the columns, then `<MoveMenu boardId card columns />` per row. Live that is two column
   queries, cached. It is its own slice so it can be dropped if the kebab crowds the phone row;
   without it the section is still complete against the card's ask.

6. **Cap 12 per box, not the rail's 8.** The rail shares a page with four other groups; this
   section is the bottom of a page and four boxes wide, so a box can afford twelve rows before
   it needs a toggle. `Show all 61` expands one box to `maxHeight: 560, overflowY: auto` (the
   lane height on the Delegations tab); `Show fewer` collapses it. State is per box and resets
   on remount — nothing to persist.

7. **Fleet truncation is stated, never silent.** `CardListDto.truncated` (server `Take(limit+1)`,
   `CardService.cs:179-189`) renders as "Showing the 500 most recently updated cards; open the
   board for the rest." Not reachable today (67), but the header must not count 500 as the
   backlog when it is not.

8. **No server change.** `GET /api/cards?status=` returns the summary representation
   (`ToSummaryDto`, `hasMore: true` on all 67 because descriptions are cut to
   `SummaryPreviewChars`) ordered by `UpdatedAt desc`; the client re-sorts. Extending
   `GET /api/orchestrator/state` was rejected: CARD-0092 pinned it as the *session* snapshot
   ("one projection, not three"), and card data already has its own cache and invalidation.

9. **No collision with CARD-0092 or CARD-0093.** CARD-0092 changed the Running Sessions table,
   the *Running* metric subline and the caption under the header, and listed "CARD-0094
   (backlog by priority on this page)" under what it does not do. CARD-0093 adds a History
   tab and reshapes the Delegations tab, links and API docs; it does not touch the Cards tab.
   The only shared file is `OrchestratorPage.tsx`, where this card edits the `cards`
   `Tabs.Panel` body and CARD-0093 adds a tab and a panel — a trivial merge, but if the two
   build at the same time the second one takes `-Worktree`.

---

## Ground truth (checked, not guessed)

| Claim in the card | Today | Consequence |
|---|---|---|
| `Card.Priority` is a plain integer, values 0/1/2 | Gone. `CardImportance`/`CardUrgency`/`CardQuadrant` enums, `CardRanking.Rank`, wire names not numbers; `priority` in a request body → 400 (`docs/antiphon-api.md` §Importance, urgency, and rank) | Group by `quadrant`, order by `rank` |
| CARD-0039 is an open backlog card | Done today; `eab26088` is its S6 | "Group by the existing integer if 0039 hasn't landed" is moot |
| Orchestrator page has no backlog view | Still true at `1706af7c`; the Cards tab is metrics + Running Sessions + Retry Queue | Build |
| Backlog is what the orchestrator dispatches | No. Eligibility is `BoardColumn.IsActive && !IsTerminal` (+ unowned, unheld, no live session, retry gate); both live boards mark only *In Progress* active | The section is a picking list; copy must not say "queued for dispatch" |
| Many boards | 10 boards; Backlog cards on 2 (Antiphon 60, Gym Stat 7) | Board chip on rows when >1 board present; no filter in v1 |
| Unbounded growth risk (Q3) | 67 today; server cap 500 with `truncated` | Cap 12 per box + expand; header states truncation |
| Needs SignalR? (Q4) | `CardChanged`/`RunAttemptChanged` already invalidate `['cards','list']` | Nothing to add |
| Home *Up next* already covers it? | Cap 8 per project, one flat list, `+60 more → open board` live | Not covered |
| Board bands already cover it? | Per board; only over 20 cards; empties dropped | Not covered |

Live `GET /api/cards?status=Backlog` on 2026-09-02: 67 cards, `truncated: false`, 81,535 bytes;
importance High 6 / Normal 54 / Low 7; urgency and effective urgency all `Normal`; `dueAt` none;
ranks 7 ×6, 10 ×54, 13 ×7; labels on 31; external issue on 1; sessions, assigned agent and
`autoDispatchHeldAt` on none.

---

## Slices

Sequential, one Code worker, client-only. Shared workspace is fine unless CARD-0093's build is
running at the same time (§Decision 9).

### S1 — Model, section, page wiring, tests

**Files:**
- `client/src/features/board/boardShapeModel.ts` — `export` the existing `orderCards`; no other
  change.
- `client/src/features/orchestrator/backlogModel.ts` (new) —
  `BACKLOG_BOX_CAP = 12`; `QUADRANT_HINTS: Record<CardQuadrant, string>`;
  `groupBacklog(cards: CardDto[]): BacklogBox[]` returning all four cells in `QUADRANT_ORDER`
  (from `cardRanking.ts`), each `{ quadrant, label, hint, cards: orderCards(cell) }`;
  `boardsPresent(cards): number`.
- `client/src/features/orchestrator/BacklogSection.tsx` (new) — `useCards({ status: 'Backlog' })`,
  `useBoards()` (for names; already cached by the Decisions tab), `useMediaQuery('(max-width:
  48em)')` for the stacked row layout. Header: `Title order={4}` "Backlog", badge
  `N outstanding`, dimmed `on B boards`, the truncation sentence when `truncated`, a refresh
  icon (the panel's `TbRefresh` pattern). Body: `SimpleGrid cols={{ base: 1, sm: 2, lg: 4 }}`
  of `BacklogBox` (`Paper withBorder p="xs"`, `data-testid="backlog-box-<quadrant>"`), each
  with its capped rows and `Show all N` / `Show fewer`. Loading: one `Loader`; error: an
  `Alert` inside the section only.
- `client/src/features/orchestrator/BacklogRow.tsx` (new) — §Decision 4; `useNavigate()` to
  `/boards/${card.boardId}?card=${card.id}`.
- `client/src/features/orchestrator/OrchestratorPage.tsx` — the `cards` panel becomes
  `<Stack gap="lg"><OrchestratorPanel /><BacklogSection /></Stack>`.

**Tests:**
- `backlogModel.test.ts` — four cells always present and in order; rank then `dueAt` then
  `createdAt` inside a cell; a cell with no cards is `[]`, not absent; `boardsPresent`.
- `BacklogSection.test.tsx` (MSW `/api/cards`, `/api/boards`) — four boxes with counts; empty
  box shows "Nothing here." and its hint; 14 Someday cards → 12 rows + `Show all 14` → 14 rows
  → `Show fewer`; board chip present only when two boards are in the list; `truncated: true`
  renders the sentence; row click navigates to `/boards/<id>?card=<id>` (assert
  `window.location`); error response → alert with `data-testid="backlog-error"`.
- `OrchestratorPage.test.tsx` — add `http.get('/api/cards', …)` to `serve()` (it would
  otherwise error under `onUnhandledRequest: 'error'`) and count its calls; new cases "the
  cards tab renders the backlog section under the retry queue" and "the backlog request is
  deferred while another tab is open" (mirror of the existing delegations deferral test).

**Verify:** `pwsh -File scripts/test-client.ps1 features/orchestrator`; `npm run build`
from `client/` for the typecheck. Estimate 0.5 d.

### S2 — Row action: MoveMenu (droppable)

**Files:** `BacklogSection.tsx` (`useBoardColumnsFor(boardIds, cards.isSuccess)` and a
`columnsByBoardId` map, the `DecisionsPanel.tsx:27-33` shape), `BacklogRow.tsx` (a trailing
`<Box onClick={stopPropagation}><MoveMenu boardId card columns /></Box>` exactly as
`CardRow.tsx:104-106`; hidden until the board's columns have loaded).

**Tests:** `BacklogSection.test.tsx` — with `/api/boards/{id}/columns` served, the kebab is
present and `Move to` lists the legal targets for a Backlog card; without columns, no kebab
and the row still opens. `OrchestratorPage.test.tsx` `serve()` gains the columns handler.

**Verify:** same commands. Estimate 0.25 d.

### S3 — Contract fixture, story, screenshots, docs, browser check

**Files:**
- `tests/Antiphon.E2E/ContractSnapshotTests.cs` — one `SnapshotAsync(app,
  "/api/cards?status=Backlog", "cards-backlog.json", …)` beside the `home-tasks.json` capture
  (`:804`), seeded so the fixture has cards in at least three cells (one `High/Now`, one
  `High/Normal`, several `Normal/Normal`, one `Low/Normal` with a `dueAt` inside 14 days to
  exercise *Clear*) on two boards. Stories must seed from contract fixtures only
  (`DelegationsBoard.stories.tsx:8-11`); this is the only server-side touch and it is a test.
- `client/src/features/orchestrator/BacklogSection.stories.tsx` (new) — `Desktop` and `Mobile`
  (viewport `mobile1`) stories seeded through `boardKeys.cardsFor({ status: 'Backlog' })` and
  `boardKeys.all`, `Date.now` pinned as the delegations story does.
- `docs/ui-screenshots/` — `npm run screenshots` from `client/`; add the two images to the
  README.
- `docs/antiphon-api.md` route map — one line for the list endpoint that is missing today:
  `GET /api/cards  (?boardId=|status=|updatedSince=, at least one) → { cards, truncated }`,
  summary representation, `Cards:MaxListResults` cap. `docs/ops-http.md` already tells agents
  the rule; the route map should match it.
- Browser check on 17203 after the watcher rebuilds (`client-mode.ps1 -Status`): desktop four
  columns; a 390 px viewport shows four stacked boxes with *Do first* and *Clear* as one-liners.

**Verify:** `dotnet run --project tests/Antiphon.E2E --property:OutputPath=bin-card0094/ --
--treenode-filter "/*/*/ContractSnapshotTests/*"`; `pwsh -File scripts/test-client.ps1
BacklogSection`; delete every `bin-card0094` directory. Estimate 0.25–0.5 d.

---

## What this card does not do

- **A board filter, a project filter, or a search box** on the section. Two boards carry
  Backlog cards today; the chip and `/boards` cover it. Left open.
- **Any server change** — no endpoint, no `view=`, no orchestrator-state field, no SignalR
  event. The list endpoint, its cap and its invalidation all exist.
- **Grouping by importance or by rank value**, or a 2×2 desktop matrix. §Verdict and
  §Decision 1; a 2×2 gives half the width to two cells that are empty today.
- **Changing the board page's bands** (`BAND_THRESHOLD`, first-two-open) or the Home rail's
  *Up next* cap. CARD-0002/0031 own those.
- **Making this a tab, or the default view**, or reordering the existing tabs.
- **"Why has it not started"** — CARD-0031 Decision 4 / CARD-0304's `queueReason` are about
  delegated tasks; a Backlog card has no queue reason because it is not queued.
- **Drag-to-reorder or a stored ordinal** — CARD-0098.
- **Bulk re-rate from the section.** `card.ps1 edit` with a reason, or the card modal.

## Left open, deliberately

1. **Cap and expand behaviour** if a box ever routinely holds hundreds of rows — swap the
   expanded body for the `VirtualTaskLane` pattern from `DelegationsBoard.tsx`; not before.
2. **A per-board or per-project filter** once a third board grows a real backlog.
3. **The Cards tab on a 390 px phone** already scrolls two `miw` tables sideways, and
   CARD-0093 adds a fifth tab to `Tabs.List`. Neither is this card's; if the tab strip wraps
   badly it is a one-line `Tabs.List` change for whoever notices first.
4. **`Critical` is still assigned to no card** (CARD-0039 S6's hand re-rate list). When it is,
   *Do first* fills and the box order earns its place; nothing here depends on it.

---

## Test matrix

| Layer | Test | Command |
|---|---|---|
| Client model | `backlogModel.test.ts` (new) | `pwsh -File scripts/test-client.ps1 backlogModel` |
| Client component | `BacklogSection.test.tsx` (new), `BacklogRow` via the section | `pwsh -File scripts/test-client.ps1 BacklogSection` |
| Client page | `OrchestratorPage.test.tsx` (+2, `serve()` gains `/api/cards`) | `pwsh -File scripts/test-client.ps1 OrchestratorPage` |
| Client regression | `OrchestratorPanel.test.tsx`, `DecisionsPanel` tests unchanged | `pwsh -File scripts/test-client.ps1 features/orchestrator features/attention` |
| Board export | `boardShapeModel.test.ts` unchanged (export only) | `pwsh -File scripts/test-client.ps1 boardShapeModel` |
| Contract (S3) | `ContractSnapshotTests` captures `cards-backlog.json` | `dotnet run --project tests/Antiphon.E2E --property:OutputPath=bin-card0094/ -- --treenode-filter "/*/*/ContractSnapshotTests/*"` |
| Screenshots (S3) | `Orchestrator/Backlog — Desktop`, `— Mobile` | `npm run screenshots` in `client/` |

Never read a Bash pipeline's exit code for the Vitest runs; use the script.

## Sequencing, estimate, risks

**Order:** S1 → S2 → S3, one landing each. **Estimate:** S1 0.5 d, S2 0.25 d, S3 0.25–0.5 d;
about 1–1.25 d total.

| Risk | Disposition |
|---|---|
| Every fleet-wide `CardChanged` refetches ~80 KB | Measured; fine at today's rate. `updatedSince` and `staleTime` are the two knobs if it is not |
| `OrchestratorPage.test.tsx` and `OrchestratorPanel.test.tsx` break on the new request | Decision 2: mount from the page, add the handler to `serve()` in S1 |
| Two empty boxes look like a bug | Decision 3: header + hint + "Nothing here." on every box; the story's fixture fills three cells so the screenshot shows both states |
| The server order (`UpdatedAt desc`) leaks into a box | `groupBacklog` always sorts; the model test pins rank-then-created |
| `MoveMenu` kebab crowds the phone row | S2 is droppable; the section is complete without it |
| Merge with CARD-0093 in `OrchestratorPage.tsx` | Same file, different blocks; `-Worktree` for whichever builds second |
| `hasMore: true` rows have no full description | The row never renders it; the card modal on the board does |

## Execution notes

- Build the client with `npm run build` from `client/` for the typecheck; server tests only in
  S3, to `--property:OutputPath=bin-card0094/` (forward slash), and delete the `bin-card0094`
  directories before finishing.
- Read `quadrant` and `rank` off the DTO; do not recompute them with `cardRanking.ts` in the
  section (the mirror exists for fixtures and optimistic renders).
- Copy: the section is "outstanding", never "queued" or "scheduled for dispatch" — Backlog is
  not an active column on any live board.
- Keep `OrchestratorPanel.tsx` byte-identical; its tests rely on its empty-state strings.
- When closing the card, record on the move revision: "built as the Backlog section at the
  bottom of `/orchestrator`'s Cards tab, grouped by CARD-0039's quadrant (Do first · Schedule ·
  Clear · Someday), rank order inside, `SimpleGrid base 1 / sm 2 / lg 4` for phone vs desktop;
  `Card.Priority` no longer exists, so 'by priority' means by quadrant."
