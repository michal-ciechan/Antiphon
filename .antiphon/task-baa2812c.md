# Task baa2812c — CARD-1481 repair: ClaudeEffortByRole pinned placement

Owner: Code task baa2812c (descendant of 8333e231; lands the whole CARD-1481 delta).
Branch `feat/card-task-baa2812c`, runner mirror `/work/worktrees/task-baa2812c`.
Plans: `docs/superpowers/plans/2026-10-22-claude-effort-by-role-plan.md` (original, overlay edited),
`docs/superpowers/plans/2026-10-22-claude-effort-by-role-repair.md` (repair slices, V-7..V-10, closed CP table, PC-7..PC-10).

## Outcome

Review df795c38 D-1 fixed: the effort decline now applies only to POOL placement
(`DeclinesReuseForClaudeEffort`): in the unpinned pool search, and for a pinned pool delegate only
after the busy check. Standing seats (`PlaceOnStandingAgentAsync`) never see it. Advisory taken:
the validator accepts role NAMES only (numeric `2` and comma `Plan,Code` keys now fail with the
literal unknown-role message). `docs/agent-kinds.md` documents that standing agents (the AlwaysOn
Check seat) ignore the setting and drops Check from the operator overlay; the plan overlay too.

## Commits

- `ae4fa6f0c1838de5f4c32407ee51d072ae1bd352` red witness (R1)
- `6b4b0137a37ff1b70f4d19c41e2688ce1e3fb72a` fix + docs + repair plan (R2)
- this report (R3) — the certifying checkpoint run is at the commit that adds this file

## Red witness (unlisted, stated in the repair plan)

One build-slot build (`bin-c1481dev/`, slot granted waited=75s, 0 errors) and one method-scoped
build-slot run at `ae4fa6f0c`: total 9, failed 4, passed 5, skipped 0. Failed on the guarded defect:
`An_unknown_effort_or_role_fails_validation_naming_the_setting(defined-numeric-role)` and
`(comma-separated-roles)` (`result.Failed` expected True, was False);
`a_check_task_pinned_to_its_standing_seat_keeps_the_seats_placement_under_an_override`
(expected `Reused`, was `SpawnFresh`); `a_task_pinned_to_a_busy_pool_delegate_still_waits_for_it_under_an_override`
(expected `WaitForAgent`, was `SpawnFresh`). Empty-map controls inside those methods passed before
the overridden assertion. `bin-c1481dev` deleted (28 directories) before the checkpoint run.

## Landability

- `git merge-tree --write-tree origin/master HEAD` at origin/master `2fc03221aad59278be9a526893b91e3a88036ac5`: clean (exit 0).
- rc pick onto `origin/rc/2026-10-20` (`ceb6e3d6b`), per-commit cherry-pick in a scratch worktree of
  4bf0275ee ce72659f9 92a5e97eb a57a77635 98d47fa7b ae4fa6f0c 6b4b0137a: clean except the plan doc
  (modify/delete; rc carries no `docs/superpowers/plans`, dropped). Net rc delta: 7 files,
  +837/-9 (agent-kinds.md, AgentTaskDispatcher.cs, AgentTaskService.cs, ClaudeLaunchArgs.cs,
  DelegationSettings.cs, the two test classes). Exclude 792227d02 (plan) and the task reports.

## Checkpoints

Run once at this report's commit (the final tip); CHECKPOINT lines, slot and counts are in the
task's final message (they cannot be in the commit they certify). `import --plan` of the repair
plan: 4 rows, exit 0.

## Pending for SourceLanding Mutation

PC-1..PC-6 (original plan) and PC-7..PC-10 (repair plan), all pending.

## Not run

Unit lane / whole suites (brief: minimal testing, no whole-suite runs); `DelegateLaunchArgvIntegrityTests` (Windows-only).
Residual noted, not changed: an escalation event for a Check task would still name a Check override
if an operator set one, although a standing seat ignores it; Check is out of the documented overlay.
