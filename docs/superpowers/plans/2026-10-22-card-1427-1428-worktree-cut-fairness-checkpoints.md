# CARD-1427 + CARD-1428: checkpoint table for the checkpoint tool (Code task 3b2c2936)

Companion to [the plan](2026-10-22-card-1427-1428-worktree-cut-fairness-plan.md). The plan's own
`### Checkpoints` table carries an `Environment` column whose cells (`lane: server2 (default)`) are not
`NAME=value`, which the checkpoint importer refuses (CARD-0723). The rows below are the plan's rows
unchanged, without that column; the lane is the runner default (server2) read from
`GET /api/runner-defaults`, not an embedded location. Run with `--plan <this file>`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1b | `tests/Antiphon.Tests -> bin-c1427/` | waiters-unit | `/*/*/RepositoryLeaseWaitersTests/*` | V-1, V-2, V-8 | all passed (5 existing + 2 new = 7), 0 failed/skipped | 7 | 3 |
| CP-2 | S1b | CP-1 | lease-turn-scenario | `/*/*/LeaseTurnScenarioTests/C1427_back_to_back_lands_give_a_waiting_cut_its_turn` | V-3 | 1 passed, 0 failed/skipped | 1 | 2 |
| CP-3 | S2b | `tests/Antiphon.Tests -> bin-c1428/` | inspection-results-unit | `/*/*/SiblingInspectionResultsTests/*` | V-5, V-6 | 3 passed, 0 failed/skipped | 3 | 3 |
| CP-4 | S2b | CP-3 | dispatcher-sibling-lease | `/*/*/DispatcherSiblingInspectionSchedulingTests/*` | V-4, V-7, V-8 | all passed (existing CARD-1349 cases + 3 new), 0 failed/skipped | 3 | 8 |
| CP-5 | S2b | CP-3 | land-yield-regression | `/*/*/AgentTaskLandDispatchYieldTests/*` | V-8 (D-7, D-8 refactor) | all passed (existing CARD-0672 cases), 0 failed/skipped | 6 | 8 |
