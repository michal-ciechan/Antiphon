# RC rollout finish, redeploy-old through retire-temp (task be6a673b)

Stopped at gate 6. `redeploy-old` refused `RecycleGitAuditUnknown` during deploy-parent preflight, before any container stop or removal. Smoke, `drain-temp`, and `retire-temp` were not run. No retry: the token is not a settled-task `RecycleLandUnknown`.

Prior attempts, including the stale worktree-admin cleanup, are on `feat/card-task-c0d49725` at `d79a9887d225318f0f70d764272dfcd446d2ce46`. This file was missing on `feat/card-task-be6a673b`.

Requested image SHA for every phase: `09e48c5d6f1d404cfea8ec0d992270424fd2a184`.
Canonical checkout and `GET /api/version`: `7b3b9adbe98b18ae306b73cfb220331dc433f801`. The checkout was not switched or pulled. AppHost and the desktop runner were not restarted.

The doc names gate 6 `redeploy-old`. There is no separate `upgrade-old` phase. That is the command that ran.

## Preflight (2026-10-17, before the phase)

| Check | Result |
|---|---|
| `GET /api/agent-tasks?landPending=true&projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f` | `items` empty, `excluded.total=0` |
| `server2` | `buildVersion=7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`, `draining=true`, `redirectTo=server2-temp`, `retireWhenIdle=false`, `acceptingNewWork=false`, `sessions=0`, `queuedTasks=0`, `runnerSessions=0` |
| `server2-temp` | `buildVersion=09e48c5d6f1d404cfea8ec0d992270424fd2a184`, `acceptingNewWork=true`, `dispatchEligible=true`, `draining=false`, counters 0 |

## Phases

| Phase | UTC | Exit | Result |
|---|---|---|---|
| `redeploy-old` | start `2026-10-17T08:32:39.4552695Z`, end `2026-10-17T10:04:35.3852817Z` | 2 | `Rolling deploy stopped: HostCaseFailed deploy-parent exit=2`, `DIAGNOSIS=RecycleGitAuditUnknown audit check=for-each-ref status=128 repo=repos/antiphon/.git/worktrees/master` |
| smoke `server2` and `verify-card0849-caches.ps1 -Case Both` | not started | | |
| `drain-temp` | not started | | |
| `retire-temp` | not started | | |

Command, from `C:\src\Antiphon`:

```
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 09e48c5d6f1d404cfea8ec0d992270424fd2a184 -Phase redeploy-old
```

## Receipts

Evidence root: `C:\src\Antiphon\.antiphon\rolling-server2\c727a1f82f2ababd`

- `c590-result.json` and `deploy-parent\c590-result.json`: `accepted=false`, `diagnosis=RecycleGitAuditUnknown audit check=for-each-ref status=128 repo=repos/antiphon/.git/worktrees/master`, `exit=2`
- `deploy-parent\census-deploy-parent-server2.json`: `refusal` empty, so the land race did not fire
- `deploy-parent\recycle.json`: `phase=preflight`, `outcome=refused`, operation `c10084fa7d62ec9ba4845bf071445f0d516b1`, `composeDigest` `0c0d1515ccc640731beca6942613a1d489c3c364898eb35e0bd02dcc441e1147`, image `sha256:ea8bafe24373694b3421d63afdffb4533faf0710f709d96dea661123b55e6835` (the previous image, tag `7507b3cfe0e2`), `stopReceipts` and `removeReceipts` empty, volume outcomes `pending`, `ownedRemoved=false`
- `deploy-parent\generation.json`: `statusBuildVersion=7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`

The land closure in that receipt has no pending ids for project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`. Open rows are desktop tasks. This Deploy task `be6a673b-ae1e-4f35-83a3-feabfde8c561` is one of them (`status=Dispatched`, `runnerId=desktop`). None are bound to `server2`.

## After the refusal

Observed at `2026-10-17T10:05:30Z`:

| Runner | State |
|---|---|
| `server2` | still `7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`, `draining=true`, `redirectTo=server2-temp`, `retireWhenIdle=false`, `acceptingNewWork=false`, `sessions=0`, `queuedTasks=0`, `runnerSessions=0`, same `runnerStoreId` `f519bd08-e53a-47d1-adb1-2ab33475446f` and `processBootId` `9a6d75cc-09cb-4034-818e-440ceb9e2d2f` |
| `server2-temp` | still accepting `09e48c5d6f1d404cfea8ec0d992270424fd2a184`, `draining=false`, `dispatchEligible=true`, `sessions=5`, `queuedTasks=0`, `runnerSessions=5` |

`server2-temp` had zero counters at preflight. Five sessions were on temp when the phase returned. Temp was not drained. Standing `server2` `buildVersion` is not the requested SHA. `server2-temp` is not retired.

A later `redeploy-old` needs an explicit decision. This pass did not start one, and it did not inspect or change `/work/repos/antiphon/.git/worktrees/master`.

## Decisions

The git-audit refusal on `repos/antiphon/.git/worktrees/master` (`for-each-ref` status 128) is a different token from the settled-task census class. Another `redeploy-old` is not authorized by this pass. Temp remains the accepting runner and is no longer idle.
