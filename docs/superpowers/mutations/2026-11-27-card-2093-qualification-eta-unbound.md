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
