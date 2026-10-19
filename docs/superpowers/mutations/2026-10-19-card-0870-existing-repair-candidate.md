# CARD-0870 unbound Mutation candidate

Original Code task / landing owner: `34eae1e5-0e77-4009-8ddc-f55d928ac126`.
This is an unbound candidate; no source landing operation is claimed.

The assigned Code base already includes the harness repair in
`d75783b811e18453cf2e7bf8841a4b135d898896` and
`cb67130bbe8523f970ea10af8dcfe37d28edff15`.
This task introduces no test or production changes and no PC-n/variant rows.
Mutation still owns missing-control discovery and deliberate red/restore/green
cycles, including whether removing the store or restoring the retired outcome
would be caught by the native witness. All such work is pending.

Ordinary verification selection and results are separate from Mutation:
`docs/superpowers/plans/2026-10-19-card-0870-existing-repair-verification.md`.
The server's original Code owner is `2a687183-07bc-44e9-af70-6c806311ec8d`.
Its landing operation `ba47b99e-e0c9-4050-99ce-b3d10999d0bc` reports `Landed`,
source `63a950e787acc8736fb9283bd69d53915fcc3391`, published
`cb67130bbe8523f970ea10af8dcfe37d28edff15`. Cleanup was refused for
`evidence_retention_unavailable`. The caller should use that original owner's
publication identity if commissioning SourceLanding Mutation of the existing
repair; this verification-only task does not republish it.
