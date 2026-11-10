# Docker engine outage during rc run 6591e378

Read-only incident note. Nothing on the desktop stack was restarted, stopped, or written outside this worktree. Observed from about 16:58Z through 17:10Z on 2026-11-10. The qualification was still running (supervisor pid 329676, driver pid 287488).

## Outcome

Windows Update installed Windows Subsystem for Linux 3.0.5.0 and, at 16:47:01Z, disabled the WSL Service. That tore down the docker-desktop WSL2 VM. Docker Desktop's own processes stayed up, so the engine pipe returned HTTP 500 until the orchestrator killed and relaunched Docker Desktop at 16:54:14Z–16:54:20Z. Certainty: high. The service-control event, the Hyper-V NIC delete, and the Docker backend pipe failure are the same second.

The qualification lane then recorded four false chunks. `antiphon-all-group-115` through `118` failed entirely in `TestDbFixture`'s BeforeAssembly hook because Testcontainers could not ping `npipe://./pipe/docker_engine`. Those four TRX files are the outage victims (`docs/investigations/2026-11-10-docker-outage-victim-chunks.txt`). They started after the VM was already gone, so they did not kill Docker.

Do not stop run 6591e378. One chunk has finished since 16:55Z and it passed (`antiphon-all-group-124`, 8/8). At 17:10:07Z the supervisor heartbeat was `trx=96`, `hangSuspected=false`, idle 5.6 minutes against a 20-minute budget, and `antiphon-all-group-130` was still in phase `run` with no TRX yet.

## Cause

Certainty: high.

| Time (UTC) | Evidence |
|---|---|
| 16:36:49 | Windows Update started downloading an update (System log, WindowsUpdateClient 44, twice). |
| 16:37:33–16:37:51 | Defender security-intelligence update KB2267602 (Version 1.463.227.0) installed successfully. This finished about nine minutes before the VM died. |
| 16:41:33 | Windows Update started installing Windows Subsystem for Linux 3.0.5.0 (x64). |
| 16:42:42 | Last line retained from the docker-desktop VM: wsl-bootstrap removed a testcontainer port forward to 172.17.0.3:5432. That matches `antiphon-all-group-100` finishing its Testcontainers Postgres at 16:42:44. |
| 16:47:01.431 | `com.docker.backend.exe.log.20261110-165239.528`: `wsl.exe -d ubuntu` docker-desktop-user-distro proxy failed, exit status 1, then "The pipe is being closed", gvisor vpnkit EOF. |
| 16:47:01 | Service Control Manager event 7040: start type of the WSL Service changed from auto start to disabled. |
| 16:47:02.584 | `com.docker.backend.exe.log.20261110-165239.530`: `wsl.exe -d docker-desktop ... wsl-bootstrap run ... failed: exit status 1`. The stderr is the VM's bootstrap log, and it ends at the 16:42:42 port removal. |
| 16:47:03 | Hyper-V-VmSwitch events 234 and 233: the VM NIC was disconnected and deleted. Backend log: connection to `/run/guest-services/docker.proxy.sock` refused. |
| 16:47:22 | Service Control Manager 7045: WSL Service reinstalled (`C:\Program Files\WSL\wslservice.exe`, auto start, LocalSystem). |
| 16:47:24 | Windows Update event 19: installation of WSL 3.0.5.0 succeeded. |
| 16:47:22–16:54:14 | The engine stayed down. Docker Desktop processes were still alive. There is no Application Error for `com.docker.backend`, `vmmem`, or `wslservice`. |
| 16:54:14.321 | `Docker Desktop.exe.log`: backend `exit status 0xffffffff` (process killed). |
| 16:54:20.543 | Same log: Docker Desktop relaunched the backend. This matches the orchestrator's restart. This note did not read that session. |
| 16:54:25 | Hyper-V-VmSwitch recreated the WSL NIC and loaded the VM networking driver. |

Not the cause, checked and absent in 16:30Z–17:05Z:

- No Kernel-Power sleep or resume.
- No Resource-Exhaustion-Detector event, and no Application Error for the Docker or WSL processes.
- No reboot. The WSL package was replaced in place.
- `.wslconfig` already caps the utility VM at `memory=8GB` and `swap=8GB`, with `autoMemoryReclaim=dropcache` and `sparseVhd=true`. Docker Desktop `settings-store.json` has `MemoryMiB=2048`, `SwapMiB=1024`, `Cpus=8`. Neither cap fired. An OOM would not disable the WSL Service or emit Windows Update event 19.
- `wsl --status` (read only; `wsl --shutdown` was not run): default distribution Ubuntu, version 2.

The new engine did not retain `docker events` from before 16:54Z. The retained VM record is the wsl-bootstrap stderr dumped into the 16:47:02 backend line.

A Windows Error Reporting event at 16:49:49 is `RADAR_PRE_LEAK_64` for `Antiphon.SessionRunner.exe`. That is a leak radar during the outage, not a process crash, and it is after the WSL service was already disabled.

## The tests did not kill Docker

Certainty that groups 115–118 did not kill the engine: high. Their TRX `Times.start` values are 16:50:09Z, 16:50:31Z, 16:50:54Z, and 16:51:15Z, all after the 16:47:01Z service disable.

The chunk in flight at 16:47:01Z was `antiphon-all-group-109` (16:42:44Z–16:47:56Z). It is 30 `Antiphon.Tests.Checkpoints.*` classes, 281 passed, 3 skipped, 0 failed. `antiphon-all-group-110` then ran 16:47:57Z–16:50:08Z, also only checkpoint classes, and passed 123/123 while the engine was already dead. A search of `tests/Antiphon.Tests/Checkpoints` finds no `wsl.exe`, `Stop-Process`, `docker system`, or `taskkill`. A search of the test tree for `docker system prune`, `wsl --shutdown`, `wsl --terminate`, and `Stop-Process`/`taskkill` against Docker names hits rollout-script fixtures and a test that forbids those prune commands. None of those classes were in groups 109, 110, or 115–118.

`HostCleanupWorktreeInventoryTests` is in group 100, which passed 146/146 and finished at 16:42:44Z, before the service was disabled. `DockerStackContractTests` and `Server2ProductionServerContractTests` were not in the chunks running at the death.

## Timeline, 16:30Z–17:00Z

The qualification is one antiphon-all lane. Supervisor log is a 10-minute progress line, not a per-chunk line. Chunk bounds are `*.timing.json` `startedAt`/`completedAt`, confirmed against TRX `Times`. Every chunk in this hour has `buildSlot=not-used` (the suite was already built; shared `buildSeconds` 655.862). Supervisor: 16:32:23 trx=83; 16:42:37 trx=87; 16:52:47 trx=95; 17:10:07 trx=96.

| Minute | Qualification | Everything else that showed up |
|---|---|---|
| 16:30 | group 90 running (started 16:28:21, 351 tests) | Watchdog fire. Task `8eda8c6c` Merge still in flight. |
| 16:31 | group 90 still running | Merge `8eda8c6c` succeeded 16:31:39. |
| 16:32 | group 90 still running | Supervisor trx=83. Watchdog fire. |
| 16:33 | group 90 still running | Review `448ebe7e` succeeded 16:33:13. Merge `9cf87914` dispatched. |
| 16:34 | group 90 still running | Code `0ddd898d` dispatched. Watchdog fire. |
| 16:35 | group 90 passed 16:35:22 (350 succeeded, 0 failed). Group 91 started, passed 16:37:08 (340/0). | |
| 16:36 | group 91 running | Windows Update began a download. Watchdog fire. |
| 16:37 | group 91 passed. Group 92 started 16:37:08 and passed 16:38:40 (310/0). | Defender KB2267602 installed 16:37:33–16:37:51. |
| 16:38 | group 82 started 16:38:40 | Watchdog fire. |
| 16:39 | group 82 running | |
| 16:40 | group 82 running | Watchdog fire. |
| 16:41 | group 82 passed 16:41:41 (338/0). Group 100 started. | WSL 3.0.5.0 installation started 16:41:33. |
| 16:42 | group 100 passed 16:42:44 (146/0). Group 109 (checkpoints) started 16:42:44. VM's last logged act is the 16:42:42 port-forward removal. | Debug `c7ed4119` succeeded 16:42:52. Supervisor trx=87. |
| 16:43 | group 109 running | |
| 16:44 | group 109 running | Postgres's last checkpoint later reported as 16:44:32. Review `b3dbf071` dispatched 16:44:14. Watchdog fire. |
| 16:45 | group 109 running | |
| 16:46 | group 109 running | Watchdog fire 16:46:25. No failure yet. |
| 16:47 | 16:47:01 WSL Service disabled and the engine pipe died, during group 109. Group 109 still passed 16:47:56 (281 passed, 3 skipped, 0 failed). Group 110 started 16:47:57. | WSL Service reinstalled 16:47:22. WSL 3.0.5.0 install succeeded 16:47:24. Engine stayed down. |
| 16:48 | group 110 running (checkpoint tests, no Docker) | Watchdog 16:48:28 health timeout, client=200. 16:48:34 and 16:48:54 "health slow, client up - not counting as down". Outage id `f55e288221914efda958b9845608aede`, `downSinceUtc` 16:48:28.4566910Z. |
| 16:49 | group 110 running | 16:49:13 probe round 3/3 health=503, client=200, still not counted as an AppHost-down restart. 16:49:49 WER leak radar for SessionRunner. |
| 16:50 | group 110 passed 16:50:08 (123/123). Groups 115 and 116 then failed on the dead pipe (178 and 244). | Watchdog probes at 16:50:29, 16:50:48, 16:51:08 all health=503, client=200. |
| 16:51 | groups 117 and 118 failed on the same hook (146 and 521). Group 120 started 16:51:43. | 16:51:08 "all 3 probe rounds failed". 16:51:55 "skip: Docker is not responsive (not counting against flap budget)". |
| 16:52 | group 120 finished 16:52:01: 88 passed, 1 real assertion failure. Group 124 started 16:52:02 and kept running. | Supervisor trx=95 at 16:52:47. Outage down event observed 16:52:27. Watchdog 503 again through 16:53:08, then another Docker-not-responsive skip at 16:53:55. |
| 16:53 | group 124 running (it does not take the Testcontainers assembly hook; it later passed) | Same watchdog skip. |
| 16:54 | group 124 still running | 16:54:14 backend killed, 16:54:20 relaunched, 16:54:25 VM NIC back. Postgres crash recovery 16:54:37–16:54:42, ready 16:54:42.766. Watchdog probe recovered health=200 client=200 at 16:54:46. Recovery event observed 16:54:46. Debug `002811c5` succeeded 16:54:44. |
| 16:55 | group 124 still running | Code `2779b08a` succeeded 16:55:52. |
| 16:56 | group 124 still running | This read task dispatched 16:56:20. Watchdog fires only, no 503. |
| 16:57 | group 124 still running | |
| 16:58 | group 124 passed 16:58:12 (8/8, duration 6m 10s). Group 130 entered phase `run` at 16:58:13. | `/health` was 200 when checked at 17:06Z. |
| 16:59–17:00 | group 130 still running, no TRX and no tests log yet | Watchdog fires only. Docker httpproxy was serving API traffic again. |

`logs/apphost-down.alert` was not on disk at read time. The watchdog clears it on recovery. The durable copy is `C:\src\Antiphon\logs\apphost-outage-events.jsonl`. Watchdog state at 17:06Z had `downSinceUtc` null and `lastOkUtc` still 16:20:40.3689394Z (the last explicit "OK health=200 client=200" line). Later fires after 16:56 logged no new failure. The watchdog did not restart AppHost. Client stayed 200 the whole time, so the server process stayed up.

Build slots: every chunk above recorded `buildSlot=not-used`. A live `GET http://localhost:17204/build-slots` around 17:07Z returned enabled, budget 2, occupied 0, no leases, no waiters, availableMb 8686, floorMb 6144. There was no historical lease ledger in that response.

`GET /api/agent-tasks?take=100` is rejected: 400 `unknown_query_parameter` (`take` is not supported; supported names include `since` and `status`). `since=2026-11-10T16:30:00Z` returned 13 tasks. None with `completedAt` inside 16:46Z–16:58Z has status Failed. The two completions inside that window are Succeeded (`002811c5` Debug at 16:54:44Z, `2779b08a` Code at 16:55:52Z). The only Failed task on `status=Failed&since=2026-11-10T16:00:00Z` is `0b6b3e44`, completed 16:05:07Z, before the outage. The backup-runner refresh named in the brief was not in the 16:30Z list, which matches "finished earlier".

## False-failure window

Every chunk whose execution overlaps 16:46:00Z–16:55:00Z. Times are TRX `Times.start` / `Times.finish`. File mtimes match the finish times within a second.

| Chunk file | Start | Finish | Result | Class |
|---|---|---|---|---|
| antiphon-all-group-109.trx | 16:42:44.498Z | 16:47:56.719Z | 284 total, 281 passed, 3 skipped, 0 failed | Not a victim. Passed. Overlaps the window only until 16:47:56, and the pass includes the second the service was disabled. |
| antiphon-all-group-110.trx | 16:47:57.903Z | 16:50:08.930Z | 123/123 passed | Not a victim. Ran entirely while the engine was down and did not touch it. |
| antiphon-all-group-115.trx | 16:50:09.922Z | 16:50:30.701Z | 178 failed, 0 passed | Outage victim. |
| antiphon-all-group-116.trx | 16:50:31.661Z | 16:50:53.615Z | 244 failed, 0 passed | Outage victim. |
| antiphon-all-group-117.trx | 16:50:54.615Z | 16:51:14.825Z | 146 failed, 0 passed | Outage victim. |
| antiphon-all-group-118.trx | 16:51:15.822Z | 16:51:42.221Z | 521 failed, 0 passed | Outage victim. |
| antiphon-all-group-120.trx | 16:51:43.451Z | 16:52:01.125Z | 88 passed, 1 failed | Real failure. Not a victim. See below. |
| antiphon-all-group-124.trx | 16:52:02.223Z | 16:58:12.270Z | 8/8 passed | Not a victim. Started during the outage, finished after recovery, passed. |

Groups 115–118: all 1,089 failures are the same BeforeAssembly error. Count of non-environment failures inside those four TRX files: 0. Each test's duration is about 0.1 ms and the start times in a chunk collapse to one instant, which is the hook failing before the test body.

First failing test of group 115, by earliest TRX `startTime`: `C448_V36_Authority_target_ref` at `2026-11-10T16:50:27.6258314+00:00`, duration `00:00:00.0000995`. The tests log's first `failed` line is `Repeated_reads_replay_gits_answer_without_starting_git` at `2026-11-10T16:50:27.6258472+00:00`. Same message, same instant (the hook stamps every test together):

> BeforeAssemblyException: BeforeAssembly hook failed: Docker is either not running or misconfigured. ... Failed to connect to Docker endpoint at 'npipe://./pipe/docker_engine'. ... Docker.DotNet.DockerApiException: Docker API responded with status code='InternalServerError', response=''.

The stack is `TestDbFixture.InitializeAsync` → `TestDbFixtureLifecycle.BootstrapAsync` → `PostgreSqlBuilder.Build` → Testcontainers ping of the named pipe. The same text is the first failure message in groups 116, 117, and 118.

Group 120's single failure is not that hook. `C1453_PostWindowSourceStatusCreatesNoIndexLock` started `2026-11-10T16:51:58.1698362+00:00`, duration 1.020s, in `LandingSourceOptionalLockTests`. Message: `ShouldAssertException: inProcess.StatusSpawns should be 0 but was 1` with additional info `a clean tree is answered in process`. The other 88 tests in the chunk passed while Docker was down, so this assembly does not open Testcontainers in BeforeAssembly. The test name is not in `prior-failures.json`. Classify it as a real failure for a Debug task. It happened to run during the outage; the failure text is an assertion, not a connection, Docker, Postgres, or git error.

## What survived

Postgres (`antiphon-postgres`) came back on the existing volume. It was not recreated. From `docker logs` (read only):

- 16:54:37.884 `database system was interrupted; last known up at 2026-11-10 16:44:32 UTC`
- 16:54:42.429 `database system was not properly shut down; automatic recovery in progress`
- 16:54:42.435 `redo starts at 1D/C2989A0`
- 16:54:42.517 `redo done at 1D/C85A5C8` (about 5.8 MB, 0.08 s)
- 16:54:42.755 end-of-recovery checkpoint, 975 buffers
- 16:54:42.766 `database system is ready to accept connections`

No migration line. The desktop server Serilog files stop at 14:07:11Z (`antiphon-20261110.log` is the 100 MB cap; `antiphon-20261110_001.log` is 0 bytes), so there is no server-log migration or recovery line at 16:55Z to quote. The server process did not restart: watchdog client=200 throughout, and a restart would have reopened the Serilog file. `/health` was 200 at 17:06Z.

At that same check, `antiphon-redpanda` and `windmill-desktop-worker` were Up, as were `desktop-caddy-1` and `desktop-links-1` (about 12 minutes, so they returned with the 16:54 relaunch). Redpanda's log is a fresh process start at 16:54:37. An extra container named `distracted_wescoff` was Up about 8 minutes and lines up with group 130 starting at 16:58; it was left untouched.

Failed tasks with `completedAt` in 16:46Z–16:58Z: 0 (see the query note above).

Sessions: `GET http://localhost:17202/api/agent-tasks` does not return a session `endedAt`. The runner's `GET http://localhost:17204/sessions` (79 sessions at 17:10Z: 14 Running, 65 Exited) also has no `endedAt`. One session's `startedAt` falls inside 16:46Z–16:58Z. No session end in that window could be counted from either payload.

## Recommendation

Continue run 6591e378. Do not run `scripts/qualification/stop-supervisor.ps1`.

Chunks completed after 16:55Z that look healthy: 1, `antiphon-all-group-124` (8 passed, 0 failed, finish 16:58:12Z). Group 130 was still in progress at 17:10Z with no result file. The false results are confined to the four victim TRX files. Rerun those four chunks after the engine is up (it was up at 17:06Z). File the group 120 assertion as its own Debug task; do not treat it as unproven-by-environment.

A repeat of this outage is a WSL servicing install during the run, not a test and not the VM memory cap.

- Defer the Windows Subsystem for Linux package update (and any update that disables WSL Service) until the qualification supervisor has exited. The install at 16:41:33 did not reboot the machine; it disabled the service at 16:47:01 and left Docker Desktop's backend alive against a missing VM.
- After WSL Service is back (here, 16:47:22), Docker Desktop did not bring the engine back by itself. The pipe stayed 500 until the 16:54:20 relaunch. A restart-on-engine-loss when `vmmem` is gone and the pipe returns 500 would have shortened the false window. The AppHost watchdog correctly refused to restart the server while Docker was unresponsive (`skip: Docker is not responsive`), and the client never went down, so that watchdog is the wrong actor for an engine-only death.
- A between-chunk `docker info` hold in the qualification driver would have kept groups 115–118 from being written as failures. The Testcontainers hook already fails fast, which is why each victim chunk ended in about 20 seconds; it does not distinguish "engine temporarily gone" from a product failure.
- Serialising Docker-touching tests away from the AppHost Postgres, or adding another WSL memory cap, would not have prevented this. `.wslconfig` already sets `memory=8GB`. Docker's `MemoryMiB=2048` is a tighter setting and was not what stopped the VM.

## Files

- This note: `docs/investigations/2026-11-10-docker-outage-rc-run-6591e378.md`
- Victim TRX names, one per line: `docs/investigations/2026-11-10-docker-outage-victim-chunks.txt`
