# CARD-2093 qualification ETA Mutation candidates

Code and landing owner `b3da96a5-6543-4d33-b19a-e28a6ddb4e43`. The original red witness is retained from Code task `64096091`.

Verification manifest [CARD-2093 qualification ETA](../plans/2026-11-27-card-2093-qualification-eta.md).

All variants are pending for SourceLanding Mutation. Bind to the landed source and run each variant with its exact method filter in `Antiphon.Tests`.

| ID / variant | Candidate defect | Expected failing assertion | Exact filter |
|---|---|---|---|
| PC-1 / raw-point | Use the raw live-to-prior ratio for the point and P50. | Point becomes 200 s instead of 150 s and P50 moves. | `/*/*/QualificationEtaTests/Scheduled_trx_history_gives_the_hand_calculated_point_and_percentiles` |
| PC-2 / imputed-unknown | Include the previous run's average duration in the projection for the unknown chunk. | Point exceeds 150 s even though only the known chunk can be priced. | `/*/*/QualificationEtaTests/Scheduled_trx_history_gives_the_hand_calculated_point_and_percentiles` |
| PC-2 / silent-unknown | Suppress the unpriced-chunk warning. | The ETA must have a second line naming `suite-unknown`. | `/*/*/QualificationEtaTests/Scheduled_trx_history_gives_the_hand_calculated_point_and_percentiles` |
| PC-3 / individual-p95 | Substitute the nearest-rank P95 of per-chunk ratios for the aggregate uncertainty calculation. | P95 moves from `2026-11-10T12:03:07.739Z` to `2026-11-10T12:05:00.000Z`. | `/*/*/QualificationEtaTests/Scheduled_trx_history_gives_the_hand_calculated_point_and_percentiles` |
| PC-4 / tiny-denominator | Compute uncertainty from the unweighted variance of individually divided live-to-prior ratios. | The tiny historical chunk pushes P95 beyond `2026-11-10T12:02:02.281Z`. | `/*/*/QualificationEtaTests/A_tiny_historical_duration_does_not_blow_up_the_finish_interval` |
| PC-5 / raw-count | Use raw matched count for the P95 threshold. | A sample dominated by one chunk produces P95 and loses provisional confidence. | `/*/*/QualificationEtaTests/One_dominant_chunk_keeps_the_sample_provisional` |
| PC-6 / fractional-tick | Use `now.AddSeconds(milliseconds / 1000)` to form the finish instant. | The point stays 40.952 s but P50 prints `12:00:40.951Z` instead of `12:00:40.952Z`. | `/*/*/QualificationEtaTests/First_match_is_damped_and_active_chunk_counts_as_unfinished` |
| PC-7 / trx-only-baseline | Skip command-suite completion when selecting the historical baseline. | The earlier mixed-suite schedule is skipped, and the native chunk loses its 100 s baseline. | `/*/*/QualificationEtaTests/Finished_mixed_suite_run_out_ranks_a_newer_stopped_attempt` |
| PC-8 / match-count-only | Allow scaling with any positive matched prior seconds. | One 10 ms prior duration inflates the 3,600 s point to 346,285.714 s. | `/*/*/QualificationEtaTests/One_ten_millisecond_match_keeps_an_hour_near_its_baseline` |
| PC-9 / summary-optional | Remove the ETA reader's ResultSummary requirement while retaining the watcher's check. | The same poll reports a parse error and credits the incomplete chunk. | `/*/*/WatchQualTests/Well_formed_trx_stays_unfinished_in_eta_until_the_result_summary_arrives` |
| PC-10 / float-match-boundary | Sum the prior weights and squared weights in double precision and compare the quotient to 20. | Twenty equal 10.1 s chunks lose P95. | `/*/*/QualificationEtaTests/Exactly_twenty_fractional_matches_publish_p95_but_unequal_weights_do_not` |
| PC-11 / force-comparable | Skip the shared eligibility check for CLI and watcher observations. | The CLI and watcher print a finish for the Linux Full run. | `/*/*/OperatorStatusTests/Linux_full_projection_is_withheld_by_status_cli_and_watcher` |
| PC-12 / unbound-explicit-log | Bypass identity checks when an explicit log directory is supplied. | The CLI estimates the wrong run even though the full IDs differ. | `/*/*/QualificationEtaTests/Explicit_log_directory_must_match_every_available_full_run_id` |
| PC-13 / fractional-observation | Pass the raw observation clock to the finish calculation. | The CLI and JSON differ by 2,904 ticks at the same observation instant. | `/*/*/OperatorStatusTests/Fractional_observation_clock_publishes_the_same_finish_in_status_and_cli` |
