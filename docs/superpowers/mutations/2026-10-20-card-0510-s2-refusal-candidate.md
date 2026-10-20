# CARD-0510 S2 refusal repair: unbound Mutation candidate

Original Code and landing owner: `1480cec4`, branch `feat/card-task-1480cec4`.
Repair execution plan: [S2 refusal repair](../plans/2026-10-20-card-0510-s2-refusal-repair.md).
Parent obligations: [CARD-0510 plan](../plans/2026-10-20-card-0510-standing-restart-escalation-plan.md).

The added ordinary witness is
`StandingRestartEscalationTests.C510_S2_refused_fresh_stays_held_across_later_ticks`.
It exercises ordinary/capacity admission and absent/existing continuity holds using
real reservation refusal on attempted input, persisted-state reads and Start preflight counts.
Its pre-fix red is ordinary regression evidence, not a deliberate mutant cycle.

No controls or payloads are bound by this repair. Every parent PC-1 through PC-89,
including every variant, remains pending for caller-commissioned SourceLanding
Mutation after ordinary Review and landing. Mutation owns missing-control discovery
and all method-scoped red/restore/green execution against the landed source.
