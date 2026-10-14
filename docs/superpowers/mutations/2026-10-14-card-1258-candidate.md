# CARD-1258 unbound Mutation candidates

All pending. Code owner: `01f4a4f0-6ba6-4753-afc4-4f21063e74ec`.
Bind only after ordinary Review and landing; no deliberate mutant was run by Code.

| ID | Mutation | Exact witness | Variants |
|---|---|---|---|
| PC-1 | Restrict the stage-pin diagnostic guard to RolePolicy again, or move it after the agent branch | model `C1258 V-1 stage-pin failure $source/$tier`, component `C1258 V-2 stage-pin failure suppresses route promises $source/$tier` | pin, pin+chain, role, any, config, rolePolicy, agent x Hard, Medium, Easy; binding chooses the applicable red witness per edit |
| PC-2 | Remove the routing-unknown gate from one candidate path or from stored origin rendering | component `C1258 V-2 stage-pin failure suppresses route promises rolePolicy/Hard` | effective candidates; stored candidates; stored origin (independent edits) |
| PC-3 | Pass null for every missing chain, ignoring default tier | `ComplexityEffectiveComposeTests.A_preferred_pin_on_an_empty_cell_appends_policy_only_for_the_default_tier`; HTTP `ComplexityEffectiveHttpTests.A_preferred_pin_on_empty_tiers_exposes_policy_only_on_the_default_tier` | Hard, Easy; preserve Medium fallback control |
| PC-4 | Restore the non-default missing-chain null argument with an unusable policy | `ComplexityEffectiveComposeTests.A_non_default_preferred_pin_does_not_report_an_unused_policy_as_unlaunchable` | unused invalid policy |

Use method-scoped bound packs and red/restore/green receipts. A source/tier whose
old behavior is already guarded is a control, not a claim that every edit kills
all 21 variants. Mutation owns final binding and missing-control discovery.
