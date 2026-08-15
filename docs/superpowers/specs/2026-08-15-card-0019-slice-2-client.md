# CARD-0019 slice 2 — client: edit, history and archive on the state-graph board

**Status:** planned, not implemented. **Card:** CARD-0019 (slice 2 of the 2026-08-11 spec).
**Date:** 2026-08-15. **Base spec:** `2026-08-11-card-0019-card-correction.md` — its ten-line
"Client changes (slice 2)" section is superseded by this file; everything else there stands.

## What the server actually shipped (verified on master, b2499dd)

Slice 1 is live and its contract differs from the base spec in ways the client must follow:

| Fact | Where |
|---|---|
| `PATCH /api/cards/{id}/content` — `UpdateCardContentRequest(ConcurrencyToken, Reason, Title?, Description?, Priority?, Labels?, EditedBy?)`, returns full `CardDto` | `CardEndpoints.cs:24`, `BoardDtos.cs:125` |
| `GET /api/cards/{id}/revisions` — `CardRevisionDto[]`, **newest first**, one monotonic `revisionNumber` across all kinds | `CardEndpoints.cs:33`, `CardService.GetRevisionsAsync` |
| `POST /api/cards/{id}/archive` and `/unarchive` — token + required reason + optional actor; **not** DELETE | `CardEndpoints.cs:52,61` |
| `GET /api/boards/{id}?includeArchived=true` — archived cards hidden by default, shown on request | `BoardEndpoints.cs:26`, `BoardService.ToDetailDto` |
| Validation is **422** (`ValidationException` → 422 with `errors` dict), not the base spec's 400. Keys are PascalCase field names (`"Description"`, `"Reason"`); messages name limit and actual length: `"Description must be at most 20,000 characters; got 20,001."` | `ValidationException.cs`, `CardService.RequireWithinLimit`, pinned over HTTP by `CardCorrectionApiTests` |
| Limits: title **300**, description **20,000**, reason **4,000** (raised from the base spec's 1,000), actor **200** | `CardService.Max*` constants |
| `CardRevisionKind`: `ContentEdit` \| `Move` \| `Archive` \| `Unarchive` (serialized as strings, like every enum in the API). `ContentEdit` carries the **superseded** title/description/priority/labels; `Move` carries `fromColumnId/toColumnId/fromStatus/toStatus` and no text; Archive/Unarchive carry only reason + actor | `CardRevisionKind.cs`, `CardRevisionDto` |
| `CardDto` grew `RevisionCount`, `ArchivedAt`, `ArchivedReason`, `ArchivedBy`. **`RevisionCount` counts moves too** — it is non-zero on most cards, so it is NOT an "edited" marker | `BoardDtos.cs:40` |
| Concurrency: token mismatch → 409; concurrent revision writers → 409 via the `(CardId, RevisionNumber)` unique index | `CardService.SaveCardWriteAsync` |
| Archive refused (409) with a live owner session or active workflow run; moving or spawning an **archived** card → 409 ("unarchive it before…") | `CardService.ArchiveAsync/MoveAsync/SpawnAsync` |
| **Reopen is NOT shipped**: `CardStateMachine` still maps Done/Canceled → `[]`. Editing and archiving work in terminal columns; moving out does not | `CardStateMachine.cs:43` |

## The surface this lands on (post-CARD-0042, commit ce48f50)

The board is a state graph, not columns of tickets. Relevant structure:

- **`BoardPage.tsx`** — filter controls, `ShapeStrip` + `CardListSection` list on desktop,
  `StatePager` (one state at a time) on mobile; also still owns **`CardCreateModal`** (the base
  spec's "BoardPage.tsx create card path" bullet is, alone among the ten, still accurate).
- **`CardRow.tsx`** — one line per card; its kebab is **`MoveMenu`** (`variant="kebab"`), already
  the card's actions menu (move targets + Copy id) with an established reason-modal pattern.
- **`CardModal.tsx`** — fullscreen card page: header (identifier, title, badges, `MoveMenu`
  button, `AgentPicker`, Spawn, Close), tabs Sessions / Diff / Details (`keepMounted={false}`),
  and a desktop sidebar duplicating `CardDetails`.
- **`boardShapeModel.ts`** — pure derivation from `BoardDetailDto`; archived cards are simply
  absent from the payload unless asked for, so it needs no filtering logic of its own.
- API: `client/src/api/boards.ts` (types + hooks; `useMoveCard` is the invalidation pattern),
  `client/src/api/client.ts` (`ApiError`, `getApiErrorMessage` — already extracts the first
  message from a problem-details `errors` dict, so 422 bodies surface without status-specific
  code; what's missing is **per-field** extraction for inline form errors).
- Tests: vitest + testing-library + msw (`client/src/test/utils`, `client/src/test/mocks/server`),
  component-level with real msw handlers — `BoardPage.test.tsx`, `CardRow.test.tsx`,
  `CardModal.test.tsx` are the models to extend. Run with `npx vitest run <file>` in `client/`.

## Design decisions

### D1 — Edit lives in the card modal, as a dialog over it

An "Edit" action (`TbPencil` icon button, `aria-label="Edit card"`) in `CardModal`'s header
actions group opens a new **`CardEditModal`** — a standard `Modal` layered over the fullscreen
card page, exactly how `MoveMenu`'s reason modal already layers (`zIndex={400}`). Prefilled
title / description / priority / labels; a required **Reason** textarea; `EditedBy` sent as
`"operator"` (the web UI is the operator's surface; agents hit the API directly — no free-text
field for it, matching the server's "self-reported, never authenticated" stance). Character
counters on description (20,000) and reason (4,000) from shared exported limits; submit disabled
while over a limit or reason is blank. Submit sends only fields that changed (null = unchanged is
the server contract) plus `concurrencyToken`.

Not a pencil "next to the title block" as the base spec sketched — that block no longer has room
(identifier + title + badges share one line with the mobile-collapsed header), and the header
actions group is where every other card-level action already sits.

### D2 — History is a tab with per-kind rows

A **History** tab joins Sessions / Diff / Details in `CardModal`'s tabs, labelled
`History (n)` from `card.revisionCount`. `keepMounted={false}` means the `useCardRevisions`
query naturally fires on first open — free lazy loading. The tab renders **`CardHistory`**, a
mixed timeline (newest first, as served) with one row shape per kind:

- **ContentEdit** — "Edited" + reason + editedBy + timestamp; the superseded title/description
  collapsed behind an expander (a superseded description can be 20k chars; never render it open).
  Superseded priority/labels shown inline when present. No diffing in this slice — the snapshot
  with its reason is the record; a computed diff against the next revision is a later nicety.
- **Move** — "Moved {from} → {to}" + optional reason + actor. Column **names** resolved from the
  `columns` prop (`CardModal` already receives `board.columns`); fall back to the
  `fromStatus → toStatus` strings when the id isn't found (all-boards modal passes `columns=[]`,
  and a column can be deleted out from under old revisions).
- **Archive / Unarchive** — "Archived" / "Unarchived" + reason + actor.

Empty history renders "No history yet" (a card that has never moved or been edited).
The base spec's "edited affordance from RevisionCount" is **overridden**: RevisionCount includes
moves, so it cannot mean "the text was corrected"; it is used only as the tab count.

### D3 — Archive rides the existing actions menu; unarchive replaces move on archived cards

`MoveMenu` (already the card actions menu — kebab on every row, button in the modal header) gains
a divider and an **"Archive…"** item (red), opening a reason modal that mirrors the move-reason
modal: required reason, warning when the card has a live session ("the server will refuse while a
session is running" — let the 409 be the enforcement; its message is good). On success:
notification; if the card modal is open on that card it closes itself (the card leaves the
default board payload, so `selectedCard` resolves null — close explicitly first for a clean UX).

For an **archived** card (visible under the D4 toggle), `MoveMenu` offers **no move targets**
(server would 409) and shows **"Unarchive…"** (reason modal, same contract) plus Copy id. Spawn in
`CardModal` is disabled for archived cards with a title explaining why.

### D4 — Archived cards: per-board opt-in toggle, nothing on the all-boards view

A **"Archived" chip** joins the board filter controls (inside the mobile filter popover, inline on
desktop), backed by a `&archived=1` search param. On, the board query asks
`?includeArchived=true`; archived cards render in their home state **dimmed with an "archived"
badge** in `CardRow` (both layouts). Query-key design: `boardKeys.detail(id)` stays as-is and a
sibling `boardKeys.detailArchived(id) = ['boards', id, 'archived']` is added — every existing
mutation invalidates by the `['boards', id]` prefix, so **no invalidation call sites change**.

The all-boards aggregate view stays live-cards-only — it is a workload view, not an archaeology
view, and wiring `includeArchived` through `useAllBoardDetails` buys nothing today. Deep-linking
`?card=<archived-id>` without `&archived=1` silently doesn't open the modal (the card isn't in
the payload); accepted for this slice — see "Not determined".

### D5 — Mobile gets all three, by construction, and the pager needs zero new code

Edit and History live in `CardModal`, which is fullscreen and identical on mobile. Archive lives
in `MoveMenu`, which `CardRow` renders in both `row` and `stacked` layouts, and `StatePager`
renders cards through the same `CardListSection` → `CardRow` chain. So the mobile pager gets
edit, history and archive with **no pager-specific work** — this is a consequence of the
CARD-0042 architecture, not a deliberate exclusion, and nothing here should special-case
`StatePager`. The archived toggle is inside the existing mobile filter popover, so the pager
keeps the screen.

### D6 — 422 handling: one field-error helper, plus client-side pre-check

`client.ts` gains `getApiFieldErrors(error: unknown): Record<string, string>` — returns the
problem-details `errors` dict flattened to first-message-per-field (empty object for non-422 /
non-ApiError). `CardEditModal` (and `CardCreateModal`) map it onto Mantine input `error` props by
PascalCase key (`Title`, `Description`, `Reason`, `Priority`); anything unmapped falls through to
the existing `getApiErrorMessage` notification. Client-side counters (D1) pre-check the limits so
the 422 is the backstop, not the UX — but the server message is always displayed verbatim when it
arrives, because the limits are constants that can drift (see D7).

### D7 — Shared limits are client constants with a lockstep note

`boards.ts` exports `CARD_LIMITS = { title: 300, description: 20_000, reason: 4_000 } as const`
with a comment naming `CardService.MaxTitleLength/MaxDescriptionLength/MaxReasonLength` as the
source of truth (same convention as the `canMoveTo` ⇄ `CardStateMachine` lockstep pair). No
endpoint serves the limits; adding one is not worth it for three integers — drift shows up as a
422 whose message the UI displays anyway.

## Deliberate overrides of the base spec's client section

1. **422, not 400** — slice 1 shipped `ValidationException` → 422 (as it has mapped across this
   codebase all along); the base spec's "400" was wrong on arrival.
2. **Archive via POST routes** — `useArchiveCard`/`useUnarchiveCard` call
   `POST /cards/{id}/archive|unarchive`; no DELETE anywhere (per the 2026-08-13 addendum).
3. **History is a mixed timeline** — four row kinds, not "revisions with the superseded text";
   Move revisions exist and dominate most cards' histories.
4. **No "edited" badge from RevisionCount** — it counts moves; used only as the History tab count.
5. **Edit affordance placement** — header actions group, not "pencil next to the title block"
   (that block no longer exists in editable form post-CARD-0042).
6. **"Archive behind the modal's overflow"** — the modal has no overflow menu; `MoveMenu` *is*
   the card's overflow everywhere, so archive goes there (and is thereby reachable from every
   card row, not only the modal).
7. **Reason ceiling is 4,000** in the counter, not the base spec's 1,000 (server raised it).
8. **Archived surfacing decided** (base spec was silent): per-board toggle, D4.

## Slices

Each is independently landable and testable; A is a dependency of B–D, but B, C and D are
mutually independent and can land in any order.

### Slice A — API contract layer

`client/src/api/boards.ts`:
- `CardDto` += `revisionCount: number`, `archivedAt: string | null`,
  `archivedReason: string | null`, `archivedBy: string | null`.
- New types: `CardRevisionKind` (string union), `CardRevisionDto` (camelCase mirror of the server
  record, nullable per kind), `UpdateCardContentRequest`, `ArchiveCardRequest`,
  `UnarchiveCardRequest`. `CARD_LIMITS` (D7).
- New key: `boardKeys.cardRevisions(cardId) = ['cards', cardId, 'revisions']` and
  `boardKeys.detailArchived(id)` (D4).
- New hooks, invalidation mirroring `useMoveCard` (detail + all + allDetails, **plus**
  `cardRevisions(cardId)`): `useUpdateCardContent(boardId)`, `useArchiveCard(boardId)`,
  `useUnarchiveCard(boardId)`, `useCardRevisions(cardId, enabled?)`.
- `useBoard(id, { includeArchived })` — picks key and appends `?includeArchived=true`.
- Fix the now-stale `MoveCardRequest.reason` doc comment (the reason **persists** on every move
  as the Move revision's reason; terminal moves additionally stamp `TerminalReason`).

`client/src/api/client.ts`: `getApiFieldErrors` (D6).

**Tests** — `client/src/api/boards.test.tsx` (new): with msw, `useUpdateCardContent` PATCHes
`/cards/{id}/content` and invalidates board detail + revisions; `useCardRevisions` GETs and
parses a mixed-kind payload; `useBoard` with `includeArchived` hits the query-string URL and a
distinct cache key. `getApiFieldErrors` unit cases (422 dict, 409 problem, non-ApiError) beside
the existing client tests. (If `renderHook` friction appears, fold the hook assertions into the
component tests of B–D instead — the coverage requirement is the request/invalidation shapes,
not the vehicle.)

### Slice B — Edit dialog + create-dialog guard

- New `client/src/features/board/CardEditModal.tsx` (D1, D6).
- `CardModal.tsx`: Edit action in header; render `CardEditModal`.
- `BoardPage.tsx` `CardCreateModal`: description counter against `CARD_LIMITS.description`,
  submit disabled over-limit, 422 field errors surfaced via `getApiFieldErrors` (title kept
  non-blank as today).

**Tests** — `CardEditModal.test.tsx` (new): prefills current values; submit disabled until reason
non-blank; sends only changed fields + token + `editedBy: "operator"`; a 20,001-char description
disables submit and shows the counter red; msw 422 with
`errors: { Description: ["Description must be at most 20,000 characters; got 20,001."] }` lands
on the description input; msw 409 surfaces as a notification. `BoardPage.test.tsx`: create
dialog over-limit guard + 422 rendering.

### Slice C — History timeline

- New `client/src/features/board/CardHistory.tsx` (D2).
- `CardModal.tsx`: History tab, label `History (n)`.

**Tests** — `CardHistory.test.tsx` (new): msw serves an interleaved list (Move → ContentEdit →
Move → Archive → Unarchive, descending revision numbers); each kind renders its row shape;
column names resolve from `columns` and fall back to statuses with `columns=[]`; superseded
description is collapsed until expanded; empty history states itself. `CardModal.test.tsx`: the
revisions request does not fire until the History tab is opened (pins the lazy-load).

### Slice D — Archive/unarchive + archived visibility

- `MoveMenu.tsx`: Archive…/Unarchive… items + reason modal (D3); archived cards get no move
  targets; also fix the stale helper text under the move-reason field ("nowhere to store it yet —
  that arrives with CARD-0019" → "kept as the reason on the card's history entry; a terminal
  move also stamps the terminal reason").
- `CardRow.tsx`: dimmed style + "archived" badge when `card.archivedAt`.
- `BoardPage.tsx`: Archived chip ↔ `&archived=1` ↔ `useBoard(id, { includeArchived })` (D4).
- `CardModal.tsx`: Spawn disabled when archived; close on successful archive.
- `boardShapeModel.ts`: no logic change; update the header comment (moves ARE recorded now — what
  is still missing is a server projection of entered-state-at into the board payload, which stays
  deferred per feature 011 §5).

**Tests** — `BoardPage.test.tsx`: toggle off → archived card absent (msw asserts no
`includeArchived` param); toggle on → present, dimmed, badged, and the URL carries `archived=1`.
`CardRow.test.tsx`: archived rendering; kebab on an archived card offers Unarchive and no move
targets. `MoveMenu` archive flow (via CardRow test): reason required, POSTs
`/cards/{id}/archive` with token+reason, 409 ("live owner session") surfaces verbatim.

## Not determined, and what would settle it

- **`renderHook` availability in the test harness** — `client/src/test/utils.ts` was not read to
  the bottom and no existing hook-level test exists to copy. Settled by one look at that file /
  one spike test; slice A names the fallback (assert via component tests).
- **Deep-linking `?card=` to an archived card** (D4) — currently resolves to nothing and the
  modal never opens. Acceptable for this slice; if operators hit it, the fix is auto-enabling the
  archived param when the card param fails to resolve, which needs a decision on whether that
  surprise-widens the board view. Evidence: an actual complaint, or an E2E script that exercises
  an archived-card link from a notification.
- **Whether Diff/History interplay needs anything** — `showDiffReview` keys off status
  Review/Done; an archived card in Review keeps its Diff tab. Assumed fine (archive ≠ close).
- **E2E coverage** is deliberately out of this slice: the vitest+msw suite pins the contract
  shapes, and a browser E2E would require rebuilding `client/dist` (the stale-bundle trap in
  CLAUDE.md) for marginal gain. If slice 3 (correcting CARD-0026/0018 on the live board) is done
  through this UI rather than the API, that *is* the acceptance test.
- **Labels editing input shape** — `CardEditModal` reuses the create dialog's comma-separated
  `TextInput` for labels rather than introducing a tag input; consistency over polish here.
  Revisit only if label editing turns out to be a real workflow.

## Out of scope (unchanged from base spec + addendum)

Reopen from terminal states (server still refuses; `canMoveTo` lockstep stays as-is), the spawn
prompt spill (CARD-0025), slice 3 record corrections, time-in-state on the board surface
(needs a server projection), hard delete, auth.
