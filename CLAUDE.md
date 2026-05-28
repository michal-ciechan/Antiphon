# Claude Code Configuration

See [AGENTS.md](AGENTS.md) for all project conventions and context.

## Dev Port Map (Aspire AppHost mode — `dev-aspire.ps1`)

| Port    | Service                   |
|---------|---------------------------|
| 17200   | AppHost resource service  |
| 17201   | PostgreSQL                |
| 17202   | .NET server (API)         |
| 17203   | Vite dev client           |
| 17204   | Session runner            |
| 17205   | Aspire dashboard UI       |
| 17206   | OTLP telemetry endpoint   |
| 17207   | Control API               |

Dashboard is pinned to **http://localhost:17205** via `applicationUrl` in `Antiphon.AppHost/Properties/launchSettings.json`. `dev-aspire.ps1` discovers it and saves to `logs/apphost-dashboard-url.txt`.

## Gotchas

- **Dev compose file**: Always use `docker compose -f docker-compose.dev.yml up -d` — the default `docker-compose.yml` is not the dev stack.
- **Startup order**: Postgres must be healthy before starting the .NET server. The AppHost handles this via `WaitFor(postgres)`.
- **npm install first**: `client/node_modules` may not exist — run `npm install` before `npm run dev` or `npm run storybook`.
- **Storybook v9+**: `@storybook/addon-essentials` does not exist for Storybook v9+. It was folded into core. Remove it from `package.json` if present — do not try to install it.
- **Orphaned Aspire DCP conflict**: Check for a stale `dcpctrl.exe` from a different Aspire project holding port 17202: `Get-NetTCPConnection -LocalPort 17202 -State Listen`. Kill the owning PID if foreign. Restarting the AppHost respawns DCP.
- **Starting the AppHost**: Use `Start-Process pwsh -ArgumentList @('-NoLogo','-File','C:\src\antiphon\dev-aspire.ps1') -WindowStyle Normal`. Do NOT use `wt new-tab` — it fails with `0x80070002` (file not found) when the title contains a space. Do NOT use `-NoNewWindow` — that attaches the AppHost to the tool session and kills it when the session ends. The script exits after ~60s; the AppHost continues in background (`logs/apphost.pid`).
- **Stale daemon supervisors**: Each AppHost restart now kills the existing supervisor (read from `logs/<name>.supervisor.pid`) before launching a new one. If supervisors accumulate from manual kills or crashes, use: `Get-WmiObject Win32_Process -Filter "Name='pwsh.exe'" | Where-Object { $_.CommandLine -like '*run-daemon*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }`. **Be precise** — filter by the specific project path or check `logs/*.supervisor.pid` first, or you risk killing the current session's supervisors.
- **appsettings.json paths on Windows**: Any file paths in `appsettings.json` (e.g. worktree directories) must use Windows-style backslashes. Linux-style forward slashes in path config break on Windows even though .NET sometimes tolerates them.
- **Postgres password**: The AppHost uses a fixed password (`antiphon_dev`) via the `pg-password` parameter so the persistent `antiphon-pgdata` volume survives restarts. Do not delete the volume without also recreating it.
- **Postgres stuck in "Created" state**: Windows HNS (Host Network Service) can enter a bad state where `docker network create` hangs indefinitely. Symptom: `docker ps -a --filter name=DefaultConnection` shows "Created" forever. Fix: restart Docker Desktop. Detection: `$j = Start-Job { docker network create test-net 2>&1 }; $j | Wait-Job -Timeout 5; if ($j.State -eq 'Running') { "HNS broken" }`. `dev-aspire.ps1` now pre-tests this and warns.
- **TUnit tests**: Use `dotnet run --project tests/<ProjectName>`, not `dotnet test`. Filter by `--treenode-filter`. Headed tests need `ANTIPHON_HEADED_TESTS=1` and must be in `[NotInParallel("Headed")]` group.
