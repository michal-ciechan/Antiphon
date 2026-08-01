<#
.SYNOPSIS
    Delete regenerable alternate build outputs (bin-verify/, bin-ptyhost/, bin-profile*/)
    across the repo. These are created by "build while daemons lock bin/" workarounds
    (dotnet build --property:OutputPath=bin-verify\) and are never referenced afterwards,
    but they bloat the tree and slow MSBuild evaluation when left to accumulate.

    Deliberately NOT touched:
      - bin/ and obj/           (live build outputs; the running daemons lock them)
      - workspace/              (live session-runner state: session logs, pty-host shadow
                                 copies that RUNNING pty-hosts execute from, manifests)

    Dirs modified in the last hour are skipped so an in-flight test run is never pulled
    out from under itself. Uses robocopy empty-mirror for deletion so paths longer than
    MAX_PATH cannot break it.

    Registered as the per-user Scheduled Task "Antiphon Build Junk Cleanup" (weekly +
    at logon) by scripts/install-cleanup-task.ps1. Safe to run manually at any time.
#>
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent),
    [int]$SkipIfModifiedWithinMinutes = 60
)

$ErrorActionPreference = 'Continue'
$patterns = @('bin-verify', 'bin-ptyhost', 'bin-profile*')
$cutoff = (Get-Date).AddMinutes(-$SkipIfModifiedWithinMinutes)

$empty = Join-Path $env:TEMP ("antiphon-empty-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $empty | Out-Null

$removed = 0
try {
    foreach ($pattern in $patterns) {
        $dirs = Get-ChildItem -Path $RepoRoot -Directory -Recurse -Filter $pattern -Depth 3 -ErrorAction SilentlyContinue
        foreach ($d in $dirs) {
            if ($d.LastWriteTime -gt $cutoff) {
                Write-Host "skip (recently modified): $($d.FullName)"
                continue
            }
            & robocopy $empty $d.FullName /MIR /NFL /NDL /NJH /NJS /W:0 /R:0 | Out-Null
            Remove-Item -LiteralPath $d.FullName -Recurse -Force -Confirm:$false -ErrorAction SilentlyContinue
            if (-not (Test-Path -LiteralPath $d.FullName)) {
                Write-Host "removed: $($d.FullName)"
                $removed++
            } else {
                Write-Host "partial (locked?): $($d.FullName)"
            }
        }
    }
} finally {
    Remove-Item -LiteralPath $empty -Recurse -Force -Confirm:$false -ErrorAction SilentlyContinue
}

Write-Host "cleanup-build-junk: $removed dir(s) removed."
