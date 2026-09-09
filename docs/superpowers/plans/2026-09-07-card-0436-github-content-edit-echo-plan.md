# CARD-0436: prevent GitHub content-edit echoes

Plan against `31a2c9e4b6722fead57d97c8ba5482dfb3138d7e`, dated 2026-09-07.
The fix prevents new duplicate discussion imports and recognizes markers already
posted by older servers. Treatment of the 43 existing External rows remains an
operator decision. This stage changes documentation only; implementation and
executable verification follow in TestDesign and Code.

Owners: [project conventions](../../project-context.md),
[workflow tracker](../../workflow-tracker-block.md),
[testing/build](../../testing-and-build.md),
[stage workflow](../../orchestration-loop.md), and
[card lifecycle](../../agent-card-lifecycle.md).

## Ground truth

The complete investigation report was read with
`pwsh -NoProfile -File scripts/delegate.ps1 -Status a2e1ac41`.
Its live observations below are attributed to that investigation, not a new live
database census by this Plan task.

| Card assumption or proposed behavior | Evidence and consequence |
|---|---|
| The matching outbound/inbound counts might only be notification wording. | Investigation a2e1ac41 found 33 distinct GitHub comment IDs created 2026-09-07 09:00:12-09:00:34 BST, imported as `CardCommentOrigin.External` with author `michal-ciechan`. All 33 markers matched same-card ContentEdit revisions, and none matched a discussion-comment ID. Ten older revision echoes were present from September 1-3. |
| This might be an escalating public spam loop. | `PushDiscussionCommentsAsync` selects only Antiphon-origin rows with null external ID and claim stamp. External imports cannot enter that query. `PullCommentsAsync` deduplicates stored external IDs, and the investigation confirmed the 33 outbound revision cursors had advanced. The demonstrated bug is one duplicate local discussion row per affected GitHub comment. |
| Content-edit posts identify an existing discussion comment. | `TrackerBidirectionalSyncService.PushContentEditCommentsAsync` currently calls `AppendCommentMarker(body, edit.Id)` (line 460 at this base); `edit.Id` is a CardRevision ID. It discards the returned GitHub comment object. `PullCommentsAsync` searches only CardComments for that marker, then fails open into an External insert and a `CommentIn` change. |
| A new marker format is needed. | `TrackerSyncMarkers.AppendSystemCommentMarker(body, cardId)` already emits a trailing `antiphon:system-comment=<card GUID in N format>` marker. The inbound branch already skips it only when it matches the tracked issue's `CardId`. State comments use this format and already have echo coverage. |
| Fixing the outbound format also fixes all old posts. | Old GitHub bodies retain their revision-ID markers. They require a compatibility branch even after new posts use system markers. A pull watermark is not proof that an older comment can never be fetched again. |
| Filtering the PAT author would be safe. | There is no author exclusion in this service. The same GitHub login can write genuine human comments. Identity filtering would suppress legitimate discussion. |
| Existing tests prove this round trip. | `TrackerBidirectionalSyncTests` covers discussion echoes and terminal/reopen system-comment echoes. `Same_pass_content_edit_comment_does_not_swallow_out_reopen` exercises outbound content edits but never their next inbound pass. `External_tracker_content_edit_is_not_echoed_as_a_GitHub_comment` tests a different outbound guard. |
| Removing the 43 rows is a routine existing discussion API operation. | `CardCommentService` exposes create/list; the entity has no deleted/hidden flag. ExternalCommentId has a filtered unique index. Hiding needs a separate feature, and deletion needs an explicitly approved data-repair operation. |

Investigation examples: CARD-0345, GitHub comment `5567345449`, revision
`01a07ac7-242b-76d0-bf46-3ce10abe027d`; CARD-0337, comment `5567345678`, revision
`01a07ac6-d31e-7380-8b5e-0f03198f327b`; CARD-0286, comment `5567349903`, revision
`01a07ac5-7c35-7eef-b4df-598ae7ed8f92`. The investigation reported the deployed
server as `3a09c6e3` with the relevant source identical to its checkout and a latest
persisted pull of 18:00 BST. These are historical evidence, not deployment checks
for the future fix.

## Decisions

- **D-1: new content-edit posts use the existing system-comment marker with
  `issueRef.CardId`.** A generated revision summary has no CardComment to link.
  Reuse the durable card identity and existing inbound behavior. Preserve the
  human-readable body, outbound count/change, eligible revision selection,
  posting order, error handling and cursor updates. Reject creating synthetic
  discussion rows or a third marker format: neither is needed to prevent echoes.
- **D-2: recognize legacy revision markers after the existing discussion lookup
  misses.** Query `CardRevisions.AnyAsync` with all three predicates:
  `r.Id == markerId && r.CardId == issueRef.CardId &&
  r.Kind == CardRevisionKind.ContentEdit`. On a match, continue without an
  insert, link stamp, or inbound change. Pass the cancellation token. This is one
  additional indexed-ID lookup only for an otherwise unrecognized comment
  marker, with no new service, state, migration or cache. Do not depend on
  `LastRevisionSynced`, timestamps, current ref origin, or author identity: those
  are not the identity of an already-posted legacy artifact.
- **D-3: retain the inbound contract around that narrow addition.** Known
  discussion markers still repair ExternalCommentId/ExternalUrl exactly as
  today; same-card system markers still skip. Unknown, malformed, non-trailing,
  wrong-card, or wrong-kind revision markers still fall through to visible
  External import. Keep the existing external-ID existence query and filtered
  unique index unchanged. Never add an exclusion for `michal-ciechan`, bot-like
  names, or bodies beginning with the content-edit prose. Reword the current
  state-only system-marker comment to describe generated card comments.
- **D-4: existing-row treatment is pending the operator's choice.** The fix does
  not remove, hide, reclassify or rewrite any stored discussion row. This is the
  interim scope while the decision is open, not a decision to retain the rows
  permanently. The options and conditional repair procedure are below. The
  prevention fix can proceed independently of that choice.
- **D-5: keep TestDesign separate.** The brief did not fold it into Plan. The
  next stage must turn the regression requirements below into the canonical
  `## Verification design` section with V-n/R-n/PC-n entries, exact expected
  outcomes and cost. No runtime test result is claimed by this plan.

Rejected wider approaches: author filtering loses human discussion; accepting
every syntactically valid marker silently drops unknown comments; looking up a
revision without both card and kind validation suppresses unrelated discussion;
moving the pull cursor or changing notification wording only masks the defect;
editing old public GitHub comments is unnecessary once compatibility recognition
exists. Preserve ordinary discussion-marker behavior rather than adding an
unrelated validation change to it in this fix.

## Implementation slices

### S-1: implement both marker paths and their regression coverage together

Files:

- `server/Application/Services/TrackerBidirectionalSyncService.cs`
- `tests/Antiphon.Tests/Application/TrackerBidirectionalSyncTests.cs`
- `server/Application/Services/TrackerSyncMarkers.cs` only to clarify the existing
  system-marker XML documentation to include content edits; no format/parser change.

In `PushContentEditCommentsAsync`, replace the single call to
`AppendCommentMarker(body, edit.Id)` with
`AppendSystemCommentMarker(body, issueRef.CardId)`. In `PullCommentsAsync`, add
the D-2 existence check inside the parsed comment-marker block, after a missing
CardComment and before its fail-open comment. A recognized legacy marker must
exit that iteration before external-ID dedup and insertion, without changing
any existing row. Existing duplicate External rows consequently remain intact.

Add the tests specified below in the existing integration class, reusing its
database fixture and `FakeBidirectionalTracker`. Keep `[Category("Integration")]`
and the existing unkeyed `[NotInParallel]`. Assertions and cleanup must target
the fixture's card/board IDs. Do not boot a real Program host, contact GitHub,
or use the production runner. Both production edits ship together; a change to
the writer alone leaves old GitHub comments vulnerable to import.

### S-2: document compatibility and report verification evidence

Files:

- `docs/workflow-tracker-block.md`
- this plan, for Code's measured verification evidence if required by TestDesign

Add a short comment-identity subsection to the workflow owner: discussion
markers link stored comments; generated state/content-edit comments use the
same-card system marker; old content-edit revision markers require same-card
ContentEdit evidence; unresolved markers stay visible and external-ID dedup
remains the replay fallback. Explain that deployment does not clean historical
duplicates. Keep the trigger, notification and field-authority contracts intact.

There is no client, adapter, scheduler, schema, notification formatter, live
configuration, or data-migration change in S-1/S-2. Partial-post/crash retry
semantics of `LastRevisionSynced` are outside this bounded inbound-echo fix;
the system marker is not a promise of exactly-once outbound posting.

## Regression requirements for TestDesign

Proposed method names below are new unless explicitly called existing. Verify
through public `RunAsync`, real test Postgres persistence, counters, itemized
changes and captured fake writes; parser-only tests cannot establish this fix.

| Requirement / proposed test | Fixture and required observations |
|---|---|
| `Content_edit_outbound_echo_and_repeated_pull_create_no_discussion_rows` | Seed an import-origin issue with a locally authored ContentEdit newer than LastRevisionSynced and matching card/issue content to avoid unrelated read-side revisions. Enable `EchoPostedComments`. First run: exactly one captured content-edit post, `CommentsOut=1`, one CommentOut change, `CommentsIn=0`, and a persisted advanced LastRevisionSynced. Assert the captured body parses as a system marker equal to CardId and does not end in a discussion marker; this catches an outbound-format regression even when legacy fallback masks its symptom. Feed the exact captured body back on both the second and third runs. Each must have `CommentsIn=0`, no CommentIn change, zero new CardComments, and no further posts; after clearing write counters, steady state has zero writes. |
| `Legacy_content_edit_revision_marker_is_ignored_on_repeated_pulls` | Seed a same-card ContentEdit and advance the outbound revision cursor so no new post distracts from inbound compatibility. Supply an old-format trailing marker containing that revision ID, a new remote comment ID, and no CardComment with the marker ID. Return it in two successive pulls. Both runs: zero inserted discussion rows, CommentsIn and CommentIn changes, with no repost. Require a success result without board Error. Use a fresh DbContext/SUT on a later pass to prove persisted state, not an accidental tracked entity, governs the round trip. |
| `Legacy_marker_requires_same_card_and_ContentEdit_kind` | Supply distinct remote IDs for another card's ContentEdit marker, a same-card Move/Reopen marker, and an unknown GUID. Each imports exactly once to the issue's own card, with original body/author/external ID/URL and one CommentIn change. Replaying imports zero more rows. Keep the foreign revision owner outside the target issue's pending outbound work. Removing either the CardId or Kind predicate must fail the relevant case. |
| `Same_author_human_comment_imports_beside_content_edit_echo` | In one pull include a valid new system echo, a valid legacy echo, and an ordinary unmarked human comment, all authored `michal-ciechan`, with distinct remote IDs. Only the human comment imports: one External row, CommentsIn=1, one CommentIn change, exact body/author/ID/URL preserved. Next pull returns all three again: CommentsIn=0, the one human row persists, and no comment is posted outbound. |
| `Unrecognized_marker_shapes_remain_visible_and_deduplicate` | Cover a wrong-card system marker, a malformed revision marker, and marker-looking text followed by genuine prose. They must import once and deduplicate by external ID on replay. Do not alter existing marker syntax/anchoring to satisfy a test. |
| `Existing_external_revision_echo_is_left_unchanged` | Seed an External duplicate row with its legacy body and remote ID, plus the matching same-card ContentEdit. Pull it again: no new row or CommentIn change, and all original persisted comment fields remain unchanged. This pins D-4's prevention-only scope. |

Retain and run these existing coverage points in the same class:

- `Loop_pin_b_Antiphon_comment_out_echo_stamps_ExternalCommentId_with_zero_new_rows`
- `Terminal_close_and_reopen_comment_echoes_create_zero_External_CardComments`
- `Comments_IN_land_as_External_CardComments_and_loop_pin_a_holds`
- `Same_pass_content_edit_comment_does_not_swallow_out_reopen`
- `External_tracker_content_edit_is_not_echoed_as_a_GitHub_comment`

The shared fake currently returns every `CommentsSince` entry regardless of
`since`, and appends posted bodies when echo mode is enabled. Keep the echo
present for both replay passes so a changed watermark cannot make a false pass.
Its generated author is `sync-bot`; for the same-author test, construct inbound
records explicitly with `michal-ciechan`, retaining the actual posted body where
testing the new writer. Seed stable labels/state so unrelated writes do not
weaken the zero-write assertion. Use fresh readback contexts for persisted row
and cursor assertions, and advance the fake clock between passes.

TestDesign must specify positive controls for restoring the old outbound
marker (the format assertion must fail even with legacy support), removing
legacy recognition, dropping each card/kind guard separately, and adding the
prohibited same-author exclusion. Require expected assertion failures, restore
each temporary change, then demonstrate green. Also ensure the external-ID
replay case would fail if dedup were removed; a caught save failure is not a
successful sync, so assert Error is null as well as counts.

Use the current testing owner's scoped TUnit command, from the eventual Code
worktree, with fresh TRX files for final and control runs:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c436/ -- --treenode-filter "/*/Antiphon.Tests.Application/TrackerBidirectionalSyncTests/*" --report-trx --report-trx-filename card-0436-bisync.trx
```

TestDesign should state the final method filters for each control and estimate
the verification floor. Code must report actual executed/passed/failed/skipped
counts and method names from fresh execution TRX evidence; exit zero or
`--list-tests` alone is insufficient. This focused service change does not
require a client/E2E or namespace-wide run. Follow the owner for isolated build
output cleanup and failure triage; never run the Pty test assembly concurrently.

## Existing duplicate rows: operator options

The investigation's 43 is a point-in-time count, not an approved deletion set.
Choose the eventual treatment separately from proceeding with S-1/S-2:

| Option | Effect and tradeoff |
|---|---|
| A. Retain the existing rows. | No data operation; preserves the complete stored discussion and external-ID links, with 43 known duplicate entries still visible. The deployed fix prevents further imports of recognizable echoes. |
| B. Export and remove only confirmed local duplicates after deployment. | Removes discussion clutter. Requires an approved exact row manifest, full backup of those rows, and a transactional operator repair. Public GitHub comments and CardRevisions remain untouched. Recovery restores the archived rows with their original IDs. |
| C. Add reversible hiding/quarantine as separate work. | Retains rows and dedup identities in the database while hiding them from normal discussion. Requires a schema/read-model/UI design because no such facility exists today; it is larger than this fix. |

No option is selected by this plan. The caller should obtain and record the
operator's choice on CARD-0436 using the normal decision/revision path, not an
alert sink or a new board column. Pending that choice, leave the live rows as
they are and continue prevention work.

If B is selected, the subsequent operator repair must satisfy this sequence:

1. Deploy the tested marker fix and directly verify the loaded code/version
   from the canonical checkout. Keep revisions and sync cursors intact. An
   old server can recreate deleted echoes if those comments are fetched again.
2. Produce a read-only candidate manifest scoped to the resolved Antiphon
   board and repository. Select External rows with non-null ExternalCommentId,
   a valid trailing legacy marker, and a matching same-card ContentEdit
   revision. Include row/card/board IDs, card identifier, repository/issue key,
   external comment ID/URL, marker/revision ID, author, timestamps and body/hash.
   Use the same marker parser semantics as runtime. Never select by author,
   date, count, or generated-text prefix alone.
3. Review every candidate against the generated-comment evidence and issue
   linkage; exclude edited/ambiguous human content. Reconcile the 33 September 7
   and 10 older entries with a fresh census. Any added/missing candidate changes
   the manifest and must be surfaced; never broaden a delete to reach 43.
   Export complete original CardComment fields to a private operator artifact,
   outside tracked docs; do not export revision PrivateNotes or credentials.
4. Present that exact manifest and restoration procedure for operator approval.
   Only then delete those explicit row IDs in one transaction, rechecking
   identity, External origin, body/hash and revision/card match under suitable
   row locking. Abort on unexpected changes or count mismatch. Do not rewrite
   Origin, null out external IDs, delete revisions, edit GitHub comments, or
   rewind pull/outbound cursors. Make reruns explicit no-ops for already-removed
   manifest IDs, with no expansion to newly discovered rows.
5. Verify the committed rows/count against the manifest, preserve unrelated
   discussion and revisions, and refresh the affected discussion view. Observe
   subsequent normal syncs for recurrence by marker/ID, not by a globally zero
   CommentsIn count: legitimate new human comments can still arrive. A fixture
   replay of archived bodies must remain ignored without the deleted local
   rows. Historical notification/counter records are not retroactively changed.

If rollback to the buggy server is required after B, account for renewed
re-import risk before resuming its syncs; the exported rows provide a restoration
path but the prevention code is what recognizes legacy artifacts without them.
Cleanup scripting/execution is not authorized by the current Plan brief and is
not part of S-1/S-2.

## Handoff and completion

Land this plan through `delegate.ps1 -Land 9f6882c4` before creating the next
worktree, then dispatch TestDesign to append the executable verification
section. Code implements S-1/S-2 and reports every verification/control result.
The existing-row decision can be resolved independently; it does not block
TestDesign or Code. Landing and any later canonical-stack deployment belong to
the caller's subsequent stages. This Plan task runs no sync, tests/builds,
database repair, restart or GitHub issue/comment write.

## Verification design

TestDesign, 2026-09-07, against `a5171a45`. This checkout contains the plan as
`89c037af`; its plan file is byte-for-byte equivalent under `git diff` to the
brief's pre-landing commit `e9ab7daa`. D-1 through D-5 and S-1/S-2 remain unchanged.
This section specifies executable Code-stage work; TestDesign ran no tests or
builds and claims no runtime result.

### Proves it works now

All new methods below belong to
`tests/Antiphon.Tests/Application/TrackerBidirectionalSyncTests.cs`. Use real
Postgres through `TestDbFixture`, public `RunAsync(graph.Board.Id, ct)`, the
existing `NewSut`/`SeedLinkedBoardAsync`/`CleanupAsync`, and
`FakeBidirectionalTracker`. Keep `[Category("Integration")]` and the unkeyed
`[NotInParallel]`. Seven new, non-parameterized `[Test]` methods are specified;
matrix rows are assertions within their named method. No new host or fake is
needed. `TrackerSyncMarkers` calls in tests supplement, rather than replace,
the integration observations.

Fixture contract for all seven methods:

- Start at `2026-09-07T12:00:00Z` with `FakeTimeProvider`. Save the graph before
  constructing the SUT. Set card title/description to `Title`/`Body`, labels to
  `[]`, importance to `Normal` with `Auto` provenance, and Backlog/open state.
  Set `Candidates` to `Issue("acme/app#1", "open", "Title", "Body",
  ["status:backlog"])`. This prevents the default `NewCard` text from creating
  an unrelated external-tracker revision and prevents label writes. Keep
  `sync_out_create: false` and the default import origin unless stated otherwise.
- Add each revision with a fresh ID, `CardId`/`Card` set, a distinct incremented
  `RevisionNumber`/`card.RevisionCount`, explicit `Kind`, `EditedBy = "operator"`,
  reason `clarify acceptance`, and UTC `CreatedAt`. For a ContentEdit, snapshot
  `Title = "Previous title"`, `Description = "Previous body"`. Advance the clock
  one minute before each run. No real sleeps are needed: this graph has no queue
  polling or provider-session dependencies.
- For inbound-only cases set `LastRevisionSynced = card.RevisionCount` after
  seeding revisions. Set `LastOutboundSyncedAt` to the seed time, at or after
  `card.UpdatedAt` and any Move/Reopen revision. Do not change these cursors
  between replay runs. Positive legacy cases must have no CardComment whose ID
  equals the marker ID, except the deliberate discussion-precedence case V-7.
- Give every inbound record its own numeric string remote ID, generated from
  `Random.Shared.NextInt64(1_000_000_000_000, long.MaxValue)` and checked distinct
  within the fixture; keep those IDs unchanged across passes. Set
  `IssueExternalId = "acme/app#1"`, a distinct `github.test` URL, UTC created time
  one minute before the seed time, and updated time equal to created time.
  Use `michal-ciechan` in V-4 and V-6; otherwise use `alice`. Never use live IDs.
- Preserve `CommentsSince` across replay passes. The fake ignores `since`, and
  `ClearWriteCounters()` leaves this collection intact. Assert the expected
  remote IDs and exact bodies are still in it before each replay. Fake echoes
  use wall-clock timestamps and author `sync-bot`; neither is an identity guard.
- On **every** run assert one result for the fixture board,
  `ConcurrentRunSkipped == false`, `Error == null`, `Skips` empty, and the
  expected `IssuesPulled == 1`. Assert counters and itemized changes separately.
  Each change must name this card's identifier and external key `#1`. The result
  can return zero counters after a caught exception; counts alone are unsafe.
- Use a fresh `CreateContext()` for each persisted readback, with queries scoped
  to this card (and the explicit foreign card where applicable). Assert exact
  imported IDs and all scalar fields: `Id` stable across replay, `CardId`,
  `Origin = External`, `Body`, `Author`, `ExternalCommentId`, `ExternalUrl`,
  `CreatedAt`, and null `SyncedAt`. Assert `TrackerCommentsPulledAt` equals that
  pass's clock time. For a zero-change pass require `Changes` empty,
  `CommentsIn/CommentsOut/LabelsChanged/StateChanges/Creates/ExternalReopens = 0`
  and `fake.WriteCallCount == 0` after clearing counters before the pass.
- Keep `CleanupAsync(tempRoot)` in `finally`. Foreign cards live under the same
  fixture project so existing cleanup covers them. No global row counts or
  production database/runner access.

| ID | Behavior / layer / executable method | Exact expected outcome |
|---|---|---|
| V-1 | New writer and two subsequent inbound passes; integration; `Content_edit_outbound_echo_and_repeated_pull_create_no_discussion_rows` | Seed one eligible local ContentEdit at revision 1 with cursor 0; enable `EchoPostedComments`, initially empty inbound list. Run 1 has one PostComment to `acme/app#1`, `CommentsOut=1`, `CommentsIn=0`, exactly one `CommentOut` change and no other changes/writes. Assert the exact generated body specified below, system parser succeeds with CardId, comment parser returns false. Fresh readback: no CardComments, revision cursor 1, `LastOutboundSyncedAt` equals pass 1 time. Retain the fake's exact posted record for runs 2 and 3. Both are successful zero-change/zero-write passes with no CardComments; cursor stays 1 and outbound timestamp stays at pass 1. Dispose the original context/SUT after run 1 and use fresh ones for runs 2 and 3 so cursor persistence is necessary. |
| V-2 | Legacy identity survives repeated pulls and ref-origin changes; integration; `Legacy_content_edit_revision_marker_is_ignored_on_repeated_pulls` | Seed local ContentEdit revision 1, cursor 1 and no discussion row. Supply `AppendCommentMarker("Historical content edit", revision.Id)` with a fresh remote ID. First and second runs both have zero changes/writes/rows; use a fresh context/SUT for the second. Then, through a separate context, set the ref origin to `AntiphonExport`, set `LastOutboundSyncedAt` at or after current card.UpdatedAt, save, and run a third pass with another fresh context/SUT and the same inbound record. It is still ignored with no writes/changes/rows. Keep marker CreatedAt older than the advancing pull watermark; revision cursor remains 1. Compatibility depends on persisted revision identity, not current origin, a recent timestamp, pending outbound work, or tracked objects. |
| V-3 | Card, kind and identity guards; integration; `Legacy_marker_requires_same_card_and_ContentEdit_kind` | Seed a target-card ContentEdit witness, target-card Move and Reopen revisions, plus a second card's ContentEdit. Give the foreign card a distinct identifier but no ExternalIssueRef. Advance target cursor past all its revisions. Supply four old-format bodies with distinct remote IDs: foreign ContentEdit ID, same-card Move ID, same-card Reopen ID, and an unknown GUID absent from both tables. First run imports all four to the target card, with exact original fields and four `CommentIn` changes, no writes. Check each remote ID individually before aggregate count assertions; foreign card gets zero comments. Second run with the same four records and a new SUT/context is successful with zero changes/writes and the same four stored IDs. The unused target ContentEdit witness makes removal of the marker-ID predicate observable. |
| V-4 | Same-author human discussion beside both echo formats; integration; `Same_author_human_comment_imports_beside_content_edit_echo` | First produce one real new-format post from an eligible local ContentEdit as in V-1; assert the post and persisted cursor before clearing counters. Construct the inbound list explicitly from that captured body, a legacy marker for the same revision, and an unmarked human body `Antiphon content edit by michal-ciechan: I wrote this comment myself.` All three records have distinct remote IDs and author `michal-ciechan`. First mixed pull imports only the human row: `CommentsIn=1`, exactly one `CommentIn`, no posts or other writes. New SUT/context on replay: successful zero-change/zero-write pass, exactly the same one human row. This also rejects generated-prose-prefix filtering. |
| V-5 | Unknown syntax remains visible; integration with parser preconditions; `Unrecognized_marker_shapes_remain_visible_and_deduplicate` | Use the nine-body matrix below with a same-card ContentEdit and advanced cursor. Assert each parser precondition, then pass the actual body through RunAsync. First run imports precisely seven invalid/unresolved bodies with exact fields and seven `CommentIn` changes, no writes; the two valid controls create no rows. Second run with all nine unchanged records and a fresh SUT/context has zero changes/writes, Error null, and the same seven rows. |
| V-6 | Prevention leaves historical rows untouched; integration; `Existing_external_revision_echo_is_left_unchanged` | Seed an External CardComment with a fresh row ID distinct from the same-card ContentEdit ID, valid legacy body, matching remote ID/URL, author `michal-ciechan`, and null SyncedAt. Also seed an unrelated human External row on this card with its own remote ID. Snapshot every scalar field of both through a fresh context. Pull only the legacy record twice, using a fresh SUT/context on replay: zero changes/writes, exactly the same two row IDs and all original scalar fields, and the revision still present. No deletion, reclassification, link stamp or body rewrite. |
| V-7 | Discussion link repair retains precedence; integration; `Known_discussion_marker_repairs_missing_link_before_legacy_lookup` | Seed a same-card Antiphon CardComment with SyncedAt already set and null ExternalCommentId/ExternalUrl, so it cannot post. Deliberately use the same GUID for a persisted same-card ContentEdit (IDs are unique within each table); advance the revision cursor. Pull a comment marker for that ID. Fresh readback: exactly the original Antiphon row, its original body/author/CreatedAt/SyncedAt, and the inbound remote ID/URL now filled. Both first pull and replay have zero counters/changes/writes and no External rows. Repeat with a fresh SUT/context. The existing discussion lookup must win even if the legacy predicate would also match. |
| V-8 | Surrounding behavior remains covered; integration class execution plus static S-2 inspection | Run all existing methods in TrackerBidirectionalSyncTests, including the five named under Regression requirements. Inspect the final diff: external-ID AnyAsync and filtered unique index unchanged; no author/prefix exclusion; compatibility AnyAsync includes ID, card and ContentEdit predicates with ct; existing discussion branch retained before it; system-parser syntax unchanged; workflow owner documents generated and legacy comment identity and pending cleanup. Production scope is S-1/S-2, without row repair or migration. |

V-1's expected body is constructed independently of the production append helper:

```csharp
$"Antiphon content edit by operator: clarify acceptance\n\n"
+ $"**Title:** {graph.Card.Title}\n\n{graph.Card.Description}\n\n"
+ "_The issue body remains authoritative on this import-origin link._\n\n"
+ $"<!-- antiphon:system-comment={graph.Card.Id:N} -->"
```

This exact suffix assertion must execute on pass 1, before any inbound pass.
The PC-1 mutant can still be suppressed by legacy recognition; zero imported
rows alone would not detect the writer defect. Do not regenerate or substitute
an expected body in the fake during V-1's replay.

V-5 matrix: `revId` is the same-card ContentEdit ID, `otherCardId` is an explicit
different card ID, `badHex` is `revId.ToString("N")[..31] + "z"`, and `shortId`
is `revId.ToString("N")[..31]`. Prefix all bodies with `Visible text\n\n`.
Each row has a distinct remote ID. Out GUID values are checked only on success.

| Row | Suffix after the visible text | Parser precondition | IN |
|---|---|---|---|
| wrong-card-system | `<!-- antiphon:system-comment={otherCardId:N} -->` | System true, otherCardId | import |
| nonhex-revision | `<!-- antiphon:comment={badHex} -->` | Comment false | import |
| hyphenated-revision | `<!-- antiphon:comment={revId:D} -->` | Comment false | import |
| short-revision | `<!-- antiphon:comment={shortId} -->` | Comment false | import |
| unclosed-revision | `<!-- antiphon:comment={revId:N}` | Comment false | import |
| nontrailing-revision | `<!-- antiphon:comment={revId:N} -->\nHuman follow-up.` | Comment false | import |
| nontrailing-system | `<!-- antiphon:system-comment={graph.Card.Id:N} -->\nHuman follow-up.` | System false | import |
| valid-legacy-control | `<!-- antiphon:comment={revId.ToString("N").ToUpperInvariant()} -->\r\n \t` | Comment true, revId | ignore |
| valid-system-control | `<!-- antiphon:system-comment={graph.Card.Id:N} -->\r\n \t` | System true, target CardId | ignore |

Use interpolation/escape sequences to create actual bodies, not literal braces
or backslash characters. Valid uppercase N-format and trailing whitespace are
accepted today; hyphenated D-format and non-trailing markers are not. Bad-shape
cases reference a real revision/card wherever possible: otherwise a loosened
parser could still fail the identity lookup and yield a false-green test.

### Guards the regression

- R-1: writing a revision-ID comment marker again | caught by V-1's exact body,
  successful system parse and failed discussion parse before replay; PC-1.
- R-2: omitting legacy recognition, treating only unsynced/recent/import-origin
  revisions as echoes, or keeping recognition only in tracked memory | caught
  by V-2's unchanged legacy record, advanced cursor/watermark, recreated SUTs
  and export-origin third pass; PC-2 exercises the missing recognition path.
- R-3: accepting any revision/valid GUID without matching ID, card and kind |
  caught by V-3's individually preserved foreign, Move, Reopen and unknown
  records; PC-3/PC-4/PC-5 remove each predicate separately.
- R-4: suppressing the PAT author or generated-looking prose | caught by
  V-4's one exact human row beside both recognized echoes; PC-6.
- R-5: losing external-ID replay dedup or hiding its database failure behind
  zero counters | caught by V-3/V-4/V-5's stable IDs, null Error and empty changes
  on replay; PC-7 leaves the unique index active and must fail on Error.
- R-6: accepting a wrong-card system marker, widening accepted GUID format or
  losing trailing anchoring | caught by V-5's matrix and parser preconditions;
  PC-8/PC-9/PC-10/PC-11. Valid controls prevent a reject-all parser from passing.
- R-7: turning prevention into cleanup or overwriting prior fields | caught by
  V-6's full scalar snapshots and stable original IDs, plus V-8 scope inspection.
- R-8: moving compatibility ahead of known discussion-marker link repair |
  caught by V-7's repaired external fields on the original row and no new row;
  PC-12. V-8 retains ordinary discussion, state/reopen, and outbound-order tests.

### Positive controls

After adding the seven tests and both production changes, establish green once.
For each PC below, make only the specified temporary source edit, build and run
the selected method, require its named assertion failure, undo only that edit,
then rebuild/run the same method green. Never combine mutants. Keep final tests
unchanged throughout. A build failure, zero tests, setup failure, or unrelated
assertion does not qualify as the expected red. PC-7's deliberate replay save
error is a service result checked by a test assertion, not a fixture failure.

The method filter for each control is exactly
`/*/Antiphon.Tests.Application/TrackerBidirectionalSyncTests/<method>`, where
`<method>` is the full method name for the referenced V row above (V-8 is not
a method). No wildcard/OR selection is needed for an individual control.

| ID | One temporary edit / guard broken | Method / required red assertion |
|---|---|---|
| PC-1 | In PushContentEditCommentsAsync restore `var marked = TrackerSyncMarkers.AppendCommentMarker(body, edit.Id);` while retaining legacy support. | V-1: captured pass-1 body differs from the required system-marker body. Record this failure, not a later row count. |
| PC-2 | Add `&& false` to the new legacy AnyAsync predicate so it cannot recognize a revision. | V-2: first pull imports the legacy comment, violating CommentsIn=0 / zero rows. |
| PC-3 | Remove only `&& r.CardId == issueRef.CardId` from that predicate. | V-3: foreign ContentEdit remote ID has no imported row (three imports instead of four). |
| PC-4 | Remove only `&& r.Kind == CardRevisionKind.ContentEdit`. | V-3: Move and Reopen remote IDs have no imported rows (two imports instead of four). |
| PC-5 | Replace only `r.Id == markerId` with `true`. | V-3: unknown GUID is suppressed by the seeded same-card ContentEdit witness; its required row is missing (all four may be suppressed). |
| PC-6 | After resolving issueRef in PullCommentsAsync insert `if (comment.Author == "michal-ciechan") continue;`. | V-4: first mixed pull imports zero instead of the one human row. |
| PC-7 | Change only the external-ID gate from `if (exists)` to `if (exists && false)`; retain its query and the database index. | V-4: first mixed pull still succeeds, replay attempts the human insert again and returns non-null board Error. `Error.ShouldBeNull()` must fail, even if counters remain zero and the index preserves the one stored row. |
| PC-8 | Remove only `&& cardId == issueRef.CardId` from the system-marker branch. | V-5: wrong-card-system row is missing (six imports instead of seven). |
| PC-9 | In TryReadTrailingCommentMarker, after the whitespace-body check, insert `body = Regex.Replace(body, @"(?<=[0-9a-fA-F])-(?=[0-9a-fA-F])", "");`. This deliberately normalizes D-format to N-format without changing HTML delimiters. | V-5: hyphenated-revision parser precondition (false) fails because it now returns true; its real same-card revision would be incorrectly suppressible. |
| PC-10 | Only in TrailingCommentMarkerRegex's GeneratedRegex line, replace final `\s*$` with `\s*`. | V-5: nontrailing-revision false-parser assertion fails; genuine trailing prose is no longer protective. |
| PC-11 | Only in TrailingSystemCommentMarkerRegex's GeneratedRegex line, replace final `\s*$` with `\s*`. | V-5: nontrailing-system false-parser assertion fails. |
| PC-12 | Change the existing discussion branch to `if (origin is not null && false)`. | V-7: the colliding revision is recognized instead, leaving ExternalCommentId/ExternalUrl null on the original Antiphon row; the fresh readback link assertions fail. |

Command from the Code worktree root, with `<method>` expanded as above and
`<n>` replaced by the control number. Change only `red` to `green` after revert:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c436/ -- --treenode-filter "/*/Antiphon.Tests.Application/TrackerBidirectionalSyncTests/<method>" --report-trx --report-trx-filename card-0436-PC-<n>-red.trx
```

Run the class once green before controls with filename
`card-0436-baseline.trx`, and after all controls with the final filename below:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c436/ -- --treenode-filter "/*/Antiphon.Tests.Application/TrackerBidirectionalSyncTests/*" --report-trx --report-trx-filename card-0436-bisync.trx
git diff --check
```

Evidence contract: each invocation needs a new TRX created by that invocation.
If retrying, use an attempt suffix rather than reusing stale evidence. Locate
the output under this worktree and report its absolute path; inspect execution
`UnitTestResult` entries, `TestDefinitions` method names and `ResultSummary`
counters. Expected at this base: **30 executed/passed, zero failed/skipped** for
each class green (23 existing + 7 new); each PC red selects **one executed,
one failed**, and each reverted green selects **one executed, one passed**.
If upstream changes alter the class census, reconcile actual method names and
counts explicitly, retaining all seven new methods and the existing five
required regressions. Neither `--list-tests` nor exit zero is execution evidence.

Code's report maps V-1..V-8, R-1..R-8 and PC-1..PC-12 to actual evidence. Each
PC row records the edit, expected observed assertion, reverted line and green
result, command/filter, counts and TRX path. Record actual elapsed time and
final commit. Before committing, inspect the diff to confirm all mutants are
gone, including temporary parser edits; only S-1/S-2 and their tests/docs remain.
Preserve evidence outside disposable build directories. Once test processes
have exited, remove only this worktree's `bin-c436` directories using the
testing owner's cleanup procedure, first resolving each absolute target and
verifying it is below the worktree root. Do not remove another checkout's output.

### Out of scope

- No cleanup choice is required for Code: V-6 pins the interim prevention-only
  behavior. The live 43-row census, export/repair/hiding, public GitHub edits and
  deployment validation belong to the separate operator decision/procedure.
- No live sync, GitHub HTTP call, notification send, AppHost restart, browser,
  runner or provider session is needed. Fake captured bodies plus real Postgres
  establish this service's marker/replay contract without external writes.
- No client/E2E, namespace-wide or full-assembly run: the implementation is
  confined to one service and its integration class. Existing GitHub adapter
  transport, scheduler, notification delivery and field-authority behavior are
  not changed by this fix.
- No exactly-once outbound/crash-retry claim, marker authentication claim,
  comment edits/deletions or schema/index redesign. The guards recognize
  existing structural identities, as specified by D-1/D-2.

### Cost

- Suites forced: `Antiphon.Tests`, only TrackerBidirectionalSyncTests, with the
  two class greens and 12 separate red/revert/green method pairs above. At this
  base that is 26 invocations, 84 executed test cases in aggregate: 72 expected
  passes and 12 intentional assertion failures. These are expected counts,
  not TestDesign execution results. No concurrent Pty assembly run.
- Verification floor estimate: **about 30 minutes with warm caches**, allowing
  repeated C# builds, Postgres startup/migration, controls and evidence review;
  budget 45-60 minutes with cold caches. There are no deliberate wall-clock
  waits in the new tests. Follow testing-and-build.md for slow-build or fixture
  triage; do not weaken assertions or widen the suite to compensate.
- Readiness: executable design complete. Land this documentation task before
  dispatching Code from the updated target, then implement S-1/S-2 and execute
  every V/R/PC item. The existing-row operator choice remains independent.

## Code verification evidence (2026-09-09)

[Code report](2026-09-09-card-0436-code-report.md) maps all V/R/PC entries to fresh
execution evidence for code/test commit `5619b3b8`. The final class passed 30/30;
all 12 controls failed at their expected assertion and passed after restoration.
The required 26 invocations executed 84 cases (72 passes and 12 intentional
failures), with zero skips or unexpected failures. Evidence is preserved at
`C:\Antiphon\evidence\card-0436-bcb6899f`. Historical rows were untouched.
Automatic approval review blocked removal of the 14 task-owned `bin-c436`
directories; their exact inventory and cleanup limitation are in the report.
