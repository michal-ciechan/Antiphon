#requires -Version 7.0
# Shared reader fixture used by named S5b cases. No live paths or runner children.
param([string]$Case,[string]$ResultsDirectory,[string]$RepositoryRoot,[string]$SourceSha)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/lib/rc-admission.ps1"
function Require($ok,[string]$label) { if (-not $ok) { throw "reader-witness:$label" } }
$source=Join-Path $ResultsDirectory 'retained'
[void][IO.Directory]::CreateDirectory($source)
function Write-Json([string]$name,$value) { [IO.File]::WriteAllText((Join-Path $source $name),(ConvertTo-Json -Depth 60 -InputObject $value)) }
[void](Invoke-TestSelectionGit $RepositoryRoot @('config','remote.origin.url','fixture://antiphon'))
$chunk=New-RcBoundaryChunk @(@{suite='antiphon';boundary='core';identityKey='Example.Tests/Check';caseKey='row:a'})
$cases=@(@{suite='antiphon';className='Example.Tests';method='Check';caseKey='row:a';stable=$true})
$tree=(Read-RcInputInventory $RepositoryRoot $SourceSha).treeId
$receipts=@(foreach ($kind in @('sdk-runtime-os-architecture','powershell-node-browser-native','configuration-environment','resolved-package-graph-and-content','generated-fixture-recipes-and-outputs','immutable-images')) {
    [IO.File]::WriteAllText((Join-Path $source ($kind+'.txt')),('actual fixture bytes '+$kind))
    Write-Json ($kind+'.proof') @{format='antiphon-rc-actual-input-v1';sourceSha=$SourceSha;runId='fixture-run';kind=$kind;digest=(Get-RcAdmissionFileDigest (Join-Path $source ($kind+'.txt')))}
    @{kind=$kind;digest=(Get-RcAdmissionFileDigest (Join-Path $source ($kind+'.txt')));provenanceDigest=(Get-RcAdmissionFileDigest (Join-Path $source ($kind+'.proof')));sourceSha=$SourceSha}
})
$manifest=New-RcChunkInputManifest (Read-RcInputInventory $RepositoryRoot $SourceSha) server $chunk $cases $receipts
[IO.File]::WriteAllText((Join-Path $source 'output.bin'),'fixture assembly and dependency closure')
$outputDigest=Get-RcAdmissionFileDigest (Join-Path $source 'output.bin')
$proof=@{format='antiphon-rc-execution-proof-v1';kind='executed';originPath=$null;sourceSha=$SourceSha;treeId=$tree;runId='fixture-run';
    chunk=$chunk;manifest=$manifest;outcome='Passed';complete=$true;exitCode=0;sourceClean=$true;custody='exclusive-owned-checkout';
    assemblyDigest=$outputDigest;buildOutputDigest=$outputDigest;discoveryOutputDigest=$outputDigest;launchOutputDigest=$outputDigest;
    outputPath='output.bin';anchorDigest='';stageReceipts=@(foreach ($stage in @('source','build','discovery','launch')) {
        Write-Json ($stage+'.json') @{format='antiphon-rc-stage-proof-v1';stage=$stage;sourceSha=$SourceSha;treeId=$tree;runId='fixture-run';manifestDigest=$manifest.digest;outputDigest=$outputDigest;sourceState='clean';custody='exclusive-owned-checkout'}
        @{stage=$stage;path=($stage+'.json')}
    });
    receipts=@(foreach ($r in $receipts) { @{kind=$r.kind;path=($r.kind+'.txt');provenancePath=($r.kind+'.proof')} });
    trxPath='chunk.trx';caseMap=@(@{testId='case-1';suite='antiphon';className='Example.Tests';method='Check';caseKey='row:a'})}
$trx='<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results><UnitTestResult testId="case-1" outcome="Passed" /></Results><TestDefinitions><UnitTest id="case-1"><TestMethod className="Example.Tests" name="Check" /></UnitTest></TestDefinitions><ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" error="0" timeout="0" aborted="0" notExecuted="0" /></ResultSummary></TestRun>'
[IO.File]::WriteAllText((Join-Path $source 'chunk.trx'),$trx)
$contract=@{format='antiphon-rc-input-contract-v1';inputMapVersion='1';sdkVersion='10.0.204';runtimeVersion='9.0.0';platform='windows';executionImplementation='compiled'}
foreach ($field in @('policyHash','catalogHash','selectorHash','skipResolverHash','discoveryHash','parserHash','coverageHash','launcherHash','custodyHash','buildHash','harnessHash','inputMapHash','sdkHash','runtimeHash')) { $contract[$field]='e'*64 }
Write-Json 'anchor.json' @{format='antiphon-rc-full-anchor-v1';sourceSha=$SourceSha;completedAtUtc='2026-11-12T12:00:00Z';mode='Full';carriedCount=0;outcome='Passed';requiredCount=1;executedCount=1;caseDigest=$manifest.caseDigest}
$anchorDigest=Get-RcAdmissionFileDigest (Join-Path $source 'anchor.json')
$proof.anchorDigest=$anchorDigest
Write-Json 'history.json' @{format='antiphon-rc-history-v1';runId='fixture-run';anchorDigest=$anchorDigest;complete=$true;failures=@()}
$envelope=@{format='antiphon-rc-admission-v1';sourceSha=$SourceSha;treeId=$tree;runId='fixture-run';repository='fixture://antiphon';platform='windows';rcLine='rc/2026-11-13';
    inventorySha256='';proofPath='proof.json';contract=$contract;anchorPath='anchor.json';historyPath='history.json';
    history=@{baselineState='admitted-ancestor';admissionChainState='verified';historyState='complete';globalOwnershipState='resolved';shadowState='clean'};
    fullAnchor=@{kind='executed-full';admissionState='verified';sourceSha=$SourceSha;closureDigest=$anchorDigest;completedAtUtc='2026-11-12T12:00:00Z'};
    releaseEvidence=@{status='complete';algorithmDigest=(Get-RcAdmissionAlgorithmDigest $RepositoryRoot $SourceSha @('scripts/lib/rc-input-owners.json'));coveredPaths=@('scripts/lib/rc-input-owners.json')}}
$script:revision=0
function Freeze-And-Read {
    $script:revision++
    Write-Json 'proof.json' $proof
    $bundle=Join-Path $ResultsDirectory ('bundle-'+$script:revision)
    $inventory=Export-RcHistoricalEvidence $source $bundle $RepositoryRoot
    $envelope.inventorySha256=$inventory.inventorySha256
    $path=Join-Path $ResultsDirectory ('admission-'+$script:revision+'.json')
    [IO.File]::WriteAllText($path,(ConvertTo-Json -Depth 60 -InputObject $envelope))
    $script:readArgs=@{Path=$path;ExpectedDigest=(Get-RcAdmissionFileDigest $path);EvidenceRoot=$bundle;RepositoryRoot=$RepositoryRoot;
        ExpectedSha=$SourceSha;ExpectedRunId='fixture-run';CurrentSha=$SourceSha;CurrentManifest=$manifest;Chunk=$chunk;CurrentContext=@{repository='fixture://antiphon';platform='windows';rcLine='rc/2026-11-13';contract=$contract};UtcNow=([DateTimeOffset]'2026-11-13T12:00:00Z')}
    Read-RcAdmission @readArgs
}
$good=Freeze-And-Read
Require ($good.eligible -and -not $good.admitted -and $good.forceFull) ('eligible-with-incomplete-coverage:'+($good.reasons -join ';'))
switch ($Case) {
    'FullProvenanceAdmitsAndReverifies' {
        $inv=Test-RcHistoricalEvidence $readArgs.EvidenceRoot $envelope.inventorySha256
        $entry=@($inv.files | Where-Object relativePath -CEQ 'chunk.trx')[0]
        $file=Join-Path $readArgs.EvidenceRoot $entry.bundlePath
        [IO.File]::AppendAllText($file,'tamper')
        $bad=Read-RcAdmission @readArgs
        Require (-not $bad.eligible -and $bad.dirty) 'tampered-bundle-refused'
        $good=Freeze-And-Read
        [IO.File]::AppendAllText($readArgs.Path,' ')
        Require (-not (Read-RcAdmission @readArgs).eligible) 'envelope-seal-refused'
        $readArgs.CurrentContext.contract=ConvertFrom-Json -AsHashtable -InputObject (ConvertTo-Json -Depth 20 $contract)
        $readArgs.CurrentContext.contract.runtimeHash='f'*64
        Require (-not (Read-RcAdmission @readArgs).eligible) 'current-contract-independent'
        $proof.buildOutputDigest='f'*64
        Require (-not (Freeze-And-Read).eligible) 'build-output-mismatch'
        $proof.buildOutputDigest=$outputDigest
        $proof.receipts[0].path=$proof.receipts[1].path
        Require (-not (Freeze-And-Read).eligible) 'receipt-bytes-mismatch'
    }
    'ChangedEnvelopeHistoryAndOriginChainRefuse' {
        $proof.kind='carried'; $proof.originPath='proof.json'
        $bad=Freeze-And-Read
        Require (-not $bad.eligible -and ($bad.reasons -join ';').Contains('origin-cycle')) 'origin-cycle'
        $proof.originPath='missing.json'; Require (-not (Freeze-And-Read).eligible) 'origin-missing'
        $proof.kind='executed'; $proof.originPath=$null
        $envelope.history.historyState='unknown'; Require (-not (Freeze-And-Read).eligible) 'history-unknown'
        $envelope.history.historyState='complete'
        $proof.sourceSha='f'*40; Require (-not (Freeze-And-Read).eligible) 'wrong-source'
        $proof.sourceSha=$SourceSha
        $proof.caseMap+=@{testId='case-2';suite='antiphon';className='Example.Tests';method='Check';caseKey='row:a'}
        Require (-not (Freeze-And-Read).eligible) 'case-alias'
        $proof.caseMap=@($proof.caseMap[0])
        [IO.File]::WriteAllText((Join-Path $source 'chunk.trx'),$trx.Replace('outcome="Passed"','outcome="Failed"'))
        Require (-not (Freeze-And-Read).eligible) 'failed-original'
    }
    'ImportIsInertAndWritesUnprovenShard' {
        $destination=Join-Path $ResultsDirectory 'import-output'
        $spec=Join-Path $ResultsDirectory 'request.json'
        [IO.File]::WriteAllText($spec,(ConvertTo-Json -Depth 60 -InputObject @{chunk=$chunk;manifest=$manifest}))
        & "$PSScriptRoot/rc-ledger-import.ps1" -SourceRunRoot $source -EvidenceRoot $destination -RepositoryRoot $RepositoryRoot -ExpectedSha $SourceSha -ExpectedRunId fixture-run -CurrentManifestPath $spec
        Require ($LASTEXITCODE -eq 0) 'import-exit'
        $report=Get-Content -Raw -LiteralPath (Join-Path $destination 'admission-report.json') | ConvertFrom-Json
        Require ($report.admitted -eq 0 -and $report.dirty -eq 1 -and $report.forceFull -and -not $report.gateCredit) 'import-full'
        $ledger=Read-RcLedger (Join-Path $destination 'shards/antiphon.json')
        Require ($ledger.identities.Count -eq 1 -and $ledger.identities[0].outcome -ceq 'unproven') 's1-unproven'
    }
    default { throw 'unknown reader case' }
}
Write-Host ('RA reader '+$Case+' passed')

exit 0
