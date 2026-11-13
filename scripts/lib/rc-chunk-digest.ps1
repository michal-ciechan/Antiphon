#requires -Version 7.0
# CARD-1836 S3b: reproducible input identities, never execution/carry authority.
. (Join-Path $PSScriptRoot 'rc-input-manifest.ps1')
. (Join-Path $PSScriptRoot 'rc-boundary.ps1')
function Get-RcManifestDigest {
    param([object[]]$Rows)
    # v1: counts and UTF-8 byte lengths are unsigned 32-bit big endian.
    $hash=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    $utf8=[Text.UTF8Encoding]::new($false,$true)
    try {
        $count=[BitConverter]::GetBytes([uint32]$Rows.Count)
        if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($count) }; $hash.AppendData($count)
        foreach ($row in $Rows) {
            $count=[BitConverter]::GetBytes([uint32]$row.Count)
            if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($count) }; $hash.AppendData($count)
            foreach ($field in $row) {
                if ($field -isnot [string]) { throw 'digest-field-must-be-string' }
                $bytes=$utf8.GetBytes($field)
                $length=[BitConverter]::GetBytes([uint32]$bytes.Length)
                if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($length) }
                $hash.AppendData($length); $hash.AppendData($bytes)
            }
        }
        return [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
    } finally { $hash.Dispose() }
}

function Assert-RcDigestText {
    param($Value, [string]$Label)
    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value) -or
        $Value.Length -gt 1048576 -or $Value -match '[\x00-\x1f]') { throw "invalid-digest-$Label" }
}

function New-RcChunkInputManifest {
    param($Snapshot, [string]$InputSuite, $Chunk, [object[]]$Cases, [object[]]$ActualReceipts)
    if ($Snapshot.format -cne 'antiphon-rc-input-snapshot-v1' -or $Snapshot.sha -cnotmatch '^[a-f0-9]{40}$' -or
        $Snapshot.treeId -cnotmatch '^[a-f0-9]{40}$') { throw 'invalid-input-snapshot' }
    $owners=@($Snapshot.suites | Where-Object id -CEQ $InputSuite)
    if ($owners.Count -ne 1) { throw 'missing-or-duplicate-input-suite' }
    $owner=$owners[0]
    $membershipId=Get-RcBoundaryChunkId $Chunk.suite $Chunk.boundary @($Chunk.identityKeys)
    if ($membershipId -cne $Chunk.chunkId) { throw 'chunk-membership-mismatch' }
    $unknown=[Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    foreach ($reason in $Snapshot.unresolved) { Assert-RcDigestText $reason 'unresolved'; [void]$unknown.Add($reason) }
    if ($Chunk.boundary -ceq 'unassigned') { [void]$unknown.Add('unassigned-boundary') }
    if ($owner.inputs.Count -gt 1000000 -or $Cases.Count -gt 1000000) { throw 'manifest-entry-limit' }
    Assert-RcDigestText $owner.project 'project'
    $inputs=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    $folded=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($record in $owner.inputs) {
        Assert-RcDigestText $record.path 'path'; Assert-RcDigestText $record.reason 'reason'
        if ($record.path -match '(^/|\\|(^|/)\.\.?(/|$)|:)' -or $record.version -ne 1 -or
            $record.suite -cne $InputSuite -or $record.project -cne $owner.project -or $record.present -isnot [bool]) { throw 'invalid-input-record' }
        if ($inputs.ContainsKey($record.path)) { throw 'duplicate-input-path' }
        if (-not $folded.Add($record.path)) { [void]$unknown.Add("case-collision:$($record.path)") }
        if ($record.present) {
            if ($record.objectId -cnotmatch '^[a-f0-9]{40}$' -or $record.mode -cnotmatch '^[0-7]{6}$' -or $record.type -cnotin @('blob','commit')) { throw 'invalid-input-object' }
            if ($record.mode -cnotin @('100644','100755') -or $record.type -cne 'blob') { [void]$unknown.Add("unresolved-link-or-submodule:$($record.path)") }
        } elseif ($null -ne $record.objectId -or $null -ne $record.mode -or $null -ne $record.type) { throw 'absent-input-has-object' }
        $inputs.Add($record.path,@('input','1',$InputSuite,[string]$record.project,[string]$record.path,
            $(if ($record.present) {'present'} else {'absent'}),[string]$record.mode,[string]$record.type,[string]$record.objectId,
            ((Get-TestSelectionSorted @($record.reason.Split(';'))) -join ';')))
    }
    if (-not $inputs.Count) { throw 'empty-input-manifest' }
    $inputRows=[Collections.Generic.List[object]]::new()
    $inputRows.Add(@('antiphon-rc-inputs','1',$InputSuite,[string]$owner.project))
    foreach ($path in (Get-TestSelectionSorted @($inputs.Keys))) { $inputRows.Add($inputs[$path]) }
    $edges=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($edge in $owner.edges) {
        foreach ($field in @('from','to','project','reason')) { Assert-RcDigestText $edge[$field] "edge-$field" }
        $row=@('edge',[string]$edge.from,[string]$edge.to,[string]$edge.project,[string]$edge.reason)
        $edges[(ConvertTo-Json -InputObject $row -Compress)]=$row
    }
    foreach ($key in (Get-TestSelectionSorted @($edges.Keys))) { $inputRows.Add($edges[$key]) }
    $inputDigest=Get-RcManifestDigest $inputRows.ToArray()

    # A suite shard aggregates methods only after every case has been accounted for.
    $methods=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($key in $Chunk.identityKeys) { $methods.Add($key,[Collections.Generic.List[object]]::new()) }
    $caseMap=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($case in $Cases) {
        Assert-TestSelectionKeys $case @('suite','className','method','caseKey','stable')
        foreach ($field in @('suite','className','method','caseKey')) { Assert-RcDigestText $case[$field] "case-$field" }
        if ($case.suite -cne $Chunk.suite) { throw 'case-suite-mismatch' }
        if ($case.className.Contains('/') -or $case.stable -isnot [bool]) { throw 'invalid-case-identity' }
        $methodKey=$case.className+'/'+$case.method
        if (-not $methods.ContainsKey($methodKey)) { throw 'extra-method-membership' }
        $key=ConvertTo-Json -InputObject @($case.suite,$case.className,$case.method,$case.caseKey) -Compress
        if ($caseMap.ContainsKey($key)) { throw 'duplicate-case' }
        $normalized=@{key=$key;suite=$case.suite;className=$case.className;method=$case.method;caseKey=$case.caseKey;stable=$case.stable}
        $caseMap.Add($key,$normalized); $methods[$methodKey].Add($normalized)
        if (-not $case.stable) { [void]$unknown.Add("ambiguous-case:$methodKey") }
    }
    $caseRows=[Collections.Generic.List[object]]::new()
    $caseRows.Add(@('antiphon-rc-cases','1',[string]$Chunk.suite,[string]$Chunk.boundary))
    $methodRows=[Collections.Generic.List[object]]::new()
    foreach ($method in (Get-TestSelectionSorted @($methods.Keys))) {
        if (-not $methods[$method].Count) { throw 'incomplete-method-membership' }
        $keys=@(Get-TestSelectionSorted @($methods[$method].key))
        $methodRows.Add(@{identityKey=$method;cases=@(foreach ($key in $keys) { $caseMap[$key] });
            ambiguous=($unknown.Contains("ambiguous-case:$method"))})
    }
    $caseRecords=@(foreach ($key in (Get-TestSelectionSorted @($caseMap.Keys))) {
        $case=$caseMap[$key]
        $caseRows.Add(@('case',[string]$case.suite,[string]$case.className,[string]$case.method,[string]$case.caseKey,
            $(if ($case.stable) {'stable'} else {'ambiguous'})))
        $case
    })
    $caseDigest=Get-RcManifestDigest $caseRows.ToArray()

    $required=@('sdk-runtime-os-architecture','powershell-node-browser-native','configuration-environment',
        'resolved-package-graph-and-content','generated-fixture-recipes-and-outputs','immutable-images')
    $receiptMap=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($receipt in $ActualReceipts) {
        Assert-TestSelectionKeys $receipt @('kind','digest','provenanceDigest','sourceSha')
        if ($receipt.kind -cnotin $required -or $receipt.digest -cnotmatch '^[a-f0-9]{64}$' -or
            $receipt.provenanceDigest -cnotmatch '^[a-f0-9]{64}$' -or $receipt.sourceSha -cnotmatch '^[a-f0-9]{40}$') { throw 'invalid-actual-receipt' }
        if ($receiptMap.ContainsKey($receipt.kind)) { throw 'duplicate-actual-receipt' }
        # S5b independently rehashes retained bytes and verifies these source/build joins.
        $receiptMap.Add($receipt.kind,@{kind=$receipt.kind;digest=$receipt.digest;provenanceDigest=$receipt.provenanceDigest;sourceSha=$receipt.sourceSha})
    }
    $receiptRows=[Collections.Generic.List[object]]::new(); $receiptRows.Add(@('antiphon-rc-actual-inputs','1'))
    foreach ($kind in (Get-TestSelectionSorted $required)) {
        if (-not $receiptMap.ContainsKey($kind)) { [void]$unknown.Add("missing-actual-receipt:$kind"); $receiptRows.Add(@($kind,'missing')) }
        else { $receiptRows.Add(@($kind,[string]$receiptMap[$kind].digest)) }
    }
    $receiptDigest=Get-RcManifestDigest $receiptRows.ToArray()
    $membershipRows=[Collections.Generic.List[object]]::new()
    $membershipRows.Add(@('antiphon-rc-membership','1',[string]$Chunk.suite,[string]$Chunk.boundary))
    foreach ($key in (Get-TestSelectionSorted @($Chunk.identityKeys))) { $membershipRows.Add(@($key)) }
    $membershipDigest=Get-RcManifestDigest $membershipRows.ToArray()
    $digest=Get-RcManifestDigest @(@('antiphon-rc-chunk-input','1',$InputSuite,[string]$Chunk.suite,[string]$Chunk.boundary,
        $inputDigest,$caseDigest,$receiptDigest,$membershipDigest))
    return @{format='antiphon-rc-chunk-input-v1';sourceSha=$Snapshot.sha;treeId=$Snapshot.treeId;inputSuite=$InputSuite;
        suite=$Chunk.suite;boundary=$Chunk.boundary;chunkId=$membershipId;membershipDigest=$membershipDigest;
        digest=$digest;inputDigest=$inputDigest;caseDigest=$caseDigest;receiptDigest=$receiptDigest;
        inputs=@($owner.inputs);edges=@($owner.edges);cases=$caseRecords;methods=$methodRows.ToArray();
        actualReceipts=@(foreach ($kind in (Get-TestSelectionSorted @($receiptMap.Keys))) { $receiptMap[$kind] });
        unknown=@($unknown);executionEvidence=$false;gateCredit=$false;activation='shadow-only'}
}
function Compare-RcChunkInputManifest {
    param($Before, $After)
    $reasons=[Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    foreach ($manifest in @($Before,$After)) {
        if ($manifest.format -cne 'antiphon-rc-chunk-input-v1' -or $manifest.digest -cnotmatch '^[a-f0-9]{64}$') { throw 'invalid-chunk-input-manifest' }
        foreach ($reason in $manifest.unknown) { [void]$reasons.Add($reason) }
    }
    if ($Before.inputSuite -cne $After.inputSuite -or $Before.suite -cne $After.suite -or $Before.boundary -cne $After.boundary) { [void]$reasons.Add('owner-or-boundary-changed') }
    foreach ($pair in @(@('inputDigest','inputs-changed'),@('caseDigest','cases-changed'),
        @('receiptDigest','actual-inputs-changed'),@('membershipDigest','membership-changed'))) {
        if ($Before[$pair[0]] -cne $After[$pair[0]]) { [void]$reasons.Add($pair[1]) }
    }
    if ($Before.digest -cne $After.digest) { [void]$reasons.Add('chunk-inputs-changed') }
    $current=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($case in $After.cases) { [void]$current.Add($case.key) }
    return @{dirty=($reasons.Count -gt 0);reasons=@($reasons);
        retired=@($Before.cases | Where-Object { -not $current.Contains($_.key) });executionEvidence=$false;gateCredit=$false}
}
