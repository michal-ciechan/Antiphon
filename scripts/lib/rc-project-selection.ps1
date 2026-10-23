#requires -Version 7.0
# CARD-1545: advisory project batches; never execution or qualification authority.
. "$PSScriptRoot/test-selection.ps1"

function Get-RcProjectSelection {
    param([string]$RepositoryRoot, [string]$BaselineSha, [string]$CandidateSha)
    return [ordered]@{ format='antiphon-rc-project-selection-v1'; baselineSha=$BaselineSha; candidateSha=$CandidateSha
        mode='Full'; effectiveMode='Full'; executionEvidence=$false; gateCredit=$false
        changedProjects=@(); affectedProjects=@(); categories=@(); batches=@(); graphs=@(); fallbackReasons=@('not-implemented') }
}
