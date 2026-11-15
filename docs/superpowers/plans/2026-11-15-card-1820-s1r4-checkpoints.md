# CARD-1820 S1R4 runtime inputs

Implements S1R4 and V-4/CP-5 from the [accepted plan](2026-11-12-card-1820-selector-v2-trustworthy-for-skipping-plan.md).
Standalone module only; main-selector/driver composition remains S1R6. Preserve the inherited
Full fence and all incompleteness reasons. Source-only inventory is not evaluated runtime proof.
Explicit declarations add bounded positive edges; they never silence unknown dynamic readers.
Every declaration names its consumer, source, kind, rationale and validating test method.
Expand each endpoint separately, then union for deleted/new glob members.

## Verification design

Commit the four V-4 witnesses against the old relation, run CP-5 red, then replace the adapter
with the runtime resolver. CP-101 is the brief's exact guard union split by cost: the slow
ordinary index class gets CP-102. CP-103 protects the adjacent project/relation contracts.
TestLaneCategoryGuardTests is the matching impacted hand row. No whole Unit lane or assembly.
Tests use in-memory trees and source parser calls; no server, DB, Docker or process fixture.
The real declaration witness reads only repository files, never starts the declared host.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-5 | S1R4 | `tests/Antiphon.Tests -> bin-c1820-r4-red/` | linux-runtime | `/*/*/ReleaseImpactRuntimeTests/*` | V-4 | four named methods, 0 failed/skipped for green | 4 | 8 | true |
| CP-101 | S1R4 | CP-5 | final-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | Final guard union; impacted test-lane-category | all named classes, 0 failed/skipped | 79 | 3 | true |
| CP-102 | S1R4 | CP-5 | ordinary-index | `/*/*/DerivedImpactIndexTests/*` | R-1/R-3 ordinary index and brief guard | all named methods, 0 failed/skipped | 1 | 7 | true |
| CP-103 | S1R4 | CP-5 | adjacent-graph | `/*/*/(ReleaseImpactRelationTests*)\|(ReleaseImpactProjectTests*)/*` | V-2/V-3 adjacent regression | all named methods, 0 failed/skipped | 17 | 1 | true |

## Performance budget

19 minutes provisional including one cold build, each row/build capped at 10 minutes;
45-minute finite admission is separate. Red runs CP-5 alone; green uses a fresh output root
and runs all rows serially. No repetition after green. Bootstrap checkpoint tool and ordinary
index driver builds are necessary, declared tooling preparation through build-slot in native
outputs with UseAppHost=false. Build the driver before two -NoBuild regenerations, require
second unchanged, commit generated index separately. No manual alternate output build.
Retain fresh TRX, phases, source stamps, slot/wait and /proc/loadavg in ignored evidence.
No comparable p95 yet; collect normal observations only under CARD-1820/CARD-1404.
Any run over 30 minutes needs caller-commissioned Debug. OS-specific behavior and migrations
are not changed; perform OS declaration lint, pin recomputation, evidence-diff and merge-tree.
No PC IDs exist in the accepted plan. All V-4 control variants (helper, hook, config/content,
unresolved read and declaration validation) remain pending for SourceLanding Mutation.
Other parent V/R rows belong to their named slices; none receives credit from S1R4.
