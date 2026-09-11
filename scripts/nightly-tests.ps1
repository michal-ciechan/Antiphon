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

    Default suites come from tests/test-execution-policy.json. E2E is a manual
    exception. Headed tests stay off. Builds into the clone's own bin/; this
    script does not use an alternate output path.

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
