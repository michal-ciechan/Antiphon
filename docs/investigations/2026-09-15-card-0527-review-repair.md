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

Ordinary implementation and verification are complete. Latest tested code and fixture SHA: `e0496abbb7604083e940a93c11ec772fbfe6359e`. Subsequent changes to this artifact record evidence only.

Every **V-1..V-55 and R-1..R-17 passed**; the table below maps every ID to its actual expanded cases and shared command. Full exact methods, filters, source SHAs, timestamps and TRX are in `C:\Antiphon\evidence\code-360e223e\verification.md`, `v-r-results.json`, and the named run directories. The two F7 Unit regressions both pass. V-50 now executes all 24 specified combinations.

Final build: `build-9.log`, 0 errors / 258 warnings; E2E `e2e-build-5.log`, 0 errors / 6 warnings. Producers used `bin-c527fix360/` and `bin-c527e2e360/`; the comparison checkout used `bin-c527base360/` and `bin-c527basee2e360/`.

| Coverage / run | Actual expanded cases | Latest outcome |
|---|---:|---|
| Full Unit (`unit-corrected`) | 2494 | 2489 pass, 4 inherited failures, 1 skip |
| Gate / policy / routing / endpoint / scripts (`classes-final`, endpoint rows superseded by `endpoint-corrected`) | 117 | 116 pass, 1 inherited failure |
| Reply (`reply-final`) | 69 | 69 pass |
| Worktree (`worktree`) | 4 | 4 pass |
| Card PR (`cardreview`) | 1 | 1 pass |
| Outbox / receipt / attention / retention (`outbox`) | 258 | 258 pass |
| Ordinary report delivery (`distillation`) | 78 | 78 pass |
| R-17 (`r17-verified`) | 1 | 1 pass |
| ProjectConfig client | 6 | 6 pass |
| Drawer client | 28 | Latest 27 pass, 1 inherited failure; raw initial 26/28 retained |

The combined class counts are Gate 34, Policy 12, Routing 12, Endpoint 12, CommitOnSettleScript 10 and DelegateScriptKind 37. Outbox counts are Persistence 9, Recovery 21, Receipt 30, Monitoring 25, DispatchBase 20, DataRetention 31 and Attention 122. Unit includes documentation 1, resolver 24, migration 3 and formatter 87. Full per-class counts are in the external verification report.

Latest outcomes retain **3056 expanded cases: 3049 pass, 6 inherited failures, 1 skip**. Counts preserve parameter variants even when their display names differ only by case. Rechecks replace only explicitly superseded cases; raw attempts remain available.

Inherited failures, each reproduced on pre-Code `b97abfd83819e2d1b705b4ed0527cfa8953b797f`:

- `TestClassificationGuardTests.Registry_matches_compiled_metadata`, `TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration`, and `TestClassificationPolicyTests.C487_G068`: missing lane on HerdrPaneDisposalEndpointTests.
- `ScopedVerificationInstructionTests.C487_G142`: obsolete next: mutation expectation.
- `DelegateScriptKindTests.WorktreeHealth_posts_and_prints_findings_without_pruning`: branch omitted from output.
- `TaskDetailBody` / `shows exact approval, source resolution, verification and legacy evidence`: duplicate Approved original label.

The no-session client case initially failed while builds were concurrent, then passed on the base and in an exact current-source rerun without concurrent builds. No source, timeout or assertion changed for that recheck. The skipped Unit case requires unavailable Windows symlink privilege.

R-17 initially found the fixture missing `session: null`; the same drift reproduced on the base. The client already accepts nullable session data. `r17-capture` regenerated the fixture from the real server (one pass; exactly one field added), and `r17-verified` compared the retained fixture to the real serialized response (one pass). Both required commit policy fields remain null on the legacy row. The new endpoint chain has a separate database per variant so independent endpoint fixtures cannot consume its six-task dispatch capacity; all 12 endpoint cases pass after that correction.

All 52 producer-owned output directories and the owned comparison checkout were removed; see `cleanup-inventory.json` and `cleanup.json`. Every owned build/test command finished. The original Code task remains landing owner; `final-state.json` records both branch references and source checkouts after advancement to this repair. No land or deploy was performed. Restart after eventual landing: **server**.

### Pending qualification and coverage gaps

All **PC-1..PC-45**, including every documented variant, remain pending for post-land SourceLanding Mutation. No deliberate mutant was executed. Mutation owns missing-control discovery.

Native provider execution is not claimed: queue tests use controlled adapters that persist submitted complete UserPrompts, while HTTP, git, dispatcher, queue and persistence are real. Long inbox spill-to-file TaskCompletion receipts and post-commit metadata inspection failures are not qualified by these new cases. Commit-child auditing without a local HEAD baseline is also outside the added matrix; the existing guard skips absent CommitBaselineSha. These limits must remain visible in ordinary Review and the companion verification obligation.

Duration checks are retained externally. Cold database startup is charged to the first reply case (about 20 seconds, shifting between first methods across runs); the existing merge-conflict case was about six seconds. Outbox classes took 6m19s; ordinary report delivery took 4m10s. No timeout, assertion or slow-test allowlist was loosened.

### Every V/R ID: executed outcome

| ID | Outcome | Expanded cases | Shared command |
|---|---|---:|---|
| V-1 | Passed | 1 | reply-final |
| V-2 | Passed | 1 | reply-final |
| V-3 | Passed | 1 | classes-final |
| V-4 | Passed | 1 | classes-final |
| V-5 | Passed | 3 | classes-final |
| V-6 | Passed | 1 | classes-final |
| V-7 | Passed | 1 | classes-final |
| V-8 | Passed | 1 | classes-final |
| V-9 | Passed | 1 | classes-final |
| V-10 | Passed | 1 | classes-final |
| V-11 | Passed | 1 | classes-final |
| V-12 | Passed | 2 | reply-final |
| V-13 | Passed | 1 | reply-final |
| V-14 | Passed | 1 | reply-final |
| V-15 | Passed | 1 | classes-final |
| V-16 | Passed | 1 | reply-final |
| V-17 | Passed | 1 | reply-final |
| V-18 | Passed | 1 | reply-final |
| V-19 | Passed | 1 | reply-final |
| V-20 | Passed | 1 | reply-final |
| V-21 | Passed | 1 | reply-final |
| V-22 | Passed | 1 | reply-final |
| V-23 | Passed | 1 | reply-final |
| V-24 | Passed | 3 | reply-final |
| V-25 | Passed | 1 | reply-final |
| V-26 | Passed | 1 | reply-final |
| V-27 | Passed | 1 | reply-final |
| V-28 | Passed | 1 | reply-final |
| V-29 | Passed | 1 | reply-final |
| V-30 | Passed | 1 | reply-final |
| V-31 | Passed | 1 | endpoint-corrected |
| V-32 | Passed | 4 | classes-final |
| V-33 | Passed | 1 | endpoint-corrected |
| V-34 | Passed | 1 | endpoint-corrected |
| V-35 | Passed | 1 | endpoint-corrected |
| V-36 | Passed | 1 | endpoint-corrected |
| V-37 | Passed | 1 | endpoint-corrected |
| V-38 | Passed | 1 | endpoint-corrected |
| V-39 | Passed | 2 | endpoint-corrected |
| V-40 | Passed | 1 | endpoint-corrected |
| V-41 | Passed | 1 | classes-final |
| V-42 | Passed | 3 | classes-final |
| V-43 | Passed | 1 | classes-final |
| V-44 | Passed | 4 | unit-corrected |
| V-45 | Passed | 2 | classes-final |
| V-46 | Passed | 1 | classes-final |
| V-47 | Passed | 1 | worktree |
| V-48 | Passed | 1 | classes-final |
| V-49 | Passed | 5 | classes-final |
| V-50 | Passed | 24 | unit-corrected |
| V-51 | Passed | 1 | classes-final |
| V-52 | Passed | 1 | classes-final |
| V-53 | Passed | 2 | unit-corrected |
| V-54 | Passed | 1 | client |
| V-55 | Passed | 1 | unit-corrected |
| R-1 | Passed | 2 | reply-final |
| R-2 | Passed | 1 | reply-final |
| R-3 | Passed | 1 | reply-final |
| R-4 | Passed | 6 | reply-final |
| R-5 | Passed | 1 | worktree |
| R-6 | Passed | 1 | reply-final |
| R-7 | Passed | 1 | reply-final |
| R-8 | Passed | 1 | classes-final |
| R-9 | Passed | 1 | classes-final |
| R-10 | Passed | 1 | classes-final |
| R-11 | Passed | 1 | unit-corrected |
| R-12 | Passed | 1 | client |
| R-13 | Passed | 1 | classes-final |
| R-14 | Passed | 1 | worktree |
| R-15 | Passed | 1 | worktree |
| R-16 | Passed | 1 | cardreview |
| R-17 | Passed | 1 | r17-verified |
