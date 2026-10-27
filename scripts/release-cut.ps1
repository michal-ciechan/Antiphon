#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1311: the RC lane entrypoint. Requires -Ref rc/YYYY-MM-DD.

    1. scripts/release-candidate.ps1 journals the existing remote tip. It does
       not cut origin/master and it does not allocate a stamp.
    2. scripts/nightly-run.ps1 -Profile rc -Trigger rc -Ref <candidate>
       -ExpectedSha <sha> runs the rc profile against the candidate checkout
       and private state root.
    3. scripts/publish-release.ps1 runs only on RC complete-green.

    A red or incomplete RC publishes nothing and keeps its evidence. This
    script never moves a tested ref or retries a failed run in place.

    Nothing here writes the master attempt, green, monitor or qualification
    receipt, and nothing here restarts AppHost or the runner.

    ASCII-only on purpose - parseable under Windows PowerShell 5.1.
#>
param(
    [string]$ReleaseRoot = 'C:\Antiphon\releases',
    [string]$CoordinationRoot = 'C:\Antiphon\verification',
    [string]$RemoteUrl = 'https://github.com/michal-ciechan/Antiphon',
    [string]$Repository = 'michal-ciechan/Antiphon',
    [string]$RunId = '',
    [string]$ScheduleSlot = '',
    [string]$JobId = '',
    [string]$Ref = '',
    [ValidateSet('Full', 'Shadow', 'ChangeAware')][string]$SelectionMode = 'Full',
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
    [string]$SeamsPath = '',
    [switch]$SkipPublish,
    [switch]$WhatIf,
    [switch]$PassThru
)

$ErrorActionPreference = 'Continue'

$lib = Join-Path $PSScriptRoot 'lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'release-gate.ps1')
. (Join-Path $lib 'release-selection.ps1')

function Write-CutLine { param([string]$Message) Write-Host ('[release-cut] {0}' -f $Message) }

$checkout = Join-Path $ReleaseRoot 'checkout'
$record = [ordered]@{
    candidateId = ''
    ref = ''
    sha = ''
    scheduleSlot = $ScheduleSlot
    windmillJobId = $JobId
    tested = $false
    published = $false
    tag = ''
    refusal = ''
    exitCode = 1
}

$wanted = Test-ReleaseGateCandidateRef -Ref $Ref
if ([string]::IsNullOrWhiteSpace($Ref) -or -not $wanted.Ok -or $wanted.Kind -ne 'long-lived') {
    if ([string]::IsNullOrWhiteSpace($Ref)) { $record.refusal = 'rc-ref-required' }
    elseif (-not $wanted.Ok) { $record.refusal = [string]$wanted.Reason }
    else { $record.refusal = 'rc-ref-required' }
    $record.exitCode = 3
    Write-CutLine ('candidate cut refused ({0}).' -f $record.refusal)
    if ($PassThru) { return [pscustomobject]$record }
    Write-Host (ConvertTo-Json -InputObject $record -Compress)
    exit 3
}

try { [void](Assert-ReleaseSelectionCapability $SelectionMode) }
catch {
    $record.refusal=$_.Exception.Message; $record.exitCode=3
    if ($PassThru) { return [pscustomobject]$record }
    Write-Host (ConvertTo-Json $record -Compress); exit 3
}

$cut = & (Join-Path $PSScriptRoot 'release-candidate.ps1') -ReleaseRoot $ReleaseRoot `
    -CheckoutRoot $checkout -RemoteUrl $RemoteUrl -Ref $Ref -ScheduleSlot $ScheduleSlot `
    -SeamsPath $SeamsPath -WhatIf:$WhatIf -PassThru
$record.candidateId = [string]$cut.CandidateId
$record.ref = [string]$cut.Ref
$record.sha = [string]$cut.Sha
if ([int]$cut.ExitCode -ne 0) {
    $record.refusal = [string]$cut.Refusal
    $record.exitCode = [int]$cut.ExitCode
    Write-CutLine ('candidate cut refused ({0}).' -f $record.refusal)
    if ($PassThru) { return [pscustomobject]$record }
    Write-Host (ConvertTo-Json -InputObject $record -Compress)
    exit ([int]$record.exitCode)
}

$candidateRoot = Get-ReleaseGateCandidateRoot -ReleaseRoot $ReleaseRoot -CandidateId $record.candidateId
$stateRoot = Join-Path $candidateRoot 'state'
New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($RunId)) { $RunId = New-NightlyRunId }
if ([string]::IsNullOrWhiteSpace($TimingRoot)) {
    if (-not (Get-Command Resolve-QualificationTimingRoot -ErrorAction SilentlyContinue)) {
        . (Join-Path $lib 'test-phase-evidence.ps1')
    }
    $TimingRoot = [string](Resolve-QualificationTimingRoot -TimingRoot '' -LogRoot (Join-Path $candidateRoot 'logs') -RunId $RunId)
}
try {
    if (-not $WhatIf) {
        # Candidate journalling reads the remote ref; acquire its pinned object before
        # selection without changing the long-lived ref or repinning the journal.
        $fetch = Invoke-ReleaseGateGit -WorkingDirectory $checkout -Arguments @('fetch','origin',$record.sha)
        if ($fetch.ExitCode -ne 0) { throw 'selection-candidate-fetch' }
        $request = @{RepositoryRoot=$checkout;Repository=$Repository;ReleaseRoot=$ReleaseRoot;CandidateSha=$record.sha
            CandidateId=$record.candidateId;CandidateRef=$record.ref;Profile='rc';RunId=$RunId
            SelectionMode=$SelectionMode;BaselineSha=$BaselineSha;BaselineReceiptPath=$BaselineReceiptPath;QualificationPath=$QualificationPath;TimingRoot=$TimingRoot;TrainManifestPath=$TrainManifestPath;ReleaseIntentPath=$ReleaseIntentPath;SelectionAttemptPath=$SelectionAttemptPath;SelectionAttemptDigest=$SelectionAttemptDigest;SelectionPath=$SelectionPath;SelectionDigest=$SelectionDigest;Attempt=$Attempt}
        $bound = New-ReleaseSelectionAttempt $request
        $SelectionAttemptPath=$bound.SelectionAttemptPath; $SelectionAttemptDigest=$bound.SelectionAttemptDigest
        $SelectionPath=$bound.SelectionPath; $SelectionDigest=$bound.SelectionDigest; $BaselineSha=$bound.BaselineSha
        $record.selection = $bound.Selection
    }
} catch {
    $record.refusal=$_.Exception.Message; $record.exitCode=3
    if ($PassThru) { return [pscustomobject]$record }
    Write-Host (ConvertTo-Json $record -Compress); exit 3
}

$runArgs = @{
    RunId = $RunId
    Repository = $Repository
    SelectionMode = $SelectionMode
    BaselineSha = $BaselineSha
    BaselineReceiptPath = $BaselineReceiptPath
    QualificationPath = $QualificationPath
    TimingRoot = $TimingRoot
    TrainManifestPath = $TrainManifestPath
    ReleaseIntentPath = $ReleaseIntentPath
    SelectionAttemptPath = $SelectionAttemptPath
    SelectionAttemptDigest = $SelectionAttemptDigest
    SelectionPath = $SelectionPath
    SelectionDigest = $SelectionDigest
    Attempt = $Attempt
    CheckoutRoot = $checkout
    StateRoot = $stateRoot
    LogRoot = (Join-Path $candidateRoot 'logs')
    Ref = $record.ref
    RemoteUrl = $RemoteUrl
    Trigger = 'rc'
    Profile = 'rc'
    ExpectedSha = $record.sha
    CandidateId = $record.candidateId
    ReleaseRoot = $ReleaseRoot
    CoordinationRoot = $CoordinationRoot
    SeamsPath = $SeamsPath
    PassThru = $true
    WhatIf = $WhatIf
}
$run = & (Join-Path $PSScriptRoot 'nightly-run.ps1') @runArgs
$record.tested = ([int]$run.ExitCode -eq 0)
if (-not $record.tested) {
    $record.refusal = [string]$run.Refusal
    $record.exitCode = [int]$run.ExitCode
    Write-CutLine ('rc run did not complete green; publishing nothing (exit {0}).' -f $record.exitCode)
    if ($PassThru) { return [pscustomobject]$record }
    Write-Host (ConvertTo-Json -InputObject $record -Compress)
    exit ([int]$record.exitCode)
}

if ($SkipPublish -or $WhatIf) {
    $record.exitCode = 0
    if ($PassThru) { return [pscustomobject]$record }
    Write-Host (ConvertTo-Json -InputObject $record -Compress)
    exit 0
}

$pub = & (Join-Path $PSScriptRoot 'publish-release.ps1') -ReleaseRoot $ReleaseRoot `
    -CandidateId $record.candidateId -CheckoutRoot $checkout -Repository $Repository `
    -SeamsPath $SeamsPath -PassThru
$record.published = [bool]$pub.Published
$record.tag = [string]$pub.Tag
$record.refusal = [string]$pub.Refusal
$record.exitCode = [int]$pub.ExitCode
if ($PassThru) { return [pscustomobject]$record }
Write-Host (ConvertTo-Json -InputObject $record -Compress)
exit ([int]$record.exitCode)
