# Upgraded server2 rollout canary
## Decisions
D-1: Keep this documentation-only canary bounded; reject configuration changes and builds because the brief authorizes only a note. A slot lease requires runner support, server enablement and a permitted role; otherwise use a legacy task mirror (CARD-0824 S1).
| Brief assumption | Documented behavior / observed ground truth |
| --- | --- |
| Server roles `none` require a per-task mirror. | The owner documents legacy mirrors under that gate, but this task actually runs in `/work/worktrees/ws-01`, a per-slot directory; placement expectation is unmet. |
Rollback: set server roles to `none` first (Docker server restart; desktop hot reload only without an environment override); confirm the next task's `WorkspaceSlotDecision` has `reason=roles_none`. Keep runner support until outstanding leases release, then save `PhoneHome__WorkspaceSlots__Enabled=false` and recreate through the named rollout phase.
Slice S1: add only this note, check its length/diff, commit and push the assigned branch. Existing tests affected: none under the CARD-1782 impacted-classes table; no migrations, DTOs/routes, operation labels, remote functions or statement budgets change. Checkpoint lane: none.
## Performance budget
No tests or builds: documentation-only scope, explicitly prohibited by the brief. Test/row/batch durations, setup/isolation, phases, measured p95 provenance, recording and approximately 2x-p95 timeouts are not applicable; no test timeout is set. Source: `docs/docker-stack.md#runner-workspace-slots-on-the-volume-card-0824-s1`.
