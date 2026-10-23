#requires -Version 5.1
# CARD-1514: write timing/summary.json, timing/tests.csv and timing/summary.md.
# ASCII-only. Does not edit TRX, LogRoot summary.json, receipts or the cost model.
param(
    [Parameter(Mandatory = $true)][string]$RunDirectory,
    [int]$Top = 50
)

$ErrorActionPreference = 'Stop'
$lib = Join-Path $PSScriptRoot 'lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'test-phase-evidence.ps1')
Invoke-TestPhaseRunSummary -RunDirectory $RunDirectory -Top $Top
