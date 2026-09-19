# CARD-0575 — Docker Grok worker that phones home

**Status:** five brief questions confirmed by live container measurements on the desktop (2026-09-19) plus the current tree. Not a product bug; feature inventory. Investigate task: `4fe68cac`. Card: CARD-0575.

**Date:** 2026-09-19. Host: Docker Desktop 4.77.0, engine 29.5.3, `OSType=linux`, context `desktop-linux`. Live API `GET /api/version` SHA `1044eef74db59426d69356a2d0d9ec03d2b9a967` (`land-v2`). Live `antiphon-postgres` healthy on host **17280** for 3 days.

Operator locks (not reopened): host-socket is Option A; credentials are not baked into an image.

Evidence directory: [2026-09-19-card-0575-evidence/](2026-09-19-card-0575-evidence/). Commands: `measure.ps1` / `measure-followup.ps1`.

Related: CARD-0490 (protocol / `docker exec` v0 / lifecycle), CARD-0038 (POSIX PTY / Linux runner). Out of scope: implementing, privileged DinD, putting the AppHost in Linux.

## Verdict

A Linux worker container on this desktop reaches Antiphon at **`http://host.docker.internal:17202`**. That origin returns `/health` (`Healthy`) and `/api/boards` (JSON). `https://antiphon.desktop.codeperf.net` is the **client** front door (Caddy → host **17203**). Its `/health` is the SPA HTML, not the API health. `/api/*` happens to reach Kestrel only because Vite proxies `/api` to 17202.

Grok in Docker is the **Linux** `grok` ELF (`linux-x86_64`, 1.0.34), not host `grok.exe`. Antiphon still launches `grok.exe` (`server/appsettings.json`). The host session-runner has **no** `docker exec` path. `docker exec -t` from the Windows host does give a Linux PTY and can inject `ANTIPHON_API`; that is enough to **prove** Grok-binary + callback + host-socket without an in-container runner. A runner **inside** the container still needs CARD-0038. **v0 can proceed without CARD-0038** if the host runner stays on Windows ConPTY and wraps `docker exec`.

Auth survives restart / replace / rebuild only when `GROK_HOME` is a **host bind-mount or a named volume**. An unmounted new container has no `auth.json`. Another machine does not see that volume.

Host-socket canary: `docker info` and `docker run --rm hello-world` from `docker:cli` with `/var/run/docker.sock` both succeeded. Binding host **17280** from inside that worker is refused (`port is already allocated`); live `antiphon-postgres` stayed healthy.

## 1. Phone-home — which origin, what `ANTIPHON_API`

Measured from `alpine:3.20` on the desktop, `host.docker.internal` = `192.168.65.254`.

| Origin from inside the container | `/health` | `/api/boards` | `/api/version` |
|---|---|---|---|
| `http://host.docker.internal:17202` | **200** `text/plain` `Healthy` | **200** `application/json` boards array | **200** JSON SHA `1044eef7…` |
| `http://host.docker.internal:17203` | n/a (GET `/` = **403** Vite `allowedHosts`) | (not hit; Host is `host.docker.internal`) | — |
| `https://antiphon.desktop.codeperf.net` | **200** `text/html` SPA (same etag as `/`) | **200** JSON via Caddy → 17203 → Vite `/api` proxy | **200** JSON Kestrel |

Same split on the host with `curl --resolve antiphon.desktop.codeperf.net:443:127.0.0.1`. Caddy `/api/boards` body is the live boards JSON (5936 bytes, Antiphon board `8988ca03-…`).

Config that produces the Caddy row:

- `C:\src\links\src\data\machines\desktop-ktlkpif.json` — Antiphon `proxy: 17203`
- `C:\src\ClaudeBot\desktop\caddy\generated\antiphon.caddy:2-5` — `reverse_proxy host.docker.internal:17203`
- `client/vite.config.ts:33-37` — `proxy['/api'].target` = the server URL (17202)

`ANTIPHON_API` a desktop Linux worker must use: **`http://host.docker.internal:17202`**.

Why not the Caddy URL as the API origin:

- Injected identity is `$ANTIPHON_API/health` and `$ANTIPHON_API/api/…` (`docs/ops-http.md`, `scripts/card.ps1`, `scripts/delegate.ps1`). Caddy `/health` is not API health.
- `/api` through 17203 depends on the Vite proxy remaining up. Direct 17202 does not.
- Default `DelegationSettings.ApiBaseUrl` is `http://localhost:17202` (`server/Application/Settings/DelegationSettings.cs:317`), stuffed into ExtraEnv as `ANTIPHON_API` (`AgentSessionLaunchComposer.cs:47`, `AgentTaskDispatcher.cs:4494`). `localhost` inside the container is the container, not the host. That default would miss home.

`docker exec -e ANTIPHON_API=http://host.docker.internal:17202 … wget $ANTIPHON_API/health` → **200 Healthy** (`60-docker-exec-tty-callback.txt`).

From another machine, 17202 is not the Tailscale path (card text; not re-measured from the laptop). That machine would need whatever public/VPN URL actually serves `/api` **and** `/health` as the API, not the client vhost. That is CARD-0490's remote half.

## 2. Grok in Docker — binary, home, exec vs runner, PTY

### Binary

| Where | What |
|---|---|
| Host | `C:\Users\lndco\.grok\bin\grok.exe` PE (`MZ`), 151 293 256 bytes, `grok 1.0.34 (3736acbc8658) [stable]` |
| Linux container | Official `curl -fsSL https://x.ai/cli/install.sh \| bash` installs **`grok` + `agent`** → `~/.grok/downloads/grok-linux-x86_64`, version **1.0.34 (3736acbc8658)** |
| ELF | `ELF 64-bit LSB pie executable, x86-64, static-pie linked, stripped`. `--version` ran on **both** `debian:bookworm-slim` and `alpine:3.20` |
| `grok.exe` in Linux | Bind-mounted into alpine and exec'd: WSL `UtilBindVsockAnyPort` error, no version. Not a Linux binary |

Antiphon still names the Windows file:

- `server/appsettings.json:91-96` — `"Exe": "grok.exe"`
- `docs/agent-kinds.md:37,141` — program `grok.exe`

There is no Linux exe swap in the launcher.

### `GROK_HOME` / `auth.json`

Default `C:\Users\lndco\.grok`. `auth.json` present (1734 bytes, mtime 2026-09-19 13:47 +01; **content not logged**). `auth.json.lock` present (16 bytes). `GrokCredentialStore.ResolveGrokHome` / `Inspect` (`src/Antiphon.Agents.Pty/GrokCredentialStore.cs:24-72`) and `GrokTranscriptTailer.ResolveGrokHome` (`src/Antiphon.SessionRunner/GrokTranscriptTailer.cs:122-134`) both look at the **process-local filesystem**: launch env `GROK_HOME`, else process env, else `~/.grok`. CARD-0324 fail-fast is that host-side probe.

Grok 1.0.34 help still has `agent stdio` (ACP). Parked in `ideas.md`; not the default here. Linux grok TUI **does** start on a container PTY (below), so ACP is not required to unblock v0.

### Host runner vs `docker exec`

`src/Antiphon.SessionRunner` and `src/Antiphon.Agents.Pty` contain **zero** `docker` references. The runner spawns via ConPTY / Porta (`PtyAgentRunner.cs:163-165` → `ModernConPtyConnection.Spawn` or `PtyProvider.SpawnAsync`). Live runner answers `GET http://127.0.0.1:17204/sessions` 200.

Measured v0 shape from this Windows host:

- `docker exec` (no `-t`): `tty=not a tty`
- `docker exec -t`: `tty=/dev/pts/0`, `stdin_tty=yes`
- `docker run -t` + dummy `GROK_HOME=/tmp/gh`: Linux grok TUI enables mouse/bracketed-paste, OSC title `grok`, then timeout. Without `-t`: `Error: No such device or address (os error 6)`
- Alpine image has `wget`, **no** `pwsh` / `powershell`. `card.ps1` / `delegate.ps1` do not exist there. HTTP callback does.

### CARD-0038 vs v0

`PtyBackendPolicy.Resolve` has two states (`InboxConhost`, `ModernConPty`). `ConPtyRedistributable.TryLocate` on non-Windows returns false with `"not Windows — there is no pseudoconsole to redirect"` (`ConPtyRedistributable.cs:81-84`) and the policy then **falls back to InboxConhost ceilings** (`PtyBackend.cs:91-94`) — the CARD-0038 slice-1 bug, still present. `WindowsJobObject` / `ModernConPtyConnection` are `IsWindows()`-gated. Tests: 95 `SkipIfNotWindows` hits. Repo `Dockerfile` publishes the **server**, not a worker (`ENTRYPOINT ["dotnet", "Antiphon.Server.dll"]`).

**v0 can proceed without CARD-0038** if the session-runner stays on Windows and the child it ConPTY-spawns is `docker.exe exec -t … grok` (Linux binary, bind-mounted `GROK_HOME`). CARD-0038 is the prerequisite for a runner **inside** the Linux container.

Seam if the host tailer stays on Windows: `GrokTranscriptTailer.ResolveUpdatesPath` does `Uri.EscapeDataString(Path.GetFullPath(cwd))` on the **runner OS** (`GrokTranscriptTailer.cs:87-104`) and `SessionRunnerRuntime` binds that path up front (`SessionRunnerRuntime.cs:1959-1979`). A Linux grok cwd `/work` will not encode the same as a Windows `C:\…` cwd. GUID locate exists (`TryLocateSessionDirectory`) but the PTY tailer does not use it for the bound path.

## 3. Auth survival — restart / replace / rebuild / other machine

Throwaway dummy `auth.json` (`{"marker":"CARD0575-DUMMY-NOT-A-SECRET"}`), **not** the real store. Bind-mount host temp dir → `/opt/grok-home`.

| Event | Bind-mount of `GROK_HOME` | Named volume `card0575-grokhome` | No volume |
|---|---|---|---|
| (a) `docker restart` | dummy + `wrote-from-a.txt` survived | (same Docker volume semantics) | n/a |
| (b) `docker rm` + new container, same image | survived | survived (second `docker run --rm -v` printed the same marker) | `auth.json` absent (`/root/.grok` missing) |
| (c) `docker build` new image id `card0575-dummy:a` → `:b` (`sha256:9c8b0c05…` → `c98f95e7…`) | survived; `/image-id.txt` changed A→B | would survive (volume is not in the image) | lost |

Forbidden path (not run): `COPY auth.json` into a Dockerfile. A rebuild would snapshot whatever was copied; rotating login would require a new image; the file would be in the layer history.

Docker secrets / tmpfs: not measured as a Grok login. They are the wrong shape for an OAuth store Grok **rewrites** (`auth.json` mtime on the host moves; CARD-0324 treats a missing file plus leftover lock as a cleared store).

Claude / Codex: not separately canaried. Same Docker rule applies to `~/.claude` and `~/.codex` / `CODEX_HOME` — persist the **home directory** as a bind-mount or named volume, never bake it. `XAI_API_KEY` / gkp remains opt-in and switches billing (`docs/agent-credentials.md`); not used here.

**Other machine:** bind-mount is a path on **this** NTFS; named volume is **this** Docker Desktop VM (`DockerRootDir=/var/lib/docker`). Recreating the container elsewhere without copying that volume has no `auth.json`. Whether a copied `auth.json` is accepted by Grok on a second host was **not** measured (would mean copying the real OAuth file).

## 4. Host-socket canary and port 17280

From `docker:cli` with `-v /var/run/docker.sock:/var/run/docker.sock` (Docker Desktop Linux engine; this is the Windows equivalent of the Unix socket, not `\\.\pipe\docker_engine` — the engine `OSType` is `linux`):

- `docker info` → `Name=docker-desktop OSType=linux ServerVersion=29.5.3`
- `docker ps --filter name=antiphon-postgres` → `0.0.0.0:17280->5432/tcp`
- `docker run --rm hello-world` → full Hello-from-Docker text (pulled `hello-world:latest`)

Collision (did **not** start Postgres; bind only):

```
docker run --rm --name card0575-steal17280 -p 17280:5432 alpine:3.20 true
```

Daemon: `Bind for 0.0.0.0:17280 failed: port is already allocated` (`32-port-17280-collision.txt`). `antiphon-postgres` still `Up 3 days (healthy)` on `17280`. Compose that wants host 17280 is refused by the host daemon; it does not silently share the live DB. Mitigation is remap / refuse / dedicated network (Plan), not DinD.

Socket is root-equivalent on the host (Option A cost; not re-tested beyond the canary).

## 5. v0 `docker exec` vs full phone-home / in-container runner

Enough to **prove** the three canaries without a runner process in the container:

| Canary | Proved by | Still missing |
|---|---|---|
| Grok in Linux Docker | install.sh + `--version` 1.0.34; TUI starts on `/dev/pts/0` | One **authenticated** turn visible as an Antiphon session/task (v0 canary slice; FakeClaude is not it) |
| Callback | `docker exec -e ANTIPHON_API=http://host.docker.internal:17202` → `/health` 200 | `card.ps1` (no pwsh in the image) |
| Host-socket docker | `docker info` + `hello-world` + 17280 refusal | compose remap policy |

A runner **inside** the container is **not** required for that local proof. It **is** required for CARD-0490 remote phone-home (another machine, no local `docker.exe`). Today's host runner cannot `docker exec`; that launch shape does not exist.

`card.ps1` in Debian is a CARD-0038 follow-on (PowerShell). HTTP to `ANTIPHON_API` is enough for a Grok worker that `wget`/`curl`s the board.

## Remaining uncertainties

- Authenticated Grok turn + `updates.jsonl` bind with a **Linux** cwd vs host `ResolveUpdatesPath` (Windows `Path.GetFullPath`). Not run: would mount the live `GROK_HOME`.
- Whether ConPTY wrapping `docker.exe exec -t` delivers Grok's composer the same as wrapping `grok.exe` (paste / DA1 / `--no-alt-screen`).
- Alpine `ldd` showing `ld-musl` on a `file`-reported static-pie; `--version` worked on alpine and debian anyway. Worker base image still a Plan choice.
- OAuth `auth.json` copied to a second machine.
- Windows-container mode (`OSType=windows`) — not the engine on this box.

## Not done, noted

Plan should pick host-runner `docker exec -t` of Linux `grok` with bind-mounted `GROK_HOME` + `ANTIPHON_API=http://host.docker.internal:17202` for v0, and treat CARD-0038 as the in-container-runner follow-on, not a v0 gate.
