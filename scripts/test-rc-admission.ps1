#requires -Version 7.0
param([string]$Case, [string]$ResultsDirectory)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/lib/c487-harness.ps1"
. "$PSScriptRoot/lib/rc-chunk-digest.ps1"
$library=Join-Path $PSScriptRoot 'lib/rc-admission.ps1'
if (Test-Path -LiteralPath $library) { . $library }
$errors=[Collections.Generic.List[string]]::new()
function Note($ok,[string]$label) { if (-not $ok) { $errors.Add($label) } }
function Clone-Ra($value) { ConvertFrom-Json -AsHashtable -InputObject (ConvertTo-Json -Depth 60 -InputObject $value) }
function Check($before,$after,$facts,[string]$reason) {
    $d=Get-RcCarryAdmissionDecision $before $after $facts -UtcNow ([DateTimeOffset]'2026-11-13T12:00:00Z')
    Note (-not $d.eligible -and $d.reasons -ccontains $reason) ("missing-refusal:"+$reason)
}
if (-not $ResultsDirectory) { $ResultsDirectory=Join-Path ([IO.Path]::GetTempPath()) ('ra-'+[guid]::NewGuid().ToString('N')) }
# Every repository and retained corpus is under the harness-owned temporary root.
$root=Join-Path $ResultsDirectory 'fixture'
[void][IO.Directory]::CreateDirectory($root)
function Git([string[]]$a) { Invoke-C487FixtureGit $root $a }
function Put([string]$path,[string]$value) {
    $full=Join-Path $root $path
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $full)); [IO.File]::WriteAllText($full,$value)
}
[void](Git @('init','-q','-b','main'))
[void](Git @('config','user.name','Fixture')); [void](Git @('config','user.email','fixture@example.invalid'))
Put 'scripts/lib/rc-input-owners.json' (@{schemaVersion=1;suites=@(@{id='server';project='tests/Server/Server.csproj'});roots=@(@{path='scripts/**';consumers=@('*');reason='harness'})} | ConvertTo-Json -Depth 8)
Put 'tests/Server/Server.csproj' '<Project />'
Put 'tests/Server/Input.cs' 'test input'
[void](Git @('add','--all')); [void](Git @('commit','-qm','fixture'))
$sha=Git @('rev-parse','HEAD')
$chunk=New-RcBoundaryChunk @(@{suite='antiphon';boundary='core';identityKey='Example.Tests/Check';caseKey='row:a'})
$cases=@(@{suite='antiphon';className='Example.Tests';method='Check';caseKey='row:a';stable=$true})
$actual=@(foreach ($kind in @('sdk-runtime-os-architecture','powershell-node-browser-native','configuration-environment','resolved-package-graph-and-content','generated-fixture-recipes-and-outputs','immutable-images')) {
    @{kind=$kind;digest=('a'*64);provenanceDigest=('b'*64);sourceSha=$sha}
})
$manifest=New-RcChunkInputManifest (Read-RcInputInventory $root $sha) server $chunk $cases $actual
$facts=@{inventoryValid=$true;provenanceValid=$true;outcome='Passed';complete=$true;releaseStatus='complete';
    algorithmDigest=('c'*64);currentAlgorithmDigest=('c'*64);anchorAtUtc='2026-11-12T12:00:00Z';
    anchorVerified=$true;historyValid=$true;originValid=$true;contractValid=$true;caseOutcomes=@('Passed')}
try {
    switch ($Case) {
        'FullProvenanceAdmitsAndReverifies' {
            $d=Get-RcCarryAdmissionDecision $manifest (Clone-Ra $manifest) $facts -UtcNow ([DateTimeOffset]'2026-11-13T12:00:00Z')
            Note ($d.eligible -and $d.reasons.Count -eq 0) 'complete-equal-proposal'
            $bad=Clone-Ra $manifest; @($bad.inputs | Where-Object present)[0].objectId='f'*40
            Check $manifest $bad $facts 'manifest-rehash-mismatch'
            $bad=Clone-Ra $manifest; $bad.cases[0].caseKey='forged'
            Check $manifest $bad $facts 'manifest-invalid'
            $bad=Clone-Ra $manifest; $bad.digest='f'*64
            Check $bad $bad $facts 'manifest-rehash-mismatch'
            # Also exercise the actual reader's byte and Git-object verification.
            & "$PSScriptRoot/test-rc-admission-reader.ps1" -Case $Case -ResultsDirectory $ResultsDirectory -RepositoryRoot $root -SourceSha $sha
            Note ($LASTEXITCODE -eq 0) 'reader-reverification'
        }
        'ShaLabelWithoutBuildProofIsUnproven' {
            $bad=Clone-Ra $facts; $bad.provenanceValid=$false
            Check $manifest $manifest $bad 'source-build-launch-unproven'
            $bad=Clone-Ra $manifest; $bad.actualReceipts=@()
            Check $manifest $bad $facts 'manifest-rehash-mismatch'
        }
        'IncompleteChunkAndCaseAliasNeverCarry' {
            foreach ($outcome in @('Failed','Timeout','Aborted','partial','NotExecuted','passed','Unknown')) {
                $bad=Clone-Ra $facts; $bad.outcome=$outcome
                Check $manifest $manifest $bad 'chunk-not-passed'
            }
            $bad=Clone-Ra $facts; $bad.complete=$false; Check $manifest $manifest $bad 'chunk-incomplete'
            $bad=Clone-Ra $facts; $bad.caseOutcomes=@('Failed'); Check $manifest $manifest $bad 'cases-not-passed'
            $bad=Clone-Ra $facts; $bad.caseOutcomes=@(); Check $manifest $manifest $bad 'cases-not-passed'
            $bad=Clone-Ra $facts; $bad.caseOutcomes=@('Passed','Passed'); Check $manifest $manifest $bad 'cases-not-passed'
            $bad=Clone-Ra $manifest; $bad.cases+=Clone-Ra $bad.cases[0]; Check $manifest $bad $facts 'manifest-invalid'
        }
        'ChangedEnvelopeHistoryAndOriginChainRefuse' {
            foreach ($pair in @(@('inventoryValid','inventory-unproven'),@('historyValid','failure-history-unproven'),@('originValid','origin-unproven'),@('contractValid','contract-unproven'))) {
                $bad=Clone-Ra $facts; $bad[$pair[0]]=$false; Check $manifest $manifest $bad $pair[1]
            }
            & "$PSScriptRoot/test-rc-admission-reader.ps1" -Case $Case -ResultsDirectory $ResultsDirectory -RepositoryRoot $root -SourceSha $sha
            Note ($LASTEXITCODE -eq 0) 'reader-history-origin'
        }
        'EveryIngredientMustMatch' {
            foreach ($field in @('digest','inputDigest','caseDigest','receiptDigest','membershipDigest','chunkId','format')) {
                $bad=Clone-Ra $manifest; $bad[$field]='wrong'
                $d=Get-RcCarryAdmissionDecision $manifest $bad $facts -UtcNow ([DateTimeOffset]'2026-11-13T12:00:00Z')
                Note (-not $d.eligible -and $d.reasons.Count -gt 0) ("field:"+$field)
            }
            $bad=Clone-Ra $facts; $bad.algorithmDigest='d'*64; Check $manifest $manifest $bad 'algorithm-binding-mismatch'
            $bad=Clone-Ra $facts; $bad.releaseStatus='incomplete'; Check $manifest $manifest $bad 'release-evidence-incomplete'
            foreach ($time in @('2026-11-06T12:00:00Z','2026-11-14T00:00:00Z','unknown',$null)) {
                $bad=Clone-Ra $facts; $bad.anchorAtUtc=$time; Check $manifest $manifest $bad 'full-anchor-invalid-or-expired'
            }
            $bad=Clone-Ra $facts; $bad.anchorVerified=$false; Check $manifest $manifest $bad 'full-anchor-invalid-or-expired'
            foreach ($field in @($facts.Keys)) {
                $bad=Clone-Ra $facts; $bad.Remove($field)
                $d=Get-RcCarryAdmissionDecision $manifest $manifest $bad -UtcNow ([DateTimeOffset]'2026-11-13T12:00:00Z')
                Note (-not $d.eligible) ("missing:"+$field)
            }
        }
        'CollisionAndAlgorithmCoverageForceFull' {
            $members=@(foreach ($key in @('10788987499786589518','17490688019260441962')) {
                @{suite='antiphon';boundary='core';identities=@(@{identityKey='CTests/M';caseKey=$key})}
            })
            $resolved=@(Resolve-RcBoundaryChunks $members)
            Note ($resolved.Count -eq 2 -and $resolved[0].collision -and $resolved[1].collision) 'real-s2-collision'
            foreach ($c in $resolved) {
                $d=Get-RcCarryAdmissionDecision $manifest $manifest $facts -Chunk $c -UtcNow ([DateTimeOffset]'2026-11-13T12:00:00Z')
                Note (-not $d.eligible -and $d.reasons -ccontains 'collision-or-unassigned') 'collision-dirty-before-s3b'
            }
            $coverage=Get-RcAdmissionAlgorithmCoverage
            Note (-not $coverage.complete -and $coverage.requiredPaths.Count -ge 8 -and $coverage.missingPaths.Count -gt 0) 'explicit-incomplete-coverage'
            foreach ($path in $coverage.requiredPaths) {
                $paths=@($coverage.requiredPaths | Where-Object { $_ -cne $path })
                $c=Get-RcAdmissionAlgorithmCoverage -CoveredPaths $paths
                Note (-not $c.complete -and $c.missingPaths -ccontains $path) ("coverage:"+$path)
            }
            $c=Get-RcAdmissionAlgorithmCoverage -CoveredPaths $coverage.requiredPaths
            Note (-not $c.complete -and $c.reasons -ccontains 'release-graph-incomplete') 'no-coverage-override'
            # A byte edit in EACH prerequisite changes the bound algorithm identity.
            foreach ($path in $coverage.requiredPaths) { Put $path 'a' }
            [void](Git @('add','--all')); [void](Git @('commit','-qm','algorithm fixture'))
            $prior=Git @('rev-parse','HEAD')
            $priorDigest=Get-RcAdmissionAlgorithmDigest $root $prior $coverage.requiredPaths
            foreach ($path in $coverage.requiredPaths) {
                Put $path 'b'
                [void](Git @('add','--',$path)); [void](Git @('commit','-qm','one byte'))
                $changed=Git @('rev-parse','HEAD')
                $nextDigest=Get-RcAdmissionAlgorithmDigest $root $changed $coverage.requiredPaths
                Note ($priorDigest -cne $nextDigest) ('algorithm-byte:'+ $path)
                $priorDigest=$nextDigest
            }
        }
        'ImportIsInertAndWritesUnprovenShard' {
            & "$PSScriptRoot/test-rc-admission-reader.ps1" -Case $Case -ResultsDirectory $ResultsDirectory -RepositoryRoot $root -SourceSha $sha
            Note ($LASTEXITCODE -eq 0) 'import-shard'
        }
        default { throw 'unknown case' }
    }
} catch { Note $false ('unexpected:'+ $_.Exception.Message) }
Assert-C487 ($errors.Count -eq 0) ("RA $Case.contract") ($errors -join '; ')
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 1
