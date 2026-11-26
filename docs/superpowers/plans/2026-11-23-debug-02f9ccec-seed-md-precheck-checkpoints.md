# Debug 02f9ccec: seed-check markdown pre-check checkpoints

R1: the land-graph pre-check approves `server/Antiphon.Server.csproj`'s `Application\Review\Checks\Seed\**\*.md`
EmbeddedResource item and still refuses every wider or other document selection (the existing CARD-1390/1400/1409/1483
pre-check witnesses plus the new `ReviewSeedCheck*` cases). A seed change still builds at land
(`C1380_NonAllowlistedInputsBuild`).

R2: a seed change selects `ReviewCheckParserTests` through the `review-check-seeds` impacted-classes row, and the table
audit stays clean; that class still parses every embedded seed.

R3: the regenerated derived impact index and the nine Final guards stay green.

## Performance budget

Short tests only (operator standing rule, 2026-11-23). One Antiphon.Tests build is reused by every row. The pre-check row
runs 83 git-fixture cases (the 24-case subset ran in under a minute on this host); the whole `LandNonCodeSkipTests`
class and `LandGraphEvaluationTests` (13 min alone) are left to the rc qualification. Estimated build and tests are about
20 minutes plus slot admission.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| CP-1 | S1 | tests/Antiphon.Tests -> bin-d02f9/ | precheck | `/*/*/LandNonCodeSkipTests/(C1390*)\|(C1400*)\|(C1409*)\|(C1483*)\|(ReviewSeedCheck*)\|(C1380_NonAllowlistedInputsBuild*)` | R1 | all listed, 0 failed | 83 | 15 |
| CP-2 | S1 | CP-1 | selector | `/*/*/ImpactedClassesSelectorTests/(Review_seed_check*)\|(seed_kinds*)` | R2 | all listed, 0 failed | 2 | 2 |
| CP-3 | S1 | CP-1 | seed-parser | `/*/*/ReviewCheckParserTests/*` | R2 | all listed, 0 failed | 4 | 1 |
| CP-4 | S1 | CP-1 | derived-index | `/*/*/(DerivedImpactIndexTests*)\|(DerivedImpactIndexInvariantTests*)/*` | R3 | all listed, 0 failed | 27 | 4 |
| CP-5 | S1 | CP-1 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R3 | Final guards | 84 | 3 |
