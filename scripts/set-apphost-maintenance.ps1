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
.PARAMETER Root
    Repo root whose logs/ holds the marker. Defaults to the MAIN worktree of the
    checkout this script lives in (CARD-0644: shared-stack state lives in main's
    logs/, which is where the watchdog-state observer looks), so running it from a
    linked worktree still marks the one shared stack. Falls back to the parent of
    this script, with a warning, only when Git cannot name the main worktree.
.PARAMETER WatchdogTaskName
    Watchdog Scheduled Task name. Default: "Antiphon AppHost Watchdog".
.NOTES
    Keep this file ASCII-only. Detection only after the marker is written;
    this script never restarts the AppHost.
#>
[CmdletBinding()]
param(
    [switch]$Clear,
    [string]$Root,
    [string]$WatchdogTaskName = 'Antiphon AppHost Watchdog'
)

$ErrorActionPreference = 'Stop'

if (-not $Root) {
    . (Join-Path $PSScriptRoot 'apphost-common.ps1')
    $scriptRepoRoot = Split-Path $PSScriptRoot -Parent
    $classification = Get-AppHostWorktreeClassification -SourceRoot $scriptRepoRoot
    if ($classification.Verified) {
        $Root = $classification.MainWorktreeRoot
    } else {
        $Root = $scriptRepoRoot
        Write-Host "WARNING: could not resolve the main worktree ($($classification.Failure)); using $Root. Pass -Root <main checkout> if this is a linked worktree."
    }
}
$logDir = Join-Path $Root 'logs'
$marker = Join-Path $logDir 'apphost.down-on-purpose'
$observer = Join-Path $PSScriptRoot 'apphost-watchdog-state-observer.ps1'

New-Item -ItemType Directory -Force $logDir | Out-Null

function Write-Step($msg) { Write-Host "> $msg" }

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
