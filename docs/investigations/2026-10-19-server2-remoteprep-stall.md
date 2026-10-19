# Server2 remote-prep stall, 2026-10-19

Confirmed. Remote workspace preparation is not stuck on server2, and it is not a Hangfire job lost at the 10:03Z restart. The desktop dispatch tick is one serial loop. Before it cuts the next worktree it walks every kept sibling branch of that card with `git merge-base`, `git rev-list`, and `git cherry`. CARD-1311 has 64 such siblings, and that walk took 13–18 minutes per task. A task whose mirror is already recorded stays Queued on the old "preparation is in flight" hold until that tick ends.

Observed from the desktop at 2026-10-19T11:16:39Z. Nothing was restarted or killed.

## What preparation does

A runner-bound Worktree task stays Queued until the desktop branch is on origin and the runner has recorded a mirror. The dispatcher commits the claim, calls `RemoteWorkspacePreparer.TryBegin`, and returns `HeldForRemotePrep` (`server/Application/Services/AgentTaskDispatcher.cs` around the `TryBegin` call). The hold text is `DispatchHoldDetails.RemoteMirrorRequested`: "remote workspace preparation is in flight for runner '…' (branch push and mirror requested …)". It is written once per stint.

The preparer runs off the tick (`RemoteWorkspacePreparer.RunAsync`). It pushes `git push origin <branch>` with budget `Delegation:RemotePrepPushBudgetMinutes` (default 20) and purpose `remote-prep-push`, then asks the runner to mirror (`RemoteWorkspaceService.PushBranchAsync`, `MirrorAsync`). One push at a time per repository. The in-flight set is memory-only: a restart drops it and the next tick re-arms the same preparation id. Success logs "Remote workspace binding for task {id} was saved". The launch is a later tick, after `RemoteWorktreePath` is set. This tick does not come back to a row it already held.

`workspaceRepositoryV1` is the runner feature for a named repository mirror. `workspacePublishV1` is the later settlement push. Today's mirrors were accepted, so this stall is not a missing feature or a runner refusal.

Hangfire is `Hangfire.InMemory` (`docs/logs.md`). A restart drops its dashboard history. Preparation is not a Hangfire job, so there is no job id to retry.

## What was stuck, and since when

Server process start in `C:\src\Antiphon\server\logs\antiphon-20261019.log`: `2026-10-19 11:03:24.634 +01:00` (`10:03:24Z`) "Antiphon server starting". The earlier start the same file is `02:03:22 +01:00` (`01:03:22Z`). The first prep after the 10:03Z start finished. It was not a lost in-memory queue.

| Task | Card | Created (Z) | Worktree event (Z) | Binding saved (Z) | Left Queued (Z) |
|---|---|---|---|---|---|
| 90dcf4ee Debug, desktop | CARD-1311 | 10:01:40 | 10:16:41 (log) | n/a | dispatched 10:16:41 |
| 046b1e67 Plan, server2 | CARD-0907 | 10:03:57 | 10:18:00 | 10:18:37 | dispatched 10:52:11 |
| adf29d7d Review, server2 | CARD-1340 | 10:04:31 | none | none | Blocked 10:18:31, Canceled 11:03:52 |
| 2551b9d9 Review, server2 | CARD-1311 | 10:04:49 | 10:36:40 | 10:37:20 | dispatched 10:52:11 |
| 3a731d3a Code, server2 | CARD-1311 | 10:05:07 | 10:51:36 | 10:52:03 | dispatched 10:52:11 |

Prep itself was short: 17s, 26s, and 14s from the hold line to "binding was saved" (`antiphon-20261019.log` 11:18:20 / 11:18:37, 11:36:54 / 11:37:20, 11:51:49 / 11:52:03 +01:00). 046b1e67's mirror was recorded at 10:18:37Z and the task still showed the 10:18:20Z in-flight hold until dispatch at 10:52:11Z (34 minutes). The tick was inside later tasks the whole time. There is no second "held:" line because the hold is deduped.

The gaps before each worktree match the sibling walk, not the push:

- CARD-1311, 64 Succeeded or Blocked Worktree rows with a branch: 90dcf4ee took about 13 minutes after the restart; 2551b9d9 took 18m09s after adf29d7d's block; 3a731d3a took 14m42s after 2551b9d9's hold.
- CARD-0907, 14 siblings: 046b1e67's worktree was 1.5 minutes after 90dcf4ee.
- CARD-1340, 2 siblings: adf29d7d was Blocked 11 seconds after the previous hold.

Count query (Workspace = Worktree, status Blocked or Succeeded, branch set), run against desktop Postgres at 11:14Z:

```sql
SELECT c."Identifier", count(*)
FROM "AgentTasks" t
JOIN "Cards" c ON c."Id" = t."CardId"
WHERE c."Identifier" IN ('CARD-1311', 'CARD-0907', 'CARD-1340')
  AND t."Workspace" = 1 AND t."Status" IN (3, 4)
  AND t."WorktreeBranch" IS NOT NULL
GROUP BY 1;
```

The walk is `EvaluateCardSiblingBaseAsync` (`AgentTaskDispatcher.cs`), called only when the worktree path is still null, before `DispatchOneAsync`. For each sibling it can run `merge-base --is-ancestor`, `show-ref`, `rev-parse`, `rev-list`, `cherry`, and `log` (`DelegationWorktreeService.ObserveContainmentAsync`, `DescribeKeptBranchAsync`). Those go through `LandingGit.RunAsync`. `LandingGit.DefaultBudget` is 5 minutes, then the child is killed. Sixty-four siblings times a few of those commands is the 15 minute class. The 5 second `Git:WorktreeBaseInspectionTimeoutSeconds` does not cover this walk.

At 11:11Z through 11:16:39Z, `Antiphon.Server.exe` pid 334724 (`C:\src\Antiphon\server\bin\Debug\net9.0\Antiphon.Server.exe`) kept spawning fresh children, for example:

```text
git merge-base --is-ancestor e7a6d63987bc72ddfc775fefed157b32e5525040 8a859e3bdac4c3cc843811947abd7b191db18735
```

Each child was about a second old when sampled, then replaced. That is the walk, not one wedged process. `C:\src\Antiphon\.git\index.lock` and `packed-refs.lock` were absent. `C:\src\Antiphon\.git\antiphon\children` was empty (no live `remote-prep-push` journal). `gc.pid` there is dated 2026-09-25 and `antiphon\landing.lock` is the lease file; its existence is not the holder.

### adf29d7d is a different failure

Blocked at 10:18:31Z, failure text:

```text
inspection_timeout: Same-card branch inspection incomplete: inspection_timeout (2 total, 2 inspected, 0 omitted). Retry or select a base explicitly.
```

That is `AgentTaskWorktreeBaseResolver` hitting the 5 second inspection budget (`GitSettings.WorktreeBaseInspectionTimeoutSeconds`, two CARD-1340 candidates). `BlockAsync` writes the row and no Information log, which is why `antiphon-20261019.log` has no "Task adf29d7d blocked" line. Canceled at 11:03:52Z. It did not hold the tick for 18 minutes.

### Still queued at 11:16:39Z

No worktree, no remote path, no events after Created:

| Task | Card | Runner | Created (Z) |
|---|---|---|---|
| 416937f3 Code | CARD-1311 | desktop | 11:03:00 |
| 0e94c39e Review | CARD-1340 | server2 | 11:03:52 |
| 32b5f51b Review | CARD-1311 | server2 | 11:05:17 |

416937f3 is first. The live `merge-base` children are its sibling walk. 0e94c39e is two siblings and should be quick once the tick reaches it. 32b5f51b is another CARD-1311 walk of the same shape, so it will sit for another ~15 minutes before its push is even requested. None of these three is in remote prep yet. The in-flight hold appears only after the walk finishes and `TryBegin` runs.

## Server2 mirror is idle

Host path `/work/repos/antiphon` is not the runner mirror (`docs/docker-stack.md`: the runner clone is the `antiphon-runner_work` volume, same path inside `antiphon-runner-session-runner-1`). Read-only `docker exec` at about 11:15Z:

- No `index.lock`, `packed-refs.lock`, or `gc.pid` under `/work/repos/antiphon/.git`.
- No `git` process in the container.
- `for-each-ref refs/heads` count 836. `rev-list --count --all` 13651.
- `/work` is the host volume, 197G free, 71% used. Inodes 28% used.
- `find` hits named `.cleanup.lock` are checkpoint and review evidence markers under `.git/antiphon/…`, not git index locks. They were not removed.

The three server2 preps that did run completed the phone-home mirror in under half a minute. This is not a slow push, a half-finished fetch, or a runner refusal.

## Smallest safe unstick

Ranked. Do not restart AppHost or the runner, and do not kill sessions. A restart would drop the in-memory prep set and the next tick would start the same 64-sibling walk again.

1. Wait. The current walk is 416937f3. Past CARD-1311 walks on this repo took 13–18 minutes. After it, 0e94c39e (CARD-1340) should reach prep within about a minute, and that prep has been 14–26 seconds. 32b5f51b then pays another CARD-1311 walk before its mirror is requested. Check, without starting git yourself:

```powershell
docker exec antiphon-postgres psql -U antiphon -d antiphon -c "SELECT left(\"Id\"::text,8), \"Status\", (\"WorktreePath\" IS NOT NULL) AS wt, (\"RemoteWorktreePath\" IS NOT NULL) AS remote FROM \"AgentTasks\" WHERE \"Id\"::text LIKE '416937f3%' OR \"Id\"::text LIKE '0e94c39e%' OR \"Id\"::text LIKE '32b5f51b%';"
Get-CimInstance Win32_Process -Filter "Name = 'git.exe'" | Select-Object ProcessId, ParentProcessId, CreationDate, CommandLine
```

Status 0 is Queued. A worktree path means the walk for that row finished.

2. Only if one `git.exe` child of `Antiphon.Server.exe` is still the same PID after more than 5 minutes (`LandingGit.DefaultBudget`): the kill-on-cancel path did not run. Stop that git PID, not the server. `IsCommitAncestorAsync` treats a failed check as "not an ancestor" and the walk continues at the next sibling. This does not skip the rest of the 64.

```powershell
Stop-Process -Id <git-pid>
```

3. Do not run `scripts/recover-repository-children.ps1`. The children journal was empty. A live `remote-prep-push` must not be recovered (`docs/orchestration-loop.md`).

4. Do not delete runner `.cleanup.lock` files, and do not `git gc` or prune either repo. They are not what the tick is waiting on.

## Product fix

Not done, noted.

Give `EvaluateCardSiblingBaseAsync` a short budget of the same kind as `WorktreeBaseInspectionTimeoutSeconds`, or one batched ancestry query, and do not run per-sibling `merge-base` / `cherry` / `rev-list` on the serial dispatch tick. A 5 minute per-command ceiling times 64 siblings can hold every later task, including rows whose mirror is already saved. When the preparer saves the binding, launch that task or replace the hold text; the deduped "in flight" line stays up for the rest of the tick even though the preparer has returned. Log the walk's card, sibling count, and current git command. `BlockAsync` for `inspection_timeout` should log the same reason it stores, so the server log is not silent.

## Not done, noted

No code change. The fix is the product-fix section above.
