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

Write-Host ""
Write-Host "AppHost running in background (PID $($appHostProc.Id))." -ForegroundColor Green
Start-Sleep 3
