# CARD-0716 — server2 phone-home closes without a handshake (2026-09-25)

Investigate stage, task 1668ddd6. Read-only except this report. No code, config, or database change.

## Verdict

**Confirmed.** The seven receive-loop ends at 07:34, 08:44, 09:16, 10:48, 11:15, 11:55 and 15:43Z are the desktop AppHost being force-killed. `scripts/restart-apphost.ps1` calls `Stop-AppHostProcessTree`, which is `taskkill /T /F` (`scripts/apphost-common.ps1:493-495`), and then `Stop-Process -Force` on whatever still owns ports 17202 and 17203 (`scripts/restart-apphost.ps1:180-191`). The phone-home WebSocket is torn down with the process, before `PhoneHomeLiveConnection.DisposeAsync` can send a close frame (`server/Infrastructure/Agents/SessionRunner/PhoneHomeLiveConnection.cs:510`). The runner's receive loop reports `WebSocketException: The remote party closed the WebSocket connection without completing the close handshake`, `overflow=false`, `pending=0`.

The runner container did not restart: `antiphon-runner-session-runner-1` `StartedAt=2026-09-25T06:49:31Z`, `RestartCount=0`, not OOM-killed. Each of the seven is followed by registration retries that fail until the new desktop process logs `Application started`, and the next socket is epoch 1.

The 10:48Z episode lasted 8m 38s because that restart took that long, not because a timer held the socket open. A keep-alive or idle-timeout change does not stop these drops.

## Which side closed

For each of the seven, the runner log (`/state/runner-logs/session-runner-20260925.log`, timestamps +00:00) records `loop=receive` and the handshake fault, then an immediate new `RunConnectionAsync`. That attempt throws, and the outer loop logs `reconnecting with backoff`. The desktop log (`C:\src\Antiphon\server\logs\antiphon-20260925.log`, timestamps +01:00) has no `Phone-home connection ... ended` line and no `Application is shutting down` for that socket. The next desktop lines are a new process: `Application started`, then `Phone-home connection server2 epoch 1 accepted`. The only `Application is shutting down` in the whole file is 01:49:29 +01:00, which is not one of the seven.

A runner-initiated stop looks different. At 06:49:19Z the runner logged `loop=heartbeat fault=cancelled` (the container was replaced at 06:49:31Z). The desktop stayed up and logged `transport_abort` at 07:49:21 +01:00, then accepted epoch 2 at 07:49:43 +01:00.

| Runner fault (Z) | epoch | lifetime | Reconnected (Z) | Desktop `Application started` | Gap |
|---|---|---|---|---|---|
| 07:34:09.081 | 2 | 44m 26s | 07:36:12.130 | 08:36:10 +01 (07:36:10Z) | 2m 03s |
| 08:44:02.077 | 1 | 67m 50s | 08:45:52.682 | 09:45:49 +01 (08:45:49Z) | 1m 51s |
| 09:16:58.228 | 1 | 31m 06s | 09:18:48.738 | 10:18:45 +01 (09:18:45Z) | 1m 51s |
| 10:48:52.181 | 4 | 8m 31s | 10:57:30.291 | 11:57:27 +01 (10:57:27Z) | 8m 38s |
| 11:15:09.979 | 1 | 17m 40s | 11:16:17.339 | 12:16:16 +01 (11:16:16Z) | 1m 07s |
| 11:55:39.298 | 2 | 37m 26s | 11:57:13.765 | 12:57:12 +01 (11:57:12Z) | 1m 34s |
| 15:43:52.510 | 3 | 10m 02s | 15:45:22.458 | 16:45:20 +01 (15:45:20Z) | 1m 30s |

09:16Z and 15:43Z are the same shape as the restarts the card listed near 07:35, 08:45, 10:50, 11:15 and 11:58Z. The current `C:\src\Antiphon\logs\apphost.log` is only the last launch: `restart-apphost.ps1:206` truncates that file on every restart, so it cannot witness the earlier kills. Its mtime is 16:44:55 +01:00, during the 15:43Z restart.

Three earlier handshake faults the same day match the same restart gap: 01:36:31Z (reconnected 01:38:51Z, desktop started 02:38:47 +01), 03:56:45Z (reconnected 03:59:22Z, started 04:59:21 +01), 05:06:31Z (reconnected 05:08:46Z, started 06:08:45 +01).

## Path, and the timeouts that were checked

`PhoneHome__ServerOrigin` on the running container is `https://antiphon.desktop.codeperf.net`. DNS for that name is Tailscale, DNS-only, so Cloudflare's proxy idle timeout is not on the path. Caddy (`desktop-caddy-1`, `C:\src\ClaudeBot\desktop\caddy\generated\antiphon.caddy:2-4`) reverse-proxies the host to `host.docker.internal:17203` with no stream or idle timeout in the vhost. Vite (`client/vite.config.ts:33-46`, shared by `server` and `preview`) proxies `/api` with `ws: true` and no timeout to the Aspire server URL, which is port 17202. Kestrel therefore sees `ClientIp ::1` (`server/Api/Middleware/CurrentUserMiddleware.cs:30` uses `RemoteIpAddress`).

After each kill, Caddy logs the runner's `POST /api/session-runners/register` as `dial tcp 192.168.65.254:17203: connect: connection refused`, status 502, duration a few milliseconds. That is 17203 gone, not an idle close of an established socket. The first such 502 is about one second after the runner's handshake fault (10:48:52.683Z after the 10:48:52.181Z fault; 15:43:53.002Z after the 15:43:52.510Z fault).

Measured on the installed ASP.NET Core 9.0.16 assemblies, because this code never sets them:

| Setting | Value | Where |
|---|---|---|
| Server WebSocket `KeepAliveInterval` | 2 minutes | `WebSocketOptions` default. `AcceptWebSocketAsync()` is called with no options (`server/Api/Endpoints/SessionRunnerEndpoints.cs:103`). `UseWebSockets()` has no options (`server/Program.cs:914`). |
| Server WebSocket `KeepAliveTimeout` | infinite | Same default. A missed pong does not abort. |
| Client `ClientWebSocket.Options.KeepAliveInterval` | 30 seconds | Runtime default. `new ClientWebSocket()` sets only the ticket header (`src/Antiphon.SessionRunner/PhoneHomeConnectionService.cs:103-108`). |
| App heartbeat | 15 seconds | `PhoneHomeProtocol.DefaultHeartbeatSeconds`. |
| Lease | 90 seconds | `PhoneHomeProtocol.DefaultLeaseSeconds`. |
| Kestrel `KeepAliveTimeout` | 2m 10s | `KestrelServerLimits` default. No `ConfigureKestrel` in `server/Program.cs`. |
| Kestrel min request/response data rate | 240 bytes/s, 5s grace | Same defaults. |

The 08:44Z socket had been up for 67m 50s (`lifetimeMs=4069947`). That outlives every timeout in the table, so those defaults are not what ended the seven sockets.

Reconnect backoff starts at 1000ms and caps at 15000ms (`src/Antiphon.SessionRunner/PhoneHomeSettings.cs:82-83`). A receive-loop fault does not throw out of `RunConnectedAsync` unless it was an overflow, so the next registration is immediate; the backoff applies to the failed registration (`PhoneHomeConnectionService.cs:56-72`).

## The 8m 38s gap

From 10:48:52Z to 10:57:30Z the desktop was down. Caddy's fast 502s cover 10:48:52Z through 10:50:23Z and again 10:56:23Z through 10:56:53Z. The runner also logged failures at 10:52:17Z, 10:54:12Z and 10:56:07Z, each about 100s after the previous attempt, with no matching Caddy line. `AddHttpClient(nameof(PhoneHomeConnectionService))` (`src/Antiphon.SessionRunner/Program.cs:45`) does not set `HttpClient.Timeout`, so the default is 100 seconds. Those three attempts expired in the client and never reached Caddy. The server then logged `Application started` at 10:57:27Z and the runner connected at 10:57:30Z. The long retries did not extend this outage past the restart; an attempt already blocked in `HttpClient` can still hide a server that comes back during that 100s.

## Overflow closes the same day

Overflow closes are a different line. The runner records `fault=none` (10:38:16Z, 10:39:07Z, 10:40:20Z, 11:18:12Z, 15:19:24Z, 15:33:49Z, 16:02:20Z, 16:02:59Z). The desktop records `phone_home_event_overflow` with `socket "Open"` and pending 8192, including `pump released 0.0 events/s` (11:38:16 +01:00 and others). That is the CARD-0679 overflow path. The seven handshake faults are `overflow=false` and `pending=0` on the runner.

## One extra handshake fault while the process stayed up

At 15:53:48.548Z the runner logged the same handshake exception (`epoch=1`, `lifetimeMs=506089`, `overflow=false`, `pending=0`, `inFlight=0`) and connected epoch 2 at 15:53:48.732Z, with no backoff line. The desktop did not restart. At 16:53:48.719 +01:00 Kestrel logged event 34 `ApplicationAbortedConnection` ("the application aborted the connection") on the websocket connection `0HNOR3G0QK89H`, then `ended: transport_abort` with `socket "Aborted"`, in flight 5, failing 5 waiters (`SessionRunnerEndpoints.cs:139-142` and `AbortReason` at `:181-182`: request aborted while the host is not stopping). Epoch 2 was accepted at 16:53:48.900 +01:00. Caddy logged no dial error in that second. The server source has no `HttpContext.Abort` call. This one recovered in the same second. It is not one of the seven restart kills, and the log order (runner fault 15:53:48.548Z, desktop abort 15:53:48.719 +01:00, desktop accept of the new socket 15:53:48.900 +01:00 versus runner connected 15:53:48.732Z) is inside the same ~170ms stamp lag as the successful reconnect, so it does not say which hop reset the TCP connection.

## Not done, noted

A keep-alive or idle-timeout edit does not survive `taskkill /F`. A graceful stop that reaches `DisposeAsync` before the kill would send the close frame the runner is missing; the runner still cannot take a launch until the new process accepts epoch 1. Setting a registration `HttpClient.Timeout` well under 100s would keep a blocked register from hiding a server that is already back.

## Uncertainties

- Nothing durable records which caller invoked `restart-apphost.ps1` for each of the seven. The evidence is the kill's effect: no shutdown line, epoch reset to 1, `Application started` one to nine minutes later, and Caddy `connection refused` to port 17203 in between.
- The 15:53:48Z in-process abort is a real handshake fault with the desktop still up. The initiator inside Kestrel, Vite, or Caddy's already-open upstream is not identified.
- The three ~100s register attempts during the 10:48Z outage never appeared in the Caddy log. The timeout matches `HttpClient`'s default; why those three connects did not reach Caddy while the attempts around them did is not identified.
