#requires -Version 7.0
# CARD-1836 S5b. Read-only historical extraction and unproven inventory. No launch,
# trust activation, network fetch, build, test, publication or historical writes.
param(
    [Parameter(Mandatory)][string]$SourceRunRoot,
    [Parameter(Mandatory)][string]$EvidenceRoot,
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$ExpectedSha,
    [Parameter(Mandatory)][string]$ExpectedRunId,
    [string]$CurrentManifestPath
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/lib/rc-admission.ps1"
if ($ExpectedSha -cnotmatch '^[a-f0-9]{40}$' -or -not $ExpectedRunId) { throw 'import-exact-source-and-run-required' }
# S5a performs confinement/immutability checks and refuses an existing output.
$bundle=Export-RcHistoricalEvidence $SourceRunRoot $EvidenceRoot $RepositoryRoot
$check=Test-RcHistoricalEvidence $EvidenceRoot $bundle.inventorySha256
if (-not $check.valid) { throw 'import-inventory-unproven' }
$coverage=Get-RcAdmissionAlgorithmCoverage
$dirty=0; $shards=@()
if ($CurrentManifestPath) {
    $request=Read-RcAdmissionJson $CurrentManifestPath (Get-RcAdmissionFileDigest $CurrentManifestPath)
    Assert-TestSelectionKeys $request @('chunk','manifest')
    $chunk=$request.chunk; $manifest=$request.manifest
    if ($chunk.suite -isnot [string] -or $chunk.suite -cnotmatch '^[a-z][a-z0-9-]*
    # A caller's discovery list is an obligation, never historical evidence. No
    # passed rows are written until the later activation/complete-graph slice.
    $identities=@(foreach ($method in $manifest.methods) {
        $slash=$method.identityKey.IndexOf('/')
        if ($slash -le 0) { throw 'import-invalid-method' }
        @{suite=$chunk.suite;className=$method.identityKey.Substring(0,$slash);method=$method.identityKey.Substring($slash+1);
            boundary=$chunk.boundary;inputDigest='unknown';outcome='unproven';durationSeconds=$null;carriedFrom=$null}
    })
    $doc=@{platform='windows';repository='antiphon';candidateId=$ExpectedRunId;candidateSha=$ExpectedSha;baselineSha=$null;baselineRef=$null;runId=$ExpectedRunId;attempt=1;
        contract=@{policyHash='unknown';catalogHash='unknown';selectorHash='unknown';runtimeHash='unknown'};
        forcedFull=@('release-graph-incomplete','historical-provenance-unproven');identities=$identities;
        chunks=@(@{chunkId=$chunk.chunkId;boundary=$chunk.boundary;suite=$chunk.suite;identityKeys=@($manifest.methods.identityKey);inputDigest='unknown';durationSeconds=$null})}
    $ledger=New-RcLedger $doc
    $relative='shards/'+$chunk.suite+'.json'
    Write-RcLedger (Join-Path $EvidenceRoot $relative) $ledger
    [void](Read-RcLedger (Join-Path $EvidenceRoot $relative))
    $shards=@(@{suite=$chunk.suite;path=$relative;sha256=(Get-RcAdmissionFileDigest (Join-Path $EvidenceRoot $relative))})
    $dirty=1
}
$report=@{format='antiphon-rc-import-report-v1';expectedSha=$ExpectedSha;expectedRunId=$ExpectedRunId;
    inventorySha256=$bundle.inventorySha256;admitted=0;dirty=$dirty;unproven=$dirty;fullAnchorKnown=$false;
    forceFull=$true;gateCredit=$false;shards=$shards;coverage=$coverage;
    reasons=@('historical-provenance-unproven','full-anchor-unproven','release-graph-incomplete');
    reasonCounts=@{'historical-provenance-unproven'=$dirty;'full-anchor-unproven'=$dirty;'release-graph-incomplete'=$dirty}}
[IO.File]::WriteAllText((Join-Path $EvidenceRoot 'admission-report.json'),(ConvertTo-Json -Depth 20 -InputObject $report))
Write-Host ('RC IMPORT admitted=0 dirty='+$dirty+' unproven='+$dirty+' Full=true')
exit 0
) { throw 'import-invalid-suite' }
    # A caller's discovery list is an obligation, never historical evidence. No
    # passed rows are written until the later activation/complete-graph slice.
    $identities=@(foreach ($method in $manifest.methods) {
        $slash=$method.identityKey.IndexOf('/')
        if ($slash -le 0) { throw 'import-invalid-method' }
        @{suite=$chunk.suite;className=$method.identityKey.Substring(0,$slash);method=$method.identityKey.Substring($slash+1);
            boundary=$chunk.boundary;inputDigest='unknown';outcome='unproven';durationSeconds=$null;carriedFrom=$null}
    })
    $doc=@{platform='windows';repository='antiphon';candidateId=$ExpectedRunId;candidateSha=$ExpectedSha;baselineSha=$null;baselineRef=$null;runId=$ExpectedRunId;attempt=1;
        contract=@{policyHash='unknown';catalogHash='unknown';selectorHash='unknown';runtimeHash='unknown'};
        forcedFull=@('release-graph-incomplete','historical-provenance-unproven');identities=$identities;
        chunks=@(@{chunkId=$chunk.chunkId;boundary=$chunk.boundary;suite=$chunk.suite;identityKeys=@($manifest.methods.identityKey);inputDigest='unknown';durationSeconds=$null})}
    $ledger=New-RcLedger $doc
    $relative='shards/'+$chunk.suite+'.json'
    Write-RcLedger (Join-Path $EvidenceRoot $relative) $ledger
    [void](Read-RcLedger (Join-Path $EvidenceRoot $relative))
    $shards=@(@{suite=$chunk.suite;path=$relative;sha256=(Get-RcAdmissionFileDigest (Join-Path $EvidenceRoot $relative))})
    $dirty=1
}
$report=@{format='antiphon-rc-import-report-v1';expectedSha=$ExpectedSha;expectedRunId=$ExpectedRunId;
    inventorySha256=$bundle.inventorySha256;admitted=0;dirty=$dirty;unproven=$dirty;fullAnchorKnown=$false;
    forceFull=$true;gateCredit=$false;shards=$shards;coverage=$coverage;
    reasons=@('historical-provenance-unproven','full-anchor-unproven','release-graph-incomplete');
    reasonCounts=@{'historical-provenance-unproven'=$dirty;'full-anchor-unproven'=$dirty;'release-graph-incomplete'=$dirty}}
[IO.File]::WriteAllText((Join-Path $EvidenceRoot 'admission-report.json'),(ConvertTo-Json -Depth 20 -InputObject $report))
Write-Host ('RC IMPORT admitted=0 dirty='+$dirty+' unproven='+$dirty+' Full=true')
exit 0
