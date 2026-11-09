# CARD-1545 evidence wrapper. ASCII. Windows PowerShell 5.1 parseable.
# Owns no build slot. The checkpoint tool acquires slots for CP-20, CP-26 and CP-29.
# The scan itself is the compiled command (antiphon-scripts live-lane-evidence, LiveLaneEvidence.cs),
# built once per source version by nightly-core.ps1. Without it (Windows PowerShell 5.1, no SDK, a failed
# build or ANTIPHON_SCRIPTS_CLI=off) there is no scanner, and every case fails closed.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Scan','Publish','CompletedRows','RunPortableRows','Roster')][string]$Case,
    [string]$Context,
    [string]$CheckpointToolPath
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/nightly-core.ps1')
$cli = Get-AntiphonScriptsCli
if ($null -eq $cli) {
    Write-Output 'INVALID/evidence-sanitization reason=scanner-unavailable'
    exit 2
}

function Invoke-EvidenceCheckpoint {
    param(
        [Parameter(Mandatory)][string]$Rows,
        [Parameter(Mandatory)][int]$TotalMinutes,
        [Parameter(Mandatory)][int]$HardMinutes
    )
    $root = Join-Path $repo '.antiphon/checkpoints'
    if (-not (Test-Path -LiteralPath $root)) { New-Item -ItemType Directory -Path $root | Out-Null }
    $before = @{}
    foreach ($dir in [IO.Directory]::GetDirectories($root)) { $before[$dir] = $true }
    $proc = Start-Process -FilePath 'dotnet' -ArgumentList @(
        $CheckpointToolPath, 'start', $manifestPath, '--rows', $Rows,
        '--expected-source-sha', $source, '--serial',
        '--total-timeout', ($TotalMinutes.ToString() + 'm'),
        '--hard-wall-timeout', ($HardMinutes.ToString() + 'm'),
        '--keep-outputs'
    ) -WorkingDirectory $repo -PassThru -NoNewWindow
    $deadline = [DateTime]::UtcNow.AddMinutes(3)
    while (-not $proc.HasExited) {
        if ([DateTime]::UtcNow -gt $deadline) { throw 'checkpoint start did not return' }
        Start-Sleep -Milliseconds 200
    }
    [Console]::Out.WriteLine('start-exit=' + $proc.ExitCode + ' rows=' + $Rows)
    if ($proc.ExitCode -ne 0) { exit $proc.ExitCode }
    $run = ''
    foreach ($attempt in 1..25) {
        $created = @()
        foreach ($dir in [IO.Directory]::GetDirectories($root)) {
            if (-not $before.ContainsKey($dir)) { $created += $dir }
        }
        if ($created.Count -gt 1) { throw ('checkpoint run identity count=' + $created.Count) }
        if ($created.Count -eq 1) {
            $run = [IO.Path]::GetFileName($created[0].TrimEnd('\', '/'))
            break
        }
        Start-Sleep -Milliseconds 200
    }
    if ([string]::IsNullOrWhiteSpace($run)) { throw 'Missing checkpoint run identity' }
    $statePath = Join-Path (Join-Path $root $run) 'state.json'
    $stateText = ''
    foreach ($attempt in 1..25) {
        if (Test-Path -LiteralPath $statePath) {
            $stateText = [IO.File]::ReadAllText($statePath)
            if ($stateText.Contains('"runId"')) { break }
        }
        Start-Sleep -Milliseconds 200
    }
    $state = $stateText | ConvertFrom-Json
    if ([string]$state.runId -ne $run) { throw 'checkpoint run id mismatch' }
    return $run
}

function Wait-EvidenceCheckpoint {
    param([Parameter(Mandatory)][string]$RunId)
    do {
        $log = Join-Path $owned ('wait-' + $RunId + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6) + '.log')
        & dotnet $CheckpointToolPath wait --run $RunId --max-wait 50s --expected-source-sha $source > $log 2>&1
        $code = $LASTEXITCODE
        if (Test-Path -LiteralPath $log) { [Console]::Out.WriteLine([IO.File]::ReadAllText($log)) }
        [Console]::Out.WriteLine('wait-exit=' + $code + ' run=' + $RunId)
    } while ($code -eq 75)
    return $code
}

if ($Case -eq 'RunPortableRows') {
    if ([string]::IsNullOrWhiteSpace($CheckpointToolPath) -or -not (Test-Path -LiteralPath $CheckpointToolPath -PathType Leaf)) {
        Write-Output 'INVALID/evidence-sanitization reason=context-absent'
        exit 2
    }
    $repo = Split-Path $PSScriptRoot -Parent
    Set-Location $repo
    $source = (& git -C $repo rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $source.Length -ne 40) { throw 'Cannot read source SHA' }
    $runKey = 's1c-ev-g' + [guid]::NewGuid().ToString('N').Substring(0, 6)
    $owned = Join-Path $repo (Join-Path '.antiphon' $runKey)
    New-Item -ItemType Directory -Path $owned | Out-Null
    $export = Join-Path $owned 'cp20-export.txt'
    $receipt = Join-Path $owned 'receipt.txt'
    $summary = Join-Path $owned 'summary.txt'
    $public = Join-Path $owned 'public-summary.txt'
    [IO.File]::WriteAllText($export, '')
    $roster = & $cli live-lane-evidence Roster
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $cases = @()
    foreach ($line in @($roster)) {
        $parts = ([string]$line).Split(' ')
        if ($parts.Count -ne 3) { throw 'roster line is not case test cp' }
        $cases += [ordered]@{ case = $parts[0]; test = $parts[1]; cp = $parts[2] }
    }
    $ptyOut = 'tests/Antiphon.Agents.Pty.Tests/bin-' + $runKey + '-pty'
    $c680Out = 'tests/Antiphon.Tests/bin-' + $runKey + '-c680'
    $doc = [ordered]@{
        source = $source
        repo = $repo
        runToken = 'portable'
        checkpointRun = ''
        resultsRoot = (Join-Path $repo '.antiphon/checkpoints')
        exportPath = $export
        receipt = $receipt
        summary = $summary
        publicSummary = $public
        requirePublic = $true
        cases = $cases
        outputs = @(
            [ordered]@{ id = 'CP-20'; path = $ptyOut },
            [ordered]@{ id = 'CP-26'; path = $c680Out }
        )
    }
    $contextPath = Join-Path $owned 'context.json'
    $doc | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $contextPath -Encoding utf8
    $manifestPath = Join-Path $owned 'manifest.yaml'
    & dotnet $CheckpointToolPath import --plan (Join-Path $repo 'docs/superpowers/plans/2026-10-31-card-1545-s1c-sanitization-checkpoints.md') --out $manifestPath
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $manifest = [IO.File]::ReadAllText($manifestPath)
    $manifest = $manifest.Replace('properties: {}', "properties:`n    UseAppHost: false").Replace('slotWaitMinutes: 45', 'slotWaitMinutes: 25')
    $manifest = $manifest.Replace('bin-live-pty-evidence', ('bin-' + $runKey + '-pty')).Replace('bin-live-evidence', ('bin-' + $runKey + '-c680'))
    [IO.File]::WriteAllText($manifestPath, $manifest)
    $previous = $env:ANTIPHON_LIVE_EVIDENCE_CONTEXT
    $previousTemp = $env:TEMP
    try {
        $env:TEMP = '/tmp'
        $env:TMPDIR = '/tmp'
        $env:ANTIPHON_LIVE_EVIDENCE_CONTEXT = $contextPath
        # Start returns before the executor finishes. Do not capture its stdout:
        # the detached executor inherits that pipe and the call operator waits
        # for the whole run. Command rows are admitted before test builds, and
        # CP-29's 15 minute deadline cannot cover those builds, so the scan
        # starts only after CP-20 and CP-26 are green.
        $testsRun = Invoke-EvidenceCheckpoint -Rows 'CP-20,CP-26' -TotalMinutes 70 -HardMinutes 90
        [Console]::Out.WriteLine('tests-run=' + $testsRun)
        $raw = [IO.File]::ReadAllText($contextPath)
        $updated = $raw.Replace('"checkpointRun": ""', ('"checkpointRun": "' + $testsRun + '"'))
        if ($updated -eq $raw) { throw 'context checkpointRun was not bound' }
        [IO.File]::WriteAllText($contextPath, $updated)
        $testsCode = Wait-EvidenceCheckpoint -RunId $testsRun
        if ($testsCode -ne 0) { exit $testsCode }
        $scanRun = Invoke-EvidenceCheckpoint -Rows 'CP-29' -TotalMinutes 20 -HardMinutes 25
        [Console]::Out.WriteLine('scan-run=' + $scanRun)
        $scanCode = Wait-EvidenceCheckpoint -RunId $scanRun
        if ($scanCode -ne 0) { exit $scanCode }
        & $cli live-lane-evidence OuterPublish $contextPath
        exit $LASTEXITCODE
    } finally {
        if ($null -eq $previous) { Remove-Item Env:ANTIPHON_LIVE_EVIDENCE_CONTEXT -ErrorAction SilentlyContinue }
        else { $env:ANTIPHON_LIVE_EVIDENCE_CONTEXT = $previous }
        if ($null -eq $previousTemp) { Remove-Item Env:TEMP -ErrorAction SilentlyContinue } else { $env:TEMP = $previousTemp }
    }
}

if ([string]::IsNullOrWhiteSpace($Context)) { & $cli live-lane-evidence $Case }
else { & $cli live-lane-evidence $Case $Context }
exit $LASTEXITCODE
