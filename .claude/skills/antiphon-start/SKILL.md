# antiphon-start

Start the Antiphon dev stack via the Aspire AppHost, verify all services are up, open the dashboard, and fix anything blocking startup.

## Trigger phrases
"start antiphon", "run antiphon", "launch antiphon", "is antiphon running", "open dashboard", "antiphon not starting", "fix antiphon startup"

---

## Port map (Aspire mode)

| Port    | Service                  |
|---------|--------------------------|
| 17200   | AppHost resource service |
| 17201   | PostgreSQL               |
| 17202   | .NET server (API)        |
| 17203   | Vite dev client          |
| 17204   | Session runner           |
| dynamic | Aspire dashboard UI      |
| 17206   | OTLP telemetry           |
| 17207   | Control API              |

**Dashboard URL is dynamic** — Aspire assigns it. `dev-aspire.ps1` finds it automatically and saves it to `logs/apphost-dashboard-url.txt`. To get it manually:
```powershell
Get-Content C:\src\antiphon\logs\apphost-dashboard-url.txt
# or: find the Aspire.Dashboard process port
$dash = Get-Process 'Aspire.Dashboard' -ErrorAction SilentlyContinue | Select-Object -First 1
Get-NetTCPConnection -State Listen -OwningProcess $dash.Id | Where-Object { $_.LocalPort -ne 17206 }
```
Log file: `C:\src\antiphon\logs\apphost.log`

---

## Step 1 — Pre-flight checks

Run these in parallel:

```powershell
# A) Docker running?
docker info 2>&1 | Select-String "Server Version|error" | Select-Object -First 1

# B) appsettings.json present?
Test-Path "C:\src\antiphon\server\appsettings.json"

# C) Stale processes?
Get-Process dcpctrl, Aspire.Dashboard -ErrorAction SilentlyContinue | Select-Object Id, ProcessName

# D) Port conflicts in range?
Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
    Where-Object { $_.LocalPort -ge 17200 -and $_.LocalPort -le 17207 } |
    Select-Object LocalPort, OwningProcess
```

Fix anything before proceeding (see Troubleshooting below).

### Always do before build: kill surviving daemon processes

Daemon processes (session-runner, server, client) survive AppHost exit and **lock their DLLs**. The build will fail with MSB3027 if they are still running. Kill them before launching:

```powershell
# Kill by port (daemons hold 17202, 17203, 17204)
17202, 17203, 17204 | ForEach-Object {
    $pid = (Get-NetTCPConnection -LocalPort $_ -State Listen -ErrorAction SilentlyContinue).OwningProcess
    if ($pid) { Stop-Process -Id $pid -Force -ErrorAction SilentlyContinue; Write-Host "Killed PID $pid on port $_" }
}
# Kill their supervisors
'server','client','session-runner' | ForEach-Object {
    $f = "C:\src\antiphon\logs\$_.supervisor.pid"
    if (Test-Path $f) {
        $p = (Get-Content $f).Trim()
        Stop-Process -Id $p -Force -ErrorAction SilentlyContinue
    }
}
```

---

## Step 2 — Launch

```powershell
Start-Process pwsh -ArgumentList @('-NoLogo', '-File', 'C:\src\antiphon\dev-aspire.ps1') -WindowStyle Normal
```

**Do NOT use `wt new-tab`** — fails with `0x80070002` when title contains a space.
**Do NOT use `-NoNewWindow`** — kills AppHost when the tool session ends.

The script will:
1. Restore + npm install (skipped with `-NoBuild` if already fresh)
2. Spawn the AppHost in a hidden background process (`logs/apphost.pid`)
3. Poll port 17205 up to 60s, then open the browser and exit

---

## Step 3 — Verify

Poll until all core ports respond (allow 90s for first build):

```powershell
$deadline = (Get-Date).AddSeconds(90)
while ((Get-Date) -lt $deadline) {
    $ports = 17200, 17202, 17204, 17205
    $open  = $ports | Where-Object {
        try { $t = [Net.Sockets.TcpClient]::new('127.0.0.1', $_); $t.Close(); $true }
        catch { $false }
    }
    $closed = $ports | Where-Object { $_ -notin $open }
    Write-Host "Open: $open   Waiting: $closed"
    if ($closed.Count -eq 0) { Write-Host "All up!"; break }
    Start-Sleep 5
}
```

Expected final state:
- **17200** — AppHost resource service
- **17202** — .NET server  
- **17204** — Session runner (may already be up from prior run)
- **17205** — Dashboard

Port 17201 (Postgres) is Aspire-managed — it's up when AppHost starts but doesn't always appear in `Get-NetTCPConnection`. Verify with: `docker ps | Select-String postgres`

---

## Step 4 — Print dashboard

Once port 17205 is open:

```
Dashboard: http://localhost:17205
Log:       C:\src\antiphon\logs\apphost.log
PID:       $(Get-Content C:\src\antiphon\logs\apphost.pid)
```

Open it: `Start-Process "http://localhost:17205"`

---

## Troubleshooting

### Docker not running
```powershell
# Check
docker info
# Fix: start Docker Desktop manually, wait for system tray icon, then re-run
```

### appsettings.json missing
```powershell
Copy-Item "C:\src\antiphon\server\appsettings.json.example" "C:\src\antiphon\server\appsettings.json"
# Then add API keys manually — do not run until configured
```

### Stale dcpctrl / dashboard from previous run
```powershell
Get-Process dcpctrl, Aspire.Dashboard -ErrorAction SilentlyContinue | Stop-Process -Force
# Also check for dotnet holding port 17200:
Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
    Where-Object { $_.LocalPort -eq 17200 } |
    ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
```

### Build fails: MSB3027 "file is locked by Antiphon.SessionRunner / server / client"
Daemon processes from a prior AppHost run are still alive and have the DLLs open. Kill them (see pre-flight above), then re-run.

### DCP network reconciler error in logs ("object already exists")
Usually non-fatal — DCP reconciler tries to re-create a network already in its internal state. Services typically still start. **But if Postgres does NOT start**, this error is the symptom of a Docker HNS issue (see below).

### Postgres container stuck in "Created" state (never starts)
**Cause**: Windows HNS (Host Network Service) is in a bad state — `docker network create` hangs indefinitely. This prevents DCP from setting up the container's network before starting it.

**Diagnose**:
```powershell
docker ps -a --filter name=DefaultConnection --format "table {{.Names}}\t{{.Status}}"
# Shows "Created" but never "Up" → Docker networking is broken

# Confirm: test network creation (should complete in <2s)
$j = Start-Job { docker network create test-hns-check 2>&1 }
$j | Wait-Job -Timeout 5
if ($j.State -eq 'Running') { "HNS broken — network create is hanging" }
Remove-Job $j -Force; docker network rm test-hns-check 2>&1 | Out-Null
```

**Fix**: Restart Docker Desktop. After it restarts, re-run `dev-aspire.ps1`.

**Note**: `dev-aspire.ps1` now auto-detects this and warns you. If you see the warning, restart Docker Desktop before retrying.

### Stale supervisor processes (session-runner rapid-restart loop)
```powershell
# Kill all orphaned run-daemon supervisors
Get-WmiObject Win32_Process -Filter "Name='pwsh.exe'" |
    Where-Object { $_.CommandLine -like '*run-daemon*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
# Then restart AppHost
```

Be precise — inspect `CommandLine` or `logs/*.supervisor.pid` before bulk-killing to avoid killing the current session.

### Port conflict on 17202 from a foreign Aspire project
```powershell
$proc = (Get-NetTCPConnection -LocalPort 17202 -State Listen -ErrorAction SilentlyContinue).OwningProcess
Get-Process -Id $proc | Select-Object Id, ProcessName, Path
Stop-Process -Id $proc -Force
```

### AppHost window closed before dashboard was ready / no log output
Check the log: `Get-Content C:\src\antiphon\logs\apphost.log -Tail 40`
If empty, the hidden pwsh wrapper failed to start. Re-run with a visible window to see the error:
```powershell
Start-Process pwsh -ArgumentList @('-NoLogo', '-File', 'C:\src\antiphon\dev-aspire.ps1') -WindowStyle Normal
```

### client/node_modules missing (npm error on build)
```powershell
Push-Location C:\src\antiphon\client; npm install; Pop-Location
# Then re-run dev-aspire.ps1
```
