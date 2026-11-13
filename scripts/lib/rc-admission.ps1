#requires -Version 7.0
# CARD-1836 S5b. Inert: no launch caller. Full remains required until the release
# graph and its COMPLETE executed algorithm closure have separately been admitted.
. (Join-Path $PSScriptRoot 'rc-chunk-digest.ps1')
. (Join-Path $PSScriptRoot 'rc-historical-evidence.ps1')
. (Join-Path $PSScriptRoot 'rc-forced-full.ps1')
. (Join-Path $PSScriptRoot 'rc-ledger.ps1')

function Get-RcAdmissionAlgorithmCoverage {
    param([string[]]$CoveredPaths=@('scripts/impacted-classes.ps1','scripts/lib/release-impact.ps1'))
    # Explicit prerequisite inventory, including relation construction, evaluation,
    # canonicalization and the compiled driver. Not a claim of transitive closure.
    $required=@('scripts/impacted-classes.ps1','scripts/lib/release-impact.ps1',
        'scripts/lib/test-selection.ps1','scripts/lib/rc-admission.ps1',
        'scripts/lib/rc-input-manifest.ps1','scripts/lib/rc-chunk-digest.ps1',
        'scripts/lib/rc-boundary.ps1','scripts/lib/rc-historical-stream.cs',
        'tests/Antiphon.Tests/Scripts/ReleaseImpactProjectGraph.cs',
        'tests/Antiphon.Tests/Scripts/ReleaseImpactGraphBuilder.cs',
        'tests/Antiphon.Tests/Scripts/DerivedImpactIndexBuilder.cs',
        'tools/Antiphon.LandGraphAudit/ReleaseInputEvaluation.cs',
        'tools/Antiphon.LandGraphAudit/ReleaseInputCommand.cs',
        'tools/Antiphon.LandGraphAudit/EvaluationExpressionPolicy.cs',
        'tools/Antiphon.LandGraphAudit/BuildInputPolicy.cs',
        'tools/Antiphon.LandGraphAudit/Program.cs',
        'tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj',
        'scripts/regenerate-derived-impact-index/Program.cs',
        'scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj')
    $covered=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($path in $CoveredPaths) { if (-not $covered.Add($path)) { throw 'duplicate-algorithm-path' } }
    $missing=@(Get-TestSelectionSorted @($required | Where-Object { -not $covered.Contains($_) }))
    # The release graph still has unresolved evaluator/canonicalizer/runtime edges.
    # Covering this minimum list cannot certify them; there is no caller override.
    return @{complete=$false;requiredPaths=@(Get-TestSelectionSorted $required);missingPaths=$missing;
        reasons=@('release-graph-incomplete')+@($missing | ForEach-Object { 'algorithm-uncovered:'+$_ })}
}

function Get-RcAdmissionAlgorithmDigest {
    param([string]$RepositoryRoot,[string]$Sha,[string[]]$CoveredPaths)
    if (-not $CoveredPaths.Count) { throw 'algorithm-coverage-empty' }
    $rows=[Collections.Generic.List[object]]::new(); $rows.Add(@('antiphon-rc-algorithm','1'))
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $objects=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($entry in (Invoke-TestSelectionGit $RepositoryRoot @('ls-tree','-r','-z','--full-tree',$Sha)).Stdout.Split([char]0)) {
        if (-not $entry) { continue }
        if ($entry -cnotmatch '^([0-7]{6}) (blob|commit) ([0-9a-f]{40})\t([^\x00-\x1f]+)$') { throw 'algorithm-tree-invalid' }
        $objects.Add($Matches[4],@($Matches[1],$Matches[2],$Matches[3]))
    }
    foreach ($path in (Get-TestSelectionSorted $CoveredPaths)) {
        Assert-RcDigestText $path 'algorithm-path'
        if (-not $seen.Add($path) -or $path -match '(^/|\\|(^|/)\.\.?(/|$)|:)') { throw 'algorithm-path-invalid' }
        if (-not $objects.ContainsKey($path) -or $objects[$path][0] -cnotin @('100644','100755') -or $objects[$path][1] -cne 'blob') { throw 'algorithm-object-unproven' }
        $rows.Add(@($path,[string]$objects[$path][0],[string]$objects[$path][2]))
    }
    Get-RcManifestDigest $rows.ToArray()
}

function Restore-RcAdmissionManifest {
    param($Manifest,$Chunk,$Snapshot)
    if ($Manifest.format -cne 'antiphon-rc-chunk-input-v1') { throw 'manifest-invalid' }
    if ($Snapshot) { [void](Restore-RcAdmissionManifest $Manifest $Chunk) }
    if (-not $Snapshot) {
        # Rehash stored records independently; Read-RcAdmission additionally
        # reconstructs the inventory from Git objects, never disk or stored hashes.
        $projects=@($Manifest.inputs.project | Select-Object -Unique)
        if ($projects.Count -ne 1) { throw 'manifest-invalid' }
        $Snapshot=@{format='antiphon-rc-input-snapshot-v1';sha=$Manifest.sourceSha;treeId=$Manifest.treeId;
            unresolved=@($Manifest.unknown);suites=@(@{id=$Manifest.inputSuite;project=$projects[0];inputs=$Manifest.inputs;edges=$Manifest.edges})}
    }
    $cases=@(foreach ($c in $Manifest.cases) {
        @{suite=$c.suite;className=$c.className;method=$c.method;caseKey=$c.caseKey;stable=$c.stable}
    })
    $rebuilt=New-RcChunkInputManifest $Snapshot $Manifest.inputSuite $Chunk $cases $Manifest.actualReceipts
    foreach ($field in @('sourceSha','treeId','inputSuite','suite','boundary','chunkId','membershipDigest','digest','inputDigest','caseDigest','receiptDigest')) {
        if ($Manifest[$field] -isnot [string] -or $rebuilt[$field] -cne $Manifest[$field]) { throw 'manifest-rehash-mismatch' }
    }
    return $rebuilt
}

function Get-RcCarryAdmissionDecision {
    param($Before,$After,$Facts,$Chunk,[DateTimeOffset]$UtcNow=[DateTimeOffset]::UtcNow)
    $reasons=[Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    if (-not $Chunk) {
        try {
            $rows=@(foreach ($c in $After.cases) { @{suite=$c.suite;boundary=$After.boundary;identityKey=($c.className+'/'+$c.method);caseKey=$c.caseKey} })
            $Chunk=New-RcBoundaryChunk $rows
        } catch { [void]$reasons.Add('manifest-invalid') }
    }
    if ($Chunk -and ($Chunk.boundary -ceq 'unassigned' -or $Chunk.collision -eq $true -or $Chunk.alwaysDirty -eq $true)) {
        [void]$reasons.Add('collision-or-unassigned')
    } else {
        foreach ($m in @($Before,$After)) {
            try { $rebuilt=Restore-RcAdmissionManifest $m $Chunk; foreach ($r in $rebuilt.unknown) { [void]$reasons.Add('input-unproven:'+ $r) } }
            catch {
                $reason=if ($_.Exception.Message.Contains('manifest-rehash-mismatch')) {'manifest-rehash-mismatch'} else {'manifest-invalid'}
                [void]$reasons.Add($reason)
            }
        }
        try { foreach ($r in (Compare-RcChunkInputManifest $Before $After).reasons) { [void]$reasons.Add($r) } }
        catch { [void]$reasons.Add('manifest-invalid') }
    }
    foreach ($pair in @(@('inventoryValid','inventory-unproven'),@('provenanceValid','source-build-launch-unproven'),
        @('complete','chunk-incomplete'),@('historyValid','failure-history-unproven'),@('originValid','origin-unproven'),@('contractValid','contract-unproven'))) {
        if ($Facts[$pair[0]] -isnot [bool] -or -not $Facts[$pair[0]]) { [void]$reasons.Add($pair[1]) }
    }
    if ($Facts.outcome -isnot [string] -or $Facts.outcome -cne 'Passed') { [void]$reasons.Add('chunk-not-passed') }
    $outcomes=@($Facts.caseOutcomes)
    if ($outcomes.Count -eq 0 -or $outcomes.Count -ne @($After.cases).Count -or @($outcomes | Where-Object { $_ -isnot [string] -or $_ -cne 'Passed' }).Count) {
        [void]$reasons.Add('cases-not-passed')
    }
    if (-not (Test-RcFullDigest $Facts.algorithmDigest) -or $Facts.algorithmDigest -cne $Facts.currentAlgorithmDigest) { [void]$reasons.Add('algorithm-binding-mismatch') }
    if ($Facts.releaseStatus -cne 'complete') { [void]$reasons.Add('release-evidence-incomplete') }
    $anchor=ConvertTo-RcFullUtcTime $Facts.anchorAtUtc
    if ($Facts.anchorVerified -isnot [bool] -or -not $Facts.anchorVerified -or $null -eq $anchor -or $anchor -gt $UtcNow -or ($UtcNow-$anchor) -ge [TimeSpan]::FromDays(7)) {
        [void]$reasons.Add('full-anchor-invalid-or-expired')
    }
    # Diagnostic eligibility ONLY. Read-RcAdmission owns byte provenance and the
    # non-overridable release-graph gate. This predicate grants no gate credit.
    return @{eligible=($reasons.Count -eq 0);reasons=@($reasons);gateCredit=$false}
}

function Get-RcAdmissionFileDigest {
    param([string]$Path)
    $stream=[IO.File]::OpenRead($Path)
    try { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
    finally { $stream.Dispose() }
}
function Read-RcAdmissionJson {
    param([string]$Path,[string]$ExpectedDigest)
    if (-not (Test-RcFullDigest $ExpectedDigest)) { throw 'admission-seal-required' }
    $file=[IO.FileInfo]::new($Path)
    if (-not $file.Exists -or $file.Length -gt 16777216 -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'admission-file-invalid' }
    # Bounded metadata only; never summary.json. Hash the exact bytes parsed.
    $bytes=[IO.File]::ReadAllBytes($Path)
    $digest=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    if ($digest -cne $ExpectedDigest) { throw 'admission-seal-mismatch' }
    $text=[Text.UTF8Encoding]::new($false,$true).GetString($bytes)
    $json=[Text.Json.JsonDocument]::Parse($text)
    try { Assert-RcInputJsonKeys $json.RootElement } finally { $json.Dispose() }
    $arguments=@{AsHashtable=$true;InputObject=$text}
    if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { $arguments.DateKind='String' }
    return (ConvertFrom-Json @arguments)
}
function Get-RcAdmissionObject {
    param($Inventory,[string]$EvidenceRoot,[string]$RelativePath)
    $matches=@($Inventory.files | Where-Object relativePath -CEQ $RelativePath)
    if ($matches.Count -ne 1) { throw 'admission-object-missing-or-duplicate' }
    $entry=$matches[0]
    # S5a verified confinement, links and exact object bytes. Also bind the bytes
    # used by this read, then reverify the full bundle before returning a decision.
    return @{path=(Join-Path $EvidenceRoot $entry.bundlePath);digest=$entry.sha256}
}
function Read-RcAdmissionTrx {
    param([string]$Path,$Cases,$CaseMap)
    $map=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($row in $CaseMap) {
        Assert-TestSelectionKeys $row @('testId','suite','className','method','caseKey')
        if (-not $row.testId -or $map.ContainsKey($row.testId)) { throw 'admission-case-alias' }
        $map.Add($row.testId,$row)
    }
    $expected=@(Sort-RcBoundaryIdentities @(foreach ($c in $Cases) { @{class=$c.className;method=$c.method;caseKey=$c.caseKey;suite=$c.suite} }))
    $actual=@(Sort-RcBoundaryIdentities @(foreach ($c in $CaseMap) { @{class=$c.className;method=$c.method;caseKey=$c.caseKey;suite=$c.suite} }))
    if ($expected.Count -ne $actual.Count -or -not $expected.Count) { throw 'admission-case-roster' }
    for ($i=0;$i -lt $expected.Count;$i++) {
        foreach ($field in @('class','method','caseKey','suite')) { if ($expected[$i][$field] -cne $actual[$i][$field]) { throw 'admission-case-roster' } }
    }
    $settings=[Xml.XmlReaderSettings]::new(); $settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit; $settings.XmlResolver=$null
    $settings.MaxCharactersInDocument=268435456
    $reader=[Xml.XmlReader]::Create($Path,$settings)
    $results=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    $definitions=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $methods=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $unit=''; $counters=$false; $terminal=$false
    try {
        while ($reader.Read()) {
            if ($reader.NodeType -ne [Xml.XmlNodeType]::Element) { continue }
            switch -CaseSensitive ($reader.LocalName) {
                'UnitTest' { $unit=$reader.GetAttribute('id'); if (-not $map.ContainsKey($unit) -or -not $definitions.Add($unit)) { throw 'admission-trx-definition' } }
                'TestMethod' {
                    if (-not $map.ContainsKey($unit) -or -not $methods.Add($unit) -or $reader.GetAttribute('className') -cne $map[$unit].className -or $reader.GetAttribute('name') -cne $map[$unit].method) { throw 'admission-trx-method' }
                }
                'UnitTestResult' {
                    $id=$reader.GetAttribute('testId')
                    if (-not $map.ContainsKey($id) -or $results.ContainsKey($id)) { throw 'admission-trx-result' }
                    $results.Add($id,$reader.GetAttribute('outcome'))
                }
                'ResultSummary' { if ($terminal) { throw 'admission-trx-summary' }; $terminal=($reader.GetAttribute('outcome') -ceq 'Completed') }
                'Counters' {
                    if ($counters) { throw 'admission-trx-counters' }; $counters=$true
                    foreach ($name in @('total','executed','passed')) { if ($reader.GetAttribute($name) -cne [string]$Cases.Count) { throw 'admission-trx-counters' } }
                    foreach ($name in @('failed','error','timeout','aborted','notExecuted')) { if ($reader.GetAttribute($name) -cne '0') { throw 'admission-trx-counters' } }
                }
            }
        }
    } finally { $reader.Dispose() }
    if (-not $terminal -or -not $counters -or $results.Count -ne $map.Count -or $definitions.Count -ne $map.Count -or $methods.Count -ne $map.Count) { throw 'admission-trx-incomplete' }
    return @($results.Values)
}

function Read-RcAdmission {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)][string]$ExpectedDigest,
        [Parameter(Mandatory)][string]$EvidenceRoot,[Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$ExpectedSha,[Parameter(Mandatory)][string]$ExpectedRunId,
        [Parameter(Mandatory)][string]$CurrentSha,[Parameter(Mandatory)]$CurrentManifest,
        [Parameter(Mandatory)]$Chunk,[Parameter(Mandatory)]$CurrentContext,[DateTimeOffset]$UtcNow=[DateTimeOffset]::UtcNow)
    $reasons=[Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    $eligible=$false; $coverage=Get-RcAdmissionAlgorithmCoverage
    try {
        if ($Chunk.boundary -ceq 'unassigned' -or $Chunk.collision -eq $true -or $Chunk.alwaysDirty -eq $true) { throw 'collision-or-unassigned' }
        $envelope=Read-RcAdmissionJson $Path $ExpectedDigest
        Assert-TestSelectionKeys $envelope @('format','sourceSha','treeId','runId','repository','platform','rcLine','inventorySha256','proofPath','fullAnchor','contract','history','releaseEvidence','anchorPath','historyPath')
        if ($envelope.format -cne 'antiphon-rc-admission-v1' -or $envelope.sourceSha -cne $ExpectedSha -or $envelope.runId -cne $ExpectedRunId -or
            -not $ExpectedRunId -or $envelope.repository -cne (Invoke-TestSelectionGit $RepositoryRoot @('config','--get','remote.origin.url')).Stdout.Trim() -or
            $envelope.repository -cne $CurrentContext.repository -or $envelope.platform -cne $CurrentContext.platform -or $envelope.rcLine -cne $CurrentContext.rcLine -or $envelope.platform -cne 'windows' -or $envelope.rcLine -cnotmatch '^rc/\d{4}-\d{2}-\d{2}$') { throw 'admission-run-source-identity' }
        if ((Invoke-TestSelectionGit $RepositoryRoot @('merge-base',$ExpectedSha,$CurrentSha)).Stdout.Trim() -cne $ExpectedSha) { throw 'admission-lineage' }
        $inventory=Test-RcHistoricalEvidence $EvidenceRoot $envelope.inventorySha256
        if ($inventory.valid -isnot [bool] -or -not $inventory.valid) { throw 'inventory-unproven' }
        $snapshot=Read-RcInputInventory $RepositoryRoot $CurrentSha
        $current=Restore-RcAdmissionManifest $CurrentManifest $Chunk $snapshot
        if ($CurrentManifest.sourceSha -cne $CurrentSha) { throw 'admission-current-source' }
        $release=$envelope.releaseEvidence
        Assert-TestSelectionKeys $release @('status','algorithmDigest','coveredPaths')
        $coverage=Get-RcAdmissionAlgorithmCoverage -CoveredPaths $release.coveredPaths
        $algorithm=Get-RcAdmissionAlgorithmDigest $RepositoryRoot $ExpectedSha $release.coveredPaths
        $currentAlgorithm=Get-RcAdmissionAlgorithmDigest $RepositoryRoot $CurrentSha $release.coveredPaths
        if ($release.algorithmDigest -cne $algorithm) { throw 'algorithm-binding-mismatch' }
        $anchorObject=Get-RcAdmissionObject $inventory $EvidenceRoot $envelope.anchorPath
        $anchorProof=Read-RcAdmissionJson $anchorObject.path $anchorObject.digest
        Assert-TestSelectionKeys $anchorProof @('format','sourceSha','completedAtUtc','mode','carriedCount','outcome','requiredCount','executedCount','caseDigest')
        if ($anchorProof.format -cne 'antiphon-rc-full-anchor-v1' -or $anchorObject.digest -cne $envelope.fullAnchor.closureDigest -or
            $anchorProof.sourceSha -cne $envelope.fullAnchor.sourceSha -or $anchorProof.completedAtUtc -cne $envelope.fullAnchor.completedAtUtc -or
            $anchorProof.mode -cne 'Full' -or $anchorProof.carriedCount -ne 0 -or $anchorProof.outcome -cne 'Passed' -or
            $anchorProof.requiredCount -le 0 -or $anchorProof.requiredCount -ne $anchorProof.executedCount -or -not (Test-RcFullDigest $anchorProof.caseDigest)) { throw 'full-anchor-unproven' }
        if ((Invoke-TestSelectionGit $RepositoryRoot @('merge-base',$anchorProof.sourceSha,$ExpectedSha)).Stdout.Trim() -cne $anchorProof.sourceSha) { throw 'full-anchor-lineage' }
        $historyObject=Get-RcAdmissionObject $inventory $EvidenceRoot $envelope.historyPath
        $historyProof=Read-RcAdmissionJson $historyObject.path $historyObject.digest
        Assert-TestSelectionKeys $historyProof @('format','runId','anchorDigest','complete','failures')
        if ($historyProof.format -cne 'antiphon-rc-history-v1' -or $historyProof.runId -cne $ExpectedRunId -or
            $historyProof.anchorDigest -cne $anchorObject.digest -or $historyProof.complete -isnot [bool] -or -not $historyProof.complete -or
            $historyProof.failures -isnot [array] -or $historyProof.failures.Count -ne 0) { throw 'failure-history-unproven' }
        # Nonempty failure history conservatively requires execution; no contained
        # fix is inferred from a later green result or a caller-supplied flag.
        $forced=Get-RcForcedFullDecision $CurrentContext.contract $envelope.contract $envelope.history $envelope.fullAnchor -UtcNow $UtcNow
        foreach ($r in $forced.reasons) { [void]$reasons.Add($r) }
        $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $proofPath=$envelope.proofPath; $before=$null; $caseOutcomes=@(); $top=$null
        for ($depth=0;;$depth++) {
            if ($depth -ge 64 -or -not $seen.Add($proofPath)) { throw 'admission-origin-cycle-or-depth' }
            $object=Get-RcAdmissionObject $inventory $EvidenceRoot $proofPath
            $proof=Read-RcAdmissionJson $object.path $object.digest
            Assert-TestSelectionKeys $proof @('format','kind','originPath','sourceSha','treeId','runId','chunk','manifest','outcome','complete','exitCode','sourceClean','custody','assemblyDigest','buildOutputDigest','discoveryOutputDigest','launchOutputDigest','receipts','trxPath','caseMap','outputPath','stageReceipts','anchorDigest')
            if ($proof.format -cne 'antiphon-rc-execution-proof-v1' -or $proof.outcome -cne 'Passed' -or $proof.complete -isnot [bool] -or -not $proof.complete -or $proof.exitCode -isnot [long] -and $proof.exitCode -isnot [int] -or $proof.exitCode -ne 0 -or
                $proof.sourceClean -isnot [bool] -or -not $proof.sourceClean -or $proof.custody -cne 'exclusive-owned-checkout') { throw 'source-build-launch-unproven' }
            if ($depth -eq 0) {
                $top=$proof
                if ($proof.sourceSha -cne $ExpectedSha -or $proof.runId -cne $ExpectedRunId -or $proof.treeId -cne $envelope.treeId) { throw 'admission-proof-identity' }
            }
            if ((Invoke-TestSelectionGit $RepositoryRoot @('merge-base',$proof.sourceSha,$CurrentSha)).Stdout.Trim() -cne $proof.sourceSha) { throw 'admission-origin-lineage' }
            $oldSnapshot=Read-RcInputInventory $RepositoryRoot $proof.sourceSha
            if ($oldSnapshot.treeId -cne $proof.treeId) { throw 'admission-proof-tree' }
            $old=Restore-RcAdmissionManifest $proof.manifest $proof.chunk $oldSnapshot
            $comparison=Compare-RcChunkInputManifest $old $current
            if ($comparison.dirty -or $proof.chunk.chunkId -cne $Chunk.chunkId -or $proof.chunk.fullDigest -cne $Chunk.fullDigest) { throw 'admission-origin-inputs' }
            if ($proof.anchorDigest -cne $anchorObject.digest) { throw 'admission-origin-anchor' }
            $output=Get-RcAdmissionObject $inventory $EvidenceRoot $proof.outputPath
            if ($output.digest -cne $proof.assemblyDigest) { throw 'admission-output-bytes' }
            $stages=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($stageRef in $proof.stageReceipts) {
                Assert-TestSelectionKeys $stageRef @('stage','path')
                if ($stageRef.stage -cnotin @('source','build','discovery','launch') -or -not $stages.Add($stageRef.stage)) { throw 'admission-stage-duplicate' }
                $stageObject=Get-RcAdmissionObject $inventory $EvidenceRoot $stageRef.path
                $stage=Read-RcAdmissionJson $stageObject.path $stageObject.digest
                Assert-TestSelectionKeys $stage @('format','stage','sourceSha','treeId','runId','manifestDigest','outputDigest','sourceState','custody')
                if ($stage.format -cne 'antiphon-rc-stage-proof-v1' -or $stage.stage -cne $stageRef.stage -or $stage.sourceSha -cne $proof.sourceSha -or
                    $stage.treeId -cne $proof.treeId -or $stage.runId -cne $proof.runId -or $stage.manifestDigest -cne $old.digest -or
                    $stage.outputDigest -cne $output.digest -or $stage.sourceState -cne 'clean' -or $stage.custody -cne 'exclusive-owned-checkout') { throw 'source-build-launch-unproven' }
            }
            if ($stages.Count -ne 4) { throw 'source-build-launch-unproven' }
            foreach ($digest in @('assemblyDigest','buildOutputDigest','discoveryOutputDigest','launchOutputDigest')) {
                if (-not (Test-RcFullDigest $proof[$digest]) -or $proof[$digest] -cne $proof.assemblyDigest) { throw 'source-build-launch-unproven' }
            }
            $receipts=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($ref in $proof.receipts) {
                Assert-TestSelectionKeys $ref @('kind','path','provenancePath')
                if (-not $receipts.Add($ref.kind)) { throw 'admission-receipt-duplicate' }
                $r=@($old.actualReceipts | Where-Object kind -CEQ $ref.kind)
                if ($r.Count -ne 1 -or $r[0].sourceSha -cne $proof.sourceSha) { throw 'admission-receipt-source' }
                $bytes=Get-RcAdmissionObject $inventory $EvidenceRoot $ref.path
                $provenance=Get-RcAdmissionObject $inventory $EvidenceRoot $ref.provenancePath
                if ($bytes.digest -cne $r[0].digest -or $provenance.digest -cne $r[0].provenanceDigest) { throw 'admission-receipt-digest' }
                $binding=Read-RcAdmissionJson $provenance.path $provenance.digest
                Assert-TestSelectionKeys $binding @('format','sourceSha','runId','kind','digest')
                if ($binding.format -cne 'antiphon-rc-actual-input-v1' -or $binding.sourceSha -cne $proof.sourceSha -or $binding.runId -cne $proof.runId -or
                    $binding.kind -cne $ref.kind -or $binding.digest -cne $bytes.digest) { throw 'admission-receipt-provenance' }
            }
            if ($receipts.Count -ne 6 -or $old.unknown.Count) { throw 'admission-receipt-incomplete' }
            if ($null -eq $before) { $before=$old }
            if ($proof.kind -ceq 'executed') {
                if ($null -ne $proof.originPath) { throw 'admission-origin-ambiguous' }
                $trx=Get-RcAdmissionObject $inventory $EvidenceRoot $proof.trxPath
                $caseOutcomes=@(Read-RcAdmissionTrx $trx.path $old.cases $proof.caseMap)
                break
            }
            if ($proof.kind -cne 'carried' -or $proof.originPath -isnot [string] -or -not $proof.originPath) { throw 'admission-origin-missing' }
            $proofPath=$proof.originPath
        }
        $facts=@{inventoryValid=$true;provenanceValid=$true;outcome=$top.outcome;complete=$top.complete;
            releaseStatus=$release.status;algorithmDigest=$algorithm;currentAlgorithmDigest=$currentAlgorithm;
            anchorAtUtc=$envelope.fullAnchor.completedAtUtc;anchorVerified=(-not $forced.forceFull);
            historyValid=(-not $forced.forceFull);originValid=$true;contractValid=(-not $forced.forceFull);caseOutcomes=$caseOutcomes}
        $decision=Get-RcCarryAdmissionDecision $before $current $facts -Chunk $Chunk -UtcNow $UtcNow
        foreach ($r in $decision.reasons) { [void]$reasons.Add($r) }
        $eligible=$decision.eligible -and $reasons.Count -eq 0
        $verified=Test-RcHistoricalEvidence $EvidenceRoot $envelope.inventorySha256
        if (-not $verified.valid -or (Get-RcAdmissionFileDigest $Path) -cne $ExpectedDigest) { throw 'admission-changed-during-read' }
    } catch { [void]$reasons.Add($_.Exception.Message); $eligible=$false }
    foreach ($r in $coverage.reasons) { [void]$reasons.Add($r) }
    $admitted=$eligible -and $coverage.complete
    return @{format='antiphon-rc-admission-result-v1';eligible=$eligible;admitted=$admitted;dirty=(-not $admitted);
        forceFull=(-not $admitted);reasons=@($reasons);coverage=$coverage;executionEvidence=$false;gateCredit=$false}
}
