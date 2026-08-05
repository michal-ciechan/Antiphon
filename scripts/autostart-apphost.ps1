<#
.SYNOPSIS
    Logon auto-start for the Antiphon Aspire AppHost (server 17202, client 17203,
    dashboard 17205, control API 17207).

    Registered as a per-user Scheduled Task by scripts/install-autostart.ps1.
    Complements the "Antiphon Session Runner" task: that one keeps the session-runner
    (17204) alive; this one brings up the rest of the stack so the app is usable
    straight after logon without running dev-aspire.ps1 by hand.

    Differences from just running dev-aspire.ps1 at logon:
      - WAITS for Docker Desktop. At logon Docker is still starting, and dev-aspire.ps1
        hard-errors ("Docker Desktop is not running") if it is not ready yet.
      - NO-OPs if the AppHost is already up (port 17202 listening), so a manual
        dev-aspire.ps1 run is never clobbered.
      - Passes -NoBrowser so no dashboard tab is opened on every logon.
      - Logs to logs/autostart-apphost.log.

    The AppHost itself is launched detached (hidden) by dev-aspire.ps1 and survives
    this script - and the Scheduled Task - exiting.
.PARAMETER NoBuild
    Skip dotnet restore + npm install (faster logon, but stale deps are not picked up).
.PARAMETER DockerTimeoutSec
    How long to wait for Docker Desktop to become responsive. Default 300 (5 min).
.PARAMETER HealthTimeoutSec
    How long to wait for the server health endpoint after launching. Default 180.
.NOTES
    Keep this file ASCII-only: it may run under Windows PowerShell 5.1, which reads
    no-BOM .ps1 as CP1252 and mangles non-ASCII characters into parse errors.
#>
[CmdletBinding()]
param(
    [switch]$NoBuild,
    [int]$DockerTimeoutSec = 300,
    [int]$HealthTimeoutSec = 180
)

$ErrorActionPreference = 'Continue'

$root      = Split-Path $PSScriptRoot -Parent      # scripts/ -> repo root
$logDir    = Join-Path $root 'logs'
$devScript = Join-Path $root 'dev-aspire.ps1'
$composeF  = Join-Path $root 'docker-compose.dev.yml'
New-Item -ItemType Directory -Force $logDir | Out-Null
$logFile   = Join-Path $logDir 'autostart-apphost.log'

$serverPort = 17202

function Write-Log([string]$msg) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [autostart-apphost] $msg"
    Write-Host $line
    Add-Content -LiteralPath $logFile -Value $line -Encoding UTF8 -ErrorAction SilentlyContinue
}

function Test-PortListening([int]$p) {
    [bool](Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue)
}

Write-Log "logon auto-start beginning (PID $PID)"

# -- 1. Already running? Never clobber a manual dev-aspire.ps1 ----------------
if (Test-PortListening $serverPort) {
    Write-Log "port $serverPort already listening - AppHost is up, nothing to do."
    exit 0
}

# -- 2. Wait for Docker Desktop ----------------------------------------------
Write-Log "waiting for Docker Desktop (up to ${DockerTimeoutSec}s)..."
$dockerDeadline = (Get-Date).AddSeconds($DockerTimeoutSec)
$dockerOk = $false
while ((Get-Date) -lt $dockerDeadline) {
    docker info 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { $dockerOk = $true; break }
    Start-Sleep 10
}
if (-not $dockerOk) {
    Write-Log "ERROR: Docker Desktop did not become responsive within ${DockerTimeoutSec}s - aborting."
    Write-Log "Fix: start Docker Desktop, then run dev-aspire.ps1 (or Start-ScheduledTask -TaskName 'Antiphon AppHost')."
    exit 1
}
Write-Log "Docker is responsive."

# -- 3. Ensure Postgres (idempotent; restart:unless-stopped usually has it up) --
docker compose -f $composeF up -d 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Log "WARNING: 'docker compose up -d' failed; continuing (AppHost pre-flight retries it)."
} else {
    Write-Log "Postgres container ensured."
}

# Wait for it to report healthy so the server does not race EF migrations.
$pgDeadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $pgDeadline) {
    $status = docker ps --filter name=antiphon-postgres --format "{{.Status}}" 2>&1
    if ($status -match 'healthy' -or ($status -match '^Up' -and $status -notmatch 'health')) { break }
    Start-Sleep 5
}
Write-Log "Postgres status: $(docker ps --filter name=antiphon-postgres --format '{{.Status}}' 2>&1)"

# -- 4. Launch the stack ------------------------------------------------------
# dev-aspire.ps1 backgrounds the AppHost (hidden) and exits once the dashboard is up.
$devArgs = @('-NonInteractive', '-NoLogo', '-ExecutionPolicy', 'Bypass', '-File', $devScript, '-NoBrowser')
if ($NoBuild) { $devArgs += '-NoBuild' }
Write-Log "launching dev-aspire.ps1 -NoBrowser$(if ($NoBuild) { ' -NoBuild' })..."

$psExe = (Get-Process -Id $PID).Path
& $psExe @devArgs *>> $logFile

# -- 5. Confirm health --------------------------------------------------------
$healthDeadline = (Get-Date).AddSeconds($HealthTimeoutSec)
$healthy = $false
while ((Get-Date) -lt $healthDeadline) {
    try {
        $r = Invoke-WebRequest "http://localhost:$serverPort/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $healthy = $true; break }
    } catch { }
    Start-Sleep 5
}

if ($healthy) {
    $dash = Join-Path $logDir 'apphost-dashboard-url.txt'
    $url  = if (Test-Path $dash) { (Get-Content $dash -Raw).Trim() } else { 'http://localhost:17205' }
    Write-Log "OK - backend healthy on :$serverPort, dashboard $url"
    exit 0
} else {
    Write-Log "ERROR: backend not healthy within ${HealthTimeoutSec}s - check logs/apphost.log"
    exit 1
}
