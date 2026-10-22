# Claude effort by role: pinned-placement repair (CARD-1481, task baa2812c)

Repair of task 8333e231 (`2026-10-22-claude-effort-by-role-plan.md`) after Final Review df795c38.

## Defect

D-1: the `Delegation:ClaudeEffortByRole` warm-reuse decline sat at the top of
`AgentTaskDispatcher.TryReuseWarmAgentAsync`, before the pinned-agent branch. Once an override was
set, any ClaudeCode task pinned to an agent got `SpawnFresh` instead of its placement:

- (a) a Check/Low task pinned to the AlwaysOn standing Check seat: empty map -> the seat's
  placement, `{Check: xhigh}` -> `SpawnFresh` (skipping the seat's compaction admission, identity
  checks and one-task-at-a-time rule);
- (b) a Docs/Medium task pinned to a busy pool delegate: empty map -> `WaitForAgent`,
  `{Docs: xhigh}` -> `SpawnFresh` (a second session mid-task).

Advisory: the validator accepted a defined numeric key (`2`) and a comma key (`Plan,Code`)
through `Enum.TryParse`; the launch-time lookup is by name, so both were silently ignored.

## Fix

- The decline (`DeclinesReuseForClaudeEffort`) applies only to pool placement: in the unpinned
  pool search, and for a pinned POOL delegate only after the busy check (`WaitForAgent` first) and
  before it is taken. A free pinned delegate launched at another effort still relaunches.
- Never for a standing agent (`PlaceOnStandingAgentAsync`): a standing seat runs at its own launch
  effort. `docs/agent-kinds.md` says so, and Check leaves the documented overlay.
- The validator accepts a key only when it equals an `AgentTaskRole` name (case-insensitive,
  trimmed), with the existing literal unknown-role message.

## Slices

- R1 (red witness): three pinned-placement tests in `ClaudeEffortByRoleDispatchTests` and two
  validator cases (`defined-numeric-role`, `comma-separated-roles`). Witnessed red at
  `ae4fa6f0c` on one narrow build-slot run: 4 failed (both validator cases `result.Failed` false;
  standing seat `Reused` expected, `SpawnFresh` actual; busy pinned `WaitForAgent` expected,
  `SpawnFresh` actual), 5 passed (the empty-map controls inside those methods, the free-pinned
  test and the existing validator cases).
- R2 (fix): dispatcher, validator, docs, overlay.
- R3: the task report (`.antiphon/task-baa2812c.md`, force-added), committed before the last
  checkpoint run so the final tip is the tested source.

## Verification design

- V-7 standing seat: a Check/Low task pinned to an AlwaysOn standing seat with a live session is
  `Reused` into that session under both the empty map (control) and `{Check: xhigh}` —
  `ClaudeEffortByRoleDispatchTests.a_check_task_pinned_to_its_standing_seat_keeps_the_seats_placement_under_an_override`.
- V-8 busy pinned pool delegate: `WaitForAgent` under both the empty map (control) and
  `{Docs: xhigh}` — `...a_task_pinned_to_a_busy_pool_delegate_still_waits_for_it_under_an_override`.
- V-9 free pinned pool delegate: empty map `Reused` (control), `{Docs: xhigh}` `SpawnFresh` — the
  decline is kept where it is right — `...a_task_pinned_to_a_free_pool_delegate_launched_at_another_effort_relaunches`.
- V-10 validator: `2` and `Plan,Code` fail with the unknown-role message —
  `ClaudeEffortByRoleSettingsTests.An_unknown_effort_or_role_fails_validation_naming_the_setting`.
- V-1..V-6, R-1, R-2 of the original plan, unchanged, at the new counts.
- Not run: the Unit lane and whole-suite runs (brief: "Minimal testing, no whole-suite runs",
  40-minute budget; the change is confined to `TryReuseWarmAgentAsync` and the validator loop);
  `DelegateLaunchArgvIntegrityTests` (skips off Windows). No manual acceptance.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | R2 | `tests/Antiphon.Tests -> bin-c1481cp/` | role-effort-unit | `/*/*/ClaudeEffortByRoleSettingsTests/*` | V-1, V-2, V-10 | all listed, 0 failed | 18 | 9 |
| CP-2 | R2 | CP-1 | role-effort-dispatch | `/*/*/ClaudeEffortByRoleDispatchTests/*` | V-3, V-4, V-5, V-6, V-7, V-8, V-9 | all listed, 0 failed | 10 | 3 |
| CP-3 | R2 | CP-1 | claude-args-pins | `/*/*/(ClaudeLaunchArgsTests*)\|(TierReasoningEffortAgreementTests*)\|(RolePolicyConfigurationValidationTests*)/*` | R-1 | all listed, 0 failed | 28 | 2 |
| CP-4 | R2 | CP-1 | delegate-dispatch-affected | `/*/*/(GrokDelegateDispatchTests*)\|(CodexDelegateDispatchTests*)\|(CardSpawnModelArgumentTests*)/*` | R-2 | all listed, 0 failed | 70 | 6 |

### Cost

9 + 3 + 2 + 6 = 20 minutes of checkpoint time, run once at the R3 tip (the report is committed
first, so the receipt validates against the reviewed SHA).

### Positive controls (pending for SourceLanding Mutation)

| PC | Mutation | Red test |
|---|---|---|
| PC-7 | decline moved back above the pinned branch | V-7 and V-8 tests |
| PC-8 | pinned-pool decline moved above the busy check | V-8 test |
| PC-9 | pinned-pool decline removed | V-9 test |
| PC-10 | validator back to `Enum.TryParse` + `IsDefined` | V-10 rows `defined-numeric-role`, `comma-separated-roles` |

PC-1..PC-6 of the original plan stay pending; PC-5 (unpinned decline removed) now targets the
`else if (_settings.PoolEnabled)` call site.

### Unlisted (stated in advance)

One dev build (`bin-c1481dev/`) and one method-scoped run through `scripts/build-slot.ps1` as the
R1 red witness (deleted before the checkpoint run); `import --plan` builds the tool once.

## rc port

R1+R2 (and the 8333e231 commits) pick onto the current rc; this plan is dropped from an rc pick.
