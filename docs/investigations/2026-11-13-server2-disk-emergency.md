# server2 disk emergency (2026-11-13, Deploy task 918ceea9)

Trigger: server2 root filesystem (`/dev/mapper/ubuntu--vg-ubuntu--lv`, 705G) at 90% with 72G free at
15:10Z, falling with 5-7 agent tasks running; `WorkspaceDiskAlarm:MinFreeGiB` floor is 50 GiB.
Authority: orchestrator operational autonomy (disk emergency, take the recommended action).

## 1. Measurement (read-only)

`df -B1 /` readings (bytes available):

| UTC | Avail (GB) | Avail (GiB) | Note |
|---|---:|---:|---|
| 15:11:39 | 77.08 | 71.8 | first reading |
| 15:14:56 | 76.30 | 71.1 | |
| 15:26:01 | 71.16 | 66.3 | last reading before any reclaim |
| 15:42:55 | 131.89 | 122.8 | after execute run 7cc7519e (25 trees) |
| 15:51:53 | 166.50 | 155.1 | after scheduled run 1591b561 (21 trees) |
| 15:55:05 | 164.94 | 153.6 | growth check after reclaim |

Growth before reclaim: 5.92 GB in 14.4 min = **~24.7 GB/h (~23 GiB/h)**, about double the 12 GB/h in the
brief. At 15:26Z that left ~16 GiB above the 50 GiB floor, i.e. **~40 minutes**.

Docker volume sizes (`sudo du -sxB1M /var/lib/docker/volumes/<v>/_data`, 15:15-15:17Z):

| Volume | MiB |
|---|---:|
| antiphon-runner_work | 269,967 (worktrees 269,162; evidence 976; review-evidence 928; repos 586) |
| antiphon-runner_runner-tmp | 110,529 |
| antiphon-runner_runner-state | 32,424 |
| antiphon-runner-temp_work (temp runner, untouched) | 17,991 |
| antiphon-runner_dind-data | 7,312 |
| antiphon-runner-temp_runner-tmp (untouched) | 4,879 |
| antiphon-runner-cache-nuget-packages | 4,368 |
| antiphon-runner-cache-npm-content | 1,073 |
| antiphon-runner-temp_dind-data (untouched) | 380 |
| antiphon-runner-temp_runner-state (untouched) | 51 |
| antiphon-runner-cache-nuget-scratch | 31 |

`docker system df` (report only, nothing pruned): images 30.05GB (1.086GB reclaimable), containers
55.94GB (9.455GB reclaimable), local volumes 466.9GB (24.07GB reclaimable), build cache 23.6GB (516
entries, 0 active, all 23.6GB reclaimable). Of the container writable layers, **`hermes-dashboard` holds
45.4GB** (not Antiphon; out of scope, reported only); `playwright-vnc` 1.12GB.

Top 15 task trees (`/work/worktrees`, MiB): task-f1903e95 5424, task-3ca2caa6 5033, task-9124df63 4998,
task-ff777e65 4936, task-58c3a29f 4542, task-990531c3 4284, task-58f85687 4262, task-2b7ef7b4 4106,
task-042b61c0 3937, task-b4b08977 3935, task-e4299096 3831, task-ee621172 3828, task-ec9dfd70 3810,
task-ff132917 3809, task-371692c7 3807. 176 `task-*` trees, 264.2 GiB in total (mean 1.5 GiB, the big ones
3.5-5.5 GiB).

Top 15 of runner `/tmp` (MiB): claude-1654 28210, c1877-comp-b 4668, c1877-comp-a 4668,
antiphon-review-d1a9978d 3115, card1873-review-d16edc11 3109, antiphon-review-ff132917 2704,
card1862-s1-b4ab4c34 2696, c2cf0b8a-review 2696, card-1875-master-scratch 2445,
card-1850-s2-review-scratch 2409, card-1820-s1r3e-review-master 2361, card-1820-s1r6d-review-253bead91 2360,
c1836-s4a-combined-53820e0f 2360, antiphon-master-8592d536 1990, task-08e79898-stream-target-red 1771.
By prefix: claude scratch 28.2G (842 dirs, **772 of them = 18.7 GiB belong to task trees that no longer
exist**), c487-probe 8.4G (175), rhp-* 7.5G (11), antiphon-review-* 6.4G, ad-hoc review/master scratch
clones (card*/c18*-review*, *-master-*) ~25G, antiphon-bridge-queue-* 4.0G (4023 entries),
c502-exit-cap 3.9G, antiphon-land-verify 1.9G, c487-tw 2.6G, c459 2.6G.

What fills a task tree (sampled task-58c3a29f 5586M and task-ff777e65 4936M): `bin/` 3659M / 2278M,
`obj/` 455M / 526M, node_modules 0, `.git` (worktree file) ~1M, `.antiphon` 47M / 59M; by directory
`tests/` 4760M / 4006M. **bin+obj are 74% / 57% of the tree, almost all under `tests/`**; the source
itself is ~0.6 GiB.

## 2. Worktree retention (CARD-1677)

The 24 h floor was an out-of-repo override in the **desktop server's user secrets**
(`%APPDATA%\Microsoft\UserSecrets\antiphon-server\secrets.json`, key
`RunnerWorktreeRetention:MinTerminalHours` = `"24"`, file last written 2026-11-06 22:24Z). The retention
reads settings through `IOptionsMonitor`, and user secrets reload on change, so no restart is needed.

Change (15:16:46Z), reversible:

```
dotnet user-secrets set "RunnerWorktreeRetention:MinTerminalHours" "12" --id antiphon-server
# backup of the prior file beside it: secrets.json.bak-918ceea9-20261113 (56 keys before and after)
# revert: dotnet user-secrets set "RunnerWorktreeRetention:MinTerminalHours" "24" --id antiphon-server
```

Preview before (run bac2171e-5c7c-4051-90e4-9bc603a64be6): examined 420, eligible 8, backedOff 8, sent 0,
`minTerminalHours` 24. Preview after (run b74281af-8737-45b8-a75e-10a1615720af): examined 420,
**eligible 52**, backedOff 8, sent 25 (dry run), queued 19, `minTerminalHours` 12.

Preview verdicts joined with the tree sizes:

| Verdict | Trees | GiB on disk |
|---|---:|---:|
| Held settling (< 12 h) | 73 | 102.0 |
| Held sync_debt | 215 | 50.0 (46 trees still on disk) |
| queued_next_tick | 19 | 47.9 |
| would_remove | 25 | 41.3 |
| live_owner | 4 | 12.6 |
| kept_recheck_pending | 8 | 10.1 |
| mutation_excluded | 26 | 0.0 |

Execute: the operator one-shot `POST .../runner-retention/server2/execute` (operator token read from its
file, never printed), 202 at 15:26:44Z, run 7cc7519e-2714-4014-ba1a-c9719da2c64d.
It finished at 15:42:43Z with **25 removed, 0 refused, 0 partial, 0 deferred** (no RecycleUnpublishedWork,
RecycleWorktreeDirty, RecycleGitAuditUnknown or other refusal). Free space went 71.16 -> 131.89 GB.

My second one-shot at 15:43:16Z got 409 `tick_in_progress`: the scheduled 15:30 tick had waited for mine
and started at 15:42:43Z (run 1591b561-7025-4c58-8e6f-cb394b4e7265, Execute). It took the 19 queued trees
plus two that had just crossed 12 h: **21 removed, 0 refused, 1 deferred (`temp_report_truncated`, see
section 3)**, finished 15:51:39Z. Free space went 131.89 -> 166.50 GB.

`/work/worktrees` went from 269,162 MiB to 180,152 MiB (-86.9 GiB, 46 trees). A follow-up preview
(run 7f6c7e1e-88a1-4bb9-a23d-ddfd6a5766c8, 15:52Z) shows eligible 8, all backed off (`kept_recheck_pending`),
nothing more to send. Further trees become eligible as the 73 settling trees (102 GiB) pass 12 h; the
scheduled `*/30` tick takes them (Execute=true is the shipped value).

### sync_debt holds (not cleared; reported)

`RunnerWorktreeRetentionService` treats `AgentTaskSyncDebts.State` in {Pending, Ready, Held} as open
(`OpenDebt`, `RunnerWorktreeRetentionService.cs:48-49`, check at :770). For the 215 held tasks the open
debts are:

| State | Reason | Debts | Trees on disk | GiB |
|---|---|---:|---:|---:|
| Ready | settlement_sync_ready | 193 | 27 | 31.7 |
| Held | settlement_sync_source_changed | 21 | 19 | 18.4 |
| Held | settlement_sync_episode_changed | 1 | 0 | 0 |

- **Ready (193): the debt is already paid.** Every one has `ConfirmedSha == SourceSha` (the desktop
  fast-forward was confirmed; `CardSiblingInventory.UnpaidSyncDebtsAsync` documents "Ready is paid" and
  excludes it). Retention alone counts Ready as open, so these finished, published trees are held forever.
  **Defect** (same `OpenDebt` list in `DesktopLegacyReleasePolicyService.cs:57`). Fix: treat Ready (with a
  confirmed SHA) as paid in both retention policies. Nothing to publish; no task to ask.
- **source_changed (21):** for all 21 the task branch on origin equals the debt's `SourceSha` (checked with
  `git ls-remote origin`), and the desktop checkout `C:\Antiphon\worktrees\card-task-<id>` still sits at
  `BaselineSha`, i.e. the owed desktop fast-forward was never applied. **This is not a missing push**: every
  branch is published. The recovery sweep re-holds them hourly (Attempts 1, NextAttemptAt 15:41-16:11Z).
  Tasks: 20805524, 2abcd1c0, 4292c713, 4f5166ec, 5b1ea772, 6155693e, 7555284f, 934cdb25, 94b17435,
  a2845b6c, b0e54eca, bc7a9fdd, bee64303, c6ca6929, c7a30141, da90742c, e7d7d78b, ec9dfd70, ed0f7942,
  f442a78f, f87fdda9. No task needs to be asked to publish.

## 3. Runner /tmp sweep (CARD-1677 S2)

The sweep runs only inside a retention tick and only with `TempSweep:Enabled=true`; the preview includes it
then, and the one-shot execute keeps it DryRun. To get its preview I added two user-secrets keys at
15:43:13Z, `RunnerWorktreeRetention:TempSweep:Enabled=true` and `...:TempSweep:Families:claude-scratch=true`,
with `TempSweep:Execute` left at the shipped `false`, so it could only dry-run. The scheduled tick 1591b561
and the preview 7f6c7e1e both ran it. I **removed both keys again at 15:53:39Z** (the secrets file is back to
56 keys, with only `MinTerminalHours=12` under `RunnerWorktreeRetention`).

Dry-run result (run 7f6c7e1e, `RunnerTemp` lane):

| Outcome | Family | Rows | GiB |
|---|---|---:|---:|
| would_remove | rcspeed | 199 | 0.01 |
| would_remove | pty-test-root | 1 | 0.00 (run 1591b561) |
| too_young | bridge-queue | 488 | 0.00 |
| too_young | pty-test-root | 12 | 0.00 |
| unregistered | (all) | 33,677 | 10.80+ (byte bound hit) |
| Deferred `temp_report_truncated` | - | 1 | - |

**Not run.** It would free ~0.01 GiB: `MaxEntriesPerTick` (200) is used up oldest-first by tiny `rcspeed`
entries. The dry-run list is also incomplete: the report was truncated (`temp_report_truncated`), and no row
at all came back for families that are present on disk and match their regexes: `c487-probe` (175 entries,
8.4G, e.g. `c487-probe-009886dab56a407d83af4c13d034d2c7`), `claude-scratch` (842 dirs, 772 of them, 18.7 GiB,
for task trees that are already gone), `c502-exit-cap` (3.9G), `land-verify` (1.9G), `c514-pty` (1.0G) and
`c1514`. An unread list does not meet the "read the dry-run list first" rule, so the sweep stayed off.

Defects seen:
- **Unregistered entries are not aggregated.** The docs promise one row per normalized prefix, but the run
  wrote 33,677 `unregistered` rows, mostly one per random `mkstemp` name (`/tmp/38RtFK`, `/tmp/u2ti1A`,
  `clr-debug-pipe-*`). That truncates the report and drops the family rows, and every enabled tick would add
  ~34k residue rows.
- **The 200-entry bound is oldest-first across families,** so thousands of KB-sized entries
  (`rcspeed`, `bridge-queue` 4023, `c487-tw`, `c459`) starve the GB-sized ones. It needs a byte-weighted or
  per-family fair order.
- **The big `/tmp` consumers are unregistered names:** review/master scratch clones (`antiphon-review-*`
  6.4G, `card*-review*`, `card-*-master-scratch`, `c1877-comp-a/b` 9.3G, `rhp-*` 7.5G, `rh-*` 1.8G,
  `antiphon-master-*` 2.0G; about 35G together). Briefs that tell agents to clone review scratch into `/tmp`
  create these, and no sweep or retention owns them.

## 4. Docker build cache / dangling images

Reported only (see section 1); nothing pruned. Build cache 23.6GB (all inactive), images 1.086GB
reclaimable, and `hermes-dashboard`'s writable layer 45.4GB.

## 5. Result and recommendations

| Step | Freed (net of growth) |
|---|---:|
| 2a. Execute one-shot 7cc7519e (25 trees) | +60.7 GB (71.16 -> 131.89) in 16 min |
| 2b. Scheduled tick 1591b561 (21 trees) | +34.6 GB (131.89 -> 166.50) in 9 min |
| 3. /tmp sweep | 0 (not run) |
| 4. Docker cache | 0 (report only) |
| **Total** | **71.16 GB (66.3 GiB) -> 166.50 GB (155.1 GiB) free; 90% -> 78% used**; worktrees -86.9 GiB |

Growth after the reclaim: 166.50 -> 164.94 GB in 3.2 min (15:51:53 -> 15:55:05), ~29 GB/h; before it,
~24.7 GB/h. **Projected time to the 50 GiB floor** from 153.6 GiB: **~4-4.5 h at 23-27 GiB/h with no
retention**. With the scheduled 12 h ticks working through the 73 settling trees (~102 GiB over the next
12 h, ~8.5 GiB/h), the net decline is ~15-18 GiB/h, about **6-7 h**. That is still not a steady state.

### Recommendations (in order)

1. **Fix the paid-sync-debt hold (Code card, high).** `RunnerWorktreeRetentionService.OpenDebt` and
   `DesktopLegacyReleasePolicyService` (line 57) count `Ready` as open debt. That holds 193 tasks whose debt
   is paid (`ConfirmedSha == SourceSha`); 27 of their trees are still on disk (31.7 GiB), and every future
   task with a debt is held forever. Treat Ready as paid, as `CardSiblingInventory` already does.
2. **Drop build outputs at terminal time (Code card, high; this is the real fix).** bin+obj are 57-74%
   of a finished tree, almost all under `tests/`. The source is ~0.6 GiB, the outputs 2.8-4.1 GiB. Neither
   CARD-1677 retention (whole-tree move only) nor the runner deletes bin/obj of terminal trees: the
   `WorktreeCleanup:DisposableIgnored` classifier (bin/obj/node_modules/dist) is desktop-only. A runner
   op that deletes ignored `bin/` and `obj/` (`git clean -X` limited to those globs, holder-scanned) on a
   terminal task's tree, at settlement or at the first retention tick, would cut the 12 h settling window
   from ~100 GiB to ~30 GiB.
3. **Keep 12 h.** The 24 h user-secrets override is gone (key now explicitly `12`). Deleting the key with
   `dotnet user-secrets remove "RunnerWorktreeRetention:MinTerminalHours" --id antiphon-server` gives the
   same 12 from `server/appsettings.json`. Revert to 24 only on purpose:
   `dotnet user-secrets set "RunnerWorktreeRetention:MinTerminalHours" "24" --id antiphon-server`.
4. **Temp sweep (Code card):** aggregate unregistered names per prefix (stop the 34k-row reports and the
   truncation), use a byte-weighted or per-family fair order under the 200-entry bound, then re-read a
   complete dry-run list before enabling `claude-scratch` (18.7 GiB orphaned) and `c487-probe` (8.4G).
   Register, or move under the task tree, the review/master scratch clones (~35G unregistered).
5. **Report-only items for the operator:** the Docker build cache is 23.6GB, all inactive (prune is an
   operator call). `hermes-dashboard` has a 45.4GB writable layer (not Antiphon). The runner-state
   `session-runner/*.ansi.log` files total 5,249, of which 4,274 (14.5 GiB) are older than 7 days with no
   retention. The four temp-runner volumes (~23 GB) wait on the retire-temp gate-9 fix (Review 01e2734e).
6. The 21 `settlement_sync_source_changed` debts are published branches whose desktop checkout never
   fast-forwarded. They are a sync-recovery question, not a disk action; no task needs to publish anything.

Read-only measurement files are on server2 at `/home/mc/disk-emergency-918ceea9/` (measure.sh, volumes.txt,
worktrees.txt, tmp.txt, work-depth1.txt).
