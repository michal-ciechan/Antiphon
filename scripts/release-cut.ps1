#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1311: the RC lane entrypoint. Requires -Ref rc/YYYY-MM-DD.

    1. The compiled release candidate (Antiphon.Nightly.ReleaseCandidate) journals the existing
       remote tip. It does not cut origin/master and it does not allocate a stamp.
    2. The rc run (-Profile rc -Trigger rc -Ref <candidate> -ExpectedSha <sha>) runs against
       the candidate checkout and private state root.
    3. Publication (Antiphon.Nightly.ReleasePublish) runs only on RC complete-green.

    A red or incomplete RC publishes nothing and keeps its evidence. Nothing moves a tested
    ref or retries a failed run in place. Nothing writes the master attempt, green, monitor or
    qualification receipt, and nothing restarts AppHost or the runner.

    Unit 15 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: a thin launcher.
    The lane is C# (scripts/lib/Antiphon.Nightly/ReleaseCut.cs, the rc run NightlyRun in the
    same process) run as `antiphon-scripts release-cut` through scripts/lib/antiphon-cli.ps1.
    Windmill (u/lndcobra/antiphon_release_candidates) starts this file by path, so the
    parameters, their defaults and the exit code are unchanged, and the last stdout line is
    still the compact JSON record (candidateId, ref, sha, scheduleSlot, windmillJobId, tested,
    published, tag, refusal, exitCode, then selection once an attempt is sealed), written by
    the CLI. -SeamsPath is kept for binding only: seams never cross a process, so a non-empty
    value refuses with exit 3 (seams-path-unsupported). An explicitly blank -ReleaseRoot,
    -CoordinationRoot, -RemoteUrl or -Repository refuses with exit 2 (blank-release-root,
    blank-coordination-root, blank-remote-url, blank-repository): the launcher drops a blank
    value, and the CLI would read the production default. Exit 9: the CLI could not be built
    or started (one stderr line, then a record with refusal release-cut-cli-unavailable).
    -PassThru returns the record (read back from the CLI's --result-file) and never exits. No
    library is dot-sourced except the launcher itself.

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

# The record of a cut that never started (ReleaseCutResult's keys: empty identity, the exit code, the refusal), printed,
# and the object -PassThru returns for it.
function New-ReleaseCutLauncherRefusal {
    param([int]$Code, [string]$Refusal)
    $record = [ordered]@{
        candidateId = ''; ref = ''; sha = ''; scheduleSlot = $ScheduleSlot; windmillJobId = $JobId
        tested = $false; published = $false; tag = ''; refusal = $Refusal; exitCode = $Code
    }
    [Console]::Out.WriteLine((ConvertTo-Json -InputObject $record -Compress))
    return [pscustomobject]$record
}

if (-not [string]::IsNullOrWhiteSpace($SeamsPath)) {
    [Console]::Error.WriteLine('release-cut: seams-path-unsupported')
    $refused = New-ReleaseCutLauncherRefusal -Code 3 -Refusal 'seams-path-unsupported'
    if ($PassThru) { return $refused }
    exit 3
}

foreach ($blank in @(@('ReleaseRoot', 'blank-release-root'), @('CoordinationRoot', 'blank-coordination-root'), @('RemoteUrl', 'blank-remote-url'),
        @('Repository', 'blank-repository'))) {
    if ($PSBoundParameters.ContainsKey($blank[0]) -and [string]::IsNullOrWhiteSpace([string]$PSBoundParameters[$blank[0]])) {
        [Console]::Error.WriteLine('release-cut: ' + $blank[1])
        $refused = New-ReleaseCutLauncherRefusal -Code 2 -Refusal $blank[1]
        if ($PassThru) { return $refused }
        exit 2
    }
}

. (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')
$arguments = @(
    '--release-root', $ReleaseRoot,
    '--coordination-root', $CoordinationRoot,
    '--remote-url', $RemoteUrl,
    '--repository', $Repository,
    '--run-id', $RunId,
    '--schedule-slot', $ScheduleSlot,
    '--job-id', $JobId,
    '--ref', $Ref,
    '--selection-mode', $SelectionMode,
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
    '--attempt', [string]$Attempt
)
if ($SkipPublish) { $arguments += '--skip-publish' }
if ($WhatIf) { $arguments += '--what-if' }

# Always a result file: -PassThru reads it back, and its absence after exit 9 means the CLI never ran.
$resultFile = [IO.Path]::Combine([IO.Path]::GetTempPath(), 'antiphon-release-cut-' + [guid]::NewGuid().ToString('N') + '.json')
$code = Invoke-AntiphonCli -Command 'release-cut' -Arguments $arguments -ResultFile $resultFile

$result = $null
try {
    if ([IO.File]::Exists($resultFile)) {
        $text = [IO.File]::ReadAllText($resultFile)
        # Strings stay the strings the cut wrote (pwsh 7.5 ConvertFrom-Json would make dates DateTime; 5.1 never does).
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
    $result = New-ReleaseCutLauncherRefusal -Code 9 -Refusal 'release-cut-cli-unavailable'
}
if ($PassThru) {
    if ($null -eq $result) { $result = [pscustomobject]@{ exitCode = $code; refusal = '' } }
    return $result
}
exit $code
