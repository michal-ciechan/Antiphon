# retire-temp stopped at the dry-run preview (2026-10-18)

`retire-temp` was not applied. The dry-run refused `RecycleComposeMismatch` before it printed any `C1008_PREVIEW remove=` line. No temp volume was removed. `-ResumeRecycle` was not passed. The standing `antiphon-runner` container was not restarted.

## Preconditions

Canonical checkout `C:\src\Antiphon` and `GET /api/version` are both `61117dd1663d2f47ce4708c18b609b8936491986` (`land-v2`). `/health` was Healthy. The checkout stayed detached; it was not switched or pulled.

`check-census` at `C:\src\Antiphon\.antiphon\rolling-server2\c7270ab68deeaf57`:

```
RECYCLE_PROJECT id=d4ea7ae9-e769-474b-95b9-aa25fbc1303f name=Antiphon resolvedBy=path
RECYCLE_CENSUS runner=server2 open=9 boundOpen=1 landPending=0 scopes=2 elapsedMs=1027
RECYCLE_CENSUS runner=server2-temp open=9 boundOpen=0 landPending=0 scopes=2 elapsedMs=1006
```

Standing server2 was accepting `09e48c5d6f1d404cfea8ec0d992270424fd2a184`, store `f519bd08-e53a-47d1-adb1-2ab33475446f`, boot `0e14a3d4-ba81-4ae9-a956-515188b82233`. server2-temp was retired at `2026-10-18T11:45:09.071621Z` (`retireReason=idle`), `runnerSessions=null`, sessions 0, queued 0, `redirectTo=server2`. Temp containers: 0. The four `antiphon-runner-temp_*` volumes were present.

Journal `/home/mc/antiphon-server2/recycle/c1008a24aceabd9ff4ec9a174752ae3ca8475.json` is still `phase=preflight`, `outcome=refused`, `ownedRemoved=false`, project `antiphon-runner-temp`, diagnosis `RecycleGitAuditUnknown audit check=gitdir-entry status=0 repo=repos/antiphon`. All four temp volume outcomes are `pending`. Stop and remove receipts are empty. The dry-run did not ask for `-ResumeRecycle`.

## Preview

Command, from `C:\src\Antiphon`:

```
pwsh -NoProfile -File C:\src\Antiphon\scripts\deploy-server2.ps1 -Rolling -Sha 09e48c5d6f1d404cfea8ec0d992270424fd2a184 -Phase retire-temp -DryRun
```

Exit 1. Printed lines:

```
DIAGNOSIS=RecycleComposeMismatch
Rolling deploy stopped: HostCaseFailed retire-temp-runner exit=2
```

Receipt `C:\src\Antiphon\.antiphon\rolling-server2\c72703fb2b2db310`.

- `host-jq-retire-temp.json`: existing `/usr/local/bin/jq` 1.7.1, sshExit 0. Not installed.
- `retire-temp-containers`: accepted, `outcome=preview`, `candidates=[]`, `removals=[]`, operation `c994d6ef4e3a02f447fe97162e46ef446c00`. No container id was removed.
- `retire-temp-runner/c590-result.json`: `{"accepted":false,"diagnosis":"RecycleComposeMismatch","exit":2}`. No `C1008_PREVIEW` line was emitted. No new `c1008` journal was written.

The refusal does not name a gitdir admin. `c1008_compose_model` drops compose stderr (`2>/dev/null`) and reports only `RecycleComposeMismatch`.

The apply path in `case_retire_temp_runner` loads `RUNNER_GROK_STORE_DIR` from `/home/mc/antiphon-server2/secrets/stack.temp.env` before `c1008_recycle`. The dry-run intercept (`C1008_DRY_RUN=1`) calls `c1008_recycle` before that assignment. `compose_temp` then exports `RUNNER_GROK_STORE_DIR` as empty, which overrides the env file. `docker-compose.server2-runner.temp.yml` requires `${RUNNER_GROK_STORE_DIR:?...}:/state/grok`, so `docker compose config` fails. The container-cleanup preview sets the variable first, which is why that case was accepted. The env value itself is a non-empty absolute path (length 63). It was not printed.

This is not the settled-task census race. It was not retried.

An earlier invocation of the same arguments resolved `scripts/deploy-server2.ps1` from the task worktree. It stopped at `RecycleProjectUnresolved cause=NoMatch candidates=81 urlMatches=0 pathMatches=0` because the project local path is the canonical checkout. Receipt `C:\Antiphon\worktrees\card-task-53b32920\.antiphon\rolling-server2\c7279a1259600595` contains only the host-jq check and a partial census. No host case ran.

## After the refusal

Standing container `a6457bc895084a5cefbe5e13de377f90e60fb5693b9030604b20bf612b6451d0` (`antiphon-runner-session-runner-1`) is still healthy, started `2026-10-18T00:32:45.678349724Z`, image `antiphon-server2/session-testing:09e48c5d6f1d`. `antiphon-runner-state-init-1` and `antiphon-runner-build-slots-1` were not changed.

Volumes still present: `antiphon-runner-temp_work`, `antiphon-runner-temp_runner-tmp`, `antiphon-runner-temp_dind-data`, `antiphon-runner-temp_runner-state`, plus `antiphon-runner_work`, `antiphon-runner_runner-tmp`, `antiphon-runner_dind-data`, `antiphon-runner_runner-state`, and the three `antiphon-runner-cache-*` volumes.

`df -Pk /` available was 191990416 KiB after the dry-run (about 183 GiB). Temp project containers remain 0.

A later status read still had server2 accepting the same SHA and store, with `sessions=0`, `runnerSessions=0`, `queuedTasks=1`. That queue was not a land. `landPending` at the census and in the cleanup receipt was 0.

## Decision

The preview did not list the four temp volumes, so the real `retire-temp` was not started in that pass. The handoff was to authorize the apply despite the dry-run refusal, or to fix the preview path first.

## Apply

Authorized apply, still without `-ResumeRecycle`. Before it, the refused journal's volume names were exactly `antiphon-runner-temp_dind-data`, `antiphon-runner-temp_runner-state`, `antiphon-runner-temp_runner-tmp`, and `antiphon-runner-temp_work`. `docker volume ls --filter label=com.docker.compose.project=antiphon-runner-temp` listed those same four names. No `antiphon-runner_*` name was in either set. `antiphon-runner_work`, `antiphon-runner_runner-tmp`, and `antiphon-runner_dind-data` existed and were mounted on `a6457bc895084a5cefbe5e13de377f90e60fb5693b9030604b20bf612b6451d0`. Temp containers were 0. `check-census` `C:\src\Antiphon\.antiphon\rolling-server2\c727fe4456f7f041` reported `landPending=0` for both runners (`server2` boundOpen=1, `server2-temp` boundOpen=0).

Command, from the canonical script:

```
pwsh -NoProfile -File C:\src\Antiphon\scripts\deploy-server2.ps1 -Rolling -Sha 09e48c5d6f1d404cfea8ec0d992270424fd2a184 -Phase retire-temp
```

Exit 0 at `2026-10-18T15:09:06Z`. Log `C:\src\Antiphon\.antiphon\rolling-server2\retire-temp-53b32920.log`. Receipt `C:\src\Antiphon\.antiphon\rolling-server2\c7276395b1ed1471`.

Container cleanup operation `c99482423dc829c049fb9d44133513f49d92` completed with `candidates=0` and `removals=[]`. Recycle operation `c1008378b136c050f457b897835547f1a9073` is `phase=volumesRemoved`, `outcome=completed`, project `antiphon-runner-temp`. All four temp volume outcomes are `removed`. Preserved names are the three `antiphon-runner-cache-*` volumes and `antiphon-runner_runner-state`. Journal free space went from 196254924800 bytes to 219499913216 bytes. The old journal `c1008a24aceabd9ff4ec9a174752ae3ca8475` is still `phase=preflight`, `outcome=refused`, `ownedRemoved=false`.

After the phase, the temp project volume list is empty and no `antiphon-runner-temp_*` volume remains. Temp containers are 0. Standing container `a6457bc89508` is healthy with the same start `2026-10-18T00:32:45.678349724Z` and still mounts `antiphon-runner_work`, `antiphon-runner_runner-tmp`, `antiphon-runner_dind-data`, `antiphon-runner_runner-state`, and the three cache volumes. server2 accepts `09e48c5d6f1d404cfea8ec0d992270424fd2a184`, store `f519bd08-e53a-47d1-adb1-2ab33475446f`, boot `0e14a3d4-ba81-4ae9-a956-515188b82233`, `acceptingNewWork=true`. server2-temp stays retired at `2026-10-18T11:45:09.071621Z` with `runnerSessions=null`. `df -Pk /` available is 214167472 KiB (70% used), up from 191807288 KiB just before the phase.

--- next stage ---
next: verify
handoff: retire-temp removed the four temp volumes. Standing server2 is still accepting 09e48c5d with store f519bd08. The documented next check is CP-5, verify-card0849-caches.ps1 -Case Retired. The dry-run RecycleComposeMismatch is a separate card. Do not ResumeRecycle c1008a24aceabd9ff4ec9a174752ae3ca8475.
artifact: docs/investigations/2026-10-18-retire-temp-complete.md
