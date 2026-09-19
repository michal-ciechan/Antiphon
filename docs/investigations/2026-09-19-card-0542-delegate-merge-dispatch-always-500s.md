# CARD-0542: delegate.ps1 -Role Merge dispatch always 500s

Status: Investigation. Root cause confirmed from the stored 2026-09-15 server log (all five trace IDs survived), the Postgres container log, Docker Desktop's host logs, the Windows event log, the `AgentTasks` table and the code at `a3ef9c9e`. Not a Merge-path defect and not reproduced live (see Uncertainties for why a live run adds nothing). Investigate task: `8734a6c1`. Card: CARD-0542 (Antiphon board).

## Verdict

The premise is wrong: nothing in the server treats `role=Merge` differently before the point where these requests died. All five 500s are the same failure, `Npgsql` timing out while opening a *new physical connection* to Postgres inside `AgentTaskService.AuthenticateAsync`, the very first database call in the request and one that runs before the request body's role is ever read. They failed because the whole Docker engine was down from 22:56:42 to 22:59:04 local on 2026-09-15: a Microsoft Store auto-update of the Windows Subsystem for Linux (2.7.13.0 → 2.7.14.0) tore down the WSL virtual machine that hosts Docker Desktop, and with it `antiphon-postgres`. The five Merge attempts (22:56:54 → 22:58:31) all fell inside that 2 min 22 s window; every other database-backed call in the process failed the same way in the same window (health check, `GET /api/boards`, every hosted-service sweep, 269 connection errors in three minutes). The card was filed 15 s after Postgres came back, and the next dispatch 30 s after that (Code role) succeeded, which produced the "Merge fails, other roles work" reading. Merge-role tasks were created over the same `POST /api/agent-tasks` route with 201 twice earlier that day and four more times within nine hours afterwards. There have been zero 500s on that route in the four days since.

| Card claim | Finding |
|---|---|
| "fails every time ... regardless of -Kind or -Goal" | All five attempts were made in a 97-second span during a 142-second Postgres outage. No request of any role was attempted in that window that could have succeeded. |
| "All other roles used this session dispatch normally" | True, but those dispatches were before 22:56 or after 22:59. The first `POST /api/agent-tasks` after recovery (22:59:55, role Code) got 201. |
| "Needs server-side log investigation using the traceIds" | Done. Each trace ends in `AgentTaskService.AuthenticateAsync` → `RelationalConnection.OpenAsync` → `NpgsqlConnector.RawOpen` → `TimeoutException: Timeout during reading attempt`, 15.0 s after the request arrived. |
| "something Merge-specific that could NPE or throw" | None on this path. The only role-specific code (routing lists, decision policy, stage mapping, the internal `CreateMergeTaskAsync`) sits after authentication and is non-throwing membership data; see Mechanism §3. |

## Timeline (local, +01:00; server log, Windows event log and card times are local, Docker and Postgres times converted from UTC)

| When | Event | Source |
|---|---|---|
| 06:44:45, 09:19:11 | Merge-role tasks `6a793bdf` ("Merge mdpkg 1.0.0 to master") and `9da0ae32` created via `POST /api/agent-tasks`, 201, in `C:\src\markdown-package`. These are the card's "used successfully for CARD-0053/0054 earlier this session". | `AgentTasks`; `antiphon-20260915.log` "Delegated task 9da0ae32 ("Worker"/"Merge" ...)" with `RequestPath /api/agent-tasks` |
| 22:55:04 | A Testcontainers `postgres:16-alpine` (`ecstatic_wiles`, session `7733f1d3`) started: an `Antiphon.Tests` run was using the same Docker engine. Context only; it is a victim below, not a cause. | `docker inspect ecstatic_wiles` |
| 22:56:06 | Last line `antiphon-postgres` wrote before the gap: "checkpoint complete". | `docker logs antiphon-postgres` |
| 22:56:27 | Last `GET /health` that executed normally (1 ms). | log line 20276-20278 |
| **22:56:42** | Docker Desktop lost its engine: `error receiving build history event ... rpc error: code = Unavailable desc = error reading from server: EOF`; `[ENGINE] Disconnected[36..40] from /events`; `[INDEX] failed listen to events ... other side closed`. | `%LOCALAPPDATA%\Docker\log\host\com.docker.build.log`, `electron-2026-09-15.log` |
| **22:56:44** | Hyper-V VmSwitch: NIC `0741BA35-...` "successfully disconnected from port" (event 234) and "operation 'Delete' succeeded" (event 233). The WSL VM's network adapter was removed, i.e. the VM was torn down. | Windows System log |
| 22:56:47 | `MsiInstaller` 1039 warnings begin for "Product: Windows Subsystem for Linux": the WSL MSI is running. | Windows Application log |
| 22:56:54.794 | `POST /api/agent-tasks` **0HNOHLVJD174I** arrives (first Merge attempt). `Executing endpoint` logged at the same millisecond. | log 20971-20972 |
| 22:56:58.919 | First three `Microsoft.EntityFrameworkCore.Database.Connection.ConnectionError` entries in the server, from background sweeps whose connection attempts began ~22:56:44. The outage predates the Merge request by ~10 s. | log 20973-20975 |
| 22:57:01 | "Product: Windows Subsystem for Linux -- Installation completed successfully", version **2.7.14.0**, MSI at `C:\Program Files\WindowsApps\MicrosoftCorporationII.WindowsSubsystemForLinux_2.7.14.0_x64__8wekyb3d8bbwe\wsl.msi`; SCM 7045 "A service was installed". | Application 11707, 1033, 1042; System 7045 |
| 22:57:09.824 | **174I** fails after 15.03 s: `ConnectionError`, then `QueryIterationFailed`, then `ExceptionMiddleware` "Unhandled exception. TraceId: 0HNOHLVJD174I:00000001" → HTTP 500 "An unexpected error occurred." | log 22252-22310 |
| 22:57:16.627 → 22:57:31.642 | **174J**: identical, 15.02 s. | log 22842, 23764, 23807 |
| 22:57:47.525 → 22:58:02.582 | **174K**: identical, 15.06 s. | log 24693, 25686, 25729 |
| 22:58:09.716 → 22:58:24.731 | **174M**: identical, 15.02 s. | log 26036, 26905, 26948 |
| 22:58:10, :30, :50 | `watchdog-apphost`: "health slow, client up - not counting as down (health=FAIL ... HttpClient.Timeout of 5 seconds)" ×4. The CARD-0310 rule correctly kept the watchdog from restarting the AppHost. | `C:\src\Antiphon\logs\watchdog-apphost.log` |
| 22:58:31.748 → 22:58:46.773 | **174O**: identical, 15.03 s. | log 27500, 28302, 28345 |
| 22:58:41 | Docker Desktop opened its error dialog (`[WINDOW] Open app://dd/error-dialog`). | `electron-errd-2026-09-15.log` lines 1-13 |
| 22:58:53 | Hyper-V VmSwitch: new port and NIC created and connected on switch "WSL" for VM `3524AF06-...`; "Networking driver in Virtual Machine is loaded". The WSL VM is back. | System 264, 233, 232, 102 |
| 22:59:00.738 | Server health check: `postgresql` **Unhealthy** "completed after 15001.99 ms with message 'Exception while reading from stream'". | log 29556 |
| 22:59:04 | Docker Desktop reconnected: `[ENGINE] Connecting[42..46] to /events`. | `electron-2026-09-15.log` |
| 22:59:07.79 | `ecstatic_wiles` (the test Postgres) recorded as exited 255: found dead when the engine came back. | `docker inspect` |
| 22:59:09.46 → 09.57 | **All five long-lived containers started within 110 ms of each other**: `antiphon-redpanda` 09.464, `desktop-links-1` 09.484, `desktop-caddy-1` 09.489, `antiphon-postgres` 09.504, `windmill-desktop-worker` 09.573. `RestartCount=0` on every one: an engine-level restart, not a container crash loop. | `docker inspect` `.State.StartedAt` |
| 22:59:09.889 | `GET /api/boards` 0HNOHLVJD174Q also 500s with the same exception: the failure is not route-specific. | log 30844 |
| 22:59:11.7 → 12.4 | Postgres: 27 × "FATAL: the database system is starting up", then "**database system was not properly shut down; automatic recovery in progress**", redo `4/24DA1AF8` → `4/24FC03D8`. The previous instance was killed, not stopped. | `docker logs antiphon-postgres` |
| 22:59:12.513 | Postgres "database system is ready to accept connections". Last server `ConnectionError` at 22:59:12.517. | `docker logs`; log |
| 22:59:25.525 | `POST /api/boards/8988ca03-.../cards` → this card, `createdAt 2026-09-15T21:59:27.017Z`. | log 40029; `card.ps1 get 542` |
| 22:59:55.921 | `POST /api/agent-tasks` 0HNOHLVJD174U → **201**: "Delegated task 991c9c6b ("Worker"/"**Code**", "Frontier", "Codex") ... `Land username localStorage feature (Merg...`". The same merge work re-dispatched as Code, 43 s after recovery. | log 40037-40041 |
| 09-16 01:13:12, 01:24:54, 01:35:58, 07:49:18 | Merge-role tasks `73a2660b`, `84e0ea68`, `1864fbe1`, `54feec98` ("CARD-0481/0462: fresh landing identity ...") created via `POST /api/agent-tasks`, all 201, Worker/Merge, Codex Low/Medium, in `C:\src\Antiphon`. | `antiphon-20260916.log` "Delegated task ..." lines carrying `RequestPath /api/agent-tasks` |
| 09-16 → 09-19 | `Unhandled exception` entries with `RequestPath /api/agent-tasks`: **0** across all four daily logs. | `grep` over `antiphon-2026091[6-9].log` |

`AgentTasks` today: 49 rows with `Role = 10` (Merge), first 2026-08-17, last 2026-09-17 08:28:54Z.

## Mechanism

### 1. Where each request died, and why role cannot matter there

`POST /api/agent-tasks` (`server/Api/Endpoints/AgentTaskEndpoints.cs:19-29`) does two things: `ResolveCallerAsync` (line 26) then `service.CreateAsync` (line 27). `ResolveCallerAsync` (`:328-336`) reads the delegation token header and, when present, calls `AgentTaskService.AuthenticateAsync` (`server/Application/Services/AgentTaskService.cs:136`). That method hashes the token and runs `_db.AgentTasks.FirstOrDefaultAsync(t => t.TokenHash == hash, ct)` (`:142`). The stored stack traces for all five requests end exactly there (the 2026-09-15 build reported it as `AgentTaskService.cs:line 131` / `AgentTaskEndpoints.cs:line 254` and `:line 25`; the lines have since moved, the methods have not):

```
System.InvalidOperationException: An exception has been raised that is likely due to a transient failure.
 ---> Npgsql.NpgsqlException (0x80004005): Exception while reading from stream
 ---> System.TimeoutException: Timeout during reading attempt
   at Npgsql.Internal.NpgsqlReadBuffer.<Ensure>g__EnsureLong|55_0(...)
   at Npgsql.Internal.NpgsqlConnector.RawOpen(SslMode sslMode, NpgsqlTimeout timeout, ...)
   at Npgsql.Internal.NpgsqlConnector.<Open>g__OpenCore|214_1(...)
   at Npgsql.PoolingDataSource.OpenNewConnector(NpgsqlConnection conn, ...)
   at Npgsql.PoolingDataSource.<Get>g__RentAsync|33_0(...)
   at Npgsql.NpgsqlConnection.<Open>g__OpenAsync|42_0(...)
   at Microsoft.EntityFrameworkCore.Storage.RelationalConnection.OpenAsync(...)
   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(...)
   at Microsoft.EntityFrameworkCore.Query.Internal.SingleQueryingEnumerable`1.AsyncEnumerator.InitializeReaderAsync(...)
   ...
   at Antiphon.Server.Application.Services.AgentTaskService.AuthenticateAsync(String token, CancellationToken ct) in ...\AgentTaskService.cs:line 131
   at Antiphon.Server.Api.Endpoints.AgentTaskEndpoints.ResolveCallerAsync(HttpContext http, AgentTaskService service, CancellationToken ct) in ...\AgentTaskEndpoints.cs:line 254
   at Antiphon.Server.Api.Endpoints.AgentTaskEndpoints.<>c.<<MapAgentTaskEndpoints>b__1_0>d.MoveNext() in ...\AgentTaskEndpoints.cs:line 25
```

(`antiphon-20260915.log` lines 22295-22310 for 174I; the other four are byte-identical apart from the trace ID, verified by grepping each `Unhandled exception` block for the `AuthenticateAsync` frame: 1 hit each.)

Three properties of this frame settle the "Merge-specific" question:

- `AuthenticateAsync` takes only the token. `request.Role`, `Kind`, `Goal`, `Card` have not been read yet; `CreateAsync` is never entered.
- The failure is inside `PoolingDataSource.OpenNewConnector` → `RawOpen`: the pool had no idle connector and the TCP-level open of a fresh one timed out. That is the state of the process, not of the request.
- The elapsed time is 15.0 s in all five cases, which is Npgsql's default connection `Timeout` (15 s). A query-level or application-level fault would not produce five identical 15.0 s intervals.

The same exception, at the same second, hit code that shares nothing with this endpoint: `AgentTaskDispatcher.SettleDeferredReportsAsync` (log 22345, `AgentTaskDispatcher.cs:2287`), `DiagnoseSweepHostedService`, `SessionReconciliationHostedService`, `GrokRulesRecoveryHostedService`, `ScheduleSweepHostedService`, `AgentTaskLandSweepHostedService`, `DataRetentionHostedService`, `RunAttemptStallHostedService`, `WatchdogHostedService`, `CompletionNoteWorkHostedService`, `SessionHealthHostedService`, `SpecialistRequestHostedService`, `AgentSessionRuntime` "Failed to record activity", the `postgresql` health check, and `GET /api/boards` (log 29397-31439, 30844). Per-minute count of `Database.Connection.ConnectionError` on 2026-09-15: 22:56 → 14, 22:57 → 58, 22:58 → 62, 22:59 → 135, then none until the next day.

### 2. Why Postgres was unreachable: the Docker engine restarted under a WSL Store update

The chain, each link from a different log:

1. **WSL update**: Windows Application log records the WSL MSI transaction starting at 22:56:47 and "Windows Installer installed the product. Product Name: Windows Subsystem for Linux. Product Version: 2.7.14.0" at 22:57:01. `wsl --version` today reports 2.7.14.0. The previous install of this product, 2.7.13.0, was 2026-09-09 17:43:59.
2. **WSL VM torn down**: Windows System log, Hyper-V VmSwitch, 22:56:44: the VM's NIC disconnected and deleted. Recreated 22:58:53. Over the last 30 days the only other "Delete" events for a VmSwitch NIC are 2026-08-21 01:06 and a cluster on 2026-09-09 17:34-17:43, the latter coinciding with the 2.7.13.0 install. The WSL package updater restarts the WSL VM.
3. **Docker Desktop lost its engine** at 22:56:42 ("error reading from server: EOF", `Disconnected from /events`), showed its error dialog at 22:58:41, reconnected at 22:59:04. Docker Desktop on this machine runs its engine inside that WSL VM (`docker version` server 29.5.3; `electron` logs name the engine `desktop-linux`).
4. **Every container restarted together** at 22:59:09 with `RestartCount=0`, and Postgres's own log says the previous instance "was not properly shut down": it was killed with the VM, and WAL redo ran on restart.
5. **Nothing else was going on**: the server process itself never restarted (the daily log is continuous, 22:50 → 23:59), the watchdog did not fire, and the Testcontainers Postgres that was alive at 22:55 (some test run) died the same way (`ExitCode 255`) rather than being a load source; Postgres's last checkpoints before the gap were routine (21:45, 21:50, 21:55 UTC, 30-43 s each).

### 3. Why the client saw an opaque 500, and why the timeout was a *read* timeout

`ExceptionMiddleware` (`server/Api/Middleware/ExceptionMiddleware.cs:43-61`) maps anything it does not recognise to `(StatusCodes.Status500InternalServerError, "An unexpected error occurred.")` (`:97`) with the trace ID; EF's `InvalidOperationException` "transient failure" wrapper is not recognised, so a dead database reads to the caller exactly like a null reference. That is why the card could not tell the two apart from the client side.

The Npgsql error is "Timeout during reading attempt" rather than "connection refused" because `localhost:17280` stayed *listening* while the VM was gone: Docker Desktop's host-side port proxy accepts the TCP connection and forwards it into a VM that is not there, so the client connects, sends the startup packet, and waits the full 15 s for a reply that never comes. The same shape appears in the watchdog log as `HttpClient.Timeout of 5 seconds elapsing` on `/health` (whose Postgres check was itself waiting 15 s). This explains the "slow", not "down", presentation of the outage.

### 4. The Merge-specific code that exists, and why it is not implicated

For completeness, every place `AgentTaskRole.Merge` is named on or near the create path:

- `server/Domain/Enums/AgentTaskEnums.cs:25` — `Merge = 10`.
- `scripts/delegate.ps1:23` — `Merge` is in the `-Role` `ValidateSet`; the script sends it as an ordinary role string, no extra calls.
- `server/Application/Services/ComplexityRoutingService.cs:66-77` — Merge is a member of the routable-roles array. Membership data; nothing throws.
- `server/Application/Services/InternalDecisionPolicy.cs:44-52` — Merge is in an `or` pattern of roles that take a default policy. Same.
- `server/Domain/Enums/OrchestrationStage.cs:37` — `AgentTaskRole.Merge => OrchestrationStage.Rebase`, the documented default (`AgentTaskDtos.cs:153`).
- `server/Application/Services/AgentTaskService.cs:2504-2563` — `CreateMergeTaskAsync`, the *system* path that spawns a Merge delegate on a rebase conflict. Internal; not reachable from `POST /api/agent-tasks`. (Tasks `2c956bc5` and `36d30f6e` on 09-17, "Resolve merge conflict: ...", came from here and have no HTTP request context in the log.)
- `AgentTaskService.cs:231`, `:1026-1030`, `:1493-1500` — `MergeTargetRef` handling, keyed on the *field*, not the role; the only throw is a `ValidationException` (400) for an explicit target that fails validation, which the card's trivial no-`-Card` repro never sets.

All of these run after `AuthenticateAsync`. None was reached.

## Reproduction

Not re-run live. The five original traces are intact and each carries the full stack; the mechanism is reconstructed from four independent stores (server log, Postgres log, Docker Desktop logs, Windows event log) whose timestamps interlock to the second; and the same route has since created Merge-role tasks with 201 four times (09-16 01:13, 01:24, 01:35, 07:49) and produced zero 500s in four days of traffic. Re-running the card's exact command would spawn a real ClaudeCode/High session for a diagnostic goal and could only reproduce the failure by coinciding with another Docker outage.

To reproduce the *class* of failure on demand without spending a dispatch: stop the engine (`wsl --shutdown`, or Docker Desktop → Restart) and issue any authenticated `POST /api/agent-tasks`, or simply `GET /api/boards`; both return the same 500 with `TimeoutException: Timeout during reading attempt` after ~15 s until the containers are back.

## Uncertainties

- **Why the WSL update restarted the VM rather than deferring** is not visible from here; the Store's updater and `wsl.msi` do not log their decision. The correlation is exact (VM NIC deleted 3 s before the MSI's first registry write, recreated 8 s before Docker reconnected) and repeated on 2026-09-09, so it is treated as the cause.
- **The port-proxy explanation for "read timeout instead of refused"** is inferred from the stack (`RawOpen` → read) plus Docker Desktop's architecture, not observed by packet capture. It changes nothing about the verdict; it explains only the 15 s shape.
- **The exact `-Kind ClaudeCode -Level High` combination** in the card has not been re-issued for Merge since 09-15. Post-outage Merge creations used Codex Low/Medium. Kind and level are validated in `CreateAsync`, which is shared by all roles and which the 22:59:55 Codex/Frontier Code dispatch and every later dispatch passed through; nothing in it branches on Merge.
- **Other `ConnectionError` episodes** in the retained logs (09-15 08:46 ×3; 09-16 02:28 ×2; 09-18 05:08, 20:43, 22:13; 09-19 03:36, 09:34; 2-8 entries each, mostly `TimeoutException: The operation has timed out`, i.e. command timeouts under load) are a different, sub-second-to-seconds phenomenon with no container restart (all containers have been up since 09-15 22:59). Not investigated further; none touched `POST /api/agent-tasks`.

## Not done, noted

- Fix idea, separate card: map `NpgsqlException`/EF transient-failure exceptions to **503** with `Retry-After` in `ExceptionMiddleware` (and let `delegate.ps1` retry a 503 once after ~20 s), so a database outage is distinguishable from a code fault at the client; plus a `docs/bootstrap.md` gotcha that a WSL Store auto-update restarts the Docker engine for ~2.5 min and every DB-backed request 500s for that window.
