#requires -Version 7.0
param([string]$Case, [string]$ResultsDirectory, [string]$FilterHost)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/lib/nightly-common.ps1"
. "$PSScriptRoot/lib/nightly-policy.ps1"
. "$PSScriptRoot/lib/nightly-coverage.ps1"
. "$PSScriptRoot/lib/release-gate.ps1"
. "$PSScriptRoot/lib/release-authority.ps1"
. "$PSScriptRoot/lib/test-selection.ps1"
. "$PSScriptRoot/lib/release-selection.ps1"
. "$PSScriptRoot/lib/nightly-tests-impl.ps1"
. "$PSScriptRoot/lib/c487-harness.ps1"
$ResultsDirectory = New-C487Root $ResultsDirectory
$repoRoot = Split-Path -Parent $PSScriptRoot
$script:FixtureRoots = @()

function Write-Fx($Root, $Relative, $Text) {
    $path = Join-Path $Root $Relative
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
    [IO.File]::WriteAllText($path, $Text)
}
function Git-Fx($Root, $Arguments) { return (Invoke-TestSelectionGit $Root $Arguments).Stdout.Trim() }
function Same-Set($Actual, $Expected) { return ((@($Actual | Sort-Object -CaseSensitive) -join "`n") -ceq (@($Expected | Sort-Object -CaseSensitive) -join "`n")) }
function Assert-Fx($Condition, $Label, $Detail = '') { Assert-C487 ([bool]$Condition) ('C1313 ' + $Label) $Detail }
function Get-FxTreeDigest($Root) {
    $files = @{}; foreach ($file in Get-ChildItem -LiteralPath $Root -Recurse -File -Force) {
        $files[$file.FullName.Substring($Root.Length)] = Get-NightlyFileSha256 $file.FullName
    }
    return Get-NightlySha256Text (ConvertTo-NightlyCanonicalJson $files)
}

function New-Fx {
    $root = Join-Path $ResultsDirectory ('space ' + [guid]::NewGuid().ToString('N'))
    $seed = Join-Path $root 'seed'; [void][IO.Directory]::CreateDirectory($seed)
    $script:FixtureRoots += $root
    [void](Git-Fx $seed @('init','-q','-b','rc/2026-10-19'))
    [void](Git-Fx $seed @('config','user.name','C1313 fixture'))
    [void](Git-Fx $seed @('config','user.email','fixture@example.invalid'))
    [void](Git-Fx $seed @('config','core.autocrlf','false'))
    [void][IO.Directory]::CreateDirectory((Join-Path $seed 'scripts'))
    Copy-Item "$PSScriptRoot/lib" (Join-Path $seed 'scripts/lib') -Recurse
    foreach ($file in @('nightly-run.ps1','nightly-tests.ps1','release-cut.ps1','release-candidate.ps1','select-test-rosters.ps1')) {
        Copy-Item (Join-Path $PSScriptRoot $file) (Join-Path $seed "scripts/$file")
    }
    foreach ($file in @('tests/test-execution-policy.json','tests/test-selection-policy.json','global.json')) {
        Write-Fx $seed $file ([IO.File]::ReadAllText((Join-Path $repoRoot $file)))
    }
    $catalog = Get-Content "$seed/tests/test-selection-policy.json" -Raw | ConvertFrom-Json
    foreach ($path in @($catalog.sets.sourceInputs + $catalog.sets.members.source | Sort-Object -Unique)) {
        if ($path -and -not (Test-Path (Join-Path $seed $path))) { Write-Fx $seed $path 'fixture source' }
    }
    # Preserve the complete production test-project census, including probes outside tests/.
    # This authority fixture does not need unrelated production project graphs: each
    # request otherwise shells out to Git for all of them at both committed revisions.
    foreach ($path in (Git-Fx $repoRoot @('ls-files','*.csproj')).Split("`n")) {
        $projectText = [IO.File]::ReadAllText((Join-Path $repoRoot $path))
        if ($projectText -match '(?is)<IsTestProject>\s*true\s*</IsTestProject>') {
            Write-Fx $seed $path $projectText
        }
    }
    # Committed differing wrapper bytes force the real self-reexec edge. These tiny
    # repositories track their ownership marker so checkout cleanup can be exercised.
    Add-Content "$seed/scripts/nightly-run.ps1" '# C1313 owned clone wrapper'
    Write-Fx $seed '.antiphon-release-owned' '{"kind":"release-clone"}'
    Write-Fx $seed '.antiphon-nightly-owned' '{"kind":"nightly-clone"}'
    Write-Fx $seed 'client/src/App.tsx' 'baseline client'
    [void](Git-Fx $seed @('add','--all')); [void](Git-Fx $seed @('commit','-qm','baseline'))
    $b = Git-Fx $seed @('rev-parse','HEAD')
    Write-Fx $seed 'tests/Antiphon.Tests/Agents/CodexResponseAnalyzerTests.cs' 'candidate provider'
    [void](Git-Fx $seed @('add','--all')); [void](Git-Fx $seed @('commit','-qm','candidate'))
    $a = Git-Fx $seed @('rev-parse','HEAD')
    $origin = Join-Path $root 'origin.git'; [void](Git-Fx $root @('clone','--bare',$seed,$origin))
    $release = Join-Path $root ('releases ' + [char]0x3bb); [void][IO.Directory]::CreateDirectory($release)
    $clone = Join-Path $release 'checkout'; [void](Git-Fx $root @('clone',$origin,$clone))
    $fx = @{Root=$root;Seed=$seed;Clone=$clone;Origin=$origin;Release=$release;B=$b;A=$a
        Ref='rc/2026-10-19';Repository='fixture/repository';Seams=(Join-Path $root 'workload.ps1');Trace=(Join-Path $root 'workload.jsonl')}
    $nodes = @(@{uid='fixture-one';type='Fixture';method='One';namespace='Fixture'})
    $rows = @(@{Id='fixture-one';ClassName='Fixture.Fixture';MethodName='One';Outcome='Passed'})
    Write-C487NativePassSeams $fx.Seams $fx.Trace $rows $nodes
    # The existing synthetic workload seam observes every production suite/entry.
    # It is never credited as native evidence or publication authority.
    Add-Content $fx.Seams @'
$NightlySeams.Git = $null
$NightlySeams.E2EPrerequisites = { return @{Ok=$true;Rows=@()} }
'@
    return $fx
}
function New-Request($Fx, $Mode = 'Shadow', $Run = '') {
    if (-not $Run) { $Run = [guid]::NewGuid().ToString('N') }
    $id = (Test-ReleaseGateCandidateRef $Fx.Ref $Fx.A).CandidateId
    return @{RepositoryRoot=$Fx.Clone;Repository=$Fx.Repository;ReleaseRoot=$Fx.Release;CandidateId=$id;CandidateRef=$Fx.Ref
        CandidateSha=$Fx.A;BaselineSha=$Fx.B;SelectionMode=$Mode;Profile='rc';RunId=$Run;Attempt=1
        BaselineReceiptPath='';QualificationPath='';TimingRoot=(Join-Path $Fx.Release 'timing root')
        ReleaseIntentPath='';TrainManifestPath=''}
}
function Bind-Request($Request) {
    $bound = New-ReleaseSelectionAttempt $Request
    foreach ($name in @('SelectionAttemptPath','SelectionAttemptDigest','SelectionPath','SelectionDigest','BaselineSha')) { $Request[$name] = $bound.$name }
    return $bound
}
function Invoke-Workload($Fx, $Request, $Log = '') {
    if (-not $Log) { $Log = Join-Path $Fx.Root ([guid]::NewGuid().ToString('N')) }
    $splat = @{RepoRoot=$Fx.Clone;LogRoot=$Log;Sha=$Request.CandidateSha;ExpectedSha=$Request.CandidateSha;GitRef=$Fx.Ref
        Profile='rc';Trigger='rc';RunId=$Request.RunId;SeamsPath=$Fx.Seams;PassThru=$true;ReleaseRoot=$Fx.Release;CandidateId=$Request.CandidateId;Repository=$Fx.Repository}
    foreach ($key in @('SelectionMode','BaselineSha','BaselineReceiptPath','QualificationPath','TimingRoot','ReleaseIntentPath','TrainManifestPath',
        'SelectionAttemptPath','SelectionAttemptDigest','SelectionPath','SelectionDigest','Attempt')) { $splat[$key]=$Request[$key] }
    return & "$PSScriptRoot/nightly-tests.ps1" @splat
}
function Get-FxDiscovery {
    . "$PSScriptRoot/lib/checkpoint-process-runner.ps1"
    $dir = Join-Path $ResultsDirectory 'discovery'; [void][IO.Directory]::CreateDirectory($dir)
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    foreach ($arg in @($FilterHost,'--list-tests','--no-progress','--no-ansi','--diagnostic','--diagnostic-output-directory',$dir,'--results-directory',$dir)) { $start.ArgumentList.Add($arg) }
    $run = [Antiphon.Checkpoints.CheckpointProcessRunner]::Run($start,60000,$false)
    [IO.File]::WriteAllText((Join-Path $dir 'console.log'),$run.Stdout+$run.Stderr)
    if ($run.Exit -ne 0) { throw 'fixture-discovery-failed' }
    $parsed = ConvertFrom-NightlyDiagnosticLog (Get-NightlyLatestDiagnosticLog $dir) discovery
    return ConvertTo-NightlyDiscoveryDocument $parsed.Nodes (Get-NightlyAssemblyHash $FilterHost)
}
function Invoke-FxRoster($Selection, $Discovery, $Label) {
    $full = @(@{class='Antiphon.Tests.FilterHost.C1313Offline';method='One'},@{class='Antiphon.Tests.FilterHost.C1313Offline';method='Rows'})
    $proposed = @($full[0])
    $members = @(Resolve-ReleaseSelectionMembers $Selection $full $proposed)
    $dir = Join-Path $ResultsDirectory $Label
    $receipt = Invoke-NightlyExactRoster -AssemblyPath $FilterHost -Discovery $Discovery -Members $members -ResultsDirectory $dir `
        -Environment @{C1313_SENTINEL=(Join-Path $ResultsDirectory 'forbidden.txt')}
    $terminals = @(); foreach ($trx in Get-ChildItem $dir -Recurse -Filter run.trx) { $terminals += @(Read-NightlyTrxIdentities $trx.FullName) }
    return ($receipt.complete -and $terminals.Count -eq 4 -and
        @($terminals | Where-Object {$_.Outcome -cne 'Passed' -or $_.ClassName -cne 'Antiphon.Tests.FilterHost.C1313Offline'}).Count -eq 0 -and
        @($terminals | Where-Object MethodName -Match '^Rows').Count -eq 3 -and -not (Test-Path (Join-Path $ResultsDirectory 'forbidden.txt')))
}

function Test-C1313_ModeCredit {
    $state = @{profile='rc';trigger='rc';ref='rc/2026-10-19';sha=('a'*40);expectedSha=('a'*40);candidateId='fixture';runId='fixture';policyHash='fixture';exitCode=0
        coverageComplete=$true;testsPassed=$true;reportDelivered=$true;coverageScope='ChangeAware'}
    $rejected = $true
    foreach ($scope in @('ChangeAware','invented','',@('Full'))) { $state.coverageScope=$scope; if ((Get-ReleaseGateCreditVerdict $state).Ok) {$rejected=$false} }
    Assert-Fx $rejected 'unqualified-no-credit'
    $fx = New-Fx; $discovery = Get-FxDiscovery
    $qualification = Join-Path $fx.Release 'qualification/active.json'; Write-Fx $fx.Release 'qualification/active.json' '{"status":"active","diagnostic":true}'
    $qualificationRoot = Split-Path -Parent $qualification
    $before = Get-FxTreeDigest $qualificationRoot
    foreach ($mode in @('omitted','Full','Shadow','ChangeAware')) {
        $request = New-Request $fx $(if ($mode -ceq 'omitted') {'Full'} else {$mode})
        $request.QualificationPath=$qualification
        if ($mode -ceq 'omitted') {
            $default = & "$PSScriptRoot/release-cut.ps1" -ReleaseRoot $fx.Release -CoordinationRoot (Join-Path $fx.Root 'coord') -RemoteUrl $fx.Origin -Repository $fx.Repository -Ref $fx.Ref -RunId $request.RunId -BaselineSha $fx.B -TimingRoot $request.TimingRoot -QualificationPath $qualification -SeamsPath $fx.Seams -SkipPublish -PassThru
            if (-not $default.selection -or $default.selection.requestedMode -cne 'Full') { Assert-Fx $false 'default-full' ($default|ConvertTo-Json -Depth 10 -Compress); return }
        }
        $bound = Bind-Request $request
        $record = $bound.Selection
        $selection = Get-Content $bound.SelectionPath -Raw | ConvertFrom-Json
        $execution = Invoke-Workload $fx $request
        $trace = Get-Content $fx.Trace -Raw
        $suiteNames = @('antiphon','session-runner','pty-host','agents-pty','messaging','client','scripts','e2e','markdown-pdf')
        if (Test-Path (Join-Path $repoRoot 'tests/Antiphon.Observability.Tests/Antiphon.Observability.Tests.csproj')) { $suiteNames += 'observability' }
        $scriptNames = @('test-apphost-graceful-stop','test-apphost-lock-age','test-apphost-main-worktree-guard','test-apphost-probe-class',
            'test-cleanup-claude-sessions','test-cleanup-codex-test-residue','test-client-mode','test-deploy-am-service','test-deploy-nightly-watchdog',
            'test-hooks','test-nightly-report','test-reap-zombie-agents','test-stage-value-report')
        $observedAll = (Same-Set $execution.Selected $suiteNames) -and
            @($scriptNames | Where-Object {$trace -notmatch ([regex]::Escape($_ + '.ps1'))}).Count -eq 0
        $native = Invoke-FxRoster $record $discovery ('native-' + $mode)
        $label = @{omitted='default-full';Full='explicit-full';Shadow='shadow-full';ChangeAware='unqualified-full'}[$mode]
        Assert-Fx ($record.requestedMode -ceq $request.SelectionMode -and $record.effectiveMode -ceq 'Full' -and
            $record.coverageScope -ceq 'Full' -and $observedAll -and $native -and
            ($mode -cne 'Shadow' -or ($selection.proposal.mode -ceq 'narrow' -and (Same-Set $selection.proposal.selectedSets.id @('core','provider.codex.offline'))))) $label
        Remove-Item -LiteralPath $fx.Trace
    }
    Assert-Fx ((Get-FxTreeDigest $qualificationRoot) -ceq $before) 'qualification-root-unchanged'
    $fallback = $true
    foreach ($path in @('notes/unmapped.txt','scripts/lib/release-authority.ps1','Directory.Build.props')) {
        $blob = Git-Fx $fx.Seed @('rev-parse',"$($fx.A):client/src/App.tsx")
        [void](Git-Fx $fx.Seed @('read-tree',$fx.A)); [void](Git-Fx $fx.Seed @('update-index','--add','--cacheinfo',"100644,$blob,$path"))
        $tree = Git-Fx $fx.Seed @('write-tree'); $sha = Git-Fx $fx.Seed @('commit-tree',$tree,'-p',$fx.A,'-m','fallback')
        [void](Git-Fx $fx.Clone @('fetch',$fx.Seed,$sha))
        $r = New-Request $fx 'Shadow'; $r.CandidateSha=$sha; $r.CandidateId=(Test-ReleaseGateCandidateRef $fx.Ref $sha).CandidateId
        $b = Bind-Request $r; $sel=Get-Content $b.SelectionPath -Raw | ConvertFrom-Json
        if ($sel.proposal.mode -cne 'full' -or $sel.fallbackReasons.Count -lt 2 -or $sel.effectiveMode -cne 'Full') {$fallback=$false}
    }
    Assert-Fx $fallback 'selector-fallback-full'
}

function Write-BaselineArchive($Fx, $ArchiveId = 'full-b') {
    # F-V diagnostic archive, with real pinned Git bytes and synthetic historical
    # TRX. This proves the reader only; it is not a production supplier fixture.
    $root = Join-Path $Fx.Release ('archive/' + $ArchiveId); $dir=Join-Path $root 'authority'
    $contract = Get-ReleaseSelectionContract $Fx.Clone $Fx.B 'rc'
    $blob = Read-ReleaseAuthorityPinnedBlob $Fx.Clone $Fx.B
    $policy = ConvertFrom-ReleaseAuthorityPolicyBytes $blob.Bytes
    $authority = @{schemaVersion=1;kind='release-authority';repository=$Fx.Repository;profile='rc';sha=$Fx.B;candidateId='historical-b';candidateRef=$Fx.Ref
        intentId='historical-intent';releaseCardId='e83e4f3f-7b8c-45e1-8d93-5b337e343f53';requiredSuites=@($policy.RequiredSuites);exclusions=@($policy.Exclusions)
        policyHash=$policy.PolicyHash;policyBlobId=$blob.BlobId;policyRawSha256=(Get-ReleaseAuthorityBytesSha256 $blob.Bytes)}
    Write-NightlyAtomicJson (Join-Path $dir 'authority.json') $authority
    $discovery=@{};foreach($suite in $policy.RequiredSuites){if($suite -cnotin @('client','scripts')){$discovery[$suite]=@(@{uid=($suite+'-one');class='Fixture';method='One'})}}
    $plan=New-ReleaseGateExecutionPlan $root $discovery @{client=@('client-tests');scripts=@('script-tests')}
    $chunks=@(); foreach($suite in $plan.suites){foreach($chunk in $suite.chunks){
        $trx=Join-Path $root ('evidence/'+$chunk.id+'.trx');Write-C487Trx $trx @(@{Id=$chunk.uids[0];ClassName='Fixture';MethodName='One';Outcome='Passed'})
        $chunks+=@{suite=$suite.id;chunk=$chunk.id;exitCode=0;trx=('evidence/'+$chunk.id+'.trx');trxSha256=(Get-NightlyFileSha256 $trx)}
    }}
    $ledger=@{sha=$Fx.B;candidateId='historical-b';intentId='historical-intent';runId='baseline-run';selection='full';seamed=$false;diagnostic=$false;noReport=$false;teardownSucceeded=$true
        authorityDigest=(Get-NightlyFileSha256 (Join-Path $dir 'authority.json'));executionPlanDigest=(Get-NightlyFileSha256 (Join-Path $dir 'execution-plan.json'))
        prepublicationReceipt=@{kind='prepublication';correlation=@{sha=$Fx.B;runId='baseline-run'};recipient=@{cardId=$authority.releaseCardId;revision=4}}
        chunks=$chunks;rosters=@(@{suite='client';exitCode=0;entries=@(@{id='client-tests';outcome='passed'})},@{suite='scripts';exitCode=0;entries=@(@{id='script-tests';outcome='passed'})})}
    Write-NightlyAtomicJson (Join-Path $dir 'ledger.json') $ledger
    $green=@{profile='rc';trigger='rc';ref=$Fx.Ref;sha=$Fx.B;expectedSha=$Fx.B;candidateId='historical-b';runId='baseline-run';policyHash=$policy.PolicyHash;exitCode=0
        coverageComplete=$true;testsPassed=$true;reportDelivered=$true;coverageScope='Full'}
    Write-NightlyAtomicJson (Join-Path $root 'complete-green.json') $green
    $pub=New-ReleaseGatePublicationAuthority $root @{Authority=$authority;Digest=$ledger.authorityDigest} ('a'*64)
    $manifest=New-ReleaseGateManifest @{repository=$Fx.Repository;candidateId='historical-b';sha=$Fx.B;publicationAuthorityDigest=$pub.Digest}
    Write-NightlyAtomicJson (Join-Path $root 'release-manifest.json') $manifest
    Write-NightlyAtomicJson (Join-Path $root 'publish.json') @{steps=@('published');sha=$Fx.B;publicationAuthorityDigest=$pub.Digest;manifestDigest=(Get-ReleaseGateManifestDigest $manifest)
        releaseId='historical-release';releaseUrl='https://example.invalid/release'}
    $files=@{};foreach($name in @('authority/authority.json','authority/execution-plan.json','authority/discovery.json','authority/ledger.json','authority/publication-authority.json','release-manifest.json','publish.json','complete-green.json')) {
        $files[$name]=Get-NightlyFileSha256 (Join-Path $root $name)
    }
    $receipt=@{format='antiphon-full-release-baseline-v1';coverageScope='Full';profile='rc';repository=$Fx.Repository;sha=$Fx.B;platform=$contract.platform;contract=$contract
        candidateRoot=('archive/' + $ArchiveId);files=$files}
    $path=Join-Path $Fx.Release ('baselines/' + $ArchiveId + '.json');Write-NightlyAtomicJson $path $receipt
    return @{Path=$path;Receipt=$receipt;Root=$root}
}
function Expect-Refusal($Request, $Reason) {
    $before=@(Get-ChildItem (Join-Path $Request.ReleaseRoot 'candidates') -Filter selection-attempt.json -Recurse -ErrorAction SilentlyContinue).Count
    $message='';try{[void](New-ReleaseSelectionAttempt $Request)}catch{$message=$_.Exception.Message}
    $after=@(Get-ChildItem (Join-Path $Request.ReleaseRoot 'candidates') -Filter selection-attempt.json -Recurse -ErrorAction SilentlyContinue).Count
    return ($message.Contains($Reason) -and $before -eq $after)
}
function Test-C1313_BaselineAuthority {
    $fx=New-Fx; $request=New-Request $fx 'ChangeAware';$r=Bind-Request $request
    Assert-Fx ($r.Selection.effectiveMode -ceq 'Full' -and $r.Selection.fallbackReasons -ccontains 'baseline-absent') 'baseline-absent-full'
    $archive=Write-BaselineArchive $fx
    $request=New-Request $fx 'ChangeAware';$request.BaselineReceiptPath=$archive.Path;$r=Bind-Request $request
    $otherFx=@{};foreach($key in $fx.Keys){$otherFx[$key]=$fx[$key]};$otherFx.B=$fx.A
    $otherArchive=Write-BaselineArchive $otherFx 'full-a'
    $explicit=New-Request $fx;$auto=$null
    try{$auto=Bind-Request $explicit}catch{}
    Remove-Item -LiteralPath $otherArchive.Path
    $omitted=New-Request $fx;$omitted.BaselineSha='';$resolved=Bind-Request $omitted
    Assert-Fx ($r.BaselineSha -ceq $fx.B -and $r.Selection.baselineDigest -ceq (Get-NightlyFileSha256 $archive.Path) -and
        $auto -and $auto.BaselineSha -ceq $fx.B -and $resolved.BaselineSha -ceq $fx.B) 'baseline-full-bound'
    # Advance an unrelated current card snapshot and the origin tip: historical proof
    # uses the archived publication and its typed receipt, with no live HTTP.
    Write-Fx $fx.Root 'current-card.json' '{"revisionCount":99}'
    $tree=Git-Fx $fx.Seed @('rev-parse',"$($fx.A)^{tree}")
    $advanced=Git-Fx $fx.Seed @('commit-tree',$tree,'-p',$fx.A,'-m','advanced')
    [void](Git-Fx $fx.Seed @('push',$fx.Origin,($advanced+':refs/heads/'+$fx.Ref)))
    $request=New-Request $fx;$request.BaselineReceiptPath=$archive.Path;$r=$null
    try{$r=Bind-Request $request}catch{}
    Assert-Fx ($r -and $r.BaselineSha -ceq $fx.B -and $r.Selection.fallbackReasons -cnotcontains 'baseline-absent') 'historical-baseline-accepted'
    foreach($variant in @('selected','readiness','legacy','foreign','platform','digest','selector')) {
        $copy=($archive.Receipt|ConvertTo-Json -Depth 30)|ConvertFrom-Json -AsHashtable
        $reason='';switch($variant){
            selected {$copy.coverageScope='ChangeAware';$reason='baseline-full-only'}
            readiness {$copy.profile='nightly';$reason='baseline-full-only'}
            legacy {$copy.format='unqualified';$reason='baseline-unqualified'}
            foreign {$copy.repository='foreign/repo';$reason='baseline-repository'}
            platform {$copy.platform='other';$reason='baseline-platform'}
            digest {$copy.files['authority/ledger.json']='0'*64;$reason='baseline-digest'}
            selector {$copy.contract.selectorHash='0'*64;$reason='baseline-contract'}
        }
        Write-NightlyAtomicJson $archive.Path $copy
        $q=New-Request $fx;$q.BaselineReceiptPath=$archive.Path
        $label=switch($variant){selected{'baseline-full-only'}digest{'baseline-corrupt-refused'}selector{'contract-hash-bound'}default{'baseline-'+$variant+'-refused'}}
        Assert-Fx (Expect-Refusal $q $reason) $label
    }
    Write-NightlyAtomicJson $archive.Path $archive.Receipt
    $gitRefused=$true
    foreach($field in @('CandidateSha','BaselineSha')){$q=New-Request $fx;$q[$field]='0'*40;if(-not(Expect-Refusal $q 'selection-commit-unavailable')){$gitRefused=$false}}
    Assert-Fx $gitRefused 'git-object-refused'
    $other=Git-Fx $fx.Seed @('commit-tree',$tree,'-m','unrelated');[void](Git-Fx $fx.Clone @('fetch',$fx.Seed,$other))
    $q=New-Request $fx;$q.BaselineReceiptPath='';$q.BaselineSha=$other
    # Explicit absent receipt prevents auto-resolving a different claimed B.
    $q.BaselineReceiptPath=Join-Path $fx.Release 'absent.json'
    Assert-Fx (Expect-Refusal $q 'baseline-not-ancestor') 'baseline-ancestor-required'
    $incompatible=$true
    foreach($variant in @('policy','runtime')){
        [void](Git-Fx $fx.Seed @('read-tree',$fx.A))
        if($variant -ceq 'runtime'){$path='global.json';$text='{"sdk":{"version":"99.0.100"}}'}else{
            $path='tests/test-execution-policy.json';$p=Get-Content (Join-Path $fx.Clone $path) -Raw|ConvertFrom-Json
            $p.profiles.rc.description+=' different contract';$p.policyHash=Get-NightlyPolicyHash $p;$text=$p|ConvertTo-Json -Depth 30
        }
        Write-Fx $fx.Seed $path $text;[void](Git-Fx $fx.Seed @('add','--',$path));$t=Git-Fx $fx.Seed @('write-tree')
        $a=Git-Fx $fx.Seed @('commit-tree',$t,'-p',$fx.A,'-m',$variant);[void](Git-Fx $fx.Clone @('fetch',$fx.Seed,$a))
        $q=New-Request $fx 'ChangeAware';$q.CandidateSha=$a;$q.CandidateId=(Test-ReleaseGateCandidateRef $fx.Ref $a).CandidateId;$q.BaselineReceiptPath=$archive.Path
        $r=Bind-Request $q;if($r.Selection.effectiveMode -cne 'Full' -or $r.Selection.fallbackReasons -cnotcontains 'baseline-incompatible'){$incompatible=$false}
    }
    Assert-Fx $incompatible 'baseline-incompatible-full'
    $publish=Join-Path $archive.Root 'publish.json';$pub=Get-Content $publish -Raw|ConvertFrom-Json;$pub.steps=@('intent');Write-NightlyAtomicJson $publish $pub
    $archive.Receipt.files['publish.json']=Get-NightlyFileSha256 $publish;Write-NightlyAtomicJson $archive.Path $archive.Receipt
    $q=New-Request $fx;$q.BaselineReceiptPath=$archive.Path
    Assert-Fx (Expect-Refusal $q 'baseline-publication-required') 'baseline-publication-required'
}

function Test-C1313_ParameterHops {
    $fx=New-Fx;$q=New-Request $fx 'Shadow' 'hop-run'
    $q.BaselineReceiptPath=Join-Path $fx.Release 'absent receipt.json'
    $q.QualificationPath=Join-Path $fx.Release 'qualification/claim.json';Write-Fx $fx.Release 'qualification/claim.json' '{"status":"unqualified"}'
    $q.ReleaseIntentPath=Join-Path $fx.Release 'future intent.json';$q.TrainManifestPath=Join-Path $fx.Release 'future train.json'
    Write-Fx $fx.Release 'future train.json' '{"fixture":"opaque input only"}'
    $bound=Bind-Request $q
    $args=@{ReleaseRoot=$fx.Release;CoordinationRoot=(Join-Path $fx.Root 'coord');RemoteUrl=$fx.Origin;Repository=$fx.Repository;Ref=$fx.Ref
        RunId=$q.RunId;SelectionMode=$q.SelectionMode;BaselineSha=$q.BaselineSha;TimingRoot=$q.TimingRoot
        BaselineReceiptPath=$q.BaselineReceiptPath;QualificationPath=$q.QualificationPath;ReleaseIntentPath=$q.ReleaseIntentPath;TrainManifestPath=$q.TrainManifestPath;SeamsPath=$fx.Seams;SkipPublish=$true;PassThru=$true}
    $cut=& "$PSScriptRoot/release-cut.ps1" @args
    $summaryFiles=@(Get-ChildItem (Join-Path $fx.Release 'candidates') -Filter summary.json -Recurse)
    # Timing also writes summary.json. Only run records carry the consumed selection.
    $summaries=@($summaryFiles | Where-Object {
        $record=Get-Content $_.FullName -Raw|ConvertFrom-Json
        $null -ne $record.PSObject.Properties['runId']
    })
    $consumed=$null;if($summaries.Count -eq 1){$consumed=(Get-Content $summaries[0].FullName -Raw|ConvertFrom-Json).selection}
    $bindingEvidence=Join-Path $ResultsDirectory 'hop-bindings'
    [void][IO.Directory]::CreateDirectory($bindingEvidence)
    Write-NightlyAtomicJson (Join-Path $bindingEvidence 'bound.json') $bound.Selection
    $summaryInventory=@();$index=0
    foreach($file in $summaryFiles){
        $document=Get-Content $file.FullName -Raw|ConvertFrom-Json
        Copy-Item $file.FullName (Join-Path $bindingEvidence ("summary-$index.json"))
        $summaryInventory+=@{path=$file.FullName;runId=$document.runId;hasSelection=($null -ne $document.selection)};$index++
        if($document.selection){
            foreach($field in @('baselineSha','candidateSha','runId','timingRoot','baselineReceiptPath','qualificationPath','releaseIntentPath','trainManifestPath','trainDigest','selectionDigest')){
                Write-Host ("C1313 hop-binding {0}={1} actual={2} expected={3}" -f $field,($document.selection.$field -ceq $bound.Selection.$field),$document.selection.$field,$bound.Selection.$field)
            }
        }
    }
    Write-NightlyAtomicJson (Join-Path $bindingEvidence 'inventory.json') $summaryInventory
    Write-Host ("C1313 hop-summary-count={0}" -f $summaries.Count)
    Assert-Fx ($consumed -and $consumed.baselineSha -ceq $fx.B -and $consumed.candidateSha -ceq $fx.A -and
        $consumed.runId -ceq $q.RunId -and $consumed.timingRoot -ceq $q.TimingRoot -and
        $consumed.baselineReceiptPath -ceq $q.BaselineReceiptPath -and $consumed.qualificationPath -ceq $q.QualificationPath -and
        $consumed.releaseIntentPath -ceq $q.ReleaseIntentPath -and $consumed.trainManifestPath -ceq $q.TrainManifestPath -and
        $consumed.trainDigest -ceq (Get-NightlyFileSha256 $q.TrainManifestPath) -and $consumed.selectionDigest -ceq $bound.SelectionDigest) 'hop-baseline-bound' ($cut|ConvertTo-Json -Depth 10 -Compress)
    if(-not $consumed){return}
    $discovery=Get-FxDiscovery
    Assert-Fx (Invoke-FxRoster $consumed $discovery 'hop-native') 'hop-executed-full'
    $same=New-ReleaseSelectionAttempt $q
    Assert-Fx ($same.SelectionAttemptDigest -ceq $bound.SelectionAttemptDigest) 'attempt-identical-resume'
    $original=Get-NightlyFileSha256 $bound.SelectionAttemptPath
    $changed=@{};foreach($key in $q.Keys){$changed[$key]=$q[$key]};$changed.SelectionMode='ChangeAware'
    $modeConflict=Expect-Refusal $changed 'selection-attempt-conflict'
    $trainText=[IO.File]::ReadAllText($q.TrainManifestPath);[IO.File]::AppendAllText($q.TrainManifestPath,' ')
    $trainConflict=Expect-Refusal $q 'selection-attempt-conflict'
    Remove-Item $fx.Trace -ErrorAction SilentlyContinue
    $trainHop=Invoke-Workload $fx $q
    $trainRefused=$trainHop.Refusal -ceq 'selection-hop-trainDigest' -and -not(Test-Path $fx.Trace)
    [IO.File]::WriteAllText($q.TrainManifestPath,$trainText)
    Assert-Fx ($modeConflict -and $trainConflict -and $trainRefused -and (Get-NightlyFileSha256 $bound.SelectionAttemptPath) -ceq $original) 'attempt-conflict-refused'
    $retry=New-Request $fx 'Shadow' 'retry-run';$retry.Attempt=2;$next=$null
    try{$next=Bind-Request $retry}catch{}
    Assert-Fx ($next -and $next.SelectionAttemptPath -cne $bound.SelectionAttemptPath -and (Get-NightlyFileSha256 $bound.SelectionAttemptPath) -ceq $original) 'prior-attempt-kept'
    $saved=[IO.File]::ReadAllText($bound.SelectionPath);[IO.File]::AppendAllText($bound.SelectionPath,' ')
    Remove-Item $fx.Trace -ErrorAction SilentlyContinue
    $r=Invoke-Workload $fx $q
    Assert-Fx ($r.Refusal -ceq 'selection-hop-digest' -and -not(Test-Path $fx.Trace)) 'hop-digest-refused'
    [IO.File]::WriteAllText($bound.SelectionPath,$saved)
    $foreign=@{};foreach($key in $q.Keys){$foreign[$key]=$q[$key]};$foreign.CandidateSha=$fx.B
    $r=Invoke-Workload $fx $foreign
    Assert-Fx ($r.Refusal -ceq 'selection-hop-candidateSha' -and -not(Test-Path $fx.Trace)) 'hop-sha-refused'
    $path=$env:PATH
    try{
        $env:PATH=Join-Path $fx.Root 'no executables'
        $refused=$true;$capabilityResults=@()
        foreach($mode in @('Shadow','ChangeAware')){
            $r=& "$PSScriptRoot/release-cut.ps1" -ReleaseRoot $fx.Release -Ref $fx.Ref -SelectionMode $mode -PassThru
            $capabilityResults+=@{mode=$mode;refusal=$r.refusal;exitCode=$r.exitCode;phase=$r.phase}
            if($r.refusal -cne 'selection-pwsh-required'){$refused=$false}
        }
        Assert-Fx $refused 'pwsh-required-before-work' (@{runtime=$PSVersionTable.PSVersion.ToString();edition=$PSVersionTable.PSEdition;OS=$env:OS;results=$capabilityResults}|ConvertTo-Json -Depth 6 -Compress)
        $full=New-Request $fx 'Full' 'without-pwsh';$full.BaselineSha='';$r=New-ReleaseSelectionAttempt $full
        Assert-Fx ($r.Selection.effectiveMode -ceq 'Full') 'full-without-pwsh'
    }finally{$env:PATH=$path}
    Write-Fx $fx.Clone 'bin-fixture/output.txt' 'disposable'
    [void](Git-Fx $fx.Clone @('clean','-fdx'))
    Assert-Fx ((Get-NightlyFileSha256 $bound.SelectionAttemptPath) -ceq $original -and -not(Test-Path "$($fx.Clone)/bin-fixture")) 'attempt-survives-cleanup'
}

try { & "Test-$Case" }
finally {
    # Retain actual native TRX/diagnostics independently of disposable fixture cleanup.
    $retained=Join-Path $repoRoot ('.antiphon/test-output/c1313-s6a/'+[guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($retained)
    foreach($item in Get-ChildItem $ResultsDirectory -Directory | Where-Object {$_.Name -match '^(native-|hop-native|hop-bindings|discovery)'}){Copy-Item $item.FullName $retained -Recurse}
    foreach($root in $script:FixtureRoots){Get-ChildItem $root -Recurse -Force -File|Where-Object {$_.IsReadOnly}|ForEach-Object {$_.IsReadOnly=$false}}
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 0
