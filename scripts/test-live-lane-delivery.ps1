[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CheckpointToolPath,
    [switch]$Controls
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$source = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot read source SHA' }
$tool = [IO.Path]::GetFullPath($CheckpointToolPath)
$runKey = '82f0-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$manifestPath = Join-Path $repo ".antiphon/$runKey.yaml"
& dotnet $tool import --plan (Join-Path $repo 'docs/superpowers/plans/2026-10-31-card-1545-s1c-test-design.md') --out $manifestPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$manifest = [IO.File]::ReadAllText($manifestPath).Replace('properties: {}', "properties:`n    UseAppHost: false").Replace('slotWaitMinutes: 45','slotWaitMinutes: 25').Replace('rowMinutes: 15','rowMinutes: 16')
$manifest = $manifest.Replace('bin-live-delivery',"bin-$runKey").Replace('timeoutMinutes: 18','timeoutMinutes: 2')
$rows = @('CP-19')
if ($Controls) {
    foreach ($entry in @(@(18,'ExplicitBridgeSetup'),@(19,'ControlRequiredForSettingsOff'),@(20,'ControlWindowBoundsOffObservation'),@(21,'StartupMethodsUseControlOracle'),@(22,'LoadedSettingsSurviveOffOverlay'))) {
        $v = $entry[0]; $method = $entry[1]
        $manifest += @"

- id: PC-V$v
  repeat: 1
  after: [S1c-delivery-oracles]
  build: bin-$runKey
  group: portable-delivery-controls
  filter: /*/Antiphon.Agents.Pty.Tests/LiveClaudeDeliveryWiringTests/$method
  expect: [$method]
  minExecuted: 1
  estimatedMinutes: 1
  timeoutMinutes: 2
  serial: true
  environment:
    C1545_DELIVERY_CONTROLS: V-$v
"@
        $rows += "PC-V$v"
    }
}
[IO.File]::WriteAllText($manifestPath,$manifest)
$previousTemp = $env:TEMP
try {
    $env:TEMP = '/tmp'
    $started = & dotnet $tool start $manifestPath --rows ($rows -join ',') --expected-source-sha $source --serial --total-timeout 26m --hard-wall-timeout 29m --keep-outputs
    $started | Write-Output
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $match = [regex]::Match(($started -join "`n"),'RUN ([^ ]+) started')
    if (-not $match.Success) { throw 'Missing checkpoint run identity' }
    do {
        & dotnet $tool wait --run $match.Groups[1].Value --max-wait 50s --expected-source-sha $source
        $code = $LASTEXITCODE
    } while ($code -eq 75)
    exit $code
} finally {
    $env:TEMP = $previousTemp
}
