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
After ordinary verification and Review, the caller must resolve the original
already-landed Code owner's publication identity before commissioning a sourced
Mutation pass. Do not infer that identity from these ancestor commit hashes.
