# CARD-1820 S1R2 implementation checkpoint scope

Selected S1R2, the first unimplemented slice in dependency order: S1R1 is its only prerequisite.
S1R6 (including Review A-1) requires S1R3-S1R5 and is deferred; A-3 cleanup travels with that later witness.
Upstream plan: `origin/feat/card-task-bcf16391` at `950f541ec082e94651b11a9a89b7aef412355b7c`,
`docs/superpowers/plans/2026-11-12-card-1820-selector-v2-trustworthy-for-skipping-plan.md`.

Implement the separate uncapped, project-qualified declaration/relation artifact and explicit driver mode.
No release CLI composition or completeness admission; project evaluation/runtime/script closure remains unknown/Full.
Ordinary builder and selector behavior stay unchanged. Native Windows proof remains orchestrator-owned.
V-2 asserts the 26th consumer, global build inputs, duplicate qualified names/partial declarations, and real generic/nested declarations excluding raw-string pseudo-classes.
The initial red adapter calls the existing ordinary builder. Replace only that adapter with the release builder for green.

## Verification design

CP-2/CP-3 are the upstream slice's closed rows. CP-101 batches the required Final guards;
CP-102 verifies regenerated ordinary-index drift; CP-103 verifies spawn census (the red adapter launches Git).
No csproj/limiter owner changes are intended; no migration, delivery, landing, lease or persistence behavior changes.
After green, run only newly affected required rows, never repeat unchanged green selections.
Every run binds committed HEAD with `--expected-source-sha`. Inspect fresh TRX identities and counts.
No manual acceptance or PC-n variants are declared by this slice; deliberate controls remain pending for SourceLanding Mutation under the operator waiver.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-2 | S1R2 | `tests/Antiphon.Tests -> bin-c1820-r2/` | linux-relation | `/*/*/ReleaseImpactRelationTests/*` | V-2 | all 4 named, 0 failed/skipped | 4 | 10 | true |
| CP-3 | S1R2 | CP-2 | linux-ordinary-cap | `/*/*/DerivedImpactIndexTests/(a_path_with_26_classes_is_capped*)\|(solution_file_selects_no_derived_class*)` | R-1 | both methods, 0 failed/skipped | 2 | 1 | true |
| CP-101 | S1R2 | CP-2 | final-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | Final guards and matched test-lane-category | all listed, 0 failed/skipped | 78 | 3 | true |
| CP-102 | S1R2 | CP-2 | ordinary-index-drift | `/*/*/DerivedImpactIndexTests/committed_index_matches_the_builder` | R-3 drift | named method, 0 failed/skipped | 1 | 1 | true |
| CP-103 | S1R2 | CP-2 | spawn-census | `/*/*/ProcessSpawnLimitTests/*` | spawn-limit-rosters | all listed, 0 failed/skipped | 57 | 2 | true |

## Performance budget

17 minutes provisional including one cold build; test body/relation setup in memory after the red witness.
Use the upstream 20-minute explicit build/row execution bound, finite 45-minute slot acquisition,
and separate queue/admission from execution. No timeout or assertion is widened.
No whole Unit lane or full assembly. No database/container/server/migration/snapshot phases apply.
Keep TRX, phase journal, build/row/slot costs and unedited lines in ignored checkpoint outputs;
retain concise actual outcomes/provenance in the Code report. Unknown phase attribution stays unknown/remainder.
The first five comparable Code/Review/qualification samples under CARD-1820/CARD-1404 may establish p95;
do not repeat passing tests for timing. An observed run over 30 minutes requires caller-commissioned Debug.
The generator and checkpoint tool bootstrap builds are necessary preparation outside these test rows,
use native default output and the build-slot gate, and are separately reported. The release driver mode
is manually probed through the gate using the built driver and retains its ignored artifact.
