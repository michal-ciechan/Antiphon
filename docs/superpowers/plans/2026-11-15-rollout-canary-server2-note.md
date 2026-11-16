# Upgraded server2 rollout canary — 2026-11-15
A reusable lease needs runner support, server WorkspaceSlotsEnabled, and an admitting WorkspaceSlotRoles value.
Roles accept `*`, `none`, or a comma-separated list such as `Plan,Docs`; `none` admits no role.
The runner records reusable `ws-NN` directories and their custody in `/work/workspace-slots/ledger.json`.
Runner-only enablement should leave tasks on legacy `task-<8hex>` mirrors while server roles are `none`.
Observed: this task runs in `/work/worktrees/ws-01`, a per-slot directory, contrary to the brief's expected per-task path.
That observation does not establish the effective server role setting or this preparation's recorded lease decision.
Soft rollback: set server roles to `none`; Docker requires a server restart, then confirm `WorkspaceSlotDecision reason=roles_none`.
Keep runner slot support enabled until outstanding leases release; then save its flag as `false` and recreate through a named rollout phase.
Do not remove quarantined workspaces by hand; use the documented custody-release operation.
