# Architecture principles

Use these rules when changing ownership, a dependency boundary, a public contract, or the implementation of an existing capability. [Project context](project-context.md) owns conventions and layer direction. [The orchestration loop](orchestration-loop.md) owns stage authority, verification and landing.

## Design rules

1. **Single source of truth.** Give each business rule, state transition, data contract, configuration value, and standard a named owner. Derive views from the owner or check the projection against it.
2. **Canonical implementation.** Route equivalent callers through one implementation. Preserve behavioral differences that serve a real contract.
3. **Deep modules.** Give shared modules small, stable interfaces that absorb real complexity. Prefer existing seams and use the interface and layer rules in [Project context](project-context.md#enforcement-rules).
4. **High cohesion and low coupling.** Keep related invariants with their owner and keep infrastructure behind the prescribed boundaries.
5. **Locality of change.** Keep a concept's implementation and contract with its owner. Move code when the move improves ownership or dependency direction.
6. **Deletion.** Retire replaced paths once their callers have migrated and their compatibility obligations are met.
7. **Enforcement.** Use the cheapest reliable guard for a critical invariant. Use types and project references for dependencies, focused static checks for observable rules, and contract tests for behavior.

## Architecture evidence

For a local refactor, the diff and one sentence naming the preserved behavior suffice as architecture evidence.

When a change introduces or removes a shared seam, apply the **Deletion Test**. Name which callers would absorb the seam's work if it were removed and which contract, if any, justifies keeping the seam.

When replacing a shared implementation, record the old entry point, the scope and result of the bounded caller search, the new destination, and the deletion or the bridge owner and removal condition. Include external callers, jobs, configuration or stored data wherever the affected contract reaches them. Review compares base references with the migrated sites and the post-change search, then checks the deletion or bridge and the existing focused behavior witness.
