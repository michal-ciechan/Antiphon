# CARD-0824 S2 Code: checkpoint amendment

Code task 9ee6f8ca, 2026-11-03, server2 (Linux). This file amends only the `### Checkpoints` table of the
[S2 test design](2026-11-02-card-0824-S2-test-design.md) for the Code stage's runs; the design itself is
unchanged. CP-12's executed floor is 42, not 40: Code adds `C0824_dry_run_never_deletes` (the brief's
"DryRun never deletes") and `C0824_target_rewritten_after_land_keeps_branch` (V-49, the brief's "confirmed
at deletion time, never from a cached state": origin's target must still contain `VerifiedSourceSha`).
CP-21 and CP-22 are regression rows for surfaces this slice changed outside the design's classes: the
dispatcher's every-operation contract enumeration (one new phone-home operation) and the EF pending-model
check (five new `AgentTaskLandings` columns). CP-15, CP-16, CP-19 and CP-20 are copied verbatim.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-12 | S2 | tests/Antiphon.Tests -> bin-c0824-s2/ | portable-branch-delete | /*/Antiphon.Tests.Application/C0824PublishedBranchDeletionTests/* | V-14..V-19, V-38, V-39, V-41, V-42, V-45..V-49 | class, 0 failed/skipped | 42 | 13 | true |
| CP-15 | S2 | CP-12 | portable-retention-regression | /*/Antiphon.Tests.Application/(AgentTaskLandRemoteSourceRetention*)/* | R-1 | 4 existing classes, 0 failed/skipped | 32 | 4 | true |
| CP-19 | S2 | CP-12 | portable-publication-regression | /*/Antiphon.Tests.Application/AgentTaskLandPublicationTests/* | R-2 | existing class, 0 failed/skipped | 26 | 4 | true |
| CP-21 | S2 | CP-12 | portable-dispatcher-ops-regression | /*/Antiphon.Tests.Agents/PhoneHomeConnectionTests/Supported_operations_preserve_contracts | dispatcher handles every PhoneHomeOperation | method, 0 failed/skipped | 1 | 2 | true |
| CP-22 | S2 | CP-12 | portable-migration-regression | /*/Antiphon.Tests.Application/AgentTaskInternalDecisionMigrationTests/* | no pending model change after Card0824BranchDeletion | existing class, 0 failed/skipped | 1 | 3 | true |
| CP-16 | S2 | tests/Antiphon.SessionRunner.Tests -> bin-c0824-s2r/ | portable-runner-refs | /*/Antiphon.SessionRunner.Tests/C0824RunnerBranchRefsTests/* | V-40 | class, 0 failed/skipped | 11 | 4 | true |
| CP-20 | S2 | CP-16 | portable-binding-regression | /*/Antiphon.SessionRunner.Tests/RunnerWorkspaceBindingTests/* | R-3 | existing class, 0 failed/skipped | 11 | 2 | true |
