# CARD-1259 D-1 unbound Mutation candidates

Code and landing owner: 8a60a8e2-1e87-4482-9e47-1e15f3bbfbe8, including the inherited
pump-decoupling chain. These are pending candidates for binding after ordinary Review
and landing. Code ran no deliberate mutant. The defect reproduction at
f047d9c1870e366e5e75cee8b25076c47190bdda failed all eight new cases on real assertions.

All PC-1 through PC-113 and every variant in
[the original manifest](../plans/2026-10-15-card-1259-transcript-pump-verification.md)
remain pending. Ordinary red/green evidence discharges none of them.

| ID | Compiling defect to introduce | Exact method filter | Pending variants and required red |
|---|---|---|---|
| PC-114 | Restore unconditional Pending-row deferral in OnTurnEndCoreAsync | `/*/*/SessionRuntimeActionDeferralTests/C1259_ineligible_pending_rows_finish_and_release_retention` | parked, stopped, failed, failed-rules, accumulated-turns; nonzero action debt instead of zero |
| PC-115 | Restore fixed one-second deferral, or omit Attempts increment | `/*/*/SessionRuntimeActionDeferralTests/C1259_transient_deferrals_back_off_without_charging_input` | schedule; attempt bookkeeping; not-due hints must not repeat checks |
| PC-116 | Disable deferred queue replacement, or apply DueAt before the replacement check | `/*/*/SessionRuntimeActionDeferralTests/C1259_new_requests_replace_backed_off_queue_work` | boundary and status; two retained wakeups instead of one before the old due time |
| PC-117 | Omit superseded boundary notification, or publish it for narrow requests too | `/*/*/SessionRuntimeActionDeferralTests/C1259_new_requests_replace_backed_off_queue_work` | missing per-boundary notification; extra status notification; exact finish counts differ |

Mutation owns method-scoped red/restore/green and missing-control discovery, including
independent terminal-while-Working gates, transport/Herdr transient branches, late-confirm
obligations carried by superseded actions, and the existing send-before-ack uncertainty.
No exactly-once external transport guarantee across acceptance-before-ack is added.

| PC-118 | Remove the boundary/manual/status restriction on the action being replaced | `/*/*/SessionRuntimeActionRoutingTests/C1259_review_recovery_uses_authorized_queue_identity_once` | enqueue-failure H3 busy: original typed review handoff action must remain at phase 8 while submission is held and shutdown joins it |

PC-118 also remains pending. Its existing assertion exposed the over-broad replacement in
Code's first full routing run; the assertion and fixture were preserved.
