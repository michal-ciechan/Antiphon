<#
.SYNOPSIS
    Start Antiphon via the Aspire AppHost (dashboard mode).
    - Postgres: Aspire-managed container (persistent named volume)
    - Server + Client: true daemons — survive AppHost exit, log to <repo>/logs/
    - Dashboard: http://localhost:15888  (or whatever Aspire prints)
    - Control API: http://localhost:17289/control/{name}/start|stop|restart
.PARAMETER NoBuild  Skip dotnet restore/build before starting.
#>
param([switch]$NoBuild)

$ErrorActionPreference = 'Stop'
$root        = $PSScriptRoot
$appHostDir  = "$root\Antiphon.AppHost"
$settingsFile = "$root\server\appsettings.json"

if (-not (Test-Path $settingsFile)) {
    Write-Error "server\appsettings.json not found. Copy appsettings.json.example first."
}

docker info 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Error "Docker Desktop is not running. Start it, wait for the tray icon, then re-run."
}

# Ensure log/backup dirs exist
foreach ($d in @("$root\logs", "$root\server\logs", "$root\server\logs\sessions", "$root\backups")) {
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Force $d | Out-Null }
}

# Worktrees must stay outside the repo
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

Write-Host "`n▶ Starting Aspire AppHost..." -ForegroundColor Cyan
Write-Host "  Dashboard URL will be printed below." -ForegroundColor DarkGray
Write-Host "  Server/client logs appear in the dashboard (no extra windows)." -ForegroundColor DarkGray
Write-Host "  Control API: http://localhost:17289/control/{server|client}/restart" -ForegroundColor DarkGray
Write-Host ""

Set-Location $appHostDir
dotnet run
