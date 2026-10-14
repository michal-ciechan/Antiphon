# CARD-1253 Code report

Implemented both restrictive-FK fixes and verified all 110 affected integration cases; ready for ordinary Review within the caller's clarified scope. R-3's proposed bounded whole Unit follow-up is NOT RUN, DEFERRED and outside this task. The earlier broad Unit attempt timed out after 21 minutes without a terminal TRX. All 19 failures printed before that timeout reproduce at the original master base and are inherited. The partial console also records 37 Windows-only skips, which remain unqualified. No whole Unit pass is claimed.

## Identity and source

- Original Code task / landing owner: f3a5b136-0402-405d-88cd-4b3875b5d366.
- Branch: feat/card-task-f3a5b136.
- Worktree: /work/worktrees/task-f3a5b136.
- Assigned starting SHA: 2a64db19bea886657526100ff92a6af4aad2f67f.
- Master base at start, reached by fast-forward: c637b74eea51bb88cf0d2137c05672692ab909dc.
- Tests-first commit: 71f2cf93261adab4049310cff7ae792c1d0cc5a4; production code unchanged from base.
- Implementation commit and actual candidate tested: 44fb572e67122b18847378a4f733b0560209b323.
- The subsequent report/plan-only commit is identified by the settlement's full SHA and progress token. Earlier receipts remain bound to their actual tested SHAs, not that documentation commit.
- Plan: docs/superpowers/plans/2026-10-14-card-1253-cascade-foreign-keys.md.
- Round: Final with caller-scoped verification V-1 through V-4 and R-1/R-2/R-4 complete; R-3 follow-up deferred. Restart target after eventual land: server. No restart/deploy performed.

## Change and scope

ProjectCascade now deletes ExpectationNudges through the exact doomed CardComment IDs, and AgentTaskDecisionQuestions through the exact doomed AgentSession IDs, before those parents. Both statements execute inside the caller's existing transaction. Task/card ownership and destination-session snapshots are deliberately not substituted for these FKs. No FK behavior, confirmation safeguard, workflow blocker, timeout or assertion was loosened.

Files changed: server/Application/Services/ProjectCascade.cs; tests/Antiphon.Tests/Application/ProjectCascadeForeignKeyTests.cs; the plan above; this report.

The Done cleanup executors remain unregistered. Activating them requires coordination with in-flight recovery records so deletion cannot discard a running cleanup's recovery evidence. This task does not activate or repair those executors.

The narrow brief said “NO whole-Unit lane”; the higher-priority Code/Final instructions and appended Final profile required it. CP-3 was declared before execution. No full-assembly run occurred. The caller audit also identified SessionStateWarmupTests and CardFileBoardLookupTests as affected delete consumers, so their full classes were included in CP-4 before final execution.

Closing scope clarification: the caller explicitly excludes the proposed bounded whole Unit continuation, instructs no additional Unit run or wait, and requests ordinary Review. R-3 is therefore a deferred disclosure, and CP-3 has been removed from the executable checkpoint table. This documentation-only correction preserves the historical attempt, its actual timeout and all unedited evidence below; it does not relabel that attempt as never run or passed. No further build or test was started for this closing amendment.

## Ordinary outcomes

| ID | Actual outcome |
|---|---|
| V-1 | PASS: 2 nudge deletion cases, board/project, with surviving sibling/other-project audits. Both failed on unchanged production with FK_ExpectationNudges_CardComments_AuditCommentId (23503). |
| V-2 | PASS: 2 decision-question cases, board/project, including task ownership in another project. Both failed on unchanged production with FK_AgentTaskDecisionQuestions_AgentSessions_AgentSessionId (23503). |
| V-3 | PASS: 4 rollback variants (nudge/question x board/project). All four were red first because FK failure preceded the exact injected failure. The fixed version reaches the failure after card deletion and a fresh context proves all parents, evidence rows and owner links restored. |
| V-4 | PASS: 2 live task-history/cleanup probes plus 1 literal 15-link model census. All 3 were also green before the production fix. |
| R-1 | PASS: full ProjectDeletionTests, including force confirmation, workflow refusal, last-board workflow preservation, CARD-1244 cleanup and rollback. |
| R-2 | PASS: full BoardProjectArchiveApiTests and CardFilePrivacyOwnershipAcceptanceTests. |
| R-3 | NOT RUN, DEFERRED: proposed bounded whole Unit follow-up excluded by the caller. Historical broad Unit attempt timed out at 21 minutes with no terminal TRX/counters; 19 console failures reproduce at base and are inherited; 37 partial Windows skip messages remain unqualified. The full-lane timeout itself was not rerun at base. No passing Unit result claimed. |
| R-4 | PASS: full SessionStateWarmupTests and CardFileBoardLookupTests. |
| M-1 | PASS: full snapshot audit and runtime literal census agree on all 15 Restrict links; no explicit NoAction links or omitted default-delete links target the seven named parent types. |
| M-2 | Code-head history guard and merge-tree passed; final report-head history guard, remote equality and clean-tree results are supplied in settlement. |

| Class | Red baseline at tests-first commit | Fixed candidate |
|---|---:|---:|
| ProjectCascadeForeignKeyTests | 11 executed: 3 passed, 8 failed, 0 skipped | 11 passed |
| ProjectDeletionTests | not selected in red run | 20 passed |
| BoardProjectArchiveApiTests | not selected in red run | 2 passed |
| CardFilePrivacyOwnershipAcceptanceTests | not selected in red run | 16 passed |
| SessionStateWarmupTests | not selected in red run | 16 passed |
| CardFileBoardLookupTests | not selected in red run | 45 passed |

Fixed integration total: 110 executed, 110 passed, 0 failed, 0 skipped. Fresh TRX definitions and results were inspected for every intended class and every new method/argument variant.

## Full restrictive-FK audit

| Restrictive link | Probe result and handling |
|---|---|
| ExpectationNudge.AuditCommentId -> CardComment | Newly fixed; V-1/V-3 red before fix, green after. |
| AgentTaskDecisionQuestion.AgentSessionId -> AgentSession | Newly fixed; V-2/V-3 red before fix, green after. |
| AgentTask.ParentTaskId -> AgentTask | V-4 green before/after; child retains parent, because tasks survive card/project deletion. |
| AgentTaskDispatchWarningIntent.TaskId -> AgentTask | V-4 green before/after; intent and parent task survive. |
| AgentTaskLandRequest.TaskId -> AgentTask | V-4 green before/after; request and parent task survive. |
| AgentTaskLanding.TaskId -> AgentTask | V-4 green before/after; operation and parent task survive. |
| Board.ProjectId -> Project | V-4 green before/after, V-1/V-2 green after; boards removed before project. |
| CardWorktreeCleanup.CardId -> Card | V-4 green before/after; existing leaf-first cleanup handles it; R-1 CARD-1244 cases green. |
| CardWorktreeCleanup.DoneRevisionId -> CardRevision | V-4 green before/after; generation removed before card/revision; R-1 rollback green. |
| CardWorktreeCleanupTarget.TaskId -> AgentTask | V-4 green before/after; target removed, task survives. |
| TaskWorktreeMirrorBinding.TaskId -> AgentTask | V-4 green before/after; binding and task survive. |
| TaskWorktreeRetirement.TaskId -> AgentTask | V-4 green before/after; retirement and task survive. |
| VerificationExecution.TaskId -> AgentTask | V-4 green before/after; execution and task survive. |
| WorktreeCleanupAttempt.TaskId -> AgentTask | V-4 green before/after; attempt and task survive. |
| Workflow.ProjectId -> Project | R-1 green: forced project delete refuses while workflows exist; deleting the last board preserves its workflow-owned project. A dangling-parent delete cannot occur through these guarded services. |

## Source-qualified receipts

CP-1/CP-2/CP-4 receipt validation returned exit 0: CHECKPOINT SOURCE VALID source=44fb572e67122b18847378a4f733b0560209b323 rows=3. Validating the historical entire selection, including CP-3, returned exit 2, reason=row_failed. There is no clean whole Unit certificate; the scoped handoff relies on the three validated integration rows.

All builds/rows used a granted host slot, waited=0s. The checkpoint bootstrap was the only tooling build outside the manifest: scripts/build-slot.ps1 -Label c1253-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1253-tool/ --property:UseAppHost=false --nologo. Its lease was granted, waited=0s, held=5s, build passed with two warnings. The red source group built once; the final group built once and reused it for all four rows. Linux UseAppHost=false was retained.

Unedited CHECKPOINT lines follow, including the invalid zero-test base-filter attempt. Its repair added the documented method-prefix wildcards and reused the same verified base build; it is not counted as test evidence.

```text
CHECKPOINT CP-1 commit=71f2cf93261adab4049310cff7ae792c1d0cc5a4 build=ok filter=/*/*/(ProjectCascadeForeignKeyTests*)/* executed=11 passed=3 failed=8 skipped=0 trx=/work/worktrees/task-f3a5b136/.antiphon/checkpoints/20261014-123634-2e87/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=71f2cf93261adab4049310cff7ae792c1d0cc5a4 sourceState=clean buildSource=verified
CHECKPOINT CP-1 commit=44fb572e67122b18847378a4f733b0560209b323 build=ok filter=/*/*/(ProjectCascadeForeignKeyTests*)/* executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-f3a5b136/.antiphon/checkpoints/20261014-124155-df0e/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=44fb572e67122b18847378a4f733b0560209b323 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=44fb572e67122b18847378a4f733b0560209b323 build=reused filter=/*/*/(ProjectDeletionTests*)|(BoardProjectArchiveApiTests*)|(CardFilePrivacyOwnershipAcceptanceTests*)/* executed=38 passed=38 failed=0 skipped=0 trx=/work/worktrees/task-f3a5b136/.antiphon/checkpoints/20261014-124155-df0e/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=44fb572e67122b18847378a4f733b0560209b323 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=44fb572e67122b18847378a4f733b0560209b323 build=reused filter=/*/*/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=21m slot=granted waited=0s dirty=0 source=44fb572e67122b18847378a4f733b0560209b323 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=44fb572e67122b18847378a4f733b0560209b323 build=reused filter=/*/*/(SessionStateWarmupTests*)|(CardFileBoardLookupTests*)/* executed=61 passed=61 failed=0 skipped=0 trx=/work/worktrees/task-f3a5b136/.antiphon/checkpoints/20261014-124155-df0e/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=44fb572e67122b18847378a4f733b0560209b323 sourceState=clean buildSource=verified
CHECKPOINT CP-BASE commit=c637b74eea51bb88cf0d2137c05672692ab909dc build=ok filter=/*/*/*/(Registry_matches_compiled_metadata)|(environment_reaches_only_its_row_and_its_reruns)|(preparing_journal_precedes_all_run_io)|(late_executor_ack_resolves_launch_unknown)|(dead_executor_ack_recovers_without_progress)|(definite_native_refusal_rolls_back_owned_run)|(partial_copy_failure_rolls_back_owned_run)|(launch_intent_failure_prevents_native_launch)|(post_launch_bookkeeping_failure_keeps_image)|(confirmed_dead_local_identity_permits_cleanup)|(deleting_root_rejects_registration)|(reruns_only_known_flaky_names_once)|(rerun_lines_and_reruns_suffix)|(row_green_only_when_rerun_passed_and_nothing_else_failed)|(rerun_invokes_once_per_class)|(unlisted_failure_never_reruns)|(copies_the_dll_set_never_the_source_tree)|(total_deadline_kills_running_and_skips_queued)|(total_deadline_skips_a_queued_build_without_starting_it) executed=0 passed=0 failed=0 skipped=0 trx=/work/worktrees/task-f3a5b136/.antiphon/c1253-baseline/CP-BASE-20261014-131240-5eb0/run.trx slot=granted waited=0s dirty=0 source=c637b74eea51bb88cf0d2137c05672692ab909dc sourceState=clean buildSource=verified
CHECKPOINT CP-BASE commit=c637b74eea51bb88cf0d2137c05672692ab909dc build=reused filter=/*/*/*/(Registry_matches_compiled_metadata*)|(environment_reaches_only_its_row_and_its_reruns*)|(preparing_journal_precedes_all_run_io*)|(late_executor_ack_resolves_launch_unknown*)|(dead_executor_ack_recovers_without_progress*)|(definite_native_refusal_rolls_back_owned_run*)|(partial_copy_failure_rolls_back_owned_run*)|(launch_intent_failure_prevents_native_launch*)|(post_launch_bookkeeping_failure_keeps_image*)|(confirmed_dead_local_identity_permits_cleanup*)|(deleting_root_rejects_registration*)|(reruns_only_known_flaky_names_once*)|(rerun_lines_and_reruns_suffix*)|(row_green_only_when_rerun_passed_and_nothing_else_failed*)|(rerun_invokes_once_per_class*)|(unlisted_failure_never_reruns*)|(copies_the_dll_set_never_the_source_tree*)|(total_deadline_kills_running_and_skips_queued*)|(total_deadline_skips_a_queued_build_without_starting_it*) executed=19 passed=0 failed=19 skipped=0 trx=/work/worktrees/task-f3a5b136/.antiphon/c1253-baseline/CP-BASE-20261014-131943-442e/run.trx slot=granted waited=0s dirty=0 source=c637b74eea51bb88cf0d2137c05672692ab909dc sourceState=clean buildSource=verified
```

Raw evidence roots (ignored, retained):

- /work/worktrees/task-f3a5b136/.antiphon/checkpoints/20261014-123634-2e87
- /work/worktrees/task-f3a5b136/.antiphon/checkpoints/20261014-124155-df0e
- /work/worktrees/task-f3a5b136/.antiphon/c1253-baseline/CP-BASE-20261014-131240-5eb0
- /work/worktrees/task-f3a5b136/.antiphon/c1253-baseline/CP-BASE-20261014-131943-442e

The final checkpoint run terminated with exit 5; the focused base row with exit 1. Every foreground invocation was awaited to termination. No exit-7 owner retry or commit_refs retry was needed. No proof repetition after green occurred. The base probe and its zero-selection repair are explicitly unlisted failure-triage drivers, required to establish inheritance; they did not repeat the full Unit lane.

## Base confirmation of Unit failures

The exact 19 printed failed methods were selected at c637b74eea51bb88cf0d2137c05672692ab909dc in an isolated detached worktree. Fresh TRX: 19 executed, 0 passed, 19 failed, 0 skipped; clean source and verified build binding. These are inherited failures under the same Linux environment. They are not cascade regressions. The full Unit timeout and unexecuted remainder are still unresolved.

| Base class | Failed cases |
|---|---:|
| Antiphon.TestSupport.TestClassificationGuardTests | 1 |
| Antiphon.Tests.Checkpoints.CheckpointImportTests | 1 |
| Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests | 7 |
| Antiphon.Tests.Checkpoints.CheckpointProcessIdentityTests | 1 |
| Antiphon.Tests.Checkpoints.CheckpointRunOwnershipTests | 1 |
| Antiphon.Tests.Checkpoints.RerunPolicyTests | 5 |
| Antiphon.Tests.Checkpoints.ShadowCopyTests | 1 |
| Antiphon.Tests.Checkpoints.TimeoutTests | 2 |

All 19 base failures, with the observed assertion/error:

- Antiphon.Tests.Checkpoints.TimeoutTests.total_deadline_kills_running_and_skips_queued: ShouldAssertException: result.Rows.Select(row => row.State).OrderBy(state => state) should be ["skipped", "timeout"] but was (case sensitive comparison) ["slot-refused", "slot-refused"] difference [*"slot-refused"*, *"slot-refused"*]
- Antiphon.Tests.Checkpoints.RerunPolicyTests.reruns_only_known_flaky_names_once: ShouldAssertException: result.ExitCode should be 0 but was 2
- Antiphon.Tests.Checkpoints.RerunPolicyTests.rerun_invokes_once_per_class: ShouldAssertException: result.ExitCode should be 0 but was 2
- Antiphon.Tests.Checkpoints.CheckpointImportTests.environment_reaches_only_its_row_and_its_reruns: ShouldAssertException: retryDriver.Count(CheckpointFixtures.IsRun) should be 2 but was 1
- Antiphon.Tests.Checkpoints.RerunPolicyTests.row_green_only_when_rerun_passed_and_nothing_else_failed: ShouldAssertException: mixed.ExitCode should be 1 but was 2
- Antiphon.Tests.Checkpoints.CheckpointRunOwnershipTests.deleting_root_rejects_registration: IOException: refused: unrecorded-existing
- Antiphon.Tests.Checkpoints.RerunPolicyTests.unlisted_failure_never_reruns: ShouldAssertException: result.ExitCode should be 1 but was 2
- Antiphon.Tests.Checkpoints.CheckpointProcessIdentityTests.confirmed_dead_local_identity_permits_cleanup: ShouldAssertException: receipt.Outcome should be "Removed" but was "Refused" difference Difference \| \| \| \| \| \\|/ \\|/ \\|/ Index \| 0 1 2 3 4 5 6 Expected Value \| R e m o v e d Actual Value \| R e f u s e d Expected Code \| 82 101 109 111 118 101 100 Actual Code \| 82 101 102 117 115 101 100 
- Antiphon.Tests.Checkpoints.RerunPolicyTests.rerun_lines_and_reruns_suffix: ShouldAssertException: result.Line should contain (case insensitive comparison) "reruns=1" but was actually ""
- Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.preparing_journal_precedes_all_run_io: ShouldAssertException: Runs(repo) should be empty but had 1 item and was ["/tmp/c723-01a13a91abe27daa8734896dd0143c45/.antiphon/checkpoints/20261014-131947-5b61"]
- Antiphon.Tests.Checkpoints.ShadowCopyTests.copies_the_dll_set_never_the_source_tree: IOException: refused: output-create-failed:2
- Antiphon.Tests.Checkpoints.TimeoutTests.total_deadline_skips_a_queued_build_without_starting_it: ShouldAssertException: driver.Count(CheckpointFixtures.IsBuild) should be 1 but was 0
- Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.late_executor_ack_resolves_launch_unknown: ShouldAssertException: new ToolCopyCleanup().Remove(run).Outcome should be "Removed" but was "Refused" difference Difference \| \| \| \| \| \\|/ \\|/ \\|/ Index \| 0 1 2 3 4 5 6 Expected Value \| R e m o v e d Actual Value \| R e f u s e d Expected Code \| 82 101 109 111 118 101 100 Actual Code \| 82 101 102 117 115 101 100 
- Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.dead_executor_ack_recovers_without_progress: ShouldAssertException: Directory.Exists(Path.Combine(run, "tool")) should be False but was True
- Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.partial_copy_failure_rolls_back_owned_run: ShouldAssertException: Runs(repo) should be empty but had 1 item and was ["/tmp/c723-01a13a91abe273c888bfe319a6920921/.antiphon/checkpoints/20261014-131947-d4bc"]
- Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.launch_intent_failure_prevents_native_launch: ShouldAssertException: Runs(repo) should be empty but had 1 item and was ["/tmp/c723-01a13a91abe274bbbab4a6672024b303/.antiphon/checkpoints/20261014-131947-424a"]
- Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.post_launch_bookkeeping_failure_keeps_image: ShouldAssertException: result.ExitCode should be 2 but was 0
- Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.definite_native_refusal_rolls_back_owned_run: TestExecutionException: Test failed: Directory.Exists(result.RunDirectory) should be False but was True \| After(Test) hook failed: checkpoint fixture teardown failed: /tmp/c723-01a13a91abe27699b3eb049debfd94fe: nested executor launch-outcome-unknown: /tmp/c723-01a13a91abe27699b3eb049debfd94fe/.antiphon/checkpoints/20261014-131947-3efb
- Antiphon.TestSupport.TestClassificationGuardTests.Registry_matches_compiled_metadata: ShouldAssertException: lines should be empty but had 1 item and was ["lane-xor Antiphon.Tests.Application.MatrixDefaultComplexitySettingsTests"] Additional Info: lane-xor Antiphon.Tests.Application.MatrixDefaultComplexitySettingsTests 

## Unqualified native cases

These 37 messages are from the partial Unit console, not a complete native-test census or passing coverage. Each remains outstanding on an appropriate native host; the Linux driver was not forced to simulate Windows.

```text
The_shipped_codex_definition_resolves_to_a_real_executable_on_this_machine — The npm shim layout is Windows-specific
Resolves_sibling_flavor_when_configured_one_is_gone — Sibling executable flavors (.exe/.cmd/.bat) are resolved only on Windows
V01_canonical_cwd_uses_windows_separators_and_drops_trailing_slash — Canonical pin cwd is a Windows drive-rooted path
Off_settings_path_round_trips_through_LaunchArgvGuard — CommandLineToArgvW is Windows-only
reported_repository_paths_normalize_relative_and_absolute_windows_forms — Drive-letter report paths resolve only against a Windows repo root
trailing_slash_lists_children_of_that_directory — Drive-letter directory listing needs a Windows MockFileSystem
prefix_returns_matching_child_directories — Drive-letter directory listing needs a Windows MockFileSystem
partial_leaf_matches_substring_within_child_name — Drive-letter directory listing needs a Windows MockFileSystem
existing_path_reports_exists_true — Drive-letter directory listing needs a Windows MockFileSystem
caches_within_ttl_and_refreshes_after — Drive-letter directory listing needs a Windows MockFileSystem
Unsafe_raw_rules_are_refused_server_side_before_runner_calls(nul) — CARD-0382 Grok rules argv guard is Windows-only by design
Unsafe_raw_rules_are_refused_server_side_before_runner_calls(crlf) — CARD-0382 Grok rules argv guard is Windows-only by design
Unsafe_raw_rules_are_refused_server_side_before_runner_calls(duplicate_first) — CARD-0382 Grok rules argv guard is Windows-only by design
Unsafe_raw_rules_are_refused_server_side_before_runner_calls(env_flag) — CARD-0382 Grok rules argv guard is Windows-only by design
Unsafe_raw_rules_are_refused_server_side_before_runner_calls(cr) — CARD-0382 Grok rules argv guard is Windows-only by design
Unsafe_raw_rules_are_refused_server_side_before_runner_calls(duplicate_second) — CARD-0382 Grok rules argv guard is Windows-only by design
Unsafe_raw_rules_are_refused_server_side_before_runner_calls(missing) — CARD-0382 Grok rules argv guard is Windows-only by design
Unsafe_raw_rules_are_refused_server_side_before_runner_calls(equals) — CARD-0382 Grok rules argv guard is Windows-only by design
Unsafe_raw_rules_are_refused_server_side_before_runner_calls(lf) — CARD-0382 Grok rules argv guard is Windows-only by design
Unsafe_raw_rules_are_refused_server_side_before_runner_calls(env) — CARD-0382 Grok rules argv guard is Windows-only by design
Unsafe_raw_rules_are_refused_server_side_before_runner_calls(alias) — CARD-0382 Grok rules argv guard is Windows-only by design
Unsafe_raw_rules_are_refused_server_side_before_runner_calls(braced_env) — CARD-0382 Grok rules argv guard is Windows-only by design
A_runner_on_the_modern_backend_confirms_the_raised_ceilings — Windows delivery profile proof
A_runner_on_the_inbox_conhost_downgrades_a_modern_server — Windows delivery profile proof
Unknown_runner_backend_downgrades_modern_server — Windows delivery profile proof
A_silent_runner_leaves_this_processes_own_decision_standing — Windows delivery profile proof
Phone_home_Grok_never_uses_local_modern_evidence — ConPTY only on Windows
Phone_home_Claude_keeps_the_inbox_ceiling_for_its_own_kind — ConPTY only on Windows
tool_copy_removal_retries_while_a_file_is_still_held_open — Native file-sharing retry proof requires Windows.
C665_LockedFileMidDeleteResumesOnLaterPass — Sharing-mode locks are a Windows file-system behaviour.
C721_HeldHandleDuringCleanupStaysRegisteredOrRecorded — Sharing-mode locks are a Windows file-system behaviour.
windows_quick_row_finishes_beside_a_slow_row — Concurrent Windows row processes must not inherit each other's stdout pipes.
windows_row_timeout_kills_the_start_b_grandchild — The descendant sweep kills a start /b grandchild on Windows.
windows_chatty_row_drains_interleaved_stdout_and_stderr — Windows row pipes must drain a large interleaved stdout and stderr.
windows_row_arguments_round_trip_intact — Windows row arguments are delivered by ProcessStartInfo.ArgumentList.
C1050_Windows_rolling_outcomes_are_exact — CARD-1050 and CARD-1110: this audit needs Windows placement
C1050_Windows_remote_outcomes_are_exact — CARD-1050 and CARD-1110: this audit needs Windows placement
```

## Pending SourceLanding Mutation

No deliberate mutants or PC red/restore/green cycles ran. All nine PC IDs and all 23 named variants remain pending; Mutation also owns missing-control discovery. Candidate mutation artifacts belong under docs/superpowers/mutations/.

| PC | Pending variants |
|---|---|
| PC-1 | omit nudge deletion: board/project; move it after parent deletion: board/project (4). |
| PC-2 | broaden nudge deletion to sibling board / other project (2). |
| PC-3 | omit question deletion: board/project; move it after session deletion: board/project (4). |
| PC-4 | select questions through task ownership instead of session (1). |
| PC-5 | broaden question deletion to sibling board / other project (2). |
| PC-6 | delete outside caller transaction: nudge/question x board/project (4). |
| PC-7 | remove surviving task/history: board/project (2). |
| PC-8 | omit endpoint / target / generation cleanup ordering (3). |
| PC-9 | remove workflow project-delete safeguard (1). |

## Remaining work and cleanup

R-3 bounded whole Unit continuation is not run and is deferred outside this task by the caller's closing clarification. The 19 base-confirmed failures remain listed as inherited, and the 37 Windows-only skips remain unqualified. No deferred ID is called passed. V-1 through V-4, R-1/R-2/R-4 and the required manual work are complete. Next is ordinary Review, then caller landing of the original Code task and SourceLanding Mutation; every PC remains pending. No timeout or assertion was loosened.

Removed only task-created outputs at literal paths: tests/Antiphon.Tests/bin-c1253/ (red and final generations), tools/Antiphon.Checkpoints/bin-c1253-tool/, both checkpoint tool shadows, and /tmp/card1253-base-f3a5b136/tests/Antiphon.Tests/bin-c1253-base/. The clean detached baseline worktree was removed through git worktree remove. Raw evidence stayed in the assigned worktree. This report and the plan status are documentation-only after the tested implementation SHA.
