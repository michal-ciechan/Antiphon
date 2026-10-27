<#
.SYNOPSIS
    Remove old alternate build outputs only with the producing process's creation records.
    A bin-* name, age, or disk owner marker alone grants no deletion authority. Scheduled
    invocations have no in-memory custody and leave legacy outputs for explicit inspection.
    Producers may invoke this script in-process with their CheckpointCleanupContext after
    sealing completed outputs. The shared ledger refuses tracked, linked, changed or
    unrecorded entries and deletes recorded entries without recursive wildcard removal.

    Recurring cleanup runs via Windmill on server2 (u/lndcobra/antiphon_build_junk_cleanup,
    Mon 09:00 Europe/London), not a local Windows Scheduled Task.
#>
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent),
    [int]$SkipIfModifiedWithinMinutes = 60,
    [object]$CleanupContext = $null
)

$ErrorActionPreference = 'Stop'
$cutoff = (Get-Date).AddMinutes(-$SkipIfModifiedWithinMinutes)
$removed = 0
if ($null -eq $CleanupContext) {
    Write-Host 'skip: producer creation records unavailable; outputs left in place'
} else {
    $ledger = [Antiphon.Checkpoints.CheckpointOutputLedger]::new($CleanupContext)
    $dirs = Get-ChildItem -LiteralPath $RepoRoot -Directory -Recurse -Filter 'bin-*' -Depth 3
    foreach ($d in $dirs) {
        try {
            if (-not (Test-Path -LiteralPath $d.FullName)) { continue }
            if ($d.LastWriteTime -gt $cutoff) {
                Write-Host "skip (recently modified): $($d.FullName)"
                continue
            }
            $reason = $ledger.Remove($d.FullName, $false)
            if ($null -ne $reason) {
                Write-Host "skip: $($d.FullName) - $reason"
                continue
            }
            Write-Host "removed: $($d.FullName)"
            $removed++
        } catch {
            Write-Host "error (skipped): $($d.FullName) - $($_.Exception.Message)"
        }
    }
}
Write-Host "cleanup-build-junk: $removed dir(s) removed."
