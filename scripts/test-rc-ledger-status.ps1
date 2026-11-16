#requires -Version 7.0
param([string]$Case,[string]$ResultsDirectory)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/lib/c487-harness.ps1"
. "$PSScriptRoot/lib/test-selection.ps1"
$library=Join-Path $PSScriptRoot 'lib/rc-incremental-plan.ps1'
if (Test-Path -LiteralPath $library) { . $library }
$errors=[Collections.Generic.List[string]]::new()
function Note($ok,[string]$label) { if (-not $ok) { $errors.Add($label) } }
function Copy-Rs($v) { ConvertFrom-Json -AsHashtable -InputObject (ConvertTo-Json -Depth 60 -InputObject $v) }
function Refuses([scriptblock]$body,[string]$reason) {
    try { & $body | Out-Null; Note $false ('accepted:'+ $reason) }
    catch { Note ($_.Exception.Message.Contains($reason)) ('wrong-refusal:'+ $reason+':'+$_.Exception.Message) }
}
function Obligation([string]$method,[string]$caseKey='one',[string]$chunk='chunk-a') {
    @{suite='server';className='Example.Tests';method=$method;caseKey=$caseKey;stable=$true;boundary='core';chunkId=$chunk}
}
function Decision($row,[string]$kind='Carry',[string]$chunk='historical-one',[double]$wall=12) {
    @{suite=$row.suite;className=$row.className;method=$row.method;caseKey=$row.caseKey;disposition=$kind;reasons=@('fixture');
        origin=@{suite=$row.suite;envelopeDigest=('a'*64);runId='old-run';sourceSha=('b'*40);chunkId=$chunk;wallSeconds=$wall}}
}
function Suites($rows) { ,@(@{suite='server';adapter='tunit';cases=@($rows)}) }
if (-not $ResultsDirectory) { $ResultsDirectory=Join-Path ([IO.Path]::GetTempPath()) ('rs-'+[guid]::NewGuid().ToString('N')) }
[void][IO.Directory]::CreateDirectory($ResultsDirectory)
$a=Obligation A; $b=Obligation B one chunk-b
$suites=Suites @($a,$b); $decisions=@((Decision $a),(Decision $b))
try {
    switch ($Case) {
        'PartialDecisionsCannotCloseUniverse' {
            Refuses { New-RcExactPartition $suites @($decisions[0]) } 'decision-universe-mismatch'
            Refuses { New-RcExactPartition $suites @($decisions+$decisions[0]) } 'duplicate-decision'
            $extra=Decision (Obligation Extra)
            Refuses { New-RcExactPartition $suites @($decisions+$extra) } 'extra-decision'
            Refuses { New-RcExactPartition (Suites @($a,$a)) @($decisions[0]) } 'duplicate-obligation'
            $all=New-RcExactPartition $suites $decisions
            Note ($all.required.Count -eq 2 -and $all.carried.Count -eq 2 -and $all.launch.Count -eq 0) 'all-carried-no-launch'
            $dirty=@((Decision $a Dirty),(Decision $b Dirty))
            $none=New-RcExactPartition $suites $dirty
            Note ($none.dirty.Count -eq 2 -and $none.carried.Count -eq 0 -and $none.launch.Count -eq 2) 'zero-carried-exact'
            $reverse=New-RcExactPartition @($suites) @($decisions[1],$decisions[0])
            Note ($all.digest -ceq $reverse.digest) 'ordinal-digest-independent-of-order'
        }
        'ParameterizedMethodsAndNonTunitStayRequired' {
            $x=Obligation Rows alpha; $y=Obligation Rows beta chunk-b
            $p=New-RcExactPartition (Suites @($x,$y)) @((Decision $x Dirty),(Decision $y))
            Note ($p.dirty.Count -eq 2 -and $p.carried.Count -eq 0) 'whole-parameterized-method-dirty'
            $y=Obligation B beta chunk-a
            $p=New-RcExactPartition (Suites @($a,$y)) @((Decision $a Dirty),(Decision $y))
            Note ($p.launch.Count -eq 2 -and $p.carried.Count -eq 0) 'packed-chunk-actually-launched'
            $p=New-RcExactPartition (Suites @($a,$y)) @((Decision $a Sample),(Decision $y))
            Note ($p.sampled.Count -eq 2 -and $p.carried.Count -eq 0) 'sample-complete-chunk'
            $fresh=@{suite='client';adapter='fresh';cases=@()}
            $p=New-RcExactPartition @($suites[0],$fresh) $decisions
            Note ($p.required.Count -eq 3 -and $p.dirty.Count -eq 1 -and $p.dirty[0].suite -ceq 'client' -and $p.dirty[0].reasons -ccontains 'fresh-suite-adapter-required') 'non-tunit-suite-cannot-vanish'
            Refuses { New-RcExactPartition @(@{suite='client';adapter='tunit';cases=@()}) @() } 'empty-tunit-discovery'
            $ambiguous=Copy-Rs $a; $ambiguous.stable=$false
            $p=New-RcExactPartition (Suites @($ambiguous)) @((Decision $ambiguous))
            Note ($p.dirty.Count -eq 1 -and $p.dirty[0].reasons -ccontains 'ambiguous-case') 'unstable-discovery-dirty'
            $other=Copy-Rs $a; $other.suite='other'
            $p=New-RcExactPartition @($suites[0],@{suite='other';adapter='tunit';cases=@($other)}) @($decisions+(Decision $other))
            Note ($p.required.Count -eq 3) 'same-method-cross-suite-distinct'
            $mixed=Copy-Rs $a; $mixed.boundary='other'
            Refuses { New-RcExactPartition (Suites @($a,$mixed)) $decisions } 'mixed-boundary-chunk'
        }
        'StatusCountsOriginsWithoutDoubleCharging' {
            $p=New-RcExactPartition $suites $decisions
            $s=Get-RcPartitionStatus $p @()
            Note ($s.planned -eq 2 -and $s.carried -eq 2 -and $s.executed -eq 0 -and $s.notRun -eq 0 -and $s.uniqueOriginChunks -eq 1 -and $s.historicalPotentialWallSeconds -eq 12) 'split-source-chunk-once'
            $d=@((Decision $a Dirty),(Decision $b Sample))
            $p=New-RcExactPartition $suites $d
            $s=Get-RcPartitionStatus $p @(@{key=$p.launch[0].key;outcome='Failed'})
            Note ($s.planned -eq 2 -and $s.carried -eq 0 -and $s.sampled -eq 1 -and $s.failed -eq 1 -and $s.executed -eq 1 -and $s.notRun -eq 1 -and $s.historicalPotentialWallSeconds -eq 0) 'actual-execution-separate-from-history'
            $bad=Copy-Rs $decisions; $bad[1].origin.wallSeconds=13
            Refuses { Get-RcPartitionStatus (New-RcExactPartition $suites $bad) @() } 'origin-wall-conflict'
            $bad=Copy-Rs $decisions; $bad[1].origin.envelopeDigest='c'*64
            Refuses { Get-RcPartitionStatus (New-RcExactPartition $suites $bad) @() } 'origin-envelope-conflict'
            Refuses { Get-RcPartitionStatus $p @(@{key='absent';outcome='Passed'}) } 'result-outside-launch'
            $r=@{key=$p.launch[0].key;outcome='Unproven'}
            $s=Get-RcPartitionStatus $p @($r)
            Note ($s.unproven -eq 1 -and $s.notRun -eq 1) 'unproven-not-green'
            Refuses { Get-RcPartitionStatus $p @($r,$r) } 'duplicate-result'
        }
        'CrashRevisionCannotPublishClosure' {
            # The real entrypoint binds a current committed fixture and receipts.
            $repo=Join-Path $ResultsDirectory 'repo'; [void][IO.Directory]::CreateDirectory($repo)
            [void](Invoke-C487FixtureGit $repo @('init','-q','-b','main'))
            [void](Invoke-C487FixtureGit $repo @('config','user.name','Fixture'))
            [void](Invoke-C487FixtureGit $repo @('config','user.email','fixture@example.invalid'))
            [IO.File]::WriteAllText((Join-Path $repo 'input'),'input')
            [void](Invoke-C487FixtureGit $repo @('add','input')); [void](Invoke-C487FixtureGit $repo @('commit','-qm','fixture'))
            $sha=Invoke-C487FixtureGit $repo @('rev-parse','HEAD'); $tree=Invoke-C487FixtureGit $repo @('rev-parse','HEAD^{tree}')
            $partition=New-RcExactPartition $suites $decisions
            $bindings=@{}
            foreach ($kind in @('input','output')) {
                $receipt=@{kind=$kind;candidateSha=$sha;treeId=$tree;universeDigest=$partition.universeDigest}
                $path=Join-Path $ResultsDirectory ($kind+'.json')
                [IO.File]::WriteAllText($path,($receipt | ConvertTo-Json -Compress))
                $bindings[$kind]=@{path=$path;digest=(Get-RcAdmissionFileDigest $path)}
            }
            $request=@{format='antiphon-rc-plan-request-v1';candidateSha=$sha;treeId=$tree;platform='linux';rcLine='rc/2026-11-23';suites=$suites;decisions=$decisions;bindings=$bindings;admissions=@();exclusionDigest=('c'*64);contractDigest=('d'*64);boundaryMapDigest=('e'*64);historyDigest=('f'*64);sampleSeed='fixture-seed';sampleBudget=30}
            $requestPath=Join-Path $ResultsDirectory 'request.json'; [IO.File]::WriteAllText($requestPath,($request | ConvertTo-Json -Depth 60))
            $planPath=Join-Path $ResultsDirectory 'plan.json'
            & "$PSScriptRoot/rc-ledger-plan.ps1" -RequestPath $requestPath -RequestDigest (Get-RcAdmissionFileDigest $requestPath) -RepositoryRoot $repo -ExpectedSha $sha -PlanPath $planPath
            Note ($LASTEXITCODE -eq 0) 'plan-command'
            $plan=Read-RcAdmissionJson $planPath (Get-RcAdmissionFileDigest $planPath)
            Note ($plan.effectiveMode -ceq 'Full' -and $plan.partition.carried.Count -eq 0 -and $plan.partition.launch.Count -eq 2 -and -not $plan.gateCredit -and -not $plan.coverageComplete -and $plan.forcedReasons -ccontains 'release-graph-incomplete') 'admission-stays-fail-closed'
            $bad=Copy-Rs $request; $bad.bindings.input.digest='0'*64
            $fallback=New-RcIncrementalPlan $bad $repo $sha
            Note ($fallback.forcedReasons -ccontains 'receipt-unproven:input' -and $fallback.partition.launch.Count -eq 2) 'changed-receipt-full'
            $bad=Copy-Rs $request; $bad.decisions=@($bad.decisions[0])
            $fallback=New-RcIncrementalPlan $bad $repo $sha
            Note ($fallback.forcedReasons -ccontains 'proposal-invalid:decision-universe-mismatch' -and $fallback.partition.launch.Count -eq 2) 'partial-public-proposal-full'
            $bad=Copy-Rs $request; $bad.sampleSeed='different-seed'
            Note ((New-RcIncrementalPlan $bad $repo $sha).digest -cne $plan.digest) 'plan-binds-sample-seed'
            $bad=Copy-Rs $request; $bad.platform='windows'
            $windows=New-RcIncrementalPlan $bad $repo $sha
            Note ($plan.coverageScope -ceq 'PlatformDiagnostic' -and $windows.coverageScope -ceq 'IncrementalDiagnostic' -and -not $windows.coverageComplete -and -not $windows.gateCredit) 'platform-labels-grant-no-credit'
            Refuses { New-RcIncrementalPlan $request $repo ('1'*40) } 'candidate-binding-mismatch'
            Refuses { Write-RcIncrementalPlan $planPath $plan } 'write-once'
            $root=Join-Path $ResultsDirectory 'revisions'
            $revision=Write-RcPlanRevision $root $planPath $plan.digest @()
            $status=Read-RcPlanStatus $root $plan.digest
            Note ($status.notRun -eq 2 -and -not $status.coverageComplete -and -not $status.gateCredit) 'initial-open'
            $failed=@{key=$plan.partition.launch[0].key;outcome='Failed'}
            $revision=Write-RcPlanRevision $root $planPath $plan.digest @($failed)
            Refuses { Write-RcPlanRevision $root $planPath $plan.digest @() } 'revision-result-history'
            $changed=Copy-Rs $failed; $changed.outcome='Passed'
            Refuses { Write-RcPlanRevision $root $planPath $plan.digest @($changed) } 'revision-result-history'
            # Orphan durable revision after a crash cannot replace the published pointer.
            [void][IO.Directory]::CreateDirectory((Join-Path $root 'revisions/3'))
            [IO.File]::WriteAllText((Join-Path $root 'revisions/3/revision.json'),'{"state":"complete","results":["Passed"]}')
            $again=Read-RcPlanStatus $root $plan.digest
            Note ($again.sequence -eq 2 -and $again.failed -eq 1 -and $again.notRun -eq 1) 'crash-keeps-previous-pointer'
            Refuses { Write-RcPlanRevision $root $planPath $plan.digest @($failed) } 'revision-already-exists'
            $lock=[IO.File]::Open((Join-Path $root '.writer.lock'),[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
            try { Refuses { Write-RcPlanRevision $root $planPath $plan.digest @() } 'writer-busy' } finally { $lock.Dispose() }
            $before=Get-RcAdmissionFileDigest (Join-Path $root 'index.json')
            & "$PSScriptRoot/rc-ledger-status.ps1" -EvidenceRoot $root -ExpectedPlanDigest $plan.digest
            Note ($LASTEXITCODE -eq 0 -and $before -ceq (Get-RcAdmissionFileDigest (Join-Path $root 'index.json'))) 'status-read-only'
            [IO.File]::AppendAllText((Join-Path $root $revision.path),' ')
            Refuses { Read-RcPlanStatus $root $plan.digest } 'admission-seal-mismatch'
            $allPassed=@(foreach ($row in $plan.partition.launch) { @{key=$row.key;outcome='Passed'} })
            $otherRoot=Join-Path $ResultsDirectory 'all-passed'
            $pointer=Write-RcPlanRevision $otherRoot $planPath $plan.digest $allPassed
            $status=Read-RcPlanStatus $otherRoot $plan.digest
            Note ($status.executed -eq 2 -and $status.notRun -eq 0 -and -not $status.coverageComplete -and -not $status.gateCredit) 'ordinary-results-cannot-publish-closure'
            $revisionPath=Join-Path $otherRoot $pointer.path
            $terminal=Read-RcAdmissionJson $revisionPath $pointer.sha256; $terminal.state='complete'
            [IO.File]::WriteAllText($revisionPath,($terminal | ConvertTo-Json -Depth 60))
            $pointer.sha256=Get-RcAdmissionFileDigest $revisionPath
            [IO.File]::WriteAllText((Join-Path $otherRoot 'index.json'),($pointer | ConvertTo-Json -Depth 10))
            Refuses { Read-RcPlanStatus $otherRoot $plan.digest } 'revision-not-open-or-bound'
        }
        default { throw 'unknown-case' }
    }
} catch { Note $false ('unexpected:'+ $_.Exception.Message) }
Assert-C487 ($errors.Count -eq 0) ("RS $Case.contract") ($errors -join '; ')
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 1
