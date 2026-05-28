<#
.SYNOPSIS
    Start Antiphon via the Aspire AppHost (dashboard mode).
    - Postgres: Aspire-managed container (persistent named volume)
    - Server + Client: true daemons — survive AppHost exit, log to <repo>/logs/
    - Dashboard: URL parsed from log (Aspire assigns it; opens automatically)
    - OTLP:      http://localhost:17206  (fixed)
    - Control API: http://localhost:17207/control/{name}/start|stop|restart
    - Script exits after dashboard is ready (AppHost continues in background).
.PARAMETER NoBuild  Skip dotnet restore/build before starting.
#>
param([switch]$NoBuild)

$ErrorActionPreference = 'Stop'
$root         = $PSScriptRoot
$appHostDir   = "$root\Antiphon.AppHost"
$settingsFile = "$root\server\appsettings.json"

if (-not (Test-Path $settingsFile)) {
    Write-Error "server\appsettings.json not found. Copy appsettings.json.example first."
}

docker info 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Error "Docker Desktop is not running. Start it, wait for the tray icon, then re-run."
}

# ── Pre-flight: test Docker network creation ───────────────────────────────
Write-Host "`n▶ Testing Docker network health..." -ForegroundColor Cyan
$dockerNetworkHealthy = $false
$testNetName = "aspire-preflight-$(Get-Random)"
$netJob = Start-Job { param($n) & docker network create $n 2>&1; $LASTEXITCODE } -ArgumentList $testNetName
$netCompleted = $netJob | Wait-Job -Timeout 8
if ($netCompleted -and $netCompleted.State -eq 'Completed') {
    $dockerNetworkHealthy = $true
    $rmJob = Start-Job { param($n) & docker network rm $n 2>&1 } -ArgumentList $testNetName
    $rmJob | Wait-Job -Timeout 5 | Out-Null; Remove-Job $rmJob -Force
    Remove-Job $netJob -Force
    Write-Host "  Docker networking: OK" -ForegroundColor DarkGray
} else {
    Remove-Job $netJob -Force -ErrorAction SilentlyContinue
    Write-Host "" -ForegroundColor Yellow
    Write-Host "  WARNING: Docker network creation is unresponsive (>8s)." -ForegroundColor Yellow
    Write-Host "  Postgres will NOT start. Restart Docker Desktop, then re-run this script." -ForegroundColor Red
    Write-Host ""
}

# ── Pre-flight: remove stale Aspire-managed postgres containers ────────────
Write-Host "`n▶ Removing stale containers..." -ForegroundColor Cyan
$staleIds = docker ps -aq --filter 'name=DefaultConnection' 2>&1
if ($staleIds) {
    foreach ($id in $staleIds) {
        docker rm -f $id 2>&1 | Out-Null
    }
    Write-Host "  Removed stale container(s)" -ForegroundColor DarkGray
} else {
    Write-Host "  None found" -ForegroundColor DarkGray
}

# ── Pre-flight: clean up old Aspire DCP temp state dirs ───────────────────
$aspireDirs = Get-ChildItem 'C:\Users\lndco\AppData\Local\Temp' -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like 'aspire.*' -and $_.LastWriteTime -lt (Get-Date).AddHours(-2) }
if ($aspireDirs) {
    foreach ($d in $aspireDirs) {
        Remove-Item $d.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Host "  Cleaned $($aspireDirs.Count) old DCP temp dir(s)" -ForegroundColor DarkGray
}

foreach ($d in @("$root\logs", "$root\server\logs", "$root\server\logs\sessions", "$root\backups")) {
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Force $d | Out-Null }
}

$worktreeDir = 'C:\Antiphon\worktrees'
if (-not (Test-Path $worktreeDir)) { New-Item -ItemType Directory -Force $worktreeDir | Out-Null }

if (-not $NoBuild) {
    Write-Host "`n▶ Restoring AppHost dependencies..." -ForegroundColor Cyan
    dotnet restore $appHostDir
    if ($LASTEXITCODE -ne 0) { Write-Error "dotnet restore failed." }

    Write-Host "`n▶ Installing client npm packages..." -ForegroundColor Cyan
    Push-Location "$root\client"
    npm install
    if ($LASTEXITCODE -ne 0) { Write-Error "npm install failed." }
    Pop-Location
}

$logFile = "$root\logs\apphost.log"
$pidFile = "$root\logs\apphost.pid"

Write-Host "`n▶ Starting Aspire AppHost (background)..." -ForegroundColor Cyan
Write-Host "  Control : http://localhost:17207/control/{server|client}/restart" -ForegroundColor DarkGray
Write-Host "  OTLP    : http://localhost:17206" -ForegroundColor DarkGray
Write-Host "  Log     : $logFile" -ForegroundColor DarkGray
Write-Host ""

# Truncate old log so we don't match stale lines
'' | Set-Content $logFile

# Launch AppHost detached in a hidden window so this script can exit cleanly
$encodedCmd = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes(
    "Set-Location '$appHostDir'; dotnet run 2>&1 | Tee-Object -FilePath '$logFile'"
))
$appHostProc = Start-Process pwsh -ArgumentList @(
    '-NonInteractive', '-NoLogo', '-EncodedCommand', $encodedCmd
) -WindowStyle Hidden -PassThru

$appHostProc.Id | Set-Content $pidFile
Write-Host "  AppHost PID : $($appHostProc.Id)" -ForegroundColor DarkGray
Write-Host "  Waiting for dashboard URL in log (up to 90s)..." -ForegroundColor DarkGray
Write-Host ""

# Parse dashboard URL from log — Aspire prints:
#   "Login to the dashboard at http://..." or "Now listening on: http://..."
# The dashboard process writes its URL back into the AppHost log.
$timeout = 90; $elapsed = 0; $dashboardUrl = $null
while ($elapsed -lt $timeout) {
    Start-Sleep 3; $elapsed += 3

    if (Test-Path $logFile) {
        $lines = Get-Content $logFile -ErrorAction SilentlyContinue
        # Look for dashboard login URL line
        $match = $lines | Select-String 'Login to the dashboard at (https?://\S+)' | Select-Object -First 1
        if ($match) {
            $dashboardUrl = $match.Matches[0].Groups[1].Value -replace '/login.*',''
            break
        }
        # Fallback: find the dashboard process and get its HTTP port
        $dash = Get-Process 'Aspire.Dashboard' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($dash) {
            $otlpPort = 17206
            $uiPort = Get-NetTCPConnection -State Listen -OwningProcess $dash.Id -ErrorAction SilentlyContinue |
                Where-Object { $_.LocalPort -ne $otlpPort } |
                Select-Object -ExpandProperty LocalPort -First 1
            if ($uiPort) {
                $dashboardUrl = "http://localhost:$uiPort"
                break
            }
        }
    }
    Write-Host "  ...($elapsed s)" -ForegroundColor DarkGray
}

if ($dashboardUrl) {
    Write-Host ""
    Write-Host "  Dashboard : $dashboardUrl" -ForegroundColor Green
    $dashboardUrl | Set-Content "$root\logs\apphost-dashboard-url.txt"
    Start-Process $dashboardUrl
} else {
    Write-Host ""
    Write-Host "  Dashboard URL not found after ${timeout}s." -ForegroundColor Yellow
    Write-Host "  Check log: Get-Content '$logFile' -Tail 30" -ForegroundColor DarkGray
}

# ── Post-launch: verify Postgres started ──────────────────────────────────
Write-Host "`n▶ Checking Postgres..." -ForegroundColor Cyan
$pgDeadline = (Get-Date).AddSeconds(45)
$pgOK = $false
while ((Get-Date) -lt $pgDeadline) {
    Start-Sleep 5
    $runningPg = docker ps --filter name=DefaultConnection --format "{{.Status}}" 2>&1 | Where-Object { $_ -like "Up*" }
    if ($runningPg) { $pgOK = $true; Write-Host "  Postgres: Up" -ForegroundColor Green; break }
    $stuckPg = docker ps -a --filter name=DefaultConnection --filter status=created --format "{{.Names}}" 2>&1
    if ($stuckPg) {
        Write-Host "  Postgres container exists but has not started yet..." -ForegroundColor DarkGray
    }
}
if (-not $pgOK) {
    Write-Host ""
    Write-Host "  WARNING: Postgres did not start within 45s." -ForegroundColor Yellow
    $stuck = docker ps -a --filter name=DefaultConnection --format "{{.Names}}\t{{.Status}}" 2>&1
    if ($stuck) { Write-Host "  Container state: $stuck" -ForegroundColor DarkGray }
    Write-Host "  Likely cause: Docker network creation failed (Windows HNS issue)." -ForegroundColor Yellow
    Write-Host "  Fix: Restart Docker Desktop, then re-run this script." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "AppHost running in background (PID $($appHostProc.Id))." -ForegroundColor Green
Start-Sleep 3
