# How server2 phones home to the desktop server

Investigate task c7653f3f, 2026-11-02, base `4bd1473f0b59620db8832eb781a57a8d1da6e78c`. Read-only.
No build, test or host change was made. Secret values were not read or printed.

## Answer

server2 reaches the desktop through the same public HTTPS name the UI uses,
`https://antiphon.desktop.codeperf.net`. On the tailnet that name resolves to the desktop's Tailscale
address, `100.79.51.37`, on port 443. The connection goes from server2 to Tailscale, then Caddy on
:443, then the Vite server on `127.0.0.1:17203`, whose `/api` proxy has `ws: true`, then Kestrel
behind Aspire's 17202. The runner opens every connection. The desktop never connects to server2. No
Antiphon port is reachable from outside the desktop, and server2 does not need one. A Windows
laptop on the tailnet can already use the same route. The operator's two probes returned 404
because they asked for paths that do not exist.

## 1. How the server2 runner reaches the desktop

- **Direction:** the runner connects out to the server. The runner container publishes no port:
  `docker-compose.server2-runner.yml:164` says "No published port. The container phones home."
  It is hosted by `PhoneHomeConnectionService`, registered at
  `src/Antiphon.SessionRunner/Program.cs:97`.
- **Transport:** there are two steps, both on one origin
  (`src/Antiphon.SessionRunner/PhoneHomeConnectionService.cs:106-162`).
  1. An HTTP `POST {origin}/api/session-runners/register` sends the shared secret in the
     `X-Antiphon-Runner-Secret` header (`:113-131`;
     `src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs:10,13`). The reply carries a
     short-lived ticket and the server-owned epoch.
  2. A raw WebSocket (`ClientWebSocket`, not SignalR or gRPC) connects to
     `wss://{origin authority}/api/session-runners/server2/connect`. It sends the ticket in
     `X-Antiphon-Runner-Ticket` (`:135-144`; `PhoneHomeContracts.cs:11,14`). `wss` is used because
     the origin's scheme is https (`:140`). After that, JSON frames go both ways: Request, Result,
     Error, Event, Heartbeat (every 15 s) and EventWindow (`PhoneHomeContracts.cs:20,495-503`).
- **Origin in use:** `https://antiphon.desktop.codeperf.net`, with no port, so 443.
  - This investigation runs as a child of the server2 runner. Its inherited environment has
    `PhoneHome__ServerOrigin=https://antiphon.desktop.codeperf.net` and
    `PhoneHome__RunnerId=server2`.
  - The same origin appears in `docker/stack.env.example:11` and
    `docs/investigations/2026-09-25-card-0716-phone-home-drops.md:35`.
  - From inside the runner, `getent hosts` resolves both `antiphon.desktop.codeperf.net` and
    `desktop.codeperf.net` to `100.79.51.37`. The DNS search domain is `tail62cf02.ts.net`.
- **Route:** the traffic goes over Tailscale to Caddy. It does not use a Cloudflare tunnel or a
  published Antiphon port.
  - The name is DNS-only to a Tailscale IP, so Cloudflare's proxy is not on the path (CARD-0716
    investigation, `:35`).
  - The CARD-0604 plan measured the route from server2
    (`docs/superpowers/plans/2026-09-22-card-0604-persistent-runner-dind-plan.md:81`):
    `http://100.79.51.37:17202/api/version` was refused, and
    `https://antiphon.desktop.codeperf.net/api/version` returned 200. A WebSocket upgrade through
    Caddy reached Kestrel.
  - The plan rejected the direct Tailscale `:17202` origin (`:118-121,143`).
  - The Cloudflare tunnel is only for the operator's SSH to server2. The runner does not use it.
- **Configuration keys on server2:**
  - Compose environment (`docker-compose.server2-runner.yml:52-64`):
    - `PhoneHome__Enabled`.
    - `PhoneHome__RunnerId: server2`, hard-coded at `:53`.
    - `PhoneHome__ServerOrigin`, read from `${PHONE_HOME_SERVER_ORIGIN}` (`:54`).
    - `PhoneHome__SecretPath=/run/antiphon/phone-home`.
    - `ANTIPHON_PHONE_HOME_SECRET_SOURCE=/run/secrets/phone-home`.
  - The compose secret `phone-home` is the file named by `PHONE_HOME_SECRET_FILE` (`:194-195`).
    `deploy-parent` generates it once on the server2 host as `secrets/phone-home`, mode 0600
    (`docs/docker-stack.md:1112`; example path in `docker/stack.env.example:14`). The entrypoint
    copies it to tmpfs as 0400, owned by uid 1654.
  - Both variables live in `/home/mc/antiphon-server2/secrets/stack.env` on server2
    (`docs/investigations/2026-10-17-server2-recycle-recovery.md:70`).
- **Server-side keys:** these are .NET user-secrets in the `antiphon-server` store, not the
  AppHost's (`docs/testing-and-build.md:968-983`):
  - `PhoneHomeRunner:Enabled=true`.
  - `PhoneHomeRunner:Runners:server2:{SharedSecret, CallbackOrigin=https://antiphon.desktop.codeperf.net, RunnerWorkspace, RunnerRepository, MaxCapacity, ...}`.
  - `server2-temp` repeats the same values, including the same secret
    (`docs/agent-credentials.md:327-328`).
  - `server/appsettings.json:60-62` ships `Enabled: false`.
- **Authentication:**
  - The server checks the secret header against that runner's `SharedSecret` with
    `CryptographicOperations.FixedTimeEquals`
    (`server/Api/Endpoints/SessionRunnerEndpoints.cs:45-57`;
    `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs:253-265`).
  - It refuses a runner id that is not in the `Runners` map (`PhoneHomeRunnerDirectory.cs:271-272`).
  - The connect route requires a real WebSocket upgrade and a valid ticket
    (`SessionRunnerEndpoints.cs:277-293`).

## 2. How the desktop reaches the server2 runner

All desktop-to-runner traffic uses the WebSocket that the runner opened.

- `PhoneHomeRunnerDirectory.Resolve` returns a `PhoneHomeRunnerClient` built on the live
  connection. With no open socket it throws "Phone-home runner is unavailable" or "connection is
  closed" (`PhoneHomeRunnerDirectory.cs:156-169`).
- Every call becomes `_connection.RequestAsync(PhoneHomeOperation.X, ...)`
  (`server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs:19-312`). That covers:
  - Launch, List, Get, Buffer and Snapshot.
  - Workspace operations.
  - Health, HostStats, Capabilities and Retire.
- Session output and state changes come back as Event frames on the same socket.
- `SessionRunnerHttpClient` is used only for the desktop's own runner: `SessionRunner:BaseUrl`,
  which the AppHost sets to `http://localhost:17204` (`Antiphon.AppHost/Program.cs:69`).
- `CallbackOrigin` runs the other way. It becomes `ANTIPHON_API` for sessions launched on the
  runner (`server/Application/Services/PhoneHomeLaunchPolicy.cs:250`), so agents on server2 call
  the same `https://antiphon.desktop.codeperf.net/api/...`. This session's `ANTIPHON_API` has that
  value.

## 3. Bindings and the Caddy route

- **17202 (server):** `.WithHttpEndpoint(port: 17202, env: "ASPNETCORE_HTTP_PORTS")`
  (`Antiphon.AppHost/Program.cs:82`). This is an endpoint proxied by Aspire 9.3 DCP, with no
  target host set.
  - DCP's proxy listens on localhost by default. That default comes from Aspire, not from this
    repo, and is inferred.
  - It matches the measured refusal on `100.79.51.37:17202`.
- **17203 (client):** `.WithHttpEndpoint(port: 17203, env: "VITE_PORT")` (`Program.cs:96`). The
  shared Vite `server`/`preview` config sets no `host`, so Vite binds localhost
  (`client/vite.config.ts:27-55`).
  - `allowedHosts` includes `.desktop.codeperf.net` (`:33`).
  - `/api` and `/hubs` are proxied to the Aspire server URL with `ws: true` (`:43-54`). The
    comment at `:35-42` explains that `ws: true` exists for the phone-home connect upgrade.
- **17204 (runner):** `--urls http://localhost:17204` (`Program.cs:35`).
- **17205 (dashboard):** `http://127.0.0.1:17205` (`Antiphon.AppHost/Properties/launchSettings.json`).
- **Caddy config:** it is not in this repo. It lives on the desktop as a generated file,
  `C:\src\ClaudeBot\desktop\caddy\generated\antiphon.caddy`, served by the `desktop-caddy-1`
  container.
  - The vhost is
    `antiphon.desktop.codeperf.net { reverse_proxy host.docker.internal:17203 { header_up Host {host} } tls {dns cloudflare} }`.
  - It is generated by `gen-caddy.mjs` from `C:\src\links\src\data\machines\desktop-ktlkpif.json:16`
    (`docs/superpowers/plans/2026-08-27-card-0216-remote-home-load-and-post-load-spinner-plan.md:37-42`;
    CARD-0716 investigation `:35`).
  - Caddy has no separate `/api` route. It sends everything to 17203, and Vite forwards `/api` and
    `/hubs`, including WebSockets, to Kestrel.
  - Kestrel therefore sees loopback (`docs/ops-http.md:148`; `server/Program.cs:1245`).
- **Why the operator's probes returned 404:** neither route exists.
  - Health is `/health`, not `/api/health` (`server/Program.cs:1182`). Through the vhost, `/health`
    goes to Vite rather than Kestrel, because Vite proxies only `/api` and `/hubs`.
  - The runner catalogue is `/api/session-runners`, not `/api/runners`
    (`SessionRunnerEndpoints.cs:33`).
  - Both requests reached Kestrel through the Vite proxy and ended at `MapFallbackToFile("index.html")`
    (`server/Program.cs:1259`).
  - Correct probes: `GET https://antiphon.desktop.codeperf.net/api/version` (200 from server2,
    CARD-0604 plan `:81`) and `/api/session-runners`.

## 4. What a new remote runner on a Tailscale laptop needs

Neither option the operator proposed is needed:

- (a) A new Caddy route is not needed, because the existing vhost already carries `/api` and the
  WebSocket.
- (b) Binding 17202 to Tailscale and opening the firewall is the route the CARD-0604 plan
  rejected (`:143`).

The repository's own pattern is the one server2 uses:

1. **Origin:** `PHONE_HOME_SERVER_ORIGIN=https://antiphon.desktop.codeperf.net`. The register call
   goes to `https://antiphon.desktop.codeperf.net/api/session-runners/register` with header
   `X-Antiphon-Runner-Secret: <redacted>`. The socket goes to
   `wss://antiphon.desktop.codeperf.net/api/session-runners/<laptop-id>/connect`. The operator has
   already shown that the laptop resolves the name to `100.79.51.37` and reaches 443.
2. **Desktop server:** add a `PhoneHomeRunner:Runners:<laptop-id>` entry to the `antiphon-server`
   user-secrets, with its own `SharedSecret` and
   `CallbackOrigin=https://antiphon.desktop.codeperf.net`, then restart the AppHost. An id that is
   not in the map is refused at registration.
3. **Runner id:** the runner must not reuse `docker-compose.server2-runner.yml` unchanged, because
   `PhoneHome__RunnerId` is hard-coded to `server2` there (`:53`).
4. **Laptop platform caveat (affects scope):** the phone-home runner is a Linux-container design.
   - `PhoneHomeSettings.Validate` refuses non-POSIX `AllowedCwd`, `RunnerRepository`,
     `ClaudeHome` and `CodexHome` (`src/Antiphon.SessionRunner/PhoneHomeSettings.cs:111-130`).
   - Server entries default to `/work` paths and `/bin/sh` allow-lists
     (`server/Application/Settings/PhoneHomeRunnerSettings.cs:19-34`).
   - A Windows laptop would therefore run the runner as a Linux container under Docker Desktop
     with a laptop runner id, not as a native Windows SessionRunner.
   - Whether a native Windows phone-home runner works end to end was not examined.
   - CARD-0812 (runner hard-wired to one repo) also applies.

## Remaining uncertainty

- The DCP proxy binding to localhost on 17202 is inferred from Aspire's default and the measured
  refusal. It was not read from DCP configuration.
- The desktop Caddy file was cited from earlier investigations (2026-08-27, 2026-09-25). It was not
  re-read today, because it is not in the repo and the desktop was off-limits.
- The exact body of the operator's 404 responses was not captured.
- This task made one read-only call through `ANTIPHON_API` (`card.ps1 search`), and it succeeded.
  That is a live witness today that `/api` on the vhost reaches Kestrel from server2.

## Not done, noted

- Fix idea: write the laptop-runner recipe (a compose file with a parameterised runner id, plus the
  user-secrets entry) as a Plan, and correct the probe paths in the operator's checklist.
