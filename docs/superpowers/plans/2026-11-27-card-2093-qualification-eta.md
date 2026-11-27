# CARD-2093 qualification ETA

The task base is `561591c562f31dd4fe0fc8f940f2ea7e947ce965`. This Code brief supplies the acceptance criteria directly. No earlier plan or TestDesign was supplied.

## Implementation

Use the current dispatch schedule and the previous run's TRX start-to-finish duration per chunk. Join on the scheduled suite and chunk name. Use the same estimate for `qualification-eta`, `status` and `watch-qual`. Keep the previous complete-run selection shared. Retain the active chunk's whole prior duration until it has a live TRX, consistent with status and the hand projection. Running and outstanding counts still distinguish the active scheduled chunk.

For matched finished chunks, the raw ratio is the sum of live seconds divided by the sum of previous seconds. Let the effective sample size be the square of the total previous seconds divided by the sum of squared previous seconds. Shrink the raw ratio toward 1 with a prior of 20 effective matches. Report confidence as provisional below 20 effective matches and matched thereafter. P50 is the damped point. P95 adds 1.645 times the duration-weighted standard error of the matched ratios to the damped ratio and is available from 20 effective matches. This is a conditional planning estimate. The standard error measures uncertainty in the common ratio and omits unscheduled overhead and unpriced chunks.

Unknown unfinished chunks are listed as unverified and excluded from the lower-bound estimate. When no remaining chunk has a usable historical timing, the finish is unavailable. At zero matches, use the unscaled prior for available remaining history and label it provisional. Keep the ETA read-only.

## Performance budget

One portable fixture assembly. The new ETA witnesses use small in-memory timing roots and a frozen clock. Status retains its existing per-test scratch files and bounded HTTP fakes. No new database, AppHost, or process fixture is needed by the ETA tests. The impacted nightly/live tests use their existing fixture lifetimes. Record test and slot costs in the fresh checkpoint reports and TRX. Estimates are provisional against this checkout. Budget six minutes for the isolated assembly build and four minutes for all selected rows, excluding slot queue time. No per-test timeout changes.

## Verification design

- V-1 runs the hand-calculated CLI witness red before the repair and green on the candidate. Point 150 s, P50 12:02:30.000Z, P95 12:03:07.739Z, one unpriced chunk, and an UNVERIFIED line.
- V-2 exercises early uncertainty, a missing baseline or invalid inputs, active progress, status and watcher output, and confirms the timing root is read-only.
- R-1 runs the required guard union, including PlanCheckpointClassSelectionGuardTests and the two companion live-gate methods.
- R-2 runs the matched `nightly-live-lane` impact classes, LiveLaneRunnerTests and NightlyCoreGoldenTests. The invariant is that shared nightly code retains the planning and live-lane fixture contracts.
- R-3 checks entry-point wiring, diagnoses the retired estimate from the source chain and the reported incident, runs evidence-diff, and checks merge-tree against fresh origin/master. No OS-specific production branch changes.

| PC | Exact class and method | Mutation |
|---|---|---|
| PC-1 | QualificationEtaTests.Scheduled_trx_history_gives_the_hand_calculated_point_and_percentiles | Use the raw matched ratio as the point and P50. |
| PC-2 | QualificationEtaTests.Scheduled_trx_history_gives_the_hand_calculated_point_and_percentiles | Price an unmatched chunk from the historical average or suppress the missing-chunk warning. |
| PC-3 | QualificationEtaTests.Scheduled_trx_history_gives_the_hand_calculated_point_and_percentiles | Substitute the 95th percentile of individual chunk ratios for the aggregate standard error. |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c2093-eta/` | eta-witness | `/*/*/QualificationEtaTests/Scheduled_trx_history_gives_the_hand_calculated_point_and_percentiles` | V-1 | exact method, 0 failed on candidate | 1 | 6 |
| CP-2 | all | CP-1 | eta-entry-points | `/*/*/(QualificationEtaTests*)\|(OperatorStatusTests*)\|(WatchQualTests*)/*` | V-2 | all three classes, 0 failed/skipped | 3 | 1 |
| CP-3 | all | CP-1 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(PlanCheckpointClassSelectionGuardTests*)/*` | R-1 | all eleven classes, 0 failed/skipped | 78 | 1 |
| CP-4 | all | CP-1 | live-gate-guards | `/*/*/LiveLaneInventoryTests/(EveryLiveGateHasDisposition*)\|(ExplicitGatesAreNeverFullRequired*)` | R-1 | both methods, 0 failed/skipped | 2 | 1 |
| CP-5 | all | CP-1 | nightly-live-impact | `/*/*/(LiveLaneRunnerTests*)\|(NightlyCoreGoldenTests*)/*` | R-2 | both classes, 0 failed/skipped | 2 | 1 |
