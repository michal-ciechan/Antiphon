# Report store publication repair checkpoints

R1 requires one verified canonical file when two store instances publish the same task and exact bytes. The contention witness holds the first store after its destination check, observes the second store's pending gate admission, and checks both results, exact bytes, a single rename, and cleanup. The original and open-reader witnesses also run in CP-1.

R2 requires a complete receipt for the impacted delivery class. CP-2 covers the nine Final guards and CP-3 covers the other impacted storage classes. There are no additional numbered V/R or manual acceptance rows for this repair.

## Performance budget

Review round 1 measured a shared cold build at 439 seconds and CP-1 through CP-3 at 12, 84, and 34 seconds. CP-4 has a provisional six-minute estimate from the review selection. Run it once against the isolated PostgreSQL fixture. The suite shares a fixture server and uses cloned databases to isolate cases. Retain phase timing and per-case TRX by checkpoint row. The brief sets a 570-second test-host ceiling. The four rows share one build and run serially on Linux, with report-store measurements enabled. Estimated build and test execution is about 16 minutes plus slot admission.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| CP-1 | S1 | tests/Antiphon.Tests -> bin-g82-ba2664ff/ | custody | `/*/Antiphon.Tests.Application/AgentReportStoreTests/*` | R1 | Entire custody class | 30 | 1 |
| CP-2 | S1 | CP-1 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | Final guards | All nine classes | 84 | 2 |
| CP-3 | S1 | CP-1 | impacted | `/*/*/(AgentReportStoreBudgetTests*)\|(AgentReportStoreLoadTests*)/*` | Storage deadline and load | Both storage classes | 22 | 2 |
| CP-4 | S1 | CP-1 | delivery | `/*/Antiphon.Tests.Application/OutputDistillationDeliveryTests/*` | R2 | Entire delivery class | 81 | 6 |
