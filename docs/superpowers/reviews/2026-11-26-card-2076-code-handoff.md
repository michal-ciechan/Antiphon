# CARD-2076 S1–S4 Code handoff

[Implementation plan](../plans/2026-11-26-card-2076-proven-subtraction-plan.md) · [Verification design](../plans/2026-11-26-card-2076-proven-subtraction-test-design.md) · [57 controls and 87 variants](../mutations/2026-11-26-card-2076-candidates.md)

The production cut executes Full on this branch. Compiler subtraction and the proof replay are opt-in diagnostics. Their artifacts carry no authority to change execution mode. Review round 1 covers S1–S4 at the published tip.

| Slice | Landing boundary | Acceptance boundary |
|---|---|---|
| S1 compiler input custody | Independently landable with proof disabled. Merge absorbs the test project and generated index conflicts. | Keep the v1 output bytes with the flag omitted. An opt-in proof requires the pinned compiler pack and both endpoint identities. |
| S2 occurrence proofs | Depends on S1. The v2 producer may only provide advisory evidence. | Check the literal before/site/retained sets and canonical output, including actual SDK-generated inputs and the pinned Library → Facade → Destination signature. Unsupported forwarding shapes retain their complete candidate set. Keep bounded downstream and unbounded Full obligations in view. |
| S3 chunk composition | Depends on S2. Two endpoint proofs may change a diagnostic decision. | Review the owed/carry sets against the full case digest, both graphs, readers, project closures, and strict executed origins. Independent recomputation and acceptance against coherently resealed proposals belong to CARD-2069 and the fresh-master task. |
| S4 replay and miss records | Depends on S3. Replay and miss files are advisory evidence. | Verify declared attempts and case contents before scoring, both prediction seals, only own outcomes revealed for held groups, and immutable failed-case observations. Native shadow/audit integration and qualification closure belong to the fresh-master task. |
| S5 production withdrawal | Deliberately outside this branch. | Commission a Code task from fresh master after the CARD-2069 test-only verifier, CARD-2078, and CARD-2080 batch publishes. Use master’s default-off CARD-1836 dispatch path for CP-5, V-5, R-5, same-SHA Full recovery, and the repair procedure and rendered instructions. |

SourceLanding Mutation owns all per-variant defect, restore, and green cycles. Bind the roster to reviewed and landed source. A diagnostic replay or fixture approval cannot grant carries or renew an approval.
