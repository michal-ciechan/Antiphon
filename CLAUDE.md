# Claude Code Configuration

See [AGENTS.md](AGENTS.md) for all project conventions and context.

## Dev Port Map (Aspire AppHost mode — `dev-aspire.ps1`)

| Port    | Service                   |
|---------|---------------------------|
| 17200   | AppHost resource service  |
| 17280   | PostgreSQL (always-on external container `antiphon-postgres`) |
| 17202   | .NET server (API)         |
| 17203   | Vite dev client           |
| 17204   | Session runner            |
| 17205   | Aspire dashboard UI       |
| 17206   | OTLP telemetry endpoint   |
| 17207   | Control API               |

Dashboard is pinned to **http://localhost:17205** via `applicationUrl` in `Antiphon.AppHost/Properties/launchSettings.json`. `dev-aspire.ps1` discovers it and saves to `logs/apphost-dashboard-url.txt`.

## Always-on backend (auto-start)

So agents can run without launching the AppHost, two pieces auto-start at login (set up once via `scripts/install-autostart.ps1`; remove with `-Uninstall`):

- **PostgreSQL** — standalone container `antiphon-postgres` (`docker-compose.dev.yml`, `restart: unless-stopped`) on host port **17280**. Returns on boot via that restart policy + Docker Desktop "AutoStart". It is **no longer Aspire-managed**: the AppHost references it with `AddConnectionString("DefaultConnection")` (value in `Antiphon.AppHost/appsettings.json` = the same `localhost:17280` string the server uses).
- **Session-runner** — native daemon (port **17204**) started by the per-user Scheduled Task **"Antiphon Session Runner"**, which runs `scripts/autostart-session-runner.ps1` → `scripts/run-daemon.ps1`. It writes the same `logs/session-runner.*` pid/state files the AppHost uses, so `dev-aspire.ps1` **adopts** the already-running instance instead of spawning a duplicate (see `DaemonProcessService.InitialiseAsync` — "port already listening — adopting").

The **server, client, and dashboard** are NOT auto-started — run `.\dev-aspire.ps1` for those. Start the session-runner now without re-login: `Start-ScheduledTask -TaskName "Antiphon Session Runner"`.

> **PowerShell 7 (pwsh 7.6+) is installed** (winget MSIX — runs via the per-user WindowsApps app-exec alias `%LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe`, not `Program Files`). The Scheduled Task and AppHost daemon supervisors use it. Keep **Windows PowerShell 5.1** (`powershell.exe`) in mind as the fallback: it reads no-BOM `.ps1` files as CP1252, so a non-ASCII char (em-dash `—`, arrows, box-drawing) can inject a smart-quote and break parsing. **Keep daemon/auto-start scripts ASCII-only** so they work under either host.

## Gotchas

- **Dev compose file**: Always use `docker compose -f docker-compose.dev.yml up -d` — the default `docker-compose.yml` is not the dev stack.
- **Startup order**: Postgres must be healthy before the .NET server starts. Postgres is now an always-on external container (auto-started at login), so it is already up by the time you run `dev-aspire.ps1`. The AppHost references it via `AddConnectionString` (no `WaitFor` — connection-string resources don't support it); `dev-aspire.ps1` also `docker compose up -d`'s it as a safety net.
- **npm install first**: `client/node_modules` may not exist — run `npm install` before `npm run dev` or `npm run storybook`.
- **Storybook v9+**: `@storybook/addon-essentials` does not exist for Storybook v9+. It was folded into core. Remove it from `package.json` if present — do not try to install it.
- **Orphaned Aspire DCP conflict**: Check for a stale `dcpctrl.exe` from a different Aspire project holding port 17202: `Get-NetTCPConnection -LocalPort 17202 -State Listen`. Kill the owning PID if foreign. Restarting the AppHost respawns DCP.
- **Starting the AppHost**: Use `Start-Process pwsh -ArgumentList @('-NoLogo','-File','C:\src\antiphon\dev-aspire.ps1') -WindowStyle Normal`. Do NOT use `wt new-tab` — it fails with `0x80070002` (file not found) when the title contains a space. Do NOT use `-NoNewWindow` — that attaches the AppHost to the tool session and kills it when the session ends. The script exits after ~60s; the AppHost continues in background (`logs/apphost.pid`).
- **Stale daemon supervisors**: Each AppHost restart now kills the existing supervisor (read from `logs/<name>.supervisor.pid`) before launching a new one. If supervisors accumulate from manual kills or crashes, use: `Get-WmiObject Win32_Process -Filter "Name='pwsh.exe'" | Where-Object { $_.CommandLine -like '*run-daemon*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }` (supervisors run under pwsh 7; use `powershell.exe` in the filter if you ever fall back to 5.1). **Be precise** — filter by the specific project path or check `logs/*.supervisor.pid` first, or you risk killing the current session's supervisors. Note the session-runner supervisor is normally owned by the **"Antiphon Session Runner"** Scheduled Task, not the AppHost.
- **appsettings.json paths on Windows**: Any file paths in `appsettings.json` (e.g. worktree directories) must use Windows-style backslashes. Linux-style forward slashes in path config break on Windows even though .NET sometimes tolerates them.
- **Postgres credentials/volume**: The always-on `antiphon-postgres` container uses db/user/password `antiphon` / `antiphon` / `antiphon_dev` (fixed in `docker-compose.dev.yml`), data in the `antiphon_pgdata` Docker volume. Don't delete the volume without recreating it. (The old Aspire-managed `pg-password` parameter and `antiphon-pgdata` volume are gone.)
- **Postgres stuck in "Created" state**: Windows HNS (Host Network Service) can enter a bad state where `docker network create` hangs indefinitely. Symptom: `docker ps -a --filter name=antiphon-postgres` shows "Created" forever. Fix: restart Docker Desktop, then `docker compose -f docker-compose.dev.yml up -d`. Detection: `$j = Start-Job { docker network create test-net 2>&1 }; $j | Wait-Job -Timeout 5; if ($j.State -eq 'Running') { "HNS broken" }`. `dev-aspire.ps1` still pre-tests this and warns.
- **TUnit tests**: Use `dotnet run --project tests/<ProjectName>`, not `dotnet test`. Filter by `--treenode-filter`. Headed tests need `ANTIPHON_HEADED_TESTS=1` and must be in `[NotInParallel("Headed")]` group.
- **Sessions survive runner restarts (pty-host split)**: Since the pty-host split (spec: `docs/superpowers/specs/2026-07-19-pty-host-split.md`), each agent session's ConPTY child lives in a detached per-session `Antiphon.PtyHost` process, NOT in the session-runner. `restart-session-runner.ps1` no longer kills sessions — the restarted runner re-adopts live hosts from `<SessionLogPath>/pty-hosts/manifests/*.json`. Scorched earth: `-KillSessions` flag or `POST :17204/sessions/kill-all`. Hosts run from shadow-copied binaries under `pty-hosts/bin/<yyyyMMdd-HHmmss>-<sha8>/` (never from `bin/` — running exes there would break builds); unreferenced version dirs are pruned automatically.
- **Building while daemons run**: the always-on session-runner (and dev server) lock their `bin/` outputs. To build/test without restarting them, use an alternate output path: `dotnet run --project tests/<X> --property:OutputPath=bin-ptyhost\` (gitignored). Note some Antiphon.Tests are PTY-timing flaky under full parallel load — rerun the failures in isolation before blaming a change.
