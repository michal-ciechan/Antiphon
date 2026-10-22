# Review df795c38 — CARD-1481 `Delegation:ClaudeEffortByRole` (Final), subject 8333e231

Outcome: **found**. One regression reachable with the operator's intended map. All checkpoint rows green and validated at the reviewed SHA.
Reviewed source `feat/card-task-8333e231` @ `0e8be8391245d51ce506bfcbd2e9c93bd0257641` (base `60f594b762e9a1b2d5d77712c0a7e70d1fd4f2b8`).

## Defect D-1 (regression): the warm-reuse decline runs before the pinned/standing-agent branch

- **Where:** `server/Application/Services/AgentTaskDispatcher.cs:8002-8009`, `TryReuseWarmAgentAsync`. The new
  `RoleOverrideDiffersFromTier` decline returns `SpawnFresh` at the top of the method. That is before the
  `claimed.AgentId is Guid pinnedId` branch, so it also runs before `PlaceOnStandingAgentAsync` and before the
  busy-pin `WaitForAgent`.
- **Failure:** any ClaudeCode task pinned to an agent, where the role override differs from the tier's effort, now
  gets `SpawnFresh`. `ResolveAgentAsync` then returns the pinned row and the dispatcher cold-launches a **new
  session on it**. Two witnesses (throwaway, in a scratch worktree, never committed), both with green empty-map
  controls:
  - `zz_review_pinned_standing_check_seat_keeps_its_placement_under_the_override`: a Check/Low task pinned to an
    AlwaysOn standing seat. Empty map gives `WaitForAgent`; `{Check: xhigh}` gives **`SpawnFresh`**.
  - `zz_review_pinned_busy_pool_delegate_is_waited_for_under_the_override`: a Docs/Medium task pinned to a Running
    pool delegate. Empty map gives `WaitForAgent`; `{Docs: xhigh}` gives **`SpawnFresh`**.
- **Why it matters:** the operator's map includes `Check`.
  - Check tasks are always pinned to the standing Check seat (`SpecialistRequestService.cs:233`,
    `SpecialistTaskRunner.cs:343`). That seat is born `ModelLevel = Low` (`StandingSpecialistProvisioner.cs:87`),
    so `low` is not `xhigh` and every Check dispatch skips the standing-seat rules:
    - `CheckCompactionAdmission.ClosesSeatAsync`
    - specialist identity/generation checks
    - one task at a time on the live composer (the CARD-0079 occupancy rule)
  - Instead it launches a delegate session on the seat row.
  - The same thing happens to a follow-up pinned to a busy pool delegate of any overridden role (every
    non-Review role at High/Medium/Low): a second session starts on a Running agent mid-task.
  - With an empty map nothing changes. So this is not an empty-map regression, but it is reached the moment the
    intended overlay is applied.
  - The override cannot even take effect on the Check seat through the normal path: standing seats launch via
    `AgentSessionLaunchComposer`, which carries no role.
- **Fix:**
  - Unpinned branch: apply the decline only there (filter or skip the candidate).
  - Pinned pool delegate: apply it only after the `Status != Idle → WaitForAgent` check, just before `agent = pinned`.
  - Standing agents: never apply it (leave `PlaceOnStandingAgentAsync` untouched).
  - Add both witnesses above as regression tests.
  - Recommend dropping `Check` from the documented overlay, or saying that standing Check seats ignore it.
  - Landing owner stays Code task 8333e231.

## Brief checks

1. **Empty map is byte-identical.**
   - Every path short-circuits when the map is empty: `Effort(level, role, null|empty)` returns `Effort(level)`,
     `RoleOverrideDiffersFromTier` is false, and `ClaudeEffortOverrideNote` is empty.
   - Codex and Grok `LaunchEffort` arms are unchanged.
   - Pinned by V-1 (every level × role, for both empty and null) and V-4 (literal argv, element-for-element). OK.
2. **Injection.**
   - Only a `CliEfforts` catalog constant reaches argv. `Canonical` trims and matches case-insensitively.
   - Spaces, quotes, `--` and empty strings return null and throw, or fail the validator. `XHIGH` becomes `xhigh`.
   - `ValidateOnStart` (`Program.cs:170-172`) refuses startup with the literal messages.
   - Advisory: the validator's `Enum.TryParse` accepts numeric keys (`"2"`) and comma-combined keys (`"Plan,Code"`,
     which equals 3, which is `Review`). `TryGetOverride` matches by name only, so these entries are silently ignored.
     They never reach argv or Review. Tightening it would mean requiring `Enum.GetNames` membership.
3. **Warm-pool residual** (a task without an override reusing a warm session launched overridden).
   - It is reachable: the unpinned pool predicate matches tier, kind, scope, runner, directory and env, never role.
   - But only for **Shared** Review tasks in the same directory. Worktree tasks never warm-reuse, and the pipeline's
     Review is Worktree.
   - With the operator's all-xhigh map the effect is only **more** effort on Review (xhigh instead of high, medium
     or low).
   - The reverse direction (an overridden role reusing a Review-launched process) is blocked by the decline (V-5).
   - The plan and `agent-kinds.md` §3 document it. Disclosure only.
   - Note: because overridden roles always decline, the pool fills with xhigh delegates whose only consumers are
     non-overridden roles. Backlog suggestion: record the launch effort on the pool agent.
4. **Escalation and effort text** is truthful for fresh launches. The Claude same-model arm is unreachable because
   all aliases are distinct.
5. **Tests.**
   - The red record is legitimate: 17 of 23 failed on their own assertions; 6 compatibility pins stayed green.
   - The repaired CP-2 warm test has two directories with the control first, and asserts
     `AgentId != warm` and `warm.Status == Idle`.
   - Throwaway mutation (the raw operator string reaches argv):
     `ClaudeEffortByRoleSettingsTests.the_value_is_case_insensitive_and_the_catalog_constant_reaches_argv` went red
     3/3 (`should be "xhigh" but was "XHIGH"`, `" max "`, `"Low"`).
6. **Overlay lines.** 16 keys = all 17 `AgentTaskRole` names minus Review (Check, Distill, Diagnose included).
   `Delegation:ClaudeEffortByRole:<Role>` binds to the string-keyed dictionary. Correct, but see D-1 about `Check`.

## Receipt

Run `20261022-113813-9fd9`, GREEN exit 0. Every row has `sourceState=clean` and `buildSource=verified` at `0e8be8391`;
`validate-checkpoint-receipt.ps1` printed `CHECKPOINT SOURCE VALID rows=4`.

| Row | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| CP-1 | 16 | 16 | 0 | 0 |
| CP-2 | 7 | 7 | 0 | 0 |
| CP-3 | 30 | 30 | 0 | 0 |
| CP-4 | 76 | 76 | 0 | 0 |

- `check-evidence-diff.ps1` over 60f594b76..HEAD: commits=7, violations=0.
- `git merge-tree --write-tree origin/master(08765c68c) HEAD`: clean.
- rc: the five commits cherry-pick cleanly onto `origin/rc/2026-10-20` (`ceb6e3d6b`) in a discarded scratch worktree.

### Runs outside the checkpoint tool

All went through `build-slot.ps1`, in a scratch detached worktree that has since been removed:

- one checkpoint-tool build;
- one `Antiphon.Tests` build carrying the mutation plus the witnesses;
- one filtered run: 5 executed, 5 red as designed. The two witness controls passed.

### Not run

The Unit lane. The plan's closed list excludes it, and the brief named CP-1..CP-4 as the required receipt.
`DelegateLaunchArgvIntegrityTests` was not run either (Windows-only). PC-1..PC-6 stay pending for SourceLanding
Mutation.
