[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CheckpointToolPath,
    [switch]$Controls
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$tool = [IO.Path]::GetFullPath($CheckpointToolPath)
$source = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot read source SHA' }
$runKey = 'f727-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$scratch = [IO.Directory]::CreateTempSubdirectory('c1545-portable-').FullName
$contextPath = Join-Path $scratch 'context.json'
$context = [ordered]@{ root=$scratch; source=$source; run=$runKey; producerBuild=$null; producerReceipt=$null; producerReceiptHash=$null }
$context | ConvertTo-Json | Set-Content -LiteralPath $contextPath
$manifestPath = Join-Path $repo ".antiphon/$runKey.yaml"
& dotnet $tool import --plan (Join-Path $repo 'docs/superpowers/plans/2026-10-31-card-1545-s1c-test-design.md') --out $manifestPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$manifest = Get-Content -Raw -LiteralPath $manifestPath
$manifest = $manifest.Replace('properties: {}', "properties:`n    UseAppHost: false").Replace('slotWaitMinutes: 45','slotWaitMinutes: 25').Replace('rowMinutes: 15','rowMinutes: 16')
$manifest = $manifest.Replace('bin-live-pty-isolation',"bin-$runKey-pty").Replace('bin-live-isolation',"bin-$runKey-c680")
$manifest = $manifest.Replace('timeoutMinutes: 18','timeoutMinutes: 2').Replace('timeoutMinutes: 30','timeoutMinutes: 4')
$manifest = $manifest.Replace('environment: {}', "environment:`n    ANTIPHON_LIVE_PORTABLE_CONTEXT: '$contextPath'")
# An exact-name union selects the commission-scoped Explicit method; a class wildcard omits it.
$names = 'DedicatedConfigOwnsTrustAndEvidence','CwdInstructionsCannotLeak','SettingsProjectionDetectsDrift','UnknownConfigDisablesReuse','StartupArgvMatchesProductionApplyOff'
$union = ($names | ForEach-Object { '(' + $_ + ')' }) -join '|'
$manifest = $manifest.Replace('/*/Antiphon.Tests.Scripts/LiveLaneIsolationTests/*', '/*/Antiphon.Tests.Scripts/LiveLaneIsolationTests/' + $union)
$producerRows = @('CP-18')
$consumerRows = @('CP-24')
if ($Controls) {
    $methods = @(
        @(7,'DedicatedConfigOwnsTrustAndEvidence'),@(8,'CwdInstructionsCannotLeak'),@(9,'SettingsProjectionDetectsDrift'),@(10,'UnknownConfigDisablesReuse'),@(11,'LaunchArgumentsKeepProductionSources'),@(12,'ModelSelectionComesFromContext'),
        @(13,'DedicatedConfigOwnsTrustAndEvidence'),@(14,'CwdInstructionsCannotLeak'),@(15,'SettingsProjectionDetectsDrift'),@(16,'UnknownConfigDisablesReuse'),@(17,'StartupArgvMatchesProductionApplyOff'))
    foreach ($entry in $methods) {
        $v = [int]$entry[0]; $method = $entry[1]
        $isPty = $v -le 12
        $buildId = if ($isPty) { "bin-$runKey-pty" } else { "bin-$runKey-c680" }
        $prefix = if ($isPty) { '/*/Antiphon.Agents.Pty.Tests/LiveClaudeIsolationWiringTests/' } else { '/*/Antiphon.Tests.Scripts/LiveLaneIsolationTests/' }
        $minutes = if ($isPty) { 2 } else { 4 }
        $manifest += @"

- id: PC-V$v
  repeat: 1
  after: [S1c-profile]
  build: $buildId
  group: portable-profile-controls
  filter: $prefix$method
  expect: [$method]
  minExecuted: 1
  estimatedMinutes: 1
  timeoutMinutes: $minutes
  serial: true
  environment:
    ANTIPHON_LIVE_PORTABLE_CONTEXT: '$contextPath'
    C1545_PROFILE_CONTROLS: V-$v
"@
        if ($isPty) { $producerRows += "PC-V$v" } else { $consumerRows += "PC-V$v" }
    }
}
Set-Content -LiteralPath $manifestPath -Value $manifest
function Invoke-Group([string[]]$Rows) {
    $start = & dotnet $tool start $manifestPath --rows ($Rows -join ',') --expected-source-sha $source --serial --total-timeout 26m --hard-wall-timeout 29m
    $start | Write-Output
    if ($LASTEXITCODE -ne 0) { throw "Checkpoint start refused ($LASTEXITCODE)" }
    $match = [regex]::Match(($start -join "`n"), 'RUN ([^ ]+) started')
    if (-not $match.Success) { throw 'Missing owned run identity' }
    $checkpointRun = $match.Groups[1].Value
    do {
        & dotnet $tool wait --run $checkpointRun --max-wait 50s --expected-source-sha $source
        $code = $LASTEXITCODE
    } while ($code -eq 75)
    if ($code -ne 0) { throw "Checkpoint group failed ($code), run=$checkpointRun, context=$contextPath" }
    $script:lastGroupRun = $checkpointRun
}
$oldTemp = $env:TEMP
try {
    $env:TEMP = '/tmp'
    Invoke-Group $producerRows
    $context.producerReceipt = Join-Path $repo ".antiphon/checkpoints/$lastGroupRun/report.json"
    $context.producerReceiptHash = (Get-FileHash -LiteralPath $context.producerReceipt -Algorithm SHA256).Hash
    $assembly = Join-Path $repo "tests/Antiphon.Agents.Pty.Tests/bin-$runKey-pty/Antiphon.Agents.Pty.Tests.dll"
    $context.producerBuild = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
    $context | ConvertTo-Json | Set-Content -LiteralPath $contextPath
    Invoke-Group $consumerRows
    Write-Output "PROFILE COMPLETE context=$contextPath producer=$($context.producerReceipt) consumer=$lastGroupRun"
} finally {
    $env:TEMP = $oldTemp
}
