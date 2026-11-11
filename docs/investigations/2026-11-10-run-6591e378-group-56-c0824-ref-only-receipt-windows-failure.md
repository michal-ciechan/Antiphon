# Run 6591e378 group 56: C0824RefOnlyReceiptTests Windows failure

Read-only extract from the desktop qualification logs. No build or test was run. The running qualification was not touched.

- Run: 6591e378 on rc/2026-11-15 = `ac6d00022e940ee3b90584c7d313567b4491c498`.
- Checkout: `C:\Antiphon\releases\checkout-4dd2a426` at that same SHA. Test assembly `C:\Antiphon\releases\checkout-4dd2a426\tests\Antiphon.Tests\bin\Debug\net9.0\Antiphon.Tests.dll`.
- Host: DESKTOP-KTLKPIF, TUnit v1.44.0.0, Windows 10.0.19045, win-x64, .NET 9.0.22, Microsoft Testing Platform v2.2.2.
- Invocation: `C487_INVOCATION=6591e378c04d47bc898cf0927352b01b`.
- Sources under `C:\Antiphon\releases\candidates\rc-2026-11-15-ac6d00022e94\logs\2026-11-10-1248-6591e378\` (name filter `antiphon-all-group-56*`):
  - `antiphon-all-group-56.trx` (89719 bytes)
  - `antiphon-all-group-56-tests.log` (4055 bytes)
  - `antiphon-all-group-56.timing.json` (2458 bytes)
  - `antiphon-all-group-56-execution\log_261111031631937.diag` (177372 bytes)
- Filter from the diag command line: `/*/Antiphon.Tests.Application/(AgentTaskLandNestedRepositoryCleanupTests*)|(C0824RefOnlyReceiptTests*)|(RunnerTaskSettlementTests*)/*`
- Chunk: started `2026-11-11T03:16:31.6697247Z`, completed `2026-11-11T03:18:46.5323678Z`. Counters total 43, executed 43, passed 42, failed 1, error 0, timeout 0, skipped 0. Wall `2m 12s 608ms` (timing `runWallSeconds` 134.747). `queueWaitSeconds` 0. `buildSeconds` 655.862 before the run (`buildSlot` `not-used`). `fixtureSetupSeconds` 4.89, `processSpawnSeconds` 2.41, `processWaitSeconds` 55.05. p50 9.123s, p95 17.621s, max 81.042s (this failure).
- RunInfo: outcome Error, exit code 2, timestamp `2026-11-11T03:18:44.7519848Z` (`Exit code indicates failure: '2'`). That is the testing-platform failed-test exit, not a crash.

## Failed test

One failure. Class `Antiphon.Tests.Application.C0824RefOnlyReceiptTests`. Method `Settlement_note_reaches_caller_and_recovers_handoffs`.

| Field | Value |
|---|---|
| Outcome | Failed |
| Start | `2026-11-11T03:16:48.0541811Z` |
| End | `2026-11-11T03:18:09.0964565Z` |
| Duration | `00:01:21.0422754` (timing.json 81.042s, the chunk maximum) |
| Categories | Integration, Slow |
| Source | `tests\Antiphon.Tests\Application\C0824RefOnlyReceiptTests.cs` line 19 (diag LineSpan) |
| Stdout | only `ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Application.C0824RefOnlyReceiptTests.1.1.Settlement_note_reaches_caller_and_recovers_handoffs.1.1.0` |
| StdErr | none on this result |
| Expected vs actual | none. No Shouldly message. |

The phase id is written by `TestTiming.OnTestStart` (`tests/Shared/TestTiming.cs`), so the test was entered. The method body writes nothing. Diag `StepTimings` is empty.

### Assertion message

There is no assertion message. The TRX `ErrorInfo.Message` is:

```
TimeoutException: Test 'Settlement_note_reaches_caller_and_recovers_handoffs' timed out after 00:01:20

--- Task Status: WaitingForActivation ---

--- Timeout Handler Stack Trace ---
Note: This is the timeout handler's stack trace, not the blocked test's stack.
   at System.Environment.get_StackTrace()
   at TUnit.Engine.Helpers.TimeoutDiagnostics.AppendStackTraceDiagnostics(StringBuilder sb)
   at TUnit.Engine.Helpers.TimeoutDiagnostics.BuildTimeoutDiagnosticsMessage(String baseMessage, Task executionTask)
   at TUnit.Engine.Helpers.TimeoutHelper.ExecuteWithTimeoutAsync(Func`2 taskFactory, TimeSpan timeout, CancellationToken cancellationToken, String timeoutMessage)
   ... timer / thread-pool frames ...
```

`tests.log` repeats the same `TestFailedException` and the same note that the stack is the timeout handler, not the blocked test. The TRX `timeout` counter stays 0 because TUnit records this `TimeoutException` as outcome Failed.

### Stack top frames (TRX StackTrace)

```
at TUnit.Engine.Helpers.TimeoutHelper.ExecuteWithTimeoutAsync(...)
at TUnit.Engine.TestExecutor.ExecuteAsync(...)
at TUnit.Engine.TestExecutor.ExecuteAsync(...)
at TUnit.Engine.Services.TestExecution.TestCoordinator.ExecuteTestAsync(...)
at TUnit.Engine.Services.TestExecution.TestCoordinator.ExecuteTestAsync(...)
```

`WaitingForActivation` is the status of the async test task after it has yielded on an await. The handler never captured the test's own frames.

The class attribute is `[Timeout(80_000)]` (`C0824RefOnlyReceiptTests.cs` line 16: provisional queue/restart pack). Duration `00:01:21.042` is that 80s budget plus handler accounting. The method is the class's only `[Test]`. It loops nine cuts in one budget: `eligible`, `busy`, `settlement-before-save`, `settlement-before-commit`, `settlement-saved`, `before-enqueue`, `queue-inserted`, `queue-committed-before-wakeup`, `receipt-before-save`. Each cut builds a `RunnerSettlementWorld`, pushes git, restarts services, and reconciles a caller receipt. Nothing in stdout names the cut that was in flight.

## Same-class siblings

None passed, and none exist. `git grep` at `ac6d00022` shows a single `[Test]` in `C0824RefOnlyReceiptTests`. The chunk filter included the whole class (`C0824RefOnlyReceiptTests*`) and the TRX has one result for it. Same-class siblings therefore do not bound which cut hung.

## What passed in the chunk (bounds the host, not the cut)

All 42 other results passed.

`AgentTaskLandNestedRepositoryCleanupTests` (8/8), all overlapping this test:

- `C971_UnsupportedNestedRecoveryIsRefused(False)` 11.009s, started `03:16:48.053Z`
- `C971_UnsupportedNestedRecoveryIsRefused(True)` 10.958s, started `03:16:48.053Z`
- `C971_LateNestedRepositorySurvivesSetAsideResume` 12.623s, started `03:16:48.053Z`
- `C971_PublicationPreservesNestedRepository(obj/nested, False)` 17.571s, started `03:16:48.053Z`
- `C971_PublicationPreservesNestedRepository(obj/nested, True)` 8.933s
- `C971_PublicationPreservesNestedRepository(ignored/nested, False)` 14.235s
- `C971_PublicationPreservesNestedRepository(obj/deeper/cache/nested, False)` 13.795s
- `C971_PlainDisposableOutputIsCleaned` 14.158s

`RunnerTaskSettlementTests` (34/34). These 26 overlapped the failure window (`start < 03:18:09.096Z` and `end > 03:16:48.054Z`):

- `C1805_BindRefusalRecoveryUnderLeaseContentionStaysBlockedWithoutDebt`
- `Runner_sync_block_commits_the_completion_obligation`
- `Bind_refusal_recovery_commits_the_completion_obligation`
- `C1113_CodeLeaseBusyWithoutProgressServiceStaysBlocked`
- `Settlement_sync_fences_a_competing_retirement`
- `Sync_uncertainty_blocks_and_reply_retries`
- `Sustained_lease_contention_spans_sweeps_without_holding_up_other_settlements`
- `Artifact_and_git_summary_use_fetched_commit`
- `Refused_sync_never_autosaves_or_releases_workspace`
- `C1082_ReviewLeaseBusySettlesSucceededWithPendingSyncDebt`
- `Bind_refusal_recovery_stays_blocked_unless_the_named_tip_descends`
- `C1082_CodeLeaseBusyEqualTipFailsOnItsOwnVerdict`
- `Uncorrelated_and_nonfinal_turns_never_fetch`
- `C1082_CodeLeaseBusyWithLocalObjectsSettlesSucceededPending`
- `C1802_LeaseBusyObservationSettlesWithDebtAndNoReply`
- `C788_NonWorktreeDoesNotGetNoPushWarning(Shared)`
- `Unpushed_runner_commit_is_salvaged_before_completion`
- `Bind_refusal_recovery_names_only_full_commit_ids`
- `Rebased_runner_mirror_blocks_with_retained_tip_and_recovery`
- `Bind_refusal_recovery_blocks_an_unconfirmed_runner_branch`
- `Force_pushed_origin_tip_still_blocks_with_reviewed_source_recovery`
- `Runner_push_settles_success_without_claim`
- `No_push_on_a_plan_role_settles_succeeded_with_a_visible_warning`
- `C788_NonCodeNoPushRoleMatrix(Investigate)` (19ms overlap at the end)
- `Bind_refusal_recovery_confirms_the_pushed_tip_its_held_report_names`
- `No_push_settles_with_specific_reason`

These 8 `RunnerTaskSettlementTests` passed after the failure ended:

- `C788_NonCodeNoPushRoleMatrix(Review)`
- `C788_NonCodeNoPushRoleMatrix(Custom)`
- `C788_NonWorktreeDoesNotGetNoPushWarning(ReadOnly)`
- `C788_ClaimWarningDoesNotDuplicateNoPush`
- `C788_NonCodeNoPushRoleMatrix(TestDesign)`
- `C1082_PendingDebtRecoversThroughTheDispatcherSweep`
- `C1082_DisabledSettingKeepsLeaseBusyBlocked`
- `Unmarked_completion_gets_the_code_progress_policy`

## Classification

Timing/load timeout, not a deterministic assertion on returned state, and not a sharing violation.

Quoted evidence:

- Message is `TimeoutException: Test 'Settlement_note_reaches_caller_and_recovers_handoffs' timed out after 00:01:20` with `Task Status: WaitingForActivation`.
- No expected value, no actual value, no Shouldly text, no `IOException`, no sharing-violation text.
- Duration `00:01:21.0422754` matches the class `[Timeout(80_000)]`.
- The test sits on `ProcessSpawnLimit` (`Limit` 1). The other two classes sit on `GitProcessSpawnLimit` (`Clamp(ProcessorCount / 2, 2, 4)`), which runs beside the strict lane. Four `C971_*` tests were stamped at `03:16:48.053Z`, the same millisecond this test started, and git-lane settlement tests kept passing for the whole 81s.
- Chunk `processWaitSeconds` is 55.05. This test is the max (81.042s) against a p95 of 17.621s.

One sample cannot separate "this pack always exceeds 80s" from "this pack exceeded 80s beside the git lane". The log does not name the cut. Debug has to reproduce the single method.

## Docker-down window

Not in the window. The chunk ran `2026-11-11T03:16:31Z` to `2026-11-11T03:18:46Z`. The cited Docker-down window is 16:46Z to 16:55Z. Docker was up in this process: the opening `C971` stdout records testcontainers starting container `69300dc55891`, `pg_isready` succeeding, and `Docker container 69300dc55891 ready`. A later passing test (`Unmarked_completion_gets_the_code_progress_policy`) logs `[testcontainers.org 00:02:03.50] Stop Docker container 69300dc55891`, after this failure.

## Stderr near the failure

The failed result has no `StdErr`. Concurrent results that did:

- `C971_UnsupportedNestedRecoveryIsRefused(True)`: `C1420 schema-cache image key=900301511a50a42f7bc489d217ccc834 ms=6080` (passed, 10.958s).
- `C971_LateNestedRepositorySurvivesSetAsideResume`: `[TUnit] External span cap of 100 reached` (passed).
- Several passing settlement tests logged `repository_child_tree_kill` / `repository_child_journal_release` with `still_alive=none` and `reason=removed` (roots 484268, 409260, 231604, 267708). Those are cleanup lines on passed tests, not a stuck child of the failed test.

## Git for Windows

Not indicated. The failure text has no `core.autocrlf`, no `ReadOnly`, no path-form error, and no git stderr on the failed result. Concurrent git-lane tests that fetch, push, and salvage commits passed in the same process (`Artifact_and_git_summary_use_fetched_commit`, `Unpushed_runner_commit_is_salvaged_before_completion`, `Runner_push_settles_success_without_claim`, and the bind-refusal recovery set).

## Master since the rc cut

Not fixed on master. `git ls-remote origin refs/heads/master` and local `origin/master` are both `3fd231a2e498e463443079c40cf38711c0be8504` (`2026-11-11 02:33:23 +0000`, `chore: refresh land-graph records for the landing batch`). `bc71a95e7710532a9a68416f1d7b9e158fe96a15` (`2026-11-11 01:46:16 +0000`, `fix: regenerate the derived index on the batch 175 tree`) is an ancestor of that master and is not an ancestor of `ac6d00022`.

`git log ac6d00022e940ee3b90584c7d313567b4491c498..origin/master` is empty for the receipt test and the code it drives. Blob hashes match at both tips:

| Path | Blob |
|---|---|
| `tests/Antiphon.Tests/Application/C0824RefOnlyReceiptTests.cs` | `7b85b2b48f55b884a85a5d5c4c1d8f468bad7541` |
| `tests/Antiphon.Tests/TestHelpers/RunnerSettlementWorld.cs` | `12b9d0a75e61af19241b63a9e07de96be350dd62` |
| `tests/Antiphon.Tests/TestHelpers/RestartableQueueRecipient.cs` | `40f518fe1200665383415f98e0dc335d8dc15af9` |
| `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs` | same at both tips |
| `tests/Antiphon.Tests/TestHelpers/ProcessSpawnLimit.cs` | same at both tips |
| `server/Application/Services/AgentTaskLandNotificationService.cs` | `6b2b4ddacaaefa9f303d917091e0ef5d99488a6b` |
| `server/Application/Services/LandDeliveryBoundary.cs` | `6191379321dc44416b3ba3cf782b2a48517885c0` |
| `server/Application/Services/TaskCompletionNotification.cs` | `1506c3525b415c898c25ae071786af8834edb004` |
| `server/Application/Services/CompletionNoteWork.cs` | same at both tips |
| `server/Application/Core/DelegationReportFormatter.cs` | same at both tips |
| `src/Antiphon.SessionRunner.Contracts/PromptSubmissionMatch.cs` | `69853c9cd5b0039c646206cec01d29e9bb1e79d3` |

No commit in that range mentions `C0824` or `RefOnlyReceipt`. Receipt-named commits in the range are other tests (`CheckpointReceiptSafetyTests`, `C0824RefOnlySnapshotMigrationTests`, CARD-1293 HTTP receipt, CARD-0908 CRLF prune preview). The last commit that touched this test file is `ce881d97a94d44435729635ba2b5596b0e8d3761` (`2026-11-06 15:02:05 +0000`, `test(card-0824): rebuild recipient queue graphs before and after real caller input`), which is already in the rc cut.

No why-missed note for this class is in this worktree. CARD-1781 still owes that note from the Debug fix.

## Cause for Debug

`Settlement_note_reaches_caller_and_recovers_handoffs` ran until the 80s class timeout while parked on an await (`WaitingForActivation`). It produced no assertion and no cut name. The other 42 tests in group 56 passed, including every git-lane settlement and nested-repository test, and Docker was ready. The receipt test, its helpers, and the land-notification services are byte-identical on `origin/master` `3fd231a2`, so master has not fixed it.
