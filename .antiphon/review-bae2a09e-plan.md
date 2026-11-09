# Review bae2a09e receipt extract (CARD-1807 round 1)

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-r-bae2a09e/` | census-selector | `/*/*/(ProcessSpawnLimitTests*)\|(ImpactedClassesSelectorTests*)\|(MergeHighRadiusSubsetTests*)/*` | census, selector row | 74 passed; no failed/skipped | 74 | 15 |
| CP-2 | S1 | CP-1 | policy-guards | `/*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)/*` | guards | 77 passed; no failed/skipped | 77 | 5 |
