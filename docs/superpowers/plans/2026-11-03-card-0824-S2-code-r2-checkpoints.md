# CARD-0824 S2 Code round 2: checkpoint amendment

Code task 9ac68027, 2026-11-03, server2 (Linux). This file amends only the `### Checkpoints` table of the
[S2 test design](2026-11-02-card-0824-S2-test-design.md) and the
[round 1 amendment](2026-11-03-card-0824-S2-code-checkpoints.md) for the round 2 Code runs (Review 370083f1 test
gaps and advisories A-1..A-4). The design itself is unchanged.

- CP-12's executed floor is 52, not 43. It adds nine cases:
  - `C0824_dependent_rows(owner-reopened)` (owner_open);
  - `C0824_dependent_rows(pending-land-request-owner)` (A-3);
  - `C0824_kill_switch_mid_run_stops_before_next_row`;
  - `C0824_repository_busy_defers_without_git`;
  - `C0824_second_runner_on_branch_is_asked_and_its_refusal_defers` with `short`, `full`, `tracking` and
    `remote-tracking` (second runner and A-1);
  - `C0824_dependent_opened_during_runner_step_defers_before_push` (A-2).
- CP-16's floor is 12, not 11: it adds `C0824_missing_repository_refuses_unavailable` (A-4).
- CP-17 (V-43) and CP-18 (V-44) are the design's rows. Both classes are `[NativeLinuxOnly]` and reuse CP-12's build.
- CP-15, CP-19, CP-20, CP-21 and CP-22 are copied from the round 1 amendment. CP-19 keeps its one Windows-only skip
  by design: `C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance(locked, False)`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-12 | S2 | tests/Antiphon.Tests -> bin-c0824-s2/ | portable-branch-delete | /*/Antiphon.Tests.Application/C0824PublishedBranchDeletionTests/* | V-14..V-19, V-38, V-39, V-41, V-42, V-45..V-49, review 370083f1 gaps 1-4, A-1..A-3 | class, 0 failed/skipped | 52 | 15 | true |
| CP-15 | S2 | CP-12 | portable-retention-regression | /*/Antiphon.Tests.Application/(AgentTaskLandRemoteSourceRetention*)/* | R-1 | 4 existing classes, 0 failed/skipped | 32 | 4 | true |
| CP-17 | S2 | CP-12 | linux-native-recycle-audit | /*/Antiphon.Tests.Scripts/C0824BranchDeletionRecycleAuditTests/* | V-43 | class, 0 failed/skipped | 3 | 3 | true |
| CP-18 | S2 | CP-12 | linux-native-branch-refs-delivery | /*/Antiphon.Tests.Application/C0824BranchRefsDeliveryTests/* | V-44 | class, 0 failed/skipped | 4 | 3 | true |
| CP-19 | S2 | CP-12 | portable-publication-regression | /*/Antiphon.Tests.Application/AgentTaskLandPublicationTests/* | R-2 | existing class, 0 failed/skipped | 26 | 4 | true |
| CP-21 | S2 | CP-12 | portable-dispatcher-ops-regression | /*/Antiphon.Tests.Agents/PhoneHomeConnectionTests/Supported_operations_preserve_contracts | dispatcher handles every PhoneHomeOperation | method, 0 failed/skipped | 1 | 2 | true |
| CP-22 | S2 | CP-12 | portable-migration-regression | /*/Antiphon.Tests.Application/AgentTaskInternalDecisionMigrationTests/* | no pending model change after Card0824BranchDeletion | existing class, 0 failed/skipped | 1 | 3 | true |
| CP-16 | S2 | tests/Antiphon.SessionRunner.Tests -> bin-c0824-s2r/ | portable-runner-refs | /*/Antiphon.SessionRunner.Tests/C0824RunnerBranchRefsTests/* | V-40, A-4 | class, 0 failed/skipped | 12 | 4 | true |
| CP-20 | S2 | CP-16 | portable-binding-regression | /*/Antiphon.SessionRunner.Tests/RunnerWorkspaceBindingTests/* | R-3 | existing class, 0 failed/skipped | 11 | 2 | true |
