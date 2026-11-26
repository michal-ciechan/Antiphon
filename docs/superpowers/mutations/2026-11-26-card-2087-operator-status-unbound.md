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
