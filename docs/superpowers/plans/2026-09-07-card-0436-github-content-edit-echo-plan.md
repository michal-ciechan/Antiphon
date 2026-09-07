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
