# CARD-0170 + CARD-0175 — GitHub-imported cards: landing column and identifier scheme — plan

**Date:** 2026-08-24 · **Cards:** CARD-0170 (`b20b2ad9-9c78-4d1e-9d1f-b1b91629bf73`), CARD-0175
(`e8a47420-f679-4355-aec1-aff4c21a8d76`) — bundled by operator decision ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** `master` @ `b6e5fbb`. Every file:line below was re-read out of the code on
that commit; every live fact was queried this pass (2026-08-24 19:00–20:00 BST).

**Established facts (Investigate, this pass — LIVE-VERIFIED where marked):**

- **`IsActive` has one meaning and it is not "where new work lands" — it is "auto-dispatch may
  start an agent here".** `OrchestratorService.LoadEligibleCandidatesAsync`
  (`server/Application/Services/OrchestratorService.cs:501`) selects
  `c.BoardColumn.IsActive && !c.BoardColumn.IsTerminal`; `CardService.MoveAsync` (`CardService.cs:275-295`)
  sets `AutoDispatchHeldAt` on a move INTO an active column that did not ask to spawn (CARD-0087),
  `ReopenAsync` does the same (`:457`), `SpawnAsync` moves a card into the first active column
  before launching (`:551-556`), and `ApplyColumnMove` stamps `StartedAt` on entry (`:795`). Nothing
  reads `IsActive` as an intake target. The only other selector of "where new work lands" is
  `CardService.CreateAsync` (`:106-109`): first column with `CardStatus == Backlog`, else the first
  column by order.
- **The sync conflates "open in the tracker" with "an agent should be working it" — by design of
  the original E10 feature, not by accident.** `ExternalTrackerSyncService.UpsertIssuesAsync`
  (`server/Application/Services/ExternalTrackerSyncService.cs:126-129`) resolves `activeColumn` as
  first `IsActive && !IsTerminal`, creates every open issue there (`:232,239`), and `UpdateExisting`
  (`:346-365`) **drags any unowned, non-terminal card back to that column on every sync**
  (`shouldMoveForTrackerState`, reason "External tracker state 'open' maps to this column"). Two
  E10 integration tests pin this as intended:
  `PollTick_syncs_external_tracker_issue_into_card_and_dispatches_it`
  (`tests/Antiphon.Tests/Application/OrchestratorServiceIntegrationTests.cs:112`) and
  `PollTick_dispatches_external_issue_only_after_blockers_terminal` (`:324`, expects the unblocked
  card to be dispatched and reach Review). `ApplyExternalReopens`
  (`TrackerBidirectionalSyncService.cs:188-190`) uses the same first-active rule for a GitHub reopen,
  with no `AutoDispatchHeldAt` — a reopen on GitHub is an auto-dispatch, which contradicts
  CARD-0054's "a reopen never spawns". Both commits trace to `c361fda feat(e10): add external
  tracker adapters`.
- **LIVE: the drag-back already happened.** Card `#6` (`05c57304`) history: rev 1 14:37 manual move
  to Backlog; **rev 2 15:07:25 `external-tracker` "External tracker state 'open' maps to this
  column"** (the 30-minute tick sync moved it back to In Progress — same second on all 11); rev 3
  18:48 manual move to Backlog again. The CARD-0175 mitigation therefore only holds until the next
  successful tick sync. (No `external-tracker` revision has landed since 18:48 as of 19:57, across
  at least two due ticks; the tick path never runs `TrackerTokenResolver` — only
  `TrackerBidirectionalSyncService` does, `:22,:33`, and `GitHubIssuesTracker.cs:426-432` falls
  back to an env var — and the read-only sync logs nothing on success or on a parser skip, so
  whether it is running is unobservable from the log. Noted, not chased: separate card material.)
- **LIVE: the mismatch is universal, not this board's configuration.**
  `BoardService.CreateDefaultColumns` (`BoardService.cs:304-312`) seeds
  `Backlog[Backlog] > In Progress[InProgress, IsActive] > Review > Done[terminal]`; every one of the
  ~150 boards in the deployment (psql over `Boards ⋈ BoardColumns`) has exactly that shape, and the
  client's default template agrees (`client/src/features/board/BoardPage.tsx:71-76`). Any board that
  activates a tracker gets CARD-0170.
- **LIVE: the 11 imported cards.** `#3`–`#13`, all in Backlog, `Status=Backlog`,
  `RetrySchedules.AttemptCount=3/3, NextRetryAt=null` (exhausted), `AutoDispatchHeldAt=null`,
  `CurrentWorktreeId=null` (the failure is before the `Worktree` insert), `ExternalIssueRefs.Origin=0`
  (ExternalImport), `ExternalKey == Identifier == '#N'`. Nothing else in the database has an
  identifier outside `^[A-Za-z0-9._-]+$`. The exhausted schedules are permanent — nothing in
  `CardService` resets a `RetrySchedule` (only `ProjectCascade.cs:118` deletes them).
- **The worktree validator is the right shape and must not move.** `WorktreeManager.ValidateCardId`
  (`server/Infrastructure/Git/WorktreeManager.cs:249-264`): empty / untrimmed / `..` / regex
  `^[A-Za-z0-9._-]+$`; called by `BuildBranchName` (`:266`, `feat/card-<id>`) and
  `BuildDirectoryName` (`:268`, `card-<id>`), from `CreateAsync` (`:48`), reached via
  `AgentSessionService.ResolveOrCreateWorktreeAsync` passing `card.Identifier` (`AgentSessionService.cs:1553`).
  The orphan-recovery arm matches `w.CardId == card.Identifier` (`:1562`), and `ListAsync` derives
  `CardId` from the branch suffix when sidecar metadata is missing (`:124`) — so any transform between
  identifier and branch would have to be reversible to keep orphan recovery working. Git itself
  accepts `#` (`git check-ref-format --branch 'feat/card-#12'` passes) and so does NTFS; the
  validator's exclusion is about shell/URL/tooling hygiene and traversal, not git legality. Pinned by
  `WorktreeManagerSafetyTests` (`tests/Antiphon.Tests/Infrastructure/WorktreeManagerTests.cs:16-31`).
- **`#N` is already taken — it is the display AND entry form of `CARD-000N`.**
  `client/src/shared/cardIdentifier.ts:9-21`: `CARD-0041` renders as `#41`; anything else renders
  verbatim — so on the Antiphon board **`CARD-0012` and GitHub's `#12` render identically** and the
  client has no external-ref field to tell them apart (`grep externalKey client/src` → nothing;
  `CardDto` has no such field, `server/Application/Dtos/BoardDtos.cs:45-75`).
  `CardService.ResolveCardIdAsync` (`CardService.cs:167-205`, documented in `docs/antiphon-api.md:75-79`
  and `scripts/card.ps1`) canonicalises `#51` → `CARD-0051` and matches
  `Identifier == canonical || Identifier.ToLower() == raw` — **LIVE: `GET /api/cards/%235` is a 409
  today** ("matches cards on more than one board" — wrong message, real collision: `CARD-0005` and
  `#5` are on the same board). `card.ps1 get '#N'` is broken for every N ≤ 13 on this board and the
  set grows with every import.
- **The export direction already has the shape CARD-0175 sketches as option two.**
  `TrackerBidirectionalSyncService.CreateMissingIssuesAsync` (`:668-700`) leaves an export-origin
  card's `Identifier` as `CARD-nnnn` and stores `#N` in `ExternalIssueRef.ExternalKey`; the notifier
  renders `CARD-0170 (#14)` (`TrackerSyncSummaryFormatter.cs:111-127`, which for an import today
  renders `#3 (#3)`). Import-origin is the odd one out.
- **`UpdateExisting` re-asserts `Identifier = ExternalKey` on every sync** (`:321-325`), so any
  one-off rename of the 11 cards is reverted by the next tick unless that line goes.
- **Identifier allocation** is `CardService.NextIdentifierAsync` (`:831-848`): highest numeric
  suffix after the last `-` across the board's cards (archived included), +1. `#12` has no `-`, so
  imported keys are ignored by the sequence today; a Jira `ANT-9` would be counted as 9. The
  per-board unique index is `IX_Cards_BoardId_Identifier` (`AppDbContext.cs:881-883`, max length 100
  at `:851`).
- **Template variables:** `WorkflowDefinitionLoader.BuildPromptVariables`
  (`WorkflowDefinitionLoader.cs:214,218`) binds BOTH `issue.identifier` and `card.identifier` to
  `card.Identifier`; the default prompt is `Work on card {{ issue.identifier }}: {{ issue.title }}`
  (`:237`); `CardService.BuildPrompt` (`:851-855`) is the non-workflow twin.
- **Blocked-issue support is Linear-only.** `GitHubIssuesTracker.cs:296` and `JiraTracker.cs:105`
  emit `BlockedByExternalIds: []`; only `LinearTracker.cs:157` populates it. `blockedColumn`
  (`ExternalTrackerSyncService.cs:132-134`, first `!IsActive && !IsTerminal`) is the Backlog column on
  every default-shaped board.
- **Config extension point:** unknown scalar keys under `tracker:` land in
  `IssueTrackerConfig.Options` (`docs/workflow-tracker-block.md`; `sync_out_create`, `export_since`
  and `notify_channel` are read that way). No parser change is needed for a new key.

**Related:** CARD-0166 (the sync), CARD-0171 (the notifier that renders identifier + key),
CARD-0087 / CARD-0051 (a card landing in an active column without an explicit spawn is a hold, not
a launch), CARD-0054 (a reopen never spawns), CARD-0005 (identifiers are cited outside the database,
so the sequence only moves forward).

---

## Verdict up front — the six decisions

1. **CARD-0170: imports land where manual creates land, and the tracker stops owning the
   non-terminal column.** New `TrackerLandingColumn.Resolve(board, config)` returns the board's
   `CardStatus.Backlog` column (fallback: first non-active non-terminal, then first column). Used by
   `UpsertIssuesAsync` for creates AND by `ApplyExternalReopens` for reopens. In this mode the
   tracker moves a card only across the terminal boundary (closed → terminal via `MarkInactive`,
   unchanged; cursor-proven reopen → landing column). `shouldMoveForTrackerState` does not fire
   between non-terminal columns. §1.
2. **The E10 "tracker is the queue" behaviour survives as a per-board opt-in, default off:**
   `tracker.import_column: active` (default `backlog`). With `active`, today's code path is
   byte-for-byte what runs: first `IsActive && !IsTerminal`, tracker state owns the non-terminal
   column, blocked → waiting column, unblocked → active. The two E10 tests set the key and keep
   passing; the default gets new tests. §1, §2.
3. **CARD-0175: every card gets a `CARD-nnnn` identifier; the tracker's key lives only in
   `ExternalIssueRef.ExternalKey`.** This is the export-origin shape already in production, applied
   to import-origin. `ValidateCardId` is **not** touched — `#3` stays rejected, and a new test says
   so. A sanitising transform was rejected: it fixes the launch but leaves the `#5` resolver
   collision, the indistinguishable `#12`/`#12` display, and the orphan-recovery round-trip
   (`ListAsync:124` ⇄ `:1562`) as open wounds. §3.
4. **Allocation moves out of `CardService` into a shared `CardIdentifierAllocator`** so a batch of N
   imports gets N distinct numbers in one `SaveChanges`; a lost race against a manual create is a
   unique-index failure the sync reports with the constraint name and retries on the next tick. §3.
5. **Backfill is an EF migration, in the same deploy as the code that stops re-asserting `#N`.**
   Renames every linked card whose `Identifier` is not `CARD-`shaped to the next free number on its
   board (ordered by `CreatedAt`, then the key's number, so `#3` → the lowest new number), and deletes
   the renamed cards' exhausted `RetrySchedules` — those three failures were the bug's, not the
   cards'. §4.
6. **The GitHub number stays visible and addressable in the new shape:** `CardDto.ExternalIssue
   {Key, Url, TrackerKind}`, rendered as a link in the card modal and as a dimmed tag on the board
   card; `issue.identifier` / new `issue.url` template variables bind to the external key/url when the
   card is linked (`card.identifier` stays `CARD-nnnn`); `ResolveCardIdAsync`'s foreign-key arm
   also matches `ExternalIssueRef.ExternalKey`, so `card.ps1 get ANT-12` keeps working on a Jira
   board. `#N` keeps meaning `CARD-000N` everywhere — a GitHub issue is addressed by its card or its
   URL, never by `#N`. §5.

---

## 1. Decision 1 — the landing column

**Why not "just use `CardStatus == Backlog`" and stop there.** The create site is one of three places
the sync pushes a card into the active column; changing only the create leaves `UpdateExisting`
dragging every unowned non-terminal card back on the next tick (live-proven at 15:07:25 on all
eleven) and `ApplyExternalReopens` auto-dispatching reopens. All three go through one resolver.

**Why not "define a new column concept".** `IsActive` already has a precise meaning (dispatch
eligibility) and `CardStatus.Backlog` already names intake — `CardService.CreateAsync` has used it
as the intake rule since the board model existed. A third flag would be a second way to say
"Backlog".

**Rule, default mode (`import_column: backlog`):**

| Event | Card state | Action |
|---|---|---|
| New open issue | — | create in landing column, `Status = column.CardStatus`, no hold needed (column is not active) |
| Open issue | card non-terminal, any non-terminal column | **no move** — column is Antiphon's; title/body/labels/priority still updated per origin authority |
| Open issue | card terminal, cursor `closed` | reopen → landing column (`ApplyExternalReopens`), revision reason unchanged |
| Issue leaves active states / closed | card non-terminal, unowned | → terminal column (`MarkInactive`, unchanged) |
| Blocked / unblocked | any | ignored in this mode (Linear-only signal; a human decides when blocked work moves) |

`TerminalReason = "External tracker blockers are not terminal."` and its clearing arm
(`:363-370`) become `active`-mode-only.

**Rule, opt-in mode (`import_column: active`):** exactly today's code, including `blockedColumn`,
`shouldMoveForTrackerState`, and reopen → first active. Not a deprecation — a board whose tracker
genuinely is its queue (Linear "Todo" = go) is a legitimate configuration; it is just not the
default, because on the default column shape it means "every open issue starts an agent", which is
the CARD-0087 hole re-opened by the sync. A value other than `backlog`/`active` is a parser-level
validation error surfaced on workflow save (same place `kind` is validated), never a silent default.

**Blast radius:** `PollTick_syncs_external_tracker_issue_into_card_and_dispatches_it` and
`PollTick_dispatches_external_issue_only_after_blockers_terminal` add `import_column: active` to
their front matter; `External_tracker_sync_updates_existing_card_without_duplicating_reference`
(`:178`) and `..._marks_cards_terminal_when_issue_leaves_active_states` (`:251`) are unaffected
(no column assertions on the open path; the terminal path is unchanged). Every other tracker
consumer (`ReconcileStaleIssuesAsync`, label/comment/state OUT, notifier) is column-agnostic.

## 2. Decision 2 — where the switch lives

`import_column` is a scalar under `tracker:`, read from `IssueTrackerConfig.Options` like
`sync_out_create` — one new row in `docs/workflow-tracker-block.md`, no parser signature change. It
is per board because the semantic is per board (what a tracker state means to *this* team), and it
is in the workflow YAML because that is where every other tracker decision is made and revisioned.

Not a server setting: an `Orchestrator:` knob would make every tracked board flip together.

## 3. Decision 3 — the identifier scheme (CARD-0175)

**Three options were costed:**

| | A. widen `CardIdPattern` to admit `#` | A′. sanitise at the worktree site (`#12` → `issue-12`) | **B. `CARD-nnnn` for every card; key in `ExternalKey`** |
|---|---|---|---|
| Launch works | yes | yes | yes |
| `#5` resolver 409 (live) | stays | stays | **gone** — `#5` ⇒ `CARD-0005` only |
| `#12` vs `#12` on the board | stays | stays | **gone**; GH number shown as a tag |
| Orphan recovery `ListAsync:124` ⇄ `:1562` | ok | **breaks** without metadata (`issue-12` ≠ `#12`) | ok |
| Validator strength | **weakened** for every identifier shape | unchanged | unchanged |
| Branch/dir `feat/card-#12` in URLs, PowerShell `#` at token start, `gh` | awkward | fine | fine |
| Sequence (`NextIdentifierAsync`) | ignores `#N` | ignores `#N` | consistent; Jira `ANT-9` no longer bumps the CARD sequence |
| Notifier `#3 (#3)` | stays | stays | `CARD-0176 (#3)` |
| Consistency with export-origin cards | no | no | **same shape** |
| Backfill needed | no | no | yes (11 rows, one SQL statement) |

A is ruled out by the brief's own constraint. A′ fixes the symptom on the card and nothing that
caused it to be filed alongside CARD-0170. B is the only option under which "a card on this board"
has one identifier scheme — which is what `cardIdentifier.ts`, `ResolveCardIdAsync`,
`docs/antiphon-api.md` and `card.ps1` already assume.

**Changes:**

- `ExternalTrackerSyncService.UpsertIssuesAsync:233` — `Identifier = allocator.Next()` where
  `allocator = await CardIdentifierAllocator.ForBoardAsync(_db, board.Id, ct)` is built once per
  board, only when at least one issue will be created.
- `UpdateExisting:321-325` — **deleted.** `ExternalKey`/`Url`/`RawPayloadJson` re-assertion
  (`:327-345`) stays.
- `CardService.NextIdentifierAsync` becomes a call into the same allocator (same parse, same
  archived-included rule, same forward-only guarantee; the doc comment moves with it).
- `CardIdentifierAllocator`: `ForBoardAsync` loads the board's identifiers and computes `highest`
  exactly as today; `Next()` returns `CARD-{++highest:0000}`. No table, no sequence object: the
  unique index is the arbiter, as it is today for two concurrent manual creates.
- Race: sync tick vs. manual create on the same board in the same ~100 ms. The sync's
  `SaveChangesAsync` throws `DbUpdateException` on `IX_Cards_BoardId_Identifier`; `SyncAsync`
  catches it around the save, logs Warning **with the constraint name from the inner exception**
  (AGENTS.md: never report a DB failure without the DB's own message), and returns — the next tick
  re-reads and re-allocates. No retry loop inside the tick; 30-minute cadence makes the window
  irrelevant and a loop would hide a real duplicate.
- `WorktreeManager` — untouched. A regression lock is added: `#3` joins the rejected set in
  `Sanitise_rejects_path_traversal_and_special_chars`, and the sync integration test asserts
  `WorktreeManager.ValidateCardId(card.Identifier)` passes for the imported card — the
  cross-boundary contract that was missing.

**What `Identifier` no longer means for Jira/Linear boards:** today a Linear import is `ANT-12`;
after this it is `CARD-nnnn` with `ExternalKey = ANT-12`. That is a change for boards that do not
exist yet in this deployment (zero Jira/Linear boards, live), and it is the same change the
notifier, resolver and templates get for GitHub — one scheme, key on the ref. Decision 6 keeps
`card.ps1 get ANT-12` and `{{ issue.identifier }}` = `ANT-12` working.

## 4. Decision 5 — backfill

**Vehicle:** EF migration `RenameImportedCardIdentifiers` (`server/Migrations/`), raw SQL in `Up`.
It runs at server start (AGENTS.md: migrations run automatically), i.e. in the same deploy as the
code that stops re-asserting `#N`, so no tick can revert it. Not a script: a script needs the
server stopped or the sync paused, and an operator to remember to run it.

**Selection:** cards with an `ExternalIssueRef` whose `Identifier !~ '^CARD-[0-9]+$'`. Live that is
exactly the 11 GitHub cards; nothing else matches (verified). Export-origin cards are already
`CARD-`shaped and are not touched.

**Numbering:** per board, `highest` = `MAX` of the numeric suffix after the last `-` over ALL the
board's cards (archived included, mirroring `NextIdentifierAsync`), then
`row_number() OVER (PARTITION BY BoardId ORDER BY CreatedAt, <numeric part of the key>, Identifier)`
— the 11 share one `CreatedAt`, so the key's number orders them: `#3` gets the lowest new number,
`#13` the highest. At today's `highest = 175` that is `CARD-0176`…`CARD-0186`; the migration
computes it at run time, so cards filed between now and deploy simply shift the range.

**Also in `Up`:** `DELETE FROM "RetrySchedules" WHERE "CardId" IN (<renamed>)` — the exhausted
3/3 schedules are the bug's residue. (In default mode they are inert anyway — the tick never
dispatches from Backlog — but a card moved to active with `-Spawn` later should not carry a
"failed three times" schedule that never gets reset.) `UpdatedAt` and `ConcurrencyToken` rotate on
the renamed rows so any client holding a stale token gets a clean 409.

**No `CardRevision` row:** `CardRevisionKind` has Move / ContentEdit / Reopen / ArchiveChange
(`CardRevisionLog.cs:28-95`); an identifier has never changed before and adding a kind for an
eleven-row one-off is not worth it. The old value is not lost — it is the card's `ExternalKey`, shown
on the card after Decision 6. The migration's summary comment lists the mapping. `Down` is a no-op
with a comment: the sequence must not run backwards (CARD-0005).

**External citations of `#3`…`#13`:** none in git (`grep` of `.antiphon/`, docs, commits — the
only `#N` mentions are CARD-0170/0175's own text, which describe the bug). Nothing to rewrite.

## 5. Decision 6 — the key stays visible and addressable

After the rename the GitHub number would vanish from the UI (the client has no external-ref field
today) and from agent prompts (which say `Work on card #3` today and would say `CARD-0176`). Both
are regressions the plan owns:

- **DTO:** `CardDto.ExternalIssue` (nullable record `ExternalIssueDto(TrackerKind, Key, Url)`),
  mapped in `BoardService.ToCardDto` (`BoardService.cs:217`); the board-columns query and
  `CardService.LoadCardAsync` add `.Include(c => c.ExternalIssueRef)` (neither includes it today —
  verified by grep).
- **Client:** `client/src/api/boards.ts` `Card.externalIssue?`; `CardModal.tsx:91` renders
  `#176 · GH #3 ↗` (link to `Url`, `rel=noopener`); `BoardCard.tsx:36` and `CardRow.tsx:42` append
  a dimmed `GH #3` after the identifier when present. `displayIdentifier` is untouched.
- **Prompts:** `WorkflowDefinitionLoader.BuildPromptVariables` — `issue.identifier` ⇒
  `ExternalIssueRef?.ExternalKey ?? card.Identifier`; new `issue.url` and `issue.tracker`;
  `card.identifier` unchanged. Default prompt and `CardService.BuildPrompt` become
  `Work on card CARD-0176 (GitHub issue #3, <url>): <title>` when linked — so an agent that writes
  `Fixes #3` in a commit gets GitHub's autolink, and one that writes `CARD-0176` gets ours.
- **Resolver:** `ResolveCardIdAsync`'s foreign arm (`CardService.cs:234`, `PREFIX-digits`) also
  matches `ExternalIssueRefs.ExternalKey` exact/case-insensitive, and the 409 message says
  "on more than one card" (today's text blames boards for a same-board collision). `#N` is
  deliberately NOT routed to a GitHub key — it is the canonical `CARD-000N` form in three places
  and a documented CLI form. `docs/antiphon-api.md` §"Card identifiers" gains one sentence: a foreign
  tracker's key resolves through the card's external ref.
- **Notifier:** no change — `CARD-0176 (#3)` falls out of the existing formatter.

## 6. Out of scope, stated

- Resolving a GitHub number from the CLI (`gh#3` / `GH-3`). One regex arm on the resolver if it
  is ever wanted; the card modal link covers the need today.
- Why the read-only tick sync produced no revision after 18:48 despite due ticks. Needs its own
  observation (token resolution on the tick path, a success/skip log line). File a card if the
  next tick after deploy does not sync.
- Auto-resetting `RetrySchedule` on manual moves generally (the 11-row delete is targeted).
- A migration for future boards' column shapes; `CreateDefaultColumns` is right as it is once
  the sync stops reading `IsActive` as intake.

## 7. Verification / test design

All in `tests/Antiphon.Tests` unless marked; integration tests use `TestDbFixture` and scope every
assertion to the rows the test made (AGENTS.md).

**Allocator (unit, `CardIdentifierAllocatorTests`)**
- highest+1 across `CARD-0007`, `CARD-0003` ⇒ `CARD-0008`; archived rows counted.
- `#12`, `ANT-9`-shaped rows: `#12` ignored, `ANT-9` counted (documents the parse; unchanged).
- `Next()` ×3 on one instance ⇒ three consecutive distinct values.

**Sync — identifier (integration, `ExternalTrackerSyncIdentifierTests`)**
- T1 `Imported_issue_gets_a_card_identifier_and_keeps_the_tracker_key_on_the_ref`: GitHub fake
  issue `#3` ⇒ `Identifier == CARD-0001` on an empty board, `ExternalKey == "#3"`,
  `WorktreeManager.ValidateCardId(card.Identifier)` does not throw.
- T2 `A_batch_of_imports_gets_distinct_consecutive_identifiers`: three issues in one sync ⇒
  `CARD-0001..0003`; a second sync changes none of them (the `:321` re-assertion is gone).
- T3 `Hash_N_resolves_to_the_card_not_the_import`: board with manual `CARD-0005` + imported `#5`
  ⇒ `ResolveCardIdAsync("#5")` returns the manual card; `ResolveCardIdAsync("#5")` before this
  change is the live 409 (write the test red first).
- T4 `Foreign_key_resolves_through_the_external_ref`: Linear import `ANT-12` ⇒
  `ResolveCardIdAsync("ANT-12")` returns the card whose `ExternalKey` is `ANT-12`.
- T5 `A_lost_identifier_race_is_reported_with_the_constraint_name_and_retried_next_tick`: pre-insert
  `CARD-0001` between allocator build and save ⇒ one Warning naming
  `IX_Cards_BoardId_Identifier`, no card created, the next `SyncAsync` creates `CARD-0002`.

**Sync — landing column (integration, `ExternalTrackerSyncLandingColumnTests`)**
- T6 `Default_import_lands_in_the_backlog_column_and_the_tick_does_not_dispatch_it`: default
  columns ⇒ card in the `CardStatus.Backlog` column, `Status == Backlog`, `StartedAt == null`;
  `PollTickAsync` ⇒ `Dispatched == 0`, no `AgentSession` for the card.
- T7 `Default_mode_never_moves_a_non_terminal_card_for_an_open_issue`: card moved by hand to
  In Progress, then to Review; two more syncs ⇒ column unchanged both times, zero
  `external-tracker` revisions.
- T8 `Default_mode_still_moves_closed_to_terminal`: issue leaves active states ⇒ terminal column
  (asserts the unchanged arm still fires with the new resolver in place).
- T9 `Default_mode_reopen_lands_in_backlog_not_active` (`TrackerBidirectionalSyncTests`):
  cursor-proven GitHub reopen ⇒ landing column, `StartedAt` untouched, tick dispatches 0.
- T10 `Import_column_active_keeps_the_e10_behaviour`: the two existing E10 tests gain
  `import_column: active` in their YAML and pass unchanged; one new assertion that with the key
  set the reopen goes to the first active column.
- T11 `Unknown_import_column_value_is_a_validation_error_on_save` (`IssueTrackerConfigParserTests`
  or the workflow-save test that already covers `kind`).
- T12 `Landing_column_falls_back_when_the_board_has_no_backlog_status_column`: columns
  `[Todo(InProgress, active), Done(terminal)]` ⇒ first non-active non-terminal, then first column.

**Worktree (unit, existing `WorktreeManagerSafetyTests`)**
- `#3` added to `Sanitise_rejects_path_traversal_and_special_chars` — the validator was not widened.

**Templates / prompt (unit)**
- `BuildPromptVariables` with a linked card ⇒ `issue.identifier == "#3"`, `issue.url` set,
  `card.identifier == "CARD-0176"`; unlinked ⇒ both identifiers equal.

**Client (vitest, via `pwsh -File scripts/test-client.ps1`)**
- `CardModal` renders the external link when `externalIssue` is present and nothing when absent;
  `displayIdentifier` tests unchanged.

**Live verification after deploy (S3, in order):**
1. `psql`: `select "Identifier", e."ExternalKey" from "Cards" c join "ExternalIssueRefs" e …` ⇒
   `CARD-0176..0186` ⇄ `#3..#13`; `select count(*) from "RetrySchedules" where "CardId" in (…)` ⇒ 0.
2. `GET /api/cards/%235` ⇒ 200, `CARD-0005`.
3. Wait one tick interval (or `POST /api/boards/8988ca03…/tracker/sync` without `notify`) ⇒ no
   `external-tracker` revision on any of the 11; they stay in Backlog.
4. `card.ps1 move CARD-0176 -To "In Progress" -Spawn` ⇒ session starts, `git worktree list` shows
   `feat/card-CARD-0176`, agent prompt cites `GitHub issue #3`. Then stop the session and move the
   card back (or let it work — operator's call; the plan's proof is the worktree line).

## 8. Build order

Slices are independently green and committed; S1 and S2 can be built in parallel by two delegates
(different files: S1 is identifier/resolver/templates, S2 is the column resolver and the two E10
tests). S3 must follow S1 (the migration must not deploy ahead of the code that stops re-asserting
`#N`). S4 follows S1 (needs the DTO field's semantics).

- **S1 — one identifier scheme.** `CardIdentifierAllocator`; `UpsertIssuesAsync` allocates;
  `UpdateExisting:321-325` deleted; `SyncAsync` catches the unique-index failure with the
  constraint name; `ResolveCardIdAsync` foreign arm + message; `BuildPromptVariables` /
  `BuildPrompt` / default prompt; `WorktreeManagerSafetyTests` `#3`; T1–T5, template unit tests.
  Commit: `fix(tracker-sync): CARD-0175 S1 - imported cards get CARD-nnnn identifiers; key stays on the ref`.
- **S2 — landing column.** `TrackerLandingColumn.Resolve`; default-mode rules in
  `UpsertIssuesAsync`/`UpdateExisting`/`ApplyExternalReopens`; `import_column` read + validation;
  E10 tests opt in; T6–T12; `docs/workflow-tracker-block.md` row + a "Landing column" paragraph.
  Commit: `fix(tracker-sync): CARD-0170 S2 - imports land in Backlog; tracker owns only the terminal boundary; import_column opt-in`.
- **S3 — backfill + deploy.** Migration `RenameImportedCardIdentifiers` (rename + RetrySchedule
  delete, no-op Down); `pwsh -File scripts/restart-apphost.ps1`; live checks §7.1–7.4; AGENTS.md
  gotcha bullet (three sentences: `#N` is CARD-000N and nothing else; the tracker never picks a
  non-terminal column unless `import_column: active`; the worktree validator is the last line, not
  the first). Commit: `feat(tracker-sync): CARD-0175 S3 - backfill the 11 imported identifiers, reset their retry schedules`.
- **S4 — the key on the card.** `ExternalIssueDto` on `CardDto` + Includes; client link/tag;
  vitest. Commit: `feat(board): CARD-0175 S4 - show the tracker key and link on linked cards`.
- **Close:** `docs/antiphon-api.md` sentence (can ride S1); CARD-0170 and CARD-0175 closed with
  reasons naming the migration's mapping and the live worktree line.
