# CARD-1794 Code report (task 99f321fb)

Branch `feat/card-task-99f321fb`, runner mirror `/work/worktrees/task-99f321fb`, base master `7624fb6820a48805c2fb23c9d7e38f324f7cece2`.

## Commits

| Role | SHA |
|---|---|
| Red witnesses + compile shims | `ee8654d1d2bfd32f8020936fb9cb6b4ce79a8bf8` |
| Fix | `781275142931caef6f25f314f111093bff1ec90d` |
| Harness repair (other-runner inventory) = tested tip | `ed5f670509938734e1758b2f317fe24b44f37034` |

## What changed, per card item

1. **Budget order.** `RunnerWorktreeRetentionService`: with a complete runner inventory, an eligible task whose `task-<id8>` is not listed is "gone". Present trees are sent first on `MaxRemovalsPerTick` (oldest first, then least-recently-sent retries); gone ones follow on a new `MaxAbsentPerTick` (200, validator 0..1000), unpaced, not counted against the removal budget. The runner still answers `already_absent` before the path is cleared. An incomplete inventory proves nothing, so behaviour is as before. Every eligible task past either bound gets a `queued_next_tick` Held row (cursor-deduped on scheduled ticks), so no eligible tree is missing from the census. Size-ordering ("largest first") is not done: the inventory carries no byte counts; that needs a runner contract change.
2. **Stuck settling — explained.** The 2026-11-08 previews 8f1f4338/ddb57b73 (live API read): 168 settling rows, all `CompletedAt` set (167 Succeeded, 1 Failed), max age 23.98 h at run start; the youngest `sync_debt` row was 24.03 h. The deployed server applied `MinTerminalHours=24`, not the shipped 12 (repo has 12 in code and appsettings; no tracked override). So the cause is an out-of-repo configuration override on the deployed server — the operator should find and remove it. Code fix: the tick result and log line now carry `minTerminalHours`, so the preview reply shows the applied floor.
3. **Unbranched report policy.** Defined and documented (docs/docker-stack.md): the task record's `Result` is the durable report (D-9). A finished mirror whose `.antiphon/task-<id8>.md` is not in the branch is retired when `Result` holds the report; while `Result` is empty and the file lives in the mirror it stays `report_unpreserved`. No code change (existing behaviour; pinned by a new test). Copying a file-only report into the record would need a runner read op — not built; a follow-up card if those rows are numerous (the two previews had no `report_unpreserved` rows).
4. **Execute route.** `POST /api/agent-tasks/worktree-residue/runner-retention/{runnerId}/execute`: operator credential (`X-Antiphon-Operator-Token`) required, else 403 `operator_token_required`; then one Execute tick for that allow-listed runner, even while `Execute=false`. `Enabled` (kill switch), `Runners`, the tick budget and all runner checks still apply; temp sweep stays DryRun. No in-process lock against a concurrent scheduled tick: a double send is runner-verified (second answer is `already_absent`/kept), so it is safe but not deduplicated.
5. **Disk alert.** `WorkspaceDiskAlarm:MinFreeGiB` (code 10, appsettings 50, 0 off, validator 0..100000): free space below it raises the `WorkspaceDiskLow` row as **Error**, even below `UsedPercent`; the Warning at 85% is unchanged. Note: the S3 Warning already existed on the deployed rc (d164e93ae); the feed is pull-only, so nobody was told. This makes it an Error row; it still does not push anywhere.

Docs: docker-stack.md (retention section), ops-http.md and antiphon-api.md (new route).

## Tests

Witnesses (new, in existing classes):

- `C1677RetentionServerTests.C1794_*`: absent mirrors off budget; gone bound + queued row; queued row in preview; incomplete inventory control; one-shot execute while DryRun; kill switch/allow-list control (2); effective floor; report policy pin; execute route credential (3 cases).
- `C1677DiskAlarmTests.C1794_*`: free-space floor (4 cases); MinFreeGiB bounds (5 cases).

Harness change: `World.EligibleAsync` lists the seeded mirror in the inventory (present: true default, only for this rig's runner); `InventoryComplete` switch; `ExecuteOnceAsync`.

Runs (all through build-slot, native output, UseAppHost=false, server2 Linux):

| Run | SHA | Filter | Result |
|---|---|---|---|
| red | ee8654d1 | `(C1677RetentionServerTests*)\|(C1677DiskAlarmTests*)/C1794*` | 21 total, 12 failed (all assertion failures at the witnessed facts), 9 passed (controls/pins) — slot waited=0s |
| touched 1 | 78127514 | `(C1677RetentionServerTests*)\|(C1677DiskAlarmTests*)\|(C1677RetentionSettingsTests*)/*` | 149 total, 1 failed: `Each_ineligible_task_alone_is_recorded_and_never_sent(other-runner)` — my harness listed another runner's mirror in this runner's inventory; fixed in ed5f6705 |
| touched 2 | ed5f6705 | same | 149/149 passed (TRX: C1677RetentionServerTests 83, C1677DiskAlarmTests 52, C1677RetentionSettingsTests 14) |
| guards | ed5f6705 | TestClassificationPolicy*, TestClassificationGuard*, SlowTestRegistryMerge*, SlowTestTripwire*, NativeLinuxPolicyWitnessTests*, MechanicalPcContractTests*, TaskPlatformGuidanceTests*, C1729LayeringGuardTests* | 75/75 passed (TRX per class: 39/1/5/2/3/11/5/9) |

Builds: 3 leased `dotnet build tests/Antiphon.Tests` (red, fix, harness fix), each 0 errors, waited=0s.

- check-evidence-diff base..HEAD: commits=3 entries=0 violations=0.
- merge-tree vs origin/master 584224f0e: clean.
- tests/test-execution-policy.json unchanged, so no policyHash/PinnedInputDigest refresh.
- Not run: the whole Unit lane (owned by nightly/rc per docs/testing-and-build.md per-task Final), checkpoint tool (no plan with a Checkpoints table for this card).
- bin/obj outputs deleted by literal path.

## Owed / pending for Mutation (no mutants run here — budget)

- PC-a: gone partition ignored (`Gone` always false) — C1794_absent_mirrors_do_not_spend_the_removal_budget, C1794_gone_mirrors_have_their_own_bound...
- PC-b: incomplete inventory treated as complete — C1794_incomplete_inventory_proves_no_absence
- PC-c: absent sends counted in `sent` / against MaxRemovalsPerTick — C1794_absent_mirrors... (`Sent == 2`)
- PC-d: queued rows dropped — C1794_gone_mirrors..., C1794_eligible_trees_past_the_bound...
- PC-e: forced ignored (StopReason Execute check not bypassed, or DryRun mode) — C1794_one_shot_execute_sends_execute...
- PC-f: forced bypasses Enabled / allow-list — C1794_one_shot_execute_obeys_the_kill_switch_and_the_allow_list
- PC-g: `OperatorCredential.Require` removed — C1794_execute_route_requires_the_operator_credential(no-token|wrong-token)
- PC-h: `MinTerminalHours` not reported — C1794_tick_result_names_the_effective_age_floor
- PC-i: floor `<` -> `<=`, floor ignored, Error -> Warning — C1794_free_space_floor_raises_an_error (4 cases)
- PC-j: MinFreeGiB validator bounds — C1794_min_free_bounds
- Report-policy pin (C1794_report_kept_in_the_task_record...) is green at base by design (existing behaviour).

## Operator follow-ups

- Find and remove the deployed `RunnerWorktreeRetention:MinTerminalHours=24` override (env var or untracked appsettings on the serving host); confirm with a preview reply's `minTerminalHours`.
- After land + activation, `POST .../runner-retention/server2/execute` with the operator token gives the one-shot reclaim.
- 171 `sync_debt` Held rows (oldest 592 h) hold mirrors that are mostly gone; they do not spend budget but they never clear. Out of scope here.
