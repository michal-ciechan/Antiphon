# Review 4520ae2c: CARD-0167 S3 repair 2 (Final, Full)

Subject: Code task 8c2a794e-d6ff-422c-afb6-78104239c66d, feat/card-task-8c2a794e at a36448ab40cfc651010b730b13f0b5b5a6a4b9b3 (repair-2 start ref 81937b7e5; S3 range a38e1e1bd..a36448ab4).

Verdict: clean under the operator verdict policy. R1 is closed and nothing turned up in the regression, mix-up, secret, save/launch, migration or test-adequacy categories. Landing is NOT mechanical any more (see Landing).

## Receipt (checkpoint tool, server2)

Run 20261022-133031-f66c, `--rows CP-1,CP-2,CP-3,CP-4,CP-5`, one build (bin-c0167r2, deleted by the tool). Verdict GREEN exit=0; unlisted: none.

| CP | executed | passed | failed | skipped | state |
|---|---:|---:|---:|---:|---|
| CP-1 | 37 | 37 | 0 | 0 | clean/verified |
| CP-2 | 12 | 12 | 0 | 0 | clean/verified |
| CP-3 | 62 | 62 | 0 | 0 | clean/verified |
| CP-4 | 25 | 25 | 0 | 0 | clean/verified |
| CP-5 | 43 | 43 | 0 | 0 | clean/verified |

`scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261022-133031-f66c/report.json -ExpectedSourceSha a36448ab4...` gave `CHECKPOINT SOURCE VALID source=a36448ab40cfc651010b730b13f0b5b5a6a4b9b3 rows=5`, exit 0. The review tree is clean.
Advisory: the plan's CP-5 `Min` is 35, but the row runs 43. The floor is lax, not wrong.

## CP-6 diagnostic (outside the receipt, build-slot `dotnet run`, detached scratch worktrees)

The filter `/*/*/(AgentTaskPoolTests*)|(BootWedgeRelaunchParityTests*)/*` gave the same result at both commits:
- base 81937b7e5: 50 total, 28 passed, 22 failed
- tip a36448ab4: 50 total, 28 passed, 22 failed

The two sorted failing-name lists are identical: the C691 sweep x7, janitor x4, release-sweep x6, `a_busy_standing_agent_writes_one_held_event`, `a_mid_turn_warm_agent_is_not_reused_by_the_unpinned_shop`, `the_per_directory_cap_skips_a_mid_turn_agent`, `the_hold_retraces_on_reply_and_releases_on_settle` and `unrelated_work_compacts_the_session_first_focused_on_the_new_task`. Both BootWedgeRelaunchParity cases pass, and nothing is new at the tip.

## Throwaway mutations (scratch worktree, restored, porcelain empty after)

- **A1.** Drop the stamp in BuildLaunchSpecAsync (`if (agent.IsPoolDelegate && agent.Id == Guid.Empty)`).
- **A3.** Move the control-start stamp block above the reservation's `ReloadAsync`.

A1 and A3 ran together in one build because they touch different files. All 3 named witnesses went red on assertions:
- `C0167_cold_launch_stamps_the_map_its_launch_spec_read`: the stamp was "unknown", F' expected.
- `C0167_boot_wedge_relaunch_restamps_after_the_endpoint_is_set_changed_and_cleared`
- `C0167_start_restamps_a_pool_rows_provider_projection`: the stale stamp survived the reload.

- **B.** Stamp from the claim-time read: restore the repair-1 claim stamp and turn the spec-time stamp off. `C0167_cold_launch_stamps_the_map_its_launch_spec_read` went red because the stamp held F's fingerprint while the spec held F'.

The failure text shows only `{{key:NAME}}` placeholders and URLs.

## Check findings

1. **R1/D1 closed.** Every process start of a pool row restamps from its own read or marks the row unknown:
   - **Cold dispatch.** The claim sets "unknown", commits, then reaches FinishDispatchedLaunchSpecAsync. BuildLaunchSpecAsync stamps from the `endpoints` it passes to BuildLaunchSpec, saves, and only then is the launch enqueued.
   - **Boot-wedge relaunch.** The relaunch commit sets "unknown", then takes the same Finish path, then enqueues.
   - **Control start/resume.** The stamp comes from `resolved.ProjectProviderEndpoints`, the options read of the funnel that produced `spec`, and is set after the reservation's agent reload and the UpdatedAt check. Both arms save afterwards, and only the `previous` session and the intent are reloaded in between.
   - **Herdr adopt.** Marked "unknown".
   - **Warm reuse** starts no process, so the stamp stays right.
   - **NULL stamp.** It reads as "launched with nothing". That is true, because before S3 (4135dcb83, same delta) nothing was projected.
   - **Runner-bound (CARD-0628).** `PhoneHomeLaunchPolicy.Project` only refuses credential names and sets the homes. It never touches the endpoint env.
   - **Profile branch.** If a pool row ever reached it, the row would stay "unknown", which is safe.
   - **"unknown" never matches.** PoolFingerprint returns null or a JSON object, and the predicate compares Ordinal, so "unknown" matches nothing, including an empty or NULL map. The attach witness checks this against NoEndpoints.
   - **CARD-0260.** The inherit-list half is unchanged and runs first.
2. **Secrets.** The fingerprint is a SortedDictionary with StringComparer.Ordinal and is taken before placeholder resolution. The only new log line in S3 is host-only (`HostOf`, `uri.Host`, so no userinfo). The tests use the sentinel `sentinel-c0167-value-not-a-key` and assert `ShouldNotContain(KeyValue)` on the stamp.
3. **Resume arm.** The stamp statement is shared and sits before the new/resume split, inside one reservation, with no agent reload before either arm's SaveChanges. The resume arm launches the same `spec` (env unchanged). The new-session witness plus mutation A3 is sufficient. I found no reachable resume path that stamps wrong.
4. **CP-6.** See above: identical 22 at base and tip.
5. **Earlier checks.**
   - Migration 20261022110953 is AddColumn nullable text only and unchanged in repair 2. It sorts after master's latest (20261022095749) and rc/2026-10-20's latest (20261020142734).
   - The no-setting projection is null, so no-setting launches stay byte-identical (CP-3/CP-4 green).
   - The log line is host-only.
   - CP-1..CP-4 cover D2 parity, Codex -c and the runner refusals; all are green.
6. **Evidence diff.** `check-evidence-diff.ps1` reported violations=0 over a38e1e1bd..a36448ab4 (18 commits) and over 81937b7e5..a36448ab4 (10 commits).

## Landing (not mechanical)

A scratch `git rebase --onto origin/master a38e1e1bd` at origin/master 9cf77dbad exits **1**. It stops at 4135dcb83 (S3 wiring) on one content conflict in `server/Application/Services/AgentTaskDispatcher.cs`, around the predicate wrappers:
- Master's c554c0e60/47772c152 (8333e231/baa2812c ClaudeEffortByRole, 13:59 +0100) inserted `DeclinesReuseForClaudeEffort` directly above the old 2-arg `InheritedEnvMatchesPool(Agent, AgentTask) => ...projectEndpoints: null...` wrapper.
- S3 replaces that wrapper with the 3-arg `(agent, task, projectEndpoints)` form.

The carry 25ae68691 drops as already upstream. After hand-resolving that one hunk in scratch, the rest of the chain applies cleanly (17 commits remain, not the 13 the brief expected). The whole-branch `merge-tree` also reports add/add on ProviderEndpointSettings.cs and ProviderEndpointApiTests.cs (carried files). Per the carried-file pattern those drop on a rebase land, so the dispatcher hunk is the real blocker.

Correct resolution: keep `DeclinesReuseForClaudeEffort` and its two call sites, delete the 2-arg null-map wrapper, and keep the 3-arg wrapper. The two `InheritedEnvMatchesPool` call sites already auto-merge to the branch's `projectEndpoints` form.

Risk: keeping the null-map wrapper and calling it from a pool path would compare stamps against "the task projects nothing". That would be a warm-pool mix-up, so the re-cut must leave no 2-arg caller.

The land gate's rebase will refuse. A re-cut from origin/master (Code, start ref origin/master, resolving that hunk, then rerunning CP-3/CP-5 at the new tip) is expected to work.

## Cleanup

Scratch worktrees wt-base, wt-tip, wt-mut and wt-land were removed and pruned. `bin-rv4520` was deleted under tools/Antiphon.Checkpoints and src/Antiphon.HostCleanup; 0 remain.
