# CARD-1892 unbound Mutation candidate

Original Code/landing owner: 13c4448d-6092-41e2-8ea0-4657c3b70bee.
Plan: ../plans/2026-11-13-card-1892-input-uncertainty.md.
Status: unbound; every PC/variant below is pending SourceLanding Mutation.
Code ran ordinary red-first witnesses only. No deliberate mutant was run.
Bind a confirmed landing and method-scoped pack before executing controls.
Mutation owns red/restore/green, missing-control discovery and additional variants.

| ID | Variant | Guarded defect | Exact ordinary method |
|---|---|---|---|
| PC-1 | raw Input | restore global workspace gate around Input | C1677RetentionDispatchTests/Phase_a_does_not_block_input |
| PC-1 | ConditionalInput | restore global workspace gate around ConditionalInput | C1677RetentionDispatchTests/Phase_a_does_not_block_input |
| PC-2 | Grok timeout, cancellation, disconnect | restore destructive launch catch for uncertain Input | GrokRulesReadyOrderingTests/Ambiguous_input_preserves_launch_and_waits_for_transcript |
| PC-2 | card timeout, cancellation, transport | restore destructive card launch catch | AgentSessionLaunchFailureTests/Card_input_uncertainty_preserves_session_and_original_attempt |
| PC-3 | Enter, body-to-Enter, re-Enter | revert unknown attempt to Pending | SessionMessageQueuePhoneHomeDropTests/Drop_under_an_in_flight_Enter_keeps_the_attempt_and_late_confirms_without_retyping; Drop_between_the_body_and_the_Enter_keeps_the_attempt_and_presses_Enter_without_retyping; Drop_between_the_Enter_and_its_re_Enter_keeps_the_attempt_and_late_confirms_without_retyping |
| PC-3 | expired rules deadline | fail a charged verdict-less rules attempt by time alone | GrokRulesReadyOrderingTests/Ambiguous_input_preserves_launch_and_waits_for_transcript |
| PC-4 | wait, hold, threshold | omit structured timing or emit below threshold | C1677RetentionDispatchTests/Mutation_lock_reports_slow_owner_wait_and_hold |

| PC-5 | external caller cancellation | swallow cancellation after retaining the process | AgentSessionLaunchFailureTests/External_cancellation_during_rc_setup_is_not_reported_as_degraded |

Use one exact `/*/*/ClassName/ExactTestMethod` filter per cycle, with all native
parameterized results inspected. Keep true readiness/spawn failure and generation
replacement smoke beside the restored source; those remain ordinary regressions.
Native Windows acceptance is separately owed by the caller. No PC is passed here.
