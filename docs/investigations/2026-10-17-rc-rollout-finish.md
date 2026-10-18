# 2026-10-18 server2 rollout finish - stopped at census

Deploy task `26205ec4` on `feat/card-task-26205ec4`. Target `-Sha` `09e48c5d6f1d404cfea8ec0d992270424fd2a184`. No `-ResumeRecycle`. No volume was deleted. No journal was edited or moved by this task.

Prior records: `fcc7fd7caf940cffb5ad7845992fa0c60e23d50b` and tip `ce4b503c3034bbbe36e342c7b2fa522b91075cfd` on `feat/card-task-41d7a523`; diagnosis `e7a6d63987bc72ddfc775fefed157b32e5525040` on `feat/card-task-69c07e08`.

Canonical checkout stayed detached at `69978ec1a246cd7a860f88a364eafc53a416aff7`. `GET /api/version` was that same SHA with `land-v2`. `/health` was Healthy. AppHost and the desktop runner were not restarted.

## Journals

Re-read at 2026-10-18T10:37Z. The five refused-preflight journals were already in `/home/mc/antiphon-server2/recycle/archived-refused/` (moved by task `41d7a523` at 2026-10-18T10:28:08Z). Each was still the only copy, `phase=preflight`, `outcome=refused`, `ownedRemoved=false`, `stopReceipts=[]`, `removeReceipts=[]`, `sourceSha=09e48c5d6f1d404cfea8ec0d992270424fd2a184`, project `antiphon-runner`. They were not moved again.

| operationId | diagnosis |
|---|---|
| `c1008001b165462094cb4a27f484ffe6fc850` | `RecycleGitAuditUnknown` for-each-ref status 128 `repos/antiphon/.git/worktrees/card1259-base-d636249e` |
| `c10084fa7d62ec9ba4845bf071445f0d516b1` | `RecycleGitAuditUnknown` for-each-ref status 128 `repos/antiphon/.git/worktrees/master` |
| `c1008bd48f43e1fa54a19ba070d3ea30986b9` | `RecycleWorktreeDirty` status `worktrees/task-2d835afa` |
| `c1008ce5bf1ed6d4b4ed7918d807e8489c390` | `RecycleWorktreeDirty` index-content `worktrees/task-71fd0d46` |
| `c1008ffe0deb1d99946b6940cf84f675909ad` | `RecycleGitAuditUnknown` tmp-git status 1 `/tmp/card1259-base-d636249e` |

Sealed journal `c100808382374392e4ce1ac7dde48ca3b195f` was read only: `phase=completed`, `outcome=completed`, `diagnosis=preserve-seal volumes kept`, outcomes `kept` for `antiphon-runner_work`, `antiphon-runner_runner-tmp`, and `antiphon-runner_dind-data`. Other top-level journals were not opened for edit. None of them is an unfinished `antiphon-runner` journal for this SHA. The only same-SHA `antiphon-runner` journal left in `/home/mc/antiphon-server2/recycle/` is the sealed one.

Copies of the five archived files: `C:\src\Antiphon\.antiphon\rolling-server2\c7275c8685ffd5da\archived-refused\`.

## Runners

`GET /api/session-runners/server2/status` at 2026-10-18T10:37:14Z:

- `buildVersion=09e48c5d6f1d404cfea8ec0d992270424fd2a184`
- `runnerStoreId=f519bd08-e53a-47d1-adb1-2ab33475446f`
- `processBootId=0e14a3d4-ba81-4ae9-a956-515188b82233`
- `available=true` `dispatchEligible=true` `draining=true` `redirectTo=server2-temp` `acceptingNewWork=false`
- `sessions=0` `runnerSessions=0` `queuedTasks=0`

server2-temp was accepting the same SHA, store `1559ba5a-92e3-42a4-a891-74bce5267a2e`, `sessions=4` `runnerSessions=4` `queuedTasks=0`.

`antiphon-runner-session-runner-1` (`a6457bc89508`) was Up (healthy) on `antiphon-server2/session-testing:09e48c5d6f1d`. The three kept volumes and `antiphon-runner_runner-state` were present. Temp containers were still up.

## Census stop

`check-census` from `C:\src\Antiphon` at 2026-10-18T10:38:38Z-10:38:41Z. Exit 0. Receipt `C:\src\Antiphon\.antiphon\rolling-server2\c7275c8685ffd5da`. Project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`.

| Runner | open | boundOpen | landPending |
|---|---|---|---|
| server2 | 12 | 0 | 0 |
| server2-temp | 12 | 4 | 0 |

landPending was 0. server2 had no Queued or Dispatched task. server2-temp still had two Dispatched tasks, re-read after the census:

| id | status | role | title |
|---|---|---|---|
| `6b367dd6-e20f-4e3c-8a3b-e1070cd8620c` | Dispatched | Review | Review npm fixture network id |
| `b2d71f07-1259-4d73-9b2d-240e54f16068` | Dispatched | Review | Review orchestrator bundle trim |

Both had null land timestamps. Also bound to server2-temp, not Dispatched: Blocked Code `89bf7b58-b629-46ca-9b4e-500fd29014c0` (CARD-1320 S3 rebase onto master) and Blocked Code `bd6bce3d-816e-469f-a30d-0621ab09b644` (CARD-1320 S4 rebase onto master, land Completed).

This is not the settled-task census race. `redeploy-old` was not run. Smoke, the Plan canary, `verify-card0849-caches.ps1 -Case Both`, `drain-temp`, and `retire-temp` were not run. Standing server2 remains drained onto server2-temp with `acceptingNewWork=false`.
