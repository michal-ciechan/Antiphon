#requires -Version 7.0
# CARD-1545 S2a-1/S2a-2: prints one shadow-only live-lane state document. Exit 0 with a state,
# exit 2 on invalid input. -Attestation adds the environment component (S2a-2). Receipts
# (-History) and the reuse policy (-ReusePolicy) arrive in S2b; until then they refuse rather
# than being ignored.
param([string]$Lane, [string]$CandidateSha, [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Attestation, [string]$History, [string]$ReusePolicy)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/live-lane-fingerprint.ps1')
try {
    foreach ($option in @(@('History', $History), @('ReusePolicy', $ReusePolicy))) {
        if ($option[1]) { throw "option-not-available:$($option[0])" }
    }
    if (-not $Lane) { throw 'lane-required' }
    $document = Get-LiveLaneStatus -RepositoryRoot $RepositoryRoot -Lane $Lane -CandidateSha $CandidateSha -Attestation $Attestation
    [Console]::Out.WriteLine((ConvertTo-TestSelectionCanonicalJson $document))
    exit 0
} catch {
    $reason = $_.Exception.Message
    if ($reason -cnotmatch '^[a-z][a-z0-9-]*(:[^\r\n]{0,200})?$') { $reason = 'status-failed' }
    [Console]::Out.WriteLine((ConvertTo-TestSelectionCanonicalJson ([ordered]@{format = 'antiphon-live-lane-status-v1'; laneId = $Lane
                    state = 'INVALID'; error = $reason; effectiveMode = 'Full'; activation = 'shadow-only'; gateCredit = $false; executionEvidence = $false })))
    exit 2
}
