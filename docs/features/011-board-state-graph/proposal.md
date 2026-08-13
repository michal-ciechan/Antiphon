# 011 — Board state graph: the shape of the work

**Status: implemented** 2026-08-13 — see §11 for what shipped and what did not
**Card**: CARD-0042 · **Story**: S7 in `docs/product/user-stories.md`
**Replaces**: the stacked-column kanban rendering of `client/src/features/board/BoardPage.tsx`
(single-board view). The all-boards view and `CardModal` are touched only lightly (§6).

## Summary

The single-board page stops being columns of stacked tickets and becomes two things:

1. **Desktop — a shape strip**: a graph of the board's states across the top — one node per
   column, carrying count, priority mix, oldest-card age and live-session signal — with the card
   **list** beneath it, grouped by state, filtered by clicking a node. The shape of the work is
   legible before any card is read.
2. **Mobile — one state at a time**: a state pager. You see a single state's cards full-width,
   with the states that usually feed it entering at the top and the states it usually leads to
   leaving at the bottom. Navigating the board is moving along the flow, not scrolling a wall
   sideways.

Identity: cards render as **`#41`** everywhere in the UI — a *display form* of the stored
`CARD-0041`, not a new identifier scheme (§4 answers the question the card asks).

Real numbers used throughout (the board as fetched on 2026-08-13):
**42 cards — Backlog 32 (8 P0 · 10 P1 · 12 P2 · 2 P3), In Progress 0, Review 4, Done 6.**
The empty state (In Progress) and the overloaded state (Backlog, 30+) are not hypothetical; they
are the board today.

---

## 1. What exists today (verified against the code, 2026-08-13)

### Server

- **The read path** is `BoardService.GetByIdAsync` → `LoadBoardAsync` + `ToDetailDto`
  (`server/Application/Services/BoardService.cs:49-53, 119-152, 197-210`). (The card names
  `GetDetailAsync`; the method is `GetByIdAsync`, shaping `BoardDetailDto`.) It loads the whole
  board: columns ordered by `ColumnOrder`; cards grouped per column, ordered **priority desc,
  then `CreatedAt` asc** (`:123`); every `CardDto` carries the **full description** and **every
  session** the card ever had (`:154-195`).
- **Payload weight, measured**: 122,861 bytes for today's 42 cards, of which description text is
  81,551 chars (~66%). One `GET /api/boards/{id}` is the whole surface's data.
- **Columns are the states.** `BoardColumnDto` carries `StateKey`, `Name`, `ColumnOrder`,
  `CardStatus`, `IsActive`, `IsTerminal` (`server/Application/Dtos/BoardDtos.cs:29-38`). A card's
  `Status` is copied from its column on every move (`CardService.ApplyColumnMove`,
  `server/Application/Services/CardService.cs:230-269`).
- **Default boards have four columns** — backlog / in-progress / review / done
  (`BoardService.CreateDefaultColumns`, `:212-221`) — but `CardStatus` has six members
  (`Blocked = 4`, `Canceled = 5`, `server/Domain/Enums/CardStatus.cs`). On a board without a
  Blocked or Canceled column those statuses are **unreachable**: there is no column to move a card
  into. The Antiphon board is a default board; it has never had a Blocked or Canceled card.
- **The state machine is fully connected between live states** (commit `b48f928`,
  `server/Domain/StateMachine/CardStateMachine.cs`): any live state moves directly to any other;
  Done and Canceled are terminal with **no transitions out** (reopen is deferred to CARD-0019's
  revision history). The file's comment records why: a forced path is not just ceremony —
  **moving a card into an `IsActive` column spawns an agent** (`ApplyColumnMove` stamps
  `StartedAt`; the spawn is the documented CARD-0040 constraint), so the old
  Backlog→InProgress→Review→Done-only table made a bookkeeping move launch six unwanted agent
  sessions on 2026-08-13.
- **A move carries a `Reason`** (`MoveCardRequest`, `BoardDtos.cs:92-106`). Today it persists
  only into `TerminalReason` on a terminal move and is dropped otherwise, pending CARD-0019's
  `CardRevision`.
- **No transition history exists.** Nothing records when a card entered its current state. The
  timestamps available are `CreatedAt`, `UpdatedAt` (bumped by *any* write, including concurrency
  token churn), `StartedAt` (first move into an active column), `CompletedAt`. This bounds what
  the graph can honestly encode (§3).
- **`CardService.NextIdentifierAsync` is count-based** (`CardService.cs:271-275`):
  `CARD-{count+1:0000}`, scoped **per board**. CARD-0005 documents the live bug: delete a card
  and the next created card reuses the freed identifier.

### Client (`client/src/features/board/`, ~3,250 lines incl. ~1,200 of tests)

- **Routes**: `/boards` and `/boards/:id` both render `BoardPage` (`client/src/App.tsx:87-106`).
  The only query param is `?card=<cardId>`, which opens `CardModal`
  (`BoardPage.tsx:71, 126-140`). Board switching is path-based.
- **The single-board view** (`BoardPage.tsx`, 446 lines) renders columns as a
  `Group wrap="nowrap"` inside a `ScrollArea` (`:233-241`); each column
  (`BoardColumn.tsx`, 47 lines) is a `Paper` with hardcoded `minWidth: 280` and
  `height: calc(100vh - 180px)`.
- **The card tile** (`BoardCard.tsx`, 79 lines) shows: `identifier`, `title` (clamp 2),
  `description` (clamp 2), `P{n}` badge, first **2** labels, and a green left border + terminal
  icon when `OwnerSessionId` is set. It does **not** show status, assigned agent, queue position,
  workflow stage, sessions, or any timestamp.
- **Drag-and-drop is the only move surface.** `@dnd-kit/core`, handle-only drag
  (`BoardCard.tsx:45-56`), no within-column sort; drop → `useMoveCard` →
  `PATCH /api/cards/{id}` with `{boardColumnId, concurrencyToken}`, optimistic with rollback
  (`client/src/api/boards.ts:261-288`). **`CardModal` has no move action at all** — no Reason can
  be supplied from the UI today, and no move is possible except by dragging.
- **`CardModal`** (197 lines) is fullscreen, opened purely via `?card=`; tabs Sessions / Diff
  (only when a worktree exists and status is Review or Done) / Details; actions: agent picker +
  Spawn, diff comments, Open PR (Done only). No edit, no move, no approve.
- **There is no filtering, search, or sorting anywhere on the board page.** None. The only
  searchable inputs are entity pickers (board / agent / session selects).
- **Freshness is push-only**: zero polling in `api/boards.ts`; SignalR events `BoardChanged`,
  `CardChanged`, `RunAttemptChanged`, `AgentQueueChanged`, `SessionStarted/Exited/Finished`,
  `WorkflowReloaded` invalidate the board queries (`client/src/hooks/useSignalRInvalidation.ts`).
- **Mobile**: the board has no breakpoints — columns overflow horizontally on a phone. The only
  responsive CSS in the feature is `CardModal.css`'s 900px collapse. (The app shell itself is
  responsive: burger + drawer below `sm`, `shared/Layout.tsx:169-204`.)
- **The all-boards view** (`/boards` with no id) flattens every board's cards into **six
  synthetic status buckets** — including Blocked and Canceled — hardcoded at
  `BoardPage.tsx:45-58`, with drag rendered but silently inert (no `onDragEnd`, `:320`).

### The problem, in the data

The stacked-column rendering gives Backlog's 32 cards ~8 visible tiles in a 280px column and
gives the *shape* — 0 In Progress while agents run, a Review card sitting 11 days, 8 P0s buried
in Backlog — no rendering at all. S7's complaint is this exact board.

---

## 2. UX design

### 2.1 Desktop — the shape strip

The top of `/boards/:id` becomes a horizontal strip of **state nodes**, one per column, in
`ColumnOrder`, connected by edges along the usual flow. The card list (§2.3) sits beneath it.

Each node encodes, top to bottom:

| Element | Encoding | Source (all in `BoardDetailDto` already) |
|---|---|---|
| State name + flags | text; `●` suffix on `IsActive` columns, `⊗` on `IsTerminal` | `BoardColumnDto.Name/IsActive/IsTerminal` |
| **Count** | a large numeral — the primary encoding | `Cards.length` |
| Priority mix | mini stacked bar, P0→P3 left-to-right, 2px gaps, P0 darkest | `CardDto.Priority` |
| Signal line | worst-case fact for this state (see below) | derived |

Count is a **numeral, not an area or bar height**. At 0–50 cards a number is read faster and
more precisely than comparative area, and area would imply a precision of comparison nobody
needs. The priority bar under it is the "shape within the shape": Backlog's problem today is not
32, it is *8 of the 32 are P0*.

The signal line is one derived fact per state, chosen by rule:

- Empty state → `—` (the node stays rendered; absence is information).
- Any card with a live session (`Sessions[]` with `Status` running-ish, or `OwnerSessionId`
  set) → `● n running`.
- Otherwise → `oldest #id · nd` — the card with the earliest `CreatedAt` still in this state.
  This is honest: it is *card age*, not *time in state* — time in state is not derivable until
  CARD-0019's revisions exist, and the design must not pretend otherwise (§3).

**Edges are a presentation of the usual flow, not the rule.** Since `b48f928` the state machine
is fully connected between live states, so drawing Backlog→InProgress→Review→Done as *the*
paths would be a lie by implication — and drawing the true graph (every live state to every
other: 20 directed edges over 4 nodes, 30 over 6) is unreadable and, worse, we have **no
transition data to weight it** (nothing records moves). So: a single spine of muted arrows in
`ColumnOrder`, and a permanent caption under the strip:

> *edges show the usual path — any card can move directly between any live states*

**Interaction**: clicking a node filters the list below to that state and sets `?state=review`
(shareable). Clicking again clears it. Nothing else on the node is clickable — the node is a
lens, not a menu.

**Color** carries state identity redundantly, never alone (every node is name-labeled, every
badge worded): Backlog gray, In Progress blue, Review amber, Done green — the same hue
vocabulary the client already uses for statuses, so nothing changes meaning. Amber/green are a
known CVD-weak pair; the name labels and fixed positions are the required secondary encoding.

```
┌──────────────────────────────────────────────────────────────────────────────────────────────┐
│  Antiphon ▾                                       [⌕ search cards… ]      [+ Card] [⚙ WF]   │
├──────────────────────────────────────────────────────────────────────────────────────────────┤
│                                                                                              │
│   BACKLOG              IN PROGRESS ●          REVIEW                DONE ⊗                   │
│  ┌────────────┐       ┌ ─ ─ ─ ─ ─ ─┐        ┌────────────┐       ┌────────────┐             │
│  │     32     │──────▶│      0     │───────▶│      4     │──────▶│      6     │             │
│  │ ██▓▓▓▓░░░· │       │            │        │ ████       │       │ █▓░░       │             │
│  │ 8 P0       │       │     —      │        │ oldest     │       │ all closed │             │
│  │ oldest 3d  │       │            │        │ #1 · 11d   │       │ 2026-08-13 │             │
│  └────────────┘       └ ─ ─ ─ ─ ─ ─┘        └────────────┘       └────────────┘             │
│        edges show the usual path — any card can move directly between any live states        │
├──────────────────────────────────────────────────────────────────────────────────────────────┤
│  REVIEW · 4                                                                    (all states)  │
│  ────────────────────────────────────────────────────────────────────────────────────────    │
│  #1   Agent Screen - Working Label - Always Working              P0  ·  7 sessions  · 11d    │
│  #3   Surface launch and delivery errors instead of leaving a…   P0  reliability ui ·  3d    │
│  #16  A delegated task's model tier never reaches the agent i…   P0  bug delegation ·  3d    │
│  #18  First prompt races the pty-host connection and is lost…    P0  bug pty        ·  3d    │
│                                                                                              │
│  ▸ BACKLOG · 32          ▸ IN PROGRESS · 0          ▸ DONE · 6                               │
└──────────────────────────────────────────────────────────────────────────────────────────────┘
```

Default (no node selected): the list renders **all states as collapsible groups in column
order**, with the first non-empty *non-terminal* group after Backlog expanded — i.e. the state
most likely to need eyes. (Today that is Review.) The dashed border and `—` on In Progress is
the empty-state rendering; the node never disappears.

### 2.2 Desktop — the overloaded state (Backlog selected, 32 cards)

A selected node shows the full list for that state, banded by priority. Bands beyond the first
two collapse to a summary row when the state holds more than 20 cards; expanding is one click
and remembered per session. 30+ cards is a scroll of ~36px rows — roughly one screen — instead
of a 280px column stacking 4× the viewport.

```
├──────────────────────────────────────────────────────────────────────────────────────────────┤
│  BACKLOG · 32                                                        [clear filter ✕]        │
│  P0 — 8 ────────────────────────────────────────────────────────────────────────────────     │
│  #19  A card cannot be edited or deleted, so the record cannot…  P0  bug api blocker ·  3d   │
│  #20  Addendum to CARD-0003: the stall backstop cannot fire, a…  P0  reliability     ·  3d   │
│  #21  A task outlives its dead session, and a stolen transcrip…  P0  reliability     ·  3d   │
│  #22  Notice usage-limit exhaustion, show what's left and when…  P0  reliability     ·  3d   │
│  #29  A task handed to a reused warm delegate can be swallowed…  P0  delegation      ·  1d   │
│  #31  UX: a project status view that answers what is happening…  P0  ux home         ·  1d   │
│  #35  UX: a diagnostic view for work that is stuck               P0  ux reliability  ·  1d   │
│  #41  A compacted session reads Working forever - two post-com…  P0  session         ·  0d   │
│  P1 — 10 ───────────────────────────────────────────────────────────────────────────────     │
│  #2   Tasks section on the home rail - unified cards for board…  P1  ui home         ·  3d   │
│  #5   Card identifiers are reused after a delete                 P1  bug cards       ·  3d   │
│  #40  Cards never move themselves - automate the column transi…  P1  cards board     ·  0d   │
│  #42  UX: the board should show the shape of the work - state…   P1  ux board        ·  0d   │
│  … 6 more                                                                       [show all]   │
│  ▸ P2 — 12 cards ──────────────────────────────────────────────────────────────  [expand]    │
│  ▸ P3 — 2 cards  ──────────────────────────────────────────────────────────────  [expand]    │
└──────────────────────────────────────────────────────────────────────────────────────────────┘
```

Row anatomy: `#id` (§4) · title (clamp 1) · priority badge · first 2 labels · session/agent
indicator (green dot + agent name when `OwnerSessionId`/live session; replaces the tile's green
border) · workflow stage when `CurrentWorkflowStageName` is set · age. Description leaves the
row (it was clamp-2 noise on the tile); it lives in the modal.

### 2.3 Opening a card, and moving one

- **Open**: clicking a row sets `?card=<id>` and opens the existing `CardModal`, unchanged
  mechanism (`BoardPage.tsx:126-140`). Deep links keep working; `?state=` and `?card=` compose.
- **Move**: replacing columns removes drag-and-drop, and that is deliberate, not collateral:
  - A move now **carries a Reason** (`MoveCardRequest.Reason`) and drag has nowhere to put one.
  - Moving into an `IsActive` column **spawns an agent**; a gesture that easy for a side effect
    that big is how six accidental sessions nearly launched on 2026-08-13.
  - The kanban drag answered "which adjacent column", which the fully-connected machine has made
    obsolete anyway.

  Instead: a **Move to ▾** menu on each row's kebab and in the `CardModal` header — listing every
  legal target from the (trivial since `b48f928`) rule: any live state except the current; none
  from Done/Canceled. The menu opens a small popover: target state, optional **Reason** text
  field, and — when the target column `IsActive` — an explicit confirm line: *"moving here
  spawns an agent session"*. Submits the existing `PATCH /api/cards/{id}` with
  `{boardColumnId, concurrencyToken, reason}`. `CardModal` finally gets a move affordance, which
  it has never had.

### 2.4 Filtering and search

All client-side over the already-loaded `BoardDetailDto` (42 cards, 123 KB — no server round
trip is warranted):

- **Text search** (`⌕` in the header, `?q=`): matches identifier in *all* forms (`#41`, `41`,
  `CARD-0041` — §4), title, description, labels. Case-insensitive substring; no ranking.
- **Label filter** (`?labels=a,b`): multi-select over the board's label vocabulary (46 distinct
  labels today), AND semantics.
- **Priority filter** (`?p=0,1`).
- **State filter** = node click (`?state=`), as above.

Filters apply to **both** the list and the strip: a filtered node shows `n of m` (e.g. searching
"pty" renders Backlog as `3 of 32`) so the shape under filter stays honest. **Search and filters
never hide a node** — states are the fixed frame; only their contents vary. Empty result:
the list shows "no cards match" with the active filter chips and a one-click clear.

### 2.5 Mobile — one state at a time

Below the `sm` breakpoint the same route renders a **state pager** instead of the strip+list.
One state fills the screen; connectors to the usual-flow neighbours enter at the top and leave
at the bottom; horizontal swipe (or the connector taps) moves along `ColumnOrder`.

```
┌────────────────────────────┐    ┌────────────────────────────┐
│ ☰  Antiphon ▾          ⌕   │    │ ☰  Antiphon ▾          ⌕   │
│    ○────○────●────○        │    │    ○────●────○────○        │
│                            │    │                            │
│ ┌────────────────────────┐ │    │ ┌────────────────────────┐ │
│ │ ▲ from IN PROGRESS · 0 │ │    │ │ ▲ from BACKLOG · 32    │ │
│ └────────────────────────┘ │    │ └────────────────────────┘ │
│                            │    │                            │
│  REVIEW · 4                │    │  IN PROGRESS · 0        ●  │
│  ▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂  │    │                            │
│  #1  Agent Screen -        │    │      Nothing is in         │
│      Working Label -       │    │       progress.            │
│      Always Working        │    │                            │
│      P0 · 7 sessions · 11d │    │  Cards land here when a    │
│  ▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂  │    │  session starts on them —  │
│  #3  Surface launch and    │    │  moving a card here        │
│      delivery errors…      │    │  spawns an agent.          │
│      P0 · reliability · 3d │    │                            │
│  #16 A delegated task's    │    │                            │
│      model tier never…     │    │                            │
│      P0 · bug · 3d         │    │                            │
│  #18 First prompt races    │    │                            │
│      the pty-host…         │    │                            │
│      P0 · bug pty · 3d     │    │                            │
│                            │    │                            │
│ ┌────────────────────────┐ │    │ ┌────────────────────────┐ │
│ │ ▼ to DONE · 6          │ │    │ │ ▼ to REVIEW · 4        │ │
│ └────────────────────────┘ │    │ └────────────────────────┘ │
│      [ all states ⌗ ]      │    │      [ all states ⌗ ]      │
└────────────────────────────┘    └────────────────────────────┘
        Review (populated)            In Progress (empty)
```

- The **dot rail** under the header is position along the spine; the filled dot is the current
  state. Tapping a dot jumps.
- The top/bottom connectors are the spine neighbours **with their live counts** — the phone user
  keeps the shape in peripheral vision even though only one state is on screen.
- **Full-connectivity honesty**: the `all states ⌗` sheet shows every state with count and
  priority bar (the strip, vertically), and is also where a card's Move menu offers non-adjacent
  targets. The connectors are navigation along the usual flow; they are *not* the move rule, and
  nothing on this screen implies a card may only go where the connectors point.
- A state with 30+ cards uses the same priority-band collapse as desktop (§2.2).
- Row tap opens `CardModal`, which is already fullscreen and already has its 900px one-column
  collapse — it is the one part of today's board that is mobile-ready.
- Empty state: the page stays (the pager must not skip it — 0 In Progress is the finding), with
  one line of fact and one line of mechanism, as mocked.

`?state=` doubles as the pager position, so a phone link and a desktop link are the same URL.

### 2.6 Boards whose columns include Blocked / Canceled

Nodes come from `board.columns`, so a board that defines a Blocked or Canceled column gets its
node (and pager page) automatically — Blocked slots into the spine by `ColumnOrder`; terminal
columns render with the `⊗` mark and no outgoing edge. On default boards those states simply do
not appear, which is truthful: they are unreachable there (§1). The all-boards view keeps its
six synthetic buckets and is otherwise out of scope (§6).

---

## 3. What the graph encodes — and deliberately does not

**Encodes** (all derivable from `BoardDetailDto` today):

| Fact | From |
|---|---|
| Count per state | `columns[].cards.length` |
| Priority mix per state | `Priority` |
| Oldest card per state (age since creation) | `CreatedAt` |
| Live work (running sessions, owner claims) | `Sessions[].Status`, `OwnerSessionId` |
| Emptiness (rendered, never elided) | — |
| Filtered-vs-total (`n of m`) | client state |

**Deliberately does not encode:**

- **Time in state.** Not derivable: no transition history exists, and `UpdatedAt` is bumped by
  any write (including concurrency-token churn), so "sitting in Review for 11 days" cannot be
  distinguished from "moved to Review an hour ago after 11 quiet days". The signal line says
  *oldest card*, worded as age, until CARD-0019's `CardRevision` provides entered-state-at. This
  is the single most tempting dishonest number and the design refuses it.
- **Transition volumes / flow rates.** No moves are recorded. Edges are unweighted and stay so
  until there is data; when CARD-0019 lands, per-edge counts over a window become possible and
  the strip has a natural place for them.
- **Urgency vs importance.** CARD-0039 is not built; the priority bar renders the one number
  that exists. When 0039 ships two axes, the bar is the slot that changes (mix by urgency, badge
  by importance) — the node layout does not.
- **Delegated-task blockedness.** "The delegate asked a question" lives on `AgentTask`, a
  different pipeline with no board column (feature 010 §1). This surface renders **cards only**;
  mixing pipelines is the home rail's job (010), not the record's.
- **Cross-board shape.** The strip is per-board. The all-boards aggregation keeps its current
  rendering; a portfolio shape view is explicitly deferred (user-stories: "cross-project rollup
  is a different product").

**The graph's honesty depends on the states being maintained.** Today they are not: 0 In
Progress / 6 bookkeeping-moved Done is the record's known failure mode, and CARD-0040 (automatic
transitions) is what fixes the *data*. This surface neither depends on 0040 to function nor
fixes the lie itself — what it does is make the lie **visible** (an empty In Progress node while
agents demonstrably run is itself the finding). Ship order is independent; value order is 0040
first or soon after.

---

## 4. Identity: `#41` — answered, not assumed

**Decision: `#41` is a rendering of the stored identifier, not a new identifier.** Storage, API,
prompts, and prose keep `CARD-0041`. One shared client function derives the display form:

```
displayIdentifier("CARD-0041") = "#41"     // strip prefix, parse int, drop zero-pad
displayIdentifier(anything-else) = verbatim // non-matching identifiers render unchanged
```

- **Where it applies**: every UI surface — strip signal lines, list rows, `CardModal` header,
  the home rail (feature 010 renders `CARD-nnnn` today; it adopts the same function). One
  function in `client/src/shared/cardIdentifier.ts`, used everywhere, so the forms never drift.
- **Copy keeps the canonical form.** The row kebab and modal header get "Copy id" which yields
  `CARD-0041`, because the copied thing lands in commit messages and docs where grep against
  history must keep working. Hovering `#41` shows the full form.
- **Search accepts all forms** (§2.4): `#41`, `41`, `CARD-0041`, `card-41`.
- **Scope caveat, stated**: identifiers are **per-board** (`NextIdentifierAsync` counts within
  `boardId`), so `#41` is unambiguous only within a board surface. Cross-board surfaces (the
  all-boards view, the home rail) keep the board name adjacent (the all-boards view already
  prepends it as a label) — `#41` next to a board chip stays honest.

**Why not a stored short form or base36:**

- **The count-based allocator poisons any new scheme.** `NextIdentifierAsync` is `count+1`
  (CARD-0005: delete → reuse). Migrating identifiers on top of an allocator that reuses them
  would bake ambiguity into the new scheme's history from day one. **Fix CARD-0005 first and
  regardless**: max over parsed numeric suffixes + 1 — it is a prerequisite for `#41` being a
  *stable citation*, display-form or not. (CARD-0019's archive-instead-of-delete then keeps the
  sequence monotonic by construction.)
- **References in prose cannot be rewritten.** `CARD-nnnn` appears in commit messages,
  investigation docs, CLAUDE.md, card descriptions and terminal reasons ("fixed by CARD-0041").
  A stored-scheme change means every old reference needs a resolver forever. A display form
  costs none of that and is reversible by deleting one function.
- **Base36 density is not needed at any realistic scale.** 2-char base36 tops out at 1,295; the
  gain over decimal begins around card ~1300. The board has 42 cards after 4 days of "cards are
  the record" (most migrated in bulk). Even sustained heavy automation is years from 4 digits
  mattering, and `#1042` is still shorter-*spoken* than a base36 pair ("card ten forty-two" vs
  "card T-Q"). Revisit only with a real >1,000-cards signal, and then as its own migration with
  CARD-0019's history in place.

Server work in this feature's scope: **the CARD-0005 fix only** (parse-max+1 in
`NextIdentifierAsync`). No schema, no new identifier column.

---

## 5. Server projection

**None required for v1.** The strip, list, pager, filters and search are all computed
client-side from the existing `GET /api/boards/{id}` response — measured at 123 KB for 42 cards,
fetched once and kept fresh by the existing SignalR invalidation (no polling exists and none is
added). A pure `boardShapeModel.ts` derives per-state aggregates (count, priority mix, oldest
card, live sessions) from `BoardDetailDto`; it is the unit-testable core.

Priced but deferred:

- **A description-free card summary** (`?view=summary` or a lighter `CardSummaryDto`):
  descriptions are ~66% of the payload and the board page renders them only inside the modal.
  Worth doing when payloads reach ~1 MB (≈350 cards at today's description weight); not before —
  it forks the DTO and every consumer must choose a side.
- **A server-side `BoardShapeDto`** (aggregates only): needed only if boards reach thousands of
  cards or the strip is wanted on surfaces that must not pay for the card list (e.g. a future
  project cockpit widget). The client model's functions are written so their signatures survive
  that move.
- **Legal-move targets per card**: the client mirrors the now-trivial rule (any live state
  except current; none from terminal) rather than the server shipping `availableStatuses` per
  card. This is a deliberate lockstep pair with `CardStateMachine` — the same pattern as
  `TranscriptKinds` client/server — and a one-line comment on each side names the other.

---

## 6. What happens to the existing surface, and what this costs

| Surface | Change |
|---|---|
| `BoardPage.tsx` single-board view | Columns + DnD replaced by strip + list (desktop) / pager (mobile). The board picker, `+ Card`, workflow editor button and `?card=` mechanics stay. |
| `BoardColumn.tsx`, `BoardCard.tsx` | **Retired** with the column rendering. No toggle back is kept: two renderings of the record means two things to keep honest, and the column view's illegibility at 30+ cards is this card's premise. Git history preserves it. |
| Drag-and-drop | Removed (with `@dnd-kit` dependency if nothing else uses it). Cost accepted and argued in §2.3: moves become rarer, intentional, reasoned, and confirm the agent-spawn side effect. |
| `CardModal` | Gains: Move-to menu (with Reason + spawn confirm), `#41` header rendering, Copy-id. Loses nothing. Its `?card=` opening contract is untouched, so its tests hold. |
| All-boards view (`/boards`) | **Unchanged in v1** — already status-bucketed, already drag-inert. It adopts `displayIdentifier` and the row component where convenient, nothing structural. |
| Home rail (feature 010, proposed) | Adopts `displayIdentifier` for card items. No layout change. |
| Header/nav, session tabs, terminal, diff review | Untouched. |
| Vertical budget (desktop) | The strip costs ~180–220px above the list. It collapses to a one-line summary bar (`32 → 0 → 4 → 6`, still clickable) on scroll, so reading a long list does not pay the strip's height. |
| New files | `ShapeStrip.tsx`, `StateNode.tsx`, `CardListSection.tsx`, `CardRow.tsx`, `MoveMenu.tsx`, `StatePager.tsx` (mobile), `boardShapeModel.ts`, `shared/cardIdentifier.ts`. |

Cost honestly stated: `BoardPage.test.tsx` (298 lines) is substantially rewritten — its
drag/optimistic-move tests die with drag; the move-menu tests replace them, keeping the same
optimistic + rollback + concurrency-token assertions against the same `PATCH`.

---

## 7. Relations

- **CARD-0031 (project cockpit / S1)**: different question, different screen. The cockpit
  answers *"what needs me now"* — few items, cross-pipeline, attention-ordered. This board
  answers *"what is true"* — all cards, state-complete, record-ordered. The strip is not a
  cockpit widget and the cockpit must not grow a full card list. They share only
  `displayIdentifier` and (if the cockpit ever wants a shape summary) the deferred
  `BoardShapeDto` seam (§5).
- **CARD-0040 (auto transitions)**: supplies the honest *data* this surface displays; §3 states
  the dependency direction. The empty In Progress node is this design's advocacy for 0040.
- **CARD-0039 (urgency × importance)**: replaces the priority bar's semantics in place; node
  layout survives.
- **CARD-0019 (edit/history)**: unlocks time-in-state and edge weights (§3), gives Move reasons
  a home, and its archive-instead-of-delete underwrites identifier stability (§4).
- **CARD-0005**: fixed as part of this work's server scope (§4).

---

## 8. Mockup file

`mockup.html` beside this doc is a static, self-contained render of the four decisive frames
with the real 2026-08-13 data: desktop default (Review expanded), desktop overloaded (Backlog
selected, 32 cards, banded), mobile Review (connectors), mobile In Progress (empty). It is a
communication aid for layout and encoding decisions, not a pixel spec — the real build uses
Mantine components and the app's existing status hues.

## 9. Test plan (outline)

- `boardShapeModel.test.ts` (pure): per-state aggregates from a `BoardDetailDto` fixture built
  from the real 2026-08-13 snapshot (counts 32/0/4/6, priority mix 8/10/12/2, oldest `#1` 11d);
  empty state; `n of m` under filters; live-session detection.
- `cardIdentifier.test.ts`: `CARD-0041`→`#41`, zero-pad, non-matching passthrough, search-form
  equivalence (`#41` ≡ `41` ≡ `CARD-0041` ≡ `card-41`).
- `CardRow` / `StateNode` component tests: anatomy, empty node keeps rendering, spawn-confirm
  appears only for `IsActive` targets, kebab does not trigger row-open.
- `BoardPage.test.tsx` rewrite: node click sets `?state=` and filters; move menu submits
  `PATCH` with reason + token, optimistic + rollback (assertions carried over from the drag
  tests); `?card=` open/close untouched and still green.
- Mobile pager: swipe/dot navigation, connector counts, `?state=` round-trip.
- Server: `NextIdentifierAsync` max+1 test — create, delete highest, create again → identifier
  not reused (the CARD-0005 regression pin).
- E2E: none new for v1 (no new write path beyond the existing `PATCH`); remember the
  `EnsureClientBundleIsCurrent` gate if any is added.

## 10. Deliberately left open

1. **The collapse thresholds** (20 cards per state, bands beyond P1) are starting values to
   adjust on sight, not measurements.
2. **All-boards shape rollup** — whether `/boards` ever gets a multi-board strip. Deferred with
   the cross-project rollup non-story.
3. **Whether the strip belongs on any other surface** (cockpit widget) — decided *no* for now
   (§7), revisit with CARD-0031's design.
4. **Edge weights and time-in-state** — designed-for but blocked on CARD-0019's transition
   records; the strip and signal lines have their slots reserved (§3).

## 11. What shipped (2026-08-13)

Built against the live board (45 cards — Backlog 31, In Progress 0, Review 1, Done 13), verified in
a real browser at both widths.

| Piece | File |
|---|---|
| Pure aggregate model + filters + legal-move rule | `client/src/features/board/boardShapeModel.ts` |
| `#41` display form + search-form equivalence | `client/src/shared/cardIdentifier.ts` |
| Strip, node, list, row, move menu, mobile pager | `ShapeStrip.tsx`, `StateNode.tsx`, `CardListSection.tsx`, `CardRow.tsx`, `MoveMenu.tsx`, `StatePager.tsx` |
| State colours + priority ramp | `boardVisuals.ts` |
| CARD-0005 allocator fix | `server/Application/Services/CardService.cs` |

Departures from §1–§10, all deliberate:

1. **Default expansion has a fallback.** §2.1's rule ("first non-empty non-terminal group after
   Backlog") opens *nothing* on a board whose only cards are in Backlog — which is most new boards.
   It now falls back to the first populated state (`defaultExpandedState`).
2. **The signal line for a terminal state is `last closed <date>`**, from `max(CompletedAt)`, rather
   than the oldest-card rule. Card age in Done is noise; the mockup already showed the closed date.
3. **`BoardColumn.tsx` / `BoardCard.tsx` are NOT retired.** §6 said retire them, but §6 also keeps
   the all-boards view unchanged in v1, and that view is what renders them. They lost their
   drag/drop wiring and adopted `displayIdentifier`; the single-board surface no longer uses them.
   Retiring them is the all-boards view's card, not this one.
4. **The list rows keep the CANONICAL identifier as their accessible name** (`CARD-0041 <title>`)
   while rendering `#41`. Spoken and grepped forms stay citable; E2E keeps its selector.
5. **Priority bands are keyed off the state's own card count (>20), not off node selection**, so a
   30-card group banded inside the default all-states list too.
6. **CARD-0005 is fixed but not closed.** Max-suffix+1 stops the collision (delete a middle card and
   the next create no longer duplicates a live identifier). Deleting the *current highest* card
   still frees its number — the row is the only record that it was taken. Closing that needs
   CARD-0019's archive-instead-of-delete.
7. **The mobile chrome collapses** (icon-only actions, filters behind a popover). Not in the design;
   the labelled header cost ~60% of a 390px screen before the pager began.

Not built, and why:

- **Per-edge flow counts and time-in-state** — no transition history exists (§3). Blocked on
  CARD-0019's `CardRevision`. Nothing on screen implies either number.
- **A reason on a non-terminal move is still dropped by the server.** The UI collects and sends it
  (and says so under the field); `CardService.ApplyColumnMove` has nowhere to put it yet. Same
  dependency.
- **`?labels=` uses AND over the label vocabulary as designed; there is no OR mode.** Not deferred
  — just not asked for.
