#requires -Version 5.1
<#
.SYNOPSIS
    Remove old alternate build outputs only with the producing process's creation records.
    A bin-* name, age, or disk owner marker alone grants no deletion authority. Scheduled
    invocations have no in-memory custody and leave legacy outputs for explicit inspection.

    Recurring cleanup runs via Windmill on server2 (u/lndcobra/antiphon_build_junk_cleanup,
    Mon 09:00 Europe/London), not a local Windows Scheduled Task.

    Unit 21 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: a Windows
    PowerShell 5.1 launcher kept because Windmill starts this file by path. The cleanup is C#
    (scripts/lib/Antiphon.Nightly/BuildJunkCleanup.cs) run as `antiphon-scripts cleanup-build-junk`
    through scripts/lib/antiphon-cli.ps1: a warm build of this checkout (a no-op when nothing
    changed), run from a copy. The parameters, the Windmill command line, the output lines and
    exit 0 are unchanged; 9 when the CLI cannot be built or started (one stderr line). A
    separate process holds no creation records, so it always prints "skip: producer creation
    records unavailable; outputs left in place". A producer that holds its records calls
    BuildJunkCleanup.Run in process with its ledger's removal; -CleanupContext stays for
    binding only, and a non-null value refuses with exit 3 (cleanup-context-unsupported).

    ASCII-only: parseable under Windows PowerShell 5.1.
#>
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent),
    [int]$SkipIfModifiedWithinMinutes = 60,
    [object]$CleanupContext = $null
)

$ErrorActionPreference = 'Continue'
if ($null -ne $CleanupContext) {
    [Console]::Error.WriteLine('cleanup-build-junk: cleanup-context-unsupported')
    exit 3
}

. (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')
$arguments = @(
    '--repo-root', $RepoRoot,
    '--skip-if-modified-within-minutes', [string]$SkipIfModifiedWithinMinutes
)
exit (Invoke-AntiphonCli -Command 'cleanup-build-junk' -Arguments $arguments)
