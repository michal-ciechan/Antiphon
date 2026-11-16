#requires -Version 7.0
# CARD-1836 S5c: reads retained revisions; starts no children and grants no credit.
param([Parameter(Mandatory)][string]$EvidenceRoot,[Parameter(Mandatory)][string]$ExpectedPlanDigest,[switch]$Json)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/lib/rc-incremental-plan.ps1"
$status=Read-RcPlanStatus $EvidenceRoot $ExpectedPlanDigest
if ($Json) { ConvertTo-Json -InputObject $status -Depth 60 }
else {
    Write-Host ('RC STATUS effective='+$status.effectiveMode+' planned='+$status.planned+' executed='+$status.executed+
        ' carried='+$status.carried+' sampled='+$status.sampled+' failed='+$status.failed+' unproven='+$status.unproven+' not-run='+$status.notRun)
    Write-Host ('coverageComplete=false gateCredit=false revision='+$status.sequence+' reasons='+($status.forcedReasons -join ','))
    Write-Host ('UNVERIFIED PROPOSAL carried='+$status.proposed.carried+' unique-origin-chunks='+$status.proposed.uniqueOriginChunks+
        ' historical-potential-wall-seconds='+$status.proposed.historicalPotentialWallSeconds)
    foreach ($origin in $status.proposed.origins) {
        Write-Host ('ORIGIN envelope='+$origin.envelopeDigest+' run='+$origin.runId+' source='+$origin.sourceSha+' suite='+$origin.suite+' chunk='+$origin.chunkId)
    }
}
exit 0
