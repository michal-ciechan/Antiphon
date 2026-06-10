<#
.SYNOPSIS
    Kill and restart the always-on Antiphon session-runner daemon (port 17204).

    Works whether or not the Aspire AppHost is running - it talks to the same
    supervisor / pid / state files that run-daemon.ps1 and the "Antiphon Session
    Runner" Scheduled Task use, so it does NOT depend on the AppHost control API.

    Restart strategy:
      - Soft (default): kill the running service process; the live supervisor's
        restart loop relaunches it (~3s). 'dotnet run' rebuilds first, so a soft
        restart picks up new SessionRunner code.
      - If no supervisor is alive: (re)start it via the Scheduled Task.
      - Hard (-Hard): also kill the supervisor, then restart via the Scheduled
        Task. Use after a crash/wedge or when the supervisor is orphaned.

.PARAMETER Hard        Also kill the supervisor and re-own via the Scheduled Task.
.PARAMETER TimeoutSec  Seconds to wait for /health to return 200 (default 60).
.EXAMPLE
    pwsh -File scripts/restart-session-runner.ps1
    pwsh -File scripts/restart-session-runner.ps1 -Hard
#>
param(
    [switch]$Hard,
    [int]$TimeoutSec = 60
)

$ErrorActionPreference = 'Stop'

$root   = Split-Path $PSScriptRoot -Parent     # scripts/ -> repo root
$logDir = Join-Path $root 'logs'
$stateFile      = Join-Path $logDir 'session-runner.state'
$supervisorPid  = Join-Path $logDir 'session-runner.supervisor.pid'
$servicePidFile = Join-Path $logDir 'session-runner.service.pid'
$port     = 17204
$taskName = 'Antiphon Session Runner'

function Read-PidFile([string]$file) {
    if (Test-Path $file) {
        $v = (Get-Content -LiteralPath $file -Raw -ErrorAction SilentlyContinue).Trim()
        if ($v -match '^\d+$') { return [int]$v }
    }
    return $null
}

function Test-Port([int]$p) {
    [bool](Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue)
}

function Get-PortPid([int]$p) {
    (Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue).OwningProcess |
        Select-Object -First 1
}

function Stop-Tree([int]$processId, [string]$label) {
    if (-not $processId) { return }
    if (Get-Process -Id $processId -ErrorAction SilentlyContinue) {
        # /T kills the child tree (cmd.exe wrapper -> dotnet -> SessionRunner.exe)
        taskkill /T /F /PID $processId 2>&1 | Out-Null
        Write-Host "  killed $label (PID $processId)"
    }
}

Write-Host "Restarting session-runner (port $port)..." -ForegroundColor Cyan

# A prior dashboard "Stop" may have written 'stopped'; force desired-state running
# so the supervisor / task does not immediately exit again.
New-Item -ItemType Directory -Force $logDir | Out-Null
Set-Content -LiteralPath $stateFile -Value 'running' -Encoding UTF8

$supPid   = Read-PidFile $supervisorPid
$supAlive = $supPid -and (Get-Process -Id $supPid -ErrorAction SilentlyContinue)

# 1) Kill the running service. Try the recorded wrapper PID (cmd.exe tree), then
#    the actual listener on 17204, then any stray SessionRunner.exe by name.
Stop-Tree (Read-PidFile $servicePidFile) 'service wrapper'
Stop-Tree (Get-PortPid $port)            'port listener'
Get-Process Antiphon.SessionRunner -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-Tree $_.Id 'SessionRunner.exe' }

# 2) Decide who relaunches it.
if ($Hard -or -not $supAlive) {
    if ($Hard -and $supAlive) { Stop-Tree $supPid 'supervisor' }
    Remove-Item $servicePidFile, $supervisorPid -ErrorAction SilentlyContinue
    Write-Host "  no live supervisor (or -Hard) -> starting Scheduled Task '$taskName'"
    Start-ScheduledTask -TaskName $taskName
} else {
    Write-Host "  supervisor alive (PID $supPid) -> it will relaunch the service (~3s)"
}

# 3) Wait for health to come back.
$deadline = (Get-Date).AddSeconds($TimeoutSec)
$ok = $false
while ((Get-Date) -lt $deadline) {
    Start-Sleep 2
    if (Test-Port $port) {
        try {
            $r = Invoke-WebRequest "http://localhost:$port/health" -UseBasicParsing -TimeoutSec 5
            if ($r.StatusCode -eq 200) { $ok = $true; break }
        } catch { }
    }
}

if ($ok) {
    $svc = Get-PortPid $port
    Write-Host "session-runner healthy on $port (service PID $svc)" -ForegroundColor Green
    exit 0
} else {
    Write-Host "session-runner did NOT return healthy within ${TimeoutSec}s." -ForegroundColor Red
    Write-Host "  Check: Get-Content '$logDir\session-runner.log' -Tail 30" -ForegroundColor DarkGray
    exit 1
}
