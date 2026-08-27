# CARD-0122 — A card that needs a human decision, as a real state, designed

**Date:** 2026-08-27
**Status:** planned (design only — nothing here is implemented)
**Card:** CARD-0122 (`2338c01d-a1d7-4a4d-ad28-8d0cffa0ac0e`), paired with CARD-0123 (surfacing) and
adjacent to CARD-0100 (card-to-card dependency) and CARD-0197 (`NEEDS_ANSWER` auto-progression).
**Model followed:** `docs/superpowers/plans/2026-08-26-card-0036-away-digest-plan.md` (verdict first;
"what exists, verified against the code" with file:line; then the design; then the slices).

## Verdict, in one screen

| Question the card asks | Answer |
|---|---|
| Is `CardStatus.Blocked` the right vehicle, or is a label/flag more honest? | **Reuse the slot, rename it.** `Blocked = 4` already has the state machine, the client type, the `danger` colour, a tracker label, and an "All boards" column — everything except a column on a real board. It has **zero** live usage (170 boards, 0 columns, 0 cards — §1.1), so renaming it to **`NeedsDecision`** is free of data migration (both status columns are `integer` in Postgres) and settles the CARD-0100 collision at the root: "blocked by a card" becomes an *edge* with its own word, "needs a decision" is a *state*. A label is rejected on evidence: CARD-0010 already carried a `decision` label and it was invisible for twelve days (§1.5). |
| Does any board use a Blocked column today? | **No.** Live query 2026-08-27: 170 boards, every one the same four columns (`Backlog / In Progress / Review / Done`), `SELECT count(*) FROM "BoardColumns" WHERE "CardStatus"=4` → **0**, and no card has ever held status 4. |
| Activate via `WorkflowDefinitionLoader` / `UpdateBoardWorkflowRequest`? | **Not possible — that path does not own columns.** `WORKFLOW.md` front matter carries `name`, `agent`, `tracker`, `hooks` and stages; columns are DB rows written once by `BoardService.CreateDefaultColumns` (§1.2) and there is no column CRUD endpoint. Project-wide activation is therefore: a fifth default column **plus one idempotent data migration** that gives every existing board the column (§2.3). |
| Reuse the `WaitingForHumanReview` visual language? | **No — and the reasons are structural, not aesthetic.** All three existing concepts are *agent-side pauses*: an agent on a permission prompt, a workflow stage on a gate, a delegate task on a question. They are transient, cleared by the runtime or by an answer typed into a session, and two of them are literally badged **"Review"** — the word this card exists to disambiguate. The card state is durable, cleared only by a human *move*, and its subject is the work item, not a process. What IS reused: the **help-circle icon and the "waiting on a human" vocabulary** of `AttentionKind.BlockedQuestion` (§1.4), which is the same meaning one level down. |
| Queryable without scanning descriptions? | **Two ways, both structural.** Per board: `GET /api/boards/{id}` → the column whose `cardStatus == "NeedsDecision"`. Fleet-wide: a new **`AttentionKind.CardNeedsDecision`** row on `GET /api/attention` — one per card in the state, `Evidence` = the *required* move reason (the question itself), `SinceUtc` = the move revision's timestamp (§2.4). |
| Collision with CARD-0100? | **Resolved by vocabulary, recorded in both cards.** CARD-0100 must be modelled as a relationship (`CardDependency` / "waiting on CARD-x"), never a status, and must not use the word "blocked" in UI copy. This design frees that word by not using it anywhere (§2.6). |
| CARD-0010 as first usage? | **Stale premise.** CARD-0010 was closed **Done on 2026-08-21** with all three decisions recorded in its `terminalReason` — the same day CARD-0122 was filed. Reopening a settled card to demonstrate a column would invent history (`CardStateMachine.cs:17-22`). Verification uses a throwaway card (§4); the live audit found **no** current card that is a decision request wearing a Review label (Review holds 0 cards today — §1.5). |

**What is genuinely new:** one enum rename, one fifth default column, one hand-written data
migration, one "reason required" rule on the move, one `IsSpawnable` exclusion, one appended
`AttentionKind` + two appended DTO fields + one appended `AttentionAction`, one label map and one
badge on the client, one line in `card.ps1`. No new tables, no new endpoints, no new column API.

---

## 1. What exists today (verified against the code and the live server, 2026-08-27)

### 1.1 `CardStatus.Blocked` is fully wired everywhere except onto a board

- `server/Domain/Enums/CardStatus.cs:9` — `Blocked = 4`, `Canceled = 5`.
- `server/Domain/StateMachine/CardStateMachine.cs:26-37` — every live state reaches every other
  directly (widened 2026-08-13; the comment at `:7-25` explains why a path-forcing machine was
  wrong). `Blocked` is in `AnyLiveState` and has its own row. `CanReopenFrom` (`:62-63`) is
  Done/Canceled only.
- `client/src/api/boards.ts:5` — `CardStatus` union includes `'Blocked'`.
- `client/src/features/board/boardShapeModel.ts:157-181` — `canMoveTo`, `canReopenFrom`,
  `legalMoveTargets` (lockstep pair with the server machine, pinned by
  `boardShapeModel.test.ts:235,248`).
- `client/src/features/board/boardVisuals.ts:8-15` — `Blocked: 'danger'` while `Review: 'warning'`.
  The file's own rule (`:3-7`): colour is a *second* encoding; every state must also be
  name-labelled.
- `client/src/features/board/BoardPage.tsx:64-77` — `ALL_CARD_COLUMNS` for the "All boards" view
  already synthesises a `Blocked` column (stateKey `blocked`, index 4) and a `Canceled` one; both
  have rendered empty since they were written.
- `server/Application/Services/TrackerSyncMarkers.cs:82-91` — `StatusLabel` emits `status:blocked`;
  `TrackerBidirectionalSyncService.cs:538-560` closes the GitHub issue only for a terminal column,
  so a Blocked card keeps its issue open. `:198-201` treats only Done/Canceled as terminal on the
  inbound reopen path. The Antiphon board is `trackerKind: GitHubIssues`, so this matters.
- `docs/agent-card-lifecycle.md:5` already lists `Blocked` as a card status.
- Storage: `information_schema` reports `Cards.Status` and `BoardColumns.CardStatus` as `integer`;
  there is no `HasConversion<string>` anywhere under `server/Infrastructure/Data`. A member rename
  changes the wire string only.

### 1.2 Columns are created once, by code, and cannot be edited afterwards

- `server/Application/Services/BoardService.cs:309-317` — `CreateDefaultColumns`: the four rows.
  Called from `BoardService.CreateAsync` (`:135`) and `AgentService.cs:935` (an agent's own board).
- `server/Api/Endpoints/BoardEndpoints.cs:33-39` — `GET /{id}/columns` is the only column route;
  there is no POST/PATCH for columns. `MoveCardRequest` (`BoardDtos.cs:138-139`) takes a
  `BoardColumnId`; `CardService.MoveAsync` (`:265-320`) resolves it, refuses cross-board targets,
  applies the state machine in `ApplyColumnMove` (`:810-855`), and **clears `AutoDispatchHeldAt`
  on a move to any non-active column** (`:294-295`).
- `server/Application/Services/WorkflowDefinitionLoader.cs:126-164` — `Parse` reads front matter
  for `name`/hooks; `WorkflowDefinitionParser.cs:15-52` reads `name`, `description`,
  `selectableStages`, stages (`executorType`, `modelName`, `gateRequired`, `systemPrompt`).
  **Nothing about columns.** `UpdateBoardWorkflowRequest` (`BoardWorkflowDtos.cs:12`) is
  `(string Content)`. The card's proposed activation path does not exist.
- `server/Application/Services/TrackerLandingColumn.cs:35-48` — imported cards land on the column
  with `CardStatus == Backlog` (or, with `import_column: active`, the first `IsActive &&
  !IsTerminal`). Neither rule can select a new non-active, non-terminal column.
- `server/Application/Services/CardLifecycleTransitions.cs:16-53` — `TryMoveToReview` selects by
  `CardStatus == Review`; `DequeueFinishedCardAsync` (`:66-79`) treats Review/Done/Canceled as
  "finished from the agent's perspective" and pulls the card off its agent queue.
- `scripts/card.ps1:339-388` — `move` resolves `-To` by column **name, stateKey or guid**
  (`:362-365`), `close` picks the first terminal column (`:346`).

### 1.3 What decides whether an agent touches a card

- `server/Application/Services/OrchestratorService.cs:506-525` — `LoadEligibleCandidatesAsync`
  requires `BoardColumn.IsActive && !IsTerminal` (`:517`), `ArchivedAt == null`,
  `AutoDispatchHeldAt == null`, no owner, no live session. A non-active column is invisible to
  the tick by construction — no new filter needed.
- `server/Application/Services/AgentControlService.cs:479-485` — `IsSpawnable` (the agent-start
  queue walk at `:460-476`) excludes `Review or Done or Canceled` and archived cards, **not
  Blocked**. A Blocked card at an agent's queue head would be respawned on at the next agent
  start, and the non-spawnable branch logs a Warning worded for *finished* cards (`:470-473`).
  This is the one behavioural gap the new state must close.

### 1.4 The three (actually four) existing "waiting for a human" concepts

| Concept | Where | Subject | Lifetime | Cleared by | Rendered as |
|---|---|---|---|---|---|
| `AgentStatus.WaitingForHumanReview = 3` | `server/Domain/Enums/AgentStatus.cs:14` | an **agent process** on a permission/approval prompt | seconds–minutes | the runtime, when the prompt is answered | orange **"Review"** badge, `client/src/features/agents/AgentActivityBadge.tsx:31-36` and `client/src/features/home/AgentRail.tsx:140-146` |
| `CardWorkflowRunStatus` / `CardWorkflowStageStatus.WaitingForHumanReview = 2` | `server/Domain/Enums/CardWorkflowRunStatus.cs:7`, `CardWorkflowStageStatus.cs:7` | a **workflow run's stage** at a `gateRequired` gate | one stage | gate approval | `workflowRunStatus` on `CardDto` (`boards.ts:8`), no dedicated badge |
| `AgentTaskStatus.Blocked = 3` | `server/Domain/Enums/AgentTaskEnums.cs:58-59` ("the delegate asked a question — it needs an answer, not a retry") | a **delegate task** | until answered | `AgentTaskReplyService.AnswerAsync` (`:225-240`) typing the answer into the session | `AttentionKind.BlockedQuestion = 0` (`AttentionDtos.cs:16`), built at `AttentionService.cs:175-225` — Critical, headline "Blocked — waiting on a human answer.", actions Reply/Cancel/Escalate; client `attentionVisuals.ts:36-41` label **"Blocked"**, colour `warning`, icon `TbHelpCircle`; fed to the away digest at `AwayDigestProjection.cs:36` |
| `CardStatus.Blocked = 4` | §1.1 | a **card** | until a human moves it | a move | nowhere on a real board |

The first two are the wrong shape for this card: they describe a process waiting on an approval,
they are owned by the runtime, and their word is "Review". The third is the *same meaning* as
this card at task level — and it is the reason the UI language chosen here borrows its icon and
its "nothing moves this but a person" framing (`attentionVisuals.ts:130-135`, group `now`), not
the agent badge's.

### 1.5 The live board, and why a label is rejected

- Antiphon board (`8988ca03-…`): 215 cards — Done 157, Backlog 54, InProgress 4, **Review 0**,
  Blocked 0. Labels are free text; `decision` appears exactly once — on CARD-0010, which carried
  `["tests","e2e","decision"]` the whole time it sat unanswered. `cardMatchesFilter`
  (`boardShapeModel.ts:93-106`) can filter on it, but nothing else in the system reads labels:
  not the state machine, not dispatch, not `/api/attention`, not the digest. A label is a hint to
  a human already looking; the card's own history is the proof it does not work.
- CARD-0010 is `Done` since `2026-08-21T18:01:38Z`; its `terminalReason` records all three
  decisions and the follow-up cards (CARD-0102, CARD-0124).
- The scripted lookup `card.ps1 get CARD-0010` returns **409 "matches more than one card"**
  because the Gym Stat board also has a CARD-0010 — identifiers are per board
  (`CardIdentifierAllocator`), the script's lookup is global. Out of scope here; worth its own
  card.
- Audit of the 58 live cards for decision-shaped descriptions (title/labels/description matching
  `decision|needs a human|open question|awaiting`): no live card is a pure decision request.
  CARD-0090 ("escalate to the user when none are…") and CARD-0197 (`NEEDS_ANSWER`) *produce*
  decision requests once built; they are not ones.

### 1.6 CARD-0100 and CARD-0197, read for collision

- CARD-0100 asks for "blocked by another card" as modelled data and itself proposes a join table
  (`CardBlockedBy`) rather than a status. Its open question 2 is whether the edge gates dispatch.
  It never needs a `CardStatus` value; it needs an edge and, possibly, an eligibility filter.
- CARD-0197's `NEEDS_ANSWER` token is "blocked on a question only the operator/caller can
  answer" — exactly this state. Today it has no column to move a card into. After this plan it
  does, and the "reason required" rule (§2.2) gives it the slot for the delegate's question.

---

## 2. Design

### 2.1 The state: `CardStatus.NeedsDecision = 4`

Rename the member; keep the integer. One vocabulary end to end:

| Surface | Value |
|---|---|
| Enum / API JSON | `NeedsDecision` |
| Default column | name **"Needs decision"**, stateKey **`needs-decision`**, `IsActive=false`, `IsTerminal=false`, `ColumnOrder=4` (appended after Done) |
| Tracker label | `status:needs-decision` |
| Client colour | `danger` (unchanged slot), label "Needs decision" |
| Attention row | `AttentionKind.CardNeedsDecision`, label "Needs decision" |

Why append at order 4 rather than insert before Review: the strip (`ShapeStrip.tsx`,
`StatePager.tsx`) reads column order as the process spine; "Backlog → In Progress → Review →
Done" stays intact and the exception state hangs off the end — the same position
`ALL_CARD_COLUMNS` already gives it. Inserting would also renumber two existing rows in the
migration for no functional gain. Discovery is not the column's job; it is CARD-0123's and the
attention row's (§2.4).

Why rename rather than keep `Blocked`: the API says `Blocked`, the attention panel says
`Blocked` for a *task*, CARD-0100 wants "blocked by" for a *dependency*, and the column would
have to say something else to be honest. Three meanings on one word is the collision the card
warns about. The rename costs ~10 mechanical edits (§3, slice 1) and no migration; leaving it
costs a permanent footnote in every doc. If the operator prefers to keep the name, slice 1 is
the only slice that changes — everything else keys on the enum member, whatever it is called.

### 2.2 Entering the state: the reason IS the question

`CardService.MoveAsync` gains one rule: **a move into a column whose `CardStatus` is
`NeedsDecision` requires a non-empty `Reason`** (ValidationException on `Reason`: "A move into
Needs decision must say what decision is needed."). Every other target keeps Reason optional.

That reason is already persisted as the Move revision's `Reason`
(`CardRevisionLog.AppendMove`, `CardRevisionLog.cs:28`; row shape `CardRevision.cs:52-72`),
visible in `GET /cards/{id}/revisions` and the History tab. It becomes the attention row's
`Evidence` and the revision's `CreatedAt` its `SinceUtc` — time-in-state, which
`boardShapeModel.ts:9-16` says the board payload cannot derive. Nothing new is stored.

Side effects on entry, all existing code:
- `AutoDispatchHeldAt = null` (`CardService.cs:294-295`, non-active target) — correct: the hold is
  about a declined spawn; leaving the state should re-enter the normal rules.
- `AssignedAgentId` / queue position are **kept** — `DequeueFinishedCardAsync`
  (`CardLifecycleTransitions.cs:69`) keys on Review/Done/Canceled and must NOT gain
  `NeedsDecision`: the work is not finished, it is parked, and the same agent should resume it.
- Owner session, if live, is left alone. A human may still be talking to it.
- `IsSpawnable` (`AgentControlService.cs:481`) **gains `NeedsDecision`** so an agent start never
  respawns onto a parked card; the skip log at `:470-473` gets a Debug branch for it ("waiting on
  a human decision") instead of the "stale queue row" Warning.
- Orchestrator tick: already excluded by `IsActive == false` (`OrchestratorService.cs:517`).
- Tracker: issue stays open, label flips to `status:needs-decision`
  (`TrackerSyncMarkers.cs:89`); the managed-label rewrite already owns `status:*`.

### 2.3 Every board gets the column

- `BoardService.CreateDefaultColumns` (`:309-317`) gains a fifth entry:
  `NewColumn(board, "needs-decision", "Needs decision", 4, CardStatus.NeedsDecision, isActive:
  false, isTerminal: false, utcNow)`.
- A **hand-written, data-only migration** (precedent:
  `server/Migrations/20260825120000_BackfillNonRawModelArgumentName.cs:1-25` — `[DbContext]` +
  `[Migration]` attributes inline, no Designer, no snapshot change) inserts one `BoardColumns`
  row per board that has no row with `"CardStatus" = 4`, `ColumnOrder = max(ColumnOrder)+1`,
  `Id = gen_random_uuid()`, timestamps `now()`. Idempotent by construction. `Down` deletes rows
  with `CardStatus = 4` **only where no card references them** (a column holding cards is not
  droppable; the rollback refuses loudly rather than orphaning `BoardColumnId`).
- 170 boards receive it, ~165 of them empty test boards. Harmless; consistent with the
  "All boards" view which already assumes the state exists everywhere.

### 2.4 Queryability: `AttentionKind.CardNeedsDecision = 13`

Append to `AttentionKind` (`AttentionDtos.cs:13-97`; "a member is APPENDED, never renumbered").
Built in `AttentionService.GetAsync` (`:114-171`) as a new condition pass:

- Query: `Cards` where `Status == NeedsDecision && ArchivedAt == null`, joined to their latest
  `CardRevision` with `Kind == Move && ToStatus == NeedsDecision` (same shape as
  `AwayDigestProjection.cs:61-64` reads Review moves).
- Row: `Severity = Critical` (group "Needs you now — nothing moves these but a person",
  `attentionVisuals.ts:127-135`; that sentence is this state's definition), `Title =
  "{Identifier} — {Title}"`, `Headline = "Needs a decision — nobody can move this but you."`,
  `Evidence = revision.Reason`, `SinceUtc = revision.CreatedAt`, `SubtreeCostUsd = null`,
  `Actions = [OpenCard]`.
- Ordering falls out of the existing sort (`:163-166`): severity, then oldest first — so the
  decision that has waited longest sits at the top of the group, which is precisely the CARD-0010
  failure inverted.
- `AttentionItemDto` (`AttentionDtos.cs:152-164`) gains two **trailing, defaulted** parameters —
  `Guid? CardId = null, Guid? BoardId = null` — so the existing positional constructor calls
  compile unchanged. `AttentionAction` gains `OpenCard = 9` (`:105-131`, append).
- Away digest: `AwayDigestProjection.cs:36` filters `TaskId is not null`, so card rows do **not**
  leak into the Telegram "blocked" section. Whether they should is CARD-0123's / CARD-0036's
  call; this plan leaves the digest byte-identical.

An orchestrator therefore asks `GET /api/attention` and filters `kind == "CardNeedsDecision"`;
a board-scoped caller reads the column. Neither reads a description.

### 2.5 Client: name-labelled, not just red

- `client/src/api/boards.ts:5` — `'Blocked'` → `'NeedsDecision'`.
- `boardVisuals.ts:8-15` — key rename; add `export function stateLabel(status: CardStatus)`
  returning "Needs decision" for the new member (and the enum name for the others), used by
  `CardModal.tsx:118` which today renders the raw `card.status` string.
- `StateNode.tsx:63-64` — beside the `active`/`terminal` outline badges, a third:
  `{state.cardStatus === 'NeedsDecision' && <Badge size="xs" variant="outline"
  color="danger" leftSection={<TbHelpCircle/>}>needs a human</Badge>}`. Same icon as the task-level
  row (`attentionVisuals.ts:39`), so the two levels read as one family. Name + badge + icon satisfy
  the `boardVisuals.ts:3-7` rule that colour is never the only cue; Review keeps its plain amber.
- `BoardPage.tsx:75` — `ALL_CARD_COLUMNS` entry becomes `{ stateKey: 'needs-decision', name:
  'Needs decision', cardStatus: 'NeedsDecision', … }`.
- `MoveMenu.tsx:41-91` — when the chosen target's `cardStatus === 'NeedsDecision'`, the Reason
  textarea is required (submit disabled while empty, placeholder "What decision is needed?"), and
  the dialog copy says the card will leave every agent's reach until a human moves it. The same
  guard in `CardThreadPanel.tsx:330-345`'s move dialog.
- `attention.ts` — add `'CardNeedsDecision'` to `AttentionKind`, `'OpenCard'` to
  `AttentionAction`, `cardId`/`boardId` to `AttentionItemDto`. `attentionVisuals.ts:35` —
  `ATTENTION_VISUALS.CardNeedsDecision = { label: 'Needs decision', color: 'danger', icon:
  TbHelpCircle, hint: 'A card is parked on a decision only a person can make. Move it when you
  have decided.' }` (the `Record` type and `attentionVisuals.test.ts:52-68` force the entry);
  `targetOf` (`:183-188`) gains `if (item.cardId && item.boardId) return
  \`/boards/${item.boardId}?card=${item.cardId}\`` ahead of the task branch. `AttentionPanel.tsx:305-307`
  labels `OpenCard: 'Open card'`; the `switch` at `:372-397` routes it to the target link.
  Anything richer (counts on the rail, a cross-board section) is CARD-0123.

### 2.6 What CARD-0100 must do so the two never collide

Recorded here so the next Plan pass inherits it: CARD-0100 is an **edge**
(`CardDependency(BlockedCardId, BlockerCardId)` or similar), never a `CardStatus`. Its UI copy
uses "waiting on CARD-x" / "depends on", not "blocked". If it gates dispatch, it does so with a
filter in `LoadEligibleCandidatesAsync`, leaving the card in Backlog/In Progress. A card can be
both waiting on another card *and* needing a decision; the two facts live in different places
and neither overwrites the other.

### 2.7 Leaving the state

Any live column, via the existing move (state machine already fully connected). The move's
Reason is where the decision gets recorded — `card.ps1 move CARD-x -To in-progress -ReasonFile
decision.md -Spawn`. Nothing enforces a reason on exit; the History tab shows the pair. CARD-0197
may later automate entry (`NEEDS_ANSWER` → this column, reason = the question) and the
`AgentTaskReplyService.LooksLikeAQuestion` settlement (`:391`, `:1558`) is the obvious existing
detector to hang it on; both are out of scope here.

---

## 3. Slices (each independently buildable, testable and committable)

**Slice 1 — rename the slot (server + client, no migration).**
`CardStatus.cs:9`; `CardStateMachine.cs:26-37`; `TrackerSyncMarkers.cs:89` (`"needs-decision"`);
`boards.ts:5`; `boardVisuals.ts:13`; `boardShapeModel.test.ts:235,248`; `BoardPage.tsx:75`;
`docs/agent-card-lifecycle.md:5`; `docs/antiphon-api.md` wherever `Blocked` names a card status.
`AgentTaskStatus.Blocked`, `AgentTaskEventType.Blocked` and `AttentionKind.BlockedQuestion` are
task-level and **untouched**. Grep gate: `rg "CardStatus\.Blocked|'Blocked'" server client/src`
returns only task-status hits.

**Slice 2 — the column, everywhere.**
`BoardService.cs:309-317` fifth entry; new
`server/Migrations/20260827120000_AddNeedsDecisionColumnToEveryBoard.cs` (data-only, §2.3);
`tests/Antiphon.Tests/Application/ProjectDeletionTests.cs:184` `ShouldBe(4)` → `5`; any other
fixture that counts default columns (`AgentControlServiceIntegrationTests.cs:908`,
`ProjectReadinessTests.cs:602` build from `CreateDefaultColumns` and need no edit).
`scripts/card.ps1:363-365` — also match `$_.cardStatus` case-insensitively so `-To
needsdecision`, `-To needs-decision` and `-To "Needs decision"` all resolve.

**Slice 3 — entry rules.**
`CardService.MoveAsync` (`:265-320`): reason-required rule before `ApplyColumnMove`, message per
§2.2. `AgentControlService.cs:481` `IsSpawnable` exclusion + `:470-473` log branch. Confirm
`DequeueFinishedCardAsync` is deliberately NOT widened (comment at the call site).

**Slice 4 — the attention row.**
`AttentionDtos.cs`: `CardNeedsDecision = 13`, `OpenCard = 9`, two trailing DTO params.
`AttentionService.cs`: `BuildCardNeedsDecisionAsync` inserted after `BuildBlockedAsync` at `:141`.
Client `attention.ts`, `attentionVisuals.ts`, `AttentionPanel.tsx` per §2.5.

**Slice 5 — board visuals.**
`StateNode.tsx:63-64` badge; `boardVisuals.ts` `stateLabel`; `CardModal.tsx:118`;
`MoveMenu.tsx` and `CardThreadPanel.tsx` required-reason guard.

**Slice 6 — docs and card housekeeping.**
`docs/agent-card-lifecycle.md` gains a "Needs decision" section (entry rule, what agents do and
do not do with it, how to leave). `docs/antiphon-api.md` attention line (`:266`) mentions the new
kind. CARD-0100's description gets §2.6 appended as a constraint; CARD-0123's "verify against
CARD-0010" note is corrected (CARD-0010 is Done); CARD-0197's description notes the column and
the reason slot now exist.

Slices 1–3 are server-shaped and can go to a Codex/Grok build tier; 4–5 touch both sides and
should be one delegate; 6 is a docs/cards pass.

---

## 4. Tests (names to write; existing files in brackets)

Server (`tests/Antiphon.Tests/Application/…`):
- `BoardServiceIntegrationTests`: `A_new_board_has_a_needs_decision_column_after_done` (five
  columns; the fifth is `NeedsDecision`, non-active, non-terminal, order 4).
- New `NeedsDecisionColumnMigrationTests` against `TestDbFixture`: a board created with four
  columns before the migration SQL runs has five after; running the SQL twice adds nothing;
  `Down` refuses to drop a column that holds a card.
- `CardCorrectionIntegrationTests` (already exercises `MoveAsync`):
  `A_move_into_needs_decision_without_a_reason_is_refused` (400 on `Reason`),
  `A_move_into_needs_decision_records_the_reason_and_keeps_the_agent_assignment`,
  `A_move_into_needs_decision_clears_the_auto_dispatch_hold`,
  `A_move_out_of_needs_decision_needs_no_reason`.
- `AgentControlServiceIntegrationTests`:
  `An_agent_start_skips_a_needs_decision_card_at_its_queue_head_and_keeps_it_queued`.
- `OrchestratorServiceIntegrationTests`:
  `A_needs_decision_card_is_never_an_auto_dispatch_candidate`.
- `AttentionServiceTests` (`:58` pattern):
  `A_needs_decision_card_is_a_critical_row_whose_evidence_is_the_move_reason`,
  `The_row_dates_from_the_move_not_the_card`, `An_archived_needs_decision_card_is_not_listed`,
  `Leaving_the_state_removes_the_row`.
- `TrackerSyncMarkersTests`: `StatusLabel(NeedsDecision) == "status:needs-decision"`.
- `AwayDigestProjectionTests`: the Telegram digest is unchanged by a needs-decision card (guards
  §2.4's "byte-identical" claim).

Client (`pwsh -File scripts/test-client.ps1`):
- `attentionVisuals.test.ts`: add `'CardNeedsDecision'` to `ALL_KINDS` (`:18`); the totality
  tests then cover it; `targetOf` returns the board+card URL when `cardId`/`boardId` are set.
- `boardShapeModel.test.ts`: rename at `:235,248`; a fixture with the fifth column keeps
  `toHaveLength(4)` at `:88` only if that fixture is hand-built (it is — leave it) .
- `StateNode.test.tsx` (new or existing): the needs-decision state renders the "needs a human"
  badge; Review does not.
- `MoveMenu.test.tsx`: Move is disabled for a needs-decision target until a reason is typed.

E2E (`tests/Antiphon.E2E/CardCliE2ETests.cs:193-200` pattern): `card.ps1 move card-1 -To
needs-decision` without `-Reason` exits non-zero with the server's sentence; with `-ReasonFile`
the card reads `status: NeedsDecision` and `GET /api/attention` lists it.

Acceptance on the live stack, after deploy: create a throwaway card on the Antiphon board, move
it to Needs decision with a reason, confirm it appears in `/api/attention` and on the board strip
with the badge, move it back, archive it. No existing card is moved.

---

## 5. What this deliberately does not do

- No column CRUD API. Boards still cannot be reshaped by hand; the fifth column is a default.
- No auto-entry from delegate questions or `NEEDS_ANSWER` (CARD-0197), no auto-exit.
- No push notification, rail count or cross-board list (CARD-0123).
- No change to the Telegram digest (CARD-0036).
- No card-to-card dependency (CARD-0100) — only the vocabulary constraint in §2.6.
- No fix for `card.ps1 get` resolving identifiers across boards (§1.5).
- No reopening of CARD-0010.
