# Why missed: GrokRulesCompactionRecoveryTests

`Captured_native_boundary_reaches_one_durable_refresh_without_idle_input(in-flight, standing)`
failed at clean master `8cec1e74e75819ef20756c228add987ce4ae4170`:
11 executed, 10 passed, 1 failed, 0 skipped. The prior attempt was already
LateConfirmed; SubmittedBodies was empty because its confirming flush was
receipt-only. CARD-1924 (`d606441d-0e04-4b46-accd-d6f68f3d976d`) was read and
describes this exact Linux reproduction. Repair
`13c17adc217a897c1246f48ff69c5f567deabf0f` asserts no write after that receipt
and drives one more safe flush before expecting exactly one refresh. All 11
lane/population variants, attempt accounting and the pending ordinary tail
remain covered.

CARD-1892's recorded ordinary Code/Review roster included
GrokRulesReadyOrderingTests, GrokRulesInitializationTests and
GrokRulesFailureTests, but omitted GrokRulesCompactionRecoveryTests. Those
selections did not seed this already-attempted ordinary row across a native
compaction boundary. The old Merge uncertainty row neither watched the queue
service nor named this class. The wider capped dependency closure was not
credited as executed in the Code evidence. Original Merge execution is
unknown; its selector omission is observable at the task base.

The cheapest pre-land selection is the shared
[`input-delivery-uncertainty` row](2026-11-14-why-missed-g90-three-classes.md#cheapest-pre-land-selection)
with this class explicit, alongside the existing ready-ordering witness.
Local evidence: `.antiphon/evidence/g90/red-grok/`.
