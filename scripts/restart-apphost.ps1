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

.PARAMETER NoBuild     Pass -NoBuild through to dev-aspire.ps1 (skip restore/npm).
.PARAMETER TimeoutSec  Seconds to wait for the dashboard + backend health (default 150).
.EXAMPLE
    pwsh -File scripts/restart-apphost.ps1
#>
param(
    [switch]$NoBuild,
    [int]$TimeoutSec = 150
)

$ErrorActionPreference = 'Stop'

$root    = Split-Path $PSScriptRoot -Parent      # scripts/ -> repo root
$logDir  = Join-Path $root 'logs'
$pidFile = Join-Path $logDir 'apphost.pid'
$urlFile = Join-Path $logDir 'apphost-dashboard-url.txt'
$logFile = Join-Path $logDir 'apphost.log'
$devScript = Join-Path $root 'dev-aspire.ps1'

# Everything the AppHost owns. 17204 (session-runner) is deliberately EXCLUDED.
# 17283 = Storybook (AppHost-managed npm app; escapes the wrapper tree like the Vite client).
$appHostPorts = 17200, 17202, 17203, 17205, 17206, 17207, 17283
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

# Guard: which PID owns the session-runner port, so we never kill it.
$srPid = Get-PortPids $sessionRunnerPort | Select-Object -First 1
if ($srPid) { Write-Host "  preserving session-runner (PID $srPid on $sessionRunnerPort)" -ForegroundColor DarkGray }

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
    if ((Get-Content $logFile -ErrorAction SilentlyContinue) -match 'The build failed') {
        Write-Host "AppHost build FAILED - check: Get-Content '$logFile' -Tail 40" -ForegroundColor Red
        exit 1
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
    exit 1
}
