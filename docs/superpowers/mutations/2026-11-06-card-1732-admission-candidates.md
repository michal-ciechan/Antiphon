# CARD-1732 unbound mutation candidates

Original Code/landing owner a98c2d1e-7763-48ab-a2a6-b1bc1f30e27e.
Plan: docs/superpowers/plans/2026-11-06-card-1732-admission-scope.md.
Unbound candidates only; Mutation owns binding, deliberate mutation, missing-control
discovery and each method-scoped red/restore/green cycle after SourceLanding.

PC-1 has two pending variants: remove `methods` from the antiphon RC exclusion
for LiveLaneIsolationTests, then independently AgentReportStoreLoadTests in
`tests/test-execution-policy.json`. Reseal policyHash and live-lane PinnedInputDigest
together. Run only `/*/*/RcCoverageAdmissionTests/Windows_and_linux_obligations_remain_exact`.
Require the real script's `CoverageAdmission separate-method-workflows` assertion
to fail and name the offending class. A null-index exception, fixture/build error,
zero tests or stale-hash refusal is not the required red. Restore original bytes
and run the same exact method green per variant. Keep all mutation evidence external.
No PC is discharged by Code's in-memory regression fixtures or ordinary green runs.
Original owner 07ab1e49's PC-1..PC-6 variants remain pending independently.
