#requires -Version 7.0
param([string]$Case, [string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/lib/nightly-common.ps1"
. "$PSScriptRoot/lib/nightly-policy.ps1"
. "$PSScriptRoot/lib/c487-harness.ps1"
$repoRoot = Split-Path -Parent $PSScriptRoot
$ResultsDirectory = New-C487Root -ResultsDirectory $ResultsDirectory

function Test-C1313_ProjectAndCatalogUniverse {
    $policy = Read-NightlyExecutionPolicy -RepoRoot $repoRoot
    $census = Test-NightlyPolicyProjectUniverse -RepoRoot $repoRoot -PolicyObject $policy.Object
    Assert-C487 -Cond ($census.Ok -and $census.Disk.Count -eq 11) -Name 'C1313 project-universe' -Detail ($census | ConvertTo-Json -Compress)
}

& "Test-$Case"
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 0
