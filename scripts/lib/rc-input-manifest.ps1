#requires -Version 7.0
# CARD-1836 S3a: fail-closed adapter for the red-first input inventory contract.
. (Join-Path $PSScriptRoot 'test-selection.ps1')
function Get-RcInputManifest {
    param([string]$RepositoryRoot, [string]$BaselineSha, [string]$CandidateSha, $DerivedIndex)
    return @{format='antiphon-rc-input-inventory-v1'; baselineSha=$BaselineSha; candidateSha=$CandidateSha
        snapshots=@(); changes=@(); dirtySuites=@('server','runner','other'); unresolved=@('inventory-unimplemented')
        activation='shadow-only'; executionEvidence=$false; gateCredit=$false}
}
