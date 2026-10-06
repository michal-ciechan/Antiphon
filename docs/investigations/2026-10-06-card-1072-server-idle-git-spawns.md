# CARD-1072: Antiphon.Server idle git spawns

Date: 2026-10-06. Card: Antiphon.Server spawns about 170 git processes per minute on the desktop while mostly idle. Confirmed mechanism, reconstructed from code plus a read-only fleet read. A 60-second process-start watcher was not repeated: this session is on the Linux runner, and the desktop server is production.

## Outcome

The steady cost is the dispatcher progress-stall clock. Every 5 seconds it runs a five-command git probe for every Dispatched or Working task whose `WorkingDirectory` exists, before `TaskProgressPolicy` is allowed to decide that the task is too young or not mid-turn. At 2026-10-06T19:26:28Z the fleet had four Dispatched tasks and no Working or Queued tasks. All four are `runnerId=server2-temp` worktrees whose `workingDirectory` is `C:\src\Antiphon`, not the worktree. That is 20 git starts per tick, 240 per minute while ticks stay on the 5-second cadence, all against the desktop main checkout. The 2026-10-05 sample of 169 starts in 60 seconds is the same site at a slightly smaller open set, or the same set with the tick stretched by slow git. It is not a new sweep from CARD-1065, CARD-1076, CARD-1079, or CARD-1082.

Not done, noted: the probe was not moved behind the stall gate.

## Evidence

The card's sample (Investigate task 4de0a86d, 2026-10-05) is 672 process starts in 60 seconds, 306 of them git, 169 of those children of Antiphon.Server, with the server itself at about 1.8% CPU. The report named on the card, `docs/investigations/2026-10-05-card-0795-vbcscompiler-shared-compiler-contention-isolated-builds-need-p-usesharedcompilation-false.md`, is not in this checkout. The card says that push failed authentication, so the argv trace is only on the desktop.

Read-only GET `$ANTIPHON_API/api/agent-tasks/summary` and `?status=Dispatched,Working&includeChecks=true` at 2026-10-06T19:26:28Z:

| status | count |
|---|---|
| Dispatched | 4 |
| Working | 0 |
| Queued | 0 (absent from `byStatus`) |

| task | role | runner | workingDirectory | worktreePath | dispatchedAt |
|---|---|---|---|---|---|
| 1075abf0 | Code | server2-temp | `C:\src\Antiphon` | `C:\Antiphon\worktrees\card-task-1075abf0` | 2026-10-06T17:03:24Z |
| 5b61cc54 | Code | server2-temp | `C:\src\Antiphon` | `C:\Antiphon\worktrees\card-task-5b61cc54` | 2026-10-06T18:59:07Z |
| 35b2285b | Investigate | server2-temp | `C:\src\Antiphon` | `C:\Antiphon\worktrees\card-task-35b2285b` | 2026-10-06T19:20:59Z |
| 4bd8ed4c | Investigate | server2-temp | `C:\src\Antiphon` | `C:\Antiphon\worktrees\card-task-4bd8ed4c` | 2026-10-06T19:21:08Z |

`Delegation:PollIntervalSeconds` is not set in `server/appsettings.json`. The code default is 5 (`DelegationSettings.cs:18`). `StallDetection.Enabled` defaults true and `StallMinutes` defaults 30 (`DelegationSettings.cs:1105-1111`). `Git:MaxConcurrentProcesses` is 8.

## The spawn

`AgentTaskDispatcherHostedService` ticks `AgentTaskDispatcher.TickAsync` on that interval (`AgentTaskDispatcherHostedService.cs:35-44`). The tick always runs the progress-stall sweep (`AgentTaskDispatcher.cs:372`).

`DetectStalledProgressAsync` (`AgentTaskDispatcher.cs:3101-3115`) loads every Dispatched or Working task that has `DispatchedAt` and `AgentSessionId`. There is no `RunnerId` filter and no age filter.

`TryRaiseProgressStallAsync` (`AgentTaskDispatcher.cs:3138-3144`) calls `ProbeWorkspaceAsync` before `TaskProgressPolicy.EvaluateAsync`. The policy then returns null, without reading the probe, when elapsed time is under `StallMinutes` (`TaskProgressPolicy.cs:75-77`) or the session is not mid-turn (`TaskProgressPolicy.cs:79`). A task that does produce a verdict is probed a second time after the transcript catch-up (`AgentTaskDispatcher.cs:3147-3150`), and only then is the existing incident allowed to suppress a repeat (`AgentTaskDispatcher.cs:3168-3171`).

`ProbeWorkspaceAsync` (`AgentTaskDispatcher.cs:3208-3214`) passes `task.WorkingDirectory`, not `WorktreePath`. `AgentFilesService.ProbeProgressAsync` (`AgentFilesService.cs:53-113`) spawns, through `GitWorkspaceService` and `GitProcessGate`:

| call | argv | file:line |
|---|---|---|
| `IsRepositoryAsync` | `rev-parse --is-inside-work-tree` | `GitWorkspaceService.cs:43-46` |
| `TryGetChangesAsync` | `status --porcelain -z --untracked-files=all` | `GitWorkspaceService.cs:88-91` |
| same | `rev-parse --show-prefix` | `GitWorkspaceService.cs:102` |
| same | `rev-parse --show-toplevel` | `GitWorkspaceService.cs:104` |
| `TryGetRecentCommitsAsync` | `log -50 --format=...` | `GitWorkspaceService.cs:350-355` |

A missing directory spawns nothing (`AgentFilesService.cs:56-57`). A directory that exists and is not a repository spawns one `rev-parse` and stops. A git checkout spawns five. The gate (`GitProcessGate`, width 8, `Program.cs:776-777`) caps concurrency and counts starts. It does not reduce the start count. `status --porcelain -z --untracked-files=all` is the expensive one of the five; CARD-0216 measured about 98 ms for that command on `C:\src\Antiphon` when the machine was otherwise idle.

Rate while the tick stays at 5 seconds: `5 * N * 12` git starts per minute for N probed checkouts. Four tasks on one existing checkout is 20 starts per tick and 240 per minute. The 169-start sample is 14.1 starts per 5-second tick, which is about 2.8 probed checkouts, or one stretched tick of the current 20-start burst (the hosted service awaits the tick, so a slow status under the gate of 8 lengthens the period instead of stacking a second burst).

Two of the four live tasks were under six minutes old at the read, and a third was about 27 minutes old. All four were still inside the probe. The workspace arm can only withhold a stall (`TaskProgressPolicy.cs:57-59`). Probing `C:\src\Antiphon` for a server2 worktree also reads the wrong tree: dirt or build output in the main checkout can withhold a stall for a delegate that never touched that directory.

## Other server git sites, idle

These do not add up to 169 starts per minute on an idle fleet. Queued was empty at the read, so the sibling-base path did not run.

| site | cadence | git while idle | parent |
|---|---|---|---|
| Sibling base, `EvaluateCardSiblingBaseAsync` (`AgentTaskDispatcher.cs:776-782`, `4269-4366`) | every 5 s, only a Queued card Worktree task that has no worktree yet | `rev-parse` for the default branch, then per kept sibling `show-ref`, `rev-parse`, and up to three commands inside `ObserveContainmentAsync` (`DelegationWorktreeService.cs:121-205`) plus three more in `DescribeKeptBranchAsync` when the tip is not contained | server, but only with a queued backlog |
| Worktree health | 60 s (`GitSettings.WorktreeHealthIntervalSeconds`, `appsettings.json:17`) | 2 per existing repo: `worktree list --porcelain` and `branch --list feat/card-task-*` (`WorktreeManager.cs:222`, `258`) | server |
| Card-file sync | 60 s (`CardFileSync:IntervalSeconds`) | 1 `ls-files -s` per pinned repo for boards already marked clean (`CardTaskFileService.cs:257-260`, `GitWorkspaceService.cs:871`) | server |
| Change detection | 30 s (`Git:PollIntervalSeconds`) | at least `fetch`, `rev-parse`, `rev-parse` per Running or GateWaiting workflow (`ChangeDetectionService.cs:131-140`); zero when that set is empty | server |
| Home workspace identity | 30 s poll, 20 s cache (`WorkspaceInfoService.cs:16`, `client/src/api/filesystem.ts:75-76`) | 2 per distinct on-screen directory per poll (`rev-parse --show-toplevel --git-common-dir`, then `branch --show-current`, `GitWorkspaceService.cs:703-724`), plus 1 `worktree list` for the selected project. The poll is longer than the TTL, so every poll misses. Only while that page is open. CARD-0216 measured 32 paths, about 64 starts per cold call | server |
| Attention full list | 15 s while a panel is open (`client/src/api/attention.ts:307`) | the same 5-command probe, but only after `StallMinutes` and only for a task that reaches that row (`AttentionService.cs:1189-1204`). The summary poll passes `includeProgressProbe: false` (`AttentionService.cs:292`) | server |
| Land | on a queued land request, not a timer (`AgentTaskLandHostedService`) | hundreds per land; the sweep itself does not run git | server |
| Park sync recovery | every dispatcher tick, but only due `AgentTaskParks` rows, at most 32, with a minute-scale backoff (`BlockedTaskSyncRecoveryService.cs:20-27`) | git only inside `RemoteWorkspaceService.SyncParkedAsync` for a row that is actually due | server |

`LandingGit`, `WorktreeManager`, and `DelegationWorktreeService`'s fallback `Process.Start` do not all share `GitProcessGate`. The stall probe does, because it goes through `GitWorkspaceService`.

## Cards landed around this session

None of these are the 169-start source.

- CARD-1065 S9 reclaim is not a git poll. `BlockedTaskParkingOptions.ReclaimExisting` defaults false (`BlockedTaskParkingOptions.cs:7`). `BlockedTaskParkingService` performs no git. Park inspection runs in the session runner on a park, so those processes are children of the runner, not of Antiphon.Server. `BlockedTaskParkReclaimTests` is not in this tree.
- CARD-1079 samples occupancy every 60 seconds (`Attention:OccupancySampleIntervalSeconds`, `SeatOccupancyHostedService.cs:22-30`). `SeatOccupancySampler.SampleOnceAsync` issues SQL and one runner inventory HTTP per remote host. It does not start git. Per remote host the join is about twelve statements (`SeatDesktopJoin.LoadAsync`: two session/task peak queries run twice, plus sessions, blocked events, cards, pooled agents, and park receipts) plus the three counts beside it. One remote host is about fifteen statements per minute. That matches the figure in the brief and does not move the git count.
- CARD-1076 adds a budget and a journal tag on the remote-prep `git push` that already ran at prepare time (`DelegationSettings.RemotePrepPushBudgetMinutes`, `ILandingGit`). It is not a timer. No prep is in flight in the idle read above.
- CARD-1082 is a Dispatched Code task (`5b61cc54`), not a new server poll in this checkout. Settlement sync remains the existing recovery sweep.

## Fix options

Smallest safe change, and the one that should ship on: in `TryRaiseProgressStallAsync`, apply the gates `AttentionService` already uses, and only then probe. Skip the probe when elapsed time is under `StallMinutes` or the session is not mid-turn. Probe once per distinct directory per tick. For a runner-bound task, probe only a local `WorktreePath`; do not run `status --untracked-files=all` on the shared desktop checkout. Expected saving on today's fleet: all 240 predicted starts per minute from this site, because every open task is remote and three of the four are inside the stall window or not yet known to be mid-turn. A task that is past the window, mid-turn, and local keeps one five-command probe per stall check instead of one per task per 5 seconds from birth. Risk: a stall incident can wait until the first tick after both gates pass, which is the attention feed's rule already. Probing the worktree instead of the main checkout stops a false withhold and can allow a stall the dirty main checkout was hiding. That is the correction, not a behavior to flag off.

Do not cache `git status` across ticks for longer than one tick: the arm exists to notice a new file. Do not raise `PollIntervalSeconds` as the fix: the other dispatcher clocks share it. Do not change landing ref or lease checks. Leave `GitProcessGate` at 8. Leave `BlockedTaskParking:ReclaimExisting` false.

A second, smaller cut is independent and only matters while the home page is open: raise the workspace-info TTL to at least the 30-second poll, so a warm poll does not miss. Saving is about two starts per on-screen directory per 30 seconds. Risk is a branch switch showing up one poll later. Not required to explain the 169.

What is default-off: no new sweep. The stall-probe skip ships on. Reclaim stays off. A caller-tagged git counter is not required for the cut; argv grouping on the desktop is enough to prove it.

## Tests that cover this code today

A checkpoint row that names only a new test misses these:

- `AttentionServiceTests`: `a_task_dispatched_under_stall_minutes_does_not_run_the_workspace_probe`, `a_task_past_stall_minutes_runs_the_workspace_probe`, `the_summary_never_runs_the_workspace_probe`. This is the gate the dispatcher does not share.
- `TaskProgressPolicyTests` and `TaskProgressPolicyFileArmTests` (`ProbeProgressAsync_reads_a_real_git_worktree` and the status/log failure arms). They cover the five-command probe, not who calls it.
- `TaskProgressStallSweepTests` drives `DetectStalledProgressAsync` and does not inject `IWorkspaceProgressProbe`, so it cannot see the git calls.
- `WorkspaceInfoGitIntegrationTests` covers the home-page cache.
- `WorktreeHealthServiceTests` covers the 60-second scan.
- `SeatOccupancySamplerTests` covers CARD-1079 and should stay free of git.
- `LandingGitTests` and `RepositoryChildJournalInspectorTests` cover the CARD-1076 push tag, not this loop.
- `CardFileBoardLookupTests` covers the opted-out skip, not the per-minute `ls-files`.

A proof test records probe calls from `DetectStalledProgressAsync`: zero for a task younger than `StallMinutes`, zero for a task that is not mid-turn, zero for a runner-bound task whose only local path is the shared repo root, and one directory probe for two local tasks that share a checkout and are past the gate. Re-run the three `AttentionServiceTests` methods above so the feed gate does not drift. `TaskProgressPolicyFileArmTests` stays the proof that the probe itself is still the five-command read.

## What a desktop measurement should capture

Sixty seconds of process starts whose parent is Antiphon.Server, with argv and the child cwd. Group by argv. The stall site is a burst, once per tick, of this set per probed directory: `rev-parse --is-inside-work-tree`, `status --porcelain -z --untracked-files=all`, `rev-parse --show-prefix`, `rev-parse --show-toplevel`, `log -50`. On the current fleet every cwd in that burst should be `C:\src\Antiphon`, 20 starts per burst, period `max(5s, tick duration)`. Record whether the home page was open; if it was, a separate burst every 30 seconds is `rev-parse --path-format=absolute --show-toplevel --git-common-dir` plus `branch --show-current`, two per on-screen directory. `worktree list` and `ls-files -s` once a minute are the health and card-file sites. Anything else is a land, a sibling-base pass, or a due park sync, and should be absent while Queued is empty and no land is running.

## Remaining uncertainties

- No second process-start sample was taken. The 169-to-240 gap is either a smaller open set on 2026-10-05 or tick stretch under the gate. Argv grouping distinguishes those.
- A user-secret override of `Delegation:PollIntervalSeconds` is not visible in the committed settings. The burst period is the check.
- `IsWorking` for the four live sessions was not read, so the share that would survive a stall-minute gate is unknown. The two tasks under six minutes old cannot survive it.
- Whether `C:\src\Antiphon` exists on the desktop is inferred from it being the configured checkout those rows name. A missing directory would spawn zero git for that task.
