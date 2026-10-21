# Server2 "remotePrep" dispatch latency, 2026-10-21

Diagnosis only. Nothing was restarted, killed, deleted or reconfigured. The server under test is
`dc47eb4833519dd734ea44c67e3d6b8faa10e14c` (rc/2026-10-20; it contains CARD-1349 off-tick sibling inspection).

## Verdict

The 10-15 minute wait is not remote preparation. When nothing else is using the desktop repository, the push and
mirror (from "preparation is in flight" to "binding was saved") take **15-17 s**. Most of the wait comes
earlier, before the desktop worktree is cut, while the task waits for the **desktop repository mutation lease**:

1. **Lands hold the lease back to back.** Seven lands ran from 21:04:29Z to 21:33:08Z. Each one held the lease for
   165-543 s, and the next land took it about 1 s after the previous one let go. A queued worktree cut never got
   a turn while lands were queued.
2. **The sibling-inspection result is used once and then discarded.** The dispatcher alternates between
   "same-card base inspection is pending" and "lease occupied". Each lease refusal throws away the inspection
   result, and the next tick has to start the inspection again. Only two workers run inspections, and a
   result expires after 30 s. When the lease was free (62 s at 21:29:23-21:30:25Z), no task cut a worktree,
   because every queued task was waiting for a fresh inspection.
3. **Each cut also runs under the lease and the cuts are serial.** Progress-baseline capture runs after
   `git worktree add` and before the lease is released. It takes about 13 s with no contention and up to
   2m13s while a land runs. After the lease was freed at 21:33:08Z, the six queued tasks cut one at a time,
   and the last one cut at 21:35:51Z.
4. **The desktop ref store is bloated, and every git step pays for it.** `C:\src\Antiphon\.git` holds
   **23,162 loose ref files**: 9,586 `refs/antiphon/land`, 7,701 `refs/antiphon/progress`, 3,142
   `refs/remotes/origin` and 2,678 `refs/heads/feat`. `packed-refs` was last written on 2026-09-28.
   `git for-each-ref` takes **10.4 s** there. The server2 mirror takes **29 ms** for the same command.

The `remotePrep` label is correct only for the last 15-17 s, which is the moment an operator usually reads the
pipeline after a burst of cuts. The dispatcher's `held aged` warning makes this worse. It prints the latest
hold sentence for the whole stint, for example `reason=Held: remote workspace preparation is in flight` on a
325 s wait. The ledger on the same line reads `prepWait=0s`.

## (1) What a server2 -Worktree task does before dispatch

All of this runs on the desktop server's serial dispatch tick (`server/Application/Services/AgentTaskDispatcher.cs`):

| Step | Code | Lease? |
|---|---|---|
| Sibling inspection: no git runs on the tick. `Take` returns a finished result once, or the tick enqueues a new inspection and holds with `SiblingInspectionPending` | `AgentTaskDispatcher.cs:838-879`; queue `CardSiblingInspectionQueue.cs` (2 workers, `Start`; result single-use in `Take`; 30 s freshness in `Take` and `Reconcile`) | no |
| Claim plus `git worktree add` from the base | `DispatchOneAsync` → `HeldOnLease` if busy (`AgentTaskDispatcher.cs:925-939`, `DescribeLeaseHoldAsync`, `RegisterLeaseWaiterAsync`) | **yes** |
| Progress baseline capture (`CaptureProgressBaselineAsync`) | `AgentTaskDispatcher.cs:~5780-5815` | **yes** (released at 5891) |
| Commit the claim, dispose the lease, `_remotePrep.TryBegin`, return `HeldForRemotePrep` | `AgentTaskDispatcher.cs:5884-5894` | released |
| Off the tick: `git push origin <branch>` (one per repository, budget `RemotePrepPushBudgetMinutes`), then the runner mirror (`workspaceRepositoryV1`) | `RemoteWorkspacePreparer.RunAsync`, `RemoteWorkspaceService.PushBranchAsync` / `MirrorAsync` | no |
| Binding saved → `ReadyHold` → launched on a later tick | `RemoteWorkspaceReadiness.RecordPathAsync` | no |

Queue reasons map from the latest current-stint Held row (`AgentTaskPipelineStatusService.cs:768-798`, `:623-641`).
`DispatchHoldDetails.ClassOf` (`DispatchHoldDetails.cs:171-205`) classes "same-card base inspection is pending" as
`Other` (queue reason `awaitingDispatch`), "repository mutation lease…" as `Lease` (`repositoryLease`), and only
"remote workspace preparation…" as `RemotePrep`.

## (2) Where the time went, per task

From `AgentTaskEvents` (desktop Postgres) and `server/logs/antiphon-20261021*.log`. Times are Z.

| Task | Created | Worktree cut | Prep requested | Binding saved | Dispatched | Total | Prep (push+mirror) |
|---|---|---|---|---|---|---|---|
| 5148a35a Code | 20:55:01 | 20:57:52 | 20:58:06 | 20:58:21 | 20:58:33 | 3m32s | 15 s |
| 1a1b5d09 Review | 21:04:17 | 21:05:13 | 21:06:46 | 21:09:13 | 21:10:14 | 6m | 2m27s (during 543 s land) |
| e3c54ec5 Plan | 21:04:29 | 21:06:48 | 21:09:01 | 21:10:57 | 21:11:02 | 6m33s | 1m56s (during land) |
| 35f7e693 Plan | 21:14:30 | 21:14:51 | 21:15:58 | 21:17:45 | 21:18:17 | 3m47s | 1m47s (during 398 s land) |
| c31e4000 Code | 21:18:59 | **21:33:10** | 21:33:25 | 21:33:41 | 21:34:34 | 15m35s | 16 s |
| b7bea094 Code | 21:19:45 | **21:33:34** | 21:33:49 | 21:34:06 | 21:34:36 | 14m51s | 17 s |
| 039de4fc Plan | 21:20:34 | **21:33:50** | 21:34:06 | 21:34:22 | 21:34:38 | 14m04s | 16 s |
| 5f0d41b3 Plan | 21:27:42 | 21:34:49 | 21:35:03 | 21:35:19 | 21:35:47 | 8m05s | 16 s |
| 6e5b8a50 Review | 21:30:00 | 21:35:04 | 21:35:19 | 21:35:36 | 21:35:49 | 5m49s | 17 s |
| 40c93127 Code | 21:30:25 | 21:35:51 | 21:36:08 | 21:36:25 | 21:36:27 | 6m02s | 17 s |

The dispatcher's own wait ledger (`held aged` warnings, server log) agrees:

```text
22:25:03 c31e4000 held 328s  leaseWait=91s prepWait=0s otherWait=237s class=other
22:25:31 b7bea094 held 308s  leaseWait=63s prepWait=0s otherWait=244s class=other
22:26:03 039de4fc held 321s  leaseWait=66s prepWait=0s otherWait=254s class=other
22:33:25 5f0d41b3 held 314s  leaseWait=62s prepWait=0s otherWait=251s class=other
22:35:19 6e5b8a50 held 305s  reason=Held: remote workspace preparation is in flight ... leaseWait=67s prepWait=0s otherWait=237s
22:36:08 40c93127 held 325s  reason=Held: remote workspace preparation is in flight ... leaseWait=87s prepWait=0s otherWait=238s
```

(`+01:00` local times.) `otherWait` is the "same-card base inspection is pending" spans, which alternate with the
lease spans roughly every 10-30 s for every queued task. See the c31e4000 rows from 21:19:34Z to 21:32:24Z: 11
"lease occupied" rows naming four different land owners, interleaved with 13 "inspection pending" rows.

Lease holders (`AgentTaskLandRequests`, start → terminal event) and the `Land git profile` lines:

| Land task | Lease from | Lease to | wall s | push s | cleanup s | git processes |
|---|---|---|---|---|---|---|
| 7aab6803 | 20:54:52 | 20:57:46 | — | — | — | — |
| 1f58d427 | 21:04:29 | 21:13:31 | 543 | — | — | 270 |
| cf240f1e | 21:13:51 | 21:20:29 | 398 | — | — | 272 |
| 45340222 | 21:20:30 | 21:23:23 | 174 | — | — | 237 |
| e3c54ec5 | 21:23:24 | 21:26:17 | 174 | 40.4 | 73.9 | 233 |
| 07273b7d | 21:26:18 | 21:29:23 | 186 | 44.2 | 73.6 | 233 |
| 35f7e693 | 21:30:25 | 21:33:08 | 165 | 37.8 | 67.3 | 231 |

Every land profile shows `canonicalHits≈3460` and `remote=19`. Between 21:20:30Z and 21:29:23Z the lease was never
free for more than about 1 s. The cuts at 21:05-21:15Z went in between land phases, and their baseline capture
and push slowed to 1-2.5 minutes while the land competed for the same `.git`.

Cut duration under the lease, from the `Worktree created` event to the `worktree at` log line that follows baseline capture:
13 s idle (c31e4000 21:33:10 → 21:33:23), 34 s and 2m09s during the 1f58d427 land (1a1b5d09, e3c54ec5).

## (3) Serialisation

- **Repository mutation lease (desktop `C:\src\Antiphon`).** This is the main serialiser. Lands, worktree cuts
  (including baseline capture), and the settlement sync sweep all take it. `AgentTaskSyncDebts` has
  `runner_sync_lease_busy` rows as late as 21:43:03Z: a server2 settlement could not fast-forward the desktop
  worktree because a land held the lease. That is the "desktop-sync pending" note from earlier today. It
  comes from the same lease, held by lands.
- **The land queue hands the lease straight to the next land** (about 1 s gap, three times in a row above).
  Lease waiters (`RegisterLeaseWaiterAsync`) do not get a turn in between.
- **Sibling inspection queue.** It has two workers, a single-use result and 30 s freshness, so it adds a full
  inspection round after every lease refusal. While the lease is busy the cost is mostly hidden, but the
  queue missed the one 62 s free window.
- **The preparer's one-push-per-repository gate was not a factor.** The prep windows on the 21:33-21:36 burst did
  not overlap: b7bea094 saved at 21:34:06.125 and 039de4fc was requested at 21:34:06.323. `prepWait=0s` in every ledger.
- **Serial tick.** Cuts happen one per tick-pass, each under the lease. Six tasks needed 2m43s to cut after the lease freed.

## (4) Git costs

Desktop canonical `C:\src\Antiphon\.git` (measured 21:4xZ):

- `count-objects -vH`: 72,647 loose objects (443.7 MiB), 118 packs (151 MiB), prune-packable 4,768. The command took 10.0 s.
- 23,162 loose ref files. `packed-refs` is 854 KB and dated 2026-09-28 (no ref packing since). There are 3,616 heads and 3,299 remote refs.
- `git for-each-ref`: 10.4 s (two runs). Heads only: 1.6 s. `rev-parse HEAD`: 0.35 s. `status`: 0.6 s.
- 1,156 registered worktrees (`.git/worktrees` 1.2 GB). `git worktree list --porcelain`: 2.3 s. There are 1,161 dirs under `C:\Antiphon\worktrees`.
- `gc.pid` is dated 2026-09-25 and there is no `gc.log`. `gc.autodetach=false` (user gitconfig), so an auto gc would run in the foreground of whatever git call
  triggered it. The packed-refs date and the loose counts say no effective gc or pack-refs has run since late September. Why auto gc
  is not running was **not determined**.

Server2 runner mirror `/work/repos/antiphon` in `antiphon-runner-session-runner-1` (read-only, `docker exec -u 1654`):

- 283 loose objects, 13 packs (106 MiB), 32 loose refs, `packed-refs` written 20:54Z today.
- `for-each-ref` 29 ms. `fetch --dry-run origin master` 680 ms. 248 worktrees, 246 dirs under `/work/worktrees`.
- `/work` 424 GB free (38 %). Host load average was about 30, but the 16 s prep times show it is not on this path.

The server2 side is healthy. The git cost is on the desktop, and the lease takes on all of it, because lands run about 230 git processes against the
bloated ref store while holding the lease.

## Ranked fixes

All of these are proposals. Each operational item mutates the shared repository, so it is the operator's call.

Quick and operational:

1. **Don't land a burst while server2 work is queued, or space the lands out.** Each land blocks every new cut for about 3 minutes, and a queue
   of four lands blocks them for 12 minutes or more. This has the biggest effect today and needs no code change.
2. **Pack refs on the desktop canonical repository** (`git pack-refs --all`, run while no land or cut is in flight, for example under a
   drained dispatcher). It turns 23k loose ref files into one file, and every land, cut, baseline and sibling inspection gets faster. Expected
   effect: `for-each-ref` drops from about 10 s to sub-second. That was not measured, because it would change the repository.
3. **Find out why auto gc and pack-refs stopped after 2026-09-25/28** (stale `gc.pid`, an Antiphon `-c gc.auto=0`, or gc failing silently),
   then restore scheduled maintenance (Windmill or Hangfire, per the scheduling memory). Pruning `refs/antiphon/land` and `refs/antiphon/progress` for
   retired tasks is a deletion, so it is human-gated and needs a design for which refs are safe to drop.

Code changes:

4. **Give queued cuts a turn on the lease between lands.** For example, the land queue waits one dispatch tick, or lease waiters
   (`RegisterLeaseWaiterAsync`) get priority, before handing the lease to the next land. A cut needs about 15 s and a land about 3 minutes.
5. **Keep the sibling-inspection result across a `HeldOnLease` refusal.** Don't consume it in `Take` when `DispatchOneAsync` returns
   `HeldOnLease`, or key freshness on the observed refs instead of 30 s. Then a free lease window is used straight away, instead of
   waiting for another inspection round on two workers.
6. **Shorten the land's lease hold.** `cleanup` takes about 70 s and `push` about 40 s per land, both under the lease. A desktop land push takes 40 s,
   while the prep push takes 16 s. Moving cleanup out of the lease, or making it cheaper, cuts lease time by about 40 %.
7. **Release the lease before progress-baseline capture** for runner-bound tasks, or make the capture cheaper. It takes 13 s idle and up to 2m13s under contention.
8. **Attribution fixes.** `held aged` should name the ledger's dominant class, not the latest sentence (it printed remotePrep with
   `prepWait=0s`). Give `SiblingInspectionPending` its own pipeline queue reason (for example `siblingInspection`) instead of `awaitingDispatch`.
   Log sibling inspection timing at Information, not Debug (`CardSiblingInspectionService.cs:105,124`), so the next investigation can time it directly.

## Not measured, noted

- How long an individual sibling inspection takes. It logs at Debug only, and no task was queued while I was reading, so I could not sample its git children.
- Why auto gc is not running on the desktop repository.
- The effect of item 2 on timings (it would mutate the repository).
