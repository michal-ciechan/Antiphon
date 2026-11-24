# antiphon-run — Antiphon Dev Stack Manager

Use this skill when the user wants to start, stop, restart, reset, upgrade, back up, or restore the Antiphon local dev environment.

## Trigger phrases
"run antiphon", "start antiphon", "stop antiphon", "restart antiphon", "fresh start", "upgrade antiphon", "backup antiphon", "restore antiphon", "antiphon backup", "is antiphon running"

---

## Stack overview

First-time setup is [docs/bootstrap.md](../../../docs/bootstrap.md). Aspire is
the default. Postgres is the always-on `antiphon-postgres` container on **17280**
(not Aspire-managed). Dashboard is pinned at **http://localhost:17205**.

### Aspire AppHost mode (`scripts/antiphon dev-aspire`) — preferred

| Port  | Service                   | Notes                        |
|-------|---------------------------|------------------------------|
| 17200 | AppHost resource service  | Aspire orchestrator          |
| 17280 | PostgreSQL                | always-on `antiphon-postgres` (compose, not Aspire-managed) |
| 17202 | .NET server (API)         | Daemon — survives AppHost exit |
| 17203 | Vite dev client           | Daemon — survives AppHost exit |
| 17204 | Session runner            | Daemon — survives AppHost exit |
| 17205 | Aspire dashboard UI       | Pinned; opens in browser automatically |
| 17206 | OTLP telemetry endpoint   |                              |
| 17207 | Control API               | POST /control/{name}/restart |
| 17208 | Fake messaging gateway    | Dev/test Kafka tool          |
| 17209 | Storybook                 | AppHost-managed npm app      |

### Simple Docker Compose mode (`scripts/antiphon dev-start`) — fallback

| Port  | Service              | Process            |
|-------|----------------------|--------------------|
| 17280 | PostgreSQL           | same `antiphon-postgres` container |
| 17281 | .NET API server      | `dotnet run`       |
| 17282 | React/Vite client    | `npm run dev`      |
| 17204 | Session runner       | always-on daemon (same process as Aspire) |
| 17209 | Storybook (optional) | `npm run storybook`|

---

## Launching the AppHost

If an AppHost may already exist, restart — do not launch a second copy:

```sh
scripts/antiphon restart-apphost
```

First launch only, when nothing is listening:

```sh
scripts/antiphon dev-aspire
```

- The dev stack scripts are C# commands since 2026-11-24 (unit 37 of
  docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). Units 43 and 44 deleted the repo-root launchers;
  everyone runs the commands (`scripts\antiphon.cmd <command>` on Windows).
- The command exits once the dashboard is ready (90 s at most); the AppHost runs on in a detached wrapper
  (`run-apphost`: the stamped build, then the AppHost, output in `logs/apphost.log`). A shell whose Job Object kills its
  children at command end still takes the wrapper with it (docs/apphost-runbook.md).
- Dashboard is pinned at http://localhost:17205
- PID saved to `logs/apphost.pid`, logs to `logs/apphost.log`

---

## Commands — when to use each

| Command (`scripts/antiphon <command>`) | When |
|----------------------|---------------------------------------------------|
| `restart-apphost`    | Canonical Aspire restart. Use whenever an AppHost may already exist. |
| `dev-aspire`         | First Aspire launch only if nothing is listening. Never a second copy. |
| `dev-start`          | Simple-mode fallback (API 17281 / client 17282; runner 17204). |
| `dev-stop`           | Stop server + client; postgres stays up.          |
| `dev-stop --include-postgres` | Stop everything.                         |
| `dev-fresh`          | Nuclear reset (all data lost). Prompts to confirm (`--yes` skips it). |
| `dev-upgrade`        | backup + git pull + build + npm install + restart. |
| `dev-backup`         | Create a pg_dump backup in `<repo>/backups/` (override with `--backup-dir`). |
| `dev-restore`        | Restore DB from latest (or `--backup-file`) backup. Prompts to confirm. |
| `restart-dev-stack`, `restart-server`, `stop-server` | Restart through the external Aspire control endpoint (Mikey.AspireCommand, `--endpoint`). |

On Windows run them as `scripts\antiphon.cmd dev-start --no-client`, `scripts\antiphon.cmd dev-backup --backup-dir ...`.

---

## Persistent storage locations

| What               | Where                                     | Notes                                   |
|--------------------|-------------------------------------------|-----------------------------------------|
| Database           | Docker named volume `antiphon_pgdata`     | Survives container restarts; lost with `down -v` |
| Backups            | `<repo>/backups/`                         | `.sql.zip` files, 14-backup retention; `dev-backup` default |
| Git worktrees      | `C:\Antiphon\worktrees\`                  | Created by agent sessions               |
| App logs           | `C:\MavLog\Antiphon\`                     | Serilog daily rolling files             |
| PTY session logs   | `C:\MavLog\Antiphon\sessions\`            | Full ANSI audit per session             |

---

## Common flows

### First time

Follow [docs/bootstrap.md](../../../docs/bootstrap.md). Short form:

1. Start Docker Desktop (wait for tray icon)
2. `docker compose -f docker-compose.dev.yml up -d` (Postgres on 17280)
3. Put LLM keys in `dotnet user-secrets` (`--id antiphon-server`) or the Settings UI after start. Do **not** copy `appsettings.json.example` over the tracked `server/appsettings.json`.
4. `scripts/antiphon install-autostart`
5. `scripts/antiphon restart-apphost` (or `scripts/antiphon dev-aspire` if nothing is listening)
6. `scripts/antiphon bootstrap-check` (read-only; exit code = number of FAILs)

### Every day
1. Aspire is already always-on after `scripts/antiphon install-autostart`. If it is down: `scripts/antiphon restart-apphost`
2. Work in the browser at http://localhost:17203 (dashboard http://localhost:17205)
3. Postgres stays up. Do not run a second `dev-aspire`.

### After git pull / code changes
```
scripts/antiphon dev-upgrade
```
Backs up first, then pulls, builds, npm installs, restarts.

### Manual backup
```
scripts/antiphon dev-backup
```
Creates `<repo>/backups/antiphon-YYYYMMDD-HHmmss.sql.zip` (its path is the last line). Prunes to last 14 (`--retain`).
Override with `--backup-dir`.

### Restore from backup
```
scripts/antiphon dev-restore                              # latest backup
scripts/antiphon dev-restore --backup-file path/to.zip    # specific backup
```

### Nuclear reset (wipe all data)
```
scripts/antiphon dev-fresh
```
Prompts for confirmation. Drops volume, wipes worktrees, starts fresh.

---

## Status check

Prefer `scripts/antiphon verify-dev-stack --skip-browser` (or `--simple-mode`) over
hand-rolled port loops.

Check what's listening (Aspire mode; Postgres is 17280):
```powershell
17200,17202,17203,17204,17205,17206,17207,17280 | ForEach-Object {
    $r = Test-NetConnection -ComputerName localhost -Port $_ -WarningAction SilentlyContinue -InformationLevel Quiet
    [pscustomobject]@{ Port = $_; Open = $r }
}
```

Check what's listening (simple mode):
```powershell
Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
    Where-Object { $_.LocalPort -in 17280,17281,17282,17204 } |
    Select-Object LocalPort, OwningProcess
```

Check postgres container:
```powershell
docker compose -f docker-compose.dev.yml ps
```

Check recent logs:
```powershell
Get-ChildItem C:\MavLog\Antiphon -Filter '*.log' | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Get-Content -Tail 50
```

---

## Troubleshooting

### Docker not running
Start Docker Desktop. Watch the system tray; wait until "Docker Desktop is running".
Then `scripts/antiphon restart-apphost` (Aspire) or `scripts/antiphon dev-start` (simple-mode fallback).

### Port conflict (stale Aspire dcp.exe)
Check for a stale `dcpctrl.exe` or `dcp.exe` holding a port in the 17200–17207 range:
```powershell
Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
    Where-Object { $_.LocalPort -ge 17200 -and $_.LocalPort -le 17207 }
Stop-Process -Id <pid> -Force
```

### Migrations fail on startup
Stop the server, create the migration, restart:
```sh
scripts/antiphon dev-stop
dotnet ef migrations add <Name> --project server
scripts/antiphon dev-start
```

### First `dotnet run` is slow
It's compiling. Takes 20-30 seconds on first run; subsequent runs are fast.

---

## Backup planning

Current approach: **manual** with `scripts/antiphon dev-backup`.

To automate daily backups, use `/schedule` to set up a Claude Code routine, or register a Windows Task:
```powershell
$action  = New-ScheduledTaskAction -Execute 'C:\src\antiphon\scripts\antiphon.cmd' -Argument 'dev-backup'
$trigger = New-ScheduledTaskTrigger -Daily -At '02:00'
Register-ScheduledTask -TaskName 'Antiphon-Daily-Backup' -Action $action -Trigger $trigger -RunLevel Highest
```
`dev-backup` defaults to `<repo>/backups/` with 14-file retention.
