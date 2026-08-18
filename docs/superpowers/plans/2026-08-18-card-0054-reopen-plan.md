# CARD-0054 — reopening a closed card: plan

**Verdict up front:** the amendment spec (`2026-08-13-card-0019-amendment-1.md` §2) already decided
the shape — a dedicated `POST /api/cards/{id}/reopen`, NOT a new move edge — and that decision
survives contact with today's code with exactly one reconciliation needed: the spec predates slice 1's
`Move`-revision writing inside `ApplyColumnMove`, so the reopen path must suppress the automatic
`Move` row or every reopen writes two history entries for one act. Everything below is that spec
carried forward onto master as it stands (`8050a10`), with the spec's silences answered explicitly.

## What the spec already answers (carried forward, not re-decided)

- **Endpoint, not transition.** `POST /api/cards/{id}/reopen` with `ConcurrencyToken` + **required**
  `Reason` (reopening is always a correction) + optional target live column, defaulting to the
  board's Backlog column. `CardStateMachine.Transitions` keeps `Done → []` and `Canceled → []` for
  the *move* verb — that is deliberate and stays. The reopen reaches its target through the existing
  `ApplyColumnMove(enforceStateMachine: false)` seam (`CardService.cs:454-459` is the precedent
  caller).
- **Revision kind: `Reopen`, one row.** The amendment's §1 kind list was `Content, Move, Archive,
  Unarchive, Reopen`; slice 1 shipped only the first four (`CardRevisionKind.cs` ends at
  `Unarchive = 3`). Add `Reopen = 4`. §1 also already states the move fields
  (`FromColumnId/ToColumnId/FromStatus/ToStatus`) are "populated for Move/Reopen" — so the Reopen
  revision **is** the move record for that transition, carrying from/to itself. One act, one row.
  A reader of the interleaved history sees `Reopened` as its own badge with the transition inline,
  which is what "tell reopen from the revision log, not infer it" requires.
- **Snapshot-then-mutate.** The Reopen revision is written BEFORE `ApplyColumnMove` runs, because
  `ApplyColumnMove` clears `CompletedAt`/`TerminalReason` on any non-terminal landing
  (`CardService.cs:663-667`) — same rule content edits follow.
- **`CompletedAt` / `TerminalReason`: cleared on the card, preserved in the revision.** The card
  surface reads live again (a reopened card is not completed); the record keeps both values on the
  Reopen row. This is the same pattern `Unarchive` set for `ArchivedAt/Reason/By`
  (`CardService.cs:386-392`).

## The spec's silences, answered

1. **The completion review checkpoint: preserved, untouched — by construction.**
   `AgentReviewCheckpointService` rows are append-only, keyed to the **agent** (not the card), and
   the card holds no pointer to them; `MoveAsync` captures one on terminal entry
   (`CardService.cs:262-263`) and nothing ever deletes one. A reopen therefore has nothing to clear:
   the sign-off that happened, happened. When the reopened card is later re-closed, terminal entry
   captures a fresh checkpoint and `GetLatestAsync` naturally serves the newer baseline. The plan
   adds **no code** here — but the reopen tests should pin that reopen deletes no checkpoint rows.
2. **Where the superseded terminal facts live: two new nullable columns on `CardRevisions`.**
   `CardRevision` has no field that can hold `TerminalReason`/`CompletedAt` (the `Reason` column is
   the reopen's own required reason). Reconstructing them from prior `Move` rows does not work,
   for three independent reasons: history is non-retroactive (every card closed before slice 1
   shipped 2026-08-14 has terminal facts on the card row and **no** Move rows), a defaulted
   `TerminalReason` ("Moved to terminal column.") appears on no revision (the Move row's `Reason`
   is null in that case, `CardService.cs:658-661`), and `CompletedAt ??=` keeps the FIRST terminal
   timestamp across re-closes while the latest Move row carries a later one. So: add
   `TerminalReason (text, null)` and `CompletedAt (timestamptz, null)` to `CardRevisions`, named
   like the card fields they snapshot (the existing convention — `Title` on a revision is the
   superseded title), populated only on `Kind = Reopen`. One migration, `AddCardReopenRevisionFields`.
3. **Double-revision reconciliation.** `ApplyColumnMove` now calls `CardRevisionLog.AppendMove` on
   every move (`CardService.cs:643-644`) — added by slice 1 after the amendment was written. Give it
   `bool recordRevision = true`; `ReopenAsync` appends its Reopen row first (reading
   from-column/status and the terminal facts off the still-unmutated card) and calls
   `ApplyColumnMove(..., enforceStateMachine: false, recordRevision: false)`. Every other caller is
   untouched.
4. **Spawn: a reopen never spawns, full stop.** The amendment warned that moving into an `IsActive`
   column spawns an agent — that was true when it was written, and CARD-0051 has since made spawn
   **opt-in** everywhere (`MoveAsync` spawns only on `request.Spawn`, and only `MoveAsync` has that
   logic; `ApplyColumnMove` itself never spawns). `ReopenAsync` calls `ApplyColumnMove` directly, so
   even a reopen aimed at an active column starts nothing. Want an agent on the reopened card?
   `POST /spawn` afterwards — one explicit call. This is *stronger* than the amendment's
   "Backlog-default makes it safe" and should be pinned by a test, not just documented.

## Design in full

### Server (slice 1)

- `CardRevisionKind.Reopen = 4` — doc comment: "A terminal close was undone: the row carries the
  transition AND the superseded `TerminalReason`/`CompletedAt`. Always has a reason."
- `CardRevision`: the two nullable columns above, under a new `// ---- Reopen only ----` section.
  `AppDbContext`: `TerminalReason` as `text` (matches `Cards.TerminalReason`), nothing else.
- `CardRevisionLog.AppendReopen(card, toColumn, reason, reopenedBy, utcNow)` — reads
  `card.BoardColumnId/Status/TerminalReason/CompletedAt` (pre-mutation) into the row; `Reason`
  required at the call site, trimmed like the rest.
- `CardStateMachine.CanReopenFrom(CardStatus from) => from is CardStatus.Done or CardStatus.Canceled;`
  — the reopen rule lives in the same file as the `[]` rows it complements, with the comment at
  `CardStateMachine.cs:38-44` updated: reopen now EXISTS, as a distinct verb, and the `[]` rows are
  about the move verb only.
- `CardService.ReopenAsync(Guid id, ReopenCardRequest request, CancellationToken ct)`:
  1. Validate: `Reason` required, ≤ `MaxReasonLength` (4000); `ReopenedBy` optional ≤ 200 — same
     shape as `ValidateArchiveRequest`.
  2. `LoadCardForUpdateAsync` (already Includes `Board.Columns` — no extra query for the default
     column); token required + match (409 on mismatch, before any write).
  3. `card.ArchivedAt is not null` ⇒ 409 "unarchive it before reopening it" (axes stay orthogonal,
     same as `MoveAsync`).
  4. `!CardStateMachine.CanReopenFrom(card.Status)` ⇒ 409 "is not closed".
  5. Resolve target: `request.BoardColumnId` if given — must belong to the card's board (400) and be
     non-terminal (400); else the board's lowest-`ColumnOrder` column with
     `CardStatus == CardStatus.Backlog`, else the lowest-order non-terminal column, else 409 (a board
     with no live column cannot take the card back).
  6. `CardRevisionLog.AppendReopen(...)`, then
     `ApplyColumnMove(card, target, enforceStateMachine: false, recordRevision: false, reason: request.Reason, movedBy: request.ReopenedBy)`
     — which clears `CompletedAt`/`TerminalReason`, rotates the token, stamps `UpdatedAt`.
     (`StartedAt ??=` fires only if the target is active; a Backlog reopen leaves it alone —
     correct, the original start stands.)
  7. Save, publish `CardChanged`, return `CardDto` (like archive/unarchive — there is no spawn
     half to report, so `MoveCardResult` would be noise).
- DTOs: `ReopenCardRequest(Guid ConcurrencyToken, string Reason, Guid? BoardColumnId = null,
  string? ReopenedBy = null)`; `CardRevisionDto` gains `TerminalReason` + `CompletedAt` (nullable,
  populated on Reopen rows); `BoardService.ToRevisionDto` maps them.
- Endpoint: `cards.MapPost("/{id}/reopen", ...)` next to archive/unarchive in `CardEndpoints.cs`.
- Courtesy: the state-machine rejection in `ApplyColumnMove` (`CardService.cs:637-639`) appends
  ", a closed card is reopened via POST /cards/{id}/reopen" when `CanReopenFrom(card.Status)` —
  the error a script hits today should name the door.
- Docs: `server/Bundles/board-api.md` gains the verb; `docs/product/card-workflow-decisions.md`
  gets a short "reopen" entry noting terminal-stays-terminal for moves + the dedicated verb.

### Client (slice 2) — the lockstep, both halves named

- `client/src/api/boards.ts`: `CardRevisionKind` union gains `'Reopen'`; `CardRevisionDto` gains
  `terminalReason: string | null` and `completedAt: string | null`; add `reopenCard(cardId, body)`.
- `client/src/features/board/boardShapeModel.ts`: **`canMoveTo` deliberately does not change** — the
  client is currently consistent with reopen not existing (`from !== 'Done' && from !== 'Canceled'`,
  line 282, confirmed on master) and stays consistent with the server's move verb, where terminal is
  still a wall. The new edge is `canReopenFrom(status)`, lockstep-paired with
  `CardStateMachine.CanReopenFrom` — update BOTH lockstep comments (`boardShapeModel.ts:271-273` and
  `CardStateMachine.cs:38-44`) in the same commit so the pair that diverged once (`ca754d1`) is
  re-tied at both ends.
- `CardHistory.tsx`: `KIND_LABEL['Reopen'] = 'Reopened'`, `KIND_COLOR['Reopen'] = 'orange'`; the row
  renders the transition (reuse `describeMove` — the fields are the same) plus the superseded facts:
  "was closed <completedAt>: <terminalReason>".
- `CardModal`: a Reopen affordance shown when `canReopenFrom(card.status)` (and not archived),
  prompting for the required reason — same interaction pattern the modal already uses for
  archive/unarchive per the slice-2 spec.
- **`client/dist` must be rebuilt before any E2E run touching this** (standing gotcha).

### CLI + record (slice 3)

- `scripts/card.ps1`: new verb `reopen CARD-NNNN -Reason r | -ReasonFile p [-To column] [-By name]
  [-Token g]` — same token-prefetch behavior, ASCII-only. Update the header verb list and
  `ValidateSet`.
- Close CARD-0054 with a reason that cites this plan; if any card is currently known to be
  wrongly closed, reopening it is the natural smoke test.

## Test plan (follow `CardCorrectionIntegrationTests` / `CardCorrectionApiTests` precedent)

Unit (`CardStateMachineTests`): `CanReopenFrom` true for exactly Done/Canceled;
`terminal_states_are_immutable` stays untouched (moves out remain illegal).

Integration (shared-Postgres rules: assert only on rows the test created, scope by card id):

- `Reopen_writes_one_revision_with_the_superseded_terminal_facts_and_clears_them` — close with a
  reason, reopen; assert exactly ONE new revision from the act (kind Reopen, from=Done,
  to=Backlog, `TerminalReason`/`CompletedAt` populated, no sibling Move row), card's
  `CompletedAt`/`TerminalReason` null, status Backlog, token rotated.
- `Reopen_defaults_to_the_backlog_column_and_never_spawns` — reopen with no target lands in
  Backlog; reopen explicitly into the active column starts NO session (assert no AgentSession
  rows for the card beyond what the test made) and leaves `OwnerSessionId` null.
- `Reopen_of_a_card_closed_before_revisions_existed_still_keeps_its_terminal_facts` — seed a
  terminal card with terminal fields set and zero revisions (the pre-slice-1 shape); reopen;
  the facts survive on the Reopen row. This is the test that justifies the new columns.
- `A_reopened_card_recloses_with_a_fresh_completion` — reopen then move to Done again:
  new `CompletedAt` (not the original — `??=` sees null now), new `TerminalReason`; history holds
  the original pair on the Reopen row and both closes as Move rows; revision numbers strictly
  interleave across all kinds (extends `Revisions_number_one_sequence_across_kinds...`).
- `Reopen_with_a_stale_token_is_a_conflict_and_writes_no_revision`.
- `Reopen_of_a_live_card_or_into_a_terminal_column_or_without_a_reason_is_rejected` — 409 / 400 /
  400 respectively.
- `Reopen_of_an_archived_card_is_refused_until_unarchive`.
- `Reopen_deletes_no_review_checkpoint` — close with an assigned agent (checkpoint captured),
  reopen, checkpoint row still present.
- `A_terminal_move_rejection_names_the_reopen_endpoint` — the courtesy message.

API (`CardCorrectionApiTests` style): `Reopen_over_http_returns_the_card_live_again_with_history` —
POST reopen, assert 200, status live, then GET `/revisions` shows the Reopen entry with the
superseded facts serialized.

Client: `boardShapeModel.test.ts` — `canReopenFrom` table + `canMoveTo` unchanged for terminal;
`CardHistory.test.tsx` — Reopen row renders badge, transition and superseded facts;
`boards.test.tsx` — `reopenCard` request shape.

Run server tests with `--property:OutputPath=bin-0054/` (trailing forward slash) while daemons hold
`bin/`, and delete the scattered `bin-0054` dirs afterwards.

## Deliberately not done

- **No new move-verb transitions.** `Done`/`Canceled` stay `[]` in `Transitions`; a drag out of Done
  on the board stays refused. Reopen-with-reason is the only exit, which is the record-integrity
  point of the card.
- **No retro-backfill of Reopen rows** for the historical b48f928-era workaround moves — history is
  non-retroactive here as everywhere (amendment §3.4 precedent).
- **No reopen for archived cards in one call** — unarchive and reopen stay two verbs on two axes.
- **No `MoveCardResult`-style spawn reporting on reopen** — reopen structurally cannot spawn, so
  there is nothing to report.
