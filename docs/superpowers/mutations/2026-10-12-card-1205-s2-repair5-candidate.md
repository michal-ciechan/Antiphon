# CARD-1205 repair 5 — unbound Mutation candidate

Status: pending; no SourceLanding Mutation was run. Landing owner is
`d3cc8360-d502-4525-8f0b-b161e1adc4b4`; inherited Code lineage is
`54ad209e-df48-4ac1-9fe8-e05fd928c3ba`. S1 lands with S2 after Final Review.

All PC-1..175 and every variant remain pending. The additive PC-172..175 payloads
and red assertions are bound in [test design](../plans/2026-10-12-card-1205-test-design.md#repair-5-exception-binding-t53).
Every phase uses only
`/*/*/SessionRunnerHeldEventDeliveryTests/C1205_Catchup_exception_survives_bounded_held_delivery`
(three literal results), never a class/suite. Each control requires independent
red/restore/green evidence from the landed source snapshot. Mutation owns deliberate
mutants and missing-control discovery. The ordinary baseline and requested PC-118
scratch diagnostic do not discharge a SourceLanding cycle.

PC-173 changes `await delivery` to `await Task.WhenAny(delivery)` in the late
observer. Completion and scope disposal still happen, but the fault is not awaited
or logged. Removing the whole observer and losing its scope custody would produce
a teardown failure rather than the intended `observed.ShouldBeTrue` assertion red.
PC-172 removes the finally's secondary-exception catch/filter. PC-174 skips the
common offer on exceptional catch-up unwind. PC-175 restores unbounded None dispatch.
No payload or assertion was weakened, and no test timeout was increased.

Unmeasured additive budget: four cycles at seven minutes each = 28 minutes, plus
snapshot binding/setup. This is pending work, not claimed execution or savings.
Restart: none.
