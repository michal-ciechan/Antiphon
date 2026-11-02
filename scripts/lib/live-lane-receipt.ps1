#requires -Version 7.0
# CARD-1545 S2b red-first FAIL-OPEN STUB (never landed): the final signatures with no checks.
# Every receipt is valid GREEN and every state is SATISFIED; the green commit replaces this file.
. "$PSScriptRoot/live-lane-fingerprint.ps1"

function Get-LiveLaneRosterDigest { param([string[]]$Roster) return Get-TestSelectionDigest @(Get-TestSelectionSorted @($Roster)) }

function Test-LiveLaneReceipt {
    param([string]$Receipt, [string]$Manifest, [string[]]$Roster, [string]$RosterDigest)
    $digest=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($Receipt))).ToLowerInvariant()
    return [ordered]@{valid=$true;reasons=@();outcome='GREEN';code=0;digest=$digest;receipt=$null}
}

function Test-LiveLaneUseRecord { param([string]$Path) return [ordered]@{valid=$true;reasons=@();digest=$null;record=$null} }

function Get-LiveLaneState {
    param($Candidate, [string[]]$History = @(), [string[]]$Roster, [string]$RepositoryRoot, [string]$ReusePolicy = 'AlwaysRerun',
        $NowUtc, $Probes = @(), $Revocations = @())
    return [ordered]@{format='antiphon-live-lane-state-v1';laneId=$Candidate['laneId'];candidateSha=$Candidate['sha'];state='SATISFIED';reasons=@()
        localCompatibility='SATISFIED';reuseEligible=$true;policy=$ReusePolicy;selected=$null;lastGreen=$null;freshness=$null
        effectiveMode='Full';activation='shadow-only';gateCredit=$false;executionEvidence=$false}
}

function New-LiveLaneUseRecord { param($State, [string]$Path, $NowUtc) return [ordered]@{} }

function Get-LiveLaneHistoryStatus {
    param([string]$RepositoryRoot, [string]$Lane, [string]$CandidateSha, [string]$Attestation, [string]$History, [string]$Context,
        [string]$ReusePolicy = 'AlwaysRerun', [string]$UseRecord)
    $document=Get-LiveLaneStatus -RepositoryRoot $RepositoryRoot -Lane $Lane -CandidateSha $CandidateSha -Attestation $Attestation
    $document.state='SATISFIED'
    return $document
}
