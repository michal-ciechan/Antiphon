# g90 operator abandon: unbound Mutation candidate

Code task/landing owner: ac7f437c-c7df-4e32-ba07-a57dce8c6eac.
Plan: ../plans/2026-11-14-g90-operator-abandon.md.
Every PC/variant below remains pending SourceLanding Mutation. No deliberate
mutant was executed by Code. Bind the actual published L and exact methods first.
Parent PC-1 through PC-10, with every variant, remain pending as inventoried in
2026-11-13-card-1892-input-uncertainty-candidate.md. No historical result is relabelled.

| ID | Variant | Exact ordinary method | Status |
|---|---|---|---|
| PC-11 | hide Sent/null/charged queue rows; omit last attempt/uncertainty | SessionQueueAbandonTests/Unconfirmable_sent_row_is_visible_until_operator_abandons_it | pending |
| PC-11 | omit durable attention; remove abandon action; offer SendNow; incident-age/session-state cutoff | SessionQueueAbandonTests/Unconfirmable_sent_row_is_visible_until_operator_abandons_it | pending |
| PC-11 | omit operator credential or reason validation; skip/duplicate/overwrite audit | SessionQueueAbandonTests/Abandon_requires_operator_and_reason_and_is_idempotently_audited | pending |
| PC-11 | permit Pending/confirmed/zero-attempt/ordinary-canceled | SessionQueueAbandonTests/Abandon_refuses_rows_without_an_unresolved_sent_attempt | pending |
| PC-11 | omit session predicate | SessionQueueAbandonTests/Another_session_cannot_abandon_the_row | pending |
| PC-11 | reconcile canceled back to Sent; resend/retype after abandon | SessionQueueAbandonTests/Late_prompt_after_abandon_leaves_row_terminal_and_never_types | pending |
| PC-11 | remove queue lock or verdict recheck; split status and audit commit | SessionQueueAbandonTests/Abandon_and_late_confirmation_serialize_without_resurrection | pending |
| PC-11 | leave source row blocking after explicit abandon | StandingSessionSwitchConcurrencyTests/Operator_abandon_of_unstartable_composerless_input_unblocks_history_switch | pending |
| PC-11 | omit attempts > 0 switch predicate | StandingSessionSwitchConcurrencyTests/Zero_attempt_sent_row_does_not_block_switch | pending |
| PC-11 | judge Canceled Grok rules on a late prompt/deadline; omit canceled identity and create a replacement | SessionQueueAbandonTests/Grok_rules_late_prompt_does_not_resurrect_abandoned_input | pending |
| PC-11 | remove shared session-row lock or verdict recheck while Grok rules reconciliation races abandon | SessionQueueAbandonTests/Grok_rules_confirmation_races_abandon_without_resurrection | pending |

Mutation owns missing-control discovery, including whether transaction rollback,
permanent-summary retention, and all alternate confirmation paths need further
method-scoped controls. Ordinary green does not discharge those obligations.
