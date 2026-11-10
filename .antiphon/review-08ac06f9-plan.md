# Review 08ac06f9 checkpoint extract (CARD-1514 expected platform skips, subject d1579709)

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Repeat |
|---|---|---|---|---|---|---|---:|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-r08ac/` | touched-and-guards | `/*/*/(TestLaneCategoryGuardTests*)\|(ExpectedPlatformSkipRosterTests*)\|(NightlyCoreTests*)\|(LiveLaneRunnerTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)/*` | touched classes plus guard batch | all pass, 0 failed | 59 | 20 | |
