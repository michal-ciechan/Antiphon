#requires -Version 7.0
# CARD-1545 S2a-1/S2a-2/S2b: prints one shadow-only live-lane state document. Exit 0 with a
# state, exit 2 on invalid input. -Attestation adds the environment component (S2a-2).
# -History (a directory of receipts, use and revocation records) with -Context evaluates the
# lane state (S2b); -ReusePolicy defaults to AlwaysRerun (D-6). No state carries gate credit.
param([string]$Lane, [string]$CandidateSha, [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Attestation, [string]$History, [string]$ReusePolicy = 'AlwaysRerun', [string]$Context, [string]$UseRecord)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/live-lane-receipt.ps1')
try {
    if (-not $Lane) { throw 'lane-required' }
    if ($History) {
        $document = Get-LiveLaneHistoryStatus -RepositoryRoot $RepositoryRoot -Lane $Lane -CandidateSha $CandidateSha -Attestation $Attestation `
            -History $History -Context $Context -ReusePolicy $ReusePolicy -UseRecord $UseRecord
    } else {
        foreach ($option in @(@('Context', $Context), @('UseRecord', $UseRecord))) { if ($option[1]) { throw "history-required:$($option[0])" } }
        if (-not (@('AlwaysRerun', 'Bounded24h') -ccontains $ReusePolicy)) { throw 'policy-invalid' }
        $document = Get-LiveLaneStatus -RepositoryRoot $RepositoryRoot -Lane $Lane -CandidateSha $CandidateSha -Attestation $Attestation
    }
    [Console]::Out.WriteLine((ConvertTo-TestSelectionCanonicalJson $document))
    exit 0
} catch {
    $reason = $_.Exception.Message
    if ($reason -cnotmatch '^[a-z][a-z0-9-]*(:[^\r\n]{0,200})?$') { $reason = 'status-failed' }
    [Console]::Out.WriteLine((ConvertTo-TestSelectionCanonicalJson ([ordered]@{format = 'antiphon-live-lane-status-v1'; laneId = $Lane
                    state = 'INVALID'; error = $reason; effectiveMode = 'Full'; activation = 'shadow-only'; gateCredit = $false; executionEvidence = $false })))
    exit 2
}
