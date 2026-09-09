# CARD-0436 Code report

Implemented and verified the content-edit echo fix. Ready for Review; alternate-output
cleanup remains blocked by automatic approval review. No deployment or stored-row repair
was performed.

Code/test commit: `5619b3b8` (branch `feat/card-task-bcb6899f`, pushed). The implementation
checkpoint `1cf9487b` was committed and pushed before the first long run. The second commit
orders the guard test's unknown-ID assertion first so PC-5 names the required missing row.
All final controls ran against `5619b3b8` with unchanged tests and one temporary mutation
at a time. This report and the plan's evidence pointer are a subsequent documentation commit.

## Changes and scope inspection (V-8)

- `server/Application/Services/TrackerBidirectionalSyncService.cs`: generated content-edit
  posts use `AppendSystemCommentMarker(body, issueRef.CardId)`. After discussion lookup
  misses, legacy recognition uses one `CardRevisions.AnyAsync` with marker ID, CardId and
  ContentEdit-kind predicates and the cancellation token. A match continues without a row,
  link stamp or inbound change.
- `server/Application/Services/TrackerSyncMarkers.cs`: XML documentation includes content
  edits. Parser implementation, marker formats and regex anchoring are unchanged.
- `tests/Antiphon.Tests/Application/TrackerBidirectionalSyncTests.cs`: seven new integration
  methods with the existing fixture/fake and unkeyed `NotInParallel`. Each pass constructs
  a fresh context/SUT; persisted readbacks use separate contexts. Assertions cover board
  identity, successful result, pull watermark, counters, itemized changes, exact comment
  fields and replay stability. The writer assertion uses an independently constructed body.
- `docs/workflow-tracker-block.md`: documents discussion, generated and legacy identity,
  fail-open imports, deduplication and the pending historical-row decision.

Inspected the final diff after every mutant was restored. Discussion lookup/repair still
precedes legacy recognition; the same-card system branch and external-ID existence query
are unchanged. There is no author/prefix filter, cache, new marker format, migration,
schema/index change, adapter change, scheduler change or historical-row operation.
The existing filtered unique index remains active, demonstrated by PC-7. `git diff --check`
passed. Source/test files matched the committed checkpoint before the final class run.

## Execution evidence

Preserved evidence directory, outside the worktree and disposable build outputs:

`C:\Antiphon\evidence\card-0436-bcb6899f`

`execution-manifest.json` in that directory contains all 29 invocations, absolute TRX
paths, actual executed method names/outcomes/counters, commands, measured elapsed times,
source commits, DLL hashes/timestamps, and the exact mutated/restored text for every PC.
`sha256.json` records evidence-file hashes. Individual TRX, build/test logs, per-run JSON
and the local control driver are also preserved. TRX `UnitTestResult`, `TestDefinitions`
and `ResultSummary` were inspected; all selected methods actually executed.

| Run set | Invocations | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|---:|
| Frozen-test baseline | 1 | 30 | 30 | 0 | 0 |
| Required PC red runs | 12 | 12 | 0 | 12 expected assertions | 0 |
| Required restored greens | 12 | 12 | 12 | 0 | 0 |
| Final class regression | 1 | 30 | 30 | 0 | 0 |
| Required design total | 26 | 84 | 72 | 12 intentional | 0 |
| Preliminary baseline and PC-1 cycle | 3 | 32 | 31 | 1 intentional | 0 |
| Entire Code verification | 29 | 116 | 103 | 13 intentional | 0 |

No unexpected test failures, build failures, fixture failures or zero-test selections.
The preliminary PC-1 wrapper initially expected a fully qualified exception name; the
TRX used `ShouldAssertException`. The expected body mismatch was confirmed, source was
restored, and its method-scoped green passed. After the guard-assertion ordering refinement,
the baseline and PC-1 pair were repeated with fresh attempt-suffixed evidence before the
remaining controls. The complete required sequence therefore uses frozen final tests.

The 26 required invocations took **1,975.62 seconds (32m 55.62s)** including builds and
fixture setup. Baseline: 104.40s; final class: 50.29s. The final class census is exactly
23 existing plus seven new tests, matching TestDesign. All five named surrounding
discussion/state/content-edit regression methods are present and passed in the final TRX.
No client/E2E, full-assembly, Pty assembly, live GitHub, real Program host or production
runner execution was needed or performed.

Baseline TRX: `C:\Antiphon\evidence\card-0436-bcb6899f\card-0436-baseline-attempt2.trx`.
Final TRX: `C:\Antiphon\evidence\card-0436-bcb6899f\card-0436-bisync.trx`.

## Verification and regression map

Every V-1..V-7 method below passed in both the frozen baseline and final class TRX.
Method names also define the exact PC filter suffix in the next table.

| Verification | Executed method / observation | Regression guards |
|---|---|---|
| V-1 | `Content_edit_outbound_echo_and_repeated_pull_create_no_discussion_rows`: exact system-marker post, advanced persisted cursor, original captured echo on two fresh-context replays, zero rows/changes/writes. | R-1; PC-1 |
| V-2 | `Legacy_content_edit_revision_marker_is_ignored_on_repeated_pulls`: old revision marker ignored across advancing watermarks, fresh contexts and export-origin third pass; cursor retained. | R-2; PC-2 |
| V-3 | `Legacy_marker_requires_same_card_and_ContentEdit_kind`: unknown, foreign ContentEdit, same-card Move and Reopen bodies all import with original fields, then deduplicate with stable row IDs. | R-3, R-5; PC-3/4/5 |
| V-4 | `Same_author_human_comment_imports_beside_content_edit_echo`: actual posted new echo and legacy echo ignored beside the one human comment by the same account; replay succeeds without writes. | R-4, R-5; PC-6/7 |
| V-5 | `Unrecognized_marker_shapes_remain_visible_and_deduplicate`: nine parser-precondition cases; precisely seven visible imports and two ignored valid controls; replay has no changes or error. | R-5, R-6; PC-8/9/10/11 |
| V-6 | `Existing_external_revision_echo_is_left_unchanged`: legacy External row and unrelated human row retain every scalar field and row ID on both pulls; revision remains. | R-7 |
| V-7 | `Known_discussion_marker_repairs_missing_link_before_legacy_lookup`: colliding revision/discussion IDs preserve original Antiphon row and repair only missing external ID/URL; replay is unchanged. | R-8; PC-12 |
| V-8 | All 30 methods executed; final diff inspection above confirms the bounded S-1/S-2 scope and preserved surrounding contracts. | R-1..R-8; existing regressions |

## Positive controls

Every row below selected **one executed test**. Every red had **0 passed / 1 failed /
0 skipped** at the named assertion; every restored green had **1 passed / 0 failed /
0 skipped**. Restore means writing the original source bytes with a fresh timestamp.
For all 12 pairs, the green test-output server DLL had a different SHA-256 from the
mutant DLL and a timestamp at or after source restoration. All mutants were removed.

The exact command for each row substitutes its V-method from the preceding table and
the TRX filename in its links below; the green command changes only that filename:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c436/ -- --treenode-filter "/*/Antiphon.Tests.Application/TrackerBidirectionalSyncTests/<method>" --report-trx --report-trx-filename <filename>
```

| PC / filter method | Temporary edit; observed red assertion; restored fixed text | Red / green seconds | Absolute TRX paths |
|---|---|---:|---|
| PC-1 / V-1 | Restore old outbound `AppendCommentMarker(body, edit.Id)`; `post.Body` differs at marker suffix. Restored `AppendSystemCommentMarker(body, issueRef.CardId)`. | 60.84 / 79.10 | [red](C:/Antiphon/evidence/card-0436-bcb6899f/card-0436-PC-1-red-attempt2.trx), [green](C:/Antiphon/evidence/card-0436-bcb6899f/card-0436-PC-1-green-attempt2.trx) |
| PC-2 / V-2 | Append `&& false` to legacy predicate; `CommentsIn` is 1 instead of 0. Removed only `&& false`, restoring recognition. | 63.11 / 76.93 | [red](C:/Antiphon/evidence/card-0436-bcb6899f/card-0436-PC-2-red.trx), [green](C:/Antiphon/evidence/card-0436-bcb6899f/card-0436-PC-2-green.trx) |
| PC-3 / V-3 | Remove card predicate; foreign ContentEdit remote row absent (`matches.Length` 0 instead of 1). Restored `&& r.CardId == issueRef.CardId`. | 97.47 / 104.65 | [red](C:/Antiphon/evidence/card-0436-bcb6899f/card-0436-PC-3-red.trx), [green](C:/Antiphon/evidence/card-0436-bcb6899f/card-0436-PC-3-green.trx) |
| PC-4 / V-3 | Remove kind predicate; Move remote row absent (`matches.Length` 0 instead of 1). Restored `&& r.Kind == CardRevisionKind.ContentEdit`. | 76.30 / 71.62 | [red](C:/Antiphon/evidence/card-0436-bcb6899f/card-0436-PC-4-red.trx), [green](C:/Antiphon/evidence/card-0436-bcb6899f/card-0436-PC-4-green.trx) |
| PC-5 / V-3 | Replace marker-ID predicate with `true`; unknown GUID row absent (`matches.Length` 0 instead of 1). Restored `r.Id == markerId`. | 68.66 / 62.99 | [red](C:/Antiphon/evidence/card-0436-bcb6899f/card-0436-PC-5-red.trx), [green](C:/Antiphon/evidence/card-0436-bcb6899f/card-0436-PC-5-green.trx) |
| PC-6 / V-4 | Skip `michal-ciechan` after issue resolution; human `CommentsIn` is 0 instead of 1. Removed the author exclusion completely. | 52.33 / 57.85 | [red](C:/Antiphon/evidence/card-0436-bcb6899f/card-0436-PC-6-red.trx), [green](C:/Antiphon/evidence/card-0436-bcb6899f/card-0436-PC-6-green.trx) |
| PC-7 / V-4 | Use `if (exists && false)`; replay returns a save error and `result.Error.ShouldBeNull()` fails. Restored `if (exists)`; query/index retained throughout. | 57.84 / 54.66 | [red](C:/Antiphon/evidence/card-0436-bcb6899f/card-0436-PC-7-red.trx), [green](C:/Antiphon/evidence/card-0436-bcb6899f/card-0436-PC-7-green.trx) |
| PC-8 / V-5 | Remove system-marker card predicate; wrong-card-system remote row absent. Restored `&& cardId == issueRef.CardId`. | 72.42 / 78.90 | [red](C:/Antiphon/evidence/card-0436-bcb6899f/card-0436-PC-8-red.trx), [green](C:/Antiphon/evidence/card-0436-bcb6899f/card-0436-PC-8-green.trx) |
| PC-9 / V-5 | Normalize GUID hyphens with the planned `Regex.Replace`; `hyphenated-revision` parser returns true instead of false. Removed normalization; original parser bytes restored. | 95.13 / 140.22 | [red](C:/Antiphon/evidence/card-0436-bcb6899f/card-0436-PC-9-red.trx), [green](C:/Antiphon/evidence/card-0436-bcb6899f/card-0436-PC-9-green.trx) |
| PC-10 / V-5 | Remove discussion regex's final `$`; `nontrailing-revision` parser returns true instead of false. Restored final `\s*$`. | 105.58 / 79.18 | [red](C:/Antiphon/evidence/card-0436-bcb6899f/card-0436-PC-10-red.trx), [green](C:/Antiphon/evidence/card-0436-bcb6899f/card-0436-PC-10-green.trx) |
| PC-11 / V-5 | Remove system regex's final `$`; `nontrailing-system` parser returns true instead of false. Restored final `\s*$`. | 69.05 / 63.37 | [red](C:/Antiphon/evidence/card-0436-bcb6899f/card-0436-PC-11-red.trx), [green](C:/Antiphon/evidence/card-0436-bcb6899f/card-0436-PC-11-green.trx) |
| PC-12 / V-7 | Use `if (origin is not null && false)`; persisted `ExternalCommentId` remains null instead of the inbound ID. Restored `if (origin is not null)` before legacy lookup. | 65.27 / 67.46 | [red](C:/Antiphon/evidence/card-0436-bcb6899f/card-0436-PC-12-red.trx), [green](C:/Antiphon/evidence/card-0436-bcb6899f/card-0436-PC-12-green.trx) |

Final regression command (use a fresh filename when rerunning):

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c436/ -- --treenode-filter "/*/Antiphon.Tests.Application/TrackerBidirectionalSyncTests/*" --report-trx --report-trx-filename card-0436-review.trx
git diff --check
```

## Handoff and retained outputs

Review D-1..D-4 and the V/R/PC evidence, then use the normal landing workflow. Existing
historical External rows remain an independent operator decision; no cleanup choice is
needed to review or land this prevention fix. Deployment/loaded-code verification has
not been performed and remains a later operation.

All test subprocesses were awaited and exited. Fourteen `bin-c436` directories were
inventoried as newly produced by this task; their resolved absolute paths are beneath
`C:\Antiphon\worktrees\card-task-bcb6899f`, with no reparse-point targets and no running
executable found under that worktree. The exact inventory is preserved in
`C:\Antiphon\evidence\card-0436-bcb6899f\owned-outputs.json`.

Automatic approval review rejected both the combined guarded cleanup command and a
narrower `Remove-Item -LiteralPath <14 verified absolute paths> -Recurse -Force` command
with **"blocked by policy"**, without a more specific reason. Nothing was deleted; those
14 build directories remain. `cleanup-receipt.json` records this limitation. Cleanup is
the only requested operation left incomplete; no source or test fix is pending.
