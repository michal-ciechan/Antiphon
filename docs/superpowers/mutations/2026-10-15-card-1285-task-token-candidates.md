# CARD-1285 unbound mutation candidates

Original Code/landing owner: `23d87a95-40ee-40a5-9ce9-81faded14d82`.
Plan: `docs/superpowers/plans/2026-10-15-card-1285-task-launch-token-plan.md`.
All PCs and variants below remain pending. Code ran no deliberate mutants.
After ordinary Review and publication, Mutation binds the landed source, owns
method-scoped red/restore/green and discovers missing controls. Do not execute
these candidates against the moving Code branch.

Every cycle selects exactly
`/*/*/PhoneHomeTaskDispatchProjectionTests/C1285_worker_launch_has_durable_task_token`.
Parameterized variants are identified in the fresh TRX; zero tests, build errors
and fixture errors do not count as red.

| PC | Candidate in AgentTaskDispatcher.FinishDispatchedLaunchSpecAsync | Intended failing variants/oracle | Status |
|---|---|---|---|
| PC-1 | Skip absent-token replacement | Codex Code missing, Claude Review missing, Grok Code missing, Claude Code relaunch: durable token at enqueue | Pending |
| PC-2 | Omit saving the replacement hash before returning the spec | All missing, stale and relaunch variants: independent database observation at enqueue | Pending |
| PC-3 | Accept a cached token without comparing the persisted hash | Codex Review stale: durable token at enqueue | Pending |
| PC-4 | Replace even a valid cached credential | Codex Code cached: original token retained | Pending |

Natural red-first failures at the pre-fix source and ordinary green results do not
discharge these controls. No timeout or assertion may be weakened.
