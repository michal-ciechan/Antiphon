# CARD-2093 qualification ETA

The task base is `561591c562f31dd4fe0fc8f940f2ea7e947ce965`. This Code brief supplies the acceptance criteria directly. No earlier plan or TestDesign was supplied.

## Implementation

Use the current dispatch schedule and the previous run's TRX start-to-finish duration per chunk. Join on the scheduled suite and chunk name. Use the same estimate for `qualification-eta`, `status` and `watch-qual`. Keep the previous complete-run selection shared. Retain the active chunk's whole prior duration until it has a live TRX, consistent with status and the hand projection. Running and outstanding counts still distinguish the active scheduled chunk.

For matched finished chunks, the raw ratio is the sum of live seconds divided by the sum of previous seconds. Let the effective sample size be the square of the total previous seconds divided by the sum of squared previous seconds. Shrink the raw ratio toward 1 with a prior of 20 effective matches. Report confidence as provisional below 20 effective matches and matched thereafter. P50 is the damped point. P95 adds 1.645 times the standard error derived from the matched elapsed-second residuals to the damped ratio and is available from 20 effective matches. This is a conditional planning estimate. The standard error measures uncertainty in the common ratio. Divide the root of the summed squared residuals, corrected by 1 / (1 − 1 / effective matches), by the total previous seconds. The estimates omit unscheduled overhead and unpriced chunks.

Unknown unfinished chunks are listed as unverified and excluded from the lower-bound estimate. When no remaining chunk has a usable historical timing, the finish is unavailable. At zero matches, use the unscaled prior for available remaining history and label it provisional. Keep the ETA read-only.

## Incident diagnosis

On 1fe763a9, the captured point was 22,845.126 s (6.35 h). At the 18:42Z observation, the two reported percentile timestamps implied about 50.8 h and 620.6 h remaining. The run finished at 22:36Z, 3.9 h later. The operator's separate prior-TRX calculation was about 4.4 h and covered a different set of priced chunks.

The old command read `groups-planned-<suite>.json` first. `TestPhaseManifests` copies the launch group's planner `seconds` to `plannedSeconds`. The planner divides each class's measured serial seconds by its measured node count and multiplies the resulting per-node price by the current node count. Unmeasured nodes receive modeled costs. `refresh-nightly-class-costs` builds the serial totals by adding test result durations, including overlap within a parallel test host. The expected-duration roster export copies those class totals for test metadata. It is a projection of the same measurements. The old optional `--class-cost-path` fallback summed complete class prices only when a row lacked `plannedSeconds`. A zero planner price counted as priced in the headline.

The units were seconds throughout. A launch price can mix conservative serial totals, modeled fractions of a second, and host-independent per-node rates. The timing sidecar wall seconds include launch work and measure elapsed time. Dividing wall by a small plan cost produces a large ratio even for a short chunk. The old point used a ratio of sums across completed priced chunks and applied it to a different mix of unfinished plan prices. Each old percentile instead applied a single nearest-rank per-chunk ratio to the entire remaining plan sum. Scaling the total by a single-chunk ratio applies that chunk's full pricing error across every remaining launch. The original timing sidecars and selection at 18:42Z were outside this workspace, so the exact incident rank and contributing chunks remain unverified.

## Performance budget

One portable fixture assembly. The new ETA witnesses use small in-memory timing roots and a frozen clock. Status retains its existing per-test scratch files and bounded HTTP fakes. No new database, AppHost, or process fixture is needed by the ETA tests. The impacted nightly/live tests use their existing fixture lifetimes. Record test and slot costs in the fresh checkpoint reports and TRX. Estimates are provisional against this checkout. Budget six minutes for the isolated assembly build and four minutes for all selected rows, excluding slot queue time. No per-test timeout changes. The red witness used bin-c2093-eta/ and retained its failed-run outputs. Its assembly build under contention took about 11.5 minutes after slot admission. The candidate uses a fresh named output for custody.

## Verification design

- V-1 runs the hand-calculated CLI witness red before the repair and green on the candidate. Point 150 s, P50 12:02:30.000Z, P95 12:03:07.739Z, one unpriced chunk, and an UNVERIFIED line.
- V-2 exercises early uncertainty, a missing baseline or invalid inputs, active progress, status and watcher output, and confirms the timing root is read-only. The point and finish timestamp use the same whole milliseconds.
- R-1 runs the required guard union, including PlanCheckpointClassSelectionGuardTests and the two companion live-gate methods.
- R-2 runs the matched `nightly-live-lane` impact classes, LiveLaneRunnerTests and NightlyCoreGoldenTests, and the derived release-documentation classes, PerTaskFinalUnitLaneWitnessTests and ReleaseBranchModelDocumentationTests. The shared nightly code must retain the planning and live-lane fixture contracts, and the ETA documentation must preserve the per-task and release qualification rules.
- R-3 checks entry-point wiring, diagnoses the retired estimate from the source chain and the reported incident, runs evidence-diff, and checks merge-tree against fresh origin/master. The inputs and arithmetic are portable. Record the scripts/lib startup-path census and its Windows pre-pick probe obligation.
- V-3 covers the seven round 1 review findings. The CP-1 selection keeps the original hand-calculated witness and adds the seven regression methods. The witness-only commit runs that selection against the pre-fix implementation.

| PC | Exact class and method | Mutation |
|---|---|---|
| PC-1 | QualificationEtaTests.Scheduled_trx_history_gives_the_hand_calculated_point_and_percentiles | Use the raw matched ratio as the point and P50. |
| PC-2 | QualificationEtaTests.Scheduled_trx_history_gives_the_hand_calculated_point_and_percentiles | Price an unmatched chunk from the historical average or suppress the missing-chunk warning. |
| PC-3 | QualificationEtaTests.Scheduled_trx_history_gives_the_hand_calculated_point_and_percentiles | Substitute the 95th percentile of individual chunk ratios for the aggregate standard error. |
| PC-4 | QualificationEtaTests.A_tiny_historical_duration_does_not_blow_up_the_finish_interval | Compute uncertainty from variance of individually divided ratios, reintroducing the tiny denominator. |
| PC-5 | QualificationEtaTests.One_dominant_chunk_keeps_the_sample_provisional | Gate P95 by raw matched count instead of effective count. |
| PC-6 | QualificationEtaTests.First_match_is_damped_and_active_chunk_counts_as_unfinished | Convert whole milliseconds back to floating seconds and pass them to AddSeconds. |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c2093-review-fixes/` | eta-witness | `/*/*/(QualificationEtaTests*)\|(OperatorStatusTests*)\|(WatchQualTests*)/(Scheduled_trx_history_gives_the_hand_calculated_point_and_percentiles)\|(Finished_mixed_suite_run_out_ranks_a_newer_stopped_attempt)\|(One_ten_millisecond_match_keeps_an_hour_near_its_baseline)\|(Well_formed_trx_stays_unfinished_in_eta_until_the_result_summary_arrives)\|(Exactly_twenty_fractional_matches_publish_p95_but_unequal_weights_do_not)\|(Linux_full_projection_is_withheld_by_status_cli_and_watcher)\|(Explicit_log_directory_must_match_every_available_full_run_id)\|(Fractional_observation_clock_publishes_the_same_finish_in_status_and_cli)` | V-1, V-3 | eight named methods, 0 failed on candidate | 8 | 14 |
| CP-2 | all | CP-1 | eta-entry-points | `/*/*/(QualificationEtaTests*)\|(OperatorStatusTests*)\|(WatchQualTests*)/*` | V-2 | all three classes, 0 failed/skipped | 37 | 1 |
| CP-3 | all | CP-1 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(PlanCheckpointClassSelectionGuardTests*)/*` | R-1 | all eleven classes, 0 failed/skipped | 78 | 1 |
| CP-4 | all | CP-1 | live-gate-guards | `/*/*/LiveLaneInventoryTests/(EveryLiveGateHasDisposition*)\|(ExplicitGatesAreNeverFullRequired*)` | R-1 | both methods, 0 failed/skipped | 2 | 1 |
| CP-5 | all | CP-1 | nightly-live-impact | `/*/*/(LiveLaneRunnerTests*)\|(NightlyCoreGoldenTests*)/*` | R-2 | both classes, 0 failed/skipped | 37 | 1 |
| CP-6 | all | CP-1 | release-doc-impact | `/*/*/(PerTaskFinalUnitLaneWitnessTests*)\|(ReleaseBranchModelDocumentationTests*)/*` | R-2 | both classes, 0 failed/skipped | 4 | 1 |
