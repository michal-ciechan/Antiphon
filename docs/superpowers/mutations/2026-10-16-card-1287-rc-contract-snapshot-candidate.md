# Unbound candidate: RC contract snapshots D-8 through D-11

Original Code / landing owner: `98df1e5f-93e3-4ea1-a0d7-8aedf2b0a146`.
Plan: `docs/superpowers/plans/2026-10-16-card-1287-rc-contract-snapshots.md`.
Evidence: `docs/investigations/2026-10-16-card-1287-rc-contract-snapshot-repair.md`.

Zero authored PC rows. SourceLanding Mutation must discover missing controls and bind
any deliberate mutant, red/restore/green cycle to the landed snapshot and exact method.
Ordinary baseline reds and final greens do not discharge Mutation.

Guarded methods in `Antiphon.E2E.ContractSnapshotTests`: Pipeline_status_contract,
Backlog_quadrant_contract, Delegated_task_board_and_drawer_contracts,
Home_tasks_rail_contract. Preserve full-object equality and downstream board, drawer
and recorded-base assertions. Fixture input supplies runner capabilities; it does not
replace the real endpoint projection. Timestamp type checks must also remain effective.

Commission only after ordinary Review and caller landing of the original Code task.
Restart: none.
