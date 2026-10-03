# Read-only CARD-1015 proposed-history guard. Missing/invalid inputs exit 2 without prompts.
param([string]$Repository = '.', [string]$BaseRef, [string]$HeadRef = 'HEAD',
    [string]$EventPath, [string]$EventName, [string]$ManualBase, [string]$ManualHead)
$ErrorActionPreference = 'Stop'
try {
    . (Join-Path $PSScriptRoot 'lib/evidence-policy.ps1')
    exit (Invoke-EvidenceHistory @PSBoundParameters)
} catch { Write-Host ('EVIDENCE error reason=' + $_.Exception.Message); exit 2 }
