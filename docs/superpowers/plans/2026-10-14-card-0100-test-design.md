# CARD-0100 test design: "blocked by" as a first-class card relationship

Date: 2026-10-14. Stage: TestDesign; next stage: Code (S1+S2 as one task). TestDesign task
`0166fae1`. Plan: [2026-10-14-card-0100-card-relationships-plan.md](2026-10-14-card-0100-card-relationships-plan.md)
at `e9e30c763a2e047dc654050705a36a4a37738b5b`. Inspected checkout: the same commit
(`origin/master` = `e0877982c`). TestDesign ran no build and no test; counts below are a source
census (`[Test]` methods plus `[Arguments]` rows; vitest `it`/`it.each` rows) taken on 2026-10-14.
Session: `ANTIPHON_TASK_TOKEN` was present, so the task ran bound; the token was not printed.

Orchestrator decisions in force: OD-1 join table; OD-2 S1+S2 are ONE Code task, S3, S4 and S5
queue behind it and are independent of each other; OD-3 task creation is not refused on a blocked
card; OD-4 nothing about relationships reaches a card file; OD-5 reason optional on block.

## Audit of plan decisions against the code (D-1..D-13)

Each row names the plan decision, what the code actually does, and the frozen consequence. A-n
rows amend the plan; Code follows this document where the two differ.

| Decision | Code read | Verdict and amendment |
|---|---|---|
| D-1, D-2 | No relationship entity under `server/`; `ExternalTrackerSyncService` untouched. | Confirmed. |
| D-3 entity | `CardComment.cs` + `AppDbContext.cs:1208-1345` are the child-row precedent: `ToTable`, `HasKey`, `HasIndex(...).HasDatabaseName`, `HasOne(c => c.Card).WithMany(card => card.Comments).HasForeignKey(c => c.CardId).OnDelete(Cascade)`; a check constraint is declared through `ToTable(name, t => t.HasCheckConstraint(...))` (`:217`). | **A-1.** `CardRelationship` carries TWO navigations, `Card` (the blocked card, FK `CardId`) and `RelatedCard` (the blocker, FK `RelatedCardId`), each configured explicitly (`WithMany(c => c.BlockedBy)` / `WithMany(c => c.Blocks)`), both `OnDelete(Cascade)`; Postgres accepts two cascading FKs to one parent. `Reason` is `text` (as `CardRevision.Reason`), `CreatedBy` `HasMaxLength(200)` (= `CardService.MaxActorLength`). Unique index `IX_CardRelationships_CardId_RelatedCardId_Kind`, index `IX_CardRelationships_RelatedCardId`, check `CK_CardRelationships_NotSelf` (`"CardId" <> "RelatedCardId"`). Migration CLI-generated; the test template picks it up automatically (see Fixtures). |
| D-4 direction, token, fence | `MoveAsync`/`UpdateContentAsync` (`CardService.cs:505-513`, `:596-600`): empty token is 422 `validation_failed`, stale token is 409 `conflict` "was modified by another operation", archived is 409 `conflict` "is archived; …". `ResolveCardIdAsync(string, CardScopeContext, ct)` (`:350`) with `CardScopeContext(ExplicitBoardId, InheritedCardId, CallerSessionId, Directory)`; `CardIdentifierScope.ResolveAsync` treats `ExplicitBoardId` as a fence and `InheritedCardId` as scope A (that card's board wins, `CardIdentifierScope.cs:130-136`); ambiguity is 409 `card_identifier_ambiguous`. | **A-2.** `relatedCard` resolves through `ResolveCardIdAsync(request.RelatedCard, new CardScopeContext(request.RelatedBoardId, InheritedCardId: card.Id, null, null), ct)`: with no `relatedBoardId` the blocked card's own board wins when the identifier exists there; an explicit `relatedBoardId` is a fence. Token rules copy `:505-513` verbatim (empty 422, stale 409 `conflict`). |
| D-5 predicate | Three read sites confirmed: `OrchestratorService.LoadEligibleCandidatesAsync` (`:819-867`), `AgentTaskPipelineStatusService` backlog query (`:144-150`) and candidate filter (`:166-175`), `ExpectationSnapshotReader.ReadBacklogAsync` (`:340-375`). | Confirmed. **A-3 (fail-closed).** `CardBlockers.HasOpenBlocker` is a whitelist of RESOLVED states: a blocker counts as resolved only when `ArchivedAt != null` or `Status` is `Done` or `Canceled`; any other status, including a value this code does not know, keeps the row open and the card held. |
| D-6 codes | `ConflictException(string message, string code)` exists (`ConflictException.cs:11`); `ValidationException(string field, string error)` yields 422 with `code: validation_failed` and `errors.<field>`; problem documents carry `type`, `title`, `status`, `detail`, `code`, `errors` (`ExceptionMiddleware.cs:57-77`). Precedent constant: `CardService.CardOrderStaleCode` (`:654`). | **A-4.** Constants `CardService.CardRelationshipCycleCode = "card_relationship_cycle"` and `CardRelationshipExistsCode = "card_relationship_exists"`. Self-reference, unknown `kind`, over-length `reason`/`createdBy` are `ValidationException` on fields `relatedCard`, `kind`, `reason`, `createdBy`. **`kind` binds as a string** and is checked against the whitelist `{"BlockedBy"}` so an unknown kind is 422 `validation_failed`, not a 400 enum bind failure. Unknown related card is 404 (from `ResolveCardIdAsync`). |
| D-7 resolution computed | Nothing clears rows today; status lives on `Card.Status`/`ArchivedAt`. | Confirmed; T-11 is the proof. |
| D-8 history | `CardRevisionKind.cs` ends at `Reorder = 5`; `CardRevisionLog.Append` allocates from `Card.RevisionCount`; `BoardService.ToRevisionDto` (`:491`) maps every column; `CardRevisionDto` is a positional record whose trailing optionals serialize as `null`; client `KIND_LABEL`/`KIND_COLOR` are `Record<CardRevisionDto['kind'], string>` (`CardHistory.tsx:63-78`), so a new union member without a label is a `tsc` error, not a runtime fallback. | **A-5.** `CardRevision` gains THREE nullable columns: `RelatedCardId` (Guid), `RelatedCardIdentifier` (string, 100), `RelationshipKind` (`CardRelationshipKind`). The identifier is snapshotted so `card.ps1 history` and the client row print `-> CARD-0049` without a join and after the other card is gone (the superseded-values stance of `CardRevision`). `CardRevisionDto` gains the same three as `[property: JsonIgnore(Condition = WhenWritingNull)]` so existing rows stay byte-identical on the wire. `RelationshipAdded = 6`, `RelationshipRemoved = 7`; `CardRevisionLog.AppendRelationship(card, kind, relatedCardId, relatedIdentifier, reason, actor, utcNow)`. |
| D-9 archived | `:512-513`, `:670-671`, `:1330-1331` are the sentence shape. | Confirmed; both sides checked independently (G-4, G-14). |
| D-10 card files | `RenderCard` (`CardTaskFileRenderer.cs:94-134`) writes a fixed key list from `CardFilePublicCard` (`CardFilePublicCard.cs:8-33`). `CardTaskFileRendererTests` (11) assert with `ShouldContain`/`ShouldNotContain` per line, so an ADDED `blocked_by:` line turns none of them red. | **A-6.** R-2 needs a decisive assertion: T-29 pins the exact front-matter key set of a fully populated card. The plan's PC-10 is re-pointed at T-29. |
| D-11, D-12, D-13 | `AttentionKind` has no card-relationship member; `CardRelationshipKind` single member; `POST /api/agent-tasks` is not touched. | Confirmed; D-13 has no production line to guard (Out of scope). |

Further corrections found in the census:

- **A-7.** `AgentTaskPipelineStatusTests` is a `partial class` across four files
  (`AgentTaskPipelineStatusTests.cs` 30 methods / 39 executions, `AgentTaskPipelineStatusC557Tests.cs`
  19 / 19, `AgentTaskPipelineStatusC1076Tests.cs` 4 / 9, `HostBudgetPipelineTests.cs` 4 / 4): the class
  filter executes **71** today, not 30. T-16 lives in a new partial file
  `AgentTaskPipelineStatusC100Tests.cs` (the C557/C1076 convention) and CP-6 expects 72.
- **A-8.** `CardHistory.test.tsx` has 9 `it` plus one 3-row `it.each` = 12 executions, so the six
  CP-10 files hold 2 + 12 + 18 + 26 + 10 = **68** today, not 65; with the 7 new client tests CP-10
  is frozen at **75**.
- **A-9.** `scripts/test-client.ps1` has no lint flag. The lint gate is `client/src/lint.gate.test.ts`
  (one vitest test running ESLint over the client, 180 s budget), which the full-suite row CP-11
  executes. `npm run lint` is not a separate row.
- **A-10.** OD-2 makes S1+S2 one Code task, so CP-1..CP-4 share one isolated build (`bin-c0100-s12/`)
  and run after `S1-S2`; the plan's second build is removed.
- **A-11.** `POST …/relationships` 201 body is the `CardRelationshipDto` plus `cardConcurrencyToken`
  (the blocked card's rotated token), so `card.ps1 block` and the client can continue without a
  second GET; `…/remove` returns the `CardDto` as planned.
- **A-12.** `CardDto.Blockers` is `null` (not an empty list) when the card has no rows and is
  `[JsonIgnore(WhenWritingNull)]`; `OpenBlockerCount` is `WhenWritingDefault`. A card with no
  relationship rows serializes exactly as today (R-1 `Full_board_views_remain_wire_identical…`).

## Frozen API contract (S2)

Routes resolve `{id}` through `CardEndpoints.ResolveAsync` with `?boardId=&cwd=` like neighbours.

```http
GET  /api/cards/{id}/relationships
200  { "blockedBy": [CardRelationshipDto], "blocks": [CardRelationshipDto] }

POST /api/cards/{id}/relationships
     { "concurrencyToken": "<guid>", "relatedCard": "CARD-0002", "kind": "BlockedBy",
       "relatedBoardId": null, "reason": null, "createdBy": null }
201  Location: /api/cards/{cardId}/relationships
     { "id": "<guid>", "kind": "BlockedBy", "cardId": "<blocked guid>", "relatedCardId": "<blocker guid>",
       "relatedIdentifier": "CARD-0002", "relatedTitle": "…", "relatedBoardId": "<guid>",
       "relatedStatus": "Backlog", "relatedArchivedAt": null, "resolved": false,
       "reason": null, "createdBy": null, "createdAt": "…", "cardConcurrencyToken": "<new guid>" }
404  relatedCard names no card                      (NotFoundException, code null)
409  code conflict                 stale token / archived blocked card / archived blocker
409  code card_identifier_ambiguous                 (existing resolver, with candidates)
409  code card_relationship_exists                  duplicate (blocked, blocker, kind)
409  code card_relationship_cycle                   detail lists the path identifiers, e.g. "CARD-0001 <- CARD-0002 <- CARD-0003 <- CARD-0001"
422  code validation_failed, errors.concurrencyToken | errors.relatedCard (self) | errors.kind | errors.reason | errors.createdBy

POST /api/cards/{id}/relationships/{relationshipId}/remove
     { "concurrencyToken": "<guid>", "reason": null, "removedBy": null }
200  CardDto (blocked card; token rotated; openBlockerCount/blockers recomputed)
404  relationshipId is not a BlockedBy row OF THIS card
409  code conflict                 stale token / archived blocked card
422  code validation_failed, errors.concurrencyToken (empty)
```

`Related*` fields describe the OTHER card of the row relative to the requested card (the blocker
in `blockedBy`, the blocked card in `blocks`). `resolved` is always computed on the blocker end:
`relatedArchivedAt != null` or blocker status `Done`/`Canceled`. `CardDto` gains
`openBlockerCount` (int, omitted at 0) and `blockers` (`[{ identifier, status, resolved }]`,
omitted when null). `CardRevisionDto` gains `relatedCardId`, `relatedCardIdentifier`,
`relationshipKind` (omitted when null). Writes publish `CardChanged` for the blocked card only.

## Test list (29; case → mutation → red)

Names are frozen. TUnit classes use the repo's `Snake_case_sentence` style; vitest names are the
`it(...)` strings. "Red" names the first assertion that fails under the mutation.

| ID | Test | Case and oracle | Mutation | Red statement |
|---|---|---|---|---|
| T-1 | `CardRelationshipSchemaTests.C100_Duplicate_pair_is_refused_by_the_unique_index` | isolated clone; `GetPendingMigrationsAsync()` empty and `HasPendingModelChanges()` false; the last migration ends with `_AddCardRelationships` and the one before with `_AddCardCleanupScheduling`; insert `(A, B, BlockedBy)` twice; second `SaveChangesAsync` throws `DbUpdateException` whose inner `PostgresException` has `SqlState == UniqueViolation` and `ConstraintName == "IX_CardRelationships_CardId_RelatedCardId_Kind"` | drop `IsUnique()` | `duplicateAccepted.ShouldBeFalse()` |
| T-2 | `CardRelationshipSchemaTests.C100_Self_reference_is_refused_by_the_check_constraint` | insert `(A, A)`; `SaveChangesAsync` throws with `SqlState == CheckViolation (23514)` and `ConstraintName == "CK_CardRelationships_NotSelf"` | remove the check constraint | `selfAccepted.ShouldBeFalse()` |
| T-3 | `CardRelationshipSchemaTests.C100_Deleting_either_card_cascades_its_relationship_rows` | rows `(A, B)` and `(C, B)`; `ExecuteDelete` card B → 0 rows remain; re-seed `(A, B2)`; `ExecuteDelete` card A → 0 rows | `OnDelete(Restrict)` on the `RelatedCard` FK | `ExecuteDeleteAsync` of B throws `PostgresException 23503` before the first count |
| T-4 | `CardRelationshipApiTests.Add_blocker_by_identifier_returns_201_rotates_the_token_and_writes_RelationshipAdded` | seed board with CARD-0001, CARD-0002; POST on CARD-0001 `relatedCard: "CARD-0002"`, reason "needs the schema first", createdBy "operator"; 201 body as frozen; GET card → `concurrencyToken != old`, `revisionCount == 1`, `updatedAt > before`; GET revisions → one row `kind == RelationshipAdded`, `relatedCardId == CARD-0002.id`, `relatedCardIdentifier == "CARD-0002"`, `relationshipKind == BlockedBy`, `reason`, `editedBy == "operator"` | skip `AppendRelationship` | `revisions.Count.ShouldBe(1)` |
| T-5 | `CardRelationshipApiTests.Adding_a_blocker_leaves_the_blockers_token_and_revisions_untouched` | same POST; GET CARD-0002 → `concurrencyToken` equals the pre-POST value, `revisionCount == 0`, `updatedAt` unchanged | rotate the blocker's token in `AddRelationshipAsync` | `blockerAfter.ConcurrencyToken.ShouldBe(blockerBefore.ConcurrencyToken)` |
| T-6 | `CardRelationshipApiTests.An_empty_or_stale_token_is_refused` `[Arguments("empty")]`, `[Arguments("stale")]` | empty `Guid.Empty` → 422 `errors.concurrencyToken`; stale (token from before an unrelated content edit) → 409 `code == "conflict"`; neither writes a row (GET relationships empty) | drop the `request.ConcurrencyToken != card.ConcurrencyToken` comparison | stale row: `response.StatusCode.ShouldBe(Conflict)` |
| T-7 | `CardRelationshipApiTests.Unknown_kind_and_over_length_text_are_422` `[Arguments("kind")]`, `("reason")`, `("createdBy")` | `kind: "Blocks"` → 422 `errors.kind`; reason of `MaxReasonLength + 1` chars → `errors.reason`; createdBy of `MaxActorLength + 1` → `errors.createdBy`; no row written | drop the kind whitelist check | kind row: `StatusCode.ShouldBe(UnprocessableEntity)` (a 500 or 201 arrives) |
| T-8 | `CardRelationshipApiTests.Duplicate_pair_is_409_card_relationship_exists` | POST twice with refreshed token; second → 409 `code == CardService.CardRelationshipExistsCode`; GET relationships → exactly one row | drop the pre-insert existence check | `problem.GetProperty("code")…ShouldBe("card_relationship_exists")` (the unique index surfaces as 409 `conflict` or 500) |
| T-9 | `CardRelationshipApiTests.Relationship_write_refusal_table` `[Arguments("self")]`, `("two_cycle")`, `("three_cycle")`, `("diamond")`, `("archived_blocker")`, `("unknown_card")` | self: POST A←A → 422 `errors.relatedCard`; two_cycle: A←B then B←A → 409 `card_relationship_cycle`; three_cycle: A←B, B←C then C←A → 409 `card_relationship_cycle`, detail contains "CARD-0001", "CARD-0002", "CARD-0003"; diamond: X←P, X←Q, P←R, Q←R (R reachable twice) then W←X → 201 (visited set, no false cycle); archived_blocker: blocker archived → 409 `code == "conflict"`, detail contains "archived"; unknown_card: `relatedCard: "CARD-9999"` → 404. Every refusal leaves GET relationships of the blocked card unchanged | cap the walk at depth 1 | three_cycle row: `StatusCode.ShouldBe(Conflict)` (201 arrives); two_cycle stays green, which is what proves the cap, not the walk, was removed |
| T-10 | `CardRelationshipApiTests.An_archived_blocked_card_refuses_add_and_remove` | archive CARD-0001 (with an existing row to CARD-0002); POST add → 409 `conflict` detail contains "archived"; POST remove of the existing row → 409 `conflict`; row still present after unarchive | drop the `card.ArchivedAt` check in `AddRelationshipAsync` | `add.StatusCode.ShouldBe(Conflict)` |
| T-11 | `CardRelationshipApiTests.Resolved_follows_the_blockers_Done_Canceled_archived_state_and_reopen` | row A←B; sequence over HTTP on B: move to Done → GET A relationships `resolved true`; reopen → false; move to Canceled → true; reopen → false; archive → true; unarchive → false; throughout `blockedBy.Count == 1` and A's `revisionCount` unchanged | drop `Canceled` from the resolved set | `afterCancel.Resolved.ShouldBeTrue()` |
| T-12 | `CardRelationshipApiTests.Remove_writes_RelationshipRemoved_rotates_the_token_and_clears_both_views` | row A←B; GET B relationships `blocks` has one row whose `relatedIdentifier == "CARD-0001"`; POST remove with A's token, reason "unblocked", removedBy "operator" → 200 `CardDto` with new token and no `blockers` key; GET A and GET B relationships both empty; A revisions newest `kind == RelationshipRemoved`, `relatedCardIdentifier == "CARD-0002"`; B `revisionCount` unchanged | skip the revision on remove | `revisions[0].Kind.ShouldBe(RelationshipRemoved)` |
| T-13 | `CardRelationshipApiTests.Card_and_list_views_carry_open_blocker_count_and_blockers_excluding_resolved` | A←B, A←C; GET A → `openBlockerCount == 2`, `blockers` two entries `{identifier, status, resolved:false}`; `GET /api/cards?boardId=` page row for A identical; move C to Done → `openBlockerCount == 1`, `blockers` still two with C `resolved true`; raw JSON of an unrelated card D has neither `openBlockerCount` nor `blockers` key | count resolved rows in `OpenBlockerCount` | `afterDone.OpenBlockerCount.ShouldBe(1)` |
| T-14 | `CardRelationshipApiTests.Cross_board_blocker_resolves_on_the_blocked_cards_board_by_default_and_honours_relatedBoardId` | two boards in one project, both holding a card named CARD-0002 (second board seeded with identifiers CARD-0001..0002 too); POST on board-1 CARD-0001 `relatedCard: "CARD-0002"`, no fence → 201 `relatedCardId == board1.CARD-0002.id`; POST on board-1 CARD-0003 with `relatedBoardId: board2.id` → 201 `relatedCardId == board2.CARD-0002.id`, `relatedBoardId == board2.id` | ignore `request.RelatedBoardId` | `fenced.RelatedCardId.ShouldBe(board2Card.Id)` |
| T-15 | `OrchestratorServiceIntegrationTests.PollTick_skips_a_card_with_an_open_blocker_and_dispatches_after_the_blocker_closes` | `CreateGraph(tempRoot)` card in the active column; `AddCard(graph, "E07B", graph.ReviewColumn)` as blocker with a `CardRelationship` row; `PollTickAsync` + `WaitForIdleAsync` → `AgentSessions.Count(card) == 0`, `OwnerSessionId null`, `adapter.Started false`; then set blocker `Status = Done`, `BoardColumnId = graph.DoneColumn.Id`, save; second tick → `AgentSessions.Count(card) == 1`, `adapter.Started true` | remove `.Where(CardBlockers.NotBlocked)` from `LoadEligibleCandidatesAsync` | first tick: `(await verify.AgentSessions.CountAsync(s => s.CardId == graph.Card.Id)).ShouldBe(0)` |
| T-16 | `AgentTaskPipelineStatusTests.C100_investigate_backlog_omits_blocked_cards_and_lists_them_as_blocked` (new partial file `AgentTaskPipelineStatusC100Tests.cs`) | `C557Seed`: board 1 with cards 1, 2, 3 Backlog; relationship rows 2←1 (open), 3←4 where card 4 is `CardStatus.Done`; `C557Read(db).InvestigateBacklog` → `Total == 2` (cards 1 and 3), `Items` ids `[1, 3]`, `Blocked == 1`, `BlockedItems[0].CardId == card 2`, `BlockedBy == ["CARD-0001"]` | leave blocked cards in `candidates` | `backlog.Total.ShouldBe(2)` |
| T-17 | `ExpectationSnapshotTests.C100_Backlog_candidates_exclude_cards_with_an_open_blocker` | `World`; Backlog cards E (eligible), H (blocked by open Backlog card O), R (blocked by Done card D), Z (blocked by archived card X); rows seeded directly; `ReadAsync` → `BacklogCandidateIds` = {E, R, Z, O} sorted, `EligibleBacklog == 4` | drop the predicate in `ReadBacklogAsync` | `snapshot.EligibleBacklog.ShouldBe(4)` (5 arrives) |
| T-18 | `CardRelationshipScriptTests.Block_reads_the_card_then_posts_the_relationship_body_with_the_fetched_token` | stub answers GET limits, GET `/api/cards/CARD-0051` (token `11111111-…`), POST `/api/cards/<id>/relationships` (201); run `card.ps1 block CARD-0051 -BlockedBy CARD-0049 -Reason 'schema first' -By operator`; exit 0; recorded calls in order GET card, POST; POST JSON body `concurrencyToken == "11111111-…"`, `relatedCard == "CARD-0049"`, `kind == "BlockedBy"`, `reason`, `createdBy == "operator"`, no `relatedBoardId`; stdout contains `blocked by  CARD-0049` | post `$Token` (empty default) instead of `Resolve-Token` | `body.GetProperty("concurrencyToken").GetString().ShouldBe("11111111-…")` |
| T-19 | `CardRelationshipScriptTests.Unblock_resolves_the_row_by_relatedIdentifier_and_posts_remove` | stub GET card, GET relationships with two `blockedBy` rows (`relatedIdentifier` CARD-0049 id R1 whose `relatedTitle` is "CARD-0050 lookalike", and CARD-0050 id R2), POST `…/relationships/<rid>/remove` → 200 CardDto; run `unblock CARD-0051 -BlockedBy CARD-0049`; exit 0; the remove path ends with `/relationships/R1/remove`; body token equals the fetched token | match on `relatedTitle` | `removePath.ShouldEndWith("/relationships/R1/remove")` |
| T-20 | `CardRelationshipScriptTests.Get_and_history_print_blocked_by_blocks_and_relationship_rows` | stub GET card with `blockers [{CARD-0049, InProgress, false}, {CARD-0048, Done, true}]`, GET relationships `blockedBy` two rows / `blocks` one row (CARD-0052), GET revisions with one `RelationshipAdded` row (`relatedCardIdentifier` CARD-0049); `get` stdout has lines `blocked by  CARD-0049 (InProgress)`, `blocked by  CARD-0048 (resolved: Done)`, `blocks      CARD-0052`; `history` stdout line contains `RelationshipAdded` and `-> CARD-0049`; `get -Json` passes the card through unchanged | drop the `blocked by` lines from `get` | `stdout.ShouldContain("blocked by  CARD-0049 (InProgress)")` |
| T-21 | `CardRelationshipScriptTests.Unblock_without_a_matching_blocker_exits_non_zero_and_names_the_current_blockers` | relationships `blockedBy` = [CARD-0049]; run `unblock CARD-0051 -BlockedBy CARD-0047`; exit code non-zero; stderr names `CARD-0049`; no POST recorded | `exit 0` on no match | `exitCode.ShouldNotBe(0)` |
| T-22 | `CardBlockers.test.tsx` "adds a blocker with the current token and shows it with its status" | msw GET relationships `[]`, POST relationships records body → returns the frozen 201; type `CARD-0049`, click Add; body `{ concurrencyToken: 'token-1', relatedCard: 'CARD-0049', kind: 'BlockedBy' }`; after refetch the list shows `CARD-0049` with an `InProgress` chip | omit `concurrencyToken` from the POST body | `expect(post).toHaveBeenCalledWith(expect.objectContaining({ concurrencyToken: 'token-1' }))` |
| T-23 | `CardBlockers.test.tsx` "a resolved blocker is struck through and still removable" | row `resolved: true`; the identifier text has `text-decoration: line-through` (data attribute `data-resolved="true"`); the Remove button exists and posts `…/remove` with the token | render resolved rows as open | `expect(item).toHaveAttribute('data-resolved', 'true')` |
| T-24 | `CardBlockers.test.tsx` "hides add and remove on an archived card" | `archivedAt` set; no `Add` button, no `Remove` button; the list still renders | always show the controls | `expect(screen.queryByRole('button', { name: 'Add blocker' })).toBeNull()` |
| T-25 | `BoardCard.test.tsx` "shows a Blocked chip naming the blockers when openBlockerCount is positive" | `openBlockerCount: 1, blockers: [{identifier:'CARD-0049', status:'InProgress', resolved:false}]`; tile shows text `Blocked`; the chip's `title`/tooltip text contains `CARD-0049` | render on `blockers.length` instead of the count | case `openBlockerCount: 0, blockers: [{…resolved:true}]` in the same test: `expect(within(tile).queryByText('Blocked')).toBeNull()` |
| T-26 | `BoardCard.test.tsx` "shows no Blocked chip when openBlockerCount is zero or absent" | default card (field absent) and `openBlockerCount: 0`; no `Blocked` text | always render the chip | `expect(within(tile).queryByText('Blocked')).toBeNull()` |
| T-27 | `CardHistory.test.tsx` "labels relationship revisions and names the related card" | revisions `[RelationshipAdded (relatedCardIdentifier 'CARD-0049'), RelationshipRemoved ('CARD-0048')]`; rows show badge text `Blocked by added` / `Blocked by removed` and the text `-> CARD-0049` / `-> CARD-0048` | render `relatedCardId` instead of `relatedCardIdentifier` | `expect(within(row).getByText('-> CARD-0049')).toBeInTheDocument()` |
| T-28 | `CardModal.test.tsx` "renders the Blocked-by panel in the sidebar from the relationships query" | handlers `cardHandler`, `discussionHandler`, `agentDefinitionsHandler`, GET `/api/cards/card-1/relationships` → one `blockedBy` row CARD-0049; `within(screen.getByTestId('card-detail-sidebar')).findByTestId('card-blockers')` shows `CARD-0049`; the handler was called once | do not mount `CardBlockers` in the sidebar | `findByTestId('card-blockers')` times out |
| T-29 | `CardTaskFileRendererTests.Frontmatter_key_set_is_exactly_the_public_whitelist` | render a card with every nullable field set (alias, due, started, completed, external tracker/key/url/author, needsHumanReview, archived/by/reason); take the lines between the two `---` fences, split on `:` → key list equals exactly `[id, identifier, title, alias, status, importance, importance_provenance, urgency, due, labels, created, started, completed, external_tracker, external_key, external_url, external_author, needs_human_review, archived, archived_by, archived_reason]`; the whole rendered text does not contain `block` (case-insensitive); `typeof(CardFilePublicCard)` constructor parameter names equal the frozen 24-name list | add `blocked_by:` to `RenderCard` | `keys.ShouldBe(expectedKeys)` |

Counts by class: `CardRelationshipSchemaTests` 3; `CardRelationshipApiTests` 11 methods / 19
executions; S3 one method each; `CardRelationshipScriptTests` 4; client 7; `CardTaskFileRendererTests`
11 → 12.

## Fixtures and isolation (binding on Code)

- **Schema tests (T-1..T-3)** copy `CardCleanupSchedulingSchemaTests` (CARD-1033): `[Category("Integration")]
  public sealed class`, no web host; each test `await using var schema = await
  TestDbFixture.CreateIsolatedSchemaAsync();` and `new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString))`.
  Cost: one `CREATE DATABASE test_<guid> TEMPLATE <template>` clone per test, about 100–300 ms
  (`AntiphonWebAppFactory.cs:48`), dropped `WITH (FORCE)` on dispose. The template is built once per
  assembly by `MigrateAsync` on the shared store and then `CREATE DATABASE … TEMPLATE`
  (`TestDbFixtureLifecycle.cs:48-69`, `:313-324`), so the S1 migration is in every clone with no
  fixture edit. `ExecuteDelete` of a card (T-3) therefore touches no other suite's rows.
- **`AntiphonWebAppFactory.ResetAsync` is not a database reset.** It clears `IResettableCache`
  singletons only (`:165-170`); the factory owns one cloned database per factory instance shared by
  every test of the session. The schema tests do not need it. The API tests (T-4..T-14) copy
  `CardCommentApiTests`/`CardCorrectionApiTests`: `[NotInParallel]`,
  `[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]`, `[Before(Test)] ResetAsync`,
  `[After(Test)]` cleanup by project: `CardRelationships` (both `CardId` and `RelatedCardId` in the
  project's card ids) → `CardRevisions` → `Cards` → `BoardWorkflowDefinitions` → `BoardColumns` →
  `Boards` → `Projects` via `ExecuteDeleteAsync`. Status moves in T-11/T-13 go through
  `PATCH /api/cards/{id}` (`MoveCardRequest`) and `POST …/reopen`, `…/archive`, `…/unarchive` with the
  current token, never by editing the entity, so the "no write on the blocked card" claim is read over
  HTTP after a real write path.
- **T-15** uses the class's `CreateContext()` (assembly store), `CreateGraph`, `AddCard`, `BuildHarness`,
  `PollTickAsync`, `LaunchQueue.WaitForIdleAsync` exactly as `PollTick_skips_a_card_whose_auto_dispatch_is_held`
  (`OrchestratorServiceIntegrationTests.cs:194-222`); the class is already `[NotInParallel]`. Do not
  assert `result.Dispatched` (assembly-global counter); assert the session rows and `adapter.Started`.
- **T-16** uses `C557Seed` (`seed.Card(number, board, status:)`), adds `db.Add(new CardRelationship{…})`
  rows before `seed.SaveAsync()`, reads through `C557Read(db)`; the fixture clones its own schema.
- **T-17** uses `World.CreateAsync`, the `Card(id, world, prefix, status:, archivedAt:)` helper and
  `new ExpectationSnapshotReader(read).ReadAsync(world.Directive, world.Digest, now, ExpectationProbeInput.None, ct)`.
- **Script tests** copy `CardListSearchScriptTests` (`[Category("Integration")]
  [ParallelLimiter<ProcessSpawnLimit>]`, `HttpListener` stub bound through `EphemeralHttpListener`,
  `RunAsync` sets `ANTIPHON_API` and `ANTIPHON_TASK_TOKEN`), with the `CardDiagnoseScriptTests.StubApi`
  shape that branches on `HttpMethod` and reads `Request.InputStream` so POST bodies are recorded.
- **Client tests** use `renderWithProviders`, `server.use(http.get/post…)` from `src/test/mocks/server`
  (`CardModal.test.tsx:1-90`). `CardModal` renders `CardDetails` twice (details tab and sidebar); T-28
  scopes to `card-detail-sidebar`. `CardBlockers` carries `data-testid="card-blockers"`, rows
  `data-testid="card-blocker-<identifier>"` and `data-resolved`.

## Verification design

### Inspection
- Bodies read: `Card.cs`, `CardRevision.cs`, `CardRevisionKind.cs`, `CardComment.cs`,
  `CardRevisionLog.cs` (whole), `CardService.cs` (:205-236, :347-470, :501-540, :588-652, :684-712,
  :991-1052, :1308-1321, :1540-1600), `CardIdentifierScope.cs` (:12-84, :125-147),
  `AppDbContext.cs` (:1208-1345), `BoardDtos.cs` (:55-120, :205-230, :325-385), `BoardService.cs`
  (:355-400, :491-520), `CardEndpoints.cs` (:1-60, :75-90, :160-235, :314-335),
  `ExceptionMiddleware.cs` (:40-100), the three exception classes, `OrchestratorService.cs` (:819-867),
  `AgentTaskPipelineStatusService.cs` (:140-185), `AgentTaskPipelineDtos.cs` (:67-78),
  `ExpectationSnapshotReader.cs` (:340-375), `CardTaskFileRenderer.cs` (:94-136),
  `CardFilePublicCard.cs`, `scripts/card.ps1` (header, params, helpers :395-420, `get`/`history`,
  `reorder`), `test-client.ps1`, `CardAxisBadges.tsx`, `BoardCard.tsx` (:40-70), `CardHistory.tsx`
  (:55-100), `CardModal.tsx` (:40-60, :250-300), `boards.ts` (hooks), and the test fixtures named above
  (`AntiphonWebAppFactory.cs` whole, `TestDbFixtureLifecycle.cs` :48-69/:215-262,
  `CardCleanupSchedulingSchemaTests.cs` whole, `CardCommentApiTests.cs`, `CardCorrectionApiTests.cs`,
  `OrchestratorServiceIntegrationTests.cs` :1-80/:186-224/:1352-1450/:1588-1616,
  `AgentTaskPipelineStatusC557Tests.cs` :25-110/:249-262, `ExpectationSnapshotTests.cs` :1-60/:196-244/:554-580,
  `CardListSearchScriptTests.cs` :1-116, `CardDiagnoseScriptTests.cs` :60-135, `CardTaskFileRendererTests.cs`,
  `BoardCard.test.tsx`, `CardHistory.test.tsx`, `CardModal.test.tsx` :1-135, `lint.gate.test.ts`).
- Boundaries: schema → V-1; service/API → V-2..V-5; tick/pipeline/watchdog → V-6; script → V-7;
  client → V-8; card-file privacy → R-2/T-29; task creation (D-13) → excluded (no changed line).

### Delivery inventory
This card adds no asynchronous delivery path: no session input, no queue, no scheduled job, no
tracker push (D-11). The one event, `CardChanged` published through `_eventBus.PublishToAllAsync`
on add/remove (same call as `CardService.cs:649`), is a best-effort UI refresh hint with no
persistence boundary, no recovery and no recipient the server waits for; the client hooks
invalidate their own queries on mutation success, so no test depends on its receipt and none
claims it. Every persistence claim is proven by write → status → HTTP read-back in that order
inside one test (T-4 GET card and GET revisions after the 201; T-12 GET both cards' relationships
after the 200; T-11 GET after each move). Declared substitutes: none. Not applicable: UserPrompt
transcripts, busy recipients, enqueue-failure recovery.

### Proves it works now
- V-1: the schema refuses duplicates and self-reference, cascades on either card's delete, and the generated migration matches the model | DB | T-1, T-2, T-3 | CP-1, 3 passed
- V-2: adding a blocker writes one row and one `RelationshipAdded` revision on the blocked card, rotates only that card's token, and refuses empty/stale tokens | HTTP | T-4, T-5, T-6 | CP-3
- V-3: self, unknown kind, over-length text, duplicate, cycle (2 and 3 hops), archived blocker, archived blocked card and unknown card are refused with the frozen statuses and codes; a diamond is not a cycle | HTTP | T-7, T-8, T-9, T-10 | CP-3
- V-4: `resolved` follows Done/Canceled/archived and their undo without a write; remove records `RelationshipRemoved` and clears both views | HTTP | T-11, T-12 | CP-3
- V-5: card and list views carry `openBlockerCount`/`blockers` excluding resolved rows and omit them on untouched cards; cross-board resolution prefers the blocked card's board and honours the fence | HTTP | T-13, T-14 | CP-3
- V-6: the tick, the pipeline backlog and the watchdog backlog exclude cards with an open blocker; the pipeline lists them with their blockers | service, real `PollTickAsync` / `GetAsync` / `ReadAsync` | T-15, T-16, T-17 | CP-5, CP-6, CP-7
- V-7: `card.ps1 block`/`unblock`/`get`/`history` send and print the contract and fail closed on no match | pwsh against an HTTP stub | T-18..T-21 | CP-8
- V-8: the panel posts the token, renders resolved and archived states, the tile chip keys on the count, history labels the kinds, the modal mounts the panel | vitest/jsdom | T-22..T-28 | CP-10

### Guards the regression
- R-1: content edit, archive, move, reopen, list and summary wire shapes are unchanged by the new `CardRevision` columns, DTO fields and includes | `CardCorrectionApiTests` (12), decisive: `Full_board_views_remain_wire_identical_and_do_not_emit_summary_metadata` | CP-2
- R-1b: identifier resolution is unchanged by the new scope caller | `CardIdentifierResolutionTests` (18) | CP-4
- R-2: no relationship field reaches a card file | T-29 (new, decisive key-set equality) plus the existing 11 `CardTaskFileRendererTests` | CP-4
- R-3: existing orchestrator, pipeline and snapshot behaviour | `OrchestratorServiceIntegrationTests` (27), `AgentTaskPipelineStatusTests` (71), `ExpectationSnapshotTests` (6) | CP-5, CP-6, CP-7
- R-4: existing card.ps1 list/search behaviour with the shared helper edits | `CardListSearchScriptTests` (11) | CP-9
- R-5: existing board client tests, the whole client suite and the lint gate (`lint.gate.test.ts`) | `CardRow.test.tsx` (26), `BacklogSection.test.tsx` (10), `CardModal.test.tsx` (18), `BoardCard.test.tsx` (2), `CardHistory.test.tsx` (12), full suite | CP-10, CP-11

### Guard inventory
Safety-critical = can hold or release orchestrator dispatch, can corrupt or bypass a card write
(token, history, graph), or can leak into a published card file. UI rendering (chip, labels, panel
mount: T-23, T-25..T-28) is not safety-critical and has no PC; each still names its mutation above.

- G-1: D-6 cycle walk refuses a 3-hop cycle (depth) | PC-1
- G-2: D-6 cycle walk refuses a 2-hop cycle (edge check) | PC-2
- G-3: D-6 visited set, a diamond is not a cycle | PC-3
- G-4: D-6 self-reference refused at the service (422, not a DB 500) | PC-4
- G-5: D-6 duplicate pair refused with its code before the index | PC-5
- G-6: D-4 empty token refused | PC-6
- G-7: D-4 stale token refused | PC-7
- G-8: D-4 blocker token and history untouched | PC-8
- G-9: D-9 archived blocker refused | PC-9
- G-10: D-9 archived blocked card refused on add | PC-10
- G-11: D-9 archived blocked card refused on remove | PC-11
- G-12: A-4 unknown `kind` refused (whitelist) | PC-12
- G-13: A-4 over-length reason refused | PC-13
- G-14: D-8 `RelationshipAdded` revision written | PC-14
- G-15: D-8 `RelationshipRemoved` revision written | PC-15
- G-16: D-5/D-7 resolved set is exactly Done/Canceled/archived (fail-closed on anything else) | PC-16
- G-17: D-5 tick excludes a card with an open blocker | PC-17
- G-18: D-5 pipeline backlog excludes blocked cards | PC-18
- G-19: S3 pipeline lists blocked cards with their blockers | PC-19
- G-20: D-5 watchdog backlog excludes blocked cards | PC-20
- G-21: `OpenBlockerCount` excludes resolved rows | PC-21
- G-22: D-4 `relatedBoardId` fence honoured | PC-22
- G-23: S1 unique index | PC-23
- G-24: S1 check constraint | PC-24
- G-25: S1 cascade from the blocker FK | PC-25
- G-26: S1 cascade from the blocked-card FK | PC-26
- G-27: S1 migration matches the model (no pending model changes) | PC-27
- G-28: S4 `block` posts the freshly fetched token | PC-28
- G-29: S4 `unblock` matches on `relatedIdentifier` | PC-29
- G-30: S4 `unblock` fails closed with no match | PC-30
- G-31: D-10/OD-4 card-file front matter has no relationship key | PC-31
- G-32: S5 add posts the current token | PC-32
- G-33: S5 add/remove hidden on an archived card | PC-33

guards=33, mapped=33, missing=0, duplicate PC maps=0.

### Positive controls
Pending: post-land Mutation is paused; Code runs V/R only; Review judges the design. Each PC is
method-scoped (`--treenode-filter "/*/*/Class/Method*"`; vitest `-t "<name>"`), red → restore →
green, in a scratch copy, never committed. Rows of one `[Arguments]` method are inspected in the
fresh TRX.

- PC-1: break G-1 by capping the breadth-first walk at depth 1; expect `CardRelationshipApiTests.Relationship_write_refusal_table` red at the `three_cycle` row `StatusCode.ShouldBe(Conflict)` while `two_cycle` stays green.
- PC-2: break G-2 by skipping the walk when the blocker has exactly one `BlockedBy` edge; expect the `two_cycle` row red at `StatusCode.ShouldBe(Conflict)`.
- PC-3: break G-3 by treating a re-visited node as a cycle; expect the `diamond` row red at `StatusCode.ShouldBe(Created)`.
- PC-4: break G-4 by deleting the `relatedCardId == card.Id` check; expect the `self` row red at `StatusCode.ShouldBe(UnprocessableEntity)` (500 from `CK_CardRelationships_NotSelf`).
- PC-5: break G-5 by deleting the existence query; expect `Duplicate_pair_is_409_card_relationship_exists` red at `code…ShouldBe("card_relationship_exists")`.
- PC-6: break G-6 by deleting the `Guid.Empty` check; expect `An_empty_or_stale_token_is_refused` `empty` row red at `StatusCode.ShouldBe(UnprocessableEntity)`.
- PC-7: break G-7 by deleting the token comparison; expect the `stale` row red at `StatusCode.ShouldBe(Conflict)`.
- PC-8: break G-8 by setting `blocker.ConcurrencyToken = Guid.NewGuid()` in `AddRelationshipAsync`; expect `Adding_a_blocker_leaves_the_blockers_token_and_revisions_untouched` red at `blockerAfter.ConcurrencyToken.ShouldBe(…)`.
- PC-9: break G-9 by deleting the blocker `ArchivedAt` check; expect the `archived_blocker` row red at `StatusCode.ShouldBe(Conflict)`.
- PC-10: break G-10 by deleting the blocked-card `ArchivedAt` check in `AddRelationshipAsync`; expect `An_archived_blocked_card_refuses_add_and_remove` red at `add.StatusCode.ShouldBe(Conflict)`.
- PC-11: break G-11 by deleting the same check in `RemoveRelationshipAsync`; expect the same method red at `remove.StatusCode.ShouldBe(Conflict)`.
- PC-12: break G-12 by accepting any `kind` string; expect `Unknown_kind_and_over_length_text_are_422` `kind` row red at `StatusCode.ShouldBe(UnprocessableEntity)`.
- PC-13: break G-13 by deleting the reason length check; expect the `reason` row red at `StatusCode.ShouldBe(UnprocessableEntity)`.
- PC-14: break G-14 by deleting `AppendRelationship` on add; expect `Add_blocker_by_identifier_returns_201_rotates_the_token_and_writes_RelationshipAdded` red at `revisions.Count.ShouldBe(1)`.
- PC-15: break G-15 by deleting `AppendRelationship` on remove; expect `Remove_writes_RelationshipRemoved_rotates_the_token_and_clears_both_views` red at `revisions[0].Kind.ShouldBe(RelationshipRemoved)`.
- PC-16: break G-16 by dropping `Canceled` from the resolved set; expect `Resolved_follows_the_blockers_Done_Canceled_archived_state_and_reopen` red at `afterCancel.Resolved.ShouldBeTrue()`.
- PC-17: break G-17 by deleting `.Where(CardBlockers.NotBlocked)` in `LoadEligibleCandidatesAsync`; expect `PollTick_skips_a_card_with_an_open_blocker_and_dispatches_after_the_blocker_closes` red at the first-tick `CountAsync…ShouldBe(0)`.
- PC-18: break G-18 by leaving blocked rows in `candidates`; expect `C100_investigate_backlog_omits_blocked_cards_and_lists_them_as_blocked` red at `backlog.Total.ShouldBe(2)`.
- PC-19: break G-19 by returning `Blocked = 0, BlockedItems = []`; expect the same method red at `backlog.Blocked.ShouldBe(1)`.
- PC-20: break G-20 by deleting the predicate in `ReadBacklogAsync`; expect `C100_Backlog_candidates_exclude_cards_with_an_open_blocker` red at `EligibleBacklog.ShouldBe(4)`.
- PC-21: break G-21 by counting every row; expect `Card_and_list_views_carry_open_blocker_count_and_blockers_excluding_resolved` red at `afterDone.OpenBlockerCount.ShouldBe(1)`.
- PC-22: break G-22 by passing `ExplicitBoardId: null`; expect `Cross_board_blocker_resolves_on_the_blocked_cards_board_by_default_and_honours_relatedBoardId` red at `fenced.RelatedCardId.ShouldBe(board2Card.Id)`.
- PC-23: break G-23 by removing `IsUnique()` and regenerating the migration in the scratch copy; expect `C100_Duplicate_pair_is_refused_by_the_unique_index` red at `duplicateAccepted.ShouldBeFalse()`.
- PC-24: break G-24 by removing the check constraint (regenerated migration); expect `C100_Self_reference_is_refused_by_the_check_constraint` red at `selfAccepted.ShouldBeFalse()`.
- PC-25: break G-25 by `OnDelete(Restrict)` on the `RelatedCard` FK (regenerated); expect `C100_Deleting_either_card_cascades_its_relationship_rows` red at the blocker `ExecuteDeleteAsync` (PostgresException 23503).
- PC-26: break G-26 by `OnDelete(Restrict)` on the `Card` FK (regenerated); expect the same method red at the blocked-card `ExecuteDeleteAsync`.
- PC-27: break G-27 by adding a property to `CardRelationship` without a migration; expect `C100_Duplicate_pair_is_refused_by_the_unique_index` red at `HasPendingModelChanges().ShouldBeFalse()`.
- PC-28: break G-28 by posting `$Token` without `Resolve-Token`; expect `Block_reads_the_card_then_posts_the_relationship_body_with_the_fetched_token` red at the body token assertion.
- PC-29: break G-29 by matching `relatedTitle`; expect `Unblock_resolves_the_row_by_relatedIdentifier_and_posts_remove` red at `removePath.ShouldEndWith("/relationships/R1/remove")`.
- PC-30: break G-30 by `exit 0` on no match; expect `Unblock_without_a_matching_blocker_exits_non_zero_and_names_the_current_blockers` red at `exitCode.ShouldNotBe(0)`.
- PC-31: break G-31 by appending `blocked_by: []` in `RenderCard`; expect `Frontmatter_key_set_is_exactly_the_public_whitelist` red at `keys.ShouldBe(expectedKeys)`.
- PC-32: break G-32 by omitting `concurrencyToken` from the client POST body; expect "adds a blocker with the current token and shows it with its status" red at `toHaveBeenCalledWith(objectContaining({ concurrencyToken }))`.
- PC-33: break G-33 by rendering the controls regardless of `archivedAt`; expect "hides add and remove on an archived card" red at `queryByRole('button', { name: 'Add blocker' })).toBeNull()`.

### Out of scope
- D-13 (task creation against a blocked card keeps working): no production line changes; the existing `AgentTaskService` create tests stay the guard; a refusal would be OD-3, a separate card.
- `CardChanged` receipt by a browser: no test; the hooks refetch on mutation success (see Delivery inventory).
- `BacklogRow` (orchestrator screen) chip: `investigateBacklog.items` never contains a blocked card after S3, so the chip cannot show there; `BacklogSection.test.tsx` runs as regression only.
- Full `Antiphon.Tests` assembly, namespace runs, `Antiphon.Agents.Pty.Tests`: no touched line.
- Platform: every row runs on the default placement (server2); no row needs an OS.
- Monotonic time: no deadline or wait is introduced; `CreatedAt` uses the service's existing `UtcNow()`.

### Checkpoints

One run per committed slice group through the checkpoint tool
(`dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-14-card-0100-test-design.md --after <group> --expected-source-sha <sha> --serial --max-wait 55s`,
then `wait` until the exit is not 75). The tool bootstrap build (`bin-c0100-tool/`, `UseAppHost=false`)
is the one unlisted, slot-gated build. Remove every `bin-c0100-*` directory before the task ends.
Filters were checked against the census above; a zero-test run is a wrong filter, not a pass.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c0100-s12/` | relationship-schema | `/*/*/CardRelationshipSchemaTests/*` | V-1 | all 3 methods, 0 failed/skipped | 3 | 6 | true |
| CP-2 | S1-S2 | CP-1 | correction-regression | `/*/*/CardCorrectionApiTests/*` | R-1 | all 12 methods, 0 failed/skipped | 12 | 3 | true |
| CP-3 | S1-S2 | CP-1 | relationship-api | `/*/*/CardRelationshipApiTests/*` | V-2, V-3, V-4, V-5 | 19 executions over 11 methods (T-6 2 rows, T-7 3 rows, T-9 6 rows), 0 failed/skipped | 19 | 5 | true |
| CP-4 | S1-S2 | CP-1 | identifier-and-renderer-regression | `/*/Antiphon.Tests.Application/(CardIdentifierResolutionTests*)\|(CardTaskFileRendererTests*)/*` | R-1b, R-2 | 18 + 12 = 30 executions, both classes present in the TRX, 0 failed/skipped | 30 | 3 | true |
| CP-5 | S3 | `tests/Antiphon.Tests -> bin-c0100-s3/` | orchestrator-eligibility | `/*/*/OrchestratorServiceIntegrationTests/*` | V-6, R-3 | all 28 methods, 0 failed/skipped | 28 | 12 | true |
| CP-6 | S3 | CP-5 | pipeline-backlog | `/*/*/AgentTaskPipelineStatusTests/*` | V-6, R-3 | 72 executions across the four partial files, 0 failed/skipped | 72 | 5 | false |
| CP-7 | S3 | CP-5 | snapshot-backlog | `/*/*/ExpectationSnapshotTests/*` | V-6, R-3 | all 7 methods, 0 failed/skipped | 7 | 2 | false |
| CP-8 | S4 | `tests/Antiphon.Tests -> bin-c0100-s4/` | card-script-relationships | `/*/*/CardRelationshipScriptTests/*` | V-7 | all 4 methods, 0 failed/skipped | 4 | 6 | false |
| CP-9 | S4 | CP-8 | card-script-regression | `/*/*/CardListSearchScriptTests/*` | R-4 | all 11 methods, 0 failed/skipped | 11 | 2 | false |
| CP-10 | S5 | n/a | client-card-blockers | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/board/CardBlockers.test.tsx src/features/board/BoardCard.test.tsx src/features/board/CardHistory.test.tsx src/features/board/CardModal.test.tsx src/features/board/CardRow.test.tsx src/features/orchestrator/BacklogSection.test.tsx` | V-8, R-5 | 6 files; `Tests 75 passed` (68 existing + 7 new), 0 failed/skipped; `CLIENT TESTS EXIT CODE: 0` | n/a | 4 | false |
| CP-11 | S5 | n/a | client-full | `pwsh -NoProfile -File scripts/test-client.ps1` | R-5 (whole suite and `lint.gate.test.ts`) | `CLIENT TESTS EXIT CODE: 0`, 0 failed/skipped, `lint gate` present in the run | n/a | 10 | true |

Census behind the counts (2026-10-14, source): `CardCorrectionApiTests` 12; `CardIdentifierResolutionTests`
18; `CardTaskFileRendererTests` 11 (+1 = 12); `OrchestratorServiceIntegrationTests` 27 (+1 = 28);
`AgentTaskPipelineStatusTests` 39 + 19 + 9 + 4 = 71 (+1 = 72); `ExpectationSnapshotTests` 6 (+1 = 7);
`CardListSearchScriptTests` 11; client `BoardCard` 2 (+2), `CardHistory` 12 (+1), `CardModal` 18 (+1),
`CardRow` 26, `BacklogSection` 10, `CardBlockers` 0 (+3). Green client runs print the aggregate only,
so Code reports the `Tests N passed` line plus a per-file source census. No census class has a
`[Skip]`, `[Explicit]`, platform attribute or `MethodDataSource`.

### Cost
- Ordinary V/R floor (Code) = sum of EstimatedMinutes = 6 + 3 + 5 + 3 + 12 + 5 + 2 + 6 + 2 + 4 + 10 = **58 minutes** (estimated): S1-S2 task 17 (one build), S3 19 (one build), S4 8 (one build), S5 14 (no build). Plus the tool bootstrap build, about 4 minutes, once per Code task.
- Savings versus the plan: one isolated build removed (S1 and S2 share `bin-c0100-s12/`), about 4 minutes; the plan's two-filter CP-4 is one combined-class row.
- PC floor (Mutation, pending): 33 PCs. Server-side PCs (PC-1..PC-31) each need a scratch build plus red and green runs of one method, about 7 minutes unbatched = 217 minutes; batched by independent file and method (PC-1..PC-5 share `AddRelationshipAsync` and run separately; the rest form roughly eight batches) about 8 × 8 = 64 minutes plus one baseline, estimated 70. Client PCs (PC-32, PC-33) about 3 minutes each = 6. Schema PCs (PC-23..PC-27) need a regenerated migration in the scratch copy; count them in the server-side figure.
- Total (estimated) = setup/build 4 per Code task × 4 = 16 + V/R 58 + PC 76 = **150 minutes** (unbatched PC alternative 239).

## Collisions and ordering (re-checked 2026-10-14 against `origin/master` = `e0877982c`)

| Slice | Checked | Finding | Handling |
|---|---|---|---|
| S1 | Every `origin/feat/*` branch committed since 2026-10-05: `git diff --name-only --diff-filter=A origin/master <branch> -- server/Migrations` | No open branch adds a migration file absent from master; `20261012112137_AddCardCleanupScheduling` (CARD-1033) and `20261012075920_AddTaskWorktreeMirrorBinding` (CARD-1032 S1) are already on master. Branches still differing in `AppDbContext.cs` (CARD-1032/1033/0505/0822 carriers) are landed work whose commits predate the master tip. | The Code dispatcher repeats the command at dispatch time; on a hit, land the other migration first and generate `AddCardRelationships` on the rebased tip. The pipeline snapshot read from this runner's `ANTIPHON_API` showed no in-flight Code task, so it is not evidence of fleet state; the git check is. |
| S2 | `CardService.cs`, `CardEndpoints.cs`, `BoardDtos.cs`, `BoardService.cs`, `CardRevisionLog.cs` on open branches | No CARD-0097, CARD-0506, CARD-0507 or CARD-1213 branch touches them. `docs/antiphon-api.md` open hunks are at lines 457, 538-566, 569, 827 (CARD-0881, CARD-0506 S4, CARD-1123, CARD-1085); S2 edits the cards block at lines 151-166. | Distinct hunks; accept the rebase. |
| S3 | `docs/orchestration-loop.md`: CARD-1213 hunks at 222, 236, 986, 1181; CARD-0506 S4 hunks at 810, 925. S3 edits §1 step 4 (line 481-487) and `docs/agent-card-lifecycle.md` "Status Rules" (line 43). `AgentTaskPipelineStatusService.cs` differs on CARD-1093/CARD-1100 branches dated 2026-10-11 (comment and hold-stint fixes). | Distinct hunks; dispatcher confirms CARD-1093/1100 are landed (`git log origin/master --grep`) before S3 and defers otherwise. |
| S4 | `scripts/card.ps1`, `server/Bundles/board-api.md`, `docs/ops-http.md` | No open branch touches them. | None. |
| S5 | `client/src/api/boards.ts`, `client/src/features/board/**` | No open branch touches them (CARD-0097/0506 client work is under `features/settings`). | None. |
| docs/cards | generated | Never edited (CARD-0004). | — |

Scope strings are the plan's; S1+S2 dispatch as one Code task with the union of the S1 and S2
scopes plus `tests/Antiphon.Tests/Application/CardTaskFileRendererTests.cs` (T-29). S3 adds
`tests/Antiphon.Tests/Application/AgentTaskPipelineStatusC100Tests.cs`.

## Code brief (first slice)

Dispatch **S1+S2 as one Code task** on the default placement, Worktree, scope as above. Deliver in
two commits (S1 schema and history kinds, S2 service/routes/DTOs/tests/doc), then run the checkpoint
tool with `--after S1-S2` at the second commit's SHA. Follow A-1..A-6, A-11, A-12 and the frozen
contract; write T-1..T-14 and T-29 exactly as named; every new test is red-first against its named
mutation line (Review rejects a test that cannot go red). S3, S4 and S5 queue behind it, each its
own worktree, in parallel.

--- next stage ---
next: code
handoff: Code S1+S2 as one task on the default placement: CardRelationship entity/migration/history kinds (A-1, A-5), CardBlockers predicate, three routes with the frozen contract and codes (A-2..A-4, A-11, A-12), tests T-1..T-14 and T-29 red-first, then checkpoint rows CP-1..CP-4 with --after S1-S2; S3, S4, S5 queue behind it in parallel.
artifact: docs/superpowers/plans/2026-10-14-card-0100-test-design.md
