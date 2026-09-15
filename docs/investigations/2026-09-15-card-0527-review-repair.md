# CARD-0527 review repair: Code 360e223e

Original Code / landing owner: `2853f966-b31e-4893-9bcc-b25fb9c0a4be`.
Repair branch: `feat/card-task-360e223e`; checkout:
`C:\Antiphon\worktrees\card-task-360e223e`.
Subject reviewed: `2193e8fad782a0578d84f43e28ff9d9da31be4d2`.
[Original plan and V/R/PC design](../superpowers/plans/2026-09-15-card-0527-commit-on-settle-plan.md).
Review findings/evidence: `C:\Antiphon\evidence\review-89bfbbdf`.
Repair execution evidence: `C:\Antiphon\evidence\code-360e223e`.

## Repairs and corrected contracts

- F1: G1 checks both rename endpoints. A scoped selection of either endpoint includes both in the commit.
- F2: unavailable ignore, staged-path, history and committed-path inspections remain failed inspections. No gate commit follows a failed check. The gate captures the pre-stage index with git write-tree and restores it with read-tree after a late refusal, preserving foreign partial staging and all working files. Hook failure keeps its existing staged-index contract.
- F3: outcome-bearing Shared settlements (committed/refused/Commit-child handoff) and Commit-child reports write an immutable TaskCompletion entry into the existing notification outbox with the task, terminal event and any child in the same SaveChanges. The existing boot/periodic outbox scanner recovers missing enqueue, keys queue insertion, and confirms only a complete destination UserPrompt past the attempt floor. The rendered body preserves child IDs, git receipts and audit warnings. Ordinary pruning already retains outstanding outbox rows, queues and destination transcripts. These immutable outcomes bypass optional distillation and polled-note shrinking; ordinary reports retain their existing behavior.
- F4: a server-created Shared/Never Commit child has explicit gated-commit authority. Never disables recursive automatic settlement; its complete brief still forbids push. The chain test uses the actual spawned row, routing selection, dispatcher, queue, UserPrompt, authenticated HTTP commit, child settlement and parent receipt.
- F5: nullable CommitUpstreamBaselineJson captures inspection status, full upstream ref and upstream SHA independently of local CommitBaselineSha. Missing and changed configuration are explicit. Movement is an observation, not proof the child pushed.
- F6: recovery matches a settlement digest of task ID, session ID, dispatch timestamp and complete report, and validates exact unique antiphon/task/gate/settlement trailers in git's parsed trailer block. Explicit endpoint commits and incidental GUID mentions cannot satisfy recovery. Dirty work appearing after a recovered settlement is preserved and named without new attribution.
- F7: eligibility uses AgentTaskRoles.IsSpecialist; endpoint test registration uses the centralized git helper. Required Unit and R-17 execution are part of ordinary verification.

The migration is CLI-generated and adds one nullable text column. Restart after eventual landing: **server**. No runner restart is required. This task does not land or deploy.

## Ordinary verification design

Run all original V-1..V-55 and R-1..R-17. The original plan's narrow 'Unit' example is superseded by the full `/*/*/*/*[Category=Unit]` lane. Every command records its exact source SHA and fresh TRX under the external evidence root; inspect each intended class/method and nonzero expanded counts.

| Coverage | Class / selection | Estimated minutes |
|---|---|---:|
| Required architecture, policy, migration, documentation, briefs | Full Unit category | 3 |
| Gate, rename endpoints, failed inspections, exact trailer recovery | GatedCommitServiceTests, all methods | 1 |
| Policy and live routing pin | CommitOnSettlePolicyTests; RoutingPinCandidateDispatchTests | 1 |
| Endpoint and complete spawned-child chain | AgentTaskCommitEndpointTests, all methods | 2 |
| Settle, audits, outbox failures and complete parent receipts | AgentTaskReplyIntegrationTests: C527_* plus a_shared_report_* and a_merge_conflict* | 4 |
| Worktree gate and existing merge contract | DelegationWorktreeTests: a_worktree_with_a_force_added*, a_clean_change_lands*, a_still_registered*, a_commit_all_failure* | 1 |
| Existing commit-all caller | CardReviewServiceIntegrationTests.CardPrApi_open_pushes_branch_and_creates_pr | 0.2 |
| CLI request and compatibility contracts | CommitOnSettleScriptTests; DelegateScriptKindTests | 1 |
| Reused outbox identity, receipts, recovery, attention and retention | AgentTaskLandNotificationPersistenceTests; AgentTaskLandNotificationRecoveryTests; AgentTaskLandReceiptTests; AgentTaskLandMonitoringTests; DispatchBaseNotificationTests; DataRetentionServiceTests; AttentionServiceTests | 5 |
| Ordinary report delivery preserved | OutputDistillationDeliveryTests | 3 |
| Settings modal V-54/R-12 | ProjectConfig.test.tsx through scripts/test-client.ps1 | 1 |
| R-17 nullable session fixture compatibility | TaskDrawer.test.tsx; TaskDetailBody.test.tsx through scripts/test-client.ps1 | 1 |
| R-17 serialized response contract | Antiphon.E2E/ContractSnapshotTests.Delegated_task_board_and_drawer_contracts | 2 |

Build each producer once into `bin-c527fix360/` and reuse --no-build. Rebuild after source repairs as necessary. Expected ordinary cost ~24 minutes plus compilation. No namespace/full-assembly integration run is authorized or needed. New tests remain within existing integration classes. All deliberate mutants stay pending for SourceLanding Mutation.

## Additional ordinary cases

- The specified V-50 matrix had only 15 of 24 combinations. Add all nine missing variants. AddCommitUpstreamBaseline_is_nullable_and_additive checks the new migration. Settlement_recovery_distinguishes_unborn_from_unavailable_history covers a new repository and failed HEAD/history inspection (3). C527_repeated_report_selects_this_settlements_notification proves an old matching report digest cannot replace the new obligation. Generated Commit-child goals and complete briefs now persist canonical LF, matching terminal delivery byte for byte.
- F1: Renamed_ignore_rule_endpoint_refuses_before_staging: scoped/whole x original/destination endpoint (4); Scoped_rename_selecting_either_endpoint_commits_both (2).
- F2: Unavailable_ignore_check_refuses_before_staging: error 128 / timeout result -1 (2); Unavailable_post_stage_inspection_restores_exact_prior_index: scoped/whole staged-path error and whole late-ignore error (3). C527_unavailable_child_audit_is_reported_to_parent: ignore/path/history/upstream failure (4).
- F3: C527_completion_outbox_recovers_complete_receipt: five boundaries (settlement-before-save, settlement-saved, before-enqueue, queue-inserted, receipt-before-save) x busy/eligible x parent-handoff/child-audit (20). V-30 now flushes and verifies complete parent receipt after a git-commit/save interruption.
- F4: Spawned_child_complete_brief_gated_operation_and_parent_receipt: ordinary / after-settlement-save interruption recovered by the hosted scanner (2).
- F5: C527_spawned_child_audit_compares_independent_upstream_baselines: unchanged ahead, unchanged behind, movement to old local HEAD, removed, added and changed-ref (6). Existing V-24 upstream case now asserts 'upstream moved' and 'child action is unproven'.
- F6: Settlement_recovery_requires_exact_unique_trailers: valid, body-only, wrong task, wrong gate, wrong identity, duplicate trailer (6). C527_partial_or_incidental_task_commit_does_not_hide_dirty_work: explicit partial / incidental GUID (2). C527_recovered_settlement_reports_new_dirty_work_without_committing_it (1).

## Pending positive controls

Original **PC-1..PC-31 remain pending**, including every parameter variant in the original design. No deliberate mutation was executed by Code. Update these old recipes to the repaired implementation:

- PC-23 removes independent upstream comparison; C527_commit_child_audit_flags_upstream_movement must fail the 'upstream moved' warning assertion.
- PC-27 omits RestoreIndexAsync after refusal; Whole_tree_recheck_catches_a_file_force_added_mid_run_and_resets_the_index must fail its empty-index assertion.
- PC-30 omits FindSettlementCommitsAsync recovery; C527_settle_save_failure_after_the_commit_recovers_without_a_second_commit must fail the committed header/complete receipt assertion.

Additional per-method controls (all pending, no broad red runs):

| ID | Compiling defect | Exact method / decisive assertion |
|---|---|---|
| PC-32 | Omit OldPath from the G1 endpoint list | GatedCommitServiceTests.Renamed_ignore_rule_endpoint_refuses_before_staging; IgnoreRulesChanged (4 variants, old-source variants decisive) |
| PC-33 | Ignore the failed initial CheckIgnored result | GatedCommitServiceTests.Unavailable_ignore_check_refuses_before_staging; CommitFailed, no add (2) |
| PC-34 | Treat failed StagedPaths inspection as successful empty results | GatedCommitServiceTests.Unavailable_post_stage_inspection_restores_exact_prior_index; CommitFailed (scoped/whole staged-error variants) |
| PC-35 | Omit index restore in RefuseAfterStageAsync | Same exact method as PC-34; cached binary diff equals prior index (all 3 variants); separate cycle |
| PC-36 | Omit TaskCompletion outbox insertion from the settlement transaction | AgentTaskReplyIntegrationTests.C527_completion_outbox_recovers_complete_receipt; exactly one durable notification (20) |
| PC-37 | Skip TaskCompletion rows in the hosted outbox scan | AgentTaskCommitEndpointTests.Spawned_child_complete_brief_gated_operation_and_parent_receipt; recovered queue/complete child and parent receipts (recovery variant) |
| PC-38 | Emit DoNotCommitLine for the created Commit child | Same chain method; full UserPrompt contains explicit commit authority and excludes DoNotCommitLine (2); separate cycle |
| PC-39 | Compare upstream SHA to CommitBaselineSha again | AgentTaskReplyIntegrationTests.C527_spawned_child_audit_compares_independent_upstream_baselines; no upstream warnings for unchanged divergent refs (ahead/behind) |
| PC-40 | Accept grep matches without validating the parsed unique trailer block | GatedCommitServiceTests.Settlement_recovery_requires_exact_unique_trailers; zero recovered matches for invalid variants (5 invalid + valid companion) |
| PC-41 | Suppress remaining-dirty-work warning on recovered settlement | AgentTaskReplyIntegrationTests.C527_recovered_settlement_reports_new_dirty_work_without_committing_it; complete prompt names both remaining paths |
| PC-42 | Treat unavailable child audit input as clear | AgentTaskReplyIntegrationTests.C527_unavailable_child_audit_is_reported_to_parent; complete prompt contains audit unavailable (ignore/path/history/upstream variants, mutate each seam separately) |
| PC-43 | Treat failed HEAD/history lookup as successful empty history | GatedCommitServiceTests.Settlement_recovery_distinguishes_unborn_from_unavailable_history; failed Succeeded assertion (HEAD/history error variants, each seam separately; unborn companion) |
| PC-44 | Select the oldest same-digest completion instead of this transaction's identity | AgentTaskReplyIntegrationTests.C527_repeated_report_selects_this_settlements_notification; new queue/complete prompt correlation fails |
| PC-45 | Omit LF normalization from BuildBrief | AgentTaskCommitEndpointTests.Spawned_child_complete_brief_gated_operation_and_parent_receipt; complete UserPrompt equals stored brief fails (both variants on Windows) |

Every red and restored green uses `/*/*/<Class>/<ExactMethod>`. Parameterized methods execute their named variants together; mutation evidence must identify the decisive variant. Missing-control discovery remains Mutation's responsibility.

## Evidence and disposition

Execution results, expanded per-class/per-method counts and V/R mapping will be added after ordinary verification. Native provider execution is not claimed: the queue tests use controlled adapters that persist submitted complete UserPrompts, while the endpoint and git operations are real. No live broker, production runner, land or deploy is used.
