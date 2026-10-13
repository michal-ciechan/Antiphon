# CARD-1205 repair 6 — unbound Mutation candidate

Status: pending; post-land SourceLanding Mutation was NOT run. Current landing
owner is `cbce1a3f-5d59-47d2-a8a3-859ca571c16f`, inheriting original Code task
`d3cc8360-d502-4525-8f0b-b161e1adc4b4` and S1 34df9193c. S1 lands with S2
after Final Review. This supersedes repair 5's candidate index, not its pending PCs.

All PC-1..182 and every variant remain pending. Inherited PC-1..175 retain their
bindings in the [test design](../plans/2026-10-12-card-1205-test-design.md).
Additive PC-176..182 payloads/red assertions are bound in its
[repair 6 section](../plans/2026-10-12-card-1205-test-design.md#repair-6-callback-exception-binding-t54-t56).
Mutation owns deliberate mutants, red/restore/green and missing-control discovery.
The ordinary unchanged-production baseline is not a discharged PC cycle.

Every cycle must be method-scoped using one of these exact filters:

- PC-176..179 and PC-182:
  `/*/*/SessionRunnerHeldEventDeliveryTests/C1205_Callback_fault_preserves_original_identity`
  (sixteen literal async/sync/host/deadline x IOE/unrelated OCE x catch-up outcome rows).
- PC-180:
  `/*/*/SessionRunnerHeldEventDeliveryTests/C1205_Completed_callback_has_no_abandonment`
  (two literal catch-up outcome rows).
- PC-181:
  `/*/*/SessionRunnerHeldEventDeliveryTests/C1205_Deadline_before_late_fault_retains_abandonment`
  (two literal catch-up outcome rows).

Do not use a whole class/suite or remove scope custody to produce teardown red.
PC-181 preserves observer cleanup while omitting actual fault observation, as PC-173
does for T53. No assertion or timeout may be loosened.

Ordinary source verified clean: 61d7517349427a4098597c66de9fd246233f3572.
Bind this unbound candidate to the separately commissioned landed snapshot before
execution. The caller lands the original Code task only after Review, then commissions
SourceLanding Mutation; this document does not authorize snapshot repairs or publication.

Unmeasured additive budget: seven cycles at seven minutes = 49 minutes plus
snapshot binding/setup; inherited PC budget remains separate. Restart: none.
