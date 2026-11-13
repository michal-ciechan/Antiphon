#requires -Version 7.0
# CARD-1836 S5a. Extraction is integrity from now onward, never historical admission.
if (-not ('Antiphon.Rc.HistoricalEvidence' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'rc-historical-stream.cs')
}
function Export-RcHistoricalEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SourceRunRoot,
        [Parameter(Mandatory)][string]$EvidenceRoot,
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [string[]]$RelativePath,
        # Deterministic read-boundary witness only; no admission or validation bypass.
        [Action]$AfterOpen
    )
    $checkout = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    [Antiphon.Rc.HistoricalEvidence]::Export($SourceRunRoot, $EvidenceRoot, $RepositoryRoot, $checkout, $RelativePath, $AfterOpen)
}
function Test-RcHistoricalEvidence {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$EvidenceRoot, [string]$ExpectedInventorySha256)
    [Antiphon.Rc.HistoricalEvidence]::Verify($EvidenceRoot, $ExpectedInventorySha256)
}
