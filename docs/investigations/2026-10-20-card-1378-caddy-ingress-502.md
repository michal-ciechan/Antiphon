# CARD-1378: desktop Caddy ingress 502s

The 502 mechanism is confirmed from stored evidence: Caddy exhausts its default three-second TCP dial deadline to `192.168.65.254:17203`, has no configured reverse-proxy retry, and returns 502. The reason those connections fail is **not confirmed**. In particular, neither a concurrent-request burst nor an overflowing Node accept queue has been established.

Investigation: 2026-10-20, task `f3b2d395`, branch `feat/card-task-f3b2d395`, starting commit `6ec71d427574f3a1b74bfbe2042e0ea9a819d191`. This is an evidence-only report. No traffic probes, bursts, load tests, application builds/tests, source repairs, configuration changes, reloads, restarts or deployments were performed. The explicitly requested card read was the only Antiphon API read; subsequent investigation used files, process/socket inventory, Docker metadata/logs and upstream source documentation.

## Evidence custody

New sanitized evidence is under `C:\Antiphon\worktrees\card-task-f3b2d395\.antiphon\task-f3b2d395` (abbreviated **E** below). Generated files remain gitignored. This report preserves the essential observations so that its conclusions do not depend solely on that worktree surviving.

- `E\caddy-request-rows.jsonl:1`: 38 errors, one JSON object per line, in timestamp order. Each records the original Docker output stream/window line, UTC, path, duration, error ID and trace. Request credentials, cookies and query strings are omitted.
- `E\caddy-summary.json:1`, `E\failure-overlap.json:1`, `E\server-correlation.json:1`: counts, reconstructed intervals and server source-line references.
- `E\caddy-config-projection.json:1`, `E\caddy-container.json:1`, `E\listener-snapshot.json:1`, `E\processes.json:1`, `E\remote-route.json:1`, `E\docker-settings-projection.json:1`: observed configuration and runtime projections.
- `E\collect.py:1` and `E\correlate.py:1` implement the passive log reconstruction. Rerun with `python .antiphon/task-f3b2d395/collect.py`, then `python .antiphon/task-f3b2d395/correlate.py` while the original logs remain available.
- Earlier Debug evidence: `C:\src\Antiphon\.antiphon\task-f52e06bb` (**D** below), especially `report.md:7`, `provenance.json:1`, `process-starts.json:1`, and `code-task-502-transcript.json:1`.

## Exact route and ownership

Observed route:

```text
server2-temp container
  HTTPS antiphon.desktop.codeperf.net:443
  desktop address 100.79.51.37 (previous Debug DNS/connection observation)
  Docker Desktop published :443
  desktop-caddy-1, Caddy v2.11.6
  HTTP host.docker.internal:17203 = 192.168.65.254:17203
  Docker Desktop host networking
  Windows loopback :17203, dcpctrl.exe PID 136556
  ::1:65128, node.exe PID 19688, Vite 8.0.0 preview
  /api proxy -> localhost:17202, dcpctrl.exe PID 136556
  :65122, Antiphon.Server.exe PID 310044, Kestrel
```

The live socket snapshot at **04:33:19Z** records both listening ports and a connected chain: `127.0.0.1:17203` belongs to DCP; DCP connects to `::1:65128`; Node connects to `::1:17202`; DCP connects to `::1:65122`. Both public Aspire ports, **17203 and 17202**, belong to DCP, not directly to Node or Kestrel. The private ports are observations for this process generation, not stable configuration values (`E\listener-snapshot.json:3`, `E\processes.json:1`).

The temp container's selected environment, inspected over SSH at **04:33:17Z**, is `PhoneHome__ServerOrigin=https://antiphon.desktop.codeperf.net`, `PhoneHome__RunnerId=server2-temp`; its ID is `328f856108e40be8168c3a1fb2addba4e00e06e7bca028ae013ecf5771d8a9f0`. The main `antiphon-runner-session-runner-1` container was absent, so no current main-container route is claimed. The base compose supplies the origin to server2 and the temp overlay inherits it: `docker-compose.server2-runner.yml:54`, `docker-compose.server2-runner.temp.yml:6`. The desktop address is inherited historical evidence from `D\provenance.json:15`, not a new DNS probe.

This Caddy runs in **desktop Docker Desktop**, not in server2 and not in the Antiphon repository:

| Owner / source | Evidence |
|---|---|
| `C:\src\ClaudeBot\desktop\docker-compose.yml:2` | Caddy service; lines 6–8 publish TCP 80/443 and UDP 443; lines 10–11 bind the main file and generated directory read-only. |
| `C:\src\ClaudeBot\desktop\caddy\Caddyfile:23` | Imports `/etc/caddy/generated/*.caddy`. |
| `C:\src\ClaudeBot\desktop\caddy\generated\antiphon.caddy:2` | Desktop hostname; line 3 sends **all paths** to `host.docker.internal:17203`; line 4 preserves Host. The localhost hostname repeats this at line 16. |
| `C:\src\links\src\data\machines\desktop-ktlkpif.json:16` | Antiphon's service entry specifies `"proxy":17203` for the frontend. |
| `C:\src\ClaudeBot\desktop\gen-caddy.mjs:20` | Reads that links file; line 63 generates the single upstream from `link.proxy`, without an API path distinction. |
| `Antiphon.AppHost/Program.cs:76`, `:86`, `:90` | Defines the server ingress, starts `npm run serve` with a server reference, and defines client ingress. |
| `client/vite.config.ts:17`, `:43`, `:89` | Resolves the server URL, forwards `/api` with WebSocket support, and shares the configuration with preview. |

Consequently, API traffic traverses 17203 because the hostname is configured as a frontend entry point and Vite forwards API paths from that origin. There is no separate `/api` route in Caddy. This is a configuration explanation, not evidence that the extra hops caused the stalled handshake.

Container metadata confirms `caddy run --config /etc/caddy/Caddyfile --adapter caddyfile`, the above mounts, Docker `json-file` logging, start time `2026-10-13T21:11:55.861Z`, and restart count zero. Persisted active config `/config/caddy/autosave.json`, timestamp `2026-10-13T21:12:01.019Z`, contains the same Antiphon route and no transport/load-balancing overrides. Its projection is in `E\caddy-config-projection.json:4`. No admin API/reload was used. Observed owner checkout HEADs: ClaudeBot `cab424674cfdba5de7c3361971838d20637607a2`; links `b4c1a7ccd50fc7801bd6ff2245dd7f1d6f3716be`. These identify checkouts, not cleanliness or a deployment attestation.

## Effective Caddy settings and 502 mechanism

There are no explicit overrides for the following settings in either the generated route or persisted active route. Installed version: **v2.11.6**.

| Setting | Effective value / implication |
|---|---|
| `dial_timeout` | 3 seconds. |
| `lb_try_duration` | 0; no time-budget retries. |
| `lb_retries` | 0; no count-budget retries. With both zero, `tryAgain` returns false. |
| `lb_try_interval` | Unconfigured and inactive; 250 ms is supplied only if a positive try duration is configured. |
| `lb_retry_match` | Unconfigured. The default GET rule is not an enabled retry policy; dial errors also have special eligibility distinct from post-connect errors. |
| HTTP keepalive | Enabled. |
| Idle keepalive timeout | 2 minutes. |
| Idle connections per upstream | 32; this is an idle-pool setting, not a concurrent-request ceiling. |
| Global idle / active per-host connection caps | 0 / 0, meaning unlimited at these Caddy transport settings. |
| TCP keepalive probe interval | 30 seconds. |
| Response-header / transport read / write timeout | No configured timeout; these are not the observed three-second deadline. |

Version-specific sources: [Caddy HTTP transport, lines 76–77, 98–129, 208–226, 914–937](https://github.com/caddyserver/caddy/blob/v2.11.6/modules/caddyhttp/reverseproxy/httptransport.go#L208); [retry initialization, lines 376–388, and decision, lines 1350–1390](https://github.com/caddyserver/caddy/blob/v2.11.6/modules/caddyhttp/reverseproxy/reverseproxy.go#L1350). Copies are in `E\sources`.

The reconstruction is direct: the transport wraps a failed dial in `DialError` (`httptransport.go:297`); the retry decision returns false; `statusError` returns an error at `reverseproxy.go:1652`. All 38 stored errors identify that same trace, upstream and approximately three-second duration. The wrapper embeds `error`, not the `net.Error` interface (`reverseproxy.go:1754`), which explains why this timeout follows the generic 502 path rather than the direct `net.Error` 504 branch. [Caddy error mapping](https://github.com/caddyserver/caddy/blob/v2.11.6/modules/caddyhttp/reverseproxy/reverseproxy.go#L1624).

This establishes failure before Caddy could transmit the affected HTTP request upstream. It does not locate where the TCP establishment stalled within Docker forwarding or the Windows accept path.

## What listens, backlog, and Docker limits

`C:\src\Antiphon\logs\client.state.json:2` says `mode=built`, PID 19688, `status=serving`, since `2026-10-19T15:55:44.938Z`. Process metadata identifies `node.exe ...\vite\bin\vite.js preview`, Node **v24.6.0**. It is a Node/Vite preview server serving the built bundle and proxying API requests, not an independent static-server process. `client/scripts/serve.mjs:56` defines build, preview and a separate build watcher.

Installed Vite source `C:\src\Antiphon\client\node_modules\vite\dist\node\chunks\node.js:10535` creates a normal Node HTTP server; `:10602` calls `httpServer.listen(port, host)` without a backlog argument. Preview installs the API proxy before static serving at `:33450`, then starts listening at `:33475`. No application `maxConnections`, backlog or keepalive override was found in these paths.

Node's default backlog argument is **511**, not a measured queue occupancy or a guarantee of Windows capacity. The Node server defaults to a five-second HTTP keepalive timeout plus a one-second buffer and no per-socket request-count limit. These describe the **private Vite listener**, not DCP's port 17203. [Node v24.6.0 `net.js:1937`](https://github.com/nodejs/node/blob/v24.6.0/lib/net.js#L1937), [HTTP defaults at `lib/_http_server.js:483`, `:491`, `:584`](https://github.com/nodejs/node/blob/v24.6.0/lib/_http_server.js#L483). The OS controls the actual backlog capacity. [Node listen documentation](https://nodejs.org/api/net.html#serverlisten).

**DCP's actual accept backlog and its incident-time queue occupancy remain unknown.** The running binary is Aspire orchestration package 9.3.0's `dcpctrl.exe`. AppHost does not set a backlog, and no historical accept trace is available. Assigning Node's 511 default to public port 17203 would identify the wrong listener.

Docker Desktop adds a host networking layer. Docker documents that its backend handles Windows container/host networking and creates host TCP connections. [Docker Desktop networking](https://docs.docker.com/desktop/features/networking/). The local settings projection records `NetworkType=gvisor`, `WslEngineEnabled=true`, `UseVpnkit=true`, `VpnkitCIDR=192.168.65.0/24`, and `VpnKitMaxPortIdleTime=300`. No max-connection/backlog setting was present among the inspected settings keys (`E\docker-settings-projection.json:1`).

That absence does **not** prove an unlimited forwarding implementation. No numeric forwarding limit, exhausted counter, or correlated backlog/drop diagnostic was established. Targeted reads of `com.docker.backend.exe.log`, `vpnkit-forwarder.log`, `syn-filter.log` and `nat.log` found no matching connection-limit/backlog diagnostic; numeric `17203` matches in the backend log were unrelated IPC sequence IDs. The forwarding-limit hypothesis remains open.

## Concurrency correlation: what the logs can and cannot show

**There is no Caddy access log for the requested correlation.** The Caddyfile has no access `log` directive; persisted HTTP server `srv0` has no `logs` property (`E\caddy-config-projection.json:45`). Reading all Docker output for 02:10–04:25Z yields exactly **38 `http.log.error`, two `http.acme_client`, two `tls.cache.maintenance`, and zero access entries**. Caddy explicitly disables request access logging when its server log configuration is absent. [Caddy `server.go:1178`](https://github.com/caddyserver/caddy/blob/v2.11.6/modules/caddyhttp/server.go#L1178).

All 38 errors are GET `/api/agent-tasks/{id}`, to the same upstream, with duration **3.000311070–3.004189670 seconds**. From each error's timestamp minus duration, approximate request-handler intervals overlap as follows:

- **24/38** failed requests overlap at least one other failed request.
- Peak simultaneous failed-request intervals: **3**. This is a lower bound on total ingress concurrency, not its measurement.
- At the census incident, intervals are 02:16:01.450–04.452, 02:16:02.476–05.478, and 02:16:04.130–07.130. All three overlap for about 0.322 seconds.
- No successful Caddy requests are available to supply a denominator, total arrival rate, connection reuse/new-dial rate or peak total concurrency.

The census source itself uses a synchronous `foreach` and waits for each `Invoke-RecycleRead`: `scripts/deploy-server2.ps1:697`, `:734`; that helper makes one synchronous `Invoke-WebRequest` at `:435`. One invocation therefore does not create a parallel task-detail fan-out. Independent census/owner-guard callers may overlap. The checkpoint owner guard also requests the same task-detail route (`tools/Antiphon.Checkpoints/Execution/TaskOwnerGuard.cs:165`).

The table below covers **every** error. The final column counts server-log task-detail GET arrivals within that error's reconstructed three-second interval, using `CurrentUserMiddleware` request records from `C:\src\Antiphon\server\logs\antiphon-20261020.log`, converted from +01:00 to UTC. Counts are **0–9**; they are neither ingress concurrency nor proof of a burst. They omit requests that never reached Kestrel and can include callers using other routes. Full source line/request IDs are in `E\server-correlation.json:1`.

| Failure UTC | Error ID | Duration (s) | Server task-detail GET arrivals during interval |
|---|---|---:|---:|
| 02:16:04.452 | `kan8k68fw` | 3.001215 | 2 |
| 02:16:05.478 | `hmu3gk48h` | 3.001223 | 0 |
| 02:16:07.130 | `vmb5ea3ed` | 3.000801 | 1 |
| 02:17:14.013 | `rbe9p2bbz` | 3.004190 | 9 |
| 02:36:33.279 | `f2kecwpiu` | 3.000603 | 2 |
| 02:45:07.557 | `8xx7nc9df` | 3.000384 | 0 |
| 02:51:22.234 | `qsiz443yy` | 3.000802 | 1 |
| 02:51:24.474 | `shahmutij` | 3.000611 | 1 |
| 03:02:58.643 | `inb8epskj` | 3.001011 | 2 |
| 03:06:05.796 | `gr25axfcz` | 3.000998 | 3 |
| 03:15:38.412 | `466ysgh65` | 3.000605 | 3 |
| 03:16:47.931 | `6qks7yv3n` | 3.001280 | 2 |
| 03:16:48.612 | `9ehfqmccp` | 3.000674 | 1 |
| 03:16:51.075 | `8msbt99rd` | 3.001384 | 1 |
| 03:16:53.937 | `kzpy7732k` | 3.000770 | 2 |
| 03:19:02.233 | `5798q9ica` | 3.001268 | 1 |
| 03:19:04.728 | `c9w9ccr79` | 3.000714 | 1 |
| 03:19:05.020 | `fydbwvxdc` | 3.000993 | 1 |
| 03:33:10.493 | `89qtu3fe9` | 3.000724 | 1 |
| 03:33:13.456 | `rfbe9zhge` | 3.000855 | 3 |
| 03:33:14.574 | `yr6p8ekj7` | 3.000854 | 2 |
| 03:40:10.058 | `dfzhky9dc` | 3.000690 | 2 |
| 03:40:11.785 | `ugctttdn2` | 3.001280 | 1 |
| 03:40:15.525 | `wgzepypzi` | 3.001106 | 4 |
| 03:41:33.812 | `mjyaaiaiu` | 3.000387 | 0 |
| 03:41:34.975 | `gsin4s9z4` | 3.000734 | 1 |
| 03:44:00.801 | `vdkff7mus` | 3.000311 | 4 |
| 03:46:08.836 | `sy3hkthih` | 3.000469 | 3 |
| 03:46:10.697 | `in6xxtgw4` | 3.000665 | 1 |
| 03:46:13.636 | `f8axeq3s5` | 3.001256 | 3 |
| 03:50:02.074 | `9rcqe5mtk` | 3.000606 | 3 |
| 03:56:07.462 | `sb8hw5k47` | 3.000773 | 4 |
| 04:02:58.183 | `ma7dn7far` | 3.000529 | 4 |
| 04:17:28.897 | `xgp9cas3n` | 3.000728 | 3 |
| 04:17:56.168 | `usj6q5cxw` | 3.000877 | 2 |
| 04:17:57.324 | `g2fssuvnv` | 3.000901 | 2 |
| 04:20:39.133 | `sxjvw86u2` | 3.001366 | 3 |
| 04:24:00.425 | `gvcdgm4m8` | 3.001127 | 2 |

Millisecond timestamps here are truncated from the original numeric timestamp; this explains the earlier report's rounded 04:17:56.169 versus this table's 04:17:56.168.

The census error `hmu3gk48h` targets task `783030cd-f4e7-44dd-8b98-230b1d506ff9` (`E\caddy-request-rows.jsonl:2`; `D\report.md:19` records the matching census refusal). The checkpoint errors target `7fa0910b-0f44-4607-8a4c-bb9628895544` (`E\caddy-request-rows.jsonl:34` and `:35`). Stored normalized transcript session `0b657d49-31eb-42b9-84bc-a5fc8e62484c`, sequence **14**, time **04:19:14.268Z**, explicitly records the owner's HTTP 502 checks (`D\code-task-502-transcript.json:2`).

Server 200 responses are recorded at source lines **65991** (02:16:02.496Z), **66005** (02:16:05.527Z), **126479** (04:17:26.886Z), and **126746** (04:17:59.215Z; the checkpoint task). They establish continued API service for other requests, not successful handling of the failed Caddy requests. The earlier 30/30 sequential `/api/version` probe also overlapped a task-detail failure (`D\probe-summary.json:1`, `D\caddy-during-probe.jsonl:1`); it is not recovery evidence. Process start times precede the incidents (`D\process-starts.json:1`).

## Remaining uncertainties and disposition

Confirmed: route, listener identities, defaults, absence of proxy retries/access logging, all 38 failures, overlap among failed requests, and downstream incident impact.

Unconfirmed: the packet/queue reason for failed TCP establishment; incident-time DCP backlog occupancy; any Docker forwarding ceiling; total Caddy request concurrency; whether using the other DCP listener on 17202 would avoid these timeouts. Node keepalive or a Vite stall is not demonstrated by a dial error to the earlier DCP ingress.

To resolve those uncertainties requires evidence spanning a naturally occurring failure: timestamped successful and failed ingress requests plus connection/packet observations at the Docker-to-host boundary and DCP listener, with listener queue/drop counters where available. Any eventual comparison of ingress paths must be separately authorized after the protected census window. No such collection/configuration change was started here.

The root-cause stage remains **Investigate** because the cause of the TCP stalls is unproved; the brief's requested Plan handoff would otherwise promote the backlog hypothesis into a finding. There is sufficient evidence to describe the failure mechanism, but not to select a demonstrated root-cause repair.

## Not done, noted

Candidates only, not selected or designed: `/api` to 17202 removes Vite/client ingress but retains Docker and DCP; bounded retries can mask transient failures at a latency cost; a longer dial deadline adds waiting without proving recovery; backlog tuning has no measured saturation basis. Caddy route/retry/deadline changes belong to external ClaudeBot/links operational configuration and require the operator's go before live application; an Antiphon Vite/AppHost change would be repo code, while DCP/Docker/OS tuning is operational. The smallest safe root-cause fix is not established by this evidence.

--- next stage ---
next: investigate
handoff: Establish why Caddy TCP dials to Docker-host/DCP port 17203 stall; access logging was absent, Vite listens privately on 65128, and the census is sequential. Obtain failure-time connection/queue evidence after the protected census window; coordinate with CARD-1374 and the ClaudeBot/links config owners.
artifact: docs/investigations/2026-10-20-card-1378-caddy-ingress-502.md
