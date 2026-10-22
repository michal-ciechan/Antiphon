# CARD-1511 unbound Mutation candidate

Original Code/landing owner: `d59d844d-b6a0-4fbd-b94a-b067911fb12d`.
Status: unbound; no controls executed. Bind to SourceLanding only after ordinary
Review and publication. Restart: none.

Plan and ordinary evidence:
`docs/superpowers/plans/2026-10-22-card-1511-removal-matrix-fixture.md`.

Exact method-scoped filter for both controls:
`/*/*/AgentTaskLandRemovalMatrixTests/C448_V18_LowLevelRemovalRechecksAtBothContentBoundaries`.

| Control | Mutant | Required red witness | Status |
|---|---|---|---|
| PC-1 | Bypass only the first `Protected.Length != 0` return in `GuardedWorktreeRemoval.RemoveDirectoryAsync` | `(2, submodule)` reaches its forbidden second hook; `(1, nested-repository)` sees an extra status read | pending |
| PC-2 | Bypass only the second `Protected.Length != 0` return | `(2, nested-repository)` reaches set-aside/unregistration and fails ownership/no-removal assertions, even if the no-follow delete later preserves bytes | pending |

Do not batch these controls: both change the same file and method. Mutation owns
each exact red/restore/green cycle, per-variant TRX, restoration proof and missing
control discovery. No timeout widening or assertion loosening. The existing 24
matrix variants plus the two new boundary cases must be accounted for each run.
The historical unchanged-source red is a fixture defect witness, not a PC cycle.
