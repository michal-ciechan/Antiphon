#requires -Version 5.1
<#
.SYNOPSIS
    Switch or inspect what port 17203 is serving (CARD-0216 S1): the built bundle behind
    `vite preview`, or a live `vite` dev server for HMR-driven frontend work. Both modes run
    inside the same process, client/scripts/serve.mjs, which Aspire starts via `npm run serve`
    (Antiphon.AppHost/Program.cs). This script never restarts the AppHost - it only writes the
    mode file the shim polls every ~1s and (for -Mode) waits for the swap to land.

.PARAMETER Mode
    'built' or 'dev'. Writes logs/client.mode, then polls http://localhost:$Port/ until the
    response body matches the requested mode ('/@vite/client' present in the HTML response
    means dev mode) or -TimeoutSec elapses.

.PARAMETER Status
    Prints logs/client.state.json (written by the shim: mode, pid, since, lastBuildAt, status)
    plus a live probe of the port, without writing anything.

.PARAMETER Rebuild
    Built mode only. Touches logs/client.rebuild-requested (a sentinel file); the shim runs one
    more clean `vite build` in place, without restarting `vite preview` or the watcher, and
    without needing a mode switch. A no-op if the shim is currently in dev mode (nothing reads
    the sentinel there).

.PARAMETER TimeoutSec
    How long -Mode waits for the port to reflect the requested mode. Default 60 - a clean build
    is the slow path (built mode does one on every swap into it).

.PARAMETER Port
    Which port to probe. Default 17203 (the only port the shim ever runs on in this repo); a
    non-default value exists purely so tests can point this script at a port nothing is
    listening on without touching the real client.

.EXAMPLE
    pwsh -File scripts/client-mode.ps1 -Mode dev
.EXAMPLE
    pwsh -File scripts/client-mode.ps1 -Status
.EXAMPLE
    pwsh -File scripts/client-mode.ps1 -Rebuild
.NOTES
    Keep this file ASCII-only: it may run under Windows PowerShell 5.1, which reads no-BOM
    .ps1 as CP1252 and mangles non-ASCII characters into parse errors.
#>
param(
    [ValidateSet('built', 'dev')]
    [string]$Mode,
    [switch]$Status,
    [switch]$Rebuild,
    [int]$TimeoutSec = 60,
    [int]$Port = 17203
)

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$logsDir = Join-Path $root 'logs'
$modeFile = Join-Path $logsDir 'client.mode'
$stateFile = Join-Path $logsDir 'client.state.json'
$rebuildSentinel = Join-Path $logsDir 'client.rebuild-requested'

function Get-ClientProbe {
    param([int]$ProbePort)
    try {
        $r = Invoke-WebRequest "http://localhost:$ProbePort/" -UseBasicParsing -TimeoutSec 5
        $isDev = $r.Content -match [regex]::Escape('/@vite/client')
        return [pscustomobject]@{
            Reachable    = $true
            StatusCode   = $r.StatusCode
            ModeObserved = if ($isDev) { 'dev' } else { 'built' }
            Error        = $null
        }
    } catch {
        return [pscustomobject]@{
            Reachable    = $false
            StatusCode   = $null
            ModeObserved = $null
            Error        = $_.Exception.Message
        }
    }
}

if (-not $Mode -and -not $Status -and -not $Rebuild) {
    Write-Host 'Specify -Mode built|dev, -Status, or -Rebuild.' -ForegroundColor Yellow
    exit 1
}

New-Item -ItemType Directory -Force $logsDir | Out-Null

if ($Rebuild) {
    $ts = [datetime]::UtcNow.ToString('o')
    Set-Content -LiteralPath $rebuildSentinel -Value $ts -Encoding UTF8
    Write-Host "Rebuild requested at $ts (built mode only - a no-op right now if the shim is in dev mode)."
    if (-not $Mode -and -not $Status) { exit 0 }
}

if ($Mode) {
    Set-Content -LiteralPath $modeFile -Value $Mode -Encoding UTF8 -NoNewline
    Write-Host "Wrote $modeFile = $Mode"
    Write-Host "Waiting up to ${TimeoutSec}s for :$Port to reflect '$Mode' mode..."

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $probe = $null
    while ((Get-Date) -lt $deadline) {
        $probe = Get-ClientProbe -ProbePort $Port
        if ($probe.Reachable -and $probe.ModeObserved -eq $Mode) { break }
        Start-Sleep 1
    }

    if ($probe -and $probe.Reachable -and $probe.ModeObserved -eq $Mode) {
        Write-Host "OK: :$Port is serving '$Mode' mode (HTTP $($probe.StatusCode))." -ForegroundColor Green
        if (-not $Status) { exit 0 }
    } elseif ($probe -and $probe.Reachable) {
        Write-Host "TIMEOUT: :$Port is reachable but still reports '$($probe.ModeObserved)' mode after ${TimeoutSec}s." -ForegroundColor Yellow
        if (-not $Status) { exit 1 }
    } else {
        Write-Host "TIMEOUT: :$Port did not become reachable within ${TimeoutSec}s ($($probe.Error))." -ForegroundColor Red
        if (-not $Status) { exit 1 }
    }
}

if ($Status) {
    if (Test-Path $stateFile) {
        $state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
        Write-Host "State file ($stateFile):"
        Write-Host ("  mode        : {0}" -f $state.mode)
        Write-Host ("  status      : {0}" -f $state.status)
        Write-Host ("  pid         : {0}" -f $state.pid)
        Write-Host ("  since       : {0}" -f $state.since)
        Write-Host ("  lastBuildAt : {0}" -f $state.lastBuildAt)
    } else {
        Write-Host "No state file at $stateFile yet (the shim writes this on its first poll)." -ForegroundColor Yellow
    }

    $probe = Get-ClientProbe -ProbePort $Port
    if ($probe.Reachable) {
        Write-Host ("Live probe   : reachable, HTTP {0}, observed mode = {1}" -f $probe.StatusCode, $probe.ModeObserved)
    } else {
        Write-Host ("Live probe   : NOT reachable ({0})" -f $probe.Error) -ForegroundColor Red
    }
    exit 0
}
