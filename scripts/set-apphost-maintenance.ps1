<#
.SYNOPSIS
    Supported way to leave the Antiphon AppHost stack down on purpose.

    Entering maintenance creates logs/apphost.down-on-purpose FIRST, then
    disables the "Antiphon AppHost Watchdog" Scheduled Task. Leaving
    maintenance re-enables the watchdog FIRST, then removes the marker.
    The independent watchdog-state observer records maintenance=true so a
    recovered AppHost does not raise Critical attention for an intentional
    outage.

    Direct Task Scheduler / Disable-ScheduledTask / schtasks.exe edits still
    work and are what the observer detects as an unintentional disable.
.PARAMETER Clear
    Leave maintenance: re-enable the watchdog, then remove the marker.
.PARAMETER EnterIfAbsent
    Enter maintenance only when the marker is absent. The marker is created
    with create-new semantics. If it already exists, this exits 2 and does
    not disable the watchdog or remove the marker. The default enter path
    is unchanged.
.PARAMETER Root
    Repo root. Defaults to the parent of this script.
.PARAMETER WatchdogTaskName
    Watchdog Scheduled Task name. Default: "Antiphon AppHost Watchdog".
.NOTES
    Keep this file ASCII-only. Detection only after the marker is written;
    this script never restarts the AppHost.
#>
[CmdletBinding()]
param(
    [switch]$Clear,
    [switch]$EnterIfAbsent,
    [string]$Root,
    [string]$WatchdogTaskName = 'Antiphon AppHost Watchdog'
)

$ErrorActionPreference = 'Stop'

if (-not $Root) { $Root = Split-Path $PSScriptRoot -Parent }
$logDir = Join-Path $Root 'logs'
$marker = Join-Path $logDir 'apphost.down-on-purpose'
$observer = Join-Path $PSScriptRoot 'apphost-watchdog-state-observer.ps1'

New-Item -ItemType Directory -Force $logDir | Out-Null

function Write-Step($msg) { Write-Host "> $msg" }

if ($Clear -and $EnterIfAbsent) {
    Write-Host 'REFUSED: -Clear and -EnterIfAbsent cannot be combined.'
    exit 2
}

if ($EnterIfAbsent) {
    Write-Step "Entering AppHost maintenance only when the marker is absent."
    try {
        $stream = [System.IO.File]::Open(
            $marker,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None)
        try {
            $bytes = [System.Text.Encoding]::ASCII.GetBytes('down-on-purpose ' + [DateTime]::UtcNow.ToString('o'))
            $stream.Write($bytes, 0, $bytes.Length)
        } finally {
            $stream.Dispose()
        }
    } catch [System.IO.IOException] {
        Write-Host '  Marker already present. This run did not enter maintenance.'
        exit 2
    }
    Write-Host "  Wrote $marker"
    if (-not (Get-Command Get-ScheduledTask -ErrorAction SilentlyContinue)) {
        Write-Host '  Watchdog task cmdlet is not available; marker is in place.'
    } else {
        $task = Get-ScheduledTask -TaskName $WatchdogTaskName -ErrorAction SilentlyContinue
        if ($task) {
            Disable-ScheduledTask -TaskName $WatchdogTaskName | Out-Null
            Write-Host '  Watchdog task disabled.'
        } else {
            Write-Host "  Watchdog task '$WatchdogTaskName' is not registered; marker is in place."
        }
    }
    if (Test-Path -LiteralPath $observer) {
        & $observer -TaskName $WatchdogTaskName -Root $Root | Out-Host
    }
    Write-Host 'Done. This script does not start or stop the AppHost.'
    exit 0
}

if ($Clear) {
    Write-Step "Leaving AppHost maintenance (re-enable '$WatchdogTaskName', then remove marker)."
    $task = Get-ScheduledTask -TaskName $WatchdogTaskName -ErrorAction SilentlyContinue
    if ($task) {
        Enable-ScheduledTask -TaskName $WatchdogTaskName | Out-Null
        Write-Host "  Watchdog task enabled."
    } else {
        Write-Host "  Watchdog task '$WatchdogTaskName' is not registered; marker will still be removed."
    }
    if (Test-Path -LiteralPath $marker) {
        Remove-Item -LiteralPath $marker -Force
        Write-Host "  Removed $marker"
    } else {
        Write-Host "  Marker already absent."
    }
} else {
    Write-Step "Entering AppHost maintenance (create marker, then disable '$WatchdogTaskName')."
    if (-not (Test-Path -LiteralPath $marker)) {
        Set-Content -LiteralPath $marker -Value ("down-on-purpose " + [DateTime]::UtcNow.ToString('o')) -Encoding ASCII
        Write-Host "  Wrote $marker"
    } else {
        Write-Host "  Marker already present."
    }
    $task = Get-ScheduledTask -TaskName $WatchdogTaskName -ErrorAction SilentlyContinue
    if ($task) {
        Disable-ScheduledTask -TaskName $WatchdogTaskName | Out-Null
        Write-Host "  Watchdog task disabled."
    } else {
        Write-Host "  Watchdog task '$WatchdogTaskName' is not registered; marker is in place."
    }
}

if (Test-Path -LiteralPath $observer) {
    & $observer -TaskName $WatchdogTaskName -Root $Root | Out-Host
}

Write-Host "Done. This script does not start or stop the AppHost."
exit 0
