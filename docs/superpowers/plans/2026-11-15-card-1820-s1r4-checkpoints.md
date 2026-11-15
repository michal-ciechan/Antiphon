# CARD-1820 S1R4 runtime inputs

Implements S1R4 and V-4/CP-5 from the [accepted plan](2026-11-12-card-1820-selector-v2-trustworthy-for-skipping-plan.md).
Standalone module only; main-selector/driver composition remains S1R6. Preserve the inherited
Full fence and all incompleteness reasons. Source-only inventory is not evaluated runtime proof.
Explicit declarations add bounded positive edges; they never silence unknown dynamic readers.
Every declaration names its consumer, source, kind, rationale and validating test method.
Expand each endpoint separately, then union for deleted/new glob members.

## Verification design

Commit the four V-4 witnesses against the old relation, run CP-5 red, then replace the adapter
with the runtime resolver. The brief's guard union is split by cost/platform: CP-101 contains
nine portable classes, the ordinary index class gets CP-102, and WindowsUnitPlatformAdmissionTests
gets six portable methods in CP-104 plus its pre-existing Windows-only method in CP-105.
CP-105 records an expected Linux skip, never Windows execution credit. CP-103 protects the adjacent project/relation contracts.
The full task selector also derives ReleaseImpactEnvelopeTests from the changed ordinary index;
CP-106 includes that class (eleven argument-expanded results, source-bound envelope/drift invariant).
TestLaneCategoryGuardTests is the matching impacted hand row. No whole Unit lane or assembly.
Tests use in-memory trees and source parser calls; no server, DB, Docker or process fixture.
The real declaration witness reads only repository files, never starts the declared host.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-5 | S1R4 | `tests/Antiphon.Tests -> bin-c1820-r4-green/` | linux-runtime | `/*/*/ReleaseImpactRuntimeTests/*` | V-4 | four named methods, 0 failed/skipped | 4 | 8 | true |
| CP-101 | S1R4 | CP-5 | final-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | Final guard union; impacted test-lane-category | all named classes, 0 failed/skipped | 78 | 3 | true |
| CP-102 | S1R4 | CP-5 | ordinary-index | `/*/*/DerivedImpactIndexTests/*` | R-1/R-3 ordinary index and brief guard | all named methods, 0 failed/skipped | 1 | 7 | true |
| CP-103 | S1R4 | CP-5 | adjacent-graph | `/*/*/(ReleaseImpactRelationTests*)\|(ReleaseImpactProjectTests*)/*` | V-2/V-3 adjacent regression | all named methods, 0 failed/skipped | 17 | 1 | true |
| CP-104 | S1R4 | CP-5 | platform-guards | `/*/*/WindowsUnitPlatformAdmissionTests/(C1210_Guard_matches_native_platform*)\|(C1210_Native_linux_entries_guard_before_work*)\|(C1210_Roster_closure_matches_source_gates*)\|(C1210_Audit_rejects_empty_or_incomplete_outcomes*)\|(C1210_Host_roster_matches_windows_shaped_observation*)\|(C1226_Pin_inspection_methods_skip_off_linux*)` | Final guard union portable methods | all six methods, 0 failed/skipped | 6 | 1 | true |
| CP-105 | S1R4 | CP-5 | native-pending | `/*/*/WindowsUnitPlatformAdmissionTests/C1210_Windows_linux_only_outcomes_are_exact` | Existing native obligation; no Windows credit | one named skip on Linux, 0 failed | 0 | 1 | true |
| CP-106 | S1R4 | CP-5 | envelope-regression | `/*/*/ReleaseImpactEnvelopeTests/*` | V-1/V-6D derived impact | all eleven results, 0 failed/skipped | 11 | 2 | true |

## Performance budget

23 minutes provisional including one cold build, each row/build capped at 10 minutes (`--row-timeout 10m`);
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

## Runtime declaration contract

Schema 1 requires declarations with unique IDs, one consumer project, assembly or exact
qualified class scope, source paths, repo-relative input paths/globs, kind, environmentNames,
rationale, and witness path/method. Unknown/duplicate JSON fields, unsafe paths, absent
consumers/readers/witnesses and unmatched inputs are incomplete. Class scope cannot narrow a
helper source outside that class's declaration paths. The real server factory and shared Grok
fixture are assembly-scoped and both are exercised by AppsettingsAndContentExpand.
Environment declarations enumerate names and immutable input paths but keep environment
identity unavailable; no environment values are read. Literal file reads add conservative
positives; computed paths, directory enumeration and host launch remain unresolved even when
a declaration adds known inputs. This module retains runtime-reader-census-unavailable and
every inherited graph reason. S1R6 must bind the full immutable trees and algorithm inputs;
this module cannot supply an authoritative complete release graph on its own.
Validate clean passing receipts for CP-5, CP-101..104, CP-106; inspect CP-105's fresh TRX and source
stamp separately because the green-only validator intentionally refuses skips. This existing
native method remains owed on Windows, outside portable S1R4 acceptance; no OS branch changed.

Red provenance: ebdefff01993cbaf30a02e5f7ee7cc40a1bed0b6, run 20261115-021256-db87,
CP-5 executed 4, passed 0, failed 4, skipped 0; all intended Shouldly assertions, clean source
and verified build. The preceding attempt 20261115-020608-4802 used a mistakenly unitless
timeout, rounded to one minute, and ended before tests (0 executions). Its receipt is retained;
explicit 10m restores the already planned limit. No assertion or planned timeout was relaxed.
