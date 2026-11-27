# CARD-2087 status Mutation candidates

Original Code and landing owner: `29963d0b-9188-41f6-9c81-8c7a8a54d3f3`.
Verification manifest: [CARD-2087 operator status](../plans/2026-11-26-card-2087-operator-status.md).

Every variant is pending for SourceLanding Mutation. Use the exact method
filters in Antiphon.Tests. Bind these candidates to the landed source before
running any deliberate defect.

| ID / variant | Candidate defect | Failing assertion | Method |
|---|---|---|---|
| PC-1 / unmatched-live-time | Include all live duration in the calibration numerator. | Ratio changes from 1.5 and the 60-second projection moves. | `/*/*/OperatorStatusTests/Exact_eta_matches_finished_chunks_and_lists_failures_and_unknowns` |
| PC-2 / removed-work | Include historical chunks absent from the live schedule in remaining work. | The removed 900-second chunk changes the projection and largest chunk. | `/*/*/OperatorStatusTests/Exact_eta_matches_finished_chunks_and_lists_failures_and_unknowns` |
| PC-3 / missing-baseline | Invent a finish when no prior run is readable. | Finish must stay null and the missing source must be reported. | `/*/*/OperatorStatusTests/Missing_previous_run_and_invalid_heartbeat_keep_independent_facts` |
| PC-4 / malformed-heartbeat | Suppress the unreadable heartbeat diagnostic. | The errors array must identify the heartbeat separately from the missing prior run. | `/*/*/OperatorStatusTests/Missing_previous_run_and_invalid_heartbeat_keep_independent_facts` |
| PC-5 / raw-report-next | Use the delegate report body instead of the server's parsed handoff. | The task must show Review and its typed handoff despite the misleading result body. | `/*/*/OperatorStatusTests/Status_uses_typed_handoff_and_exposes_fleet_facts_even_without_rc_sources` |
| PC-6 / truncated-run-id | Match state or heartbeat to the directory suffix instead of the full run id. | Discovery must expose the full run id and its heartbeat. | `/*/*/OperatorStatusTests/Release_layout_lookup_binds_the_state_and_heartbeat_to_the_full_run_id` |

## Round 2 (Review 1a6c7132 defects)

PC-6 is rebound: the release-layout method now adds a completed decoy state and a
supervisor record whose run id shares the live run's 8-hex prefix with a different
heartbeat (age 1800 s, hang suspected). A prefix or directory-suffix match makes
discovery ambiguous (an error, exit 2) or binds the decoy heartbeat. Round-2
witnesses: [round-2 manifest](../plans/2026-11-26-card-2087-operator-status-r2.md).

| ID / variant | Candidate defect | Failing assertion | Method |
|---|---|---|---|
| PC-6 / truncated-run-id (rebound) | Match the state or supervisor run id by the 8-hex prefix or the log directory suffix. | `errors` must be empty and `heartbeatAgeSeconds` 60. | `/*/*/OperatorStatusTests/Release_layout_lookup_binds_the_state_and_heartbeat_to_the_full_run_id` |
| PC-7 / non-trx-root-crash | Drop `InvalidDataException` from the live TRX catch in `OperatorTrxStatus.Read`. | The command throws instead of exit 2 with the unreadable-TRX line. | `/*/*/OperatorStatusTests/Live_trx_without_a_TestRun_root_is_unverified_not_a_crash` |
| PC-8 / silent-lower-bound | Skip the lower-bound error (or `lowerBound`) when remaining chunks have no previous timing. | `lowerBound` true and the single UNVERIFIED line naming `suite-newWaiting`; exit 2. | `/*/*/OperatorStatusTests/Exact_eta_matches_finished_chunks_and_lists_failures_and_unknowns` |
| PC-9 / partial-baseline | Accept any earlier log directory with a TRX as the previous run (ignore `OperatorTrxStatus.Incomplete`). | `previousRunLogDir` is the complete run and the projection is 90 s. | `/*/*/OperatorStatusTests/Stopped_partial_attempt_is_not_the_eta_baseline` |
| PC-10 / unstable-shape | Remove one key from `OperatorStatusShape` (for example `landQueue` or `lowerBound`). | The key sets of the healthy and failed observations equal the documented lists. | `/*/*/OperatorStatusTests/Json_shape_keeps_every_documented_key_when_sources_fail` |
| PC-11 / sequential-or-unbounded-reads | Set `OperatorStatusReads.Workers` to 1, or ignore the per-read deadline in `Take`. | Elapsed under 12 s, the attention line names its 1 s deadline, and no task-detail GET fails. | `/*/*/OperatorStatusTests/Slow_sources_are_bounded_and_task_details_are_read_concurrently` |
