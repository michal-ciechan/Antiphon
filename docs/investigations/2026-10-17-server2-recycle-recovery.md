# Server2 recycle recovery (2026-10-17)

Read-only diagnosis of rolling operation `c100808382374392e4ce1ac7dde48ca3b195f`. No rollout phase was run, and no Docker object was created, removed, or restarted.

**Decision: GO on the volume-preserving compose-up below. NO-GO on `-ResumeRecycle` for this operation while the three pending volumes must stay.** The only coded resume continues the recycle and runs `docker volume rm` on `antiphon-runner_work`, `antiphon-runner_runner-tmp`, and `antiphon-runner_dind-data`.

Target SHA: `09e48c5d6f1d404cfea8ec0d992270424fd2a184`.
Preserved runner store: `f519bd08-e53a-47d1-adb1-2ab33475446f`.

## 1. Observed now

Facts below were read in this session on 2026-10-17. Commands were `ssh mc@server2` `docker ps -a`, `docker volume ls --filter name=antiphon-runner`, `docker volume inspect` of the three recycle targets plus `antiphon-runner_runner-state`, `docker image inspect` of the `09e48c5d6f1d` tags, and `docker compose -p antiphon-runner ps -a` / `docker compose -p antiphon-runner-temp ps -a`. Runner rows are `GET /api/session-runners/{id}/status`. The refused land is `GET /api/agent-tasks/4ce63b7a-db63-4189-90a5-96a55abb03b3`.

### Runners

| Field | server2 | server2-temp |
|---|---|---|
| available | false | true |
| dispatchEligible | false | true |
| acceptingNewWork | false | true |
| draining | true | false |
| redirectTo | server2-temp | empty |
| retireWhenIdle | false | false |
| retiredAt | null | null |
| sessions | 0 | 6 |
| runnerSessions | null | 6 |
| queuedTasks | 0 | 0 |
| buildVersion | `7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce` | `09e48c5d6f1d404cfea8ec0d992270424fd2a184` |
| runnerStoreId | `f519bd08-e53a-47d1-adb1-2ab33475446f` | `1559ba5a-92e3-42a4-a891-74bce5267a2e` |
| processBootId | `9a6d75cc-09cb-4034-818e-440ceb9e2d2f` | `6810dd8b-8ea1-4fd5-8c96-fed890286a36` |
| disconnectReason | transport_abort | empty |

`GET /api/version` is `69978ec1a246cd7a860f88a364eafc53a416aff7` with `land-v2`, and `/health` is Healthy. That SHA is the canonical desktop checkout, not the rollout SHA.

### Containers

`antiphon-runner-session-runner-1` is absent. The journaled ids are absent: `fc2983ea4991fe85b31f54ee2ef66150e0d8fef03897d6cf1c2d6ca9c0889832` (session-runner) and `2c96347842e49022771fccc34f74bb04549ceeb603a5944d860b12f438953cfe` (state-init).

`docker compose -p antiphon-runner ps -a` shows one service:

| Name | Image | Status |
|---|---|---|
| antiphon-runner-build-slots-1 (`4cabb8afc676`) | `antiphon-server2/session-testing:a8b4e9e5a071` | Up 2 weeks (healthy) |

`docker compose -p antiphon-runner-temp ps -a`:

| Name | Image | Status |
|---|---|---|
| antiphon-runner-temp-session-runner-1 (`6a2838d8e7fb`) | `antiphon-server2/session-testing:09e48c5d6f1d` | Up 22 hours (healthy) |
| antiphon-runner-temp-state-init-1 (`ac048a240b9f`) | `antiphon-server2/server:09e48c5d6f1d` | Exited (0) 22 hours ago |

Other host containers (messaging, windmill, traefik, and the rest of `docker ps -a`) were still present and are outside this recovery.

### Volumes

All three recycle targets exist, driver `local`, created `2026-10-11T13:41:14Z`, Compose project `antiphon-runner`, Compose version `2.18.1`.

| Volume | Compose label | Mountpoint |
|---|---|---|
| antiphon-runner_work | work | `/var/lib/docker/volumes/antiphon-runner_work/_data` |
| antiphon-runner_runner-tmp | runner-tmp | `/var/lib/docker/volumes/antiphon-runner_runner-tmp/_data` |
| antiphon-runner_dind-data | dind-data | `/var/lib/docker/volumes/antiphon-runner_dind-data/_data` |

`antiphon-runner_runner-state` also exists (created `2026-09-22T19:10:50Z`). Its `runner-store-id` file is present, mode `644`, owner `1654:1654`, 36 bytes, and the bytes equal `f519bd08-e53a-47d1-adb1-2ab33475446f`.

On the work volume, observed this session: 776 directories directly under `worktrees/`, one directory under `repos/`, and `repos/antiphon/.git` present. The saved audit in the journal says `repositories=800 partial=800` (800 `entry repo=` lines). Those counts are different objects: the audit counts Git directory entries; the directory listing counts checkout directories.

Host checkout `/work/repos/antiphon` is already `09e48c5d6f1d404cfea8ec0d992270424fd2a184`. `df -Pk /` reports 200111792 KiB available (about 191 GiB), 72% used. The cache budget gate is 20 GiB (`20971520` KiB). Disk is not the reason to delete volumes.

`/home/mc/antiphon-server2/secrets/stack.env` still has `SOURCE_REVISION=7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce` and `SOURCE_SHA12=7507b3cfe0e2`. `BUILD_SLOTS_SHA12=a8b4e9e5a071`. The other keys are the path and origin names the deploy script writes (`PHONE_HOME_SERVER_ORIGIN`, deploy-key path, phone-home path, Claude token path, git identity path, Codex home path, GitHub token dir, git user name, git user email). Values were not printed.

### Images

| Tag | Id |
|---|---|
| `antiphon-server2/session-testing:09e48c5d6f1d` | `sha256:a9be72ebb98bddae896fd93649897cdc7d8c59449a36629e4dd2100fd956b50c` |
| `antiphon-server2/server:09e48c5d6f1d` | `sha256:370f22e73f26498aecfe8672850cf898f962a9d09b7718784c5c0bdef7db9082` |
| `antiphon-server2/session-testing:7507b3cfe0e2` | `sha256:ea8bafe24373694b3421d63afdffb4533faf0710f709d96dea661123b55e6835` |
| `antiphon-server2/server:7507b3cfe0e2` | `sha256:5b2be260c242ec1a4d9e6c9a2c503295c3dd145eddd99a1e11e25bb6c2abda6f` |

The `09e48c5d6f1d` session-testing image is the one temp is running. Temp's registered `buildVersion` is the full target SHA, so that image's `SourceRevisionId` already reports `09e48c5d`. The `7507b3cfe0e2` images are still on the host. The journal's stopped session-runner image is the `7507b3cfe0e2` id above.

### Journal

Host file `/home/mc/antiphon-server2/recycle/c100808382374392e4ce1ac7dde48ca3b195f.json` matches the desktop copy `C:\src\Antiphon\.antiphon\rolling-server2\c72724f3f2dfd7b8\deploy-parent\recycle.json`.

- schema 1, project `antiphon-runner`, context `default`, dryRun false
- sourceSha `09e48c5d6f1d404cfea8ec0d992270424fd2a184`
- phase `containersRemoved`, outcome `refused`, ownedRemoved true
- diagnosis `RecycleLandInFlight 4ce63b7a-db63-4189-90a5-96a55abb03b3`
- previousSha `7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`
- composeDigest and previousComposeDigest are both `0c0d1515ccc640731beca6942613a1d489c3c364898eb35e0bd02dcc441e1147`
- the three target volume outcomes are `pending`; `diskBefore` is absent
- stop and remove receipts contain both journaled container ids
- preserved: the three cache volumes and `antiphon-runner_runner-state`
- liveZero at the pre-removal census: store `f519bd08-e53a-47d1-adb1-2ab33475446f`, build `7507b3cf...`, sessions 0, runnerSessions 0, queuedTasks 0, available true, draining true, redirect `server2-temp`, acceptingNewWork false, dispatchEligible true, retireWhenIdle false

The rendered compose files saved for `09e48c5d6f1d` and `7507b3cfe0e2` are byte-identical (`sha256 6506813bcbcc2976d1229e1bcadcb38a750402a53bdf5d4a3903a1d97114f0b0`, 10510 bytes). This worktree's `docker-compose.server2-runner.yml` has the same hash. The mount roster did not change between the two SHAs.

Task `4ce63b7a-db63-4189-90a5-96a55abb03b3` was, in that census, a Succeeded Plan on `server2-temp` with `landRequestedAt=2026-10-17T15:17:43.438343Z` and `landStartedAt=2026-10-17T15:17:44.132937Z`. Observed now: status Succeeded, role Plan, runner `server2-temp`, `landRequest.state=Completed`, `terminalEventId=37779ae6-7544-4eaa-bd58-89c112b1756f`, and both land timestamps null. That one land is no longer in flight. This session did not re-run the two-pass census.

Parent receipt `C:\src\Antiphon\.antiphon\rolling-server2\c72724f3f2dfd7b8\deploy-parent\c590-result.json`: `accepted=false`, same diagnosis, exit 2. `command.log` in that directory is empty.

## 2. Resume

`scripts/deploy-server2.ps1` has `-ResumeRecycle` and `-DryRun`. It has no `-Resume`, `-RecoverRecycle`, or `-ContinueOperation`. `-DryRun` together with `-ResumeRecycle` throws `RecycleContextInvalid`.

The documented command is the one in `docs/docker-stack.md` (volume recycling, CARD-1008):

```powershell
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 09e48c5d6f1d404cfea8ec0d992270424fd2a184 -Phase redeploy-old -ResumeRecycle c100808382374392e4ce1ac7dde48ca3b195f
```

Run that only from the canonical checkout `C:\src\Antiphon`, never from this worktree. It is the wrong command for this recovery.

What it does with a journal in `phase=containersRemoved`:

1. The wrapper admits a null `runnerSessions` only when `-ResumeRecycle` is set, `available` is false, and `dispatchEligible` is false (`deploy-server2.ps1` around the redeploy-old branch). It then requires `sessions=0`, `queuedTasks=0`, `draining=true`, `redirectTo=server2-temp`, `retireWhenIdle=false`, `retiredAt` empty, `acceptingNewWork=false`, and temp `acceptingNewWork=true`. A plain redeploy-old hits `RunnerCounterUnknown server2 runnerSessions` in `Assert-ZeroCounters` before `deploy-parent`. That matches the failed retry.
2. The host script loads the journal and requires the same source SHA, operation id, project, and compose-model digest (`c590-remote.sh` `c1008_recycle`).
3. `c1008_status_proof` allows null `runnerSessions` for saved phases `stopped`, `containersRemoved`, `removing`, `volumesRemoved`, `recreating`, and `verified`, and requires `ownedRemoved=true` plus no non-broker project containers.
4. The early return that skips removal applies only to `phase=recreating` or `phase=verified`, and only when every target volume outcome is `removed` or `alreadyAbsent`. `containersRemoved` with `pending` volumes does not take it.
5. Resume re-runs the publication audit (`c1008_audit_checked`) while the work volume is still `pending`. The last clean audit of this volume was the long one (`repositories=800`). Budget default is 10800 seconds.
6. After that audit and another `c1008_status_proof`, the script sets `phase=removing` and executes `docker volume rm --` on each of the three targets (`c590-remote.sh` immediately after the post-removal census). The refusal today happened at that second census, which is why the volumes are still `pending`.
7. Only after `phase=volumesRemoved` does `case_deploy_parent` rewrite `stack.env`, build images, seed an empty work volume, and `compose_host up -d --no-build`.

A land in flight fails that second census with `RecycleLandInFlight` before the `docker volume rm`. The census is doing the job the doc describes: a refusal stops destructive work. The containers were already gone because removal is journaled at `phase=containersRemoved` before that census.

### Preserve path

There is no flag that means "containers are gone, keep the volumes, start the new runner." The smallest equivalent of the script's post-removal `up`, skipping the `docker volume rm` loop, is `compose_host`'s `up -d --no-build`.

`compose_host` (`c590-remote.sh`) is:

```bash
docker compose -p antiphon-runner \
  -f /work/repos/antiphon/docker-compose.server2-runner.yml \
  --project-directory /work/repos/antiphon \
  up -d --no-build
```

with `SOURCE_SHA12` taken from `stack.env` unless the file is rewritten first. The file still says `7507b3cfe0e2`. Calling compose without overriding it selects the old image. The script rewrites `SOURCE_REVISION` and `SOURCE_SHA12` to the target SHA before `up`. The image lines in `docker-compose.server2-runner.yml` are `antiphon-server2/server:${SOURCE_SHA12}` (state-init) and `antiphon-server2/session-testing:${SOURCE_SHA12}` (session-runner). Both `09e48c5d6f1d` tags exist, so `--no-build` does not need a build and must not pull.

Named volumes `work`, `runner-state`, `dind-data`, and `runner-tmp` are not `external: true`. Compose project `antiphon-runner` already owns `antiphon-runner_work`, `antiphon-runner_runner-state`, `antiphon-runner_dind-data`, and `antiphon-runner_runner-tmp`. `up` attaches those existing volumes. It does not create a second set and it does not delete them. The cache volumes are `external: true` and stay as they are. `build-slots` is profile `broker` and is already running; a default `up` does not recreate it. Leave `BUILD_SLOTS_SHA12=a8b4e9e5a071`.

`session-runner` `depends_on` state-init with `service_completed_successfully`, so this `up` runs state-init. `docker/stack/init-state.sh` does `mkdir -p` and `chown` of `/state`, `/work`, and `/runner-state` to uid 1654. It does not delete worktrees. A directory owned by neither root nor 1654 fails `ForeignStateOwner` (exit 42). `seed_runner_checkout` clones only when `/work/repos/antiphon/.git` is absent and the directory is empty. That `.git` is present, so the seed would print `present` and leave the checkout. The `depends_on` state-init is enough; a separate seed container is not required.

Phone-home `buildVersion` is the image `SourceRevisionId`. Temp is already registered at the full target SHA on this same session-testing tag, so the new main container reports `09e48c5d6f1d404cfea8ec0d992270424fd2a184` once it connects. The store file on the preserved state volume is the current registration id, so the new process should come back as `f519bd08-e53a-47d1-adb1-2ab33475446f`.

The script's seal, after health and the phone-home probes, is `c849_status_body` matching all of: `buildVersion` equals the SHA, `runnerStoreId` equals the journal `liveZero` store, `available=true`, `dispatchEligible=true`, `draining=true`, `acceptingNewWork=false`. A miss is `RecycleRegistrationMismatch`. Drain clear is not part of `deploy-parent`. The wrapper clears it afterwards with reason `CARD-0727 rolling upgrade complete`, then waits for `acceptingNewWork=true` at the SHA.

`/tmp` inside the new container is the existing `antiphon-runner_runner-tmp` volume. `c1008_verify_tmp` requires mode `1777`. A present `/tmp/antiphon-pty-hosts` must be a directory uid 1654 can search and that contains a readable file. An absent pty-hosts directory is success.

### Receipt to write by hand

Do this only after the registration seal above has passed. Until then, leave the journal at `containersRemoved` / `refused`.

`Assert-NoIncompleteRecycle` treats every same-SHA `antiphon-runner` journal whose `phase` is not the string `completed` as unfinished, and a later plain redeploy-old then throws `RecycleResumeRequired`. The success path writes `phase=verified`, not `phase=completed`, and `verified` still requires the volumes to be `removed`. Setting `verified` here would either fail the resume proof or, if the outcomes were forged to `removed`, allow a later resume to treat the volumes as a recreated generation.

Set `phase=completed` and move the three outcomes off `pending`. Resume of a volume that still exists requires `outcome=pending` (`c1008_recycle`, the resume volume loop). Any other outcome refuses `RecycleResumeMismatch` before `docker volume rm`.

On server2, after the seal and before the wrapper redeploy-old:

```bash
f=/home/mc/antiphon-server2/recycle/c100808382374392e4ce1ac7dde48ca3b195f.json
cp -a -- "$f" "${f}.before-preserve-seal"
jq -c '
  .phase = "completed"
  | .outcome = "completed"
  | .diagnosis = "preserve-seal volumes kept"
  | .volumes["antiphon-runner_work"].outcome = "kept"
  | .volumes["antiphon-runner_runner-tmp"].outcome = "kept"
  | .volumes["antiphon-runner_dind-data"].outcome = "kept"
' "$f" > "${f}.tmp"
mv -- "${f}.tmp" "$f"
```

Copy that sealed file into the new rolling evidence directory. Do not pass `-ResumeRecycle c100808382374392e4ce1ac7dde48ca3b195f` after this. A resume of `phase=completed` does not take the early return; with outcomes `kept` it refuses on the pending check. With outcomes left at `pending` it would delete the volumes.

## 3. Risks

- `-ResumeRecycle` on the current journal deletes the three pending volumes after another full audit. `antiphon-runner_work` holds the 776 worktree directories and the runner checkout whose audit recorded 800 Git entries, all published at audit time (`partial=800`). Ignored build output, nested images in `antiphon-runner_dind-data`, and `/tmp` contents in `antiphon-runner_runner-tmp` are not covered by that publication proof and would be gone. `antiphon-runner_runner-state` and the three cache volumes are on the preserve list even in the destructive path.
- Free space is about 191 GiB. The 20 GiB budget gate already passes. Deleting the volumes is not required to start the runner.
- A compose up that leaves `SOURCE_SHA12=7507b3cfe0e2` starts the old image. The registration would stay on `7507b3cf`. Do not clear drain in that state.
- state-init chowns `/work`. It does not delete it. `ForeignStateOwner` stops the up and leaves the volumes in place.
- A new store id means the state file was not the one mounted at `/state`. Stop before drain clear. Replacing the store is the CARD-0953 retire/clear/lease path, and it is outside this recovery.
- The nested dockerd has to open the existing `dind-data`. Health includes `docker info`. A slow first start is expected. It is not a reason to remove the volume.
- server2-temp is the only accepting runner and has 6 sessions. Do not drain it, stop it, or retire it until main has passed smoke.
- The host checkout is already the target SHA, so `ensure_checkout` will not `reset --hard` it if the remote SHA is `09e48c5d`. A different `-Sha` would.
- Canonical desktop `C:\src\Antiphon` is detached at `69978ec1a246cd7a860f88a364eafc53a416aff7`, which matches `/api/version`. Two untracked files under `docs/investigations/` are the only dirt. Do not move that checkout to the rollout SHA. `Invoke-HostJq -Mode check` does not require desktop HEAD to equal `-Sha`. The remote script that gets scp'd is canonical's `scripts/c590-remote.sh` at `69978ec1`, which is the commit this diagnosis read. The compose file that `up` uses is the server2 checkout, already at `09e48c5d`, and that render matches the saved `09e48c5d` compose bytes.

## 4. Steps, in order

Re-check the census immediately before step 2. A new land, or any open task bound to server2, stops the run. `check-census` is GET-only.

From `C:\src\Antiphon`:

```powershell
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 09e48c5d6f1d404cfea8ec0d992270424fd2a184 -Phase check-census
```

Require `landPending=0` on both runners and `boundOpen=0` on server2. Temp may still show open work. Also require the section 1 runner row for server2 (null `runnerSessions`, drained onto temp, not retired) and temp still `acceptingNewWork=true` at `09e48c5d`.

On server2, as `mc`, change only the two SHA lines, then start the project. Do not print `stack.env`.

```bash
cp -a -- /home/mc/antiphon-server2/secrets/stack.env /home/mc/antiphon-server2/secrets/stack.env.c100808382374392e4ce1ac7dde48ca3b195f
sed -i -E 's|^SOURCE_REVISION=.*|SOURCE_REVISION=09e48c5d6f1d404cfea8ec0d992270424fd2a184|' /home/mc/antiphon-server2/secrets/stack.env
sed -i -E 's|^SOURCE_SHA12=.*|SOURCE_SHA12=09e48c5d6f1d|' /home/mc/antiphon-server2/secrets/stack.env
set -a
. /home/mc/antiphon-server2/secrets/stack.env
set +a
docker compose -p antiphon-runner \
  -f /work/repos/antiphon/docker-compose.server2-runner.yml \
  --project-directory /work/repos/antiphon \
  up -d --no-build
```

Confirm the new session-runner mounts `antiphon-runner_work` at `/work`, `antiphon-runner_runner-state` at `/state`, `antiphon-runner_dind-data` at `/var/lib/docker`, and `antiphon-runner_runner-tmp` at `/tmp`. Confirm `docker exec` health on `http://127.0.0.1:8080/health`, uid 1654 can read one byte of the phone-home secret path, and `/tmp` is mode `1777`. Leave the broker container as it is.

Wait until `GET /api/session-runners/server2/status` shows all of:

- `buildVersion` = `09e48c5d6f1d404cfea8ec0d992270424fd2a184`
- `runnerStoreId` = `f519bd08-e53a-47d1-adb1-2ab33475446f`
- `available` = true
- `dispatchEligible` = true
- `draining` = true
- `redirectTo` = `server2-temp`
- `acceptingNewWork` = false
- `sessions` = 0, `runnerSessions` = 0, `queuedTasks` = 0

Then write the journal seal from section 2. If registration does not match, stop. Do not clear drain and do not resume the operation id.

From `C:\src\Antiphon`, without `-ResumeRecycle`:

```powershell
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 09e48c5d6f1d404cfea8ec0d992270424fd2a184 -Phase redeploy-old
```

With `buildVersion` already equal to the SHA, the runner dispatch-eligible, and the journal `phase=completed`, this invocation skips `deploy-parent`. It runs `verify-runner-caches` for server2, POSTs drain clear reason `CARD-0727 rolling upgrade complete`, and waits until `acceptingNewWork=true` at that SHA. If it throws `RecycleResumeRequired` or `RunnerCounterUnknown`, the seal or the registration does not match this note. Stop and report the code. Do not add `-ResumeRecycle` to get past it.

Confirm:

```powershell
pwsh -NoProfile -File scripts/runner-drain.ps1 status -RunnerId server2
```

`buildVersion` is the full target SHA, `dispatchEligible=true`, `acceptingNewWork=true`, `draining=false`, and `runnerStoreId` is still `f519bd08-e53a-47d1-adb1-2ab33475446f`.

Gate 7, from the desktop, project `antiphon-runner`:

```powershell
pwsh -NoProfile -File scripts/runner-drain.ps1 status -RunnerId server2
ssh mc@server2 'c=$(docker ps -q --filter label=com.docker.compose.project=antiphon-runner --filter label=com.docker.compose.service=session-runner); test -n "$c" && docker exec -u 1654:1654 -e HOME=/home/app -e CODEX_HOME=/state/codex "$c" codex --version && docker exec "$c" pwsh --version && docker exec "$c" dotnet --version'
```

Codex exec stays uid 1654. An orchestrator, not the Deploy worker, dispatches one sanctioned Plan canary with `-Runner server2` and waits until it starts, runs, and reports. Then:

```powershell
pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Both -Sha 09e48c5d6f1d404cfea8ec0d992270424fd2a184
```

Required summary shape: `C849_BOTH runners=2 smokes=2 sharedVolumes=3 privateTmpVolumes=2 tmpMode=1777 failures=0`.

If smoke, the canary, or Both fails, drain main back and stop:

```powershell
pwsh -NoProfile -File scripts/runner-drain.ps1 drain -RunnerId server2 -RedirectTo server2-temp -Reason 'post-upgrade smoke failed'
```

Temp stays accepting. Do not start `drain-temp`.

If gate 7 passed, from `C:\src\Antiphon`:

```powershell
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 09e48c5d6f1d404cfea8ec0d992270424fd2a184 -Phase drain-temp -WaitIdleMinutes 240
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 09e48c5d6f1d404cfea8ec0d992270424fd2a184 -Phase retire-temp
```

`drain-temp` has to wait out temp's current 6 sessions, up to 240 minutes. `retire-temp` is the volume reclaim for temp only (`down -v` on `antiphon-runner-temp`). It does not remove main's volumes.

## 5. Guard to add

Do not build this here.

`RequestAsync` in `server/Application/Services/AgentTaskLandService.cs` queues a land for any Succeeded worktree task. The only same-task conflict is `land_running`. Nothing in that admission reads runner drain or a recycle journal. Orchestrator autonomy both runs `redeploy-old` and lands a Succeeded stage. The preflight census passed, the containers were removed, and during the post-removal audit the orchestrator queued land `4ce63b7a` on a Succeeded Plan that lives on `server2-temp`. `c1008_tasks` refuses any in-flight land in the closure (`Queued`, `Held`, `Running`, `NeedsResolution`), including one that is not bound to server2. That refusal is why the volumes survived. The relaxed task compare for `containersRemoved` never runs once a land is in flight.

Add a recycle hold that `deploy-server2.ps1` arms at the start of `redeploy-old`, before `Invoke-HostCase`, and clears in a `finally` when the phase exits. `RequestAsync` and `RequestCleanupRetryCoreAsync` refuse `409` `recycle_land_hold` while the hold is armed, for every project, before they set `LandRequestedAt`. The hold has to cover both audits. A preflight-only check is what already passed. Leave the host `RecycleLandInFlight` census as it is.

Also say in `docs/orchestration-loop.md`, under the rollout autonomy, that an orchestrator does not queue a land from the start of `redeploy-old` until that phase exits, including a land of an already Succeeded task on `server2-temp` or the desktop.
