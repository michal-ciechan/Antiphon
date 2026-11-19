# CARD-1744 G1-G3 runbook amendment (S5a)

This file is the runbook the G1-G3 plan reserved
(`docs/superpowers/plans/2026-11-18-card-1744-blockers-g1-g3-plan.md`, slice S5).
It is a separate file. It does not edit
[the parent cutover plan](2026-11-08-card-1744-cutover-plan.md).
The investigation `docs/investigations/2026-11-18-server-to-server2-docker-plan.md`
at `ff06bf9354d84725f8c3f3d99a9cf900d8930210` (branch `feat/card-task-b56e40f0`,
not on master) names a later front-door docs sweep as its slice 9. That sweep
is after the window. This amendment is not that sweep.

Checked against `origin/master` `a789c87770d31a77687f75a619c5af646d1ce91f`.
The task branch base `751f3a0bba4000234a6aa88508228e8583838824` has the same
bytes for every file quoted below. S4 is absent at that tip: there is no
`scripts/lib/windows-phone-home-env.ps1`, and no script reads `phone-home.json`.

Adopted operator decisions for this window (2026-11-19): O-10 (phase 1 through
the autostart path), O-11 (AppHost launch refusal while `phone-home.json`
exists), O-13 (capacity 1, then 2), O-14 (unblock CARD-1744 from CARD-1778).
**O-12, the production rehearsal, is operator-only.** A delegate does not run
it. Nothing in this file is a deploy, a restart, or a database connection.

The G1-G3 rollout step that switches with a plain `restart-session-runner.ps1`
and writes `capacity 2` is superseded here. The switch and the rollback both
use `-Hard`. Phase 2 starts at capacity 1.

## Code this amendment was checked against

| Fact | Where |
|---|---|
| The logon script has no parameters. It always launches `--urls http://localhost:17204` and writes `logs\session-runner.log` under its own repo root. | `scripts/autostart-session-runner.ps1:20-27`, `:46-55` |
| The logon task runs that script from the checkout that registered it, task name `Antiphon Session Runner`, `RestartCount 3`, interactive. | `scripts/install-autostart.ps1:83`, `:191-194`, `:211` |
| `run-daemon.ps1` starts the service with `UseShellExecute = $false` (`:166`) and appends stdout to `-LogFile` (`:174-176`). The child therefore keeps the supervisor process environment. A soft restart does not re-read a config file. | `scripts/run-daemon.ps1:166`, `:174-176` |
| `-Hard` stops the supervisor and starts the Scheduled Task again. Without `-Hard`, and while a supervisor pid is recorded, the restart only stops the service. `-KillSessions` is a different switch. | `scripts/session-runner-restart-health.ps1:226-233`, `:120-122`; `scripts/restart-session-runner.ps1:23` |
| AppHost adopts whatever is already listening on 17204 and treats it as the direct `session-runner`. | `Antiphon.AppHost/Program.cs:33-39`; `Antiphon.AppHost/Supervisor/DaemonProcessService.cs:34-37` |
| `restart-apphost.ps1` exit 3 today is the worktree guard, `-WhatIf` without `-StopOnly`, an in-flight lock, or SHA admission. It does not read `phone-home.json`. `-StopOnly` can still run. Port 17204 is excluded from the ports it stops. | `scripts/restart-apphost.ps1:94-97`, `:112-115`, `:125-128` |
| `dev-aspire.ps1` exit 3 today is the worktree guard, before it writes `logs\apphost.launch.lock`. It does not read `phone-home.json`. | `dev-aspire.ps1:22-39` |
| The foreground Windows starter refuses runner ids `local` and `desktop`, reads the secret from `<Root>\secrets\phone-home`, and sets `Serilog__LogPath` to `<Root>/state/logs`. Default `-Root` is `C:\Antiphon\runner`, default `-Port` is 17214. It runs the exe in the foreground. | `scripts/start-windows-phone-home-runner.ps1:29-39`, `:51-56`, `:75-97`, `:109-110` |
| The Windows build-slot default is `http://localhost:17204/build-slots`. A set `ANTIPHON_BUILD_SLOTS_URL` wins. Whether the desktop has that variable set is UNOBSERVED. | `scripts/lib/build-slot.ps1:27-29` |
| Worktree retention is constructed only when phone-home is enabled and the process is Linux. | `src/Antiphon.SessionRunner/Program.cs:82-100` |
| Serilog still writes the console, and the file sink is `<Serilog:LogPath>/session-runner-.log` (default `%TEMP%\antiphon-logs`). | `src/Antiphon.SessionRunner/Program.cs:19-40` |
| The production compose ceiling for `desktop-win` is `MaxCapacity` 2. Deploy `-WhatIf -EnvFile` refuses a blank `PHONE_HOME_DESKTOP_WIN_SHARED_SECRET` or `PHONE_HOME_DESKTOP_WIN_HOME` with exit 2 `env-missing`. | `docker-compose.server2-server.yml:70-84`; `scripts/deploy-server2-server.ps1:30`, `:344-348` |
| List query keys are `rootId`, `since`, `status`, `includeChecks`, `landPending`, `projectId`, `boardId`, `unscoped`. Any other key is HTTP 400 `unknown_query_parameter`. `status` is a comma-separated list. Each item carries `RequiredPlatform` and `RunnerId`. | `server/Application/Services/AgentTaskScope.cs:61-71`; `server/Api/Endpoints/AgentTaskEndpoints.cs:643-672`; `server/Application/Dtos/AgentTaskDtos.cs:350-351` |

## Who does what

### Delegates

Dispatched by the orchestrator. Grok High authors. Opus reviews.

- S4 Code and S4 Review (the helper, the autostart reader, and the launch guard). Not this file.
- S5a, this docs slice, and its Review.
- The rehearsal record (RH-5), written afterwards from the operator's notes.
- A Debug task for a red row.

A delegate does not connect to the production Postgres, deploy, restart, or start or stop anything on the desktop. O-12 is not a delegate task.

### Orchestrator

- Dispatch, monitor, and land S4 and S5a.
- The server2 staged rollout under the existing operational autonomy. Stop on `RecycleUnpublishedWork`, `RecycleWorktreeDirty`, or `RecycleGitAuditUnknown reason=registry-missing`.
- Desktop restarts that the standing rules already allow stay outside the rehearsal and the window. `restart-apphost.ps1 -ExpectedServerSha <tip>` is the pre-window phase 1 activation, after the operator's user-secrets edit: pull the main checkout, check `logs\apphost.restart.lock` and `logs\apphost.launch.lock` first, then restart from `C:\src\Antiphon`. That AppHost restart is not allowed mid-window, and not while the Linux server is the writer. An exit 3 `phone-home-config-present` in that window means stop.
- The phase 2 file and the `-Hard` refresh belong only to the operator-run rehearsal or the window: `restart-session-runner.ps1 -Hard` from `C:\src\Antiphon`. Read the desktop build-slot list before any 17204 swap.
- Phase 1 witnesses: two pinned Windows Debug tasks, one land, the Full-run comparison, and the service-pid relaunch check.
- Board edits, including O-14 on CARD-1778 and the CARD-1744 card text. This docs task does not edit cards.
- Before the rehearsal and the window: keep-six holds, the parent step 1.1 land drain, and the O-9 read below.

### Operator only

- The `desktop-win` secret file, the desktop user-secrets entry and its removal, the host env-file keys, the operator-token copy, and the AppHost user-secret remove and re-add.
- Clone `C:\Antiphon\runner\app` and start phase 1 from a normal terminal.
- Every host `-Execute`: staging, production deploy, and `docker compose stop`.
- Certificate generation, host clones, the ingress test hostname, and the vhost switch.
- `pg_stat_activity` on the host. The maintenance marker and the Scheduled Task enable or disable.
- The go or no-go for O-12 and for the window.
- `-KillSessions` stays human-only.

**O-12 is this rehearsal (RH-0 through RB).** It is a real R-2 exercise on the production database, about 30 to 45 minutes of control-plane downtime, between Full runs, at a quiet hour. It is not part of the S5a docs task.

## Phase 1 replaces window step 0.9

Parent step 0.9 only writes `C:\Antiphon\runner\secrets\phone-home` when `desktop-win` is wanted that day. O-10 replaces that step with phase 1, after the rc that contains S4 is activated, between Full runs.

1. Clone `C:\Antiphon\runner\app` at the activated rc tip. Operator.
2. Write the shared secret to `C:\Antiphon\runner\secrets\phone-home`, owner-only. The file is the one the starter already requires (`start-windows-phone-home-runner.ps1:51-56`). Do not put the secret in JSON. Operator.
3. Add the desktop `antiphon-server` user-secrets entry `PhoneHomeRunner:Runners:desktop-win:*` with the compose entry's shape (`docker-compose.server2-server.yml:70-84`), except `MaxCapacity=1` and `HostWorkspaceRoot=C:\src\Antiphon`. Operator. The AppHost restart that loads the entry is the activation batch, not a delegate.
4. Write `C:\Antiphon\runner\phone-home.phase1.json`, ASCII JSON, keys only: `runnerId` `desktop-win`, `serverOrigin` the public origin, `capacity` `1`, `port` `17214`. No secret key. Adopted schema. The reader that accepts this file is S4 and is not on this master.
5. From a normal terminal, not an agent shell:

   `pwsh -NoProfile -File C:\Antiphon\runner\app\scripts\autostart-session-runner.ps1 -PhoneHomeConfigPath C:\Antiphon\runner\phone-home.phase1.json`

   That `-PhoneHomeConfigPath` parameter is the adopted S4 signature. On this master the script has no parameters (`autostart-session-runner.ps1:18-55`).

The phase 1 file is not the phase 2 path. The AppHost keeps running beside 17214. AppHost adoption is port 17204 only (`Antiphon.AppHost/Program.cs:37`).

**Fallback if S4 is not on that rc.** Do not hold the rc. Start phase 1 with the script that exists today, from the clone, from a normal terminal:

`pwsh -NoProfile -File C:\Antiphon\runner\app\scripts\start-windows-phone-home-runner.ps1 -RunnerId desktop-win -Port 17214 -Capacity 1`

That path has no supervisor relaunch (`start-windows-phone-home-runner.ps1:109-110`). A reboot needs a manual start. S4 then rides the following rc, which is activated before the rehearsal.

**Witnesses (orchestrator, UNOBSERVED until run).**

- `GET /api/session-runners`: `desktop-win` is windows, available, eligible, capacity 1.
- Two Windows Debug tasks with `-Runner desktop-win -Platform Windows` complete, and one lands.
- One Windows Full run finishes beside phase 1. Record desktop CPU and wall time against the previous run.
- `GET /api/session-runners/desktop-win/status` stays available across one relaunch: stop only the pid in `C:\Antiphon\runner\app\logs\session-runner.service.pid`.

**Phase 1 rollback.**

1. Wait until `desktop-win` `occupied` is 0.
2. `Set-Content -LiteralPath C:\Antiphon\runner\app\logs\session-runner.state -Value stopped` when the adopted autostart path is the one running. The supervisor exits when it reads `stopped` (`autostart-session-runner.ps1:16`, `:41`).
3. `taskkill /T /F /PID <clone service pid>`.
4. Leave the user-secrets entry. Unconnected, the runner is only unavailable. Removing the entry waits for the next AppHost restart.
5. The fallback starter is a foreground process: Ctrl+C that terminal instead of steps 2 and 3.

## Phase 2 replaces window step 3.4

Parent step 3.4 starts `start-windows-phone-home-runner.ps1 -RunnerId desktop-win` in the foreground. O-10 and O-13 replace that with the logon task on 17204, capacity 1. Do this only inside the operator-run rehearsal (RH-3) or the window, between Full runs, and only when S4 is on the activated tip.

1. `GET http://localhost:17204/build-slots` shows no `rc-qualification` lease. `scripts/qualification/launch-rc.ps1:60-65` defaults `buildSlotLabel` to `rc-qualification` and runs `nightly-run.ps1` inside one `build-slot.ps1` lease. A 17204 swap drops in-memory leases. Whether a qualification record overrides `buildSlotLabel` is UNOBSERVED.
2. Stop phase 1 with the phase 1 rollback.
3. Write `C:\Antiphon\runner\phone-home.json`: `runnerId` `desktop-win`, `serverOrigin` the public origin (the test hostname in RH-3), `capacity` `1`, no `port` key. No secret key. The secret stays in `C:\Antiphon\runner\secrets\phone-home`.
4. From `C:\src\Antiphon`:

   `pwsh -NoProfile -File scripts/restart-session-runner.ps1 -Hard`

**Why `-Hard`.** The phone-home variables live in the supervisor process. `run-daemon.ps1:166` sets `UseShellExecute = $false`, so the service inherits that process environment, and a later soft restart only stops the service (`session-runner-restart-health.ps1:229-233`). `-Hard` stops the supervisor and starts `Antiphon Session Runner` again (`:231-233`, `:122`), which re-runs `autostart-session-runner.ps1`. Exit 0 means `http://127.0.0.1:17204/health` returned 200 for the main checkout's executable (`:255-263`). On this master the autostart script does not read the JSON, so this step changes mode only after S4 is on the tip the task launches.

Checks, UNOBSERVED until run:

- `GET /api/session-runners/desktop-win/status` is available and eligible.
- `pwsh -NoProfile -File scripts/build-slot.ps1 -Label probe -- pwsh -NoProfile -Command "exit 0"` prints a granted `BUILD SLOT` line. The broker address is the default in `build-slot.ps1:29`.
- `pwsh -NoProfile -File scripts/restart-apphost.ps1` exits 3 with `REFUSED: phone-home-config-present` and stops nothing. That text is the adopted O-11 guard. It is not in `scripts/restart-apphost.ps1` on this master. Until S4 is activated, do not start the AppHost while `phone-home.json` exists: `DaemonProcessService` would adopt the 17204 listener as the direct runner (`DaemonProcessService.cs:34-37`).

After one Full run has finished beside phase 2, the operator may raise `capacity` from 1 to 2 in the JSON and run `-Hard` again. Compose `MaxCapacity` 2 is the ceiling (`docker-compose.server2-server.yml:79`).

## Phase 2 rollback in front of R-2 and R-3

Run this before the parent R-2 AppHost restart, and before the parent R-3 when phase 2 was started. The G1-G3 line that uses a plain `restart-session-runner.ps1` is the line this section replaces.

1. Delete `C:\Antiphon\runner\phone-home.json`. Leave `phone-home.phase1.json` if phase 1 should return.
2. `pwsh -NoProfile -File scripts/restart-session-runner.ps1 -Hard` from `C:\src\Antiphon`.
3. `GET http://127.0.0.1:17204/health` is 200. After the `-Hard` relaunch timestamp, `C:\src\Antiphon\logs\session-runner.log` has no `Phone-home connection` line and no `Phone-home registration` line. A process still in phone-home mode that connects writes `Phone-home connection epoch={Epoch} connected (capacity {Capacity})` (`src/Antiphon.SessionRunner/PhoneHomeConnectionService.cs:184`). The only `Phone-home registration` text on this master is the failure warning `Phone-home registration failed: reason={Reason} attempt={Attempt} backoffMs={Backoff}` (`:99` and `:105`). The same service also writes `Phone-home connection ended: epoch={Epoch} loop={Loop} fault={Fault} overflow={Overflow} pending={PendingEvents} pendingBytes={PendingBytes} inFlight={InFlight} lifetimeMs={LifetimeMs} close={Close}` (`:262`), and that text plus ` wsError={WsError} socketError={SocketError}` when the socket faults (`:270`). Any of those after the relaunch means 17204 did not leave phone-home mode. Also require `GET /api/session-runners/desktop-win/status` on the Linux origin (the public origin, or `http://127.0.0.1:17400` on the host) to return `"available": false`. That field is `PhoneHomeRunnerStatusDto.Available` (`src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs:704`), true only while a live socket is open and the lease is unexpired (`PhoneHomeRunnerDirectory.Status`, `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs:739`). `dispatchEligible` (`PhoneHomeRunnerStatusDto.DispatchEligible`, `:705`) is false in the same object (`:761`). The catalogue `GET /api/session-runners` is not this witness: a disconnected `desktop-win` row is `available` false with `unavailableReason` `stale`, because `DescribeAsync` sets `Stale` to the inverse of eligibility (`:862-868`) and `SessionRunnerCatalogue.Row` copies that (`server/Application/Services/SessionRunnerCatalogue.cs:137-147`).
4. Only then run the parent rollback. Step 4 waits on both step-3 witnesses.

Parent R-2 (`2026-11-08-card-1744-cutover-plan.md`, section 3.7), after step 4: on the host, `docker compose -p antiphon-server2-server stop`; confirm `pg_stat_activity` has no production-compose client; restore `Project.LocalRepositoryPath` from the before column; then R-1.

Parent R-1: re-add the AppHost user-secret `ConnectionStrings:DefaultConnection`; `restart-apphost.ps1 -ExpectedServerSha <tip>` from `C:\src\Antiphon`; `set-apphost-maintenance.ps1 -Clear`; `Enable-ScheduledTask -TaskName 'Antiphon AppHost'`; clear keep-six holds.

Parent R-3, after the phase 2 rollback when phase 2 ran: switch the vhost back to the desktop first; if the bridge was enabled, set it false and recreate; `docker compose -p antiphon-server2-server stop`; confirm `pg_stat_activity` has no production-compose client; restore project paths and agent working directories; then R-1. The parent sentence "stop `desktop-win` if started" is this phase 2 rollback when the logon task is the runner, or Ctrl+C when the fallback starter is the runner. The `pg_stat_activity` confirmation is the same check parent R-2 and rehearsal RB already require between that stop and R-1.

If `restart-apphost.ps1` then exits 3 with `phone-home-config-present`, the phase 2 file is still present. Delete it, run `-Hard`, and retry only after `docker compose -p antiphon-server2-server stop` and `pg_stat_activity` shows no production-compose client (parent R-2, parent R-3, rehearsal RB). While the Linux server is the writer, including mid-window, this refusal means stop: leave the file in place and do not retry. That exit text arrives with S4.

## O-9 pre-window drain

Before the rehearsal and before the window, list Queued and Blocked tasks and keep every Windows task that has no runner. The read is:

`GET /api/agent-tasks?projectId=<id>&status=Queued,Blocked`

`status` and `projectId` are supported (`AgentTaskScope.cs:61-71`). `requiredPlatform` and `runnerId` are not query keys; sending either is HTTP 400 `unknown_query_parameter` (`AgentTaskEndpoints.cs:653`). Filter the items: `RequiredPlatform` Windows and `RunnerId` null (`AgentTaskDtos.cs:350-351`). An omitted `projectId` is the whole fleet (`docs/ops-http.md`).

Dispatch each kept task before the window, or cancel it and re-create it afterwards. That choice is orchestrator board work after the operator agrees. The read itself changes nothing.

## Rehearsal RH-0 to RH-5 (O-12, operator only)

This is the first exercise of the production compose. Staging has no session runner and no phone-home (`docs/docker-stack.md`, staging section: `SessionRunner` off, no phone-home origin). Staging stays precondition P-3. The rehearsal writes the Linux server's startup rows into the production database. One writer at a time.

Preconditions, from the parent plan, confirmed by the operator:

- P-1 and P-2. P-2 is 24 hours after the Postgres monitor exits 0. That exit is UNOBSERVED from this checkout (T-22).
- P-3, P-5 to P-10, P-12, P-13 (recorded only), P-14.
- S4 phase 1 green.
- RH-3 also needs P-11, a test hostname that reaches `127.0.0.1:17400` with a WebSocket upgrade.
- No `rc-qualification` lease on the desktop broker.
- `GET /api/session-runners` shows server2 `occupied` small. Keep-six holds are set (parent 0.11).
- No scheduled card action is due in the next hour. The operator reads the schedule list.
- The cutover tip is the activated rc tip, and `C:\src\Antiphon` is at that tip (parent 0.2).

| Step | Where | Action | Pass | STOP |
|---|---|---|---|---|
| RH-0 | Host, operator | `deploy-server2-server.ps1 -WhatIf -EnvFile <prod env>` | Exit 0, plan line, no docker. The desktop-win keys are present (`deploy-server2-server.ps1:344-348`). | Any refusal: fix the env file. No downtime yet. |
| RH-1 | Desktop, operator | Parent 1.1 to 1.7. **Start the downtime clock.** | As the parent plan. | Parent STOP rules. Rollback is R-1, with no phase 2 prefix unless phase 2 was started. |
| RH-2 | Host, operator | Parent 2.1, 2.2, and 2.4. Skip 2.3 when the host token file is already the desktop token (D-18, operator copy, no display). Skip 2.5 and 2.6. | `127.0.0.1:17400/health` is 200 within 2 minutes. Version equals the tip and advertises `land-v2`. The gate exits 0. The sidecar is listed linux and available. `desktop-win` and `server2` are listed. Unavailable is enough. | Any STOP: RB. |
| RH-2b | Desktop through the host tunnel, operator | `PUT /api/api-keys/card-1744-rehearsal-probe` with a non-secret dummy value, then `GET /api/api-keys`, then `DELETE /api/api-keys/{id}`. | PUT 2xx, the name is listed, DELETE 2xx. | Protector not ready: RB, and fix P-10 before any window. Do not touch the real key or any project path. |
| RH-3 | Desktop, optional, operator, needs P-11 | Phase 2 with `serverOrigin` set to the test hostname, then the launch-refusal check. | `desktop-win` is available on `127.0.0.1:17400/api/session-runners`. A granted `BUILD SLOT` line. `restart-apphost.ps1` exits 3 `phone-home-config-present` once S4 is on the tip. | Registration refused: record the runner log reason, run the phase 2 rollback, continue to RB. |
| RH-4 | Host, operator | `docker logs antiphon-server2-server-server-1 --since 10m`, and the Hangfire dashboard through the tunnel. | No repeated error. The recurring set matches parent phase 4. | Record only. |
| RB | Host, then desktop, operator | Phase 2 rollback if RH-3 ran. Then `docker compose -p antiphon-server2-server stop`. Then `pg_stat_activity` shows no production-compose client. Then R-1. **Stop the clock** when `/api/version` equals the tip on the public origin and `server2` is eligible again. | Desktop serving. `server2` reconnected. Always-on agents Running. `desktop-win` back on phase 1 if wanted. | AppHost exit 3 `phone-home-config-present`: delete the phase 2 file, `-Hard`, retry. |
| RH-5 | Orchestrator | A Docs delegate writes the record from the operator's notes: every step time, the downtime, the R-2 time against the 15-minute central, and every STOP. | Record committed. | n/a |

The rehearsal does not prove the vhost switch, a task or land on the Linux server, agent re-homing, or a child callback during RH-3. `CallbackOrigin` and `Delegation__ApiBaseUrl` stay the public origin, which still points at the stopped desktop. The window proves those.

During RH-1 through RB, sessions that wait for input on server2 keep their seats. Parking is off (`BlockedTaskParking:Enabled` false, CARD-1083), so nothing releases them on a timer. The server2 runner is disconnected and reconnects to the same origin after RB. How far its event buffer reaches across 30 to 45 minutes is UNOBSERVED.

## Timing

| Clock | Plan central | Measured |
|---|---|---|
| Rehearsal downtime, RH-1 start to RB done | 30 to 45 minutes | UNOBSERVED. This checkout has no rehearsal log. |
| Parent window downtime | 45 to 60 minutes (`docs/docker-stack.md`, recorded-numbers paragraph) | UNOBSERVED. |
| R-2 restore | About 15 minutes: vhost 1 to 5 minutes, Linux stop 30 seconds, AppHost restart 3 to 5 minutes, runner reconnect under 5 minutes (parent plan, section 3.7) | UNOBSERVED. RH-5 records the number. |

## Log paths

| Runner | Supervisor stdout | Serilog |
|---|---|---|
| Phase 1, adopted autostart from the clone | `C:\Antiphon\runner\app\logs\session-runner.log` (`autostart-session-runner.ps1:20-27` with the clone as repo root; `run-daemon.ps1:174-176`) | `C:\Antiphon\runner\state\logs\session-runner-.log` once the phone-home env sets `Serilog__LogPath` (`start-windows-phone-home-runner.ps1:94`; `Program.cs:28-35`) |
| Phase 1 fallback starter | The terminal that launched it. There is no supervisor log (`start-windows-phone-home-runner.ps1:109-110`). | Same `C:\Antiphon\runner\state\logs\session-runner-.log` (`:94`) |
| Phase 2 | `C:\src\Antiphon\logs\session-runner.log` when the logon task was registered from that checkout (`install-autostart.ps1:191-194`; `logs.md` session-runner row) | `C:\Antiphon\runner\state\logs\session-runner-.log` after S4 sets the env. Until then the supervised direct runner stays on `%TEMP%\antiphon-logs\session-runner-*.log` (`Program.cs:28-29`) |

`scripts/logs.ps1 -Source session-runner` reads the canonical checkout's supervisor file and the `%TEMP%` Serilog files. It does not read `C:\Antiphon\runner\state\logs`. Retrieval of the phone-home Serilog file is `Get-Content` of that directory on the desktop. Live tails of either phase are UNOBSERVED.

A Windows phone-home runner does not construct `RunnerWorkspaceRetention` (`Program.cs:89`). `C:\Antiphon\runner\work\worktrees` grows until someone clears it. The growth rate is UNOBSERVED.

## DockerStackDocumentationTests count

`tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs` has 20 `[Test]` methods and no `[Arguments]` on master `a789c87770d31a77687f75a619c5af646d1ce91f`. S5a adds no test. The G1-G3 checkpoint CP-9 Min of 19, counted at `997f9b8d9`, is stale and is corrected in that plan. The S5a row is CP-14, Min 20.

## Unobserved

- Every server2 host state: env file, certificate, clones, ingress, and whether `antiphon-server2-server` or staging has been started.
- Desktop user-secrets, the operator token, and whether the logon task's PowerShell is pwsh 7 or 5.1.
- The Postgres window execute and the CARD-1408 monitor exit.
- A phone-home runner on 17204 under `restart-session-runner.ps1 -Hard`: milestones and exit 0.
- The phone-home broker on Windows granting a lease.
- Worktree tasks and land on a Windows phone-home runner.
- The same-host hairpin, and the test-hostname route.
- Whether `buildSlotLabel` is overridden in the qualification records. The lease check assumes the default `rc-qualification`.
- The server2 runner's event buffer across a 30 to 45 minute disconnect.
- Whether `ANTIPHON_BUILD_SLOTS_URL` is set on the desktop.
