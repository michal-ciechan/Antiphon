# RC server2 rolling rollout 2026-10-16

Target SHA: `09e48c5d6f1d404cfea8ec0d992270424fd2a184` (`origin/rc/2026-10-15`).
Ran from the canonical desktop checkout `C:\src\Antiphon`, detached at that SHA.
`GET /api/version` returned the same SHA with `land-v2` and `operator-shutdown-v1`.
`GET /health` returned 200 `Healthy`. Canonical was not switched. AppHost and the desktop runner were not restarted.

Stopped after the temp smoke gate. The Plan canary dispatch was refused, so `drain-old` was not run. The standing `server2` container was left accepting on its previous build.

## Preconditions

Observed 2026-10-16T17:40Z before census.

| Check | Result |
|---|---|
| Canonical HEAD | `09e48c5d6f1d404cfea8ec0d992270424fd2a184`, detached |
| `GET /api/version` | same SHA, `land-v2` |
| `/health` | 200 Healthy |
| `landPending` | 0 on project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f` |
| Runner default | revision 2, `globalRunnerId=server2` (unchanged through smoke) |
| `server2` | accepting, `buildVersion=7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`, sessions/runnerSessions/queuedTasks `2/2/0`, store `f519bd08-e53a-47d1-adb1-2ab33475446f` |
| `server2-temp` | retired `2026-10-11T14:23:12.192262Z`, offline, `runnerSessions=null`, sessions/queuedTasks `0/0`, redirect `server2` |
| Standing container | `fc2983ea4991fe85b31f54ee2ef66150e0d8fef03897d6cf1c2d6ca9c0889832` started `2026-10-11T13:42:57.09302366Z` |

Two open tasks were bound to `server2`, both Blocked on a question with a Running session:

- `024ec8fb-7f2b-4800-9154-f34b2d2ab2d2` Plan CARD-1281, session `9b780c07-88eb-4ab8-9b87-41cffefc24fe`, blocked `2026-10-15T10:58:16Z`
- `ef29e094-b623-49e4-9947-07e9016b5ad3` Code CARD-0628, session `fab2dd61-cfff-42ff-bbcd-176e02c5b269`, blocked `2026-10-16T09:48:51Z`

No Working tasks were on `server2`. Census later reported `boundOpen=2` for `server2` and `0` for `server2-temp`. Those Blocked rows refuse `redeploy-old` with `RecycleBoundTasks` even after a seat release, because the census matches status `Blocked` plus `runnerId`.

Canonical had two pre-existing untracked patches. They were not staged or edited:

- `docs/investigations/2026-09-30-card-0859-codex-startup.patch`
- `docs/investigations/2026-09-30-card-0860-codex-c4.patch`

## Phases

### check-census

`2026-10-16T17:40:10.8996157Z` to `2026-10-16T17:40:31.4088205Z`. Exit 0.

```
RECYCLE_PROJECT id=d4ea7ae9-e769-474b-95b9-aa25fbc1303f name=Antiphon resolvedBy=path
RECYCLE_CENSUS runner=server2 open=12 boundOpen=2 landPending=0 scopes=2 elapsedMs=6400
RECYCLE_CENSUS runner=server2-temp open=12 boundOpen=0 landPending=0 scopes=2 elapsedMs=4213
```

Receipt: `C:\src\Antiphon\.antiphon\rolling-server2\c7270f002ac075ba`

### check-host-jq

`2026-10-16T17:40:41.3139041Z` to `2026-10-16T17:40:42.8006717Z`. Exit 0. Already qualified, so `provision-host-jq` was not run.

Receipt: `C:\src\Antiphon\.antiphon\rolling-server2\c727c0c1c941d608\host-jq-check-host-jq.json`

`jq-1.7.1` at `/usr/local/bin/jq`, digest `5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5`, mode 755, `installed=false`, `outcome=existing`, `sshExit=0`.

### Fixture

`2026-10-16T17:41:41.3760599Z` to `2026-10-16T17:47:38.9899040Z`. Exit 0.

```
C849_FIXTURE groups=9 controls=32 expectedRed=32 variants=47 expectedRedVariants=47 inventories=2 failures=0 productionMutations=0
```

Receipt: `C:\src\Antiphon\.antiphon\c849-c849b544f6298f2341d7`

### Inventory

`2026-10-16T17:47:43.6574504Z` to `2026-10-16T17:47:48.0812743Z`. Exit 0.

```
C849_INVENTORY runners=2 source=09e48c5d6f1d404cfea8ec0d992270424fd2a184
```

Receipt: `C:\src\Antiphon\.antiphon\c849-c8495503e5adf7bc4d09`

Standing runner mounts the three shared cache volumes and has package content (NuGet packages and npm cache non-empty). Temp container is absent. The accepted marker `/home/mc/antiphon-server2/cache/seed-accepted` is `schema=2`, `kind=cold`, `source-sha=1f43624d1353ea59338a9c4294912cb83e47fc2e`. A saved donor archive exists at `/home/mc/runner-cache-donor/temp-runner-cache.tar` and was not used. `-Cold` is for empty volumes with no marker. `-SavedDonor` refuses a cold marker (`CacheFullSeedRequired`).

### Seed

`2026-10-16T17:49:50.3461316Z` to `2026-10-16T17:49:59.8174883Z`. Exit 0. Ordinary reuse, no `-Cold` and no `-SavedDonor`.

```
C849_SEED kind=cold schema=2 digestType=none ready=true writable=3
```

Receipt: `C:\src\Antiphon\.antiphon\c849-c84964acc15dc67e45fd`

### deploy-temp

`2026-10-16T17:51:05.5773921Z` to `2026-10-16T17:58:44.5939912Z`. Exit 0.

Standing container id and start time were identical before and after:

`fc2983ea4991fe85b31f54ee2ef66150e0d8fef03897d6cf1c2d6ca9c0889832` `2026-10-11T13:42:57.09302366Z`

Receipt: `C:\src\Antiphon\.antiphon\rolling-server2\c727ee25f2c5e140`

The phase cleared the retired placeholder (`temp-retirement-clear.json`), confirmed an empty temp project census, reused the cold marker (`runner-cache-seed`), and started the temp runner.

### Smoke temp

`2026-10-16T17:58:54.1080714Z` to `2026-10-16T17:58:56.3825285Z`.

`server2-temp` after the phase: `buildVersion=09e48c5d6f1d404cfea8ec0d992270424fd2a184`, `dispatchEligible=true`, `acceptingNewWork=true`, `draining=false`, sessions/runnerSessions/queuedTasks `0/0/0`, store `1559ba5a-92e3-42a4-a891-74bce5267a2e`.

In-container tools, project `antiphon-runner-temp`, container `6a2838d8e7fb`:

- `codex-cli 0.160.0` as uid 1654
- PowerShell 7.5.4
- .NET `10.0.402`

`server2` was still accepting on `7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce` with the same store and `2/2/0` counters. `runner-defaults` remained revision 2, `globalRunnerId=server2`.

### Canary

`2026-10-16T17:59:34.2509007Z`. Not dispatched.

`scripts/delegate.ps1 -Role Plan -Worktree -Runner server2-temp -Card CARD-1311 -Title 'rc temp canary'` returned HTTP 403: `Workers cannot delegate.` This Deploy task is a worker, and the delegate API refuses worker-created tasks. No canary task id exists.

The smoke/canary gate treats that as a handoff failure. `drain-old` was not started. Old remains accepting. Temp remains accepting beside it for diagnosis. The standing container was not touched.

## Not run

`drain-old`, `redeploy-old`, smoke of upgraded old, `Both`, `drain-temp`, `retire-temp`, and the final runner `buildVersion` confirmation for standing `server2`. Standing `server2` is still `7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`. Temp is the process on the rc tip.

## Caller next step

Dispatch the documented Plan canary from an orchestrator, pinned `-Runner server2-temp` on a sanctioned card, and wait until it starts, runs, and reports. Then continue from `drain-old` only if that canary succeeds.

Before `redeploy-old`, the two Blocked `server2` tasks above have to leave the open set. A four-hour seat release zeros session counters and does not remove a Blocked `runnerId=server2` row, so the census still refuses `RecycleBoundTasks`.
