#requires -Version 7.0
# CARD-1545 S2a-1 red-first stub: prints the stub fingerprint.
param([string]$Lane, [string]$CandidateSha, [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Attestation, [string]$History, [string]$ReusePolicy)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/live-lane-fingerprint.ps1')
[Console]::Out.WriteLine((ConvertTo-TestSelectionCanonicalJson (Get-LiveLaneStatus -RepositoryRoot $RepositoryRoot -Lane $Lane -CandidateSha $CandidateSha)))
exit 0
