<#
.SYNOPSIS
    Kill and restart the whole Antiphon Aspire stack (AppHost + server + client +
    dashboard), then wait for it to come back healthy.

    dev-aspire.ps1 launches a NEW AppHost but does not stop an old one, so a plain
    re-run collides with the still-running AppHost/server DLLs. This script does the
    teardown first, then calls dev-aspire.ps1.

    The always-on session-runner (port 17204) is PRESERVED - it is designed to
    survive AppHost restarts, and the new AppHost re-adopts it. (Server/client have
    no standalone restart; they live and die with the AppHost. To bounce just the
    session-runner, use restart-session-runner.ps1.)

    CARD-0075: this script is the only thing that KILLS anything, and until now it
    was the only actor blind to CARD-0011's launch lock. It now takes
    logs/apphost.restart.lock for the whole run and refuses (exit 3) if either that
    lock or logs/apphost.launch.lock is held. It also returns non-zero while the
    dev-aspire.ps1 it spawned may still be legitimately launching, so a refusal has
    to be distinguishable from a failure - an indistinguishable failure is what
    produces the retry that kills a DCP mid-startup.

.PARAMETER NoBuild            Pass -NoBuild through to dev-aspire.ps1 (skip restore/npm).
.PARAMETER TimeoutSec         Seconds to wait for the dashboard + backend health (default 150).
.PARAMETER LockMaxAgeMinutes  Ignore a lock older than this (default 15, the watchdog's number).

.OUTPUTS
    Exit codes:
      0  restarted, dashboard up and /health 200
      1  failed (build failed, or did not come up within TimeoutSec)
      3  REFUSED - another restart or launch is in flight; nothing was killed
      4  Aspire's DCP dependency check timed out (see the printed verdict)
.EXAMPLE
    pwsh -File scripts/restart-apphost.ps1
.NOTES
    Keep this file ASCII-only: it may run under Windows PowerShell 5.1, which reads
    no-BOM .ps1 as CP1252 and mangles non-ASCII characters into parse errors.
#>
param(
    [switch]$NoBuild,
    [int]$TimeoutSec = 150,
    [int]$LockMaxAgeMinutes = 15
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'apphost-common.ps1')

$root    = Split-Path $PSScriptRoot -Parent      # scripts/ -> repo root
$logDir  = Join-Path $root 'logs'
$pidFile = Join-Path $logDir 'apphost.pid'
$urlFile = Join-Path $logDir 'apphost-dashboard-url.txt'
$logFile = Join-Path $logDir 'apphost.log'
$devScript = Join-Path $root 'dev-aspire.ps1'
$launchLock  = Join-Path $logDir 'apphost.launch.lock'
$restartLock = Join-Path $logDir 'apphost.restart.lock'
$watchdogLog = Join-Path $logDir 'watchdog-apphost.log'

# Everything the AppHost owns. 17204 (session-runner) is deliberately EXCLUDED.
# 17209 = Storybook (AppHost-managed npm app; escapes the wrapper tree like the Vite client).
$appHostPorts = 17200, 17202, 17203, 17205, 17206, 17207, 17209
$sessionRunnerPort = 17204

function Read-PidFile([string]$file) {
    if (Test-Path $file) {
        $v = (Get-Content -LiteralPath $file -Raw -ErrorAction SilentlyContinue).Trim()
        if ($v -match '^\d+$') { return [int]$v }
    }
    return $null
}

function Get-PortPids([int]$p) {
    @((Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue).OwningProcess) |
        Where-Object { $_ } | Select-Object -Unique
}

Write-Host "Restarting Antiphon AppHost..." -ForegroundColor Cyan

# 0) Refuse if anything else is already launching or restarting. This runs BEFORE
#    any kill: a refusal must leave the stack exactly as it found it.
New-Item -ItemType Directory -Force $logDir | Out-Null

$launchInFlight = Test-AppHostLockActive -Path $launchLock -MaxAgeMinutes $LockMaxAgeMinutes -Label 'launch lock'
if ($launchInFlight) {
    Write-Host "REFUSED: a launch is in flight - $launchInFlight" -ForegroundColor Yellow
    Write-Host "  dev-aspire.ps1 is still bringing the stack up. Killing it now would time out its" -ForegroundColor DarkGray
    Write-Host "  DCP dependency check (CARD-0075). Wait for it, then re-run if it did not come up." -ForegroundColor DarkGray
    Write-Host "  Nothing was killed." -ForegroundColor DarkGray
    exit 3
}

$lock = New-AppHostLock -Path $restartLock -MaxAgeMinutes $LockMaxAgeMinutes -Label 'restart lock'
if (-not $lock.Acquired) {
    Write-Host "REFUSED: another restart is in flight - $($lock.Reason)" -ForegroundColor Yellow
    Write-Host "  A second teardown over the top of the first is exactly what times out the DCP" -ForegroundColor DarkGray
    Write-Host "  dependency check (CARD-0075). Wait for it to finish, then check http://localhost:17202/health." -ForegroundColor DarkGray
    Write-Host "  Nothing was killed. If the holder is genuinely gone, delete $restartLock." -ForegroundColor DarkGray
    exit 3
}

try {

    # Guard: which PID owns the session-runner port, so we never kill it.
    $srPid = Get-PortPids $sessionRunnerPort | Select-Object -First 1
    if ($srPid) { Write-Host "  preserving session-runner (PID $srPid on $sessionRunnerPort)" -ForegroundColor DarkGray }
    & (Join-Path $PSScriptRoot 'check-daemon-build.ps1')

    # 1) Kill the AppHost wrapper + dotnet AppHost tree.
    $appHostPid = Read-PidFile $pidFile
    if ($appHostPid) { taskkill /T /F /PID $appHostPid 2>&1 | Out-Null; Write-Host "  killed AppHost wrapper tree (PID $appHostPid)" }

    # 2) Free every AppHost-owned port (DCP children often escape the wrapper tree).
    #    Never touch the session-runner PID.
    Start-Sleep 1
    foreach ($port in $appHostPorts) {
        foreach ($owner in Get-PortPids $port) {
            if ($owner -ne $srPid) {
                Stop-Process -Id $owner -Force -ErrorAction SilentlyContinue
                Write-Host "  freed port $port (PID $owner)"
            }
        }
    }

    # 3) Clean stale DCP / dashboard processes left orphaned by an unclean exit.
    Get-Process dcpctrl, 'Aspire.Dashboard' -ErrorAction SilentlyContinue |
        Where-Object { $_.Id -ne $srPid } |
        ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue; Write-Host "  killed stale $($_.ProcessName) (PID $($_.Id))" }

    # 4) Reset launch signals so the wait below is not fooled by stale state.
    Remove-Item $urlFile -ErrorAction SilentlyContinue
    '' | Set-Content -LiteralPath $logFile -ErrorAction SilentlyContinue

    # 5) Relaunch in a normal window (detached) - dev-aspire.ps1 backgrounds the AppHost.
    Write-Host "  launching dev-aspire.ps1$(if ($NoBuild) { ' -NoBuild' })..." -ForegroundColor DarkGray
    $devArgs = @('-NoLogo', '-File', $devScript)
    if ($NoBuild) { $devArgs += '-NoBuild' }
    Start-Process pwsh -ArgumentList $devArgs -WindowStyle Normal

    # 6) Wait for the dashboard URL + backend health.
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $dashUrl = $null
    $backendOk = $false
    while ((Get-Date) -lt $deadline) {
        Start-Sleep 3
        if (-not $dashUrl -and (Test-Path $urlFile)) {
            $u = (Get-Content -LiteralPath $urlFile -Raw -ErrorAction SilentlyContinue).Trim()
            if ($u -match '^https?://') { $dashUrl = $u }
        }
        $verdict = Get-AppHostLogVerdict -LogPath $logFile
        if ($verdict.Kind -eq 'BuildFailed') {
            Write-Host "AppHost build FAILED - check: Get-Content '$logFile' -Tail 40" -ForegroundColor Red
            exit 1
        }
        if ($verdict.Kind -eq 'DcpDependencyTimeout') {
            # The exception names podman because podman is the only probe that had
            # returned when the deadline hit. Say what docker actually reported.
            Show-DcpTimeoutVerdict -LogPath $logFile -Evidence $verdict.Evidence -PodmanNoise $verdict.PodmanNoise `
                -LaunchLock $launchLock -RestartLock $restartLock -WatchdogLog $watchdogLog
            exit 4
        }
        if ($dashUrl) {
            try {
                $r = Invoke-WebRequest 'http://localhost:17202/health' -UseBasicParsing -TimeoutSec 5
                if ($r.StatusCode -eq 200) { $backendOk = $true; break }
            } catch { }
        }
    }

    if ($dashUrl -and $backendOk) {
        Write-Host "AppHost restarted - dashboard $dashUrl, backend healthy." -ForegroundColor Green
        exit 0
    } elseif ($dashUrl) {
        Write-Host "Dashboard up ($dashUrl) but backend health not confirmed in ${TimeoutSec}s." -ForegroundColor Yellow
        exit 1
    } else {
        Write-Host "AppHost did not come up within ${TimeoutSec}s - check '$logFile'." -ForegroundColor Red
        Write-Host "  NOTE: the dev-aspire.ps1 this script spawned may STILL be launching (its own" -ForegroundColor DarkGray
        Write-Host "  budget is longer than ${TimeoutSec}s). Do not re-run blind - re-running kills a" -ForegroundColor DarkGray
        Write-Host "  DCP that is still coming up (CARD-0075). Check $launchLock first." -ForegroundColor DarkGray
        exit 1
    }

} finally {
    # Released even on a terminating error: $ErrorActionPreference is Stop, so a
    # trailing Remove-Item would be skipped (the same reasoning dev-aspire.ps1:21
    # already records for the launch lock).
    Remove-AppHostLock $lock
}
