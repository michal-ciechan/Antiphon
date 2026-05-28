# antiphon-run — Antiphon Dev Stack Manager

Use this skill when the user wants to start, stop, restart, reset, upgrade, back up, or restore the Antiphon local dev environment.

## Trigger phrases
"run antiphon", "start antiphon", "stop antiphon", "restart antiphon", "fresh start", "upgrade antiphon", "backup antiphon", "restore antiphon", "antiphon backup", "is antiphon running"

---

## Stack overview

### Aspire AppHost mode (`dev-aspire.ps1`) — preferred

| Port  | Service                   | Notes                        |
|-------|---------------------------|------------------------------|
| 17200 | AppHost resource service  | Aspire orchestrator          |
| 17201 | PostgreSQL                | Aspire-managed container     |
| 17202 | .NET server (API)         | Daemon — survives AppHost exit |
| 17203 | Vite dev client           | Daemon — survives AppHost exit |
| 17204 | Session runner            | Daemon — survives AppHost exit |
| 17205 | Aspire dashboard UI       | Opens in browser automatically |
| 17206 | OTLP telemetry endpoint   |                              |
| 17207 | Control API               | POST /control/{name}/restart |

### Simple Docker Compose mode (`dev-start.ps1`)

| Port  | Service              | Process            |
|-------|----------------------|--------------------|
| 17280 | PostgreSQL           | Docker container   |
| 17281 | .NET API server      | `dotnet run`       |
| 17282 | React/Vite client    | `npm run dev`      |
| 17283 | Storybook (optional) | `npm run storybook`|

---

## Launching the AppHost

```powershell
Start-Process pwsh -ArgumentList @('-NoLogo', '-File', 'C:\src\antiphon\dev-aspire.ps1') -WindowStyle Normal
```

- Do NOT use `wt new-tab` — fails with `0x80070002` when title has a space
- Do NOT use `-NoNewWindow` — kills AppHost when the tool session ends
- Script exits ~60s after dashboard is ready; AppHost runs in background
- Dashboard opens automatically at http://localhost:17205
- PID saved to `logs/apphost.pid`, logs to `logs/apphost.log`

---

## Scripts — when to use each

| Script               | When                                              |
|----------------------|---------------------------------------------------|
| `.\dev-start.ps1`    | Normal start. Safe to re-run (idempotent).        |
| `.\dev-stop.ps1`     | Stop server + client; postgres stays up.          |
| `.\dev-stop.ps1 -IncludePostgres` | Stop everything.                   |
| `.\dev-fresh.ps1`    | Nuclear reset (all data lost). Prompts to confirm.|
| `.\dev-upgrade.ps1`  | git pull + build + npm install + restart.         |
| `.\dev-backup.ps1`   | Create a pg_dump backup in C:\Antiphon\backups\.  |
| `.\dev-restore.ps1`  | Restore DB from latest (or specified) backup.     |

All scripts live at the repo root `C:\src\antiphon\`.

---

## Persistent storage locations

| What               | Where                                     | Notes                                   |
|--------------------|-------------------------------------------|-----------------------------------------|
| Database           | Docker named volume `antiphon_pgdata`     | Survives container restarts; lost with `down -v` |
| Backups            | `C:\Antiphon\backups\`                    | `.sql.zip` files, 14-backup retention   |
| Git worktrees      | `C:\Antiphon\worktrees\`                  | Created by agent sessions               |
| App logs           | `C:\MavLog\Antiphon\`                     | Serilog daily rolling files             |
| PTY session logs   | `C:\MavLog\Antiphon\sessions\`            | Full ANSI audit per session             |

---

## Common flows

### First time
1. Start Docker Desktop (wait for tray icon)
2. Ensure `server\appsettings.json` exists (copy from `appsettings.json.example`, add API keys)
3. `.\dev-start.ps1`

### Every day
1. `.\dev-start.ps1` — opens server + client in new windows, opens browser at :17282
2. Work in browser at http://localhost:17282
3. `.\dev-stop.ps1` when done (postgres stays up for speed)

### After git pull / code changes
```
.\dev-upgrade.ps1
```
Backs up first, then pulls, builds, npm installs, restarts.

### Manual backup
```
.\dev-backup.ps1
```
Creates `C:\Antiphon\backups\antiphon-YYYYMMDD-HHmmss.sql.zip`. Prunes to last 14.

### Restore from backup
```
.\dev-restore.ps1                          # latest backup
.\dev-restore.ps1 -BackupFile path\to.zip  # specific backup
```

### Nuclear reset (wipe all data)
```
.\dev-fresh.ps1
```
Prompts for confirmation. Drops volume, wipes worktrees, starts fresh.

---

## Status check

Check what's listening (Aspire mode):
```powershell
17200..17207 | ForEach-Object {
    $r = Test-NetConnection -ComputerName localhost -Port $_ -WarningAction SilentlyContinue -InformationLevel Quiet
    [pscustomobject]@{ Port = $_; Open = $r }
}
```

Check what's listening (simple mode):
```powershell
Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
    Where-Object { $_.LocalPort -in 17280,17281,17282 } |
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
Then `.\dev-start.ps1`.

### Port conflict (stale Aspire dcp.exe)
Check for a stale `dcpctrl.exe` or `dcp.exe` holding a port in the 17200–17207 range:
```powershell
Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
    Where-Object { $_.LocalPort -ge 17200 -and $_.LocalPort -le 17207 }
Stop-Process -Id <pid> -Force
```

### Migrations fail on startup
Stop the server, create the migration, restart:
```powershell
.\dev-stop.ps1
dotnet ef migrations add <Name> --project server
.\dev-start.ps1
```

### First `dotnet run` is slow
It's compiling. Takes 20-30 seconds on first run; subsequent runs are fast.

---

## Backup planning

Current approach: **manual** with `.\dev-backup.ps1`.

To automate daily backups, use `/schedule` to set up a Claude Code routine, or register a Windows Task:
```powershell
$action  = New-ScheduledTaskAction -Execute 'pwsh.exe' -Argument '-NonInteractive -File C:\src\antiphon\dev-backup.ps1'
$trigger = New-ScheduledTaskTrigger -Daily -At '02:00'
Register-ScheduledTask -TaskName 'Antiphon-Daily-Backup' -Action $action -Trigger $trigger -RunLevel Highest
```
Backups are stored in `C:\Antiphon\backups\` with 14-file retention.
