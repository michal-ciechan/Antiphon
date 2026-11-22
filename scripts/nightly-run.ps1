#requires -Version 5.1
<#
.SYNOPSIS
    Bootstrap for the Antiphon nightly: lock, sync the isolated clone, run
    tests, then file a card.

    CARD-0124 / CARD-0487. Windmill calls this script. After syncing
    C:\Antiphon\nightly\checkout to origin/master, the clone's own copy of this
    file is re-exec'd with -NoSync (unless the running build is the clone's own),
    so the version that runs is always the one on the ref.

    Recurring run is Windmill on server2 (u/lndcobra/antiphon_nightly_tests),
    not a local Windows Scheduled Task. Do not add one.

    W2-nightly S20: a Windows PowerShell 5.1 launcher. The run is C#
    (scripts/lib/Antiphon.Nightly/NightlyRun.cs) run as `antiphon-scripts nightly-run`
    through scripts/lib/antiphon-cli.ps1: a warm build of this checkout (a no-op when
    nothing changed), run from a copy. The parameters, the Windmill command line and
    the exit code are unchanged, and the last stdout line is still the compact JSON
    completion record (CARD-0545 D-10), written by the CLI. Exit 9: the CLI could not be
    built or started (one stderr line, then a record with refusal
    nightly-run-cli-unavailable). -PassThru returns the run's result (the run-core
    property names: ExitCode, RunId, Phase, Refusal, Sha, ...) read back from the CLI's
    --result-file, and never exits. A blank value is the CLI's default (Windows
    PowerShell 5.1 drops an empty native argument). No library is dot-sourced except the
    launcher itself.

    ASCII-only on purpose - parseable under Windows PowerShell 5.1.

.PARAMETER Suites
    Forwarded to the nightly-tests command (antiphon-scripts nightly-tests). Default comes from tests/test-execution-policy.json.

.PARAMETER NoReport
    Skip the report (Antiphon.Nightly NightlyReport; still writes last-run.json). Cannot claim delivery
    or advance last-complete-green.json.

.PARAMETER NoSync
    Skip clone/fetch/reset. Used by the self-update hop.

.PARAMETER CheckoutRoot
    Isolated clone. Default C:\Antiphon\nightly\checkout.

.PARAMETER LogRoot
    Parent of per-run stamp folders. Default <StateRoot>\logs.

.PARAMETER Ref
    Git ref to reset the clone to. Default master (origin/master after fetch).
    The master schedule uses that default. RC callers supply rc/YYYY-MM-DD or
    legacy release/rc-YYYYMMDDTHHMMSSZ; refs/heads/ prefixes are also accepted.

.PARAMETER StateRoot
    Producer-owned run state. Default C:\Antiphon\nightly. Manual/feature/NoReport
    tests pass a private root.

.PARAMETER Trigger
    scheduled (default), manual, feature, or rc. Trigger rc requires Profile rc.
    Only scheduled master complete-green advances last-complete-green.json.

.PARAMETER SeamsPath
    Kept for binding only. Seams never cross a process (the in-process tests build
    them in C#), so a non-empty value refuses with exit 3 (seams-path-unsupported)
    and the completion record is still printed. Never used by the schedule.

.PARAMETER Profile
    CARD-0599 D-5: nightly (default, the seven existing suites) or rc (those seven
    plus automated E2E). The selected profile is authoritative for required suites.

.PARAMETER ExpectedSha
    CARD-0599 D-3: the candidate's pinned full SHA. Required with -Profile rc; a
    checkout HEAD that differs refuses rather than retesting under the old identity.
    Together with the long-lived Ref, this pin determines the candidate identity.

.PARAMETER CandidateId
    RC identity: rc-YYYY-MM-DD-<first 12 lowercase SHA hex> for a long-lived Ref,
    or rc-YYYYMMDDTHHMMSSZ for a legacy stamp Ref. Omitted or whitespace values
    derive from Ref and ExpectedSha. A supplied value must match that identity.
    Derivation neither chooses private roots nor creates publication authority.

.PARAMETER ReleaseRoot
    Producer-owned release state. Default C:\Antiphon\releases. RC green is written
    under <ReleaseRoot>\candidates\<candidate-id>, never to the master state root.
    Required with -Profile rc. Pass C:\Antiphon\releases for the standard Windows layout.

.PARAMETER CoordinationRoot
    CARD-0599 D-4: root of the shared native-run lock both lanes acquire.
    Default C:\Antiphon\verification. Tests inject a private root.

.PARAMETER PassThru
    Return the run's result object instead of exiting with its code.

.EXAMPLE
    $pin = git -C C:\scratch\rc-checkout rev-parse HEAD
    .\scripts\nightly-run.ps1 -Profile rc -Trigger rc -Ref rc/2026-10-18 `
        -ExpectedSha $pin -CheckoutRoot C:\scratch\rc-checkout `
        -StateRoot C:\scratch\rc-state -LogRoot C:\scratch\rc-logs `
        -ReleaseRoot C:\scratch\rc-release -CoordinationRoot C:\scratch\rc-coordination `
        -NoReport
    Use an owned independent clone. Direct invocation does not create a candidate
    journal or publication authority, and RC success is not master readiness.
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
    [ValidateSet('Full', 'Shadow', 'ChangeAware')][string]$SelectionMode = 'Full',
    [string]$Repository = 'michal-ciechan/Antiphon',
    [string]$BaselineSha = '',
    [string]$BaselineReceiptPath = '',
    [string]$QualificationPath = '',
    [string]$TimingRoot = '',
    [string]$TrainManifestPath = '',
    [string]$ReleaseIntentPath = '',
    [string]$SelectionAttemptPath = '',
    [string]$SelectionAttemptDigest = '',
    [string]$SelectionPath = '',
    [string]$SelectionDigest = '',
    [int]$Attempt = 1,
    [string]$PriorFailuresPath = '',
    [string]$PriorFailuresDigest = '',
        [switch]$AcceptCrossCandidateSeed,
        [string]$SeedSourceCandidateId = '',
    [string]$SeamsPath = '',
    [string]$Trigger = 'scheduled',
    [string]$Profile = 'nightly',
    [string]$ExpectedSha = '',
    [string]$CandidateId = '',
    [string]$ReleaseRoot = '',
    [string]$CoordinationRoot = '',
    [string]$RunId = '',
    [string]$ContinueRunId = '',
    [int]$ContinueParentPid = 0,
    [string]$ContinueParentStartedAt = '',
    [switch]$PassThru,
    [switch]$WhatIf
)

$ErrorActionPreference = 'Continue'

# The record of a run that never started (NightlyRun.ResultRecord of a refusal: empty identity, the exit code, the refusal),
# and the result -PassThru returns for it (the run-core property names).
function New-NightlyRunLauncherRefusal {
    param([int]$Code, [string]$Refusal)
    [Console]::Out.WriteLine('{"nativeRunId":"","sha":"","ref":"","trigger":"","profile":"","candidateId":"","creditKind":"","localDueDate":"","policyHash":"","coverageComplete":false,"testsPassed":false,"reportDelivered":false,"exitCode":' + $Code + ',"summaryPath":"","refusal":"' + $Refusal + '"}')
    return [pscustomobject][ordered]@{
        ExitCode = $Code; RunId = $RunId; Phase = 'init'; Refusal = $Refusal; Sha = ''; coverageComplete = $false; testsPassed = $false
        reportDelivered = $false; trigger = $Trigger; profile = $Profile; ref = $Ref; CreditKind = ''; CandidateId = $CandidateId
        StateRoot = $StateRoot; LogDir = $null; OwnsLock = $false; LocalDueDate = ''; PolicyHash = ''; SummaryPath = ''; Selection = $null
    }
}

if (-not [string]::IsNullOrWhiteSpace($SeamsPath)) {
    [Console]::Error.WriteLine('nightly-run: seams-path-unsupported')
    $refused = New-NightlyRunLauncherRefusal -Code 3 -Refusal 'seams-path-unsupported'
    if ($PassThru) { return $refused }
    exit 3
}

. (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')
$arguments = @(
    '--suites', (@($Suites) -join ','),
    '--checkout-root', $CheckoutRoot,
    '--log-root', $LogRoot,
    '--ref', $Ref,
    '--remote-url', $RemoteUrl,
    '--state-root', $StateRoot,
    '--selection-mode', $SelectionMode,
    '--repository', $Repository,
    '--baseline-sha', $BaselineSha,
    '--baseline-receipt-path', $BaselineReceiptPath,
    '--qualification-path', $QualificationPath,
    '--timing-root', $TimingRoot,
    '--train-manifest-path', $TrainManifestPath,
    '--release-intent-path', $ReleaseIntentPath,
    '--selection-attempt-path', $SelectionAttemptPath,
    '--selection-attempt-digest', $SelectionAttemptDigest,
    '--selection-path', $SelectionPath,
    '--selection-digest', $SelectionDigest,
    '--attempt', [string]$Attempt,
    '--prior-failures-path', $PriorFailuresPath,
    '--prior-failures-digest', $PriorFailuresDigest,
    '--seed-source-candidate-id', $SeedSourceCandidateId,
    '--trigger', $Trigger,
    '--profile', $Profile,
    '--expected-sha', $ExpectedSha,
    '--candidate-id', $CandidateId,
    '--release-root', $ReleaseRoot,
    '--coordination-root', $CoordinationRoot,
    '--run-id', $RunId,
    '--continue-run-id', $ContinueRunId,
    '--continue-parent-pid', [string]$ContinueParentPid,
    '--continue-parent-started-at', $ContinueParentStartedAt
)
if ($NoReport) { $arguments += '--no-report' }
if ($NoSync) { $arguments += '--no-sync' }
if ($AcceptCrossCandidateSeed) { $arguments += '--accept-cross-candidate-seed' }
if ($WhatIf) { $arguments += '--what-if' }

# Always a result file: -PassThru reads it back, and its absence after exit 9 means the CLI never ran.
$resultFile = [IO.Path]::Combine([IO.Path]::GetTempPath(), 'antiphon-nightly-run-' + [guid]::NewGuid().ToString('N') + '.json')
$code = Invoke-AntiphonCli -Command 'nightly-run' -Arguments $arguments -ResultFile $resultFile

$result = $null
try {
    if ([IO.File]::Exists($resultFile)) {
        $text = [IO.File]::ReadAllText($resultFile)
        # Dates stay the strings the run wrote (pwsh 7.5 ConvertFrom-Json would make them DateTime; 5.1 never does).
        if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { $result = $text | ConvertFrom-Json -DateKind String }
        else { $result = $text | ConvertFrom-Json }
    }
} catch {
    $result = $null
} finally {
    try { if ([IO.File]::Exists($resultFile)) { [IO.File]::Delete($resultFile) } } catch { }
}
if ($null -eq $result -and $code -eq 9) {
    # The launcher's own failure (its stderr line is written): the CLI printed nothing, so the record is printed here.
    $result = New-NightlyRunLauncherRefusal -Code 9 -Refusal 'nightly-run-cli-unavailable'
}
if ($PassThru) {
    if ($null -eq $result) { $result = [pscustomobject]@{ ExitCode = $code; Refusal = '' } }
    return $result
}
exit $code
