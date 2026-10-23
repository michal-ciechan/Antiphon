# CARD-1531 unbound SourceLanding Mutation candidate

Original Code/landing owner: `6506e5e2`. This is an unbound candidate; ordinary
Code verification does not discharge a positive control. Review the final source
and bind a method-scoped pack only after the caller lands the original Code task.
All controls and variants below are **pending**, with no deliberate mutant run in Code.

Plan: [admission budget](../plans/2026-10-23-card-1531-checkpoint-admission-budget-plan.md).
Portable fake-clock lane; no native Windows qualification or service restart.
Each filter is `/*/*/<Class>/<ExactMethod>` using the class/method below.
Never widen a timeout, loosen an assertion, run a whole class for a mutant, or
interpret a build/fixture error or zero selection as an assertion red.

| PC / variants (all pending) | Independent defect | Class.method |
|---|---|---|
| PC-1a | Remove admission pause for first build | CheckpointAdmissionBudgetTests.C1531_wait_preserves_execution_budget |
| PC-1b | Charge a ready row's admission against execution | CheckpointAdmissionBudgetTests.C1531_ready_row_waits_then_executes |
| PC-2 | Sum overlapping waits; credit preflight/settlement | CheckpointAdmissionBudgetTests.C1531_overlapping_waits_receive_union_credit |
| PC-3 | Pause whenever any sibling is queued | CheckpointAdmissionBudgetTests.C1531_running_sibling_keeps_charging |
| PC-4a/b/c | Remove admission timer; fall back unleased at expiry; leak boundary grant/holder | CheckpointAdmissionBudgetTests.C1531_stuck_admission_is_bounded |
| PC-5 | Remove independent hard wall | CheckpointAdmissionBudgetTests.C1531_hard_wall_bounds_successive_waits |
| PC-6a/b | Extend row/build local limit (separate variants); collapse build expiry to compiler failure | CheckpointAdmissionBudgetTests.C1531_execution_overrun_keeps_old_limits |
| PC-7a/b | Publish before joining; discard partial canceled wait | CheckpointAdmissionBudgetTests.C1531_cancellation_joins_and_keeps_partial_wait |
| PC-8a/b | Reset baseline budget; grant cleanup a fresh minute | CheckpointAdmissionBudgetTests.C1531_baseline_shares_remaining_budget |
| PC-9a/b/c | Ignore configured client admission limit; ignore explicit wall/CLI override; accept invalid CLI/YAML bound (separate inputs) | CheckpointAdmissionContractTests.C1531_inputs_resolve_without_disabling_bounds |
| PC-10a/b | Zero timing; substitute duration for executed test count | CheckpointAdmissionContractTests.C1531_receipts_keep_wait_execution_and_counts |
| PC-11a/b | Default legacy unknown timing to zero; erase failed/refused attempt (separate variants) | CheckpointAdmissionContractTests.C1531_legacy_receipts_remain_unknown_not_zero |

Mutation owns deliberate red/restore/green cycles and missing-control discovery.
A missing assertion is a finding for a separately commissioned Code repair; it is
never a reason to weaken qualification or a cleanup refusal. Preserve the original
red and failed/refused receipts; do not rewrite them after restoring source.
