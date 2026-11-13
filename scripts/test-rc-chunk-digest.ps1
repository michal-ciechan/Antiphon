#requires -Version 7.0
param([string]$Case, [string]$ResultsDirectory)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/lib/rc-chunk-digest.ps1"
. "$PSScriptRoot/lib/c487-harness.ps1"
if (-not $ResultsDirectory) { $ResultsDirectory=Join-Path ([IO.Path]::GetTempPath()) ('rcd-'+[guid]::NewGuid().ToString('N')) }
$root=Join-Path $ResultsDirectory 'repository with spaces'
[void][IO.Directory]::CreateDirectory($root)
$errors=[Collections.Generic.List[string]]::new()
function Note($ok,[string]$label) { if (-not $ok) { $errors.Add($label) } }
function Git([string[]]$a) { Invoke-C487FixtureGit $root $a }
function Put([string]$path,[string]$text) {
    $full=Join-Path $root $path
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $full)); [IO.File]::WriteAllText($full,$text)
}
function Commit {
    [void](Git @('add','--all')); [void](Git @('commit','-qm','fixture')); Git @('rev-parse','HEAD')
}
function Refuses([scriptblock]$operation,[string]$reason) {
    try { & $operation | Out-Null; Note $false "refusal:$reason" }
    catch { Note ($_.Exception.Message.Contains($reason,[StringComparison]::Ordinal)) "refusal-reason:$reason" }
}
function RefusesEncoding([scriptblock]$operation, [string]$label) {
    try { & $operation | Out-Null; Note $false ("encoding-accepted:"+$label) }
    catch {
        $errorType=$_.Exception
        while ($null -ne $errorType -and $errorType -isnot [Text.EncoderFallbackException]) {
            $errorType=$errorType.InnerException
        }
        Note ($null -ne $errorType) ("encoding-error-type:"+$label+':'+$_.Exception.Message)
    }
}
function Manifest([string]$sha, [object[]]$rows=$cases, [object[]]$receipts=$actual) {
    $snapshot=Read-RcInputInventory $root $sha
    New-RcChunkInputManifest -Snapshot $snapshot -InputSuite server -Chunk $chunk -Cases $rows -ActualReceipts $receipts
}
[void](Git @('init','-q','-b','main'))
[void](Git @('config','user.name','Fixture')); [void](Git @('config','user.email','fixture@example.invalid'))
$map=@{schemaVersion=1;suites=@(@{id='server';project='tests/Server/Server.csproj'},@{id='other';project='tests/Other/Other.csproj'});
    roots=@(@{path='scripts/**';consumers=@('*');reason='harness'})}
Put 'scripts/lib/rc-input-owners.json' ($map | ConvertTo-Json -Depth 8)
Put 'tests/Server/Server.csproj' '<Project><ItemGroup><ProjectReference Include="../../server/Server.csproj" /></ItemGroup></Project>'
Put 'tests/Server/SessionMessageQueueServiceTests.cs' 'unchanged test'
Put 'server/Server.csproj' '<Project><ItemGroup><ProjectReference Include="../src/Core/Core.csproj" /></ItemGroup></Project>'
Put 'server/Application/SessionMessageQueueService.cs' 'old service'
Put 'src/Core/Core.csproj' '<Project />'
Put 'src/Core/Input.txt' 'old transitive input'
Put 'tests/Other/Other.csproj' '<Project />'
Put 'tests/Other/Input.cs' 'unrelated'
$baseline=Commit
$chunk=New-RcBoundaryChunk @(@{suite='antiphon';boundary='application';identityKey='Example.Queue/Check'},
    @{suite='antiphon';boundary='application';identityKey='Example.Queue/Other'})
$cases=@(@{suite='antiphon';className='Example.Queue';method='Check';caseKey='row:a';stable=$true},
    @{suite='antiphon';className='Example.Queue';method='Check';caseKey='row:b';stable=$true},
    @{suite='antiphon';className='Example.Queue';method='Other';caseKey='singleton';stable=$true})
# These represent retained actual receipt digests, not Git substitutes. Admission owns their verification.
$actual=@(foreach ($kind in @('sdk-runtime-os-architecture','powershell-node-browser-native','configuration-environment',
    'resolved-package-graph-and-content','generated-fixture-recipes-and-outputs','immutable-images')) {
    @{kind=$kind;digest=('a'*64);provenanceDigest=('b'*64);sourceSha=$baseline}
})
$before=if ($Case -cne 'LoneSurrogatesFailClosed') { Manifest $baseline }
switch ($Case) {
    'TransitiveEditAndDeletionChangeDigest' {
        Note ($before.unknown.Count -eq 0 -and $before.digest -cmatch '^[a-f0-9]{64}$') 'known-baseline'
        foreach ($path in @('server/Application/SessionMessageQueueService.cs','src/Core/Input.txt')) {
            $previous=Manifest (Git @('rev-parse','HEAD'))
            Put $path ('edit '+$path); $sha=Commit
            $after=Manifest $sha
            Note ($previous.inputDigest -cne $after.inputDigest) "transitive-edit:$path"
            Note ((Compare-RcChunkInputManifest $previous $after).dirty) "changed-dirty:$path"
        }
        Note ((Git @('rev-parse',"${baseline}:tests/Server/SessionMessageQueueServiceTests.cs")) -ceq
            (Git @('rev-parse',"${sha}:tests/Server/SessionMessageQueueServiceTests.cs"))) 'unchanged-test-shape'
        $previous=$after
        [IO.File]::Delete((Join-Path $root 'src/Core/Input.txt')); $sha=Commit; $after=Manifest $sha
        Note ($previous.inputDigest -cne $after.inputDigest) 'deletion-digest'
        Note ((Compare-RcChunkInputManifest $previous $after).dirty) 'deletion-dirty'
        Put 'server/Application/SessionMessageQueueService.cs' 'uncommitted'
        Note ((Manifest $sha).digest -ceq $after.digest) 'object-only-no-working-bytes'
    }
    'ProvenUnrelatedProjectKeepsDigest' {
        Put 'tests/Other/Input.cs' 'changed unrelated'; $sha=Commit; $after=Manifest $sha
        Note ($before.unknown.Count -eq 0 -and $after.unknown.Count -eq 0) 'known-closure'
        Note ($before.digest -and $before.digest -ceq $after.digest) 'unrelated-digest-equal'
        Note (-not (Compare-RcChunkInputManifest $before $after).dirty) 'unrelated-clean'
        $snapshot=Read-RcInputInventory $root $sha
        $snapshot.suites[1].inputs=[object[]]@($snapshot.suites[1].inputs)[($snapshot.suites[1].inputs.Count-1)..0]
        $reordered=New-RcChunkInputManifest $snapshot server $chunk @($cases[2],$cases[1],$cases[0]) @($actual[5..0])
        Note ($reordered.digest -ceq $after.digest) 'ordinal-order-independent'
        Note ((ConvertTo-TestSelectionCanonicalJson $reordered.inputs) -ceq (ConvertTo-TestSelectionCanonicalJson $after.inputs)) 'canonical-record-order'
        $renewed=@($actual | ForEach-Object { $copy=$_.Clone(); $copy.sourceSha=$sha; $copy.provenanceDigest='d'*64; $copy })
        Note ((Manifest $sha $cases $renewed).digest -ceq $after.digest) 'run-provenance-is-not-input-equivalence'
        Note (-not $after.executionEvidence -and -not $after.gateCredit) 'no-carry-authority'
        # Independent fixed encoding vector: fields are UTF-8, uint32 big endian counts/lengths.
        if (Get-Command Get-RcManifestDigest -ErrorAction SilentlyContinue) {
            Note ((Get-RcManifestDigest @(@('ab','c'),@('d'))) -ceq 'bbebcb50cc28f8ce245d7a308abc76bda287ab2df8ae5eb804b23787e5b5649d') 'encoding-vector'
            Note ((Get-RcManifestDigest @(@('a','bc'),@('d'))) -cne (Get-RcManifestDigest @(@('ab','c'),@('d')))) 'length-delimited'
        } else { Note $false 'encoding-vector' }
    }
    'UnknownAndCaseSetChangesAreDirty' {
        $after=Manifest $baseline @($cases[0],$cases[2])
        Note ($before.caseDigest -cne $after.caseDigest -and (Compare-RcChunkInputManifest $before $after).dirty) 'removed-case-dirty'
        Note ((Compare-RcChunkInputManifest $before $after).retired.Count -eq 1) 'retired-case-recorded'
        $extra=@{suite='antiphon';className='Example.Queue';method='Check';caseKey='row:c';stable=$true}
        $after=Manifest $baseline @($cases+$extra)
        Note ($before.caseDigest -cne $after.caseDigest) 'added-case-dirty'
        Refuses { Manifest $baseline @($cases+$cases[0]) } 'duplicate-case'
        Refuses { Manifest $baseline @($cases[0],$cases[1]) } 'incomplete-method-membership'
        Refuses { Manifest $baseline @($cases+@{suite='antiphon';className='Example.Queue';method='Extra';caseKey='singleton';stable=$true}) } 'extra-method-membership'
        Refuses { Manifest $baseline @($cases+@{suite='another';className='Example.Queue';method='Check';caseKey='row:x';stable=$true}) } 'case-suite-mismatch'
        $unstable=@($cases | ForEach-Object { $copy=$_.Clone(); if ($copy.caseKey -ceq 'row:a') { $copy.stable=$false }; $copy })
        $after=Manifest $baseline $unstable
        Note ($after.unknown -ccontains 'ambiguous-case:Example.Queue/Check' -and (Compare-RcChunkInputManifest $before $after).dirty) 'ambiguous-method-dirty'
        $after=Manifest $baseline $cases @()
        Note ($after.unknown.Count -eq 6 -and (Compare-RcChunkInputManifest $after $after).dirty) 'missing-actual-receipts-stay-dirty'
        $changed=@($actual | ForEach-Object { $copy=$_.Clone(); if ($copy.kind -ceq 'immutable-images') { $copy.digest='c'*64 }; $copy })
        Note ((Manifest $baseline $cases $changed).digest -cne $before.digest) 'actual-receipt-change-bound'
        Refuses { Manifest $baseline $cases @($actual+$actual[0]) } 'duplicate-actual-receipt'
        Put 'unknown/file.txt' 'unknown'; $after=Manifest (Commit)
        Note ($after.unknown.Count -gt 0 -and (Compare-RcChunkInputManifest $after $after).dirty) 'unowned-never-clean'
    }
    'CaseKeyedMultipleCasesMatchMembership' {
        $chunk=New-RcBoundaryChunk @(foreach ($row in $cases) {
            @{suite=$row.suite;boundary='application';identityKey=($row.className+'/'+$row.method);caseKey=$row.caseKey}
        })
        try {
            $manifest=Manifest $baseline
            Note ($manifest.unknown.Count -eq 0 -and $manifest.methods.Count -eq 2 -and $manifest.cases.Count -eq 3) 'case-keyed-complete'
            Note ($manifest.methods[0].cases.Count -eq 2) 'method-keys-deduplicated'
            Note ($manifest.membershipDigest -ceq $chunk.fullDigest) 'canonical-membership'
            Refuses { Manifest $baseline @($cases[0],$cases[2]) } 'case-membership-mismatch'
            $renamed=@($cases | ForEach-Object { $copy=$_.Clone(); if ($copy.caseKey -ceq 'row:a') { $copy.caseKey='row:c' }; $copy })
            Refuses { Manifest $baseline $renamed } 'case-membership-mismatch'
            $forged=@{}; foreach ($key in $chunk.Keys) { $forged[$key]=$chunk[$key] }; $forged.fullDigest='f'*64
            Refuses { New-RcChunkInputManifest (Read-RcInputInventory $root $baseline) server $forged $cases $actual } 'chunk-membership-mismatch'
        } catch { Note $false ('case-keyed-manifest:'+ $_.Exception.Message) }
    }
    'CaseKeyedSingletonsMatchMembership' {
        $cases=@($cases[0],$cases[2])
        $chunk=New-RcBoundaryChunk @(foreach ($row in $cases) {
            @{suite=$row.suite;boundary='application';identityKey=($row.className+'/'+$row.method);caseKey=$row.caseKey}
        })
        try {
            $manifest=Manifest $baseline
            Note ($manifest.unknown.Count -eq 0 -and $manifest.methods.Count -eq 2 -and $manifest.cases.Count -eq 2) 'case-keyed-singletons-complete'
            Note ($manifest.membershipDigest -ceq $chunk.fullDigest) 'singleton-canonical-membership'
            Refuses { Manifest $baseline @($cases+@{suite='antiphon';className='Example.Queue';method='Check';caseKey='extra';stable=$true}) } 'case-membership-mismatch'
        } catch { Note $false ('singleton-manifest:'+ $_.Exception.Message) }
    }
    'CaseOnlyMembershipChangesDigest' {
        $manifests=@(foreach ($caseKey in @('row:a','row:b')) {
            $chunk=New-RcBoundaryChunk @(@{suite='antiphon';boundary='application';identityKey='Example.Queue/Check';caseKey=$caseKey})
            $rows=@(@{suite='antiphon';className='Example.Queue';method='Check';caseKey=$caseKey;stable=$true})
            try {
                $manifest=Manifest $baseline $rows
                Note ($manifest.membershipDigest -ceq $chunk.fullDigest) "canonical-case-digest:$caseKey"
                $manifest
            } catch { Note $false ('case-digest-manifest:'+ $_.Exception.Message) }
        })
        Note ($manifests.Count -eq 2) 'two-case-memberships'
        if ($manifests.Count -eq 2) {
            Note ($manifests[0].membershipDigest -cne $manifests[1].membershipDigest) 'case-only-membership-different'
            Note ((Compare-RcChunkInputManifest $manifests[0] $manifests[1]).reasons -ccontains 'membership-changed') 'case-only-membership-dirty'
        }
    }
    'CanonicalCaseOrderAndEncoding' {
        # JSON escaping reverses these two case keys; S2 compares their raw strings.
        $rows=@(@{suite='antiphon';className='Example.Queue';method='Check';caseKey='#';stable=$true},
            @{suite='antiphon';className='Example.Queue';method='Check';caseKey='"';stable=$true},$cases[2])
        $manifest=Manifest $baseline $rows
        Note ($manifest.cases[0].caseKey -ceq '"' -and $manifest.cases[1].caseKey -ceq '#') 'canonical-global-case-order'
        Note ($manifest.methods[0].cases[0].caseKey -ceq '"') 'canonical-method-case-order'
        Note ((Manifest $baseline @($rows[2],$rows[1],$rows[0])).digest -ceq $manifest.digest) 'case-order-independent'
        # CARD-1871 rejects invalid UTF-16; S3b must use the same encoding rule.
        RefusesEncoding { Get-RcManifestDigest @(@('row',[string][char]0xd800),@('other')) } 'canonical-field'
        Note ((Get-RcManifestDigest @(@('row',[string][char]0xfffd),@('other'))) -cmatch '^[a-f0-9]{64}$') 'replacement-character-is-valid'
    }
    'LoneSurrogatesFailClosed' {
        # No digest/decision is returned for malformed paths or case names.
        foreach ($codeUnit in @(0xd800,0xdc00)) {
            $invalid=[string][char]$codeUnit
            RefusesEncoding { Get-RcManifestDigest @(@('row',$invalid),@('other')) } "field-$codeUnit"
            $snapshot=Read-RcInputInventory $root $baseline
            $owner=@($snapshot.suites | Where-Object id -CEQ server)[0]
            $owner.inputs[0].path='src/Core/bad'+$invalid+'.txt'
            RefusesEncoding { New-RcChunkInputManifest $snapshot server $chunk $cases $actual } "path-$codeUnit"
            $badCases=@($cases | ForEach-Object { $_.Clone() })
            $badCases[0].caseKey='row:'+$invalid
            RefusesEncoding { Manifest $baseline $badCases } "case-$codeUnit"
        }
        $validCases=@($cases | ForEach-Object { $_.Clone() })
        $validCases[0].caseKey='row:'+[char]0xfffd
        try {
            $valid=Manifest $baseline $validCases
            Note ($valid.unknown.Count -eq 0 -and $valid.digest -cmatch '^[a-f0-9]{64}$') 'valid-unicode-still-accepted'
        } catch { Note $false ('valid-unicode:'+ $_.Exception.Message) }
    }
    'DependencyEdgesAffectDigest' {
        $snapshot=Read-RcInputInventory $root $baseline
        $owner=@($snapshot.suites | Where-Object id -CEQ server)[0]
        Note ($owner.edges.Count -gt 0) 'fixture-has-dependency-edges'
        $inputRecords=ConvertTo-TestSelectionCanonicalJson $owner.inputs
        $owner.edges=@()
        $withoutEdges=New-RcChunkInputManifest $snapshot server $chunk $cases $actual
        Note ((ConvertTo-TestSelectionCanonicalJson $withoutEdges.inputs) -ceq $inputRecords) 'edge-only-change-keeps-input-records'
        Note ($withoutEdges.inputDigest -cne $before.inputDigest) 'dependency-edges-bound-independently'
        Note ((Compare-RcChunkInputManifest $before $withoutEdges).reasons -ccontains 'inputs-changed') 'dependency-edge-only-dirty'
    }
    'RenameSymlinkAndCaseCollision' {
        [void](Git @('mv','src/Core/Input.txt','src/Core/Renamed.txt')); $after=Manifest (Commit)
        Note ($after.inputDigest -cne $before.inputDigest -and (Compare-RcChunkInputManifest $before $after).dirty) 'path-only-rename'
        # Git object construction is portable: no native symlink privilege or case-sensitive filesystem required.
        $oid=Git @('rev-parse','HEAD:src/Core/Renamed.txt')
        [void](Git @('update-index','--add','--cacheinfo',"120000,$oid,src/Core/link"))
        $tree=Git @('write-tree'); $sha=Git @('commit-tree',$tree,'-p',(Git @('rev-parse','HEAD')),'-m','link fixture')
        $after=Manifest $sha
        Note (@($after.unknown | Where-Object { $_ -like '*unresolved-link-or-submodule*' }).Count -gt 0) 'unresolved-symlink-dirty'
        [void](Git @('update-index','--add','--cacheinfo',"100644,$oid,src/Core/renamed.txt"))
        $tree=Git @('write-tree'); $sha=Git @('commit-tree',$tree,'-p',$sha,'-m','collision fixture')
        $after=Manifest $sha
        Note (@($after.unknown | Where-Object { $_ -like '*case-collision*' }).Count -gt 0) 'windows-case-collision-dirty'
        Note ((Compare-RcChunkInputManifest $after $after).dirty) 'unknown-equality-never-clean'
    }
    default { throw "unknown case:$Case" }
}
Assert-C487 ($errors.Count -eq 0) ("RCD $Case.contract") ($errors -join '; ')
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 1
