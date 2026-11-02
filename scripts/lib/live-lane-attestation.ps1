#requires -Version 7.0
# CARD-1545 S2a-2: live-lane environment attestation consumer. RED-FIRST STUB: fail-open on
# purpose so every invalidation and refusal witness is red; the green commit replaces it.
. "$PSScriptRoot/test-selection.ps1"

function Get-LiveLaneEnvironmentAttestation {
    param([string]$Path, [string]$Lane)
    $zero='0'*64
    return [ordered]@{status='complete';reasons=@();digest=$zero
        components=[ordered]@{executable=$zero;runtime=$zero;settings=$zero;account=$zero}}
}

function Test-LiveLaneExternalFreshness {
    param([string]$Policy, $ExecutedAtUtc, $NowUtc, $Candidate, $Executed, $Probes, $Revocations, $MaxAgeHours)
    return [ordered]@{reuseEligible=$true;reasons=@('d6-bounded-exception');externalProof=$false}
}
