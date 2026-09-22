#requires -Version 5.1
<#
.SYNOPSIS
    CARD-0599 D-3: the RC lane entrypoint. Cut, test, publish - in that order.

    1. scripts/release-candidate.ps1 pins origin/master once and pushes the
       create-only candidate branch.
    2. scripts/nightly-run.ps1 -Profile rc -Trigger rc -Ref <candidate>
       -ExpectedSha <sha> runs the full automated profile against the
       candidate's own checkout and private state root.
    3. scripts/publish-release.ps1 publishes a CalVer tag and GitHub Release,
       but only on RC complete-green.

    A red or incomplete RC publishes nothing and keeps its evidence. Repairs go
    through ordinary Code/Review/land onto master and then a NEW candidate; this
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
    [string]$ScheduleSlot = '',
    [string]$JobId = '',
    [string]$Ref = '',
    [string]$SeamsPath = '',
    [switch]$SkipPublish,
    [switch]$WhatIf,
    [switch]$PassThru
)

$ErrorActionPreference = 'Continue'

$lib = Join-Path $PSScriptRoot 'lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'release-gate.ps1')

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

$runArgs = @{
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
