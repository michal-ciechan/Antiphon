# CARD-0100 card relationships: "blocked by" as a first-class relationship

Date: 2026-10-14. Stage: Plan; next stage: TestDesign. Plan task `4b032ed7-a8c2-44aa-8f01-3ed9b4f6d578`.
Inspected checkout: `e0877982c5dc41990c4e66b80cb47341a1bd0a41` (branch base). Tests, counts and
positive controls below were proposed by Plan; Plan executed none.

Verdict on the card: **(c) open, not split.** No internal-card relationship exists in the domain,
API, script, UI, orchestrator or docs (ground truth rows 1-4). The card's question 6 (a shared
model with CARD-0098 ordering) is moot: `Card.Position` shipped on the card itself (`Card.cs:51`,
`CardRevisionKind.Reorder`). The card stays one card worked in five slices; a smaller label-based
change was considered and rejected (D-1, OD-1).

Session checklist: this plan adds or changes no session that waits for input, so the CARD-1083
release checklist does not apply.

## Evidence and ground truth

| # | Card assumption / brief question | What the code actually does | Design consequence |
|---|---|---|---|
| 1 | Internal cards have no relationship column. | `server/Domain/Entities/Card.cs:5-128` has no relationship property; no entity named `*Relationship*`, `CardLink`, `ParentCard`, `BlockedByCard` exists under `server/` (grep, 2026-10-14). `CardComment.cs` is the only child row besides revisions. | New entity, table, migration (S1). |
| 2 | The Antiphon board is `TrackerKind.Internal`. | `GET /api/boards/8988ca03-…` reports `trackerKind: GitHubIssues`. `GitHubIssuesTracker.cs:330` and `JiraTracker.cs:112` return `BlockedByExternalIds: []`; only `LinearTracker.cs:158` fills it. | The external path never blocks this board. The design is tracker-independent and leaves `ExternalTrackerSyncService` untouched (D-2). |
| 3 | External "blocked" keeps a card out of active work; imitate it. | `ExternalTrackerSyncService.cs:530-587` resolves blocker state per sync; `:162-191` then places a blocked issue in the first non-active, non-terminal column (Backlog on this board) only when the tracker owns a non-terminal column. It is placement at sync, not a gate. | Not reusable: internal blockers need a read-time predicate, not a placement (D-3). |
| 4 | "Should a blocked card be excluded from `LoadEligibleCandidatesAsync`?" | `OrchestratorService.cs:819-867` pulls only cards in an active, non-terminal column with `ArchivedAt == null`, `AutoDispatchHeldAt == null`, no owner, no live session, retry due. The Backlog pull is done by the orchestrator session from the pipeline snapshot: `AgentTaskPipelineStatusService.cs:145-180` builds `investigateBacklog` (Status Backlog, not archived, no owner, not started, label `post-land-verification` excluded at `:170`); `ExpectationSnapshotReader.ReadBacklogAsync` (`:340-375`) feeds the watchdog's `EligibleBacklog`. | Three read sites share one predicate (D-5): tick eligibility, pipeline backlog (which also reports the blocked rows), watchdog backlog. |
| 5 | Cycles are possible once user-editable. | No write-time graph check exists anywhere for cards; identifier resolution is `CardService.ResolveCardIdAsync` (`:347-395`) through `CardIdentifierScope` with a board fence. | Write-time walk; 409 `card_relationship_cycle` (D-6). |
| 6 | Auto-clearing when the blocker is Done. | Nothing clears; the tracker path re-resolves every sync (row 3). Card closes already fan out through `CardTaskSettlement` and `CardChanged` events (`CardService.cs:1034-1048`, `:654`). | Rows persist; `resolved` is computed at read time from the blocker's `Status`/`ArchivedAt` (D-7). |
| 7 | Where the UI lives. | `CardModal.tsx:255-300` tabs plus a `CardDetails` sidebar; `BoardCard.tsx:52-57`, `CardRow.tsx:92-111` and `orchestrator/BacklogRow.tsx:90` all render `CardAxisBadges`. `useCard` fetches the full card on open (`CardModal.tsx:47`). | One `BlockedChip` inside `CardAxisBadges` covers tile, row and orchestrator backlog; the modal gets a Blocked-by section (S5). |
| 8 | Card history must record it. | `CardRevision` is one table with `CardRevisionKind` (`CardRevisionKind.cs`: ContentEdit, Move, Archive, Unarchive, Reopen, Reorder); `CardRevisionLog.Append*` (`CardRevisionLog.cs:25-140`) allocates from `Card.RevisionCount`; every write rotates `ConcurrencyToken` and saves through `SaveCardWriteAsync` (`CardService.cs:1543-1566`; duplicate revision number is a 409). The client `KIND_LABELS` is `Record<CardRevisionKind, string>` (`CardHistory.tsx:64-77`). | Two new kinds, one new `RelatedCardId` column on `CardRevision`, client labels (S1, S5; D-8). |
| 9 | Concurrency and archive rules for a child write. | Content/move/archive writes require the card token (`CardService.cs:596-600`, `:509`), refuse archived cards (`:512-513`, `:1330-1331`), and `ArchiveAsync` keeps rows (`Card.cs:68-74`). | Relationship writes take the blocked card's token, rotate it, refuse archived cards, never touch the blocker's token (D-4, D-9). |
| 10 | Privacy: card files must not leak. | `CardFilePublicCard.cs:8-33` is an explicit scalar whitelist; `CardTaskFileRenderer.RenderCard` (`:94-130`) renders only its fields. `docs/card-file-privacy.md:19-21` lists eligible public fields. | Relationships are not added to the projection or renderer in this card; `CardTaskFileRendererTests` (11) is the regression row (D-10). |
| 11 | Attention feed / decision questions. | `AttentionKind` (`AttentionDtos.cs:13`) is BlockedQuestion, ParkedMessage, DeadSession, NeverStarted, UncorrelatedReport: task and session states. `AgentTaskStatus.Blocked` is a task waiting on a decision, not a card relationship. | No attention item for a blocked card (D-11). |
| 12 | Spawn, parent/child, follow-ups. | `POST /api/cards/{id}/spawn` (`CardEndpoints.cs:171`, `CardService.SpawnAsync:1322`) starts a session and lifts `AutoDispatchHeldAt`; it links nothing. Follow-up companions are prose plus a label and a stable key in the description (`docs/orchestration-loop.md:1808-1810`). | Out of scope; the enum leaves room for `RelatesTo` later (D-12). |
| 13 | A label convention already gates the backlog. | `AgentTaskPipelineStatusService.cs:170` hard-codes `post-land-verification`; labels are a JSON string list (`Card.LabelsJson`) with no inverse index and are public card-file fields (row 10). | Rejected as the primary design (D-1). |
| 14 | card.ps1 surface. | Verbs at `scripts/card.ps1:80`; `get` prints fixed lines (`:666-701`), `history` prints kind/reason (`:703-718`); helpers `Get-CardOrFail:395`, `Resolve-Token:405`, `Get-RequiredReason:411`. Script tests stub the API with `HttpListener` (`CardListSearchScriptTests.cs:1-50`). | `block`/`unblock` verbs and `get` lines (S4). |
| 15 | Platform. | `GET /api/runner-defaults` revision 2: `globalRunnerId` server2, no kind overrides. `GET /api/session-runners`: desktop (windows), server2 (linux), stale `server2-temp`. | Rows run on the default placement; omit `-Runner` and `-Platform`. |

## Decisions

- **D-1 Join table, not a label convention.** A `blocked-by:CARD-nnnn` label parsed by card.ps1
  and the pipeline (OD-1 alternative) was rejected: labels have no inverse ("blocks") without
  scanning every card's `LabelsJson`, carry no author/reason/history row, publish to card files
  (row 10), and the pipeline's label string is already a hard-coded special case (row 13). The
  card asks for modeled data; a parsed label is prose in different clothes.
- **D-2 External-tracker path untouched.** `IIssueTracker.BlockedByExternalIds` and
  `ExternalTrackerSyncService` are not edited or reused (rows 2-3).
- **D-3 Entity `CardRelationship`** (`server/Domain/Entities/CardRelationship.cs`): `Id`,
  `CardId` (the blocked card), `RelatedCardId` (the blocker), `Kind` (`CardRelationshipKind`
  enum, `BlockedBy = 0` only in this card), `Reason` (nullable, `CardService.MaxReasonLength`),
  `CreatedBy` (nullable free text, same stance as `CardRevision.EditedBy`), `CreatedAt`.
  Table `CardRelationships`; unique index `IX_CardRelationships_CardId_RelatedCardId_Kind`;
  index on `RelatedCardId`; check constraint `CK_CardRelationships_NotSelf`
  (`"CardId" <> "RelatedCardId"`); both FKs `OnDelete(Cascade)`. Board and project deletion physically delete the board's cards
  (`ProjectCascade.DeleteBoardsAsync`, `ProjectCascade.cs:127`); deleting either card removes its
  rows (T-3). Navigations `Card.BlockedBy` and
  `Card.Blocks`. Migration via `dotnet ef migrations add AddCardRelationships --project server`
  (never hand-written). Rejected: a `BlockedByCardIds` jsonb list on `Card` (no inverse, no
  per-row reason); a soft-removed row (history already lives in revisions, D-8).
- **D-4 Direction and ownership.** One stored row per `(blocked, blocker)`; "blocks" is the
  inverse read, never a second row. The blocked card owns the write: its token is required and
  rotated, its revision row is written, its `UpdatedAt` moves. The blocker is read, not written
  (no token rotation, no revision), so blocking a busy card never 409s its editor. Cross-board
  blockers are allowed; the other card is named by guid or identifier and resolved through
  `ResolveCardIdAsync` with the request's optional `relatedBoardId` as the explicit fence.
- **D-5 One open-blocker predicate** in a static `CardBlockers` helper
  (`server/Application/Services/CardBlockers.cs`): `Expression<Func<Card,bool>> HasOpenBlocker`
  = any `BlockedBy` row whose blocker has `ArchivedAt == null` and `Status` not `Done`/`Canceled`.
  Used by `OrchestratorService.LoadEligibleCandidatesAsync`,
  `AgentTaskPipelineStatusService` (Investigate backlog) and
  `ExpectationSnapshotReader.ReadBacklogAsync`. `NeedsDecision`, `Review`, `InProgress`,
  `Backlog` blockers are open. Rejected: a denormalised `OpenBlockerCount` column (one more
  thing to keep consistent on every close/reopen/archive).
- **D-6 Cycle and self rules at write time.** Self-reference is 422 `validation_failed`
  (`relatedCard`). Before insert, walk `BlockedBy` edges from the blocker outward (iterative
  breadth-first over `CardRelationships`, visited set, no depth cap); reaching the blocked card
  is 409 `card_relationship_cycle` with the path identifiers in the message. A duplicate pair is
  409 `card_relationship_exists`. The duplicate check, the walk and the insert run in one transaction under
  `pg_advisory_xact_lock(hashtext('antiphon.card-relationships.graph'))`, one key for the whole graph
  because edges cross boards (T-30, Review 3501caaa F1). Rejected: documenting cycles as a footgun; a DB trigger.
- **D-7 Resolution is computed, rows persist.** A blocker that is Done, Canceled or archived
  makes the row `resolved: true`; reopening or unarchiving the blocker re-blocks without a write.
  No sweep, no auto-delete. Rejected: deleting rows on close (loses the record; reopen would not
  re-block).
- **D-8 History.** `CardRevisionKind.RelationshipAdded = 6` and `RelationshipRemoved = 7`; new
  nullable `CardRevision.RelatedCardId` (Guid) and `RelationshipKind` (`CardRelationshipKind?`);
  `Reason`/`EditedBy` carry the request's reason and actor. Written on the blocked card only
  (D-4). `CardRevisionDto` and the client `CardRevisionDto`/`KIND_LABELS` gain the fields and
  labels ("Blocked by added", "Blocked by removed").
- **D-9 Archived cards.** Adding or removing a relationship on an archived blocked card, or
  naming an archived blocker, is 409 (same sentence shape as `:512-513`). Existing rows survive
  archive and unarchive of either side; an archived blocker counts as resolved (D-5).
- **D-10 Card files.** `CardFilePublicCard` and `RenderCard` are not changed; no relationship
  field reaches a published card file in this card. A later `blocked_by` front-matter list,
  filtered to same-board identifiers that are themselves eligible, is OD-4.
- **D-11 No attention item, no tracker push.** A blocked card is visible on the tile, the
  modal, `card.ps1 get` and the pipeline snapshot; it raises nothing in `/api/attention` and
  never pushes to GitHub/Jira/Linear.
- **D-12 Scope of the enum.** `CardRelationshipKind` has one member; `RelatesTo`/`Duplicates`
  are not added without a card that needs them. The API shape (`kind` in the body) does not
  change when they arrive.
- **D-13 Task creation is not refused.** `POST /api/agent-tasks` and `delegate.ps1` against a
  blocked card keep working; the orchestrator rule is a pull rule (row 4). A refusal with an
  override flag is OD-3.

## Design

### API (S2)

| Route | Body / query | Result |
|---|---|---|
| `GET /api/cards/{id}/relationships` (`?boardId=&cwd=`) | none | 200 `{ blockedBy: CardRelationshipDto[], blocks: CardRelationshipDto[] }` |
| `POST /api/cards/{id}/relationships` | `{ concurrencyToken, kind: "BlockedBy", relatedCard: "<guid or identifier>", relatedBoardId?, reason?, createdBy? }` | 201 `CardRelationshipDto`; 409 token / archived / `card_relationship_exists` / `card_relationship_cycle`; 422 self, unknown kind, over-length reason; 404 unknown related card |
| `POST /api/cards/{id}/relationships/{relationshipId}/remove` | `{ concurrencyToken, reason?, removedBy? }` | 200 `CardDto`; 404 relationship not on this card; 409 token / archived |

`CardRelationshipDto(Id, Kind, CardId, RelatedCardId, RelatedIdentifier, RelatedTitle,
RelatedBoardId, RelatedStatus, RelatedArchivedAt, Resolved, Reason, CreatedBy, CreatedAt)`.
`CardDto` gains `OpenBlockerCount` (int, `JsonIgnore WhenWritingDefault`) and `Blockers`
(`IReadOnlyList<CardBlockerSummaryDto(Identifier, Status, Resolved)>`, omitted when empty) so
the tile renders from the list route without a second query; `GetSummaryAsync` fills them from
one grouped query over the page's card ids. POST-not-DELETE follows the archive precedent
(`CardEndpoints.cs:184-186`). Every route resolves `{id}` through `ResolveAsync` like its
neighbours. Events: `CardChanged` for the blocked card on add/remove.

### card.ps1 (S4)

```
card.ps1 block    CARD-0051 -BlockedBy CARD-0049 [-Reason r | -ReasonFile p] [-By name] [-Token g] [-RelatedBoard <name|guid>]
card.ps1 unblock  CARD-0051 -BlockedBy CARD-0049 [-Reason r | -ReasonFile p] [-By name] [-Token g]
card.ps1 get      prints "blocked by  CARD-0049 (InProgress)" per row, "(resolved: Done)" when resolved, and "blocks      CARD-0052"
card.ps1 history  prints the new kinds with "-> CARD-0049"
```

`unblock` reads `GET …/relationships`, finds the `blockedBy` row whose `relatedIdentifier`
matches, and posts `remove`; no match is a non-zero exit naming the current blockers.
`-Json` passes the response through unchanged. Header verb list, `ValidateSet`, and
`server/Bundles/board-api.md` are updated in the same slice.

### Orchestrator rule (S3)

- `OrchestratorService.LoadEligibleCandidatesAsync`: add `.Where(CardBlockers.NotBlocked)`.
- `AgentTaskPipelineStatusService`: `BacklogRow` carries `OpenBlockerIdentifiers`; blocked cards
  leave `candidates` and fill a new `AgentTaskPipelineBacklogDto.Blocked`
  (`int`) and `BlockedItems` (`AgentTaskPipelineBlockedBacklogItemDto(CardId, BoardId,
  Identifier, Title, BlockedBy: string[])`, top 5 by rank), so the snapshot shows what is held
  and why.
- `ExpectationSnapshotReader.ReadBacklogAsync`: add the same predicate so `EligibleBacklog`
  does not count held cards.
- `docs/orchestration-loop.md` §1 step 4 (line ~482): "pull the next unstarted Backlog card
  with no open blocker; `investigateBacklog.blockedItems` lists the held ones";
  `docs/agent-card-lifecycle.md` Status Rules gains a "Blocked by another card" paragraph
  (D-5, D-7).

### Board UI (S5)

- `client/src/api/boards.ts`: `CardRelationshipDto`, `CardRelationshipsDto`, `openBlockerCount?`,
  `blockers?` on `CardDto`; `CardRevisionKind` union extended; hooks `useCardRelationships(cardId)`,
  `useAddBlocker(boardId, cardId)`, `useRemoveBlocker(boardId, cardId)` invalidating
  `boardKeys` and the relationships key.
- `CardAxisBadges.tsx`: `BlockedChip` (red outline badge "Blocked", tooltip listing blocker
  identifiers) when `card.openBlockerCount > 0`. Covers `BoardCard`, `CardRow`, `BacklogRow`.
- `CardModal.tsx` sidebar: `CardBlockers` panel: "Blocked by" list (identifier, status chip,
  struck-through when resolved, remove button posting the current token), an identifier input
  with Add, and a read-only "Blocks" list. Hidden add/remove on an archived card.
- `CardHistory.tsx`: labels and colours for the two kinds; the row shows the related identifier.

## Slices

| Slice | Minutes | Files |
|---|---|---|
| S1 Schema and history kinds | 40 | new `server/Domain/Entities/CardRelationship.cs`, new `server/Domain/Enums/CardRelationshipKind.cs`, `CardRevisionKind.cs`, `CardRevision.cs` (+2 columns), `Card.cs` (+2 navigations), `AppDbContext.cs`, CLI-generated `server/Migrations/*AddCardRelationships*` + snapshot, `BoardDtos.cs` (`CardRevisionDto` fields), `BoardService.ToRevisionDto`; new `tests/Antiphon.Tests/Application/CardRelationshipSchemaTests.cs` |
| S2 Service, routes, DTOs | 60 | new `server/Application/Services/CardBlockers.cs`, `CardService.cs` (`GetRelationshipsAsync`, `AddRelationshipAsync`, `RemoveRelationshipAsync`, `LoadCardAsync` includes), `CardRevisionLog.cs` (`AppendRelationship`), `CardEndpoints.cs` (3 routes), `BoardDtos.cs` (request/response records, `CardDto` fields), `BoardService.ToCardDto`, `docs/antiphon-api.md`; new `tests/Antiphon.Tests/Application/CardRelationshipApiTests.cs` |
| S3 Orchestrator rule and docs | 45 | `OrchestratorService.cs`, `AgentTaskPipelineStatusService.cs`, `AgentTaskPipelineDtos.cs`, `ExpectationSnapshotReader.cs`, `docs/orchestration-loop.md`, `docs/agent-card-lifecycle.md`; `OrchestratorServiceIntegrationTests.cs`, `AgentTaskPipelineStatusTests.cs`, `ExpectationSnapshotTests.cs` (one new method each) |
| S4 card.ps1 | 45 | `scripts/card.ps1`, `server/Bundles/board-api.md`, `docs/ops-http.md`; new `tests/Antiphon.Tests/Scripts/CardRelationshipScriptTests.cs` |
| S5 Client | 60 | `client/src/api/boards.ts`, `CardAxisBadges.tsx`, new `client/src/features/board/CardBlockers.tsx`, `CardModal.tsx`, `CardHistory.tsx`; tests `CardBlockers.test.tsx` (new), `BoardCard.test.tsx`, `CardHistory.test.tsx`, `CardModal.test.tsx` |

Order: S1 → S2 → {S3, S4, S5 in parallel, separate worktrees}. S3-S5 each depend on S2's routes
and DTOs; they do not touch each other's files.

## Test list (case → mutation → expected red)

| ID | Test (class.method) | Case | Mutation that must go red | Red statement |
|---|---|---|---|---|
| T-1 | `CardRelationshipSchemaTests.Duplicate_pair_is_refused_by_the_unique_index` | insert the same `(CardId, RelatedCardId, Kind)` twice | drop `IsUnique()` | second `SaveChangesAsync` does not throw |
| T-2 | `CardRelationshipSchemaTests.Self_reference_is_refused_by_the_check_constraint` | row with `CardId == RelatedCardId` | drop the check constraint | insert succeeds |
| T-3 | `CardRelationshipSchemaTests.Deleting_a_card_cascades_its_relationship_rows` | delete blocker card | `OnDelete(Restrict)` | `ExecuteDelete` throws FK violation |
| T-4 | `CardRelationshipApiTests.Add_blocker_by_identifier_returns_row_rotates_token_and_writes_revision` | POST with `relatedCard: "CARD-0002"` | skip `AppendRelationship` | revision count unchanged / kind missing |
| T-5 | `CardRelationshipApiTests.Blocker_token_and_revisions_are_untouched` | same POST, then read blocker | rotate blocker token | blocker `concurrencyToken` differs |
| T-6 | `CardRelationshipApiTests.Stale_token_is_409` | POST with old token | drop the token comparison | 201 instead of 409 |
| T-7 | `CardRelationshipApiTests.Self_reference_is_422` | `relatedCard` = own identifier | drop the guard | 500 (check constraint) instead of 422 |
| T-8 | `CardRelationshipApiTests.Duplicate_pair_is_409_card_relationship_exists` | POST twice | drop the pre-check | 500 (unique index) instead of 409 code |
| T-9 | `CardRelationshipApiTests.Three_hop_cycle_is_409_card_relationship_cycle` | A←B, B←C, then C←A | cap the walk at depth 1 | 201 for the closing edge |
| T-10 | `CardRelationshipApiTests.Archived_blocked_card_and_archived_blocker_are_409` | archive either side, POST | drop either `ArchivedAt` check | 201 |
| T-11 | `CardRelationshipApiTests.Resolved_follows_blocker_done_canceled_archived_and_reopen` | move blocker Done → `resolved: true`; reopen → false; archive → true | treat Canceled as open | `resolved` false after cancel |
| T-12 | `CardRelationshipApiTests.Remove_writes_RelationshipRemoved_and_clears_both_views` | POST remove, GET both cards | skip revision on remove | kind `RelationshipRemoved` absent |
| T-13 | `CardRelationshipApiTests.Card_dto_and_list_page_carry_open_blocker_count_and_blockers` | GET card, GET list | compute count including resolved rows | count 1 after blocker Done |
| T-14 | `CardRelationshipApiTests.Cross_board_blocker_resolves_with_relatedBoardId_fence` | same identifier on two boards | ignore `relatedBoardId` | 409 ambiguous instead of 201 |
| T-15 | `OrchestratorServiceIntegrationTests.PollTick_skips_a_card_with_an_open_blocker_and_dispatches_after_the_blocker_closes` | blocked card in active column | drop the predicate | dispatched while blocked |
| T-16 | `AgentTaskPipelineStatusTests.Investigate_backlog_omits_blocked_cards_and_lists_them_as_blocked` | two Backlog cards, one blocked | omit the blocked list | `blocked == 0` / item missing |
| T-17 | `ExpectationSnapshotTests.Backlog_candidates_exclude_cards_with_an_open_blocker` | one blocked Backlog card | drop the predicate | `EligibleBacklog == 1` |
| T-18 | `CardRelationshipScriptTests.Block_fetches_the_token_and_posts_the_relationship_body` | stub GET card + POST | send stale `-Token` default | POST body token differs |
| T-19 | `CardRelationshipScriptTests.Unblock_resolves_the_row_by_identifier_and_posts_remove` | stub relationships + remove | match on `relatedTitle` | wrong relationship id posted |
| T-20 | `CardRelationshipScriptTests.Get_prints_blocked_by_and_blocks_lines` | stub card with both lists | drop the lines | output lacks `blocked by` |
| T-21 | `CardRelationshipScriptTests.Unblock_without_a_match_exits_non_zero_naming_current_blockers` | no matching row | exit 0 | exit code 0 |
| T-22 | `CardBlockers.test.tsx` "adds a blocker with the current token and shows it with its status" | type identifier, Add | omit token | request body lacks token |
| T-23 | `CardBlockers.test.tsx` "a resolved blocker is struck through and removable" | `resolved: true` row | render resolved as open | no strike style |
| T-24 | `CardBlockers.test.tsx` "hides add and remove on an archived card" | `archivedAt` set | always show | button present |
| T-25 | `BoardCard.test.tsx` "shows a Blocked chip naming the blockers when the count is positive" | `openBlockerCount: 1` | render on `blockers.length` | chip shown with resolved-only list |
| T-26 | `BoardCard.test.tsx` "shows no Blocked chip at zero" | count 0 | always render | chip present |
| T-27 | `CardHistory.test.tsx` "labels relationship revisions and names the related card" | both kinds | missing label | fallback text |
| T-28 | `CardModal.test.tsx` "renders the Blocked-by panel from the relationships query" | opened card | panel not mounted | testid missing |

## Verification design

### V/R matrix

| ID | Statement | Slice | Proving tests | Rows |
|---|---|---|---|---|
| V-1 | The schema refuses duplicates and self-reference and cascades on card delete. | S1 | T-1, T-2, T-3 | CP-1 |
| V-2 | Adding a blocker writes one row, one revision on the blocked card, rotates only that card's token. | S2 | T-4, T-5, T-6 | CP-3 |
| V-3 | Self, duplicate, cycle and archived writes are refused with the named codes. | S2 | T-7, T-8, T-9, T-10 | CP-3 |
| V-4 | `resolved` tracks the blocker's Done/Canceled/archived state without a write; remove is recorded. | S2 | T-11, T-12 | CP-3 |
| V-5 | Card and list DTOs carry the open-blocker summary; cross-board resolution honours the fence. | S2 | T-13, T-14 | CP-3 |
| V-6 | The tick, the pipeline backlog and the watchdog backlog all exclude blocked cards; the pipeline lists them. | S3 | T-15, T-16, T-17 | CP-5, CP-6, CP-7 |
| V-7 | card.ps1 `block`/`unblock`/`get` send and print the contract. | S4 | T-18..T-21 | CP-8 |
| V-8 | The modal panel, chip and history render and post the contract. | S5 | T-22..T-28 | CP-10 |
| R-1 | Existing content-edit, history and identifier-resolution behaviour is unchanged by the new revision columns and includes. | S1, S2 | `CardCorrectionApiTests` (12), `CardIdentifierResolutionTests` (18) | CP-2, CP-4 |
| R-2 | Card-file rendering is unchanged (no relationship leaks). | S2 | `CardTaskFileRendererTests` (11) | CP-4 |
| R-3 | Existing orchestrator, pipeline and snapshot tests pass. | S3 | `OrchestratorServiceIntegrationTests` (27), `AgentTaskPipelineStatusTests` (30), `ExpectationSnapshotTests` (6) | CP-5, CP-6, CP-7 |
| R-4 | Existing script tests pass with the shared helper edits. | S4 | `CardListSearchScriptTests` (11) | CP-9 |
| R-5 | Existing board client tests and the lint gate pass. | S5 | `CardRow.test.tsx` (26), `BacklogSection.test.tsx` (10), `CardModal.test.tsx` (18), full suite | CP-10, CP-11 |

### Positive controls (pending; SourceLanding Mutation, method-scoped)

| PC | Exact detecting filter | Mutation (scratch copy) | Expected red |
|---|---|---|---|
| PC-1 | `/*/*/CardRelationshipApiTests/Three_hop_cycle_is_409_card_relationship_cycle` | depth cap 1 in the cycle walk | 201 on the closing edge |
| PC-2 | `/*/*/CardRelationshipApiTests/Blocker_token_and_revisions_are_untouched` | rotate the blocker's token in `AddRelationshipAsync` | blocker token changed |
| PC-3 | `/*/*/CardRelationshipApiTests/Resolved_follows_blocker_done_canceled_archived_and_reopen` | drop `Canceled` from the resolved set | `resolved` false after cancel |
| PC-4 | `/*/*/CardRelationshipApiTests/Archived_blocked_card_and_archived_blocker_are_409` | drop the blocker `ArchivedAt` check | 201 |
| PC-5 | `/*/*/OrchestratorServiceIntegrationTests/PollTick_skips_a_card_with_an_open_blocker_and_dispatches_after_the_blocker_closes` | remove `.Where(CardBlockers.NotBlocked)` | session started while blocked |
| PC-6 | `/*/*/AgentTaskPipelineStatusTests/Investigate_backlog_omits_blocked_cards_and_lists_them_as_blocked` | leave blocked cards in `candidates` | `total` 2 |
| PC-7 | `/*/*/ExpectationSnapshotTests/Backlog_candidates_exclude_cards_with_an_open_blocker` | drop the predicate in `ReadBacklogAsync` | `EligibleBacklog` 1 |
| PC-8 | `/*/*/CardRelationshipScriptTests/Unblock_resolves_the_row_by_identifier_and_posts_remove` | match on `relatedTitle` | wrong relationship id |
| PC-9 | `/*/*/CardRelationshipApiTests/Card_dto_and_list_page_carry_open_blocker_count_and_blockers` | count resolved rows | count 1 after Done |
| PC-10 | `/*/*/CardTaskFileRendererTests/*` is a regression row; its PC is the renderer emitting `blocked_by:` | add the line to `RenderCard` | golden text differs |

### Checkpoints

Rows run on the default placement (server2 per `GET /api/runner-defaults`; omit `-Runner` and
`-Platform`). Integration rows need the shared test Postgres and are serial. No row runs a
namespace or the full `Antiphon.Tests` assembly.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c0100-s1/` | relationship-schema | `/*/*/CardRelationshipSchemaTests/*` | V-1 | 3 passed, 0 failed | 3 | 4 | true |
| CP-2 | S1 | CP-1 | correction-regression | `/*/*/CardCorrectionApiTests/*` | R-1 | 12 passed, 0 failed | 12 | 4 | true |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c0100-s2/` | relationship-api | `/*/*/CardRelationshipApiTests/*` | V-2..V-5 | 11 passed, 0 failed | 11 | 8 | true |
| CP-4 | S2 | CP-3 | identifier-and-renderer-regression | `/*/*/CardIdentifierResolutionTests/*` then `/*/*/CardTaskFileRendererTests/*` | R-1, R-2 | 18 + 11 passed, 0 failed | 29 | 5 | true |
| CP-5 | S3 | `tests/Antiphon.Tests -> bin-c0100-s3/` | orchestrator-eligibility | `/*/*/OrchestratorServiceIntegrationTests/*` | V-6, R-3 | 28 passed, 0 failed | 28 | 10 | true |
| CP-6 | S3 | CP-5 | pipeline-backlog | `/*/*/AgentTaskPipelineStatusTests/*` | V-6, R-3 | 31 passed, 0 failed | 31 | 5 | true |
| CP-7 | S3 | CP-5 | snapshot-backlog | `/*/*/ExpectationSnapshotTests/*` | V-6, R-3 | 7 passed, 0 failed | 7 | 3 | true |
| CP-8 | S4 | `tests/Antiphon.Tests -> bin-c0100-s4/` | card-script-relationships | `/*/*/CardRelationshipScriptTests/*` | V-7 | 4 passed, 0 failed | 4 | 4 | false |
| CP-9 | S4 | CP-8 | card-script-regression | `/*/*/CardListSearchScriptTests/*` | R-4 | 11 passed, 0 failed | 11 | 3 | false |
| CP-10 | S5 | n/a | client-card-blockers | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/board/CardBlockers.test.tsx src/features/board/BoardCard.test.tsx src/features/board/CardHistory.test.tsx src/features/board/CardModal.test.tsx src/features/board/CardRow.test.tsx src/features/orchestrator/BacklogSection.test.tsx` | V-8, R-5 | 6 files; aggregate = 65 existing + 7 new (TestDesign freezes it), 0 failed/skipped | n/a | 5 | false |
| CP-11 | S5 | n/a | client-full | `pwsh -NoProfile -File scripts/test-client.ps1` | lint gate, whole client suite | `CLIENT TESTS EXIT CODE: 0` | n/a | 10 | true |

Build and run through the checkpoint tool (`run --plan <this file> --after S<n> --expected-source-sha
<full committed SHA> --serial --max-wait 55s`, then `wait` until the exit is not 75), one run per
committed slice. The tool bootstrap is one unlisted, build-slot-gated isolated build
(`bin-c0100-tool/`, `UseAppHost=false` on Linux); every `bin-c0100-*` directory is removed before
the task ends. Green client runs print aggregates only; report the aggregate plus a source census.

### Cost

Rows sum to 61 estimated minutes (S1: 8, S2: 13, S3: 18, S4: 7, S5: 15). Authoring per slice is
the Slices table (250 minutes total); five Code tasks, S3-S5 concurrent after S2 lands.

## Ordering and collisions

| Slice | Collides with | Handling |
|---|---|---|
| S1 | CARD-1032 S8 repair (Code, Dispatched, task `567fbd60`): its S1 added `server/Migrations/20261012075920_AddTaskWorktreeMirrorBinding` and edited `AppDbContext.cs` (landed). If the S8 branch adds a migration, `AppDbContextModelSnapshot.cs` conflicts. | Code dispatcher checks `git diff --stat master...origin/<1032 branch> -- server/Migrations server/Infrastructure/Data` before dispatch; on a hit, land 1032 first, then generate the S1 migration on the rebased tip. |
| S2 | None in flight: `CardService.cs` (last `604d2dfb5`, 2026-09-30), `CardEndpoints.cs` (`eb7d748f2`), `BoardDtos.cs`, `BoardService.cs` (`306e32f1e`) are not in the CARD-0097 (RolePolicy*, client settings), CARD-0507 (InstructionBundles, RolePolicyEntry) or CARD-1213 (delegate.ps1, runner branch consumers) file lists. | None. |
| S3 | CARD-1213 S2 (Code, Dispatched) edits `docs/orchestration-loop.md:225,989,1000-1012,1184`; S3 edits line ~482 and `docs/agent-card-lifecycle.md`. CARD-0506 S4 docs (Code, Dispatched) is unscoped; its plan is `2026-10-13-card-0506-concurrency-settings-ui.md`. | Different hunks; accept the rebase. Dispatcher greps the 0506 plan for `orchestration-loop.md` / `agent-card-lifecycle.md` and defers S3 if either is listed. |
| S4 | CARD-0507 is tool bundles (`InstructionBundles`, `RolePolicyEntry`); S4 edits `server/Bundles/board-api.md` text only. | Dispatcher confirms the 0507 branch does not touch `server/Bundles/board-api.md` or `scripts/card.ps1`; defers otherwise. |
| S5 | CARD-0097/CARD-0506 client work is under `client/src/features/settings`; S5 is `features/board` and `api/boards.ts` (last `6b14d006c`, 2026-09-26). | None. |

Scope strings: S1 `server/Domain/**,server/Infrastructure/Data/AppDbContext.cs,server/Migrations/**,server/Application/Dtos/BoardDtos.cs,server/Application/Services/BoardService.cs,tests/Antiphon.Tests/Application/CardRelationshipSchemaTests.cs`; S2 `server/Application/Services/CardService.cs,server/Application/Services/CardBlockers.cs,server/Application/Services/CardRevisionLog.cs,server/Api/Endpoints/CardEndpoints.cs,server/Application/Dtos/BoardDtos.cs,server/Application/Services/BoardService.cs,docs/antiphon-api.md,tests/Antiphon.Tests/Application/CardRelationshipApiTests.cs`; S3 `server/Application/Services/OrchestratorService.cs,server/Application/Services/AgentTaskPipelineStatusService.cs,server/Application/Dtos/AgentTaskPipelineDtos.cs,server/Application/Services/ExpectationSnapshotReader.cs,docs/orchestration-loop.md,docs/agent-card-lifecycle.md,tests/Antiphon.Tests/Application/OrchestratorServiceIntegrationTests.cs,tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs,tests/Antiphon.Tests/Application/ExpectationSnapshotTests.cs`; S4 `scripts/card.ps1,server/Bundles/board-api.md,docs/ops-http.md,tests/Antiphon.Tests/Scripts/CardRelationshipScriptTests.cs`; S5 `client/src/api/boards.ts,client/src/features/board/**`.

## Operator decisions (OD-n, recommendation first)

- **OD-1 Join table (recommended, D-1) versus a `blocked-by:CARD-nnnn` label convention.** The
  label version is roughly S3 + S4 + a chip (about 90 minutes) and gives no inverse, no history
  row, no cycle check, and publishes to card files.
- **OD-2 Five Code tasks (recommended) versus one.** S1+S2 may be one task (the schema is inert
  without the service); S3-S5 stay separate so three worktrees run in parallel.
- **OD-3 Refuse task creation against a blocked card** (`409 card_blocked`, override
  `ignoreBlockers`). Recommended: not in this card (D-13); file a follow-up once the pull rule
  has run for a week.
- **OD-4 Publish `blocked_by` identifiers in card files** for eligible same-board pairs.
  Recommended: not in this card (D-10); a follow-up against `CardFilePublicCard` and
  `docs/card-file-privacy.md`.
- **OD-5 Reason required on `block`?** Recommended: optional (like a move reason), so the
  common case `card.ps1 block CARD-0051 -BlockedBy CARD-0049` needs no file.

--- next stage ---
next: test-design
handoff: Audit D-3..D-9 against CardService/CardRevisionLog/AppDbContext, fix exact method names, request JSON and 409 codes for the 28 tests, freeze CP-10's aggregate count, confirm the schema tests can use the shared AntiphonWebAppFactory reset, then release S1+S2 as one Code task on the default placement with S3-S5 queued behind it (OD-2).
artifact: docs/superpowers/plans/2026-10-14-card-0100-card-relationships-plan.md
