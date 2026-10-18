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

This is not the settled-task census race. `redeploy-old` was not run in that pass. The continuation below is the later approval.

## Continuation: redeploy-old and gate 7 smoke

Operator reply on this task: the two Dispatched Reviews on server2-temp are expected, and the Blocked Code tasks are not a gate. The hard conditions are `landPending=0` and server2 `boundOpen=0`.

`check-census` from `C:\src\Antiphon` at 2026-10-18T10:43:34Z. Exit 0. Receipt `C:\src\Antiphon\.antiphon\rolling-server2\c727a047200160e8`. server2 `boundOpen=0` `landPending=0`. server2-temp `boundOpen=4` `landPending=0`.

One `redeploy-old` at 2026-10-18T10:43:50Z-10:44:00Z, no `-ResumeRecycle`. Exit 0. Receipt `C:\src\Antiphon\.antiphon\rolling-server2\c72721ffbe14c3d9`. Host jq check passed (`jq-1.7.1`, digest `5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5`, `outcome=existing`). `verify-runner-caches` accepted with `schema=2` `kind=cold` `smoke=not-run`. Mounts included `antiphon-runner_work` `/work`, `antiphon-runner_runner-state` `/state`, `antiphon-runner_dind-data` `/var/lib/docker`, `antiphon-runner_runner-tmp` `/tmp`, and the three cache volumes. The phase skipped `deploy-parent` because `buildVersion` was already the target SHA. Drain clear reason recorded on the runner is `CARD-0727 rolling upgrade complete`.

`GET /api/session-runners/server2/status` after that phase:

- `buildVersion=09e48c5d6f1d404cfea8ec0d992270424fd2a184`
- `runnerStoreId=f519bd08-e53a-47d1-adb1-2ab33475446f` (unchanged)
- `processBootId=0e14a3d4-ba81-4ae9-a956-515188b82233` (unchanged)
- `acceptingNewWork=true` `draining=false` `dispatchEligible=true` `available=true`
- `sessions=0` `runnerSessions=0` `queuedTasks=0`

Container `antiphon-runner-session-runner-1` stayed `a6457bc89508` on `antiphon-server2/session-testing:09e48c5d6f1d`. server2-temp stayed accepting the same SHA with 4 sessions.

Gate 7 version smoke at 2026-10-18T10:44:39Z, exit 0:

- `runner-drain.ps1 status -RunnerId server2` showed `acceptingNewWork=true` and the target `buildVersion`
- uid 1654 `codex --version` printed `codex-cli 0.160.0`
- `pwsh --version` printed `PowerShell 7.5.4`
- `dotnet --version` printed `10.0.402`

The status row also has `codexCliVersionError=stderr_output` with `codexCliVersion=0.160.0`. The uid 1654 exec itself exited 0.

A second `check-census` at 2026-10-18T10:45Z, receipt `C:\src\Antiphon\.antiphon\rolling-server2\c727d5770d4bf76d`, still had server2 `boundOpen=0` and `landPending=0` on both runners. server2-temp `boundOpen=4`.

The Plan canary, `verify-card0849-caches.ps1 -Case Both`, `drain-temp`, and `retire-temp` had not been run at that point. Canonical checkout stayed `69978ec1a246cd7a860f88a364eafc53a416aff7`.

## Continuation: canary, Both, and drain-temp

Plan canary `a648697f` on server2 (CARD-1311) was Queued at 2026-10-18T10:47Z, Dispatched at 10:52:10Z, and Succeeded at 10:53:32Z. Session `5e1bdc76-e7ac-4e2e-bfe0-b47ed515c29a` ended 10:53:24Z. It pushed `e6963c2e7f56c8a5d6c9a9396f306153c26ba420` on `feat/card-task-a648697f`. `docs/investigations/2026-10-18-server2-canary.md` at that commit ends with `canary: ok`. No land was requested.

`verify-card0849-caches.ps1` requires checkout HEAD to equal `-Sha`. Canonical stayed at `69978ec1a246cd7a860f88a364eafc53a416aff7`. Both ran from a detached worktree `C:\Antiphon\worktrees\c849-both-26205ec4` at `09e48c5d6f1d404cfea8ec0d992270424fd2a184`, 2026-10-18T10:55:15Z-10:55:32Z, exit 0:

```
C849_BOTH kind=cold schema=2 digestType=none runners=2 writableVolumes=3 sharedVolumes=3 privateTmpVolumes=2 tmpMode=1777 failures=0
```

Receipt copy: `C:\src\Antiphon\.antiphon\c849-c84932264738a8ab43a8`.

`check-census` at 2026-10-18T10:56:19Z, receipt `C:\src\Antiphon\.antiphon\rolling-server2\c7279fa60cdb3480`: server2 `boundOpen=0` `landPending=0`, server2-temp `boundOpen=3` `landPending=0`.

`drain-temp -WaitIdleMinutes 240` started at 2026-10-18T10:56Z from `C:\src\Antiphon`. By 10:57:28Z server2-temp was `draining=true`, `redirectTo=server2`, `retireWhenIdle=true`, `acceptingNewWork=false`, `retiredAt` empty, `sessions=3`. Standing server2 stayed accepting on the target SHA and store `f519bd08-e53a-47d1-adb1-2ab33475446f`.

Temp seats fell from 3 to 2 at 11:11Z when Review `6b367dd6` Succeeded and session `10d3e112-c311-46de-a230-18870b325a3f` stopped. At 11:24Z the two seats still Running were:

| session | age | task | task status |
|---|---|---|---|
| `81088675-4185-4faf-aa2d-d458d9bc8339` | 81933 s | `89bf7b58-b629-46ca-9b4e-500fd29014c0` CARD-1320 S3 | Failed, session still Running |
| `ee2f68fa-b194-4c77-8738-0435a9258246` | 75913 s | `bd6bce3d-816e-469f-a30d-0621ab09b644` CARD-1320 S4 | Failed, session still Running |

`openTaskId` on both slots was null. `orphan=false`. Neither session was released. The local wait was stopped at 11:24:37Z so this report could be written inside the task budget. The server drain was left in place: temp still `draining=true`, `redirectTo=server2`, `retireWhenIdle=true`, `acceptingNewWork=false`, `sessions=2`. Standing server2 was accepting with `sessions=1`. `retire-temp` was not run.
