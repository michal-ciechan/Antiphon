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
$before=Manifest $baseline
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
