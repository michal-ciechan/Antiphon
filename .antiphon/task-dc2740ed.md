# CARD-1255 Code evidence: implementation complete, Final verification incomplete

Board full/summary responses now carry batched blocker summaries and open counts through the same CardBlockers predicate used by card reads. Affected integration tests passed 52/52; client files passed 61/61. Whole Unit timed out at its unchanged 40-minute limit without final TRX. All 19 observed failed methods reproduce at the original base. Final cannot be called green; next Code must complete the Unit lane before Review. No deliberate Mutation was run.

Original Code / landing owner: dc2740ed-3d7c-42a1-9f45-359b810a068d.
Branch: feat/card-task-dc2740ed.
Worktree: /work/worktrees/task-dc2740ed.
Base: a02389cab727d508532e6d3db8ff6cbcf3eb7366.
Implementation merge SHA: 06df7e14d39e494d525b7f882b2abcd32f791b7c.
Client repair tested SHA: 85a2aa10521d770d0c209ebcfdd911abd93bbeef.
Closing merge SHA: 3f4a66c02ba4fd5d2d20b142b7d9bb3880d111e7; merged fetched origin/master d74b595de6444b9b59fce7ac91819e1432326ce4 without conflicts. Closing merge adds upstream docs and CommitOnSettlePolicyTests fixture changes; no claim that this later HEAD ran the tests. Final report commit SHA and remote equality appear in the caller report.
Plan: docs/superpowers/plans/2026-10-14-card-1255-board-blockers-plan.md.
Unbound candidate: docs/superpowers/mutations/2026-10-14-card-1255-board-blockers-candidate.md.
Restart: server after Review and caller landing; runner none. No restart/deploy performed.

## Changes and manual trace

CardBlockerSummaries extracts CardService's existing batched relationship loader and attachment. BoardService loads once for all returned card IDs, before summary conversion/session context. CardService shares the helper. Empty ID sets return immediately. CardBlockers.IsResolved remains the sole resolution predicate; related status 999 remains open. Resolved summaries remain present; zero count and absent summaries remain omitted. No schema or client production changes.

The two consumer routes use GET /api/boards/{id}?view=summary: /boards through useAllBoardDetails -> BoardColumn -> BoardCard, and /boards/:id through useBoard -> BoardShapeView -> SortableCardList -> CardRow. The selector's GET /api/boards contains no card list. API docs and DTO comments now describe enrichment. Eight new backend cases cover five exact blocker tuples, live changes, cross-board blocking, raw JSON omission, and exactly one SQL query for 1/40 cards/full/summary. Two client cases use the actual query and MSW, assert summary request, visible Blocked by CARD-0049 and absence of resolved CARD-0048.

## Ordinary outcomes

| ID | Actual outcome |
|---|---|
| V-1 | Passed two full/summary live-state HTTP cases in CP-2. Before production fix both failed count 0 versus literal 2. |
| V-2 | Passed two full/summary raw JSON omission cases in CP-1 and CP-2. |
| V-3 | Passed four 1/40-card/full/summary SQL batching cases in CP-2; each originally failed 0 queries versus literal 1. Unknown status 999 remains unresolved. |
| V-4 | Passed both actual-route client cases in repaired CP-5. |
| V-5 | Source trace, fresh TRX roster/count inspection and selected green receipt validators complete. Full-range evidence diff, remote equality and closing merge-tree checks recorded in final handoff. |
| R-1 | Entire CardRelationshipApiTests: 29 passed, 0 failed/skipped. |
| R-2 | BoardServiceIntegrationTests 9; BoardProjectArchiveApiTests 2; CardCorrectionApiTests 12: all 23 passed, 0 failed/skipped. |
| R-3 | INCOMPLETE: whole Unit CP-4 timed out at 40m, no final TRX/counts. Baseline CP-6 ran exactly 19 observed failed methods: 0 passed, 19 failed, 0 skipped. Those failures predate this task; this diagnostic does not replace the whole lane. |
| R-4 | BoardPage, BoardCard, CardRow: 61 passed, 0 failed/skipped, three files passed. |

No ordinary ID is silently deferred as passed. R-3 needs completion in the continuing Final round. No full assembly was run. Delivery/landing/lease persistence are not changed by this board-read fix; no unbounded assembly impact claimed. The explicit Final profile overrides the earlier contradictory no-whole-Unit sentence in the brief.

## Counts, provenance and validators

CP-1 attempts: first 0 tests due npm esbuild hardlink ledger refusal; second 8 executed/2 passed/6 failed (two HTTP fixture errors plus four intended SQL reds); corrected fixture third 8/2/6/0 with all six failures at guarded defect assertions. Corrected CP-1 source b37452a917d64e86d9bfcaeea4c2a9e131f2e829 was clean/build verified. Both validators reject red rows, as expected; first red receipts additionally reported cleanup_refused from retained outputs.

CP-2 29/29 and corrected CP-3 23/23 both tested clean 06df7e14d39e494d525b7f882b2abcd32f791b7c with verified build provenance. Both checkpoint-tool validate and scripts/validate-checkpoint-receipt.ps1 accepted their selected rows. Initial CP-3 had zero tests because TUnit OR operands require trailing wildcards; corrected tool row reused the same certified build at the same frozen SHA. The plan records this corrective invocation. No source was edited under that run.

Initial CP-5 at 06df had 59 passed/2 failed from MSW recorder handler order. Moving the recorder registration to a later server.use fixed the fixture without weakening assertions. Repaired CP-5 at clean 85a2 passed 61/61; buildSource=notApplicable. Both validators accepted selected CP-5. Its terminal wait exit 2 reported receipt-source-mismatching against an unused old bin-c1255-red build stamp included by the full manifest; the actual selected command exited 0 and both row-scoped validators accepted. No receipts/stamps were edited.

Whole final run validators reject row_failed (CP-3 zero-tests, CP-4 timeout, initial CP-5 fixture failure). Baseline CP-6 both validators reject row_failed, while its source receipt is clean and buildSource=verified. Baseline failures were read individually from fresh TRX and match the observed methods/assertions in CP-4 console. Windows-only skips observed in incomplete CP-4 are not a certified final count.

All checkpoint driver slots granted, waited=0s except initial CP-1 waited=120s. Bootstrap build used build-slot.ps1, granted waited=165s, held=38s, 0 errors/two existing warnings. This extra build produced the checkpoint executable; no other unlisted compilation. Corrective CP-3 was explicitly disclosed to repair a zero-test filter. CP-6 was added to the closed list for original-base diagnosis. No unchanged selection exceeded three normal attempts; no loaded repeats or flake exception. No assertion, deadline or retry policy was loosened.

## Disclosures separate from product findings

npm ci created an esbuild hardlink pair that the output ledger refused; replacing the task-created file with an equivalent independent copy removed that artifact. Unknown numeric enum HTTP input returned 400, so unknown-status coverage moved to direct-service SQL cases. Checkpoint tool shadow cleanup reported output-memory-authority-missing; run-owned alternate outputs are removed manually by literal path after validators. Raw receipts/TRX/logs stay ignored. The tool's cleanup/provenance refusals and inherited Unit failures are not board blocker defects.

Runner defaults and session-runners APIs were read before placement; no runner/platform pin was embedded. No production traffic, agent dispatch, delivery, landing or broker change was performed. All test executors were awaited to terminal status; no concurrent Pty assembly run.

## Mutation pending

PC-1 omit enrichment full/summary; PC-2 count resolved/remove Done/remove Canceled/remove archived resolution, each full/summary; PC-3 unknown resolves for 1/40 and full/summary; PC-4 N+1 for 40/full/summary; PC-5 empty summaries or serialized zero, each full/summary; PC-6 missing badge or resolved tooltip identifier, each route: ALL PENDING. Exact methods and variants are in the unbound candidate. Mutation owns missing-control discovery and method-scoped red/restore/green after ordinary Review and caller landing of the original Code task. Existing CARD-0100 controls remain pending.

## Continuation

Complete R-3 with bounded Unit selections and fresh committed-SHA receipts; investigate the inherited classification/checkpoint harness failures or hand them to their owners. Keep all present positive controls pending. Do not widen deadlines or weaken assertions. The remaining 90-minute dispatch budget cannot fit completion of the Unit lane after its timeout and required base confirmation. Then commission ordinary Review; caller lands original Code task and commissions SourceLanding Mutation.

## Unedited checkpoint lines

Evidence: .antiphon/checkpoints/20261014-172124-9509/report.md

```text
source: b1938250368ea836f13102ebee6d6b42e0cc5ae2 state=clean buildSource=unknown
CHECKPOINT CP-1 commit=b1938250368ea836f13102ebee6d6b42e0cc5ae2 build=failed filter=/*/*/CardRelationshipApiTests/Board_* executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=120s slot-reason=refused: tampered dirty=unknown source=unknown sourceState=unknown buildSource=unknown
```

Evidence: .antiphon/checkpoints/20261014-172834-0473/report.md

```text
source: b1938250368ea836f13102ebee6d6b42e0cc5ae2 state=clean buildSource=verified
CHECKPOINT CP-1 commit=b1938250368ea836f13102ebee6d6b42e0cc5ae2 build=ok filter=/*/*/CardRelationshipApiTests/Board_* executed=8 passed=2 failed=6 skipped=0 trx=/work/worktrees/task-dc2740ed/.antiphon/checkpoints/20261014-172834-0473/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=b1938250368ea836f13102ebee6d6b42e0cc5ae2 sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=288.4944557s startup=75.1953003s testsWall=10.8179253s teardown=4.3117795s hostWall=90.3250173s
```

Evidence: .antiphon/checkpoints/20261014-173659-6bd9/report.md

```text
source: b37452a917d64e86d9bfcaeea4c2a9e131f2e829 state=clean buildSource=verified
CHECKPOINT CP-1 commit=b37452a917d64e86d9bfcaeea4c2a9e131f2e829 build=ok filter=/*/*/CardRelationshipApiTests/Board_* executed=8 passed=2 failed=6 skipped=0 trx=/work/worktrees/task-dc2740ed/.antiphon/checkpoints/20261014-173659-6bd9/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=b37452a917d64e86d9bfcaeea4c2a9e131f2e829 sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=363.9781312s startup=52.6584553s testsWall=6.0751611s teardown=3.6858191s hostWall=62.4194497s
```

Evidence: .antiphon/checkpoints/20261014-174622-c17a/report.md

```text
source: 06df7e14d39e494d525b7f882b2abcd32f791b7c state=clean buildSource=verified
CHECKPOINT CP-5 commit=06df7e14d39e494d525b7f882b2abcd32f791b7c build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/board/BoardPage.test.tsx src/features/board/BoardCard.test.tsx src/features/board/CardRow.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=06df7e14d39e494d525b7f882b2abcd32f791b7c sourceState=clean buildSource=notApplicable
CHECKPOINT CP-2 commit=06df7e14d39e494d525b7f882b2abcd32f791b7c build=ok filter=/*/*/CardRelationshipApiTests/* executed=29 passed=29 failed=0 skipped=0 trx=/work/worktrees/task-dc2740ed/.antiphon/checkpoints/20261014-174622-c17a/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=06df7e14d39e494d525b7f882b2abcd32f791b7c sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=213.4051569s startup=50.2432609s testsWall=23.1085486s teardown=2.765283s hostWall=76.1170103s
CHECKPOINT CP-3 commit=06df7e14d39e494d525b7f882b2abcd32f791b7c build=reused filter=/*/*/(BoardServiceIntegrationTests)|(BoardProjectArchiveApiTests)|(CardCorrectionApiTests)/* executed=0 passed=0 failed=0 skipped=0 trx=/work/worktrees/task-dc2740ed/.antiphon/checkpoints/20261014-174622-c17a/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=06df7e14d39e494d525b7f882b2abcd32f791b7c sourceState=clean buildSource=verified
PHASES CP-3 slotWait=0s build=0s startup=unavailables testsWall=unavailables teardown=unavailables hostWall=unavailables reason=trx_timestamps_missing
CHECKPOINT CP-4 commit=06df7e14d39e494d525b7f882b2abcd32f791b7c build=reused filter=/*/*/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=40m slot=granted waited=0s dirty=0 source=06df7e14d39e494d525b7f882b2abcd32f791b7c sourceState=clean buildSource=verified
```

Evidence: .antiphon/c1255-corrected/CP-3-20261014-182706-583d/report.md

```text
source: 06df7e14d39e494d525b7f882b2abcd32f791b7c state=clean buildSource=verified
CHECKPOINT CP-3 commit=06df7e14d39e494d525b7f882b2abcd32f791b7c build=reused filter=/*/*/(BoardServiceIntegrationTests*)|(BoardProjectArchiveApiTests*)|(CardCorrectionApiTests*)/* executed=23 passed=23 failed=0 skipped=0 trx=/work/worktrees/task-dc2740ed/.antiphon/c1255-corrected/CP-3-20261014-182706-583d/run.trx slot=granted waited=0s dirty=0 source=06df7e14d39e494d525b7f882b2abcd32f791b7c sourceState=clean buildSource=verified
```

Evidence: .antiphon/checkpoints/20261014-183328-edf8/report.md

```text
source: 85a2aa10521d770d0c209ebcfdd911abd93bbeef state=clean buildSource=notApplicable
CHECKPOINT CP-5 commit=85a2aa10521d770d0c209ebcfdd911abd93bbeef build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/board/BoardPage.test.tsx src/features/board/BoardCard.test.tsx src/features/board/CardRow.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=85a2aa10521d770d0c209ebcfdd911abd93bbeef sourceState=clean buildSource=notApplicable
```

Evidence: /tmp/antiphon-c1255-dc2740ed-base/.antiphon/checkpoints/20261014-183327-4b70/report.md

```text
source: a02389cab727d508532e6d3db8ff6cbcf3eb7366 state=clean buildSource=verified
CHECKPOINT CP-6 commit=a02389cab727d508532e6d3db8ff6cbcf3eb7366 build=ok filter=/*/*/*/(Registry_matches_compiled_metadata*)|(environment_reaches_only_its_row_and_its_reruns*)|(partial_copy_failure_rolls_back_owned_run*)|(launch_intent_failure_prevents_native_launch*)|(preparing_journal_precedes_all_run_io*)|(definite_native_refusal_rolls_back_owned_run*)|(post_launch_bookkeeping_failure_keeps_image*)|(dead_executor_ack_recovers_without_progress*)|(late_executor_ack_resolves_launch_unknown*)|(confirmed_dead_local_identity_permits_cleanup*)|(deleting_root_rejects_registration*)|(row_green_only_when_rerun_passed_and_nothing_else_failed*)|(reruns_only_known_flaky_names_once*)|(rerun_invokes_once_per_class*)|(rerun_lines_and_reruns_suffix*)|(unlisted_failure_never_reruns*)|(copies_the_dll_set_never_the_source_tree*)|(total_deadline_skips_a_queued_build_without_starting_it*)|(total_deadline_kills_running_and_skips_queued*) executed=19 passed=0 failed=19 skipped=0 trx=/tmp/antiphon-c1255-dc2740ed-base/.antiphon/checkpoints/20261014-183327-4b70/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=a02389cab727d508532e6d3db8ff6cbcf3eb7366 sourceState=clean buildSource=verified
PHASES CP-6 slotWait=0s build=220.8212824s startup=5.4151982s testsWall=2.4326939s teardown=0.6137513s hostWall=8.4620156s
```

## Baseline failure detail (unedited report excerpts)

```text
FAILED Antiphon.TestSupport.TestClassificationGuardTests.Registry_matches_compiled_metadata (CP-6) ShouldAssertException: lines     should be empty but had 1     item and was ["lane-xor Antiphon.Tests.Application.MatrixDefaultComplexitySettingsTests"]  Additional Info:     lane-xor Antiphon.Tests.Application.MatrixDefaultComplexitySettingsTests -> rows/CP-6/failures.md
FAILED Antiphon.Tests.Checkpoints.CheckpointImportTests.environment_reaches_only_its_row_and_its_reruns (CP-6) ShouldAssertException: retryDriver.Count(CheckpointFixtures.IsRun)     should be 2     but was 1 -> rows/CP-6/failures.md
FAILED Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.preparing_journal_precedes_all_run_io (CP-6) ShouldAssertException: Runs(repo)     should be empty but had 1     item and was ["/tmp/c723-01a13bb49e8477b4bfca93e27cd8b038/.antiphon/checkpoints/20261014-183735-d386"] -> rows/CP-6/failures.md
FAILED Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.launch_intent_failure_prevents_native_launch (CP-6) ShouldAssertException: Runs(repo)     should be empty but had 1     item and was ["/tmp/c723-01a13bb49ea972f8b71f3f0d930d4a3e/.antiphon/checkpoints/20261014-183735-a28b"] -> rows/CP-6/failures.md
FAILED Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.post_launch_bookkeeping_failure_keeps_image (CP-6) ShouldAssertException: result.ExitCode     should be 2     but was 0 -> rows/CP-6/failures.md
FAILED Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.partial_copy_failure_rolls_back_owned_run (CP-6) ShouldAssertException: Runs(repo)     should be empty but had 1     item and was ["/tmp/c723-01a13bb4a049728c96566331c97cc21e/.antiphon/checkpoints/20261014-183735-820d"] -> rows/CP-6/failures.md
FAILED Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.definite_native_refusal_rolls_back_owned_run (CP-6) TestExecutionException: Test failed: Directory.Exists(result.RunDirectory)     should be False     but was True | After(Test) hook failed: checkpoint fixture teardown failed: /tmp/c723-01a13bb4a06a77b9bf592e25c59032a8: nested executor launch-outcome-unknown: /tmp/c723-01a13bb4a06a77b9bf592e25c59032a8/.antiphon/checkpoints/20261014-183735-f42f -> rows/CP-6/failures.md
FAILED Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.dead_executor_ack_recovers_without_progress (CP-6) ShouldAssertException: Directory.Exists(Path.Combine(run, "tool"))     should be False     but was True -> rows/CP-6/failures.md
FAILED Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.late_executor_ack_resolves_launch_unknown (CP-6) ShouldAssertException: new ToolCopyCleanup().Remove(run).Outcome     should be "Removed"     but was "Refused"     difference Difference     |            |    |    |                             |           \|/  \|/  \|/             Index          | 0    1    2    3    4    5    6     Expected Value | R    e    m    o    v    e    d     Actual Value   | R    e    f    u    s    e    d     Expected Code  | 82   101  109  111  118  101  100   Actual Code    | 82   101  102  117  115  101  100 -> rows/CP-6/failures.md
FAILED Antiphon.Tests.Checkpoints.CheckpointProcessIdentityTests.confirmed_dead_local_identity_permits_cleanup (CP-6) ShouldAssertException: receipt.Outcome     should be "Removed"     but was "Refused"     difference Difference     |            |    |    |                             |           \|/  \|/  \|/             Index          | 0    1    2    3    4    5    6     Expected Value | R    e    m    o    v    e    d     Actual Value   | R    e    f    u    s    e    d     Expected Code  | 82   101  109  111  118  101  100   Actual Code    | 82   101  102  117  115  101  100 -> rows/CP-6/failures.md
FAILED Antiphon.Tests.Checkpoints.CheckpointRunOwnershipTests.deleting_root_rejects_registration (CP-6) IOException: refused: unrecorded-existing -> rows/CP-6/failures.md
FAILED Antiphon.Tests.Checkpoints.RerunPolicyTests.reruns_only_known_flaky_names_once (CP-6) ShouldAssertException: result.ExitCode     should be 0     but was 2 -> rows/CP-6/failures.md
FAILED Antiphon.Tests.Checkpoints.RerunPolicyTests.unlisted_failure_never_reruns (CP-6) ShouldAssertException: result.ExitCode     should be 1     but was 2 -> rows/CP-6/failures.md
FAILED Antiphon.Tests.Checkpoints.RerunPolicyTests.rerun_invokes_once_per_class (CP-6) ShouldAssertException: result.ExitCode     should be 0     but was 2 -> rows/CP-6/failures.md
FAILED Antiphon.Tests.Checkpoints.RerunPolicyTests.row_green_only_when_rerun_passed_and_nothing_else_failed (CP-6) ShouldAssertException: mixed.ExitCode     should be 1     but was 2 -> rows/CP-6/failures.md
FAILED Antiphon.Tests.Checkpoints.RerunPolicyTests.rerun_lines_and_reruns_suffix (CP-6) ShouldAssertException: result.Line     should contain (case insensitive comparison) "reruns=1"     but was actually "" -> rows/CP-6/failures.md
FAILED Antiphon.Tests.Checkpoints.ShadowCopyTests.copies_the_dll_set_never_the_source_tree (CP-6) IOException: refused: output-create-failed:2 -> rows/CP-6/failures.md
FAILED Antiphon.Tests.Checkpoints.TimeoutTests.total_deadline_skips_a_queued_build_without_starting_it (CP-6) ShouldAssertException: driver.Count(CheckpointFixtures.IsBuild)     should be 1     but was 0 -> rows/CP-6/failures.md
FAILED Antiphon.Tests.Checkpoints.TimeoutTests.total_deadline_kills_running_and_skips_queued (CP-6) ShouldAssertException: result.Rows.Select(row => row.State).OrderBy(state => state)     should be ["skipped", "timeout"]     but was (case sensitive comparison) ["slot-refused", "slot-refused"]     difference [*"slot-refused"*, *"slot-refused"*] -> rows/CP-6/failures.md
```

## Closing repository checks

Full-range check-evidence-diff.ps1 at f4bc9d46acd90c8be66e15908c6c69d453dac5d5: 25 commits, 1 permitted Markdown report, 0 violations. Closing merge-tree against fetched master d74b595de6444b9b59fce7ac91819e1432326ce4 exited 0. Alternate outputs and tool shadows owned by this task were removed by literal paths after validation; build-source stamps preserved unchanged in ignored .antiphon/c1255-build-provenance. Original receipts, logs and TRX remain at the recorded paths. Baseline detached checkout is retained only for its ignored diagnostic evidence. Final report-only commit is checked again over the full task range and its remote equality reported to the caller.
