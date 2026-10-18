# 2026-10-18 server2 preserve recovery — stopped at RecycleResumeRequired

Deploy task `41d7a523` on `feat/card-task-41d7a523`. Manual recovery of standing server2 after operation `c100808382374392e4ce1ac7dde48ca3b195f` (`phase=containersRemoved`, `outcome=refused`, `RecycleLandInFlight`). Diagnosis `e7a6d63987bc72ddfc775fefed157b32e5525040` on `feat/card-task-69c07e08`. Target `-Sha` `09e48c5d6f1d404cfea8ec0d992270424fd2a184`. No `-ResumeRecycle`. No volume was deleted.

Canonical checkout stayed detached at `69978ec1a246cd7a860f88a364eafc53a416aff7`. AppHost and the desktop runner were not restarted. Prior stop record: `15982940e9f4e51da74649516896aff2edef06ba` on `feat/card-task-77abf8f0`.

Smoke, the Plan canary, `verify-card0849-caches.ps1 -Case Both`, `drain-temp`, and `retire-temp` were not run. `acceptingNewWork` is still false.

## Census

`check-census` from `C:\src\Antiphon`. Project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`.

| UTC | Receipt | server2 boundOpen | landPending both | server2-temp |
|---|---|---|---|---|
| 2026-10-17T21:03:48Z | `c7274ec9e0eca0d5` | 0 | 0 | accepting `09e48c5d`, build matches |
| 2026-10-17T21:06:36Z | `c727a9d3facd97dd` | 0 | 0 | accepting `09e48c5d` (immediately before compose) |
| 2026-10-17T21:22:18Z | `c727d2eab3d70d34` | 0 | 0 | during state-init |
| 2026-10-17T21:53:59Z | `c727692a143d16de` | 0 | 0 | during state-init |
| 2026-10-17T22:55:24Z | `c72766b99d7b4884` | 0 | 0 | during state-init |
| 2026-10-17T23:56:56Z | `c727618b2ba6ed10` | 0 | 0 | during state-init |
| 2026-10-18T00:35:30Z | `c72778175a41489f` | 0 | 0 | accepting `09e48c5d` (immediately before the seal) |

server2-temp `boundOpen` moved 5 → 3 → 2 while it kept accepting the target SHA. It was not drained.

## Compose up

2026-10-17T21:06:52Z on server2, as `mc`. Backup `stack.env.c100808382374392e4ce1ac7dde48ca3b195f`. Only `SOURCE_REVISION=09e48c5d6f1d404cfea8ec0d992270424fd2a184` and `SOURCE_SHA12=09e48c5d6f1d` changed. `BUILD_SLOTS_SHA12` stayed `a8b4e9e5a071`. Then `docker compose -p antiphon-runner -f /work/repos/antiphon/docker-compose.server2-runner.yml --project-directory /work/repos/antiphon up -d --no-build`. Host checkout was already `09e48c5d`.

`antiphon-runner-state-init-1` ran `init-state.sh` `own_tree /work` from 2026-10-17T21:06:54Z and exited 0 at 2026-10-18T00:32:45Z. `antiphon-runner-session-runner-1` (`a6457bc89508`) then started. Image `antiphon-server2/session-testing:09e48c5d6f1d` id `sha256:a9be72ebb98bddae896fd93649897cdc7d8c59449a36629e4dd2100fd956b50c`. The wrapper's trailing `docker compose ps` hit a local CR in the remote script (`unknown shorthand flag: '\r'`) after `COMPOSE_EXIT=0`. Compose itself succeeded.

Confirmed on the new session-runner:

- mounts: `antiphon-runner_work` `/work`, `antiphon-runner_runner-state` `/state`, `antiphon-runner_dind-data` `/var/lib/docker`, `antiphon-runner_runner-tmp` `/tmp`
- `/tmp` mode `1777`
- uid 1654 read one byte of `PhoneHome__SecretPath` `/run/antiphon/phone-home` (`phone-home-readable=true`); the byte was not printed
- `curl http://127.0.0.1:8080/health` returned `Healthy` and `docker info` exited 0 by 2026-10-18T00:34:45Z; container health `healthy`
- build-slots container left as it was

`GET /api/session-runners/server2/status` at 2026-10-18T00:34:45Z:

- `buildVersion=09e48c5d6f1d404cfea8ec0d992270424fd2a184`
- `runnerStoreId=f519bd08-e53a-47d1-adb1-2ab33475446f`
- `processBootId=0e14a3d4-ba81-4ae9-a956-515188b82233` (new boot; store unchanged)
- `available=true` `dispatchEligible=true` `draining=true` `redirectTo=server2-temp` `acceptingNewWork=false`
- `sessions=0` `runnerSessions=0` `queuedTasks=0`

## Seal

2026-10-18T00:35:45Z. Backup `/home/mc/antiphon-server2/recycle/c100808382374392e4ce1ac7dde48ca3b195f.json.before-preserve-seal` (still `containersRemoved` / `refused`). Sealed file:

- `phase=completed` `outcome=completed` `diagnosis=preserve-seal volumes kept`
- outcomes `kept` for `antiphon-runner_work`, `antiphon-runner_runner-tmp`, `antiphon-runner_dind-data`

Those three volumes and `antiphon-runner_runner-state` still exist. Copy of the sealed file: `C:\src\Antiphon\.antiphon\rolling-server2\c727de92cd220c2a\recycle-preserve-seal.json`.

## redeploy-old

2026-10-18T00:37:40Z–00:37:42Z from `C:\src\Antiphon`, no `-ResumeRecycle`. Exit 2.

Wrapper output:

```
System.Threading.Tasks.VoidTaskResult
Rolling deploy stopped: RecycleResumeRequired
```

Receipt `C:\src\Antiphon\.antiphon\rolling-server2\c727de92cd220c2a`. Host jq check passed (`jq-1.7.1`, digest `5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5`, `outcome=existing`, `sshExit=0`). `census-redeploy-old-server2.json` is the empty receipt created before the throw; the phase never reached its task census, `deploy-parent`, cache verify, or drain clear.

`Assert-NoIncompleteRecycle` prints every journal with `sourceSha` equal to the rollout SHA, `project=antiphon-runner`, and `phase!="completed"`. After the seal, these five remain:

| operationId | phase | outcome |
|---|---|---|
| `c1008001b165462094cb4a27f484ffe6fc850` | preflight | refused |
| `c10084fa7d62ec9ba4845bf071445f0d516b1` | preflight | refused |
| `c1008bd48f43e1fa54a19ba070d3ea30986b9` | preflight | refused |
| `c1008ce5bf1ed6d4b4ed7918d807e8489c390` | preflight | refused |
| `c1008ffe0deb1d99946b6940cf84f675909ad` | preflight | refused |

The sealed operation is no longer in that set. None of these five files were edited.

## State at stop

Standing server2 is up on `09e48c5d`, store `f519bd08-e53a-47d1-adb1-2ab33475446f`, healthy, drained onto server2-temp, `acceptingNewWork=false`, counters 0. server2-temp still accepts `09e48c5d`. Volumes named above are present. Drain was not cleared.

Another `redeploy-old` without a change to those five journals throws `RecycleResumeRequired` again. `-ResumeRecycle` on `c100808382374392e4ce1ac7dde48ca3b195f` is the wrong command: `phase=completed` with outcomes `kept` refuses, and a resume that still sees `pending` deletes the three volumes.
