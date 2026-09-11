#requires -Version 5.1
<#
.SYNOPSIS
    Bootstrap for the Antiphon nightly: lock, sync the isolated clone, run
    tests, then file a card.

    CARD-0124 / CARD-0487. Windmill calls this script. After syncing
    C:\Antiphon\nightly\checkout to origin/master, if the clone's copy of this
    file differs, that copy is re-exec'd with -NoSync so the version that runs
    is always the one on the ref.

    Recurring run is Windmill on server2 (u/lndcobra/antiphon_nightly_tests),
    not a local Windows Scheduled Task. Do not add one.

    ASCII-only on purpose - parseable under Windows PowerShell 5.1.

.PARAMETER Suites
    Forwarded to nightly-tests.ps1. Default comes from tests/test-execution-policy.json.

.PARAMETER NoReport
    Skip nightly-report.ps1 (still writes last-run.json). Cannot claim delivery
    or advance last-complete-green.json.

.PARAMETER NoSync
    Skip clone/fetch/reset. Used by the self-update hop.

.PARAMETER CheckoutRoot
    Isolated clone. Default C:\Antiphon\nightly\checkout.

.PARAMETER LogRoot
    Parent of per-run stamp folders. Default <StateRoot>\logs.

.PARAMETER Ref
    Git ref to reset the clone to. Default master (origin/master after fetch).
    The schedule never passes this.

.PARAMETER StateRoot
    Producer-owned run state. Default C:\Antiphon\nightly. Manual/feature/NoReport
    tests pass a private root.

.PARAMETER Trigger
    scheduled (default), manual, or feature. Only scheduled master complete-green
    advances last-complete-green.json.

.PARAMETER SeamsPath
    Optional injectable seams script for fixtures. Never used by the schedule.
#>
param(
    [string[]]$Suites,
    [switch]$NoReport,
    [switch]$NoSync,
    [string]$CheckoutRoot = 'C:\Antiphon\nightly\checkout',
    [string]$LogRoot = '',
    [string]$Ref = 'master',
    [string]$RemoteUrl = 'https://github.com/michal-ciechan/Antiphon',
    [string]$StateRoot = 'C:\Antiphon\nightly',
    [string]$SeamsPath = '',
    [string]$Trigger = 'scheduled',
    [string]$RunId = '',
    [string]$ContinueRunId = '',
    [int]$ContinueParentPid = 0,
    [string]$ContinueParentStartedAt = '',
    [switch]$PassThru,
    [switch]$WhatIf
)

$ErrorActionPreference = 'Continue'

$lib = Join-Path $PSScriptRoot 'lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'nightly-policy.ps1')
. (Join-Path $lib 'nightly-coverage.ps1')
. (Join-Path $lib 'nightly-run-impl.ps1')

$invoke = @{
    Suites = $Suites
    NoReport = $NoReport
    NoSync = $NoSync
    CheckoutRoot = $CheckoutRoot
    LogRoot = $LogRoot
    Ref = $Ref
    RemoteUrl = $RemoteUrl
    StateRoot = $StateRoot
    SeamsPath = $SeamsPath
    Trigger = $Trigger
    RunId = $RunId
    ContinueRunId = $ContinueRunId
    ContinueParentPid = $ContinueParentPid
    ContinueParentStartedAt = $ContinueParentStartedAt
    PassThru = $PassThru
    WhatIf = $WhatIf
}

$result = Invoke-AntiphonNightlyRun @invoke
if ($PassThru) { return $result }
$code = 1
if ($null -ne $result -and $null -ne $result.ExitCode) { $code = [int]$result.ExitCode }
exit $code
