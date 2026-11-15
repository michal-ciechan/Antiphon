## Decisions
D-1: Keep this canary documentation-only to bound gate 7; reject enabling roles or changing deployment. Per [CARD-0824 S1](../../docker-stack.md#runner-workspace-slots-on-the-volume-card-0824-s1), leases require runner support, server `Delegation:WorkspaceSlotsEnabled`, and a matching `Delegation:WorkspaceSlotRoles` (`*` or a role list); `none` keeps legacy task mirrors.
D-2: Roll back by setting server roles to `none` first; confirm the next task's `WorkspaceSlotDecision` has `reason=roles_none`. Docker requires a server restart; AppHost can hot-reload only without an environment override. Keep runner support until outstanding leases release, then save `PhoneHome__WorkspaceSlots__Enabled=false` and recreate the runner through its named phase.
| Brief assumption | Documented or observed behavior |
| --- | --- |
| Runner enabled, server roles `none`; expect per-task directory | Slots use `/work/worktrees/ws-NN` (re-homed: `ws-NN-dK`), ledger `/work/workspace-slots/ledger.json`; this task actually runs in `/work/worktrees/task-6430f6eb`, consistent with the role gate. |
## Slices
S-1: Add only this note; lane: current Linux runner. Existing tests affected: none under [CARD-1782](../../testing-and-build.md#impacted-classes-card-1782); no checkpoints, filters, builds, tests, migrations, DTO/routes, operation labels, remote functions, or statement budgets change.
## Performance budget
No-test canary: no executable behavior changes. Build, setup/readiness, data, host, migration, snapshot, test-body and teardown phases are absent; no shared setup or test isolation is required. Test duration/p95 and approximately 2x-p95 timeouts are inapplicable; no timeout changes. Evidence is this note and the caller report; read-only HTTP observations use a 20-second bound, not a measured test-performance allowance.
