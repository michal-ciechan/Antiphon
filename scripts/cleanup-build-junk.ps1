<#
.SYNOPSIS
    Inventory alternate build outputs; preserve directories without producer ownership evidence.

    CARD-0448: a bin-* name, ignored status, or age does not prove that its contents are
    regenerable. This entry point previously erased arbitrary ignored files with robocopy /MIR.
    Existing build producers do not issue deletion receipts, so this script retains all matches.
    It neither traverses directory links nor invokes a second shell to delete paths.

    The existing Windmill schedule may still invoke this inventory. No schedule is created here.
#>
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent),
    [int]$SkipIfModifiedWithinMinutes = 60
)

$ErrorActionPreference = 'Stop'
$root = (Get-Item -LiteralPath $RepoRoot -Force).FullName
$cutoff = (Get-Date).AddMinutes(-$SkipIfModifiedWithinMinutes)
$retained = 0
$pending = [System.Collections.Generic.Queue[object]]::new()
$pending.Enqueue(@{ Path = $root; Depth = 0 })
while ($pending.Count -gt 0) {
    $entry = $pending.Dequeue()
    foreach ($directory in Get-ChildItem -LiteralPath $entry.Path -Directory -Force -ErrorAction Stop) {
        if (($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
        if ($directory.Name -like 'bin-*') {
            $reason = if ($directory.LastWriteTime -gt $cutoff) { 'recently modified' } else { 'ownership evidence required' }
            Write-Output "retained ($reason): $($directory.FullName)"
            $retained++
        }
        if ($entry.Depth -lt 3 -and $directory.Name -notin @('.git', 'node_modules', 'workspace')) {
            $pending.Enqueue(@{ Path = $directory.FullName; Depth = $entry.Depth + 1 })
        }
    }
}
Write-Output "cleanup-build-junk: 0 dir(s) removed; $retained retained."
