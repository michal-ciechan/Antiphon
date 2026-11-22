# Report store publication wait verification

The contention witness must fail at the stalled step and release the held writer before bounded cleanup. The test-owned admission-signal regression case exercises the missing-signal path through two real stores. The existing contention case still requires one rename, the same canonical path from both stores, exact bytes, and no temporary files. The original publication and open-reader witnesses also run in CP-1. There are no additional numbered V/R or manual acceptance rows for this repair.

## Performance budget

Round 2 Review recorded 10 successful exact contention trials with a maximum method duration of 233 ms. The two existing 5-second signal deadlines allow for custody and scheduling variability on the shared host. Writer completion and cleanup each have a 5-second bound. Both writes and the first writer's hold share a 15-second cancellation deadline. The rows share one cold build and run serially. The first pass measured 258 seconds for the build, 17 seconds for custody, and 77 seconds for the guards. Estimated wall time is six minutes plus build-slot admission and receipt settlement. The test host retains its 570-second ceiling.

Run `20261122-115714-899c` recorded three green exact contention trials at `573ab0135e8683252cd2297eb56e294583100040`. CP-1 in that run exposed a timer in the first writer's hold that could expire just ahead of the missing-admission diagnostic. The repaired hold follows the shared cancellation deadline. The final custody row executes both witnesses at the repaired tip.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| CP-1 | S1 | tests/Antiphon.Tests -> bin-g82-04779e33-settle/ | custody | `/*/Antiphon.Tests.Application/AgentReportStoreTests/*` | Round 2 repair | Entire custody class, including the missing-signal diagnostic | 31 | 1 | true |
| CP-2 | S1 | CP-1 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | Final guards | All nine classes | 84 | 2 | true |
