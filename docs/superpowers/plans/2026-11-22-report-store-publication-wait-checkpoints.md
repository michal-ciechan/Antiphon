# Report store publication wait verification

The contention witness must fail at the stalled step and release the held writer before bounded cleanup. The test-owned admission-signal regression case exercises the missing-signal path through two real stores. The existing contention case still requires one rename, the same canonical path from both stores, exact bytes, and no temporary files. The original publication and open-reader witnesses also run in CP-1. There are no additional numbered V/R or manual acceptance rows for this repair.

## Performance budget

Round 2 Review recorded 10 successful exact contention trials with a maximum method duration of 233 ms. The two existing 5-second signal deadlines allow for custody and scheduling variability on the shared host. Writer completion and cleanup each have a 5-second bound, and both writes share a 15-second cancellation deadline. The previous run measured a 349-second build, an 11-second custody class, a 79-second guard batch, and roughly 11 seconds of test-host wall time per exact trial. The rows share one cold build and run serially. Estimated wall time is eight minutes plus build-slot admission and receipt settlement. The test host retains its 570-second ceiling.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| CP-1 | S1 | tests/Antiphon.Tests -> bin-g82-04779e33-final/ | custody | `/*/Antiphon.Tests.Application/AgentReportStoreTests/*` | Round 2 repair | Entire custody class, including the missing-signal diagnostic | 31 | 1 | true |
| CP-2 | S1 | CP-1 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | Final guards | All nine classes | 84 | 2 | true |
| CP-3 | S1 | CP-1 | trial-1 | `/*/Antiphon.Tests.Application/AgentReportStoreTests/Publication_gate_serializes_two_store_instances` | Round 2 exact trial | Contention witness | 1 | 1 | true |
| CP-4 | S1 | CP-1 | trial-2 | `/*/Antiphon.Tests.Application/AgentReportStoreTests/Publication_gate_serializes_two_store_instances` | Round 2 exact trial | Contention witness | 1 | 1 | true |
| CP-5 | S1 | CP-1 | trial-3 | `/*/Antiphon.Tests.Application/AgentReportStoreTests/Publication_gate_serializes_two_store_instances` | Round 2 exact trial | Contention witness | 1 | 1 | true |
