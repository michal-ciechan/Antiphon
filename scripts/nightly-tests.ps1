#requires -Version 5.1
<#
.SYNOPSIS
    Build and run the Antiphon nightly suites in an isolated checkout and write
    an honest machine-readable record of the outcome.

    CARD-0124 / CARD-0487. This script never clones, fetches, resets, or otherwise
    changes git state: scripts/nightly-run.ps1 owns sync of
    C:\Antiphon\nightly\checkout to origin/master. Do not point -RepoRoot at
    C:\src\Antiphon or at a path under C:\Antiphon\worktrees unless you pass
    -AllowSharedTree.

    Default suites come from the selected profile in tests/test-execution-policy.json.
    CARD-0599 D-5/D-6: -Profile nightly keeps the seven existing suites and leaves E2E
    out; -Profile rc requires those seven PLUS automated E2E, and in that profile OptIn
    alone no longer excludes a discovered E2E case - only a declared policy exclusion
    does. Headed/live canaries stay excluded by name, with reason and owner. Headed
    tests stay off. Builds into the clone's own bin/; this script does not use an
    alternate output path.

    Recurring run is Windmill on server2 (u/lndcobra/antiphon_nightly_tests),
    not a local Windows Scheduled Task. Do not add one.

    ASCII-only on purpose - this may be invoked by Windows PowerShell 5.1
    through the Windmill SSH path.
#>
param(
    [string]$RepoRoot = '',
    [string]$LogRoot = '',
    [string[]]$Suites,
    [string]$Sha = '',
    [string]$GitRef = 'origin/master',
    [string]$Trigger = 'scheduled',
    [string]$Profile = 'nightly',
    [string]$ExpectedSha = '',
    [string]$RunId = '',
    [string]$SeamsPath = '',
    [string]$PolicyPath = '',
    [switch]$AllowSharedTree,
    [switch]$WhatIf,
    [switch]$PassThru
)

$ErrorActionPreference = 'Continue'

$lib = Join-Path $PSScriptRoot 'lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'nightly-policy.ps1')
. (Join-Path $lib 'nightly-coverage.ps1')
. (Join-Path $lib 'release-gate.ps1')
. (Join-Path $lib 'nightly-tests-impl.ps1')

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}

$invoke = @{
    RepoRoot = $RepoRoot
    LogRoot = $LogRoot
    Suites = $Suites
    Sha = $Sha
    GitRef = $GitRef
    Trigger = $Trigger
    Profile = $Profile
    ExpectedSha = $ExpectedSha
    RunId = $RunId
    SeamsPath = $SeamsPath
    PolicyPath = $PolicyPath
    AllowSharedTree = $AllowSharedTree
    WhatIf = $WhatIf
}

$result = Invoke-AntiphonNightlyTests @invoke
if ($PassThru) { return $result }
$code = 1
if ($null -ne $result -and $null -ne $result.ExitCode) { $code = [int]$result.ExitCode }
if ($null -ne $result -and $result.Refusal) { $code = 3 }
exit $code
