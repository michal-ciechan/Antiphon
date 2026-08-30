<#
.SYNOPSIS
    Independently sample the Antiphon AppHost Watchdog Scheduled Task state.

    Registered as the per-user Scheduled Task "Antiphon AppHost Watchdog State
    Observer" by scripts/install-autostart.ps1 and fired every two minutes.
    Writes logs/apphost-watchdog-state.json so a recovered AppHost can raise
    Critical attention for a disabled/missing/unreadable watchdog. Never
    restarts, re-enables, or writes Antiphon's database.

    Disable the watchdog through scripts/set-apphost-maintenance.ps1 so the
    observer records maintenance=true and the server stays quiet. Direct Task
    Scheduler / Disable-ScheduledTask / schtasks.exe edits remain possible and
    are exactly what this observer detects.
.NOTES
    Keep this file ASCII-only: it may run under Windows PowerShell 5.1, which
    reads no-BOM .ps1 as CP1252 and mangles non-ASCII characters into parse
    errors.
#>
[CmdletBinding()]
param(
    [string]$TaskName = 'Antiphon AppHost Watchdog',
    [string]$Root,
    [string]$StateFile,
    [string]$MarkerFile,
    [string]$LogFile
)

$ErrorActionPreference = 'Continue'

if (-not $Root) { $Root = Split-Path $PSScriptRoot -Parent }
$logDir = Join-Path $Root 'logs'
if (-not $StateFile) { $StateFile = Join-Path $logDir 'apphost-watchdog-state.json' }
if (-not $MarkerFile) { $MarkerFile = Join-Path $logDir 'apphost.down-on-purpose' }
if (-not $LogFile) { $LogFile = Join-Path $logDir 'apphost-watchdog-state-observer.log' }

New-Item -ItemType Directory -Force $logDir | Out-Null

function Write-ObserverLog([string]$level, [string]$msg) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [watchdog-state] [$level] $msg"
    Write-Host $line
    Add-Content -LiteralPath $LogFile -Value $line -Encoding UTF8 -ErrorAction SilentlyContinue
}

function Read-PreviousState {
    if (-not (Test-Path -LiteralPath $StateFile)) { return $null }
    try {
        $raw = Get-Content -LiteralPath $StateFile -Raw -ErrorAction Stop
        if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
        return $raw | ConvertFrom-Json
    } catch {
        Write-ObserverLog 'WARN' "previous state unreadable ($($_.Exception.Message))"
        return $null
    }
}

function Get-WatchdogTaskState([string]$name) {
    try {
        $task = Get-ScheduledTask -TaskName $name -ErrorAction Stop
        if (-not $task) {
            return [pscustomobject]@{ State = 'Missing'; Detail = 'Get-ScheduledTask returned nothing' }
        }
        $taskState = [string]$task.State
        if ($taskState -eq 'Disabled') {
            return [pscustomobject]@{ State = 'Disabled'; Detail = "State=$taskState" }
        }
        return [pscustomobject]@{ State = 'Enabled'; Detail = "State=$taskState" }
    } catch {
        $msg = $_.Exception.Message
        if ($msg -match 'No MSFT_ScheduledTask' -or $msg -match 'cannot find' -or $msg -match 'not found') {
            return [pscustomobject]@{ State = 'Missing'; Detail = $msg }
        }
        return [pscustomobject]@{ State = 'Unknown'; Detail = $msg }
    }
}

$observedAt = [DateTime]::UtcNow.ToString('o')
$taskInfo = Get-WatchdogTaskState $TaskName
$state = $taskInfo.State
$healthy = $state -eq 'Enabled'
$maintenance = Test-Path -LiteralPath $MarkerFile

$previous = Read-PreviousState
$episodeId = $null
$disabledSinceUtc = $null
if (-not $healthy) {
    $sameEpisode = $previous -and
        ([string]$previous.state -eq $state) -and
        $previous.episodeId -and
        $previous.disabledSinceUtc
    if ($sameEpisode) {
        $episodeId = [string]$previous.episodeId
        $prevSince = $previous.disabledSinceUtc
        if ($prevSince -is [datetime]) {
            $disabledSinceUtc = ([datetime]$prevSince).ToUniversalTime().ToString('o')
        } else {
            $disabledSinceUtc = [string]$prevSince
        }
    } else {
        $episodeId = [guid]::NewGuid().ToString('D')
        $disabledSinceUtc = $observedAt
    }
}

$payload = [ordered]@{
    observedAtUtc    = $observedAt
    taskName         = $TaskName
    state            = $state
    healthy          = $healthy
    maintenance      = $maintenance
    disabledSinceUtc = $disabledSinceUtc
    episodeId        = $episodeId
    detail           = $taskInfo.Detail
}

$json = ($payload | ConvertTo-Json -Compress)
$tmp = "$StateFile.$PID.tmp"
try {
    [System.IO.File]::WriteAllText($tmp, $json + [Environment]::NewLine)
    [System.IO.File]::Copy($tmp, $StateFile, $true)
} finally {
    if (Test-Path -LiteralPath $tmp) {
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    }
}

Write-ObserverLog 'INFO' "state=$state healthy=$healthy maintenance=$maintenance episode=$episodeId"
exit 0
