# Bootstrapping Antiphon

A brand-new Windows machine or a fresh clone. Aspire is the path. Every
automatable step already has a tracked script — this file sequences them.
Do not copy `appsettings.json.example` over the tracked
`server/appsettings.json`.

## What "done" means

- The Aspire stack answers `/health` on **17202** (API), the Vite client is
  on **17203**, and the session-runner is on **17204**. Postgres is the
  always-on `antiphon-postgres` container on **17280**. The Aspire dashboard
  is pinned at **http://localhost:17205**.
- `pwsh -File scripts/bootstrap-check.ps1` exits 0.
- You can create an agent in the UI (or `POST /api/agents` then
  `POST /api/agents/{id}/start`) and it reaches **Running**.

Cards, boards, and agents from some other deployment are **not** part of
done. A fresh database correctly contains only the seeder: admin user, BMAD
templates, empty-key LLM provider rows, model routing. Telegram / Redpanda /
channel bind is a separate stand-up — see
[telegram-bot-ops.md](telegram-bot-ops.md).

## Which scenario is this?

Pick one before touching the machine.

### (a) This operator, new machine

`~/.claude` is a git checkout of this operator's private `claude-home` repo
(not a public clone URL — a stranger cannot use it). On the new box: clone
that repo into `~/.claude`, run the `sync` skill, then the machine steps
below. Recurring build-junk cleanup is Windmill on server2
(`u/lndcobra/antiphon_build_junk_cleanup`, Mon 09:00 Europe/London), not a
Windows Scheduled Task — do not re-add a local task.

### (b) New operator

A clone of this repo already has the project skills under
`.claude/skills/`: `antiphon-delegate`, `antiphon-run`, `antiphon-start`,
`telegram-e2e-smoke`, `claude-web`, and the vendored `bmad-*` set. That is
enough to start, stop, and delegate inside Antiphon.

A new operator does **not** get the operator-environment pieces that live
only in `claude-home` / the user profile. Named so the absence is visible:

- Global `~/.claude/CLAUDE.md` and its `@`-imports (`browser-harness`,
  `bitwarden`, `docker-desktop`).
- The rebase-only git policy (`git pull --rebase`; no merge commits).
- The memsearch plugin.
- Always using browser-harness for UI verification (user-level skill).
- Browser-test CDP against the logged-in desktop browser (user-level).
- `~/.claude/commands/rc-status.md` (remote-control LIVE/DROPPED/NO-RC).
- Grok / Claude MCP registrations (see [MCP](#mcp-optional) — none of them
  are required for "done").

### (c) Fresh deployment vs migration

- **Fresh:** seeder-only DB is correct. Create the first agent as step 9.
  Do not invent seed data.
- **Migration:** on the old box run `.\dev-backup.ps1` (defaults to
  `<repo>/backups/`). On the new box run
  `.\dev-restore.ps1 -BackupFile <path-to.zip>`. Also restore the Data
  Protection ring (`%LOCALAPPDATA%\Antiphon\DataProtection-Keys`) *with*
  that dump — see [ai-agent-tui-configuration.md](ai-agent-tui-configuration.md).
  Do not inline `pg_dump`. `.\dev-fresh.ps1` is the nuclear reset (volume +
  `C:\Antiphon\worktrees`), not a bootstrap step.

## Machine steps

Dependency order. Each step points at an existing script; do not reimplement
them here.

### 1. Prerequisites

| Need | Detail |
|---|---|
| Docker Desktop | Running, tray icon idle. Compose file is `docker-compose.dev.yml`, never the default `docker-compose.yml`. |
| .NET SDK | The version `global.json` pins (today **10.0.204**, `rollForward: latestMinor`). Projects target `net9.0` — that is not a contradiction. |
| Node.js | 20+. |
| pwsh 7 | Via the version-independent alias `%LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe`. Never bake a version-pinned MSIX `pwsh.exe` path into a Scheduled Task (that path vanishes on the next PowerShell update). |
| `claude.exe` and/or `grok.exe` | On PATH and logged in as the Windows user. Wrapper-managed TUI auth is how agent sessions authenticate. Empty `Llm:Providers:*:ApiKey` does **not** block a TUI agent. |

### 2. Clone and Windows convention directories

Clone this repo. Then create the directories if they are missing (the
tracked configs assume them; they are outside the repo on purpose):

```
C:\Antiphon\worktrees
C:\logs\antiphon\session-runner
C:\logs\antiphon\check-interpreter
```

`src/Antiphon.SessionRunner/appsettings.json` sets
`SessionLogPath = C:\logs\antiphon\session-runner`. Create the directory;
do not "fix" that path.

### 3. Secrets

See the [secrets table](#secrets) below. Preferred overlay for a new
machine: `dotnet user-secrets set` against id `antiphon-server`. Also
accepted: a gitignored `server/appsettings.Development.json`. **Never**
the tracked `server/appsettings.json`.

### 4. Postgres (and Redpanda)

```
docker compose -f docker-compose.dev.yml up -d
```

Brings up `antiphon-postgres` on **17280** and Redpanda on **19092**. A
fresh machine is on that local Redpanda and needs nothing — the AppHost
only forwards a live broker when `AntiphonMessaging:BootstrapServers` is
set in the AppHost's own user-secrets (or gitignored
`Antiphon.AppHost/appsettings.Development.json`). Only Postgres is
required for "done". Channel bridge stays `Enabled: false` in the tracked
file (AppHost forces it on). Telegram / Kafka is not part of `/health` —
[telegram-bot-ops.md](telegram-bot-ops.md).

### 5. First build

```
dotnet build
cd client
npm install
npm run build
```

The E2E fixture (`AntiphonAppFixture.EnsureClientBundleIsCurrent`) hard-fails
when any `client/src` file is newer than `client/dist/index.html`. Build
`client/` once so that check starts green.

### 6. Always-on Scheduled Tasks

```
pwsh -File scripts/install-autostart.ps1
```

Registers **Antiphon Session Runner** (port 17204), **Antiphon AppHost**
(server 17202, client 17203, dashboard 17205, control API 17207), the
**Antiphon AppHost Watchdog**, and the independent **Antiphon AppHost
Watchdog State Observer** (CARD-0245). Caveats already in the script
header / AGENTS.md:

- Re-running the installer Unregister+Registers, which **terminates a
  running session-runner**. Use `-AppHostOnly` to refresh AppHost-side
  tasks (logon AppHost + watchdog + watchdog-state observer) without
  touching a healthy runner.
- `-NoWatchdog` skips the restarting watchdog only. The state observer
  stays registered so a disabled watchdog is still visible. Skip the
  observer with `-NoWatchdogStateObserver`. `-NoAppHost` skips all
  AppHost-side tasks including the observer.
- The installer prefers the version-independent
  `%LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe` alias. Never hand-register
  a `WindowsApps\Microsoft.PowerShell_<version>_…\pwsh.exe` path.
- Leave the stack down on purpose with
  `pwsh -File scripts/set-apphost-maintenance.ps1` (writes
  `logs/apphost.down-on-purpose`, then disables the watchdog). `-Clear`
  re-enables then removes the marker. Direct `Disable-ScheduledTask` still
  works and is what the observer detects as unintentional.

Already registered and you just want them up now:

```
Start-ScheduledTask -TaskName "Antiphon Session Runner"
Start-ScheduledTask -TaskName "Antiphon AppHost"
Start-ScheduledTask -TaskName "Antiphon AppHost Watchdog State Observer"
```

### 7. Start the Aspire stack

If an AppHost may already exist (including the logon task from step 6):

```
pwsh -File scripts/restart-apphost.ps1
```

That kills the old AppHost tree, frees the ports, relaunches, and waits for
health. It preserves the session-runner. **Never** launch a second bare
`dev-aspire.ps1` — the old server keeps the ports and the new code never
goes live.

If you are certain nothing is listening yet, first launch only:

```
Start-Process pwsh -ArgumentList @('-NoLogo','-File','<repo>\dev-aspire.ps1') -WindowStyle Normal
```

Never `wt new-tab` (fails `0x80070002` when the title has a space). Never
`-NoNewWindow` (attaches the AppHost to the tool session and kills it when
the session ends). The script exits after ~60s; the AppHost continues in
the background (`logs/apphost.pid`).

Port 17203 serves the built bundle by default (CARD-0216) — the client
resource's first start builds it itself via `client/scripts/serve.mjs`;
nothing extra to run beyond step 5's `npm install`. See AGENTS.md's
"Client serving mode" note if you want live HMR instead
(`client-mode.ps1 -Mode dev`).

### 8. Self-check

```
pwsh -File scripts/bootstrap-check.ps1
```

Read-only. Exit code is the number of FAILs. This is the last automated
step; do not skip it. (The script is slice 2 of CARD-0088; it wraps
`verify-dev-stack.ps1 -SkipBrowser` for the live half.)

### 9. Create the first agent

The seeder did not do this. In the UI at http://localhost:17203 create an
agent and start it, or:

```
POST /api/agents
POST /api/agents/{id}/start
```

"Done" is that agent reaching **Running**.

## Secrets

Never print a secret value. Config keys stay empty in the tracked file.

| Key / secret | Lives today | New machine |
|---|---|---|
| `Llm:Providers:anthropic:ApiKey` / `openai:ApiKey` | Database (Settings UI). Tracked config empty. `dotnet user-secrets list --id antiphon-server` empty. No matching Bitwarden item on 2026-08-19. | `dotnet user-secrets set "Llm:Providers:anthropic:ApiKey" … --id antiphon-server` (preferred) or Settings UI after first start. Seeder copies a non-empty config key into the DB. Also accepted: gitignored `server/appsettings.Development.json`. Never the tracked `server/appsettings.json`. Empty keys do not block a TUI agent. |
| `GitHub:PersonalAccessToken` | Tracked file empty, `Enabled: false`. | Optional. Same user-secrets id if wanted. |
| Agent TUI wrapper auth | `~/.claude` (Claude), `~/.grok/auth.json` (Grok). | Log the TUI in as the Windows user. |
| Agent TUI managed secrets + Data Protection ring | DB ciphertext + `%LOCALAPPDATA%\Antiphon\DataProtection-Keys`. | Fresh ring is expected; re-enter managed secrets. On a migration, back up the ring *with* the `dev-backup.ps1` output ([ai-agent-tui-configuration.md](ai-agent-tui-configuration.md)). |
| Telegram bot token | Bitwarden item **Telegram Bot Tokens (Antiphon / School Revision)** (type: Secure Note). Fields include `antiphon_assistant_bot`, `school_revision_bot`, `antiphon_test_bot`, `school_revision_test_bot`. Stand-up is [telegram-bot-ops.md](telegram-bot-ops.md). | Only if standing up channels. Env / user-secrets on the *gateway*, not the desktop `appsettings.json`. |
| `AntiphonMessaging:BootstrapServers` | This machine: `server2:19092` via `aspire-antiphon-apphost` user-secrets (`dotnet user-secrets set "AntiphonMessaging:BootstrapServers" "server2:19092" --project Antiphon.AppHost`). | Leave unset. Fresh clone uses `localhost:19092` from `server/appsettings.json`. |

User-secrets against the server project:

```
dotnet user-secrets set "Llm:Providers:anthropic:ApiKey" "<paste>" --id antiphon-server
dotnet user-secrets set "Llm:Providers:openai:ApiKey" "<paste>" --id antiphon-server
```

## MCP (optional)

Not required for "done". `scripts/bootstrap-check.ps1` does not probe MCP.
This checkout's Claude project `mcpServers` is empty.

Observed on this operator's machine, 2026-08-19 — not a required set:

| Where | What is registered | Token / notes |
|---|---|---|
| Claude user (`~/.claude.json` `mcpServers`; `claude mcp list`) | `todoist` — `C:\src\ClaudeBot\scripts\todoist-mcp-launch.cmd`. `rider` — JetBrains stdio (currently fails to connect). | Todoist: `%USERPROFILE%\.todoist-token`, refreshed from Bitwarden item **Todoist** field `api_token` by `C:\src\ClaudeBot\scripts\todoist-token-refresh.ps1`. Rider: no token. |
| Grok CLI (`grok mcp list`) | None configured. | — |
| This Grok TUI session | `todoist`, `tasks`, `rider` (rider handshake failed). | Session-injected / inherited; `todoist` uses the same token file. Not product state. |

A new operator who wants Todoist MCP: install `@doist/todoist-mcp` globally,
put the token in `%USERPROFILE%\.todoist-token`, register the launcher. Skip
the whole subsection if you do not care.

## Simple-mode fallback

Not the default. Use this only when you are deliberately not running Aspire.

| | Aspire (default) | Simple mode |
|---|---|---|
| Start | `dev-aspire.ps1` / `scripts/restart-apphost.ps1` | `.\dev-start.ps1` |
| Restart | `pwsh -File scripts/restart-apphost.ps1` | `.\restart.ps1` |
| API | 17202 | 17281 |
| Client | 17203 | 17282 |
| Session-runner | 17204 | 17204 |
| Postgres | 17280 (`antiphon-postgres`) | 17280 (same container) |
| Health | `pwsh -File verify-dev-stack.ps1 -SkipBrowser` | `pwsh -File verify-dev-stack.ps1 -SimpleMode` |

`.\dev-stop.ps1` stops server + client; Postgres stays up. Add
`-IncludePostgres` to stop the container too.

---

pwsh -File scripts/bootstrap-check.ps1


<!-- CARD-0254 preserved source begins -->

## CARD-0254 preserved operational detail

## Running Locally

### Prerequisites

- .NET SDK matching [`global.json`](global.json) (currently 10.0.204,
  `rollForward: latestMinor`). Projects target `net9.0`.
- Node.js 20+
- Docker Desktop (PostgreSQL is the always-on `antiphon-postgres` container on port 17280)
- pwsh 7 via `%LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe`

### First-time setup

Follow [docs/bootstrap.md](docs/bootstrap.md). Canonical path is the Aspire
stack (API 17202, Vite 17203, session-runner 17204; Postgres already on 17280).
Do **not** copy `appsettings.json.example` over the tracked
`server/appsettings.json` — that file is already in git. Put LLM keys in
`dotnet user-secrets` against id `antiphon-server`, or in the Settings UI after
first start. The root `appsettings.json.example` is a shape reference only.

### Canonical local restart

Aspire is the default. If an AppHost may already be running:

```
pwsh -File scripts/restart-apphost.ps1
```

Never launch a second bare `dev-aspire.ps1` — it collides with the old process
and the new code never goes live. Smoke: `http://localhost:17202/health`,
client at `http://localhost:17203`, session-runner at `http://localhost:17204`,
dashboard pinned at `http://localhost:17205`.
`pwsh -File verify-dev-stack.ps1 -SkipBrowser` is the health check.

Simple-mode fallback (no Aspire): `.\dev-start.ps1` / `.\restart.ps1`, ports
17281 (API) / 17282 (Vite) / 17204 (session-runner, the always-on daemon). Postgres is still 17280.
`pwsh -File verify-dev-stack.ps1 -SimpleMode`.

### Client serving mode: built bundle by default (CARD-0216)

Under Aspire, port 17203 (both `localhost:17203` and the remote domains it fronts,
`antiphon.{desktop,laptop}.codeperf.net`) is `npm run serve` → `client/scripts/serve.mjs`, not a
raw `vite` dev server. The shim reads `logs/client.mode` (default **built** when the file is
missing) and runs one of two modes on that same port: **built** does a clean `vite build`, starts
`vite preview` on the result, and keeps a `vite build --watch` running (`emptyOutDir` off) so a
merge or a local edit shows up within one rebuild without ever wiping `dist/` out from under a
live page; **dev** runs plain `vite` — HMR, `/@vite/client`, source maps — exactly like `npm run
dev` did before this card. Switch or inspect it with `pwsh -File scripts/client-mode.ps1 -Mode
dev|built` or `-Status` (also `-Rebuild` to force a clean rebuild in built mode without a mode
switch); the choice persists across AppHost restarts. `ANTIPHON_CLIENT_WATCH=0` turns the watcher
off. Simple mode (`dev-start.ps1`, port 17282) is unaffected — it always runs `npm run dev`
directly and has no mode concept.

### Manual backend fallback (ASP.NET Core — port 17281)

```
cd server
dotnet run --urls "http://localhost:17281"
```

Migrations run automatically on startup. The server also seeds initial data.

### Creating EF Migrations

**Always stop the server before creating a migration** — the running Aspire process holds file locks.

1. Stop: `.\stop-server.ps1`
2. Create migration: `dotnet ef migrations add <MigrationName> --project server`
3. Restart & verify: `.\restart-server.ps1`
4. Check `C:\MavLog\Antiphon\antiphon-YYYYMMDD.log` — confirm migration applied with no `[ERR]`/`[FTL]` entries

### Logs: where they are, how long they last, how to turn a source back up

| File | Written by | Retention |
|---|---|---|
| `server/logs/antiphon-*.log` | server Serilog (`Serilog:LogPath`) | **5 days**, plus a 14 x 100 MB (1.4 GB) disk backstop |
| `%TEMP%\antiphon-logs\session-runner-*.log` | session-runner Serilog — **not** under `logs/` | 14 days / 14 x 50 MB |
| `logs/session-runner.log`, `logs/fake-gateway.log` | `scripts/run-daemon.ps1` stdout capture | rolled aside at daemon (re)launch above 20 MB; rolls pruned after 5 days / 10 files |
| `C:\logs\antiphon\session-runner\*.ansi.log` | per-session raw PTY output | `AuditCleanupService` |

**Retention is by TIME, and `retainedFileCountLimit` is only a disk backstop.** It counts FILES, not
days: before CARD-0043 the server wrote a measured 40.9 MB/hour (99.9% of it two sources at
Information), so a 100 MB file filled in 21 minutes under load and the "14" that read as a fortnight
was 5-45 HOURS. What actually delivers 5 days is the write rate staying under
1.4 GB / 5 days = **11.7 MB/hour**; measured after the change it is 0.03 MB/hour average,
0.9 MB/hour in the busiest window.

**`Logging:LogLevel` in `server/appsettings.json` does not filter the server's logs.** Under
`UseSerilog` the Serilog logger factory bypasses the Microsoft.Extensions.Logging filter rules
entirely — `Microsoft.AspNetCore` events were still being written at Information despite that section
saying Warning. **`Serilog:MinimumLevel`** is the one that decides.

**Turning a source back up for one debugging session** needs no rebuild — the levels are
configuration. Either edit `server/appsettings.json`:

```jsonc
"Serilog": { "MinimumLevel": { "Override": {
  "Microsoft.EntityFrameworkCore.Database.Command": "Information"   // full SQL per query, ~30 MB/hour
} } }
```

...or set it in the environment and restart the server (`pwsh -File scripts/restart-apphost.ps1`):

```powershell
$env:Serilog__MinimumLevel__Override__Microsoft.EntityFrameworkCore.Database.Command = 'Information'
```

Re-arming EF or `System.Net.Http.HttpClient` puts the rate back to ~40 MB/hour, which is ~34 hours of
history inside the 1.4 GB backstop. To keep 5 days at that rate, raise the budget too —
`Serilog:RetainedFileCountLimit` (and `Serilog:FileSizeLimitMb`, `Serilog:RetainedFileTimeLimitDays`)
are configuration as well; 5 days at 40.9 MB/hour needs ~4.8 GB.

`Antiphon` is pinned to Information explicitly, so delegation, session, orchestration and supervision
logging survives any future turn-down of `Default`. Do not remove that override.

### Start the frontend (React/Vite — port 17282)

```
cd client
npm run dev
```

The Vite dev server proxies `/api` and `/hubs` to `http://localhost:17281`, so the backend must be running first.

Open **http://localhost:17282** in your browser.

## Dev Port Map (Aspire AppHost mode — `dev-aspire.ps1`)

| Port    | Service                   |
|---------|---------------------------|
| 17200   | AppHost resource service  |
| 17280   | PostgreSQL (always-on external container `antiphon-postgres`) |
| 17202   | .NET server (API)         |
| 17203   | Vite client (built bundle by default; `client-mode.ps1 -Mode dev` for HMR) |
| 17204   | Session runner            |
| 17205   | Aspire dashboard UI       |
| 17206   | OTLP telemetry endpoint   |
| 17207   | Control API               |
| 17208   | Fake messaging gateway (dev/test Kafka tool — real gateway is NOT run locally) |
| 17209   | Storybook (component workshop, AppHost-managed npm app) |

Dashboard is pinned to **http://localhost:17205** via `applicationUrl` in `Antiphon.AppHost/Properties/launchSettings.json`. `dev-aspire.ps1` discovers it and saves to `logs/apphost-dashboard-url.txt`.

## Always-on backend (auto-start)

So agents can run without launching the AppHost, four pieces auto-start at login (set up once via `scripts/install-autostart.ps1`; remove with `-Uninstall`):

- **PostgreSQL** — standalone container `antiphon-postgres` (`docker-compose.dev.yml`, `restart: unless-stopped`) on host port **17280**. Returns on boot via that restart policy + Docker Desktop "AutoStart". It is **no longer Aspire-managed**: the AppHost references it with `AddConnectionString("DefaultConnection")` (value in `Antiphon.AppHost/appsettings.json` = the same `localhost:17280` string the server uses).
- **Session-runner** — native daemon (port **17204**) started by the per-user Scheduled Task **"Antiphon Session Runner"**, which runs `scripts/autostart-session-runner.ps1` → `scripts/run-daemon.ps1`. It writes the same `logs/session-runner.*` pid/state files the AppHost uses, so `dev-aspire.ps1` **adopts** the already-running instance instead of spawning a duplicate (see `DaemonProcessService.InitialiseAsync` — "port already listening — adopting"). **It runs the built `Antiphon.SessionRunner.exe` directly, NOT `dotnet run`** (`run-daemon.ps1 -BuildProjectDir` rebuilds first, then launches the exe): `dotnet run` wraps the app in a kill-on-close Job Object that would capture the detached pty-hosts and kill them on restart, defeating session survival. See the pty-host-split spec.

- **AppHost** (server 17202, client 17203, dashboard 17205, control API 17207) — per-user Scheduled Task **"Antiphon AppHost"**, firing **1 minute after logon**, which runs `scripts/autostart-apphost.ps1` → `dev-aspire.ps1 -NoBrowser`. It **waits for Docker Desktop** (up to 5 min — at logon Docker is still starting and `dev-aspire.ps1` hard-errors if it isn't ready), waits for `antiphon-postgres` to be healthy, and **no-ops if port 17202 is already listening** so it never clobbers a manual `dev-aspire.ps1`. Logs to `logs/autostart-apphost.log`; exits 0 once `/health` returns 200. Opt out with `install-autostart.ps1 -NoAppHost`.
- **AppHost watchdog** — per-user Scheduled Task **"Antiphon AppHost Watchdog"**, every **2 minutes**. `scripts/watchdog-apphost.ps1` probes `http://localhost:17202/health` and `http://localhost:17203/` over HTTP (not TCP listen: Aspire's `dcpctrl` owns both published ports), confirms three **down** rounds ~15 s apart, then calls `scripts/restart-apphost.ps1` so the session-runner on 17204 is never touched. A probe of `client=200` plus `/health` HttpClient timeout is **not** a down round (loaded or coming-up, not a corpse; CARD-0310). Cooldown **15 min** (the same number as `LockMaxAgeMinutes`) / flap cap 3 restarts per 60 min; cooldown is **not** a substitute for the lock — it only tracks watchdog-stamped restarts, so a manual `restart-apphost.ps1` is invisible to it. Log `logs/watchdog-apphost.log` (includes restart-apphost's last ~40 lines and named exit: `0=healthy`, `1=timeout/build`, `3=refused`, `4=DCP timeout` — `exited N` alone is no longer the record). It skips while **either** lock **stamp** is younger than 15 min, even if the holding PID has exited, re-checks both **immediately before** restarting (the check at the top of a fire is up to ~60 s of probing old), and does not stamp a restart when `restart-apphost.ps1` exits 3 — a refusal killed nothing and must not spend flap budget (CARD-0075). Delayed 15 minutes after logon so it does not kill the logon launch. Opt out of registration with `install-autostart.ps1 -NoWatchdog`.
- **Watchdog-state observer** (CARD-0245) — per-user Scheduled Task **"Antiphon AppHost Watchdog State Observer"**, every **2 minutes**, independent of the watchdog. `scripts/apphost-watchdog-state-observer.ps1` samples whether that watchdog task is Disabled / Missing / unreadable and writes `logs/apphost-watchdog-state.json`. Detection only: it never restarts, re-enables, or writes the database. `-NoWatchdog` does not remove it; skip with `-NoWatchdogStateObserver`. Skip only with `-NoAppHost`. Leave the stack down on purpose with `pwsh -File scripts/set-apphost-maintenance.ps1` (creates `logs/apphost.down-on-purpose` **before** disabling the watchdog; `-Clear` re-enables then removes the marker). Direct `Disable-ScheduledTask` / Task Scheduler / `schtasks.exe` edits still work and are exactly what the observer detects.

Start either without re-login: `Start-ScheduledTask -TaskName "Antiphon Session Runner"` / `-TaskName "Antiphon AppHost"` / `-TaskName "Antiphon AppHost Watchdog"` / `-TaskName "Antiphon AppHost Watchdog State Observer"`.

> ⚠️ **`install-autostart.ps1` re-registers tasks by Unregister+Register, which TERMINATES a running instance.** Re-running it plainly while the session-runner is up kills its live supervisor (the daemon is left unsupervised until next logon). Use **`-AppHostOnly`** to add/refresh the AppHost-side tasks (logon AppHost + watchdog) without touching a healthy session-runner.
>
> ⚠️ **Never bake a version-pinned MSIX `pwsh.exe` path into a Scheduled Task.** pwsh 7 is MSIX-installed here, so `Get-Command pwsh.exe` resolves to `C:\Program Files\WindowsApps\Microsoft.PowerShell_<version>_x64__…\pwsh.exe` when the installer itself runs under it — that path vanishes on the next PowerShell update and the task silently stops firing. `install-autostart.ps1` now filters those out and prefers the version-independent app-exec alias `%LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe`.

> **PowerShell 7 (pwsh 7.6+) is installed** (winget MSIX — runs via the per-user WindowsApps app-exec alias `%LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe`, not `Program Files`). The Scheduled Task and AppHost daemon supervisors use it. Keep **Windows PowerShell 5.1** (`powershell.exe`) in mind as the fallback: it reads no-BOM `.ps1` files as CP1252, so a non-ASCII char (em-dash `—`, arrows, box-drawing) can inject a smart-quote and break parsing. **Keep daemon/auto-start scripts ASCII-only** so they work under either host.

### Preserved Gotcha #1

- **`localhost:17203` and the remote domain serve the built bundle; a client change appears after
  the watcher's rebuild (seconds), not instantly** (CARD-0216) — `client-mode.ps1 -Status` shows
  `lastBuildAt`, and `-Mode dev` gives HMR. A delegate that fires a browser check against 17203
  inside the rebuild window after pushing a client change is testing the OLD code; check
  `lastBuildAt` moved past the change's own commit time before trusting what the page shows, or
  switch to `-Mode dev` for the check. ### Preserved Gotcha #5

- **Dev compose file**: Always use `docker compose -f docker-compose.dev.yml up -d` — the default `docker-compose.yml` is not the dev stack. ### Preserved Gotcha #6

- **Startup order**: Postgres must be healthy before the .NET server starts. Postgres is now an always-on external container (auto-started at login), so it is already up by the time you run `dev-aspire.ps1`. The AppHost references it via `AddConnectionString` (no `WaitFor` — connection-string resources don't support it); `dev-aspire.ps1` also `docker compose up -d`'s it as a safety net. ### Preserved Gotcha #7

- **npm install first**: `client/node_modules` may not exist — run `npm install` before `npm run dev` or `npm run storybook`. ### Preserved Gotcha #8

- **Storybook v9+**: `@storybook/addon-essentials` does not exist for Storybook v9+. It was folded into core. Remove it from `package.json` if present — do not try to install it. ### Preserved Gotcha #9

- **Orphaned Aspire DCP conflict**: Check for a stale `dcpctrl.exe` from a different Aspire project holding port 17202: `Get-NetTCPConnection -LocalPort 17202 -State Listen`. Kill the owning PID if foreign. Restarting the AppHost respawns DCP. ### Preserved Gotcha #10

- **Starting the AppHost**: Use `Start-Process pwsh -ArgumentList @('-NoLogo','-File','C:\src\antiphon\dev-aspire.ps1') -WindowStyle Normal`. Do NOT use `wt new-tab` — it fails with `0x80070002` (file not found) when the title contains a space. Do NOT use `-NoNewWindow` — that attaches the AppHost to the tool session and kills it when the session ends. The script exits after ~60s; the AppHost continues in background (`logs/apphost.pid`). ### Preserved Gotcha #11

- **RESTARTING the AppHost**: `dev-aspire.ps1` does NOT stop a running AppHost — re-running it launches a second one that collides with the old (old server keeps its ports, code changes never go live). Use `pwsh -File scripts/restart-apphost.ps1` instead: it kills the old tree, frees the ports, relaunches, and waits for health, preserving the session-runner. Restarts and `deploy-local.ps1` normally originate from the main checkout: linked worktrees refuse by default (CARD-0273 — a delegate running these from its own task worktree once silently stole the canonical stack's root); `-AllowWorktree` deliberately controls the shared stack and is only for an explicit test. **A non-zero exit does not mean it stopped launching**: it spawns `dev-aspire.ps1` detached and polls its own `TimeoutSec` (150 s), while the child's real budget is longer (8 s network preflight + `dotnet restore`/`npm install` + 90 s dashboard wait + 45 s Postgres wait). **Exit 3 means REFUSED — another restart or launch is in flight and nothing was killed** (CARD-0075): it takes `logs/apphost.restart.lock` for the whole run and also honours `dev-aspire.ps1`'s `logs/apphost.launch.lock`. A lock **stamp** younger than 15 min is still in-flight even if the holding PID has exited (CARD-0310). TimeoutSec (exit 1, child may still be launching) and DCP timeout (exit 4) **leave** `apphost.restart.lock` for that window; deleting the file is how you force a retry. Exit 0 and a build failure still remove it. A successful `dev-aspire.ps1` still deletes `apphost.launch.lock` as soon as the dashboard is ready — that lock is not held for 15 min after a healthy start. Do not re-run on a plain failure without checking those two files first. ### Preserved Gotcha #12

- **A podman error in `apphost.log` means the DCP dependency check TIMED OUT — not that a container runtime is missing** (CARD-0075; same genre as the MSB3552 resx and HNS "Created" entries below — the message names the wrong thing). `dcp info` probes podman **first on every single invocation, healthy ones included** (measured 2026-08-20: podman at +0 ms, docker at +382 ms, result at +648 ms, `"runtime":"docker","installed":true,"running":true`), and Aspire splices that captured stderr into the exception text — `dependency check returned an error: {0}` where `{0}` is `"The operation has timed out. <dcp stderr>"`, one concatenated string. A timeout truncates that stderr **after** the runtime that answered instantly and **before** the one whose stall consumed the whole deadline, so the message accuses podman precisely because podman is *not* what failed. Podman is not installed here, is not needed, and this AppHost has **zero container resources** (`AddConnectionString` + `AddProject` + 2× `AddNpmApp`) — yet `EnsureDcpContainerRuntimeAsync` still sits on the critical path of every launch. Log-level filtering cannot help (`Aspire.Hosting.Dcp: Warning` is already set; this is exception content, not a log record), and *any* future dependency-check timeout prints the identical podman text regardless of cause. The real cause is almost always **two restarts racing** — a caller whose `restart-apphost.ps1` "failed" re-runs it and the re-run's `taskkill` lands on a DCP that is still coming up (a 44-run census found same-session re-runs at +145 s and +191 s, both inside the previous run's own launch window). `restart-apphost.ps1` now detects this shape itself and prints what `docker` actually said instead of leaving the podman text standing, exiting **4**. Check `docker ps`, then `logs/apphost.restart.lock`, `logs/apphost.launch.lock` and `logs/watchdog-apphost.log`, before changing any configuration. Do **not** raise `DcpPublisher:DependencyCheckTimeout` (it masks the collision) and do not chase `--container-runtime`/`DCP_CONTAINER_RUNTIME` (measured: unsupported in Aspire 9.3.0). ### Preserved Gotcha #14

- **Stale daemon supervisors**: Each AppHost restart now kills the existing supervisor (read from `logs/<name>.supervisor.pid`) before launching a new one. If supervisors accumulate from manual kills or crashes, use: `Get-WmiObject Win32_Process -Filter "Name='pwsh.exe'" | Where-Object { $_.CommandLine -like '*run-daemon*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }` (supervisors run under pwsh 7; use `powershell.exe` in the filter if you ever fall back to 5.1). **Be precise** — filter by the specific project path or check `logs/*.supervisor.pid` first, or you risk killing the current session's supervisors. Note the session-runner supervisor is normally owned by the **"Antiphon Session Runner"** Scheduled Task, not the AppHost. ### Preserved Gotcha #15

- **appsettings.json paths on Windows**: Any file paths in `appsettings.json` (e.g. worktree directories) must use Windows-style backslashes. Linux-style forward slashes in path config break on Windows even though .NET sometimes tolerates them. ### Preserved Gotcha #16

- **Postgres credentials/volume**: The always-on `antiphon-postgres` container uses db/user/password `antiphon` / `antiphon` / `antiphon_dev` (fixed in `docker-compose.dev.yml`), data in the `antiphon_pgdata` Docker volume. Don't delete the volume without recreating it. (The old Aspire-managed `pg-password` parameter and `antiphon-pgdata` volume are gone.) ### Preserved Gotcha #17

- **Postgres stuck in "Created" state**: Windows HNS (Host Network Service) can enter a bad state where `docker network create` hangs indefinitely. Symptom: `docker ps -a --filter name=antiphon-postgres` shows "Created" forever. Fix: restart Docker Desktop, then `docker compose -f docker-compose.dev.yml up -d`. Detection: `$j = Start-Job { docker network create test-net 2>&1 }; $j | Wait-Job -Timeout 5; if ($j.State -eq 'Running') { "HNS broken" }`. `dev-aspire.ps1` still pre-tests this and warns.
<!-- CARD-0254 preserved source ends -->
