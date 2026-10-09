# CARD-1105 ResumeRecycle stopped at RecycleTmpAssetsMissing

Diagnosis only. `-ResumeRecycle` was not run again. Read-only SSH inspected container state, volume `CreatedAt`, `/tmp` names inside the already-running main and temp containers, image history, and the host journal. No `docker` stop, rm, start, compose, volume change, mount of the work volume, or deploy phase was run for this read.

Evidence for this attempt: `C:\src\Antiphon\.antiphon\rolling-server2\c72747008a95b004`. `deploy-parent.manifest.json` has `sourceSha=8892b7b759d96b3b5d6fa20518caf2c79c6be81a`, `operationId=c10084fb9431d31da48edb781dd54e5ebebf7`, `resume=true`. Both `c590-result.json` copies are `{"accepted":false,"diagnosis":"RecycleTmpAssetsMissing","exit":2}`. Orchestrator log `rollout8.log` records the same diagnosis and `Rolling deploy stopped: HostCaseFailed deploy-parent exit=2`.

The checkout that produced the script is `2e7e7e58a` (`fix(CARD-1105): allow post-removal resume when other runners' open tasks move`). The image that is running is still `8892b7b759d9`.

## 1. Where the refusal is, and when it fired

`c1008_verify_tmp` is the only producer of `RecycleTmpAssetsMissing`.

`scripts/c590-remote.sh` 5579-5586:

```
c1008_verify_tmp() {
    local container="$1" mode
    mode="$(docker exec -u 1654:1654 "$container" stat -c %a /tmp 2>/dev/null)" || c1008_refuse RecycleTmpUnavailable
    [ "$mode" = 1777 ] || c1008_refuse RecycleTmpModeInvalid
    docker exec -u 1654:1654 "$container" sh -c \
        'test -d /tmp/antiphon-pty-hosts && test -n "$(find /tmp/antiphon-pty-hosts -type f -print -quit)"' \
        >/dev/null 2>&1 || c1008_refuse RecycleTmpAssetsMissing
}
```

"Tmp assets" means one directory, `/tmp/antiphon-pty-hosts`, and at least one regular file inside it, as uid 1654. That path is `SessionRunner__PtyHostDir` (`docker-compose.server2-runner.yml` 92). The mode check is separate: anything other than `1777` is `RecycleTmpModeInvalid`, and a failed `stat` is `RecycleTmpUnavailable`. This run passed the mode check. The diagnosis is the file probe.

The only caller is `case_deploy_parent` line 6186, and only when `C1008_ACTIVE=1`. It runs after all of the following have succeeded:

| Step | Where | This run |
|---|---|---|
| `compose_host up -d --no-build` | 6098-6103 | exit was not `HostComposeFailed`. `command.log` 4-10: state-init Created through Exited, then `antiphon-runner-session-runner-1` Starting and Started |
| health | 6116-6125 | `deploy-parent/health.txt` is `Healthy` |
| phone-home secret, server probe, 45s window | 6134-6178 | `phone-home-readable.txt` `true`, `phone-home-server-state.txt` `enabled`, `phone-home-failures.txt` `0` |
| git identity, checkout, mounts | 6183-6185 | `command.log` 11-13 is the checkout fetch (`8892b7b7..2e7e7e58`). `runner-mounts.txt` has `volume antiphon-runner_runner-tmp /tmp true` |

`deploy-parent/status.json` does not exist. That write is line 6227, after `c1008_verify_tmp`. The registration assert at 6231-6233 was not reached.

`c1008_refuse` (3831-3841) stores the reason on the journal and sets `outcome` to `partial` when any target volume is already `removed`. It does not change `phase`. `c1008_record_recreated` (5715), called after `up` at 6099, had set `phase=recreating` and `outcome=verificationPending`. The saved journal is therefore `phase=recreating`, `outcome=partial`, `diagnosis=RecycleTmpAssetsMissing`.

The state-init chown race was passed on this start. `command.log` line 3 is the seed's `state-init owned uid=1654` (`docker/stack/init-state.sh` 72-73 prints that only after `chown -R` returns). Compose then started the main runner, which `depends_on` state-init with `service_completed_successfully` (`docker-compose.server2-runner.yml` 158-160). Live inspect: `23c97aac84b7` `ExitCode=0`, started `2026-10-09T19:29:46.402Z`, finished `2026-10-09T19:29:46.790Z`. The same container's log still contains the 11:10 `chown: changing ownership of '/runner-state/grok/worktrees.db-wal': No such file or directory` line from the earlier exit 1, then `state-init owned uid=1654` from this start.

## 2. Current host state

Read at about 19:41Z, journal mtime `2026-10-09T19:30:53Z`, 208120 bytes. Same operation id. No work volume was mounted.

| Object | Now | Journal `.recreated` |
|---|---|---|
| `392f011e1f2b` `antiphon-runner-session-runner-1` | Up 11 minutes, healthy, `StartedAt` `2026-10-09T19:29:48.030Z`, image `antiphon-server2/session-testing:8892b7b759d9` | same id, `State.Running=true`, `Status=running` |
| `23c97aac84b7` `antiphon-runner-state-init-1` | Exited (0) at 19:29:46Z, image `antiphon-server2/server:8892b7b759d9` | same id, `Status=exited`, `Running=false` |
| `4cabb8afc676` `antiphon-runner-build-slots-1` | Up 9 days (healthy), `session-testing:a8b4e9e5a071` | not in the owned set |
| `927bb1ad3d60` `antiphon-runner-temp-session-runner-1` | Up 3 days (healthy), `session-testing:d985af05b5ac` | temp, untouched |
| `19026e1037d9` temp state-init | Exited (0) 3 days ago | untouched |
| `antiphon-runner_work`, `_runner-tmp`, `_dind-data` | `CreatedAt` `2026-10-09T11:09:57Z` | same timestamps |
| `antiphon-runner_runner-state` | `CreatedAt` `2026-09-22T19:10:50Z` | preserved |

Host journal `/home/mc/antiphon-server2/recycle/c10084fb9431d31da48edb781dd54e5ebebf7.json`: `phase=recreating`, `outcome=partial`, `diagnosis=RecycleTmpAssetsMissing`, `sourceSha=8892b7b759d96b3b5d6fa20518caf2c79c6be81a`. That matches the evidence `deploy-parent/recycle.json`.

`/tmp` inside the main container is the recreated volume. `findmnt` shows `/tmp` on `/var/lib/docker/volumes/antiphon-runner_runner-tmp/_data`. As uid 1654, `stat` is `mode=1777 uid=0 gid=0`. Names:

| Name | What it is |
|---|---|
| `/tmp/.dotnet` | directory, mode `777`, mtime `Oct 9 11:10`. Present eight hours before `StartedAt`. This is image `/tmp` copied in when `392f011e1f2b` was created at 11:10, not a write from the 19:29 process |
| `clr-debug-pipe-296-*`, `dotnet-diagnostic-296-*.socket` | uid `app`, mtime 19:29. Runtime pipes from the process that started then |
| `/tmp/antiphon-pty-hosts` | absent for uid 1654 and for root |

Temp's `/tmp` is also mode `1777`, and `/tmp/antiphon-pty-hosts` is present there (directory mode `755`, 448 regular files, including `logs/<sessionId>.log`). That tree is three days of session launches on the old temp volume. It is not a copy of the image into the new main volume.

`GET http://127.0.0.1:17202/api/session-runners/server2/status`:

| Field | server2 | server2-temp |
|---|---|---|
| `buildVersion` | `8892b7b759d96b3b5d6fa20518caf2c79c6be81a` | `d985af05b5ace2f13ec3f0d886c15992e9c13c3f` |
| `runnerStoreId` | `f519bd08-e53a-47d1-adb1-2ab33475446f` | `cd9807fa-754f-45ad-b6b7-d69100479fd0` |
| `available` / `dispatchEligible` | true / true | true / true |
| `draining` / `acceptingNewWork` | true / false | false / true |
| `redirectTo` | `server2-temp` | null |
| `sessions` / `runnerSessions` / `queuedTasks` | 0 / 0 / 0 | 5 / 5 / 0 |
| `retiredAt` | null | null |

Journal `liveZero.runnerStoreId` is the same `f519bd08-e53a-47d1-adb1-2ab33475446f` (`recycle.json` 393). `liveZero.buildVersion` is still the pre-removal value `4358939ecd85d6e7ff0941f970879499cb930e3d` (405). The live status `buildVersion` is the new SHA. The fleet list `GET /api/session-runners` agrees that server2 is available, dispatch-eligible, draining, occupied 0. Its `redirectTo` projection is null; the status body the script reads is the table above, and `redirectTo` there is still `server2-temp`.

The registration assert at 6231 would accept this status: `buildVersion` equals the operation SHA, `runnerStoreId` equals `liveZero.runnerStoreId`, `available`, `dispatchEligible`, `draining==true`, `acceptingNewWork==false`. It was not executed. `c1008_status_proof` for main (4408) still requires `redirectTo=="server2-temp"`, which holds.

## 3. Root cause

The recreated `runner-tmp` volume was initialized from the image's `/tmp` when container `392f011e1f2b` was created at 11:10. That copy does not contain `/tmp/antiphon-pty-hosts`. The check requires that directory and a file in it. Starting the same container at 19:29 does not copy again. No session has created the directory since.

Proof, in that order:

1. The volume `CreatedAt` is `2026-10-09T11:09:57Z`, from the first `HostComposeFailed` run. This resume did not create another volume. `392f011e1f2b` is that run's Created-and-never-started container, now started. Docker copies image content at the mount into an empty named volume once, at first container create. `/tmp/.dotnet` is dated 11:10. The process start is 19:29. The 11:10 tree is the copy-up.

2. Image `antiphon-server2/session-testing:8892b7b759d9` has no `/tmp/antiphon-pty-hosts`. `docker history --no-trunc` of that tag does not mention the path. At `8892b7b759d96b3b5d6fa20518caf2c79c6be81a`, `docker/session-runner-grok/Dockerfile` and `dind-entrypoint.sh` do not create it. The Dockerfile's `/tmp` directories (`/tmp/git-src`, `/tmp/grok-install`, `/tmp/claude-install`, `/tmp/jq-download`, the PowerShell tarball) are build scratch removed in the same `RUN`. `state-init` does not mount `runner-tmp` (`docker-compose.server2-runner.yml` 26-33). The session-runner mount is `runner-tmp:/tmp` with no `volume-nocopy` (110).

3. The runner creates the directory on first session launch, not at process start. `AdoptOrphanedHostsAsync` returns immediately when `PtyHostManifestDir` is absent (`SessionRunnerRuntime.cs` 1826-1831). `StartAsync` does `Directory.CreateDirectory(PtyHostLogDir)` at 2966-2967, under `/tmp/antiphon-pty-hosts/logs`, and the launcher then writes a log file. Main has `sessions=0` and is draining, so that path never ran. Temp has 448 files because it has been launching sessions for three days.

4. The check cannot see a file that was never copied and never created. Mode `1777` is why the refusal is `RecycleTmpAssetsMissing` and not `RecycleTmpModeInvalid`. `docs/docker-stack.md` 250 and 254 say image `/tmp`, including `/tmp/antiphon-pty-hosts`, copies in on first mount (CARD-0827). The image at this SHA does not contain that directory. CARD-1008 V-6 (`docs/superpowers/plans/2026-10-03-card-1008-rolling-volume-recycle-and-retire-temp-plan.md` 387) requires "image pty-host assets". The fixture enforces that with `tmpAssets=false` (`scripts/fixtures/c1008-fake-docker.sh` 146-147, `RemoteScriptContractTests.C1008_Recycle_preserves_tmp_copyup`). This is the first live recycle of a volume whose image has no such file.

The old main `/tmp` tree was deleted with the 2026-09-30 volume on purpose. It is not missing because copy-up was skipped or because the check ran before `up`.

## 4. Next step

The main runner is started and registered. It is not the accepting runner. `392f011e1f2b` is healthy on the new SHA, the preserved store id matches, phone-home failures in the deploy window were 0, and scheduling still redirects to temp (5 sessions, accepting). Phase bookkeeping is unfinished: `recreating` / `partial` / `RecycleTmpAssetsMissing`, drain not cleared. A first main session would create `/tmp/antiphon-pty-hosts` itself. Nothing will launch one while drain and `redirectTo=server2-temp` stand.

### (a) Another `-ResumeRecycle` now would not pass

`c1008_recycle` returns at 5910-5923 when `phase` is `recreating`, the volumes are `removed`, and the live non-broker containers equal `.recreated` on id, image, mounts, and host config. State is not part of that compare. The same ids, images, and volume timestamps are still that set, and `outcome=partial` is not consulted. Counters are 0, so `c1008_zero` does not refuse `RunnerBusy`. Store id matches, so 4414-4415 does not refuse.

`case_deploy_parent` then continues past that return: it builds, runs `seed_runner_checkout` (another `chown -R` of the live Grok store), and `compose up` again. If those succeed, line 6186 runs the same probe against a directory that is still absent and refuses `RecycleTmpAssetsMissing` again. Waiting will not create the directory. Do not run this command yet:

```
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 8892b7b759d96b3b5d6fa20518caf2c79c6be81a -Phase redeploy-old -ResumeRecycle c10084fb9431d31da48edb781dd54e5ebebf7
```

### (b) Script change, then that same command. This is the step to take

`c590-real.ps1` copies `scripts/c590-remote.sh` from the tree that launches the deploy. The journal `sourceSha` stays `8892b7b759d96b3b5d6fa20518caf2c79c6be81a`. An image change cannot satisfy this journal: the running image is already that SHA, and it has no pty-host tree to copy.

Minimal change in `c1008_verify_tmp`: keep the `1777` refusal. Treat a missing `/tmp/antiphon-pty-hosts` as success. Keep `RecycleTmpAssetsMissing` when the path exists and is not a directory, or is a directory with no regular file uid 1654 can see. Absence is what this image's copy-up produces. A present empty or unreadable tree is still a broken mount.

```
docker exec -u 1654:1654 "$container" sh -c '
    if [ ! -e /tmp/antiphon-pty-hosts ]; then exit 0; fi
    test -d /tmp/antiphon-pty-hosts && test -n "$(find /tmp/antiphon-pty-hosts -type f -print -quit)"
' >/dev/null 2>&1 || c1008_refuse RecycleTmpAssetsMissing
```

Test: `scripts/fixtures/c1008-fake-docker.sh` 146-147 exits 1 for every `sh` exec when `tmpAssets===false`, so `C1008_Recycle_preserves_tmp_copyup` still covers the refusal without an edit. Add a fixture result where the asset probe exits 0 because the directory is absent, and assert that `c1008_verify_tmp` does not refuse. Keep the `tmpMode=0755` case on `RecycleTmpModeInvalid`.

After that script is what the deploy tree scp's, run the command in (a). The chown race in `init-state.sh` 72 can still make that `up` return `HostComposeFailed`. If it does, stop. Do not open a second journal.

A later image can `mkdir` `/tmp/antiphon-pty-hosts` and drop a 1654-owned file so the original probe matches CARD-1008 V-6. That is a new SHA. It does not finish operation `c10084fb9431d31da48edb781dd54e5ebebf7`.

### (c) Hand-creating the directory is a host write. Do not do it

`docker exec -u 1654:1654 antiphon-runner-session-runner-1 mkdir` plus a placeholder file would make the current probe succeed and would let (a) pass the asset check if `up` itself succeeds. It writes the live runner volume, it is not a rolling phase, and the next recycle of this image fails the same way. Human-only if anyone insists. Not the repair.

Not a recovery: editing the journal, `docker start`/`rm`, or a hand `compose up`. Temp stays up. Main stays drained to temp until the scripted phase reaches `verified` and the later rolling phase clears the drain.
