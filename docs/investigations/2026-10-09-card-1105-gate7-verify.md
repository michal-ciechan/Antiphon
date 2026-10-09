# CARD-1105 gate 7 cache verify

Worktree `C:\Antiphon\worktrees\card-task-0200b90f`. The verify ran at HEAD `8892b7b759d96b3b5d6fa20518caf2c79c6be81a` with `-Sha 8892b7b759d96b3b5d6fa20518caf2c79c6be81a`.

`-Case Both` exited 1. It did not print a `C849_BOTH` summary. The failing line is `DIAGNOSIS=CacheRunnerVersionMismatch` for `verify-runner-caches` on `server2-temp`.

`docs/docker-stack.md` records this success shape for the gate:

`C849_BOTH runners=2 smokes=2 sharedVolumes=3 privateTmpVolumes=2 tmpMode=1777 failures=0`

No docker stop, rm, restart, up, prune, deploy phase, or volume recycle was run.

## verify-card0849-caches.ps1

Command:

```
pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Both -Sha 8892b7b759d96b3b5d6fa20518caf2c79c6be81a
```

Exit code: 1

Verbatim combined output:

```
DIAGNOSIS=
DIAGNOSIS=CacheRunnerVersionMismatch
Exception: C:\Antiphon\worktrees\card-task-0200b90f\scripts\verify-card0849-caches.ps1:133
Line |
 133 |  . DE -ne 0) { throw "C849 case failed: $remoteCase runner=$($runners[$i .
     |                ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
     | C849 case failed: verify-runner-caches runner=server2-temp
     | evidence=C:\Antiphon\worktrees\card-task-0200b90f\.antiphon\c849-c849d0eb40f7ba8f4a74\1
```

Local gitignored evidence: `C:\Antiphon\worktrees\card-task-0200b90f\.antiphon\c849-c849d0eb40f7ba8f4a74`.

`0/verify-runner-caches/c590-result.json`:

```
{"accepted":true,"diagnosis":"","exit":0}
```

`0/verify-runner-caches/seed-contract.txt`:

```
schema=2
kind=cold
digest-type=none
digest=none
smoke=not-run
```

`0/verify-runner-caches/status.json`:

```
{"buildVersion":"8892b7b759d96b3b5d6fa20518caf2c79c6be81a","dispatchEligible":true,"acceptingNewWork":true,"draining":false,"sessions":0,"runnerSessions":0,"queuedTasks":0}
```

`0/verify-runner-caches/runner-mounts.txt`:

```
volume antiphon-runner-cache-npm-content /home/app/.npm/_cacache true
volume antiphon-runner_dind-data /var/lib/docker true
volume antiphon-runner_runner-tmp /tmp true
volume antiphon-runner_work /work true
volume antiphon-runner_runner-state /state true
volume antiphon-runner-cache-nuget-packages /home/app/.nuget/packages true
volume antiphon-runner-cache-nuget-scratch /var/cache/antiphon/nuget-scratch true
```

`1/verify-runner-caches/c590-result.json`:

```
{"accepted":false,"diagnosis":"CacheRunnerVersionMismatch","exit":2}
```

`server2-temp` wrote no `status.json` and no `runner-mounts.txt`. The version check returns before those files. Both case directories did write `cache-roots.txt` after the script's writability probe (create, rename, remove one probe file per volume):

```
antiphon-runner-cache-nuget-packages nuget-packages 1654:1654:700
antiphon-runner-cache-nuget-scratch nuget-scratch 1654:1654:700
antiphon-runner-cache-npm-content npm-content 1654:1654:700
```

`server2` is on the requested SHA, accepting, and idle. Its private tmp mount name is still `antiphon-runner_runner-tmp`. The case was accepted, so the script's `/tmp` mode check passed at 1777. The shared seed marker is cold, so this run did not execute the apphost smoke.

`server2-temp` is `dispatchEligible=true`. The mismatch is `buildVersion`. A later Both that passed the version check would still be on this cold marker. The cold success line in `scripts/verify-card0849-caches.ps1` is `C849_BOTH kind=cold schema=2 digestType=none runners=2 writableVolumes=3 sharedVolumes=3 privateTmpVolumes=2 tmpMode=1777 failures=0`, which is not the documented `smokes=2` line. Temp's tmp mount was not collected.

## runner-drain.ps1 status

Command:

```
pwsh -NoProfile -File scripts/runner-drain.ps1 status -RunnerId server2
```

Exit code: 0

Verbatim output:

```
{"runnerId":"server2","runnerStoreId":"f519bd08-e53a-47d1-adb1-2ab33475446f","processBootId":"1499b67a-6f61-488c-8b94-13db41f0e555","epoch":3,"available":true,"dispatchEligible":true,"lastHeartbeatUtc":"2026-10-09T22:43:31.7202855+00:00","platform":"linux","buildVersion":"8892b7b759d96b3b5d6fa20518caf2c79c6be81a","disconnectReason":null,"pendingEvents":0,"pendingEventBytes":0,"lastDisconnectAtUtc":"2026-10-09T10:21:19.4270501+00:00","reconnects":2,"lastCatchUpMs":3,"acceptingNewWork":true,"draining":false,"drainedAt":null,"drainReason":"CARD-0727 rolling upgrade complete","redirectTo":null,"retireWhenIdle":false,"idleObservedAt":null,"retiredAt":null,"retireReason":null,"sessions":0,"queuedTasks":0,"runnerSessions":0,"codexCliVersion":"0.160.0","codexCliVersionCheckedAtUtc":"2026-10-09T22:40:00.1044832+00:00","codexCliVersionError":"stderr_output","codexCliVersionStale":false}
```

`buildVersion=8892b7b759d96b3b5d6fa20518caf2c79c6be81a`, `acceptingNewWork=true`, `draining=false`, `sessions=0`, `runnerSessions=0`, `queuedTasks=0`.

Command:

```
pwsh -NoProfile -File scripts/runner-drain.ps1 status -RunnerId server2-temp
```

Exit code: 0

Verbatim output:

```
{"runnerId":"server2-temp","runnerStoreId":"cd9807fa-754f-45ad-b6b7-d69100479fd0","processBootId":"f6b5c466-0e39-4536-95cd-a638b99e6ce0","epoch":3,"available":true,"dispatchEligible":true,"lastHeartbeatUtc":"2026-10-09T22:43:31.5330363+00:00","platform":"linux","buildVersion":"d985af05b5ace2f13ec3f0d886c15992e9c13c3f","disconnectReason":null,"pendingEvents":0,"pendingEventBytes":0,"lastDisconnectAtUtc":null,"reconnects":1,"lastCatchUpMs":4,"acceptingNewWork":true,"draining":false,"drainedAt":null,"drainReason":"CARD-0849 cache verification passed","redirectTo":null,"retireWhenIdle":false,"idleObservedAt":null,"retiredAt":null,"retireReason":null,"sessions":3,"queuedTasks":0,"runnerSessions":3,"codexCliVersion":"0.160.0","codexCliVersionCheckedAtUtc":"2026-10-09T22:41:44.4684481+00:00","codexCliVersionError":"stderr_output","codexCliVersionStale":false}
```

`buildVersion=d985af05b5ace2f13ec3f0d886c15992e9c13c3f`, `acceptingNewWork=true`, `draining=false`, `sessions=3`, `runnerSessions=3`, `queuedTasks=0`.
