# CARD-1545 evidence wrapper. ASCII. Windows PowerShell 5.1 compatible.
# Owns no build slot. The checkpoint tool acquires slots for CP-20, CP-26 and CP-29.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Scan','Publish','CompletedRows','RunPortableRows','Roster')][string]$Case,
    [string]$Context,
    [string]$CheckpointToolPath
)
$ErrorActionPreference = 'Stop'
$lib = Join-Path $PSScriptRoot 'lib/live-lane-evidence.ps1'
if (-not (Test-Path -LiteralPath $lib)) { throw 'missing evidence library' }

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
    $roster = & pwsh -NoProfile -File $lib -Case Roster
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
        $started = & dotnet $CheckpointToolPath start $manifestPath --rows 'CP-20,CP-26,CP-29' --expected-source-sha $source --serial --total-timeout 70m --hard-wall-timeout 90m --keep-outputs 2>&1
        $started | ForEach-Object { Write-Output $_ }
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        $match = [regex]::Match(($started -join "`n"), 'RUN ([^ ]+) started')
        if (-not $match.Success) { throw 'Missing checkpoint run identity' }
        $run = $match.Groups[1].Value
        $raw = [IO.File]::ReadAllText($contextPath)
        $updated = $raw.Replace('"checkpointRun": ""', ('"checkpointRun": "' + $run + '"'))
        if ($updated -eq $raw) { throw 'context checkpointRun was not bound' }
        [IO.File]::WriteAllText($contextPath, $updated)
        do {
            & dotnet $CheckpointToolPath wait --run $run --max-wait 50s --expected-source-sha $source
            $code = $LASTEXITCODE
        } while ($code -eq 75)
        if ($code -ne 0) { exit $code }
        & pwsh -NoProfile -File $lib -Case OuterPublish -Context $contextPath
        exit $LASTEXITCODE
    } finally {
        if ($null -eq $previous) { Remove-Item Env:ANTIPHON_LIVE_EVIDENCE_CONTEXT -ErrorAction SilentlyContinue }
        else { $env:ANTIPHON_LIVE_EVIDENCE_CONTEXT = $previous }
        if ($null -eq $previousTemp) { Remove-Item Env:TEMP -ErrorAction SilentlyContinue } else { $env:TEMP = $previousTemp }
    }
}

& pwsh -NoProfile -File $lib -Case $Case -Context $Context
exit $LASTEXITCODE
