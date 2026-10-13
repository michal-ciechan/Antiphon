# CARD-1213 S1 repair (F1, F2) checkpoints

Companion to [the test design](2026-10-13-card-1213-test-design.md). Review 6f91022a returned S1 at
`dc9084bebadbb096fc2f8ea516d4d86e7f84c1c5` with two findings:

- F1: `DelegateBranchDeliveryTests` is marked Slow but missing from
  `tests/Antiphon.Tests/slow-tests-allowlist.txt`. The existing compiled-metadata guards must go
  red before the registry entry and green after it.
- F2: `server/Bundles/orchestrator.md` and `docs/orchestration-loop.md` promised a
  `feat/card-task-<id>` branch for `-StartRef` and retired follow-up children. Editing the bundle
  changes its approved-bytes pin, so the bundle hash, size and composition-budget tests and the
  repair-route documentation tests that read the edited paragraphs are rerun.

The S1 ordinary rows CP-1..CP-8 are run from the test design (`--after S1`), with CP-4 as
amended there to exclude the inherited-red C1017_TerminalTaskRequired by exact name. The rows
below are the only additions. Build each row to its own isolated OutputPath; remove only the
outputs these runs create.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-R1 | F1 | `tests/Antiphon.Tests -> bin-c1213-r1/` | registry | `/*/*/(TestClassificationGuardTests*)\|(TestClassificationPolicyTests*)/(Registry_matches_compiled_metadata*)\|(C487_G059*)` | F1 | 2 executed; red before the registry entry, green after | 2 | 4 | true |
| CP-R2 | F2 | `tests/Antiphon.Tests -> bin-c1213-r2/` | bundle | `/*/*/(CheckpointRepeatDocumentationTests*)\|(InstructionBundleTests*)\|(RepairSourceDocumentationTests*)/(repeat_budget_reaches_code_briefs_without_bundle_growth*)\|(orchestrator_bundle_points_to_operational_autonomy_without_growing*)\|(the_worst_case_composition_measured_sits_far_under_the_budget*)\|(C603_*)\|(C675_*)\|(C499_V31_*)` | F2 | 7 executed; 0 failed; 0 skipped | 7 | 4 | true |
