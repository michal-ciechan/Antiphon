# CARD-1531 pending SourceLanding Mutation candidate

Original Code landing owner: 9241a2cb-d234-4803-af31-6fff87e55b12.
Plan: ../plans/2026-10-23-card-1531-checkpoint-admission-budget-plan.md.
Bind the source after ordinary Review and confirmed landing. No deliberate mutant
was executed in Code. All controls below are **pending**, including variants.
Use one exact `/*/*/Class/Method` filter per red/restore/green cycle; no class runs.

| PC | Deliberate defect | Exact witness method (class prefix omitted only for C1531 methods) |
|---|---|---|
| PC-1 | Remove queue-only pause | CheckpointAdmissionBudgetTests.C1531_wait_preserves_execution_budget |
| PC-2 | Charge ready-row queue time | CheckpointAdmissionBudgetTests.C1531_ready_row_waits_then_executes |
| PC-3 | Sum simultaneous waits | CheckpointAdmissionBudgetTests.C1531_overlapping_waits_receive_union_credit |
| PC-4 | Pause while any sibling waits | CheckpointAdmissionBudgetTests.C1531_running_sibling_keeps_charging |
| PC-5a | Remove admission cancellation | CheckpointAdmissionBudgetTests.C1531_stuck_admission_is_bounded |
| PC-5b | Return timeout before concurrent grant release joins; omit release | Same exact method; late-grant variant |
| PC-6 | Remove independent wall timer | CheckpointAdmissionBudgetTests.C1531_hard_wall_bounds_successive_waits |
| PC-7a | Extend row/total limits | CheckpointAdmissionBudgetTests.C1531_execution_overrun_keeps_old_limits |
| PC-7b | Collapse build deadline to compiler failure | RunSchedulerTests.build_timer_uses_selected_row_deadlines_and_override |
| PC-7c | Reclassify genuine compiler failure as timeout | RunSchedulerTests.failed_build_fails_only_its_rows |
| PC-8a | Erase partial canceled wait | CheckpointAdmissionBudgetTests.C1531_cancellation_joins_and_keeps_partial_wait |
| PC-8b | Publish caller terminal receipt before join | C1307CliCancellationTests.Executor_cancel_persists_130 |
| PC-8c | Lose bound-owner cancellation/join | CheckpointTaskOwnershipTests.settlement_cancels_two_drivers_and_skips_later_rows |
| PC-9 | Give baseline a fresh budget | CheckpointAdmissionBudgetTests.C1531_baseline_shares_remaining_budget |
| PC-10a | Ignore serialized bound; ignore configured acquisition bound | CheckpointAdmissionContractTests.C1531_inputs_resolve_without_disabling_bounds |
| PC-10b | Remove pending-HTTP acquisition timer; invalid CLI/YAML scalar accepted | Same exact method; independent variants |
| PC-11a | Zero partial timing or replace test count with elapsed seconds | CheckpointAdmissionContractTests.C1531_receipts_keep_wait_execution_and_counts |
| PC-11b | Reclassify actual assertion failure as timeout | Same exact method; real failed-TRX variant |
| PC-12 | Default missing timing to zero; erase failed history/cleanup refusal | CheckpointAdmissionContractTests.C1531_legacy_receipts_remain_unknown_not_zero |

Mutation owns missing-control discovery and reports any inadequacy without
repairing production/test source in a SourceLanding snapshot. No server/runner
restart is needed: subsequent invocations use the newly built shadow copy.
