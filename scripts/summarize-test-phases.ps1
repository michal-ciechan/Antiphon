<#
.SYNOPSIS
Write timing/summary.json, timing/tests.csv and timing/summary.md.
.DESCRIPTION
ASCII-only. Does not edit TRX, LogRoot summary.json, receipts or the cost model.
Default output is a read-only summary. It prints one publication progress line when a journal or flat executed manifest is present. That line is the live progress signal. A flat file timestamp is not.
.PARAMETER RunDirectory
Qualification log directory whose timing root is RunDirectory/timing unless a caller already set the process timing root.
.PARAMETER Top
Maximum rows written to the timing summary tables.
.PARAMETER TimingRoot
Explicit timing directory when qualification stored it separately from RunDirectory.
.PARAMETER RecoverExecutedManifest
Acquire the writer lock, rebuild the flat projection and state from the committed journal, then summarize. Refuses a live writer. Never writes a terminal seal. An intact sealed generation is restored only when the rebuilt digests match that seal. Corrupt committed history becomes a successor and the old seal stays in place.
#>
#requires -Version 5.1
param(
    [Parameter(Mandatory = $true)][string]$RunDirectory,
    [int]$Top = 50,
    [switch]$RecoverExecutedManifest,
    [string]$TimingRoot = ''
)

$ErrorActionPreference = 'Stop'
$lib = Join-Path $PSScriptRoot 'lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'test-phase-evidence.ps1')
if (-not [string]::IsNullOrWhiteSpace($TimingRoot)) {
    $script:QualificationTimingRoot = [System.IO.Path]::GetFullPath($TimingRoot)
}
if ($RecoverExecutedManifest) {
    Invoke-TestPhaseExecutedRecovery -LogRoot $RunDirectory | Out-Null
}
$timing = Get-TestPhaseTimingDirectory -RunDirectory $RunDirectory
$flat = Join-Path $timing 'groups-executed.json'
$current = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $timing) 'current.json'
$hasFlat = (Get-TestPhasePathIdentity -Path $flat).Kind -eq 'regular'
$hasJournal = (Get-TestPhasePathIdentity -Path $current).Kind -eq 'regular'
if ($hasFlat -or $hasJournal) {
    $progress = Read-TestPhaseExecutedPrefix -LogRoot $RunDirectory
    $quality = @(Get-TestPhasePublicationQuality -RunDirectory $RunDirectory)
    $qualityText = (@($quality) -join ',')
    Write-Output ('publication generation=' + [string]$progress.Generation + ' sequence=' + [string]$progress.LastSequence + ' recordKind=' + [string]$progress.RecordKind + ' quality=' + $qualityText)
}
Invoke-TestPhaseRunSummary -RunDirectory $RunDirectory -Top $Top
