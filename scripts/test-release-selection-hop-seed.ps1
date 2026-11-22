#requires -Version 5.1
# The hop failure record witness (Write-NightlyHopFailure, nightly-run-impl.ps1). W2-nightly S17: the seeded rc tests-hop
# witnesses (SeededRcHopBinds, SwitchDriftGuard) are in-process in ReleaseSelectionHopSeedTests: the tests hop is the
# nightly-tests command, and the selection hop and its switch helpers are deleted.
param([string]$Case = '', [string]$ResultsDirectory = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/c487-harness.ps1')
. (Join-Path $PSScriptRoot 'lib/nightly-run-impl.ps1')

$script:HsSeedSource = 'rc-2026-10-27-cde8def41638'
$script:HsRunId = '8dc9e695e8f445498bdb7fdd660028c2'

function Test-HopFailureRecord {
    $root = New-C487Root $ResultsDirectory
    $log = Join-Path $root 'empty-log'
    $statePath = Join-Path $root 'last-run.json'
    $state = [ordered]@{ phase = 'tests'; runId = $script:HsRunId; exitCode = 1 }
    $message = 'Exception calling "GetBytes" with "1" argument(s): "Value cannot be null. (Parameter ''chars'')"'
    $command = 'antiphon-scripts nightly-tests --repo-root C:\Antiphon\releases\checkout-250a07fb --seed-source-candidate-id ' + $script:HsSeedSource + ' --accept-cross-candidate-seed'
    [void](Write-NightlyHopFailure -State $state -StatePath $statePath -LogDir $log -ErrorMessage $message -Command $command)
    $failurePath = Join-Path $log 'hop-failure.txt'
    $text = ''
    if (Test-Path -LiteralPath $failurePath) { $text = [System.IO.File]::ReadAllText($failurePath) }
    Assert-C487 ($text.Contains($message)) 'HS HopFailureRecord log has the exception' $text
    Assert-C487 ($text.Contains($command)) 'HS HopFailureRecord log has the command' $text
    $json = $null
    if (Test-Path -LiteralPath $statePath) { $json = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json }
    Assert-C487 ($null -ne $json -and [string]$json.error -ceq $message) 'HS HopFailureRecord last-run error' ([string]$json.error)
    Assert-C487 ($null -ne $json -and [string]$json.command -ceq $command) 'HS HopFailureRecord last-run command' ([string]$json.command)
    Assert-C487 ($null -ne $json -and [int]$json.exitCode -eq 1) 'HS HopFailureRecord last-run exit' ([string]$json.exitCode)
    $impl = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'lib/nightly-run-impl.ps1'))
    Assert-C487 ($impl.Contains('Write-NightlyHopFailure -State $stateObj')) 'HS HopFailureRecord catch writes the failure'
}

if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -CommandType Function -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    Test-HopFailureRecord
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 0
