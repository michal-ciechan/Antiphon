# CARD-1015: InventoryOnly and exact marked deletion validation. Never writes Git or source.
# CARD-1024: -SupplementalCleanup CARD-nnnn opts into a later inventory and that card's trailer.
param([string]$Repository = '.', [string]$InventoryRef, [string]$InventoryPathSha256,
    [string]$InventoryCount, [string]$InventoryBytes, [string]$HeadRef = 'HEAD', [switch]$InventoryOnly,
    [string]$SupplementalCleanup)
$ErrorActionPreference = 'Stop'
try {
    . (Join-Path $PSScriptRoot 'lib/evidence-policy.ps1')
    exit (Invoke-EvidenceDeletion @PSBoundParameters)
} catch { Write-Host ('EVIDENCE error reason=' + $_.Exception.Message); exit 2 }
