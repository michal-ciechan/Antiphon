#requires -Version 5.1
<#
.SYNOPSIS
A later green proves an identity only when that seed's source SHA contains the fix commit, or --Proven names that run.
.DESCRIPTION
Merge one or more stopped-run seeds into a single operator-seed-from-stopped-run
envelope and a deterministic report. Chunk rows and evidence come from the newest
seed. failedTests lists identities that are not yet proven. Launch defaults stay
on the existing prior-failure switches. When -Repo is set, a decision SHA already
contained in the last-failing run source (ancestor or cherry-pick trailer) is
ignored for proven and for fixed-pending-proof. With no remaining SHA the identity
stays open. --Proven remains the manual override. Proof requires the later green
source to contain a fix the failing run did not already contain. Without -Repo,
git proof stays off.
A test skipped inside a completed chunk looks green; that is an accepted seed-schema limit.
#>
# Named arguments are parsed from $args. This host's pwsh -File rejects a repeated
# [string[]] parameter, and the operator command repeats -Seed and -Proven.
$mergeArgs = New-Object System.Collections.Generic.List[string]
foreach ($mergeArg in $args) { $mergeArgs.Add([string]$mergeArg) }

$ErrorActionPreference = 'Stop'
# Resolved in the body: Windows PowerShell 5.1 -File does not bind $PSScriptRoot in a param() default.
$scriptRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
    $scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
}
# The merge is compiled (scripts/lib/Antiphon.Nightly/SeedMerge.cs): seed reads, decisions, the git proof, the report
# and the merged seed. Its lines are this command's output and its code is the exit code: 0 merged, 2 refused,
# 3 merged under -Strict with an identity unplaced or open without a decision.
try {
    . (Join-Path $scriptRoot 'lib/nightly-core.ps1')
    Assert-NightlyCoreAvailable 'merge-nightly-seeds'
    $merge = [Antiphon.Nightly.SeedMerge]::Run([string[]]$mergeArgs.ToArray())
} catch {
    Write-Host $_.Exception.Message
    exit 2
}
foreach ($line in $merge.Lines) { Write-Host $line }
exit $merge.Code
