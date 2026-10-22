# CARD-1446 plan amendment: milestone 2 checkpoints (S4, S5)

Code stage, 2026-10-22, task `36786bbc`, branch `feat/card-task-36786bbc`. This file carries only the
`### Checkpoints` table for CP-7..CP-16 of
[2026-10-22-card-1446-stale-landings-plan.md](2026-10-22-card-1446-stale-landings-plan.md), which is
otherwise unchanged (that file is already on master and on the Plan branch; editing it here would be an
add/add conflict at landing). It is the `--plan` the checkpoint tool runs for this milestone.

## Filter rewrites

TUnit's `TreeNodeFilter` rejects a `/` inside an OR operand and `(ClassA)|(ClassB)/M*` runs 0 tests
(milestone 1 report, task `10e48480`, CP-4). The working form is `(ClassA*)|(ClassB*)/M*`:

| CP | Plan filter | Rewritten filter | Note |
|---|---|---|---|
| CP-12 | `/*/*/(WorktreeResiduePublicationLaneTests/*)\|(WorktreeLandingCleanupRetryTests/C1446*)` | `/*/*/(WorktreeResiduePublicationLaneTests*)\|(WorktreeLandingCleanupRetryTests*)/C1446*` | every `WorktreeResiduePublicationLaneTests` method is named `C1446_*`, so the shared method segment loses nothing |
| CP-13 | `/*/*/(WorktreeLandingCleanupRetryTests/C459*)\|(WorktreeResidueSweepTests/*)\|(WorktreeResidueRecoveryTests/*)` | `/*/*/(WorktreeLandingCleanupRetryTests*)\|(WorktreeResidueSweepTests*)\|(WorktreeResidueRecoveryTests*)/*` | a superset: the two `C1446` retry methods run again beside the `C459*` rows |
| CP-16 | `/*/*/(WorktreeResidueRegistrationTests/*)\|(WorktreeRetirementRaceTests/*)\|(CardWorktreeMirrorCleanupTests/*)` | `/*/*/(WorktreeResidueRegistrationTests*)\|(WorktreeRetirementRaceTests*)\|(CardWorktreeMirrorCleanupTests*)/*` | same shape, no roster change |

RV-1 is the plan's D-3 conditional row (`AntiphonRefProtection` exists on master): the landing clause gains
`Cleanup != Abandoned` in S4c, with two new `C1432_protection_rule_rows` cases; it reuses CP-12's build.

### Checkpoints

Lane: server2 (D-7). Build output `bin-c1446-*/` (forward slash). Red rows are run at the named test
commit, before the implementation commit exists.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-7 | S4a-test | `tests/Antiphon.Tests -> bin-c1446-s4ared/` | s4a-red | `/*/*/FileSystemEvidenceRetentionTests/C1446_copies_and_verifies` | V-6 witness | **red**: 1 executed, 1 failed (`evidence_retention_unavailable`) | 1 | 7 |
| CP-8 | S4a-impl | `tests/Antiphon.Tests -> bin-c1446-s4a/` | s4a-green | `/*/*/FileSystemEvidenceRetentionTests/*` | V-6 | all listed, 0 failed/skipped | 4 | 8 |
| CP-9 | S4b-test | `tests/Antiphon.Tests -> bin-c1446-s4bred/` | s4b-red | `/*/*/WorktreeIgnoredContentClassifierTests/C1446_unlisted_antiphon_spill_is_evidence_not_protected` | V-7 witness | **red**: 1 executed, 1 failed (spill classified Protected) | 1 | 7 |
| CP-10 | S4b-impl | `tests/Antiphon.Tests -> bin-c1446-s4b/` | s4b-green | `/*/*/WorktreeIgnoredContentClassifierTests/*` | V-7 | all listed, 0 failed/skipped | 6 | 8 |
| CP-11 | S4c-test | `tests/Antiphon.Tests -> bin-c1446-s4cred/` | s4c-red | `/*/*/WorktreeResiduePublicationLaneTests/C1446_policy_rows` | V-8 witness | **red**: 7 executed, at least 2 failed (empty policy always returns `Queue`) | 4 | 7 |
| CP-12 | S4c-impl | `tests/Antiphon.Tests -> bin-c1446-s4c/` | s4c-green | `/*/*/(WorktreeResiduePublicationLaneTests*)\|(WorktreeLandingCleanupRetryTests*)/C1446*` | V-8, V-9, V-10 | all listed, 0 failed/skipped | 7 | 10 |
| CP-13 | S4c-impl | CP-12 | s4c-regress | `/*/*/(WorktreeLandingCleanupRetryTests*)\|(WorktreeResidueSweepTests*)\|(WorktreeResidueRecoveryTests*)/*` | V-13 | all listed, 0 failed/skipped | 30 | 12 |
| RV-1 | S4c-impl | CP-12 | s4c-refs | `/*/*/LandRefRetentionTests/C1432_protection_rule_rows` | D-3 conditional (unlisted in the plan) | all listed, 0 failed/skipped | 34 | 5 |
| CP-14 | S5-test | `tests/Antiphon.Tests -> bin-c1446-s5red/` | s5-red | `/*/*/WorktreeResidueCandidateRowTests/C1446_unchanged_held_rows_are_not_rewritten` | V-11 witness | **red**: 1 executed, 1 failed (second run writes N rows) | 1 | 7 |
| CP-15 | S5-impl | `tests/Antiphon.Tests -> bin-c1446-s5/` | s5-green | `/*/*/WorktreeResidueCandidateRowTests/*` | V-11, V-12 | all listed, 0 failed/skipped | 3 | 8 |
| CP-16 | S5-impl | CP-15 | s5-regress | `/*/*/(WorktreeResidueRegistrationTests*)\|(WorktreeRetirementRaceTests*)\|(CardWorktreeMirrorCleanupTests*)/*` | V-13 | all listed, 0 failed/skipped | 20 | 12 |
