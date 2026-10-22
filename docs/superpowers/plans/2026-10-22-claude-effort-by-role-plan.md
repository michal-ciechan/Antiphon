# Claude effort by role: `Delegation:ClaudeEffortByRole` (task 8333e231)

Operator request 2026-10-22: "Do all apart from review on opus xhigh". Today a Claude delegate's
`--effort` is derived only from the tier (`ClaudeLaunchArgs.Effort(level)`: Frontier→xhigh,
High→high, Medium→medium, Low→low), so "opus (tier High) at xhigh" cannot be expressed. The
model chain (opus first for non-Review roles) is already the orchestrator's complexity matrix;
this plan adds the smallest settings-driven effort override and nothing else.

## Design

- `DelegationSettings.ClaudeEffortByRole : Dictionary<string, string>` (role name → effort),
  case-insensitive keys like `RolePolicy`, **empty as shipped**. Keys are `AgentTaskRole` names;
  values are the Claude CLI's accepted efforts `low | medium | high | xhigh | max`
  (`claude --help` 2.1.251; `ClaudeEffortIntent.IsEffort`). String keys rather than enum keys so a
  typo is a named `DelegationSettingsValidator` failure at startup, not a binder exception.
- `ClaudeLaunchArgs.Effort(level, role, byRole)`: the role's override, canonicalised to the
  catalog constant (never the operator's raw string), else `Effort(level)`. An unknown value throws
  `InvalidOperationException` naming `Delegation:ClaudeEffortByRole:<Role>` (defence in depth
  behind the validator; the launch fails, argv is never built).
- `DelegationSettingsValidator`: unknown role → `Delegation:ClaudeEffortByRole has unknown role
  '<x>'. Use <AgentTaskRole names>.`; unknown value → `Delegation:ClaudeEffortByRole:<Role> is
  '<v>', which is not a Claude effort. Use low, medium, high, xhigh or max.`
- `AgentTaskDispatcher.ComposeDelegateArgs` (the only task launch path that emits
  `ClaudeLaunchArgs.EffortFlag`) passes `task.Role` and the map. `AgentSessionLaunchComposer`
  (named standing agents, no task role) is unchanged. Codex/Grok untouched.
- Warm pool: a reused process cannot change its `--effort` (it is a launch argument), and the pool
  matches on tier, not role. A ClaudeCode task whose role override differs from its tier's effort
  declines warm reuse (`SpawnFresh`, logged), mirroring the launch-env-override decline. Residual,
  documented: a task **without** an override may still reuse a same-tier warm session that was
  launched at an overridden effort; recording the launch effort on the pool agent needs a column
  and is out of this task's scope.
- `AgentTaskService`: `TierWiredEffort` becomes role-aware (`LaunchEffort(task, level)`), and the
  escalation detail gains ` <alias> launches at <effort> by role override
  (Delegation:ClaudeEffortByRole:<Role>), not the tier-wired <effort>.` when the override differs
  from the target tier's effort. With an empty map every event string is byte-identical to today.
- Docs: `docs/agent-kinds.md` §3 paragraph (setting, default empty, how to set it, Review
  intentionally absent from the operator's map) and the `--effort` bullet in §4.

## Slices

- S1: this plan.
- S2 (red witness): the setting property and the `Effort(level, role, byRole)` overload as a
  tier-only pass-through, plus the new tests. The override tests, the validator tests, the warm
  reuse test and the escalation test fail on the guarded defect (override ignored / not validated).
- S3 (fix): override resolution, validator, dispatcher launch + warm-reuse decline,
  `AgentTaskService` wording, docs.
- S4: the task report (`.antiphon/task-8333e231.md`, force-added) so the final tip is the tested
  source.

## Verification design

Coverage to class:

- V-1 resolution: override wins for its role, absent roles keep the tier, empty map equals the
  tier table for every level × role, value is case-insensitive and canonical, `max` accepted,
  unknown value throws naming the setting — `ClaudeEffortByRoleSettingsTests`.
- V-2 validator: unknown role, unknown value, valid maps (incl. `max`, mixed case), binding through
  `IOptions` — `ClaudeEffortByRoleSettingsTests`.
- V-3 argv: with `{Code: xhigh}` a Code task at ClaudeCode/High launches `--effort xhigh`; a Review
  task at High keeps `--effort high` — `ClaudeEffortByRoleDispatchTests`.
- V-4 compatibility: with an empty map the Code and Review argv are the pinned pre-change sequence
  (`--name`, `--effort high`, `--append-system-prompt`, `--model opus`), and the Review argv under
  `{Code: xhigh}` is element-for-element identical to the empty-map argv —
  `ClaudeEffortByRoleDispatchTests`.
- V-5 warm reuse: `{Code: xhigh}`, a warm Claude High agent in the directory, a Code task at High:
  the tick spawns fresh (task not bound to the warm agent); an empty map reuses — `ClaudeEffortByRoleDispatchTests`.
- V-6 escalation text: `{Debug: xhigh}`, a Claude Debug task escalated Medium→High names the
  override; with an empty map the detail is exactly `Escalated opus -> fable.` (existing Grok-class
  pin) — `ClaudeEffortByRoleDispatchTests`.
- R-1 existing pins unchanged: `ClaudeLaunchArgsTests`, `TierReasoningEffortAgreementTests`,
  `RolePolicyConfigurationValidationTests`.
- R-2 existing launch/escalation classes unchanged: `GrokDelegateDispatchTests`,
  `CodexDelegateDispatchTests`, `CardSpawnModelArgumentTests`.
- Not run here: `DelegateLaunchArgvIntegrityTests` (every method skips off Windows; stays with the
  Windows nightly), the Unit lane (brief: "Minimal testing, no whole-suite runs", 45-minute
  budget; the change is confined to the classes above). No manual acceptance: the shipped default
  is empty and the operator sets the overlay.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S3 | `tests/Antiphon.Tests -> bin-c8333cp/` | role-effort-unit | `/*/*/ClaudeEffortByRoleSettingsTests/*` | V-1, V-2 | all listed, 0 failed | 12 | 9 |
| CP-2 | S3 | CP-1 | role-effort-dispatch | `/*/*/ClaudeEffortByRoleDispatchTests/*` | V-3, V-4, V-5, V-6 | all listed, 0 failed | 6 | 3 |
| CP-3 | S3 | CP-1 | claude-args-pins | `/*/*/(ClaudeLaunchArgsTests*)\|(TierReasoningEffortAgreementTests*)\|(RolePolicyConfigurationValidationTests*)/*` | R-1 | all listed, 0 failed | 28 | 2 |
| CP-4 | S3 | CP-1 | delegate-dispatch-affected | `/*/*/(GrokDelegateDispatchTests*)\|(CodexDelegateDispatchTests*)\|(CardSpawnModelArgumentTests*)/*` | R-2 | all listed, 0 failed | 70 | 6 |

### Cost

Ordinary Code floor 9 + 3 + 2 + 6 = 20 minutes of checkpoint time, run twice (once at S3, once at
the S4 tip so the receipt validates against the reviewed SHA).

### Positive controls (pending for SourceLanding Mutation)

| PC | Mutation | Red test |
|---|---|---|
| PC-1 | `ClaudeLaunchArgs.Effort(level, role, byRole)` ignores the map | `ClaudeEffortByRoleSettingsTests.the_role_override_wins_and_absent_roles_keep_the_tier`, `ClaudeEffortByRoleDispatchTests.a_code_task_launches_at_the_overridden_effort_while_review_keeps_the_tier` |
| PC-2 | validator skips the value check | `ClaudeEffortByRoleSettingsTests.An_unknown_effort_or_role_fails_validation_naming_the_setting` (value rows) |
| PC-3 | validator skips the role check | same test (role rows) |
| PC-4 | dispatcher passes `ClaudeLaunchArgs.Effort(task.ModelLevel)` again | `ClaudeEffortByRoleDispatchTests.a_code_task_launches_at_the_overridden_effort_while_review_keeps_the_tier` |
| PC-5 | warm-reuse decline removed | `ClaudeEffortByRoleDispatchTests.a_role_override_that_differs_from_the_tier_declines_warm_reuse` |
| PC-6 | escalation override note removed / `LaunchEffort` ignores the role | `ClaudeEffortByRoleDispatchTests.escalating_an_overridden_claude_task_names_the_override` |

### Unlisted (stated in advance)

One dev build (`bin-c8333dev/`) through `scripts/build-slot.ps1` and one method-scoped run of
the new classes at S2 as the red witness; the output is deleted before the checkpoint runs.
`dotnet run --project tools/Antiphon.Checkpoints -- import --plan` builds the tool once to
validate this table.

## Operator overlay (not shipped; per-machine)

`appsettings.Development.json` (gitignored) or `dotnet user-secrets --id antiphon-server`:

```json
"Delegation": {
  "ClaudeEffortByRole": {
    "Investigate": "xhigh", "Plan": "xhigh", "TestDesign": "xhigh", "Code": "xhigh",
    "Mutation": "xhigh", "Debug": "xhigh", "Coverage": "xhigh", "Merge": "xhigh",
    "Docs": "xhigh", "Commit": "xhigh", "Test": "xhigh", "Deploy": "xhigh",
    "Custom": "xhigh", "Distill": "xhigh", "Diagnose": "xhigh"
  }
}
```

Review is deliberately absent, so it keeps the tier's effort. Check is absent too: Check tasks run
on the standing AlwaysOn Check seat, which ignores the setting (CARD-1481 repair,
`2026-10-22-claude-effort-by-role-repair.md`). Restart the AppHost after setting it.

## rc port

The AppHost runs `rc/2026-10-20`. Whether S2+S3 cherry-pick cleanly onto `origin/rc/2026-10-20`
is reported by the Code task (per-commit `merge-tree --merge-base`); the plan doc itself is
dropped from an rc pick (rc carries no `docs/superpowers/plans`).
