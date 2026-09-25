# CARD-0716: graceful AppHost restart and fast phone-home reconnect

Date: 2026-09-25. Stage: Plan, with the verification design folded into this dispatch (the brief
asks for red tests and the `### Checkpoints` table). Task: `5b76bd03`, written on the server2 Linux
runner. Source inspected: `07df1561e754d007cdb30fdf1a41198df727727b` (the task's StartRef);
`origin/master` was `66881b06` at the time and differs only in `docs/` (the CARD-0717 plan and two
CARD-0714 documents), so every source line below is also the master line.
Investigation: [2026-09-25-card-0716-phone-home-drops.md](../../investigations/2026-09-25-card-0716-phone-home-drops.md).
Neighbouring plans: [CARD-0679 launch loss](2026-09-24-card-0679-phone-home-launch-loss-plan.md)
(D-6 runner-scoped client, D-8 re-attach/re-launch loop, D-11 harness) and
[CARD-0717 Polly resilience](2026-09-25-card-0717-polly-resilience-plan.md) (on master; phone-home
registration is classified **N**, no standard handler). Next stage: **Code**, two bounded rounds.

No code, configuration, test or deployment changed during Plan. One read-only measurement was
taken: `scripts/test-apphost-server-version.ps1` was run here on server2 (pwsh 7.5.4) against the
unmodified scripts to learn whether the restart-script fixture has a Linux lane (it does; see
ground truth row 10).

## Outcome and scope

A planned desktop restart must end the runner's phone-home socket with a close frame instead of a
severed TCP connection, so the runner's log names a planned stop rather than a handshake fault;
the runner must notice a returned server within seconds, not after a 100 s `HttpClient` default;
a remote launch that the restart interrupts must be re-attached or failed with a reason that names
the restart; and the next in-process abort of the 15:53:48Z shape must be attributable to a hop
from the logs alone.

In scope: `scripts/restart-apphost.ps1` and `scripts/apphost-common.ps1` (a graceful stop step
before the force-kill, locks and exit codes unchanged); a new operator-authenticated shutdown route
and a shutdown coordinator on the desktop server; the phone-home connect route, live connection and
recovery directory (close frame on host stop, connection id and transport codes in the log lines);
the restart reconciler's interrupted-launch pass for phone-home owners; the runner's
`PhoneHomeConnectionService` and `PhoneHomeSettings` (registration and connect timeouts, backoff
cap, classified registration failures, close status and socket error in the ended line); a
logging-only Vite proxy trace; the owner docs.

Out of scope: any retry policy or Polly handler on registration (CARD-0717 R2 owns the permanent
failure classification and must find one seam, D-4); Aspire/DCP process termination on Windows;
the AppHost daemon supervisor's own `Kill`; Caddy configuration (outside this repository); the
overflow closes the investigation lists separately (CARD-0679 path); a live server2 canary (Review
may commission one; the rounds here are desktop, runner, script and client unit/integration tests).

Deployment notes the Code rounds must carry:

- S4 changes the **runner** binary and reaches server2 only through the runner image redeploy;
  the desktop rounds land independently and degrade gracefully against an older runner (a runner
  without S4 still reads the close frame as `fault=none` and reconnects as today).
- The script change is used by the **next** `restart-apphost.ps1` run from the main checkout after
  `git pull --rebase`. That first restart runs the new script against the old server: the shutdown
  route answers 404, the script says so and force-kills as today. Every later restart is graceful.
- No schema change and no migration. No new column: the interrupted-launch record is an
  `AgentTaskEvent` on the Dispatched task (D-5), the shape CARD-0679 D-8 already writes.

## Ground truth

| Brief / card assumption | What the inspected code does | Design consequence |
|---|---|---|
| The restart could stop the server gracefully before killing it. | `scripts/restart-apphost.ps1:180` calls `Stop-AppHostProcessTree` (`taskkill /T /F`, `apphost-common.ps1:493-495`), then `Stop-Process -Force` on every port owner. The server is an Aspire `AddProject` resource under the AppHost's DCP (`Antiphon.AppHost/Program.cs:60-76`), not a daemon: `dev-aspire.ps1`'s synopsis ("Server + Client: true daemons") is stale. The AppHost control API (`ControlApiService.cs`, port 17207) knows only the daemons (`session-runner`, `fake-gateway`) and its stop is `Process.Kill(entireProcessTree: true)` (`DaemonProcessService.cs:232-236`). | A graceful stop needs a server-side trigger the script can call (D-2) and a script step that calls it and waits, bounded, before the existing kill (D-3). |
| A graceful host stop would send the close frame. | The connect route awaits `connection.ReceiveLoopAsync(ct, logger)` with `ct` = `RequestAborted` (`SessionRunnerEndpoints.cs:120-127`). Nothing in `server/` registers on `ApplicationStopping` for phone-home; `AbortReason` only reads it (`:181-182`). Kestrel cancels `RequestAborted` on an upgraded connection only when it aborts it after `HostOptions.ShutdownTimeout` (default 30 s; nothing sets it). | Even `StopApplication()` today ends in an abort with no close frame, 30 s later. D-1 links the receive loop to `ApplicationStopping` so the route ends at once, classifies `request_aborted` (the name ops-http already documents) and closes with 1001. |
| `DisposeAsync` already closes the socket properly. | It sends `CloseAsync(NormalClosure, "dispose", CancellationToken.None)` when the socket is Open (`PhoneHomeLiveConnection.cs:510-511`): unbounded, constant description. The runner returns null from `ReadFrameAsync` on a Close frame (`PhoneHomeFraming.cs:38-40`) and logs `fault=none`, discarding the status. | D-1 maps `request_aborted` to close status 1001 `server_stopping` and bounds the handshake; D-4 logs `close=<status>:<description>` on the runner. |
| The runner's registration has a timeout. | `AddHttpClient(nameof(PhoneHomeConnectionService))` (`src/Antiphon.SessionRunner/Program.cs:45`) sets none; `RunConnectionAsync` sets only `BaseAddress` (`PhoneHomeConnectionService.cs:83-84`); `ws.ConnectAsync(connect, ct)` (`:108`) is bounded only by the stopping token; backoff 1000 ms doubling to 15000 ms (`PhoneHomeSettings.cs:82-83`); the outer loop logs one Warning with the exception and no classification (`:68-73`). | D-4: `RegistrationTimeoutSeconds` and `ConnectTimeoutSeconds` (default 8), cap 5000 ms, one classified Warning line. |
| The restart reconciler resumes an interrupted remote launch. | `SessionReconciliationService.ScanAsync` runs `ResumeInterruptedLaunchesAsync` only when `owner is null` (`:126-128`). For a phone-home owner, `ReconcileSessionsAsync(..., owner)` fails a Starting row the runner does not list after `StartingGraceMs` (90 s) with "Session runner does not know this session (... or the server restarted before the launch reached the runner)" (`:230-236`) and leaves a row the runner lists Running as `Starting` forever. `AgentSessionService.ResumeInterruptedLaunchAsync` (`:816-861`) is already runner-aware: `_adapterFactory.Create(session.AgentKind, session.RunnerId)` yields the CARD-0679 D-6 runner-scoped client, and `KillRunnerSessionAsync` routes through `_runtime.KillAsync` by binding. | D-5 runs the interrupted-launch pass for phone-home owners against the recovered inventory; the resume primitive is reused unchanged; the pre-ack row keeps today's explicit failure reason and gains the shutdown's task event. |
| Launches queued when the kill lands are simply lost. | `AgentSessionLaunchQueue` tracks every launch task and has `WaitForIdleAsync(timeout, ct)` (`:179-200`) with no caller under `server/`. | D-2's shutdown drains the queue, bounded, so a launch that reaches its ack becomes a post-ack row D-5 re-attaches. |
| The 15:53:48Z abort can be attributed from the existing lines. | The accept line logs runner id, epoch, capacity, platform, build (`SessionRunnerEndpoints.cs:115-118`) but not `http.Connection.Id`, the only key Kestrel event 34 carries. The ended Warning prints the reason and counts, not the `WebSocketException`'s `WebSocketErrorCode` or inner `SocketException`. The runner's `fault=` is type and message only (`PhoneHomeConnectionService.cs:173-178`). Vite's `/api` proxy (`client/vite.config.ts:33-46`) has no `configure` hook, so the middle hop is silent. `client/scripts/serve.mjs` restarts the preview child only on a mode swap (`swapTo`, `:209-245`); `runRebuild` runs `vite build` alone. | D-6 adds the connection id and transport codes on both sides and a logging-only proxy trace; a client rebuild is not a candidate for 15:53, a mode swap is. |
| The operator token is a runner-slot credential only. | `OperatorCredential.Require` and `OperatorTokenFile` (`server/Infrastructure/Security/`) guard every operator surface, including the dashboard login (`OperatorEndpoints.cs`); `scripts/runner-slots.ps1:35-46` reads `ANTIPHON_OPERATOR_TOKEN_FILE` or `%LOCALAPPDATA%\Antiphon\operator-token` and never prints it; `PhoneHomeTestHost` has `OperatorTokenPath` and `PostOperatorAsync`. | D-2 puts the shutdown route behind the same credential; D-3 reads the same file. |
| The script can watch port 17202 to see the server go. | Aspire's `dcpctrl` owns the published 17202/17203 (AGENTS.md, bootstrap watchdog note); `Get-AppHostPortOwners 17202` returns the proxy, which keeps listening after the server exits. `Test-ProcessAlive` exists (`apphost-common.ps1:88-97`). | D-3 waits on the server PID the shutdown reply returns, never on the port. |
| The restart-script fixtures are Windows-only. | `scripts/test-apphost-server-version.ps1` ran here on server2: **130 passed, 1 failed**; the failure is inherited and unrelated (`T19 ASCII delegate.ps1: high=3`, an em dash at `scripts/delegate.ps1:118`, last touched by CARD-0644), plus a harmless `$env:SystemRoot` null on the PS 5.1 probe. | The script rows have a Linux lane. Code reports T19 as inherited, never fixes it under this card. |
| CARD-0717 will add retries to registration. | Its plan classifies phone-home registration **N** ("no standard handler around register/connect loop") and defers to R2 the narrowing of permanent auth/config failures. | D-4 sets plain timeouts and a pure classification function `PhoneHomeReconnectReason.Classify`; CARD-0717 R2 narrows that function. No Polly, no `Antiphon.Resilience` reference from the runner. |

## Decisions

Numbered D-1 to D-8, written under stated defaults; the Code rounds need no further approval.

### D-1. The host's stop ends every live phone-home connection with a 1001 close frame at once

`SessionRunnerEndpoints` connect route: the receive loop runs on a token linked from
`RequestAborted` and `lifetime.ApplicationStopping`
(`CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.ApplicationStopping)`); the
`catch (OperationCanceledException)` and the `reason` expression test the linked token. The
existing `AbortReason(lifetime)` then yields `request_aborted` ("this host stopping"), so the
D-1 reason name, the status DTO and `docs/ops-http.md` are unchanged. The accept line and the
ended Warning both gain `connection {ConnectionId}` (`http.Connection.Id`) and the accept line
`peer {RemoteIp}:{RemotePort}` (D-6).

`PhoneHomeLiveConnection.DisposeAsync(string reason)` chooses the close frame from the reason:
`request_aborted` sends `WebSocketCloseStatus.EndpointUnavailable` (1001) with description
`PhoneHomeCloseReasons.ServerStopping = "server_stopping"` (new constant in
`src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs`); every other reason keeps
`NormalClosure`/`"dispose"`. The handshake is bounded by `PhoneHomeProtocol.CloseHandshakeSeconds`
(3, a constant, not a setting): a peer that does not answer is `Abort()`ed so a stopping host
never waits on it. `ApplicationStopping` callbacks and Kestrel's own stop run after the route
ends, so the frame is on the wire before the process is killed by the script's bounded wait.

Rejected: a hosted service that closes sockets from `StopAsync` (hosted services stop after
Kestrel's `StopAsync` begins waiting on the very request that holds the socket; the ordering is
wrong by construction); registering on `ApplicationStopping` from `PhoneHomeRunnerDirectory`
(a second writer racing the route's `finally`, when the route already owns the end); making the
runner infer a planned stop from a `NormalClosure` (the runner must be able to tell "server
stopping" from "superseded" and "dispose" without reading a description string it never got).

### D-2. An operator-authenticated shutdown route drains launches, records what it interrupts, then stops the host

`POST /api/operator/shutdown` in `OperatorEndpoints`, guarded by `OperatorCredential.Require`
(403 `operator_token_required` otherwise, whatever the client address). Body:
`{ "reason": "restart-apphost" }` (optional, 200 characters max, logged). Answer: 202
`OperatorShutdownDto(Accepted: true, Pid: Environment.ProcessId, DrainSeconds, StartingLaunches)`.
The route registers the work on `http.Response.OnCompleted` so the 202 is flushed before the
host begins stopping, then calls `OperatorShutdownCoordinator.StopAsync(reason)`:

1. `ILaunchDrain.WaitForIdleAsync(TimeSpan.FromSeconds(Operator:ShutdownDrainSeconds), ct)`
   (default 10; `0` skips the wait). `AgentSessionLaunchQueue` implements the new
   `ILaunchDrain` interface with its existing `WaitForIdleAsync`; nothing refuses new launches
   (a launch that starts a second before the kill is exactly the in-flight case D-5 handles).
2. `RecordInterruptedLaunchesAsync(db)`: for every `Starting` session with a Dispatched task,
   one `AgentTaskEvent` Warning `launch interrupted by an operator shutdown (reason=<reason>) while
   Starting on runner '<RunnerId|local>'; the restart reconciler re-attaches it if the runner
   holds it, or fails it with a restart reason`. Best effort, one `SaveChangesAsync`, errors logged.
3. One Information line
   `Operator shutdown: reason={Reason} drained={Drained} in {DrainMs}ms; starting launches {Count} ({SessionIds})`.
4. `IHostApplicationLifetime.StopApplication()`.

`AntiphonCapabilities.OperatorShutdownV1 = "operator-shutdown-v1"` is added to `/api/version`
so the runbook check can name it. New `OperatorSettings` (section `Operator`,
`ShutdownDrainSeconds` default 10, validated 0..120) bound in `Program.cs`; the existing
`Operator:TokenPath` alias logic is untouched (it stays on `PhoneHomeRunnerSettings`).

Rejected: sending the server a console control event or `taskkill` without `/F` (a DCP-launched
`dotnet` child has no window and no console the script owns; not testable); stopping through
the AppHost's control API (it only knows daemons and kills them); refusing new launches during
the drain (turns a survivable in-flight launch into a Failed card row for no gain); a drain
without a bound (a ready wait can run for minutes).

### D-3. `restart-apphost.ps1` asks the server to stop and waits, bounded, before the force-kill

New step `0b` between `check-daemon-build.ps1` and step 1 (so every refusal path still kills
nothing and the locks are unchanged):

```
$graceful = Invoke-AppHostGracefulStop -TimeoutSec 5 -Reason 'restart-apphost'
if ($graceful.Class -eq 'accepted') {
    $exited = Wait-AppHostProcessExit -ProcessId $graceful.Pid -TimeoutSec $GracefulStopTimeoutSec
    Write-Host "  graceful stop accepted; server PID $($graceful.Pid) $(if ($exited) { "exited after $($exited)s" } else { "still alive after ${GracefulStopTimeoutSec}s; forcing" })"
} else {
    Write-Host "  graceful stop $($graceful.Class): $($graceful.Detail); forcing"
}
```

`apphost-common.ps1` gains three seam-able functions, all ASCII, all Windows PowerShell 5.1 safe:

- `Get-AppHostOperatorTokenPath`: `ANTIPHON_OPERATOR_TOKEN_FILE`, else
  `%LOCALAPPDATA%\Antiphon\operator-token` (the same rule as `runner-slots.ps1`, which is left
  as it is). The value is read into a header and never written to the console or a log.
- `Invoke-AppHostGracefulStop -TimeoutSec -Reason`: `POST http://localhost:17202/api/operator/shutdown`
  with `X-Antiphon-Operator-Token`, `-UseBasicParsing -TimeoutSec 5`. Returns
  `[pscustomobject]@{ Class; Pid; Detail }` with `Class` in `accepted` (202, `Pid` from the body),
  `unsupported` (404: an older server), `refused` (403: token file missing, empty or stale),
  `unreachable` (connection refused or timeout), `error` (anything else, `Detail` is the message).
- `Wait-AppHostProcessExit -ProcessId -TimeoutSec`: polls `Test-ProcessAlive` through
  `Wait-AppHostPollInterval -Seconds 1`; returns the seconds waited when the process is gone,
  `$null` on timeout.

Parameters: `-GracefulStopTimeoutSec` (default 20; the drain default of 10 plus the host's stop)
and `-SkipGracefulStop`. Exit codes and the two locks are unchanged; the step runs only after the
restart lock is held and the SHA admission passed. The health/DCP wait budget (`TimeoutSec`) is
not charged for the graceful wait.

Every fixture that reaches step 1 with seams must seam the new functions, or the real POST would
reach a live 17202: `scripts/test-apphost-server-version.ps1` (`Write-C495Seams`),
`scripts/test-apphost-lock-age.ps1` and `scripts/test-apphost-git-index-lock.ps1` gain
`Invoke-AppHostGracefulStop` returning `unreachable` and `Wait-AppHostProcessExit` returning `$null`
with a trace row. New `scripts/test-apphost-graceful-stop.ps1` (the C495 fixture shape, tag `C716`,
portable `Join-Path` segments so it runs on server2 too) is registered in
`tests/test-execution-policy.json` `scriptCensus` as `unattended`. Its cases are T-1 to T-7 below.

Rejected: waiting on port 17202 (owned by `dcpctrl`, ground truth row 9); putting the graceful
step before the lock (a refusal must kill nothing and must not stop anything either); a default
`-GracefulStopTimeoutSec` above 20 s (the socket close happens in the first second; the rest is
process exit, and the kill is still the guarantee).

### D-4. The runner bounds registration and connect, caps backoff at 5 s, and names every reconnect cause

`PhoneHomeSettings`: `RegistrationTimeoutSeconds` (default 8), `ConnectTimeoutSeconds`
(default 8), both validated 1..120; `ReconnectBackoffMaxMs` default 15000 becomes **5000**;
`Validate` requires `ReconnectBackoffMs` > 0 and <= `ReconnectBackoffMaxMs`. Compose keys are
`PhoneHome__RegistrationTimeoutSeconds`, `PhoneHome__ConnectTimeoutSeconds`,
`PhoneHome__ReconnectBackoffMaxMs`; `docker-compose.server2-runner.yml` is unchanged (defaults).

`PhoneHomeConnectionService.RunConnectionAsync`: `http.Timeout = RegistrationTimeoutSeconds`
on the factory client (the registration is the client's only use); `ws.ConnectAsync` runs under a
linked source with `CancelAfter(ConnectTimeoutSeconds)`. A timeout that is not the stopping token
surfaces as today's exception types (`TaskCanceledException` with an inner `TimeoutException`,
`OperationCanceledException` from the connect source) and the outer loop stays as it is: one
Warning, then `Task.Delay(backoff, _clock, stoppingToken)`, then double up to the cap.

The Warning becomes one classified line without a stack trace for the expected shapes:
`Phone-home registration failed: reason={Reason} attempt={Attempt} backoffMs={Backoff}`, where
`PhoneHomeReconnectReason.Classify(ex, stoppingToken)` (new file, pure) returns `http_<status>`
(`HttpRequestException.StatusCode`), `connect_<SocketError>` (inner `SocketException`),
`registration_timeout`, `connect_timeout`, `ws_<WebSocketErrorCode>`, `overflow`
(`PhoneHomeTransportException(EventOverflow)`), else `other:<TypeName>` (the exception is attached
only in that arm). This function is the single seam CARD-0717 R2 narrows for permanent
auth/config/protocol failures; CARD-0716 changes no backoff for them.

The ended line (`RunConnectedAsync`) gains two fields: `close={Status}:{Description}` read from
`ws.CloseStatus`/`ws.CloseStatusDescription` after `ReadFrameAsync` returns null (`close=none`
otherwise), and, when the ending loop faulted with a `WebSocketException`,
`wsError={WebSocketErrorCode} socketError={inner SocketException.SocketErrorCode|none}` through
the shared `PhoneHomeTransportFault.Describe(WebSocketException)` (contracts project, also used by
D-6 on the desktop). A `server_stopping` close is `fault=none`, and the next registration is
immediate as today: the cheap 502 probe beats a special pause.

Rejected: a Polly pipeline or `AddStandardResilienceHandler` on the registration client
(CARD-0717 classifies it N and its plan says "never add an inner retry budget to each iteration");
a longer first backoff after `server_stopping` (adds latency to the fast-restart case for nothing);
keeping the 15 s cap (the 10:48Z gap shows a returned server hidden behind one blocked attempt;
a 5 s cap plus an 8 s timeout bounds the worst reconnect to 13 s after the server is back).

### D-5. The restart reconciler re-attaches remote Starting rows the runner holds; pre-ack rows fail with the restart reason

`SessionReconciliationService.ScanAsync` calls `ResumeInterruptedLaunchesAsync(available.Sessions,
now, ct, owner)` for phone-home owners as well as the local one. The pass filters Starting rows by
`s.RunnerId == owner` (local: `RunnerId == null`, today's behaviour) and keeps every other guard:
runner status Running, no `Pending`, generation match, `!_ownership.Owns`, the compaction admission.
The inventory is the recovered connection's catch-up List (`GetInventoryAsync` answers
`Unavailable` until then, so no remote row is judged before the runner is back), and the handed
session goes through the unchanged `ResumeInterruptedLaunchAsync`: attach through the runner-scoped
client, ready, Running, flush the pending brief. Its non-resumable arm (no Dispatched task,
non-attachable kind, herdr) still kills through the runner and fails loudly.

A Starting row the runner does not list (the launch never reached the runner, or the runner
restarted) keeps today's path: failed after `StartingGraceMs` with the existing reason, which
already names "the server restarted before the launch reached the runner". D-2's task event is the
durable trace that a planned shutdown interrupted it; the delegation retry scheduler re-dispatches
a failed delegate launch as it does today. The log line `Interrupted launch: session ... the runner
still serves it and this process does not own the launch` gains `runner {RunnerId}`.

Rejected: persisting launch specs so a pre-ack row could be re-launched after a restart (notes,
remote-control name and initial prompt are not durable by design, `ResumeInterruptedLaunchAsync`'s
own failure text; a durable launch spec is a card of its own); widening
`PhoneHomeRecoveryPump.RunCycleAsync` to trigger the resume right after `MarkRecovered` (the
reconciliation sweep already runs on its own cadence with the transaction and generation guards;
a second trigger would race it for the same row); resuming a row whose generation differs (the
runner holds another launch; CARD-0679 D-9's fence decides that).

### D-6. Logging that pins the 15:53:48Z shape to a hop

Desktop (`SessionRunnerEndpoints`, D-1): accept line `connection {ConnectionId} peer {RemoteIp}:{RemotePort}`;
ended Warning `connection {ConnectionId}` and, for `transport_abort`,
`transport {WsError}/{SocketError}` from `PhoneHomeTransportFault.Describe`. Kestrel's event 34
(`ApplicationAbortedConnection`) names the same connection id, so it now maps to an epoch.

Runner (D-4): `close=` and `wsError=`/`socketError=`.

Client: `client/scripts/proxy-trace.mjs` (plain ESM, Node builtins only, the `serve.mjs`
convention) exports `createWsProxyTrace({ label, log, appendLine, now })`, a function for Vite's
proxy `configure` hook. It listens to http-proxy's `proxyReqWs`, `error` and `close` events and,
for a request whose URL contains `/session-runners/` and ends in `/connect`, writes one ASCII line
per event with an ISO timestamp, the runner id parsed from the path, the client `remoteAddress:port`
and, on `proxyReqWs`, `close` listeners on both the client socket and the target socket so the
first `[proxy] ws <client|target> socket closed hadError=<bool>` line names the side that went
first. Lines go to the console (the client resource's Aspire log) and are appended to
`logs/client-proxy.log` (repo-root-relative like `client.state.json`; truncated when it passes
1 MB). `vite.config.ts` passes `configure: createWsProxyTrace({ label: '/api' })` on the `/api`
entry only; `/hubs` (browser SignalR) is untouched so the file stays quiet in normal use.

What to measure the next time the shape recurs (a runner handshake fault while the desktop stays
up), in this order: (1) the desktop accept line whose `connection` id equals Kestrel's event 34 id
gives the epoch; (2) the runner's `socketError=ConnectionReset` says an RST reached the runner,
`wsError=ConnectionClosedPrematurely socketError=none` says a clean FIN without a close frame (Caddy
closing its client side because its upstream went); (3) the first `[proxy] ws ... socket closed`
line names Kestrel (`target`) or Caddy (`client`) as the side that closed first; (4) a
`[serve] mode changed ... swapping` line in the client console at that second means a client mode
swap killed the preview process and its proxied sockets; (5) Caddy's access log entry for
`GET /api/session-runners/<id>/connect` (status 101) ends at the close and its upstream error log
names a dial or read failure. Nothing in this plan changes Caddy.

Rejected: a Vite plugin with a dependency (the client's dependency tree must not be able to break
serve infrastructure, `serve.mjs`'s own rule); tracing `/hubs` too (every browser tab reconnects;
noise); a desktop-side `IConnectionLifetimeFeature` probe (the id is enough to join the lines).

### D-7. Settings and defaults

| Setting | Default | Where |
|---|---|---|
| `Operator:ShutdownDrainSeconds` | 10 (0..120) | desktop `OperatorSettings` (new) |
| `PhoneHomeProtocol.CloseHandshakeSeconds` | 3 (constant) | contracts |
| `PhoneHome:RegistrationTimeoutSeconds` | 8 (1..120) | runner `PhoneHomeSettings` |
| `PhoneHome:ConnectTimeoutSeconds` | 8 (1..120) | runner `PhoneHomeSettings` |
| `PhoneHome:ReconnectBackoffMaxMs` | 5000 (was 15000) | runner `PhoneHomeSettings` |
| `restart-apphost.ps1 -GracefulStopTimeoutSec` | 20 | script |
| `restart-apphost.ps1 -SkipGracefulStop` | off | script |

### D-8. Docs

- `docs/apphost-runbook.md` "First start and AppHost restart": the graceful step, the two
  parameters, the printed `graceful stop ...` line, the first-restart-after-landing note, and
  that exit codes and locks are unchanged. "AppHost locks and exits" is unchanged.
- `docs/ops-http.md`: a row for `POST /api/operator/shutdown` (token, body, 202 body, drain, the
  `operator-shutdown-v1` capability, and that `restart-apphost.ps1` is the caller); the status
  row's `request_aborted` now says the runner receives close 1001 `server_stopping`.
- `docs/logs.md`: the new fields on both sides (`connection`, `transport`, `close=`, `wsError=`,
  `socketError=`), the runner's `Phone-home registration failed: reason=` line, and
  `logs/client-proxy.log` with the measurement order from D-6.
- `docs/session-runtime-invariants.md`: one bullet under the phone-home group: a planned server
  stop closes the socket with 1001 `server_stopping` after a bounded launch drain; a remote
  Starting row the runner holds is re-attached by the restart reconciler; one it never received is
  failed with the restart reason and carries the shutdown's task event.
- `AGENTS.md` line 29 gains "the restart asks the server to stop gracefully first (CARD-0716)".
- `dev-aspire.ps1`'s stale synopsis line ("Server + Client: true daemons") is corrected in S3
  (comment only).

## Implementation rounds and slices

Two Code rounds. R1 (S1 to S3) is the desktop side of the restart and is complete on its own; R2
(S4 to S6) is the runner, the resume and the client trace. One Code dispatch runs R1 then R2 when
it fits the budget, else R2 is a second Code dispatch from the same table. Order inside a round is
fixed. Each slice commits its compiling red tests (`Sn-tests`) before its production change (`Sn`);
every commit message states the real checkpoint outcome.

| Round | Slice | Files | Tests (new methods) |
|---|---|---|---|
| R1 | S1: D-1 close on stop, D-6 desktop fields | `server/Api/Endpoints/SessionRunnerEndpoints.cs`, `server/Infrastructure/Agents/SessionRunner/PhoneHomeLiveConnection.cs`, `src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs` (`PhoneHomeCloseReasons`, `PhoneHomeProtocol.CloseHandshakeSeconds`, `PhoneHomeTransportFault`), `tests/Antiphon.Tests/TestHelpers/PhoneHomeTestHost.cs` (`shutdownTimeout` parameter, `MapOperatorEndpoints`, `MapVersionEndpoints`, launch-queue and `OperatorSettings` registration for S2; `PhoneHomeScriptedPeer.CloseObserved`) | `PhoneHomeConnectionTests`: V-1, V-2 |
| R1 | S2: D-2 shutdown route, coordinator, drain, capability | `server/Api/Endpoints/OperatorEndpoints.cs`, `server/Application/Services/OperatorShutdownCoordinator.cs` (new), `server/Application/Interfaces/ILaunchDrain.cs` (new), `server/Application/Services/AgentSessionLaunchQueue.cs` (implements it), `server/Application/Settings/OperatorSettings.cs` (new), `server/Application/Dtos/OperatorDtos.cs` (new DTO), `server/Application/Dtos/VersionDtos.cs`, `server/Api/Endpoints/VersionEndpoints.cs`, `server/Program.cs` (bind and register) | `OperatorShutdownCoordinatorTests` (new): V-3, V-4; `OperatorShutdownEndpointTests` (new): V-5, V-6 |
| R1 | S3: D-3 script step, seams, fixture | `scripts/apphost-common.ps1`, `scripts/restart-apphost.ps1`, `scripts/test-apphost-graceful-stop.ps1` (new), `scripts/test-apphost-server-version.ps1`, `scripts/test-apphost-lock-age.ps1`, `scripts/test-apphost-git-index-lock.ps1` (seams), `tests/test-execution-policy.json` (census), `dev-aspire.ps1` (synopsis comment), `docs/apphost-runbook.md`, `AGENTS.md` | script cases T-1 to T-7 |
| R2 | S4: D-4 runner timeouts, cap, classification, ended fields | `src/Antiphon.SessionRunner/PhoneHomeSettings.cs`, `PhoneHomeConnectionService.cs`, `PhoneHomeReconnectReason.cs` (new), `tests/Antiphon.SessionRunner.Tests/TestHelpers/PhoneHomeTestWebSocket.cs` (`EnqueueClose`, `FailNextReceive`, settable `CloseStatus`) | `PhoneHomeConnectionServiceTests`: V-7 to V-12 |
| R2 | S5: D-5 remote resume, D-2 interrupted-launch event | `server/Application/Services/SessionReconciliationService.cs`, `tests/Antiphon.Tests/Application/SessionReconciliationServiceTests.cs` (`BuildService` gains `directory:`), `tests/Antiphon.Tests/Application/PhoneHomeRestartResumeTests.cs` (new) | `SessionReconciliationServiceTests`: V-13; `PhoneHomeRestartResumeTests`: V-14, V-15 |
| R2 | S6: D-6 client trace, D-8 docs | `client/scripts/proxy-trace.mjs` (new), `client/scripts/proxy-trace.test.mjs` (new), `client/vite.config.ts`, `docs/ops-http.md`, `docs/logs.md`, `docs/session-runtime-invariants.md` | Vitest: V-16 (three cases) |

## Verification design

### Harness and red-first discipline

- Desktop transport tests use `PhoneHomeTestHost` (real Kestrel loopback, real directory, real
  connect route, capturing logger) and `PhoneHomeScriptedPeer`, the CARD-0679 D-11 harness. A host
  stop is `host.App.Lifetime.StopApplication()` on a background task; V-1's host is started with
  `shutdownTimeout: TimeSpan.FromSeconds(3)` so a red run does not sit in Kestrel's 30 s default.
  `PhoneHomeScriptedPeer` gains `CloseObserved` (a `TaskCompletionSource<(WebSocketCloseStatus?, string?)>`
  completed by its receive loop on a Close frame).
- Operator route tests use the same host with `MapOperatorEndpoints` and `PostOperatorAsync`
  (token from `host.OperatorTokenPath`, as the CARD-0653 tests do). Coordinator unit tests use a
  fake `ILaunchDrain`, a recording `IHostApplicationLifetime` and real short bounds (at most 1 s).
- Restart-resume tests use the `PhoneHomeLaunchTransportTests.LaunchWorld` shape (isolated schema,
  `PhoneHomeTestHost`, `BridgeQueueHarness` with the real `AgentProtocolAdapterFactory` bound to
  `host.Directory`, `AgentKind.Raw`) and build the reconciler through
  `SessionReconciliationServiceTests.BuildService(..., directory: host.Directory, ownership: <the harness's AgentSessionLaunchQueue>)`.
  `SingleRunnerDirectory` (TestHelpers) serves the unit-level V-13.
- Runner tests use `PhoneHomeConnectionServiceTests`' existing `Connected(logs)`, `RecordingHandler`,
  `SingleHandlerFactory` and `PhoneHomeTestWebSocket`. V-7 binds `RegistrationTimeoutSeconds`
  through `ConfigurationBuilder.AddInMemoryCollection` + `Bind` so the red test compiles before the
  property exists (the binder ignores an unknown key; today's 100 s is the red). V-8 drives
  `ExecuteAsync` on a `FakeTimeProvider` (`Task.Delay(backoff, _clock, ct)` honours it). V-10's
  hanging connect is a loopback `TcpListener` that accepts and never answers the upgrade, with the
  registration handler answering a scripted `PhoneHomeRegistrationResponse`.
- Script cases use the C495 fixture pattern (temp git repo, inert `dev-aspire.ps1`,
  `check-daemon-build.ps1`, seams file, `trace.jsonl`); no case touches 172xx.
- Red first: each slice commits compiling tests that fail on the current code at the assertion the
  roster names, runs the red row, then implements and runs the green row. No wall-clock wait in a
  test above 3 s. Nothing contacts server2, 17202 to 17205, Docker or a provider.

### Coverage roster and decisive assertions

| ID | Class.Method | Assertion / red mechanism |
|---|---|---|
| V-1 | PhoneHomeConnectionTests.Host_stop_sends_a_going_away_close_before_the_socket_dies | Connect a peer; `StopApplication()`; `peer.CloseObserved` completes within 2 s with `EndpointUnavailable` and `"server_stopping"`; `host.Logs` has one Warning `ended: request_aborted`; `Status().DisconnectReason == "request_aborted"`. Today: no close frame inside 2 s (the socket dies at the 3 s shutdown timeout), assertion 1 fails. |
| V-2 | PhoneHomeConnectionTests.Accept_and_end_lines_carry_the_connection_id_and_transport_codes | `peer.Socket.Abort()`; the accept Information entry and the ended Warning share a non-empty `ConnectionId` property; the ended entry's `WsError` is `ConnectionClosedPrematurely` and `SocketError` is a named `SocketError` or `none`. Today: no `ConnectionId`, `WsError` or `SocketError` property. |
| V-3 | OperatorShutdownCoordinatorTests.Stop_waits_for_the_launch_drain_then_stops_once | Drain fake completes after 300 ms; `StopAsync("test")`; `StopApplication` was called exactly once, after the drain completed; the Information line names `drained=True`. Today: the class does not exist (the red commit adds the class as a stub throwing `NotImplementedException`, so the failure is the assertion, not a build error). |
| V-4 | OperatorShutdownCoordinatorTests.Stop_proceeds_when_the_drain_bound_expires | Drain fake never completes, `ShutdownDrainSeconds` = 1; `StopApplication` called once after >= 1 s; line names `drained=False`. Same red mechanism as V-3. |
| V-5 | OperatorShutdownEndpointTests.Shutdown_without_the_operator_token_is_403_and_stops_nothing | `PostOperatorAsync("/api/operator/shutdown", body, token: null)` is 403 with code `operator_token_required`; `App.Lifetime.ApplicationStopping` is not requested after 500 ms. Today: 404 (no route). |
| V-6 | OperatorShutdownEndpointTests.Shutdown_with_the_token_answers_202_then_begins_stopping | With the token: 202, body `accepted == true`, `pid == Environment.ProcessId`; `ApplicationStopping` requested within 2 s; `GET /api/version` capabilities contain `operator-shutdown-v1`. Today: 404. |
| V-7 | PhoneHomeConnectionServiceTests.Registration_that_never_answers_ends_within_the_registration_timeout | Handler awaits `Task.Delay(Timeout.Infinite, ct)`; settings bound with `RegistrationTimeoutSeconds = 1`; `RunConnectionAsync(CancellationToken.None)` throws `TaskCanceledException` within 3 s. Today: `WaitAsync(3 s)` times out (`TimeoutException`), the typed-throw assertion fails. |
| V-8 | PhoneHomeConnectionServiceTests.Reconnect_backoff_caps_at_five_seconds | Handler answers 502; `FakeTimeProvider`; after attempt 1, advancing 1 s, 2 s, 4 s and 5 s yields attempts 2, 3, 4 and 5 (`RegistrationAttempts`). Today: attempt 5 needs 8 s, the count after the 5 s advance is 4. |
| V-9 | PhoneHomeConnectionServiceTests.Registration_failure_line_names_the_status_attempt_and_backoff | Handler answers 502; one Warning `Phone-home registration failed: reason=http_502 attempt=1 backoffMs=1000`, without an attached exception. Today: the line is "Phone-home connection ended; reconnecting with backoff 1000ms" with the exception. |
| V-10 | PhoneHomeConnectionServiceTests.Websocket_connect_that_hangs_ends_within_the_connect_timeout | Registration answers a ticket; `ServerOrigin` is a loopback `TcpListener` that never answers; `ConnectTimeoutSeconds = 1`; `RunConnectionAsync` throws `OperationCanceledException` within 3 s while the caller's token is not cancelled. Today: the `WaitAsync(3 s)` times out. |
| V-11 | PhoneHomeConnectionServiceTests.Server_close_frame_ends_the_connection_cleanly_and_names_its_status | `socket.EnqueueClose(EndpointUnavailable, "server_stopping")`; `RunConnectedAsync` returns without throwing; the ended line contains `loop=receive`, `fault=none` and `close=EndpointUnavailable:server_stopping`. Today: no `close=` field. |
| V-12 | PhoneHomeConnectionServiceTests.Handshake_fault_line_names_the_websocket_and_socket_errors | `socket.FailNextReceive(new WebSocketException(WebSocketError.ConnectionClosedPrematurely, new IOException("reset", new SocketException((int)SocketError.ConnectionReset))))`; the ended line contains `wsError=ConnectionClosedPrematurely` and `socketError=ConnectionReset`. Today: neither field. |
| V-13 | SessionReconciliationServiceTests.Remote_Starting_row_the_runner_serves_is_handed_to_resume | `BuildService(db, RunnerRunning(sessionId, startedAt), ..., ownership: recording, directory: new SingleRunnerDirectory(client, "grok-linux"))`, row `RunnerId = "grok-linux"`, Dispatched task; `ScanAsync`; `ownership.Resumed` contains the id once. Today: never handed (remote owners skip the pass). |
| V-14 | PhoneHomeRestartResumeTests.Starting_row_bound_to_the_runner_is_reattached_by_the_restart_scan | LaunchWorld seed (Starting, remote, Dispatched task); peer `Sessions` lists the id Running with the seeded generation; the reconciler (`directory: host.Directory`, `ownership: harness queue`) `ScanAsync`; poll 3 s: the session is `Running`, `peer.RequestCount(Get) >= 1`, `peer.Launches.Count == 0`, no `KillGeneration`. Today: stays `Starting`. |
| V-15 | PhoneHomeRestartResumeTests.Shutdown_records_the_interrupted_launch_and_the_scan_fails_the_pre_ack_row_with_the_restart_reason | Same seed; peer lists nothing; `OperatorShutdownCoordinator.RecordInterruptedLaunchesAsync(db)` then `ScanAsync` with `now` past `StartingGraceMs`; one `AgentTaskEvent` Warning on the task containing `operator shutdown` and the runner id; the row is `Failed` and `FailureReason` contains `server restarted`. Today: no task event (assertion 1 fails; the failure arm is today's behaviour and is the guard). |
| V-16 | proxy-trace.test.mjs (three `it` cases) | A fake `EventEmitter` proxy: `proxyReqWs` for `/api/session-runners/server2/connect` logs one line with `runner=server2` and the remote port, and attaches `close` listeners to both sockets whose firing logs `client socket closed` / `target socket closed`; `proxyReqWs` for `/hubs/antiphon` logs nothing; `error` with `code: 'ECONNRESET'` logs `ws error ECONNRESET`. Today: the module does not exist (import failure; the only acceptable non-assertion red, because the module is the unit). |
| T-1 | test-apphost-graceful-stop.ps1: accepted, pid exits | Seams: graceful `accepted` pid 4242, alive for 2 polls; trace order is `graceful` before `stop-tree`; output has `graceful stop accepted; server PID 4242 exited after`. Today (unmodified script): no `graceful` row. |
| T-2 | T-2 accepted, pid never exits | `-GracefulStopTimeoutSec 3`, seamed poll interval 0; output has `still alive after 3s; forcing`; `stop-tree` still traced; exit code unchanged from the same config without the graceful seam. |
| T-3 | T-3 unreachable | Seam class `unreachable`; output `graceful stop unreachable: ...; forcing`; no `Wait-AppHostProcessExit` trace; kill proceeds. |
| T-4 | T-4 unsupported and refused | 404 and 403 classes each print the class and proceed; the token value never appears in the output (the seam config carries a sentinel token; the output is grepped for it). |
| T-5 | T-5 skip switch | `-SkipGracefulStop`: no `graceful` trace, kill proceeds. |
| T-6 | T-6 refusal kills and stops nothing | A fresh restart lock: exit 3, no `graceful` and no `stop-tree` trace (extends the C495 T22 shape). |
| T-7 | T-7 ASCII and parse | `restart-apphost.ps1`, `apphost-common.ps1` and the test itself contain no byte above 127 and parse under pwsh; the PS 5.1 parse is skipped when `powershell.exe` is absent (the C495 T19 shape). |

Regression classes executed in the green rows: `PhoneHomeConnectionTests` (all 19 at `07df1561`),
`OperatorDashboardSessionsTests` (4), `PhoneHomeConnectionServiceTests` (10),
`SessionReconciliationServiceTests` (57 methods, some argument-expanded; Code reads the TRX),
`PhoneHomeLaunchTransportTests` (7) and `AgentSessionInterruptedLaunchResumeTests` (7 methods,
8 results) after S5, and the three existing restart-script fixtures after S3
(`test-apphost-server-version.ps1` 130 passing at `07df1561` plus the inherited T19 failure,
`test-apphost-lock-age.ps1`, `test-apphost-git-index-lock.ps1`).

### Execution and evidence

Run each TUnit row with `pwsh -NoProfile -File scripts/run-checkpoint.ps1` and the exact filter,
`-MinExecuted` and comma-separated `-Expect`; results roots `.antiphon/c716/CP-n-<sha>`, fresh per
run. Build outputs are `bin-c716-rN/` (forward slash), one per round, removed after the round's
awaited runs. On server2 the script adds `UseAppHost=false` itself (CARD-0671); nothing here
resolves the FakeClaude apphost. Red rows return exit 1 with the named methods in `FAILED` lines;
that exit is the evidence. Green rows require zero failed and zero skipped among the new methods
and every listed regression class executed. Script and Vitest rows are non-TUnit: Code produces
the `CHECKPOINT` line by hand with the harness's own pass/fail counts and the command in
`filter=`. Report every row as one `CHECKPOINT` line with counts, commit and TRX (or log) path,
plus `reruns=k`. Source is frozen during a run. A failure not explained by the slice is re-run
alone at the base commit and reported as inherited or owned; no timeout is widened and no
assertion loosened. `test-apphost-server-version.ps1`'s T19 (`delegate.ps1` ASCII) is inherited
at `07df1561` and is reported, not fixed.

`Antiphon.Tests` and `Antiphon.SessionRunner.Tests` never run concurrently. No row runs a
namespace or the full assembly.

### Cost

R1: three slices, about 50 to 70 minutes of authoring each, plus the rows below. R2: three
slices, about 45 to 60 minutes each. The ordinary checkpoint floor is the sum of
`EstimatedMinutes`: **41 minutes**. Suggested `-ExpectAbout`: R1 200 minutes, R2 180 minutes
(one dispatch: 380). No live canary or broad suite is in this profile.

### Checkpoints

The closed list for Code. `Sn-tests` means the slice's compiling red tests are committed before
its production change; `Sn` means the production change is committed. Every green row rebuilds
into the same `bin-c716-rN/` path (its `After` differs from the red row, so no `--no-build`
reuse). Filters use the CARD-0403 combined-class syntax; the backslashes before `|` are Markdown
escaping only. Red rows require the named assertion failures, not any failure.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-tests | `tests/Antiphon.Tests -> bin-c716-r1/` | close-on-stop-red | `/*/*/PhoneHomeConnectionTests*/*` | V-1, V-2 | 2 new + 19 existing executed; V-1, V-2 fail at their close/property assertions | 21 | 3.5 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c716-r1/` | close-on-stop-green | `/*/*/PhoneHomeConnectionTests*/*` | V-1, V-2; connection regressions | all listed, 0 failed/skipped | 21 | 3.5 |
| CP-3 | S2-tests | `tests/Antiphon.Tests -> bin-c716-r1/` | shutdown-red | `/*/*/(OperatorShutdownCoordinatorTests*)\|(OperatorShutdownEndpointTests*)\|(OperatorDashboardSessionsTests*)/*` | V-3 to V-6 | 4 new + 4 existing executed; V-3 to V-6 fail at their stop/status assertions | 8 | 3 |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c716-r1/` | shutdown-green | `/*/*/(OperatorShutdownCoordinatorTests*)\|(OperatorShutdownEndpointTests*)\|(OperatorDashboardSessionsTests*)/*` | V-3 to V-6; dashboard-session regressions | all listed, 0 failed/skipped | 8 | 3 |
| CP-5 | S3-tests | n/a | script-red | `pwsh -NoProfile -File scripts/test-apphost-graceful-stop.ps1` | T-1 to T-7 | T-1 to T-5 FAIL (no graceful step yet), T-6 and T-7 PASS; exit 1 | n/a | 2 |
| CP-6 | S3 | n/a | script-green | `pwsh -NoProfile -File scripts/test-apphost-graceful-stop.ps1` | T-1 to T-7 | 7 cases, every `PASS`, 0 `FAIL`, exit 0 | n/a | 2 |
| CP-7 | S3 | n/a | script-regression | `pwsh -NoProfile -File scripts/test-apphost-server-version.ps1; pwsh -NoProfile -File scripts/test-apphost-lock-age.ps1; pwsh -NoProfile -File scripts/test-apphost-git-index-lock.ps1` | S3 seams | server-version: 130 passed, only T19 (`delegate.ps1` ASCII, inherited) failed; the other two: 0 `FAIL` | n/a | 5 |
| CP-8 | S4-tests | `tests/Antiphon.SessionRunner.Tests -> bin-c716-r4/` | runner-red | `/*/*/PhoneHomeConnectionServiceTests*/*` | V-7 to V-12 | 6 new + 10 existing executed; V-7 to V-12 fail at their timeout/backoff/line assertions | 16 | 3.5 |
| CP-9 | S4 | `tests/Antiphon.SessionRunner.Tests -> bin-c716-r4/` | runner-green | `/*/*/PhoneHomeConnectionServiceTests*/*` | V-7 to V-12; runner connection regressions | all listed, 0 failed/skipped | 16 | 3.5 |
| CP-10 | S5-tests | `tests/Antiphon.Tests -> bin-c716-r5/` | resume-red | `/*/*/(SessionReconciliationServiceTests*)\|(PhoneHomeRestartResumeTests*)/*` | V-13 to V-15 | 3 new + 57 existing methods executed (more results with argument expansion); V-13 to V-15 fail at their handed/Running/task-event assertions | 60 | 4.5 |
| CP-11 | S5 | `tests/Antiphon.Tests -> bin-c716-r5/` | resume-green | `/*/*/(SessionReconciliationServiceTests*)\|(PhoneHomeRestartResumeTests*)\|(PhoneHomeLaunchTransportTests*)\|(AgentSessionInterruptedLaunchResumeTests*)/*` | V-13 to V-15; launch-loop and attach regressions | all listed classes, 0 failed; 3 new methods, 0 skipped | 75 | 5.5 |
| CP-12 | S6-tests | n/a | client-trace-red | `pwsh -NoProfile -File scripts/test-client.ps1 scripts/proxy-trace.test.mjs` | V-16 | the file fails (module missing); exit 1 | n/a | 1 |
| CP-13 | S6 | n/a | client-trace-green | `pwsh -NoProfile -File scripts/test-client.ps1 scripts/proxy-trace.test.mjs` | V-16 | 3 passed, 0 failed, exit 0 | n/a | 1 |

`Min` counts executed TUnit results; existing-class counts are from the classes as they stand at
`07df1561`: `PhoneHomeConnectionTests` 19, `OperatorDashboardSessionsTests` 4,
`PhoneHomeConnectionServiceTests` 10, `SessionReconciliationServiceTests` 57 methods,
`PhoneHomeLaunchTransportTests` 7, `AgentSessionInterruptedLaunchResumeTests` 7 methods (8 results).
Code reads the fresh TRX rather than these numbers if a class has grown by then. Cost floor =
3.5 + 3.5 + 3 + 3 + 2 + 2 + 5 + 3.5 + 3.5 + 4.5 + 5.5 + 1 + 1 = 41 minutes.
