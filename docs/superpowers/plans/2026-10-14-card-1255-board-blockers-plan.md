# CARD-1255 board blocker summaries

Original Code / landing owner: `dc2740ed-3d7c-42a1-9f45-359b810a068d`.
Task base: `a02389cab727d508532e6d3db8ff6cbcf3eb7366`.
Round: Final. Restart: server (activation by caller after Review/landing).

The card is the implementation brief; this task-local plan freezes its test design and closed
checkpoint list. The explicit Final profile and stage contract require the whole Unit lane,
despite the earlier contradictory "no whole-Unit lane" sentence in the brief. No full assembly
run is planned. Delivery, landing, leases and persistence outside board/card reads are unchanged.

## Design and slices

S1: tests first. Extend CardRelationshipApiTests with full and summary HTTP board reads,
literal blocker tuples, Done/Canceled/archived/unknown state coverage, cross-board blockers,
live resolution/reopen, omitted fields without relationships, and a command interceptor
asserting exactly one relationship query for both 1 and 40 blocked cards. Add BoardPage tests
for both real routes: `/boards` renders BoardCard through BoardColumn; `/boards/:id` renders
CardRow through BoardShapeView/SortableCardList. Both use `GET /api/boards/{id}?view=summary`
via useAllBoardDetails/useBoard. The board selector uses `/api/boards` summaries without cards;
no other card-list endpoint feeds these surfaces. Existing `/api/cards` is already enriched.

S2: extract the existing batched summary loader and DTO enrichment into one shared application
helper used by CardService and BoardService. Keep CardBlockers.IsResolved as the only resolution
predicate. Load summaries once for the board's returned card IDs before summary conversion or
session-context enrichment. Keep resolved summaries, omit zero counts and absent summaries.
No schema, client production, dispatch, delivery or lease changes.

## Ordinary verification

| ID | Witness and literal outcome |
|---|---|
| V-1 | `Board_views_return_blockers_using_live_resolution_state` (full/summary): count 2 and five exact tuples, including cross-board Backlog and local InProgress; after resolution count 0 with five resolved entries; reopen count 1. |
| V-2 | `Board_without_relationships_omits_blocker_fields` (full/summary): neither JSON blocker member exists; title/status retained. |
| V-3 | `Board_loads_all_blocker_summaries_in_one_query` (1/40 cards, full/summary): exactly one relationship SQL command; every blocked card count 1 and literal CARD-0001 / status 999 / resolved false summary, inverse-only blocker has no summary. |
| V-4 | BoardPage test `shows the Blocked chip from the board summary response on %s` (both routes): real query requests summary, visible Blocked tooltip names only CARD-0049; resolved-only peer has no chip. |
| R-1 | Entire CardRelationshipApiTests protects card/list summaries and relationship writes. |
| R-2 | Entire BoardServiceIntegrationTests, BoardProjectArchiveApiTests and CardCorrectionApiTests protect board reads, card moves and archived visibility. |
| R-3 | Whole Unit lane, including existing DTO/predicate/summary guards. |
| R-4 | Entire BoardPage, BoardCard and CardRow Vitest files. |
| V-5 | Manual source trace of board query consumers and single shared predicate/helper, plus fresh TRX roster/count inspection, both receipt validators, full-range evidence diff, remote ref and merge-tree checks. No live deployment required. |

Expected red: S1 V-1 count is 0 rather than 2 (two cases), V-3 relationship commands are 0
rather than 1 (four cases). V-2 and client regressions preserve already-correct behavior;
ordinary red-first does not discharge Mutation controls. No deliberate mutants in Code.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1255-red/` | board-red | `/*/*/CardRelationshipApiTests/Board_*` | V-1, V-2, V-3 | 8 executed; expected 6 red before fix | 8 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1255-final/` | relationships | `/*/*/CardRelationshipApiTests/*` | V-1, V-2, V-3, R-1 | all methods, 0 failed/skipped | 29 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S2 | CP-2 | boards | `/*/*/(BoardServiceIntegrationTests*)\|(BoardProjectArchiveApiTests*)\|(CardCorrectionApiTests*)/*` | R-2 | all three full classes, 0 failed/skipped | 23 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S2 | CP-2 | unit | `/*/*/*/*[Category=Unit]` | R-3 | whole Unit lane; nonzero counts, 0 failed/skipped | 1 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S2 | n/a | client-board | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/board/BoardPage.test.tsx src/features/board/BoardCard.test.tsx src/features/board/CardRow.test.tsx` | V-4, R-4 | all three files, 0 failed/skipped; CLIENT TESTS EXIT CODE: 0 | n/a | 5 | true | n/a |
| CP-6 | S3 | `tests/Antiphon.Tests -> bin-c1255-baseline/` | unit-baseline | `/*/*/*/(Registry_matches_compiled_metadata*)\|(environment_reaches_only_its_row_and_its_reruns*)\|(partial_copy_failure_rolls_back_owned_run*)\|(launch_intent_failure_prevents_native_launch*)\|(preparing_journal_precedes_all_run_io*)\|(definite_native_refusal_rolls_back_owned_run*)\|(post_launch_bookkeeping_failure_keeps_image*)\|(dead_executor_ack_recovers_without_progress*)\|(late_executor_ack_resolves_launch_unknown*)\|(confirmed_dead_local_identity_permits_cleanup*)\|(deleting_root_rejects_registration*)\|(row_green_only_when_rerun_passed_and_nothing_else_failed*)\|(reruns_only_known_flaky_names_once*)\|(rerun_invokes_once_per_class*)\|(rerun_lines_and_reruns_suffix*)\|(unlisted_failure_never_reruns*)\|(copies_the_dll_set_never_the_source_tree*)\|(total_deadline_skips_a_queued_build_without_starting_it*)\|(total_deadline_kills_running_and_skips_queued*)` | R-3 diagnostic | Only observed failed methods at original task base; report actual red/pass outcomes | 19 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

### Execution and cost

Ordinary estimated driver floor 36 minutes including intentional S1 red; authoring about
15 minutes and loaded-host queue time additional, within the 90-minute task budget if slots
admit promptly. Every driver queues once. Tool bootstrap is an explicitly disclosed extra
build through build-slot.ps1 because this fresh worktree has no checkpoint executable.
Run one committed group at a time with exact committed `--expected-source-sha`, `--serial`,
`--row-timeout 40m` and `--total-timeout 150m`; imported manifest `timeouts.rowMinutes: 40`
and per-row 40-minute limits pin the same requested budget. Wait until final non-75 exit.
UseAppHost=false is the Linux default. Never change assertions or deadlines to hide red.
After S2 merge fetched origin/master with a merge commit; never rebase a pushed slice.

## Mutation handoff (unbound, all pending)

PC-1 omit board enrichment: V-1 fails count 2. PC-2 count resolved summaries: V-1 fails count
2/0 (variants Done, Canceled, archived). PC-3 unknown status treated resolved: V-3 fails count 1.
PC-4 per-card query loop: V-3 40-card case fails exact one command (full/summary variants).
PC-5 always emit empty summaries: V-2 fails omitted blocker member (full/summary variants).
PC-6 omit Blocked badge or include resolved tooltip identifiers: V-4 exact route cases fail.
All controls require method-scoped SourceLanding Mutation after Review and caller landing.

S1 fixture correction: initial CP-1 executed 8 (2 pass, 6 fail). Four batching assertions
reached the intended zero-versus-one SQL defect; two HTTP cases returned 400 before the
assertion. HTTP enum serialization disallows unknown numeric values, so unknown-status
coverage is now in the direct-service batching matrix; HTTP retains five literal summaries
with Backlog and InProgress open. No production change or relaxed expectation. The first
zero-test attempt was a documented ledger refusal on npm-created esbuild hard links; both
files were replaced with equivalent independent files before the rerun.

Corrected S1 red proof: run `20261014-173659-6bd9` at
`b37452a917d64e86d9bfcaeea4c2a9e131f2e829`: 8 executed, 2 passed, 6 failed, 0 skipped.
Both HTTP cases now fail precisely at count 0 instead of 2; all four batching cases fail
at 0 relationship commands instead of 1. CP-2 reruns all eight within the full class.
Final execution uses `--keep-outputs` so run-owned compiler outputs remain available for
receipt inspection and targeted diagnostics, then deletes them by literal owned paths.
The default red-run retention is reported as `still-present` by this checkpoint version;
that cleanup status is separate from the clean source and verified build in the red receipt.

Final S2 run `20261014-174622-c17a` at `06df7e14d39e494d525b7f882b2abcd32f791b7c`: CP-2 29/29 green; CP-3 initially zero tests because pinned TUnit requires trailing wildcards in each OR operand. Corrective checkpoint-tool `row --no-build` at the same frozen source ran the documented filter and all 23 cases passed; both validators accepted it. This corrective invocation preceded the manifest amendment to reuse the certified build without changing source under the Unit run. CP-5 had 59 passes and two fixture-order failures: the default MSW handler shadowed the request recorder; recorder now registered in a later server.use call, assertions unchanged. CP-4 reached its unchanged 40-minute deadline, with no final TRX or certifiable counts. R-3 remains incomplete; visible console failures and native Windows skips are not a completed Unit result. CP-6 is the explicitly listed narrow original-base diagnostic for the 19 observed failed methods, not a replacement for the whole Unit lane.
