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
| __AFTER_ROWS__ |

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
__EXECUTE_RESULT__

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

__TEMP_RESULT__

## 4. Docker build cache / dangling images

Reported only (see section 1); nothing pruned. Build cache 23.6GB (all inactive), images 1.086GB
reclaimable, and `hermes-dashboard`'s writable layer 45.4GB.

## 5. Result and recommendations

__RESULT__
