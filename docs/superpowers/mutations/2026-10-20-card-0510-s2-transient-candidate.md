# CARD-0510 S2 transient repair: unbound Mutation candidate

Code and landing owner: `129c2eba`, branch `feat/card-task-129c2eba`.
Execution plan: [transient repair](../plans/2026-10-20-card-0510-s2-transient-repair.md).
Parent obligations: [CARD-0510 plan](../plans/2026-10-20-card-0510-standing-restart-escalation-plan.md).

The two ordinary witnesses in `StandingRestartEscalationTests` are
`C510_S2_fresh_inventory_failure_keeps_bounded_retry` and
`C510_S2_fresh_model_disabled_keeps_capacity_wait_and_bounded_retry`.
Both fail against pre-repair production because transient preflight failures persist
`AutomaticFreshRefused`. Each covers ordinary/capacity handoffs and absent/existing
repeated-resume holds. These are ordinary regression reds, not deliberate mutants.
The earlier durable-refusal witness remains in the combined green selection.

No controls or payloads are bound here. All parent PC-1 through PC-89, including
every variant, remain pending caller-commissioned SourceLanding Mutation after
ordinary Review and landing. Mutation owns missing-control discovery and all
method-scoped red/restore/green cycles against landed source.
