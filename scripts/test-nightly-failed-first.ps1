#requires -Version 5.1
# CARD-1514: hermetic evidence export/resolution through the production planner.
param([string]$Case = '', [string]$ResultsDirectory = '')
$ErrorActionPreference = 'Stop'
foreach ($name in 'nightly-common','nightly-policy','nightly-coverage','nightly-tests-impl','nightly-failure-history','c487-harness') {
    . (Join-Path $PSScriptRoot ('lib/' + $name + '.ps1'))
}
$repo = Split-Path -Parent $PSScriptRoot
$root = New-C487Root $ResultsDirectory
$clock = [Diagnostics.Stopwatch]::StartNew()
# This conservation fixture compares observations under an unexhausted reader
# clock. Production retains its 200 ms deadline; size/regular-file checks stay real.
if ($Case -cin @('FullBytesAndCoverageConserved','ConservationDiagnostics')) {
    $script:TestPhaseTrxReadWatch = [Diagnostics.Stopwatch]::new()
}
$phases = [ordered]@{}
$script:FFOperations=[Collections.Generic.List[object]]::new()
$script:FFAssertions=[Collections.Generic.List[object]]::new()
$script:FFLastAssertionAt=0.0
function Write-FFJson($Path, $Value) {
    $watch=[Diagnostics.Stopwatch]::StartNew()
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 40), [Text.UTF8Encoding]::new($false))
    $script:FFOperations.Add([ordered]@{kind='IO';operation='write-json';artifact=[IO.Path]::GetFileName($Path);seconds=$watch.Elapsed.TotalSeconds})
}
function Assert-FF([bool]$Ok, [string]$Name, [string]$Detail = '') {
    $now=$clock.Elapsed.TotalSeconds
    $script:FFAssertions.Add([ordered]@{name=$Name;passed=$Ok;elapsedSeconds=$now;secondsSincePreviousAssertion=($now-$script:FFLastAssertionAt)})
    $script:FFLastAssertionAt=$now
    Assert-C487 $Ok ('FF ' + $Case + '.' + $Name) $Detail
}
function New-FFNode([string]$Class, [string]$Method = 'Run', [string]$Uid = '') {
    $bare = Get-NightlyBareClassIdentity $Class
    $dot = $bare.LastIndexOf('.')
    if (-not $Uid) { $Uid = $Class + '.' + $Method }
    [pscustomobject]@{ ClassName = $Class; Namespace = $bare.Substring(0,$dot); Type = $Class.Substring($dot+1); Method = $Method; Uid = $Uid; Categories = @(); Excluded = $false }
}
function New-FFReplay {
    $phase = [Diagnostics.Stopwatch]::StartNew()
    $policy = Read-NightlyExecutionPolicy -RepoRoot $repo
    $listing = Join-Path $PSScriptRoot 'fixtures/nightly/c1410-antiphon-listing.txt'
    $nodes = [Collections.Generic.List[object]]::new()
    foreach ($line in [IO.File]::ReadAllLines($listing)) {
        if (-not $line -or $line.StartsWith('#')) { continue }
        $fields = $line.Split('|'); $i = 0
        foreach ($entry in $fields[2].Split(',')) {
            $eq = $entry.IndexOf('=')
            if ($eq -ge 0) { $method = $entry.Substring(0,$eq); $count = [int]$entry.Substring($eq+1) }
            else { $method = 'M' + $i.ToString('0000'); $count = [int]$entry }
            $i++
            for ($k = 0; $k -lt $count; $k++) {
                $n = New-FFNode $fields[0] $method ($fields[0] + '.' + $method + '.' + $k)
                if ($fields[1] -ceq 'S') { $n.Categories = @('Slow') }
                $nodes.Add($n)
            }
        }
    }
    $phases.fixtureSeconds = $phase.Elapsed.TotalSeconds
    $phase.Restart()
    $ex = @(Get-NightlyProfileExclusions -PolicyObject $policy.Object -Profile rc -SuiteId antiphon -Platform windows)
    $chunks = @(Resolve-NightlySuiteChunks -DiscoveryNodes $nodes.ToArray() -Chunks @([pscustomobject]@{ id = 'all'; classes = @() }) -Exclusions $ex -ProfileAware $true -Native $true -SuiteId antiphon -PolicyObject $policy.Object)
    $phases.plannerSeconds = $phase.Elapsed.TotalSeconds
    Write-FFJson (Join-Path $root 'replay.json') ([ordered]@{ nodes=$nodes.Count; chunks=$chunks.Count; listingDigest=(Get-FileHash -LiteralPath $listing).Hash; costDigest=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'lib/nightly-class-costs.json')).Hash; policyHash=$policy.Hash;phases=$phases })
    [pscustomobject]@{ id='antiphon'; nodes=$nodes.ToArray(); chunks=$chunks; exclusions=$ex }
}
function New-FFHistoricalFixture {
    $run = Join-Path $root ('retained-' + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($run)
    $names = @(
        'Application.TerminalRunnerSeatReleaseTests','Application.SessionRuntimeActionTests','Application.AttentionFeedNoiseTests',
        'Application.AgentReportStoreTests','Application.CardFileGitFailureAcceptanceTests','Application.GrokSignInRuntimeTests',
        'Application.CardFileIgnoreAcceptanceTests','Application.StandingRestartAccountingTests','Application.GrokRulesResumeMigrationTests',
        'Application.LandNonCodeSkipTests','Checkpoints.CheckpointRepeatBuildIsolationTests',
        'Infrastructure.LandingGitTests','Infrastructure.RepositoryChildRecoveryTests','Infrastructure.WorktreeManagerTests',
        'Scripts.TestSelectionTests','Scripts.ChangeAwareReleaseTests','Scripts.EvidenceDiffGuardTests','Scripts.RunCheckpointRepeatScriptTests',
        'Scripts.C1008PlatformContractTests','Scripts.WindowsUnitPlatformAdmissionTests','Scripts.CardRelationshipScriptTests',
        'Scripts.NightlyDeadOwnerLockTests','Scripts.ScriptsLaneRepairTests','Scripts.RemoteScriptContractTests','TestHelpers.TestPhaseSummaryTests')
    $classes = @($names | ForEach-Object { 'Antiphon.Tests.' + $_ }) + @('Antiphon.SessionRunner.Tests.GrokRulesHttpAcceptanceTests','Antiphon.Agents.Pty.Tests.FakeClaudeContractTests','Antiphon.E2E.BoardE2ETests')
    $suites = @(); $nodesBySuite = @{}; $defsBySuite = @{}; $rowsBySuite = @{}
    for ($i = 0; $i -lt 36; $i++) {
        $c = $classes[$i % 28]; $suite = 'antiphon'
        if ($c.StartsWith('Antiphon.SessionRunner.')) { $suite = 'session-runner' }
        if ($c.StartsWith('Antiphon.Agents.Pty.')) { $suite = 'agents-pty' }
        if ($c.StartsWith('Antiphon.E2E.')) { $suite = 'e2e' }
        if (-not $nodesBySuite.ContainsKey($suite)) { $nodesBySuite[$suite]=@(); $defsBySuite[$suite]=''; $rowsBySuite[$suite]='' }
        $uid = 'trx-' + $i; $m = 'Failure' + $i
        $nodesBySuite[$suite] += New-FFNode $c $m $uid
        $defsBySuite[$suite] += '<UnitTest id="' + $uid + '"><TestMethod className="' + $c + '" name="' + $m + '" /></UnitTest>'
        $rowsBySuite[$suite] += '<UnitTestResult testId="' + $uid + '" outcome="Failed" />'
    }
    foreach ($suite in @('antiphon','session-runner','agents-pty','e2e')) {
        $name = $suite + '-failed.trx'
        [IO.File]::WriteAllText((Join-Path $run $name), ('<TestRun><TestDefinitions>'+$defsBySuite[$suite]+'</TestDefinitions><Results>'+$rowsBySuite[$suite]+'</Results></TestRun>'))
        $suites += [ordered]@{ id=$suite; chunk='failed'; trx=$name; exitCode=1; result='FAIL'; timedOut=$false }
    }
    $extra = @('CheckpointOrFilterTests','CheckpointLaunchCleanupTests','CheckpointLaunchCleanupTests','CheckpointLaunchCleanupTests','CheckpointAppTests','CheckpointAdmissionContractTests','CheckpointSlotExecutorTests','CheckpointExecutorLogTests')
    $diag = "    version: 2.2.2`nTest framework UID: 'TUnitExtension' Version: '1.44.0.0'`n"
    $recoveredDiag=$diag
    for ($i=0; $i -lt 10; $i++) {
        $type = if ($i -lt 8) { $extra[$i] } else { 'RecoveredTests' }
        $state = if ($i -lt 7) { 'Error' } elseif ($i -lt 9) { 'InProgress' } else { 'Passed' }
        $uid = if ($i -ge 8) { 'recovered' } else { 'diag-'+$i }
        $method = if ($i -eq 7) { 'concurrent_callbacks_append_each_line_once_without_overlap' } else { 'Case'+$i }
        if ($i -eq 9) { $method='Case8' }
        $record = 'TestNodeUpdateMessage { TestNode = TestNode { Uid = TestNodeUid { Value = '+$uid+' }, DisplayName = fixture, Properties = [ TestMethodIdentifierProperty { AssemblyFullName = Fixture, Namespace = Antiphon.Tests.Checkpoints, TypeName = '+$type+', MethodName = '+$method+', MethodArity = 0, ParameterTypeFullNames = [], ReturnTypeFullName = System.Void }, '+$state+'TestNodeStateProperty { Explanation =  } ] } }' + "`n"
        if ($i -lt 8) { $diag += $record } else { $recoveredDiag += $record }
        if ($i -lt 9) { $nodesBySuite.antiphon += New-FFNode ('Antiphon.Tests.Checkpoints.'+$type) $method $uid }
    }
    [void][IO.Directory]::CreateDirectory((Join-Path $run 'antiphon-all-group-105-execution'))
    [IO.File]::WriteAllText((Join-Path $run 'antiphon-all-group-105-execution/invocation.diag'),$diag)
    [IO.File]::WriteAllText((Join-Path $run 'recovered.diag'),$recoveredDiag)
    [IO.File]::WriteAllText((Join-Path $run 'recovered.trx'),'<TestRun><TestDefinitions><UnitTest id="recovered"><TestMethod className="Antiphon.Tests.Checkpoints.RecoveredTests" name="Case8" /></UnitTest></TestDefinitions><Results><UnitTestResult testId="recovered" outcome="Passed" /></Results></TestRun>')
    $suites += [ordered]@{id='antiphon';chunk='recovered';trx='recovered.trx';execution='recovered.diag';timedOut=$false;exitCode=0}
    $suites += [ordered]@{ id='antiphon'; chunk='all-group-105'; trx='antiphon-all-group-105.trx'; execution='antiphon-all-group-105-execution/invocation.diag'; timedOut=$true; exitCode=1; result='FAIL' }
    $summary = [ordered]@{ runId='retained-run'; attempt='1'; invocationId='retained-run'; completedAt='2026-10-29T00:00:00Z'; sha=('a'*40); profile='rc'; platform='windows'; policyHash=('b'*64); selectedSuites=@('antiphon','session-runner','agents-pty','e2e'); suites=$suites; timingRoot='timing' }
    Write-FFJson (Join-Path $run 'summary.json') $summary
    [void][IO.Directory]::CreateDirectory((Join-Path $run 'timing'))
    $fallback = @($nodesBySuite.antiphon | Where-Object { $_.Uid.StartsWith('diag-') } | ForEach-Object { Get-NightlyBareClassIdentity $_.ClassName } | Select-Object -Unique) + @('Antiphon.Tests.Checkpoints.UnobservedCoPackedTests')
    $nodesBySuite.antiphon += New-FFNode 'Antiphon.Tests.Checkpoints.UnobservedCoPackedTests'
    Write-FFJson (Join-Path $run 'timing/groups-planned-antiphon.json') ([ordered]@{ version=1; suite='antiphon'; runId='retained-run'; attempt='1'; sourceDigest=('a'*40); policyDigest=('b'*64); assemblyDigest='fixture-assembly'; groups=@([ordered]@{ chunk='all-group-105'; classes=$fallback; plannedTestIds=@($nodesBySuite.antiphon | Where-Object { $_.Uid.StartsWith('diag-') -or $_.ClassName.EndsWith('UnobservedCoPackedTests') } | ForEach-Object { $_.Uid }) }) })
    # The singleton is intentionally another suite. It must never define antiphon membership.
    Write-FFJson (Join-Path $run 'timing/groups-planned.json') ([ordered]@{ version=1; suite='observability'; runId='retained-run'; attempt='1'; groups=@([ordered]@{ chunk='all-group-105'; classes=@('Foreign.WrongSuiteTests'); plannedTestIds=@('wrong') }) })
    foreach ($suite in $nodesBySuite.Keys) {
        Write-FFJson (Join-Path $run ($suite+'-profile.discovery.json')) (ConvertTo-NightlyDiscoveryDocument -Nodes $nodesBySuite[$suite] -AssemblyHash 'fixture-assembly')
    }
    [pscustomobject]@{ Run=$run; Summary=$summary; Nodes=$nodesBySuite; Classes=$classes; Fallback=$fallback }
}
function Test-EvidenceUnion {
    $replay=New-FFReplay
    $phase=[Diagnostics.Stopwatch]::StartNew()
    $fx=New-FFHistoricalFixture
    $path=Join-Path $root 'history.json'
    $export=Export-NightlyPriorFailures -RunDirectory $fx.Run -OutputPath $path -PassThru
    $history=Read-NightlyPriorFailures -Path $path -ExpectedDigest $export.Digest
    $obs=@($history.members | ForEach-Object { $_.observations })
    Assert-FF (@($obs | Where-Object { $_.kind -ceq 'trx' }).Count -eq 36) 'trx36'
    Assert-FF (@($obs | Where-Object { $_.state -ceq 'Error' }).Count -eq 7) 'diag7'
    Assert-FF (@($obs | Where-Object { $_.state -ceq 'InProgress' -and $_.uid -ceq 'diag-7' }).Count -eq 1) 'hang1'
    $observedClasses=@($history.members | Where-Object { @($_.observations).Count -gt 0 })
    Assert-FF ($observedClasses.Count -eq 34) 'classes34' ([string]$observedClasses.Count)
    Assert-FF (@($history.members | Where-Object { $_.className -ceq 'Antiphon.Tests.Checkpoints.UnobservedCoPackedTests' }).Count -eq 1 -and $history.membershipComplete -eq $true -and @($history.fallbackChunks).Count -eq 1) 'fallback'
    Assert-FF (@($replay.nodes).Count -eq 19012 -and @($replay.chunks).Count -eq 150) 'replay'
    $suites=@($replay)
    foreach ($suite in @('antiphon','session-runner','agents-pty','e2e')) {
        # Supplemental fixtures are separate from the unchanged 19,012-node anchor.
        $nodes=@($fx.Nodes[$suite]); $ex=@()
        $plan=@(Get-NightlyProfileLaunchPlan -DiscoveryNodes $nodes -Chunks @([pscustomobject]@{id='supplemental';classes=@()}) -Exclusions $ex -SuiteId $suite)
        if ($suite -ceq 'antiphon') {
            $suites[0]=[pscustomobject]@{id=$suite;nodes=@($replay.nodes)+$nodes;chunks=@($replay.chunks)+$plan;exclusions=@($replay.exclusions)}
        } else { $suites += [pscustomobject]@{id=$suite;nodes=$nodes;chunks=$plan;exclusions=$ex} }
    }
    $resolved=Resolve-NightlyPriorFailures -History $history -Suites $suites -QualificationMode Full
    $all=$true
    foreach ($m in $history.members) {
        $s=@($suites | Where-Object { $_.id -ceq $m.suite })[0]
        $expected=@($s.chunks | Where-Object { @($_.classes) -ccontains $m.className } | ForEach-Object { $_.id })
        $actual=@($resolved.members | Where-Object { $_.suite -ceq $m.suite -and $_.className -ceq $m.className })
        if ($actual.Count -ne 1 -or (@($actual[0].chunks) -join ',') -cne ($expected -join ',') -or $expected.Count -eq 0) { $all=$false }
    }
    Assert-FF ($all -and @($resolved.unresolved).Count -eq 0) 'allSplits'
    Assert-FF (@($history.members | Where-Object { $_.className -clike '*RecoveredTests' }).Count -eq 0) 'transitions'
    Assert-FF (@($obs | Where-Object { $_.state -ceq 'InProgress' -and $_.chunk -ceq 'all-group-105' -and $_.artifact -ceq 'antiphon-all-group-105-execution/invocation.diag' }).Count -eq 1) 'exactObservations'
    $before=[IO.File]::ReadAllBytes($path)
    $refused=$false; try { Export-NightlyPriorFailures -RunDirectory $fx.Run -OutputPath $path } catch { $refused=$true }
    Assert-FF ($refused -and [Convert]::ToBase64String($before) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($path))) 'immutable'
    $phases.exportResolveSeconds=$phase.Elapsed.TotalSeconds
}
function Copy-FFObject($Value) { return ($Value | ConvertTo-Json -Depth 40 | ConvertFrom-Json) }
function Test-FFRefusal([scriptblock]$Action, [string]$Reason) {
    $watch=[Diagnostics.Stopwatch]::StartNew()
    try { & $Action | Out-Null; return $false } catch { return ($_.Exception.Message -clike ('*'+$Reason+'*')) }
    finally { $script:FFOperations.Add([ordered]@{kind='body';operation='refusal';reason=$Reason;seconds=$watch.Elapsed.TotalSeconds}) }
}
function Test-FFChunkAccounting([string]$Variant) {
    $run=Join-Path $root ('accounting-'+$Variant)
    [void][IO.Directory]::CreateDirectory((Join-Path $run 'timing'))
    $nodes=@((New-FFNode 'Antiphon.Tests.Accounting.FirstTests' 'Run' 'first'),(New-FFNode 'Antiphon.Tests.Accounting.SecondTests' 'Run' 'second'))
    # Real writer shape: no coverageComplete, trx or execution properties.
    $row=[ordered]@{id='antiphon';chunk='all';name='antiphon all';log='C:\retired\logs\accounting\antiphon-all.log';exitCode=0;timedOut=$false;skipped=$false;result='pass'}
    switch ($Variant) {
        'exit' { $row.exitCode=-1073741819 }
        'crash' { $row.exitCode=-1073741819; $row.result='FAIL'; $row.detail='missing-terminal-accounting' }
        'crash-unbound' { $row.exitCode=-1073741819; $row.result='FAIL'; $row.detail='missing-terminal-accounting' }
        'result' { $row.result='FAIL' }
        'detail' { $row.detail='missing-terminal-accounting' }
    }
    $summary=[ordered]@{runId='accounting';attempt='1';completedAt='2026-10-29T00:00:00Z';sha=('a'*40);profile='rc';platform='windows';policyHash=('b'*64);suites=@($row);timingRoot='timing'}
    Write-FFJson (Join-Path $run 'summary.json') $summary
    Write-FFJson (Join-Path $run 'antiphon-profile.discovery.json') (ConvertTo-NightlyDiscoveryDocument -Nodes $nodes -AssemblyHash 'fixture-assembly')
    if ($Variant -cnotin @('missing','crash-unbound')) { Write-FFJson (Join-Path $run 'timing/groups-planned-antiphon.json') ([ordered]@{version=1;suite='antiphon';runId='accounting';attempt='1';sourceDigest=('a'*40);policyDigest=('b'*64);assemblyDigest='fixture-assembly';groups=@([ordered]@{chunk='all';classes=@($nodes.ClassName);plannedTestIds=@($nodes.Uid)})}) }
    $defs=''; $results=''
    foreach ($n in $nodes) {
        if ($Variant -ceq 'short' -and $n.Uid -ceq 'second') { continue }
        $defs += '<UnitTest id="'+$n.Uid+'"><TestMethod className="'+$n.ClassName+'" name="Run" /></UnitTest>'
        $results += '<UnitTestResult testId="'+$n.Uid+'" outcome="Passed" />'
    }
    if ($Variant -cnotin @('missing','missing-bound')) { [IO.File]::WriteAllText((Join-Path $run 'antiphon-all.trx'),('<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><TestDefinitions>'+$defs+'</TestDefinitions><Results>'+$results+'</Results></TestRun>')) }
    $exp=Export-NightlyPriorFailures $run (Join-Path $root ('accounting-'+$Variant+'.json')) -PassThru
    $history=Read-NightlyPriorFailures $exp.Path $exp.Digest
    $suite=@([pscustomobject]@{id='antiphon';nodes=$nodes;chunks=@([pscustomobject]@{id='all';classes=@($nodes.ClassName);nodes=$nodes});exclusions=@()})
    $full=Resolve-NightlyPriorFailures $history $suite Full
    if ($Variant -ceq 'missing-bound') {
        $partial=Resolve-NightlyPriorFailures $history $suite PriorFailureSweep
        return ($history.membershipComplete -and @($full.members).Count -eq 2 -and @($full.unresolved).Count -eq 0 -and @($partial.members).Count -eq 2 -and @($history.fallbackChunks).Count -eq 1)
    }
    $reason=@{crash='summary-detail';'crash-unbound'='summary-detail';exit='summary-nonzero-exit';result='summary-failed-result';detail='summary-detail';short='short-trx';missing='missing-trx'}[$Variant]
    $wholeChunk=($Variant -cin @('missing','crash-unbound') -or (@($full.members).Count -eq 2 -and @($full.unresolved | Where-Object { $_.className -cin @($nodes.ClassName) }).Count -eq 2))
    return ($wholeChunk -and @($full.unresolved).Count -gt 0 -and -not $history.membershipComplete -and @($history.fallbackChunks | Where-Object { $_.reason.Split(',') -ccontains $reason }).Count -eq 1 -and (Test-FFRefusal { Resolve-NightlyPriorFailures $history $suite PriorFailureSweep } 'prior-failure-sweep-unresolved'))
}
function Test-OrdinaryFailedChunk {
    foreach ($retainedPlan in @($true,$false)) {
        $variant=if ($retainedPlan) { 'bound' } else { 'unbound' }
        $run=Join-Path $root ('ordinary-'+$variant)
        [void][IO.Directory]::CreateDirectory((Join-Path $run 'timing'))
        $nodes=@((New-FFNode 'Antiphon.Tests.Accounting.FailedTests' 'Run' 'failed'),(New-FFNode 'Antiphon.Tests.Accounting.PassedTests' 'Run' 'passed'))
        $row=[ordered]@{id='antiphon';chunk='all';exitCode=2;result='FAIL';timedOut=$false}
        Write-FFJson (Join-Path $run 'summary.json') ([ordered]@{runId='ordinary';attempt='1';completedAt='2026-10-29T00:00:00Z';sha=('a'*40);profile='rc';platform='windows';policyHash=('b'*64);suites=@($row);timingRoot='timing'})
        Write-FFJson (Join-Path $run 'antiphon-profile.discovery.json') (ConvertTo-NightlyDiscoveryDocument -Nodes $nodes -AssemblyHash 'fixture-assembly')
        if ($retainedPlan) { Write-FFJson (Join-Path $run 'timing/groups-planned-antiphon.json') ([ordered]@{version=1;suite='antiphon';runId='ordinary';attempt='1';sourceDigest=('a'*40);policyDigest=('b'*64);assemblyDigest='fixture-assembly';groups=@([ordered]@{chunk='all';classes=@($nodes.ClassName);plannedTestIds=@($nodes.Uid)})}) }
        $defs=''; $results=''
        foreach ($n in $nodes) {
            $defs += '<UnitTest id="'+$n.Uid+'"><TestMethod className="'+$n.ClassName+'" name="Run" /></UnitTest>'
            $outcome=if ($n.Uid -ceq 'failed') { 'Failed' } else { 'Passed' }
            $results += '<UnitTestResult testId="'+$n.Uid+'" outcome="'+$outcome+'" />'
        }
        [IO.File]::WriteAllText((Join-Path $run 'antiphon-all.trx'),('<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><TestDefinitions>'+$defs+'</TestDefinitions><Results>'+$results+'</Results></TestRun>'))
        $exp=Export-NightlyPriorFailures $run (Join-Path $root ('ordinary-'+$variant+'.json')) -PassThru
        $history=Read-NightlyPriorFailures $exp.Path $exp.Digest
        $suite=@([pscustomobject]@{id='antiphon';nodes=$nodes;chunks=@([pscustomobject]@{id='all';classes=@($nodes.ClassName);nodes=$nodes});exclusions=@()})
        $full=Resolve-NightlyPriorFailures $history $suite Full
        Assert-FF ($history.membershipComplete -and @($full.unresolved).Count -eq 0 -and @($full.members).Count -eq 1 -and $full.members[0].className -ceq $nodes[0].ClassName -and @($history.fallbackChunks).Count -eq 0) ('ordinary-'+$variant+'-full')
        $accepted=$false; $detail=''
        try {
            $partial=Resolve-NightlyPriorFailures $history $suite PriorFailureSweep
            $accepted=(@($partial.members).Count -eq 1 -and $partial.members[0].className -ceq $nodes[0].ClassName -and @($partial.members[0].requiredUids).Count -eq 1 -and $partial.members[0].requiredUids[0] -ceq 'failed' -and @($partial.unresolved).Count -eq 0)
        } catch { $detail=$_.Exception.Message }
        Assert-FF $accepted ('ordinary-'+$variant+'-partial') $detail
    }
}
function Test-RefusesIncompleteEvidence {
    Test-OrdinaryFailedChunk
    $replay=New-FFReplay
    $fx=New-FFHistoricalFixture
    $path=Join-Path $root 'valid-history.json'
    $exp=Export-NightlyPriorFailures $fx.Run $path -PassThru
    $h=Read-NightlyPriorFailures $path $exp.Digest
    $current=@()
    foreach ($suite in @('antiphon','session-runner','agents-pty','e2e')) {
        $nodes=@($fx.Nodes[$suite])
        $plan=@(Get-NightlyProfileLaunchPlan -DiscoveryNodes $nodes -Chunks @([pscustomobject]@{id='all';classes=@()}) -Exclusions @() -SuiteId $suite)
        $current += [pscustomobject]@{id=$suite; nodes=$nodes; chunks=$plan; exclusions=@()}
    }
    $control=Resolve-NightlyPriorFailures $h $current PriorFailureSweep -WarningVariable missingContextWarnings
    Assert-FF (@($missingContextWarnings | Where-Object { [string]$_ -clike 'PARTIAL MISSING EXCLUSION CONTEXT:*' }).Count -eq 1) 'missingContextWarning'
    $candidateSha=git -C $repo rev-parse HEAD
    $candidatePolicy=Read-NightlyExecutionPolicy -RepoRoot $repo
    Assert-FF (@($control.members).Count -ge 34 -and @($control.unresolved).Count -eq 0 -and $h.sourceSha -cne $candidateSha -and $h.policyHash -cne $candidatePolicy.Hash) 'validControl'
    foreach ($variant in 'exit','result','detail','short','crash','crash-unbound','missing-bound') { Assert-FF (Test-FFChunkAccounting $variant) ('chunk-'+$variant) }
    # No timeout, diagnostic, failing summary marker or retained plan can mask this witness.
    Assert-FF (Test-FFChunkAccounting 'missing') 'standaloneMissingTrx'
    $retainedPlan=[IO.File]::ReadAllText((Join-Path $fx.Run 'timing/groups-planned-antiphon.json'))
    Remove-Item -LiteralPath (Join-Path $fx.Run 'timing/groups-planned-antiphon.json')
    $wrong=Export-NightlyPriorFailures $fx.Run (Join-Path $root 'wrong-suite.json') -PassThru
    $full=Resolve-NightlyPriorFailures $wrong.History $current Full
    $un=@($full.unresolved | Where-Object { $_.suite -ceq 'antiphon' -and $_.chunk -ceq 'all-group-105' -and $_.reason -ceq 'historical-membership-unavailable' })
    Assert-FF (-not $wrong.History.membershipComplete -and $un.Count -eq 1 -and @($wrong.History.members | Where-Object { $_.className -ceq 'Foreign.WrongSuiteTests' }).Count -eq 0 -and @($wrong.History.members).Count -eq 34) 'wrongSuiteFull'
    Assert-FF (Test-FFRefusal { Resolve-NightlyPriorFailures $wrong.History $current PriorFailureSweep } 'historical-membership-unavailable') 'wrongSuitePartial'
    [IO.File]::WriteAllText((Join-Path $fx.Run 'timing/groups-planned-antiphon.json'),$retainedPlan)
    $name='Antiphon.Tests.Application.TerminalRunnerSeatReleaseTests'
    $missing=Copy-FFObject $current
    $missing[0].nodes=@($missing[0].nodes | Where-Object { $_.ClassName -cne $name })
    $r=Resolve-NightlyPriorFailures $h $missing Full
    Assert-FF (@($r.unresolved | Where-Object { $_.className -ceq $name -and $_.reason -ceq 'current-class-missing' }).Count -eq 1) 'missingFull'
    Assert-FF (Test-FFRefusal { Resolve-NightlyPriorFailures $h $missing PriorFailureSweep } 'current-class-missing') 'missingPartial'
    $renamed=Copy-FFObject $missing; $renamed[0].nodes += New-FFNode ($name+'Renamed')
    Assert-FF (Test-FFRefusal { Resolve-NightlyPriorFailures $h $renamed PriorFailureSweep } 'current-class-missing') 'renamed'
    # D-21 uses real rc exclusions even when the run caller supplies nightly's empty rows.
    $policy=Read-NightlyExecutionPolicy -RepoRoot $repo
    $whole='Antiphon.Tests.Card0168InteractiveProbeTests'
    $methodClass='Antiphon.Tests.TestHelpers.TestDbFixtureLazyInitializationTests'
    $executionPlatform='windows'
    if ([IO.Path]::DirectorySeparatorChar -eq '/') { $executionPlatform='linux' }
    $policyRows=@(Get-NightlyProfileExclusions -PolicyObject $policy.Object -Profile rc -SuiteId antiphon -Platform $executionPlatform)
    $methodRow=@($policyRows | Where-Object { $_.Class -ceq 'TestDbFixtureLazyInitializationTests' })[0]
    $exHistory=Copy-FFObject $h
    $exHistory.members=@([pscustomobject]@{suite='antiphon';className=$whole;reasons=@('prior-failure');observations=@()})
    $exNodes=@(New-FFNode $whole)
    $exSuite=@([pscustomobject]@{id='antiphon';nodes=$exNodes;chunks=@([pscustomobject]@{id='all';classes=@($whole);nodes=$exNodes});exclusions=@()})
    Assert-FF (Test-FFRefusal { Resolve-NightlyPriorFailures $exHistory $exSuite PriorFailureSweep } 'entirely-excluded-class') 'excluded'
    $exHistory.members[0].className=$methodClass
    $exNodes=@(foreach ($method in $methodRow.Methods) { New-FFNode $methodClass $method })
    $exNodes += New-FFNode $methodClass 'A_db_free_exact_method_selection_constructs_and_starts_no_database'
    $exSuite[0].nodes=$exNodes; $exSuite[0].chunks=@([pscustomobject]@{id='all';classes=@($methodClass);nodes=$exNodes})
    $r=Resolve-NightlyPriorFailures $exHistory $exSuite PriorFailureSweep
    Assert-FF (@($r.members[0].requiredUids).Count -eq 1 -and @($r.excludedUids).Count -eq 3 -and $r.exclusionProfile -ceq 'rc' -and $r.policyDigest -ceq $policy.Hash -and $r.exclusionPolicyFileDigest -ceq (Get-NightlyHistoryDigest $policy.Path) -and $r.exclusionPlatform -ceq $executionPlatform -and @($r.excludedUids | Where-Object { $_.reason -cne $methodRow.Reason -or $_.owner -cne $methodRow.Owner }).Count -eq 0) 'methodExclusions'
    Assert-FF (Test-FFRefusal { Resolve-NightlyPriorFailures -History $exHistory -Suites $exSuite -QualificationMode PriorFailureSweep -ExclusionContext ([pscustomobject]@{profile='nightly'}) } 'partial-exclusion-profile-mismatch') 'exclusionMismatch'
    $goodContext=@{profile='rc';policyDigest=$policy.Hash;exclusionPolicyFileDigest=(Get-NightlyHistoryDigest $policy.Path);exclusionPlatform=$executionPlatform}
    $bindingsOk=$true
    foreach ($dictionary in @($true,$false)) {
        $context=$goodContext
        if (-not $dictionary) { $context=[pscustomobject]$goodContext }
        $bound=Resolve-NightlyPriorFailures $exHistory $exSuite PriorFailureSweep -ExclusionContext $context
        if (@($bound.members[0].requiredUids).Count -ne 1 -or @($bound.excludedUids).Count -ne 3) { $bindingsOk=$false }
        foreach ($field in 'policyDigest','exclusionPolicyFileDigest','exclusionPlatform') {
            $context=$goodContext.Clone(); $context[$field]='mismatching-context'
            if (-not $dictionary) { $context=[pscustomobject]$context }
            if (-not (Test-FFRefusal { Resolve-NightlyPriorFailures $exHistory $exSuite PriorFailureSweep -ExclusionContext $context } 'partial-exclusion-profile-mismatch')) { $bindingsOk=$false }
        }
    }
    Assert-FF $bindingsOk 'contextBindings'
    $noChunk=Copy-FFObject $h
    foreach ($m in $noChunk.members) { foreach ($o in $m.observations) { $o.chunk='' } }
    $r=Resolve-NightlyPriorFailures $noChunk $current PriorFailureSweep
    Assert-FF (@($r.members | Where-Object { $_.className -ceq $name }).Count -eq 1) 'noOldChunk'
    $empty=Copy-FFObject $h; $empty.members=@(); $empty.unresolved=@(); $empty.fallbackChunks=@()
    Assert-FF (@((Resolve-NightlyPriorFailures $empty $current Full).members).Count -eq 0 -and (Test-FFRefusal { Resolve-NightlyPriorFailures $empty $current PriorFailureSweep } 'empty-history')) 'empty'
    Assert-FF ((Test-FFRefusal { Read-NightlyPriorFailures $path ('0'*64) } 'history-digest-mismatch') -and (Test-FFRefusal { Read-NightlyPriorFailures -Path '' } 'empty-history-path')) 'digest'
    $bad=Copy-FFObject $h; $bad.format='unsupported'; $badPath=Join-Path $root 'bad.json'; Write-FFJson $badPath $bad
    Assert-FF (Test-FFRefusal { Read-NightlyPriorFailures $badPath } 'history-schema') 'schema'
    # Compare each field to bound OLD evidence, never to candidate SHA/policy.
    foreach ($field in 'sourceRunId','sourceSha','policyHash','profile','platform','sourceAttempt') {
        $bad=Copy-FFObject $h
        $value=@{sourceRunId='another-run';sourceSha=('c'*40);policyHash=('d'*64);profile='nightly';platform='linux';sourceAttempt='2'}[$field]
        $bad.$field=$value; Write-FFJson $badPath $bad
        Assert-FF (Test-FFRefusal { Read-NightlyPriorFailures $badPath } 'history-provenance') ('provenance-'+$field)
    }
    $diagPath=Join-Path $fx.Run 'antiphon-all-group-105-execution/invocation.diag'
    $old=[IO.File]::ReadAllText($diagPath); [IO.File]::AppendAllText($diagPath,"`nchanged")
    Assert-FF (Test-FFRefusal { Read-NightlyPriorFailures $path $exp.Digest } 'evidence-digest-mismatch') 'evidenceTamper'
    [IO.File]::WriteAllText($diagPath,$old)
    $oldTrx=$fx.Summary.suites[0].trx; $fx.Summary.suites[0].trx='../escape.trx'; Write-FFJson (Join-Path $fx.Run 'summary.json') $fx.Summary
    Assert-FF (Test-FFRefusal { Export-NightlyPriorFailures $fx.Run (Join-Path $root 'escape.json') } 'evidence-path-escape') 'escape'
    $fx.Summary.suites[0].trx=$oldTrx; Write-FFJson (Join-Path $fx.Run 'summary.json') $fx.Summary
    $link=Join-Path $fx.Run 'linked.trx'
    New-Item -ItemType SymbolicLink -Path $link -Target (Join-Path $fx.Run $oldTrx) | Out-Null
    $fx.Summary.suites[0].trx='linked.trx'; Write-FFJson (Join-Path $fx.Run 'summary.json') $fx.Summary
    $symbolicRefused=Test-FFRefusal { Export-NightlyPriorFailures $fx.Run (Join-Path $root 'link.json') } 'evidence-link'
    $hard=Join-Path $fx.Run 'hard-linked.trx'
    New-Item -ItemType HardLink -Path $hard -Target (Join-Path $fx.Run $oldTrx) | Out-Null
    $fx.Summary.suites[0].trx='hard-linked.trx'; Write-FFJson (Join-Path $fx.Run 'summary.json') $fx.Summary
    $hardRefused=Test-FFRefusal { Export-NightlyPriorFailures $fx.Run (Join-Path $root 'hard-link.json') } 'evidence-link'
    Remove-Item -LiteralPath $hard
    Assert-FF ($symbolicRefused -and $hardRefused) 'link'
    $fx.Summary.suites[0].trx=$oldTrx; $fx.Summary.completedAt=''; Write-FFJson (Join-Path $fx.Run 'summary.json') $fx.Summary
    Assert-FF (Test-FFRefusal { Export-NightlyPriorFailures $fx.Run (Join-Path $root 'working.json') } 'run-not-closed') 'working'
    $fx.Summary.completedAt='2026-10-29T00:00:00Z'; Write-FFJson (Join-Path $fx.Run 'summary.json') $fx.Summary
    Assert-FF (Test-FFRefusal { Export-NightlyPriorFailures $fx.Run (Join-Path $fx.Run 'inside.json') } 'history-output-inside-run') 'outputInside'
    [IO.File]::WriteAllText($diagPath,($old+$old.Substring($old.IndexOf('TestNodeUpdateMessage'))))
    Assert-FF (Test-FFRefusal { Export-NightlyPriorFailures $fx.Run (Join-Path $root 'duplicate.json') } 'history-identity-conflict') 'duplicateUid'
    [IO.File]::WriteAllText($diagPath,$old)
    $discPath=Join-Path $fx.Run 'antiphon-profile.discovery.json'; $disc=[IO.File]::ReadAllText($discPath); $d=$disc | ConvertFrom-Json
    @($d.nodes | Where-Object { $_.uid -ceq 'diag-0' })[0].type='AnotherTests'; Write-FFJson $discPath $d
    Assert-FF (Test-FFRefusal { Export-NightlyPriorFailures $fx.Run (Join-Path $root 'mapping.json') } 'history-identity-conflict') 'invalidMapping'
    [IO.File]::WriteAllText($discPath,$disc)
    $fx.Summary.suites[-1].Remove('execution'); Write-FFJson (Join-Path $fx.Run 'summary.json') $fx.Summary
    [IO.File]::WriteAllText((Join-Path $fx.Run 'antiphon-all-group-105-execution/other.diag'),$old)
    Assert-FF (Test-FFRefusal { Export-NightlyPriorFailures $fx.Run (Join-Path $root 'ambiguous.json') } 'ambiguous-invocation') 'ambiguous'
    $fx.Summary.suites[-1].execution='antiphon-all-group-105-execution/invocation.diag'; Write-FFJson (Join-Path $fx.Run 'summary.json') $fx.Summary
    Remove-Item -LiteralPath (Join-Path $fx.Run 'timing/groups-planned-antiphon.json')
    [IO.File]::WriteAllText($diagPath,'truncated diagnostic')
    $mal=$null
    try { $mal=Export-NightlyPriorFailures $fx.Run (Join-Path $root 'malformed.json') -PassThru } catch { }
    Assert-FF ($null -ne $mal -and -not $mal.History.membershipComplete -and @($mal.History.unresolved).Count -gt 0 -and (Test-FFRefusal { Resolve-NightlyPriorFailures $mal.History $current PriorFailureSweep } 'historical-membership-unavailable')) 'malformedFallback'
    [IO.File]::WriteAllText($diagPath,$old)
    $planPath=Join-Path $fx.Run 'timing/groups-planned-antiphon.json'
    Write-FFJson $planPath ([ordered]@{version=1;suite='antiphon';runId='different-run';attempt='1';sourceDigest=('a'*40);policyDigest=('b'*64);groups=@()})
    Assert-FF (Test-FFRefusal { Export-NightlyPriorFailures $fx.Run (Join-Path $root 'binding.json') } 'history-provenance') 'boundPlan'
    Remove-Item -LiteralPath $planPath
    $caseHistory=Copy-FFObject $h; $caseHistory.members[0].className=$caseHistory.members[0].className.ToLowerInvariant()
    $r=Resolve-NightlyPriorFailures $caseHistory $current Full
    Assert-FF (@($r.unresolved).Count -eq 1 -and (Test-FFRefusal { Resolve-NightlyPriorFailures $caseHistory $current PriorFailureSweep } 'current-class-missing')) 'caseSensitive'
    $selector=Copy-FFObject $h; $selector.members[0].className='Antiphon.Tests.*'
    Assert-FF ((Test-FFRefusal { Resolve-NightlyPriorFailures $selector $current Full } 'history-class-identity') -and (Test-FFRefusal { Resolve-NightlyPriorFailures $selector $current PriorFailureSweep } 'history-class-identity')) 'invalidSelector'
    $execPath=Join-Path $fx.Run 'execution.json'
    Write-FFJson $execPath ([ordered]@{format='antiphon-tunit-execution-v1';tunitVersion='1.44.0';mtpVersion='2.2.2';nodes=@([ordered]@{uid='diag-7';state='InProgress'})})
    $fx.Summary.suites[-1].execution='execution.json'; Write-FFJson (Join-Path $fx.Run 'summary.json') $fx.Summary
    $j=Export-NightlyPriorFailures $fx.Run (Join-Path $root 'execution-history.json') -PassThru
    Assert-FF (@($j.History.members | Where-Object { $_.className -ceq 'Antiphon.Tests.Checkpoints.CheckpointExecutorLogTests' }).Count -eq 1) 'executionJson'
    $fx.Summary.suites[0].trx='C:\retired\logs\retained-run\'+$oldTrx; Write-FFJson (Join-Path $fx.Run 'summary.json') $fx.Summary
    $rel=Export-NightlyPriorFailures $fx.Run (Join-Path $root 'relocated-history.json') -PassThru
    Assert-FF (@($rel.History.evidence | Where-Object { $_.relativePath -ceq $oldTrx }).Count -eq 1 -and @($rel.History.rebindings).Count -ge 1) 'relocation'
}
function New-FFGlobalFixture {
    $replay = New-FFReplay
    $policy = Read-NightlyExecutionPolicy -RepoRoot $repo
    $all = [Collections.Generic.List[object]]::new()
    $all.Add($replay)
    foreach ($s in @(@('session-runner','Antiphon.SessionRunner.Tests.GrokRulesHttpAcceptanceTests'),@('agents-pty','Antiphon.Agents.Pty.Tests.FakeClaudeContractTests'),@('e2e','Antiphon.E2E.BoardE2ETests'))) {
        $nodes = @((New-FFNode $s[1]),(New-FFNode ($s[1]+'Ordinary')))
        $chunks = @(Get-NightlyProfileLaunchPlan -DiscoveryNodes $nodes -Chunks @([pscustomobject]@{id='all';classes=@()}) -Exclusions @() -SuiteId $s[0] -PolicyObject $policy.Object)
        # Separate companion classes by exact production class filter, preserving
        # the production planner's prices and watchdog. Supplemental, not replay.
        if ($chunks.Count -eq 1) {
            $chunks = @()
            foreach ($n in $nodes) { $chunks += @(Get-NightlyProfileLaunchPlan -DiscoveryNodes @($n) -Chunks @([pscustomobject]@{id='all';classes=@()}) -Exclusions @() -SuiteId $s[0] -PolicyObject $policy.Object) }
            $chunks[1].id = 'ordinary'
        }
        $all.Add([pscustomobject]@{id=$s[0];nodes=$nodes;chunks=$chunks;exclusions=@()})
    }
    $history = [pscustomobject]@{format='antiphon-nightly-prior-failures-v1';sourceRunId='old';sourceAttempt='1';sourceSha=('b'*40);profile='rc';platform='windows';policyHash=('b'*64);createdAtUtc='2026-10-30T00:00:00Z';complete=$true;evidence=@();members=@();fallbackChunks=@();membershipComplete=$true;unresolved=@()}
    $anchors = @()
    foreach ($ordinal in @(23,99,101,118)) {
        $anchor = $replay.chunks[$ordinal-1]
        $class = Get-NightlyBareClassIdentity ([string]$anchor.classes[0])
        $anchors += [pscustomobject]@{ordinal=$ordinal;chunk=$anchor.id;className=$class}
        if (@($history.members | Where-Object {$_.className -ceq $class}).Count -eq 0) { $history.members += [pscustomobject]@{suite='antiphon';className=$class;reasons=@('Failed');observations=@()} }
    }
    foreach ($s in @($all.ToArray() | Select-Object -Skip 1)) { $history.members += [pscustomobject]@{suite=$s.id;className=$s.nodes[0].ClassName;reasons=@('Failed');observations=@()} }
    $resolved = Resolve-NightlyPriorFailures -History $history -Suites $all.ToArray() -QualificationMode Full
    $schedule = New-NightlyDispatchSchedule -PreparedSuites $all.ToArray() -PriorityMembers $resolved.members -HistoryDigest ('d'*64) -RunId 'new' -Attempt '1'
    Write-FFJson (Join-Path $root 'anchors.json') $anchors
    return [pscustomobject]@{suites=$all.ToArray();replay=$replay;history=$history;resolved=$resolved;schedule=$schedule;anchors=$anchors}
}
function Test-GlobalReplayPriority {
    $fx = New-FFGlobalFixture
    $schedule = $fx.schedule
    $priority = @($schedule.entries | Where-Object priorFailure)
    $ordinary = @($schedule.entries | Where-Object {-not $_.priorFailure})
    Assert-FF ($fx.replay.nodes.Count -eq 19012 -and $fx.replay.chunks.Count -eq 150) 'replay'
    Assert-FF ($priority.Count -gt 4 -and $ordinary.Count -gt 0 -and ($priority.dispatchOrdinal | Measure-Object -Maximum).Maximum -lt ($ordinary.dispatchOrdinal | Measure-Object -Minimum).Minimum) 'globalPriority'
    Assert-FF (($priority.canonicalOrdinal -join ',') -ceq ((@($priority.canonicalOrdinal | Sort-Object)) -join ',') -and ($ordinary.canonicalOrdinal -join ',') -ceq ((@($ordinary.canonicalOrdinal | Sort-Object)) -join ',')) 'stable'
    $keys=@($schedule.entries | ForEach-Object {$_.suite+'/'+$_.chunk})
    $canonical=@(foreach($s in $fx.suites){foreach($c in $s.chunks){$s.id+'/'+$c.id}})
    Assert-FF ($keys.Count -eq $canonical.Count -and @($keys | Select-Object -Unique).Count -eq $canonical.Count -and -not @(Compare-Object $keys $canonical -CaseSensitive).Count) 'permutation'
    $same=$true
    foreach($entry in $schedule.entries){$s=@($fx.suites|Where-Object {$_.id -ceq $entry.suite})[0];$c=@($s.chunks|Where-Object {$_.id -ceq $entry.chunk})[0];if(-not [object]::ReferenceEquals($entry.ChunkRef,$c)){$same=$false}}
    Assert-FF $same 'references'
    Assert-FF (@($priority|Where-Object {$_.suite -cne 'antiphon' -and $_.dispatchOrdinal -lt $ordinary[0].dispatchOrdinal}).Count -eq 3) 'crossSuite'
    $splits=$true
    foreach($m in $fx.resolved.members){foreach($id in $m.chunks){if(@($priority|Where-Object {$_.suite -ceq $m.suite -and $_.chunk -ceq $id}).Count -ne 1){$splits=$false}}}
    Assert-FF $splits 'allSplits'
    $plain=New-NightlyDispatchSchedule -PreparedSuites $fx.suites
    Assert-FF ($plain.canonicalPlanDigest -ceq $schedule.canonicalPlanDigest -and ($plain.entries.canonicalOrdinal -join ',') -ceq ($plain.entries.dispatchOrdinal -join ',')) 'canonicalDigest'
    $unknown=New-NightlyDispatchSchedule -PreparedSuites $fx.suites -Unresolved @([ordered]@{suite='antiphon';className='Retired.Class';reason='current-class-missing'})
    Assert-FF ($unknown.unresolved.Count -eq 1 -and $unknown.entries.Count -eq $plain.entries.Count) 'unresolved'
    Assert-FF (Test-FFRefusal {New-NightlyDispatchSchedule -PreparedSuites @($fx.suites[0],$fx.suites[0])} 'duplicate-canonical-chunk') 'duplicate'
}

function Get-FFArgument([object[]]$Arguments, [string]$Name) {
    $i = [array]::IndexOf($Arguments, $Name)
    if ($i -ge 0 -and $i+1 -lt $Arguments.Count) { return [string]$Arguments[$i+1] }
    return ''
}
function Write-FFDiagnostic([string]$Directory, [object[]]$Nodes, [string]$State = 'Passed') {
    [void][IO.Directory]::CreateDirectory($Directory)
    $text = [Text.StringBuilder]::new()
    [void]$text.Append("Version: '1.44.0.0'`nVersion: 2.2.2`n")
    foreach ($n in $Nodes) {
        $outcome = $State
        if ($n.PSObject.Properties.Name -contains 'Outcome') { $outcome = [string]$n.Outcome }
        [void]$text.Append(('TestNodeUpdateMessage {{ TestNode = TestNode {{ Uid = TestNodeUid {{ Value = {0} }}, DisplayName = fixture, Properties = [ TestMethodIdentifierProperty {{ AssemblyFullName = Fixture, Namespace = {1}, TypeName = {2}, MethodName = {3}, MethodArity = 0, ParameterTypeFullNames = [], ReturnTypeFullName = System.Void }}, {4}TestNodeStateProperty {{ Explanation =  }} ] }} }}' -f $n.Uid,$n.Namespace,$n.Type,$n.Method,$outcome))
        [void]$text.Append("`n")
    }
    [IO.File]::WriteAllText((Join-Path $Directory 'fixture.diag'), $text.ToString())
}
function Invoke-FFPreparedArm {
    param($Fixture, [string]$Name, [object[]]$Members = @(), [string]$Variant = 'pass')
    $armWatch = [Diagnostics.Stopwatch]::StartNew()
    $RepoRoot = $repo
    $LogRoot = Join-Path $root $Name
    [void][IO.Directory]::CreateDirectory($LogRoot)
    $TimingRoot = Join-Path $LogRoot 'timing'
    $script:QualificationTimingRoot = $TimingRoot
    $invocationId = 'conservation-run'; $Attempt = 1; $Sha = 'a'*40; $GitRef = 'origin/master'
    $Profile = 'rc'; $profileAware = $true
    $policy = Read-NightlyExecutionPolicy -RepoRoot $repo; $policyHash = $policy.Hash
    $startedAt = [datetime]::UtcNow.AddSeconds(-1)
    $qualificationBuildSeconds = 1.25
    $coverageComplete = $true; $testsPassed = $true; $overallFailed = $false
    $suiteResults = @(); $reasons = @(); $activeNative = 0; $nativeActiveMax = 0
    $script:NightlyTestsOutcome = 'green'
    $script:FFLaunches = [Collections.Generic.List[object]]::new()
    $script:FFArmChunks = @{}
    $script:FFVariant = $Variant
    $script:FFFailureKey = 'antiphon/'+[string]$Fixture.suites[0].chunks[-1].id
    $script:NightlySeams = @{
        StartProcess = {
            param($FilePath,$ArgumentList,$WorkingDirectory,$TimeoutMilliseconds,$Environment)
            $suiteKey = [string]$Environment['ANTIPHON_TEST_PHASE_SUITE']
            $chunkKey = [string]$Environment['ANTIPHON_TEST_PHASE_GROUP_ID']
            $key = $suiteKey+'/'+$chunkKey
            $original = $script:FFArmChunks[$key]
            if ($null -eq $original) { throw ('unkeyed-launch:'+ $key) }
            $script:FFLaunches.Add([pscustomobject]@{suite=$suiteKey;chunk=$chunkKey;key=$key;filter=(Get-FFArgument $ArgumentList '--treenode-filter');watchdog=$TimeoutMilliseconds;host=$FilePath})
            $nodes = @($original.nodes)
            $rows = @()
            $terminals = @()
            $exitCode = 0; $timedOut = $false
            $special = ($key -ceq $script:FFFailureKey)
            foreach ($n in $nodes) {
                $state = 'Passed'
                if ($special -and $script:FFVariant -ceq 'failure') { $state='Failed'; $exitCode=2 }
                if ($special -and $script:FFVariant -ceq 'skip') { $state='Skipped' }
                $rows += [pscustomobject]@{Id=$n.Uid;ClassName=$n.ClassName;MethodName=$n.Method;Outcome=$state}
                $tn = [pscustomobject]@{Uid=$n.Uid;Namespace=$n.Namespace;Type=$n.Type;Method=$n.Method;Outcome=$state}
                $terminals += $tn
            }
            if ($special -and $script:FFVariant -ceq 'unknown') { $terminals += [pscustomobject]@{Uid='foreign';Namespace=$nodes[0].Namespace;Type=$nodes[0].Type;Method=$nodes[0].Method;Outcome='Passed'} }
            if ($script:FFVariant -cin @('phase','phase-stale')) {
                Write-FFPhaseFixture -Directory ([string]$Environment['ANTIPHON_TEST_PHASE_ROOT']) -Suite $suiteKey -Chunk $chunkKey -Nodes $nodes -Stale:($special -and $script:FFVariant -ceq 'phase-stale')
            }
            $trx = Join-Path (Get-FFArgument $ArgumentList '--results-directory') (Get-FFArgument $ArgumentList '--report-trx-filename')
            if (-not ($special -and $script:FFVariant -ceq 'missing')) { Write-C487Trx -Path $trx -Rows $rows }
            Write-FFDiagnostic (Get-FFArgument $ArgumentList '--diagnostic-output-directory') $terminals
            if ($special -and $script:FFVariant -ceq 'timeout') { $timedOut=$true; $exitCode=1 }
            if ($special -and $script:FFVariant -ceq 'publication') { $script:TestPhaseCommitFault=[pscustomobject]@{Stage='Rename';Remaining=2} }
            $children = -not ($special -and $script:FFVariant -ceq 'teardown')
            return [pscustomobject]@{ExitCode=$exitCode;TimedOut=$timedOut;ChildrenExited=$children;CleanupComplete=$children;Pid=1;LogText='';JobEmptyObserved=$children}
        }
    }
    $states = @()
    $plannedBytes = @{}
    foreach ($s in $Fixture.suites) {
        $discPath = Join-Path $LogRoot ($s.id+'-profile.discovery.json')
        $doc = ConvertTo-NightlyDiscoveryDocument -Nodes $s.nodes -AssemblyHash 'fixture'
        Write-NightlyDiscoveryDocument -Path $discPath -Document $doc
        if ($s.PSObject.Properties.Name -notcontains 'SharedDiscoveryIndex') {
            $s | Add-Member -NotePropertyName SharedDiscoveryIndex -NotePropertyValue (New-NightlyDiscoveryIndex)
        }
        $index = $s.SharedDiscoveryIndex
        $entry = Get-NightlyDiscoveryIndexEntry -Index $index -Path $discPath
        $census = (Get-NightlyDiscoveryIndexView -Entry $entry -ProfileExclusions $s.exclusions -ProfileAware).Census
        $variables = @{
            suiteId=$s.id;suite=[pscustomobject]@{assembly=$s.id};label=$s.id;mode='unattended';isNative=$true;chunks=$s.chunks
            profileExclusions=$s.exclusions;suiteHost=[pscustomobject]@{FilePath=('fixture-'+$s.id);Prefix=@();AssemblyPath='fixture'}
            suiteDiscoveryNodes=$s.nodes;profileDiscovery=$doc;profileDiscoveryPath=$discPath;suiteDiscoveryIndex=$index;suiteCensus=$census
            suiteDiscoverySeconds=0.25;suiteTerminalNodes=@();chunkIndex=0;phaseAssembly=$s.id;phaseAssemblyDigest='fixture';phaseCostDigest='fixture'
        }
        $states += [pscustomobject]@{id=$s.id;nodes=$s.nodes;chunks=$s.chunks;exclusions=$s.exclusions;Variables=$variables;Completed=$false}
        if ($s.PSObject.Properties.Name -notcontains 'SharedPlannedText') {
            [void](Write-TestPhasePlannedManifest -LogRoot $LogRoot -RunId $invocationId -Attempt '1' -Suite $s.id -SourceDigest $Sha -AssemblyDigest 'fixture' -PolicyDigest $policyHash -CostFileDigest 'fixture' -AssemblyId $s.id -Groups $s.chunks -DiscoveryNodes $s.nodes -ProfileExclusions $s.exclusions -DiscoveryIndex $index -DiscoveryPath $discPath)
            $s | Add-Member -NotePropertyName SharedPlannedText -NotePropertyValue ([IO.File]::ReadAllText((Join-Path $TimingRoot ('groups-planned-'+$s.id+'.json'))))
        } else {
            # Both arms consume the same once-published immutable plans; only
            # mutable execution/journal/sidecar state has a fresh root.
            $planFile='groups-planned-'+$s.id+'.json'
            Write-TestPhaseTextAtomic -Path (Join-Path $TimingRoot $planFile) -Text $s.SharedPlannedText
            Write-TestPhaseTextAtomic -Path (Join-Path $TimingRoot 'groups-planned.json') -Text $s.SharedPlannedText
            $sharedPlan=$s.SharedPlannedText|ConvertFrom-Json
            Update-TestPhasePlannedIndex -Directory $TimingRoot -Suite $s.id -FileName $planFile -RunId $invocationId -Attempt '1' -AssemblyId $s.id -SourceDigest $Sha -PolicyDigest $policyHash -CostFileDigest 'fixture' -AssemblyDigest 'fixture' -Filters @($sharedPlan.groups.filter)
        }
        $plannedBytes[$s.id] = [IO.File]::ReadAllText((Join-Path $TimingRoot ('groups-planned-'+$s.id+'.json')))
        $plannedDoc = $plannedBytes[$s.id] | ConvertFrom-Json
        $byUid = @{}
        foreach ($n in $s.nodes) { $byUid[[string]$n.Uid]=$n }
        foreach ($g in $plannedDoc.groups) {
            $selectedNodes = @($g.plannedTestIds | ForEach-Object { $byUid[[string]$_] })
            $script:FFArmChunks[$s.id+'/'+$g.chunk] = [pscustomobject]@{nodes=$selectedNodes}
        }
    }
    $armPrepareSeconds=$armWatch.Elapsed.TotalSeconds
    $armExecuteWatch=[Diagnostics.Stopwatch]::StartNew()
    $publicationContext = Open-TestPhaseExecutedContext -LogRoot $LogRoot -TimingRoot $TimingRoot -RunId $invocationId -Attempt '1' -Suite '' -Source $Sha -InvocationId $invocationId
    if ($publicationContext.Refused) { throw ('context-refused:'+ $publicationContext.Reason) }
    $script:TestPhaseExecutedContext = $publicationContext
    $schedule = New-NightlyDispatchSchedule -PreparedSuites $states -PriorityMembers $Members -HistoryDigest ('d'*64) -RunId $invocationId -Attempt '1'
    Write-NightlyDispatchSchedule $schedule $LogRoot
    $errorText = ''
    try { $null = . Invoke-NightlyPreparedSchedule -PreparedSchedule $schedule }
    catch { $errorText=$_.Exception.Message; $coverageComplete=$false; $overallFailed=$true; $reasons += $errorText }
    finally {
        $terminal='success'; if ($overallFailed -or -not $testsPassed) { $terminal='failed-run' }
        Complete-TestPhaseExecutedProjection -Context $publicationContext -Terminal $terminal | Out-Null
        Close-TestPhaseExecutedContext -Context $publicationContext
        $script:TestPhaseCommitFault=$null
    }
    $armExecuteSeconds=$armExecuteWatch.Elapsed.TotalSeconds
    if ($Fixture.PSObject.Properties.Name -notcontains 'replay') { Invoke-TestPhaseRunSummary -RunDirectory $LogRoot }
    $timing = Get-QualificationTimingReport -LogRoot $LogRoot -TimingRoot $TimingRoot
    $projection = [IO.File]::ReadAllText((Join-Path $TimingRoot 'groups-executed.json')) | ConvertFrom-Json
    $plansStable = $true
    foreach ($s in $Fixture.suites) { if ([IO.File]::ReadAllText((Join-Path $TimingRoot ('groups-planned-'+$s.id+'.json'))) -cne $plannedBytes[$s.id]) { $plansStable=$false } }
    $output = [pscustomobject]@{Root=$LogRoot;TimingRoot=$TimingRoot;Schedule=$schedule;Launches=$script:FFLaunches.ToArray();Plans=$plannedBytes;PlansStable=$plansStable;Projection=$projection;Rows=$suiteResults;Reasons=$reasons;Coverage=$coverageComplete;Passed=$testsPassed;Error=$errorText;States=$states;Timing=$timing;Context=$publicationContext;NativeMax=$nativeActiveMax;Outcome=$script:NightlyTestsOutcome;OverallFailed=$overallFailed}
    $script:FFOperations.Add([ordered]@{kind='IO';operation=$Name;prepareSeconds=$armPrepareSeconds;executeSeconds=$armExecuteSeconds;wallSeconds=$armWatch.Elapsed.TotalSeconds})
    Write-FFJson (Join-Path $LogRoot 'arm-result.json') ([ordered]@{prepareSeconds=$armPrepareSeconds;executeSeconds=$armExecuteSeconds;wallSeconds=$armWatch.Elapsed.TotalSeconds;launches=$output.Launches;rows=$suiteResults;reasons=$reasons;coverage=$coverageComplete;passed=$testsPassed;outcome=$script:NightlyTestsOutcome;overallFailed=$overallFailed;error=$errorText;nativeMax=$nativeActiveMax})
    return $output
}
function New-FFSmallFixture {
    $policy = Read-NightlyExecutionPolicy -RepoRoot $repo
    $suites = @()
    foreach ($sid in @('antiphon','session-runner')) {
        $nodes = @((New-FFNode ('Fixture.'+$sid.Replace('-','')+'.Ordinary')),(New-FFNode ('Fixture.'+$sid.Replace('-','')+'.Priority')))
        $chunks=@()
        foreach($n in $nodes){$c=@(Get-NightlyProfileLaunchPlan -DiscoveryNodes @($n) -Chunks @([pscustomobject]@{id='all';classes=@()}) -Exclusions @() -SuiteId $sid -PolicyObject $policy.Object)[0];$c.id=$n.Type;$chunks += $c}
        $suites += [pscustomobject]@{id=$sid;nodes=$nodes;chunks=$chunks;exclusions=@()}
    }
    return [pscustomobject]@{suites=$suites;resolved=[pscustomobject]@{members=@([pscustomobject]@{suite='antiphon';chunks=@('Priority')},[pscustomobject]@{suite='session-runner';chunks=@('Priority')})}}
}
function Test-FullBytesAndCoverageConserved {
    param([switch]$DiagnosticsOnly)
    if (-not $DiagnosticsOnly) {
    $fx = New-FFGlobalFixture
    $before = ConvertTo-Json -InputObject @($fx.suites.chunks) -Depth 30 -Compress
    $a = Invoke-FFPreparedArm $fx 'canonical'
    $b = Invoke-FFPreparedArm $fx 'priority' $fx.resolved.members
    $canonical = @(foreach ($s in $fx.suites) { foreach ($c in $s.chunks) { $s.id+'/'+$c.id } })
    $actual = @($b.Launches.key)
    $prefix = @($b.Schedule.entries | Where-Object priorFailure)
    # This must precede all conservation comparisons and uses actual launches.
    Assert-FF ((@($a.Launches.key) -join ',') -ceq ($canonical -join ',') -and ($actual -join ',') -cne ($canonical -join ',') -and $actual.Count -eq $canonical.Count -and @($actual|Select-Object -Unique).Count -eq $canonical.Count -and -not @(Compare-Object $actual $canonical -CaseSensitive).Count -and [array]::IndexOf($actual, 'session-runner/'+@($fx.resolved.members|Where-Object {$_.suite -ceq 'session-runner'})[0].chunks[0]) -lt [array]::IndexOf($actual, $canonical[0])) 'observedPermutation'
    $anchorKeys=@($fx.anchors|ForEach-Object {'antiphon/'+$_.chunk})
    $observedAnchors=@($actual|Where-Object {$anchorKeys -ccontains $_})
    Write-FFJson (Join-Path $root 'observed-ordinals.json') @(foreach($anchor in $fx.anchors){[ordered]@{suite='antiphon';chunk=$anchor.chunk;canonicalOrdinal=($anchor.ordinal-1);dispatchOrdinal=[array]::IndexOf($actual,('antiphon/'+$anchor.chunk));className=$anchor.className}})
    Assert-FF (($observedAnchors -join ',') -ceq ($anchorKeys -join ',') -and @($prefix|Where-Object {$anchorKeys -ccontains ($_.suite+'/'+$_.chunk)}).Count -eq 4) 'anchors'
    $samePlans=$true
    foreach($s in $fx.suites){if($a.Plans[$s.id] -cne $b.Plans[$s.id]){$samePlans=$false}}
    Assert-FF ($samePlans -and $a.PlansStable -and $b.PlansStable -and $before -ceq (ConvertTo-Json -InputObject @($fx.suites.chunks) -Depth 30 -Compress) -and $a.Schedule.canonicalPlanDigest -ceq $b.Schedule.canonicalPlanDigest) 'canonicalBytes'
    Assert-FF ($a.Coverage -and $a.Passed -and $b.Coverage -and $b.Passed -and $a.Outcome -ceq 'green' -and $b.Outcome -ceq 'green' -and -not $a.OverallFailed -and -not $b.OverallFailed -and $a.NativeMax -eq 1 -and $b.NativeMax -eq 1 -and @($a.States|Where-Object {-not $_.Completed}).Count -eq 0 -and @($b.States|Where-Object {-not $_.Completed}).Count -eq 0) 'coverage'
    $keyed=$true
    foreach($r in $a.Projection.chunks){$other=@($b.Projection.chunks|Where-Object {$_.suite -ceq $r.suite -and $_.chunk -ceq $r.chunk})[0];foreach($f in @('exitCode','trxMissing','passedCount','outcomes','trxDigest','manufacturedPassed')){if((ConvertTo-Json -InputObject $r.$f -Depth 10 -Compress) -cne (ConvertTo-Json -InputObject $other.$f -Depth 10 -Compress)){$keyed=$false}}}
    Assert-FF ($keyed -and -not $a.Projection.manufacturedPassed -and -not $b.Projection.manufacturedPassed) 'keyedObservations'
    }
    $small=New-FFSmallFixture
    if ($DiagnosticsOnly) {
        $fx=$small
        $a=Invoke-FFPreparedArm $small 'small-canonical'
        $b=Invoke-FFPreparedArm $small 'small-priority' $small.resolved.members
        $canonical=@($a.Launches.key); $actual=@($b.Launches.key)
    }
    $matrix=@()
    foreach($variant in @('failure','missing','skip','unknown','timeout','publication','teardown')){
        $left=Invoke-FFPreparedArm $small ($variant+'-canonical') @() $variant
        $right=Invoke-FFPreparedArm $small ($variant+'-priority') $small.resolved.members $variant
        $same=($left.Outcome -ceq $right.Outcome -and $left.OverallFailed -eq $right.OverallFailed -and $left.Coverage -eq $right.Coverage -and $left.Passed -eq $right.Passed -and $left.Error -ceq $right.Error -and (($left.Reasons|Sort-Object) -join ',') -ceq (($right.Reasons|Sort-Object) -join ','))
        $expectedOutcome = 'TESTS'
        # The unchanged reducer lowers TIMEOUT to TESTS when a later passing
        # chunk observes the sticky failed/coverage flags. Preserve that behavior.
        if ($variant -cin @('publication','teardown')) { $expectedOutcome='green' }
        if ($left.Outcome -cne $expectedOutcome -or $right.Outcome -cne $expectedOutcome) { $same=$false }
        if ($variant -cin @('missing','skip','unknown','timeout') -and ($left.Coverage -or $right.Coverage -or -not $left.OverallFailed -or -not $right.OverallFailed)) { $same=$false }
        if ($variant -ceq 'failure' -and ($left.Passed -or $right.Passed -or -not $left.Coverage -or -not $right.Coverage)) { $same=$false }
        if ($variant -ceq 'publication' -and (-not $left.Passed -or -not $right.Passed -or -not $left.Coverage -or -not $right.Coverage)) { $same=$false }
        # Teardown aborts the run at its keyed chunk; no subsequent required work is credited.
        if($variant -ceq 'teardown'){$same=(-not $left.Coverage -and -not $right.Coverage -and $left.Error -like '*owned-child*' -and $right.Error -like '*owned-child*' -and $left.OverallFailed -and $right.OverallFailed -and $left.Outcome -ceq 'green' -and $right.Outcome -ceq 'green')}
        $matrix += [pscustomobject]@{variant=$variant;left=$left;right=$right;same=$same}
    }
    Assert-FF (@($matrix|Where-Object {-not $_.same}).Count -eq 0) 'terminalMatrix' (($matrix|Where-Object {-not $_.same}).variant -join ',')
    $failed=@($matrix|Where-Object {$_.variant -ceq 'failure'})[0]
    $prefixOk=$true
    foreach($arm in @($failed.left,$failed.right)){
        $passed=$true;$coverage=$true
        foreach($r in $arm.Projection.chunks){if($r.testsPassed -ne $passed -or $r.coverageComplete -ne $coverage){$prefixOk=$false};if($r.exitCode -ne 0){$passed=$false}}
    }
    Assert-FF $prefixOk 'prefixState'
    $companion=@($small.resolved.members|Where-Object {$_.suite -ceq 'session-runner'})
    $header=Invoke-FFPreparedArm $small 'companion-first' $companion
    Assert-FF ($a.Projection.suite -ceq $a.Launches[0].suite -and $b.Projection.suite -ceq $b.Launches[0].suite -and $header.Projection.suite -ceq 'session-runner' -and $header.Projection.runId -ceq 'conservation-run' -and $header.Projection.attempt -ceq '1') 'stickyHeader'
    Assert-FF ((@($a.Rows|ForEach-Object {$_.id+'/'+$_.chunk}) -join ',') -ceq ($canonical -join ',') -and (@($b.Rows|ForEach-Object {$_.id+'/'+$_.chunk}) -join ',') -ceq ($actual -join ',')) 'settlementOrder'
    Assert-FF (($failed.left.Reasons -join ',') -ceq ($failed.right.Reasons -join ',') -and -not $failed.left.Passed -and -not $failed.right.Passed) 'reasonSequence'
    $journalOk=$true
    foreach($arm in @($a,$b,$header)){
        $current=[IO.File]::ReadAllText((Join-Path $arm.TimingRoot 'groups-executed.journal/current.json'))|ConvertFrom-Json
        $generation=Join-Path (Join-Path $arm.TimingRoot 'groups-executed.journal') $current.generation
        $seal=[IO.File]::ReadAllText((Join-Path $generation 'seal.json'))|ConvertFrom-Json
        if($seal.projectionDigest -cne (Get-NightlyHistoryDigest (Join-Path $arm.TimingRoot 'groups-executed.json')) -or $seal.sequence -ne $arm.Launches.Count -or -not $arm.Context.Closed){$journalOk=$false}
        $records=@(Get-ChildItem -LiteralPath $generation -File|Where-Object {$_.Name -match '^\d{8}\.json$'}|Sort-Object Name)
        for($i=0;$i -lt $records.Count;$i++){$record=[IO.File]::ReadAllText($records[$i].FullName)|ConvertFrom-Json;if($record.sequence -ne $i+1 -or (ConvertFrom-TestPhaseB64 $record.freshSuiteKey) -cne $arm.Launches[$i].suite -or (ConvertFrom-TestPhaseB64 $record.freshChunkKey) -cne $arm.Launches[$i].chunk){$journalOk=$false}}
    }
    Assert-FF $journalOk 'journalBindings'
    $timelinesOk=$true
    foreach($arm in @($a,$b)){
        foreach($timeline in $arm.Timing.Timeline){$chunks=@($arm.Timing.Chunks|Where-Object {$_.suite -ceq $timeline.suite});if($chunks.Count -eq 0 -or $timeline.startedAt -cne @($chunks.startedAt|Sort-Object)[0] -or $timeline.completedAt -cne @($chunks.completedAt|Sort-Object)[-1] -or $timeline.lastPhaseUtc -cne @($chunks.lastPhaseUtc|Sort-Object)[-1] -or $timeline.lastPhase -cne @($chunks|Sort-Object lastPhaseUtc)[-1].lastPhase){$timelinesOk=$false}}
        foreach($t in $arm.Timing.Chunks){if($t.buildSlot -cne 'not-used' -or $null -ne $t.buildSlotWaitSeconds -or $null -ne $t.distillationSeconds){$timelinesOk=$false}}
    }
    Assert-FF $timelinesOk 'timelines'
    $summaries=$true
    foreach($arm in @($failed.left,$failed.right,$header)){
        foreach($file in @('summary.json','tests.csv','summary.md')){if(-not [IO.File]::Exists((Join-Path $arm.TimingRoot $file))){$summaries=$false}}
        if([IO.File]::Exists((Join-Path $arm.TimingRoot 'summary.json'))){$summary=[IO.File]::ReadAllText((Join-Path $arm.TimingRoot 'summary.json'))|ConvertFrom-Json;if($summary.version -ne 1){$summaries=$false}}
        $singleton=[IO.File]::ReadAllText((Join-Path $arm.TimingRoot 'groups-planned.json'))|ConvertFrom-Json
        $index=[IO.File]::ReadAllText((Join-Path $arm.TimingRoot 'groups-planned-index.json'))|ConvertFrom-Json
        if($singleton.suite -cne $small.suites[-1].id -or $index.suites.Count -ne $small.suites.Count){$summaries=$false}
    }
    Assert-FF $summaries 'phaseSummariesAndIndex'
    Assert-FF ($a.Schedule.preparationComplete -and $b.Schedule.preparationComplete -and $b.Schedule.historyDigest -ceq ('d'*64) -and $b.Schedule.unresolved.Count -eq 0) 'schedulePolicy'
    Test-FFPhaseBindings $small
    Test-FFPreparationSequencing
    Test-FFAuthorityConservation
    Test-FFPlannedGoldenBytes
    Test-FFArtifactSchemas @($failed.left,$failed.right,$header)
}
function Invoke-FFPreparationArm {
    param([string]$Name, [string]$HistoryKind, [string]$Failure = 'discovery', [switch]$WriteFault)
    $RepoRoot=$repo; $LogRoot=Join-Path $root $Name; [void][IO.Directory]::CreateDirectory($LogRoot)
    $TimingRoot=Join-Path $LogRoot 'timing'; $script:QualificationTimingRoot=$TimingRoot
    $policy=Read-NightlyExecutionPolicy -RepoRoot $repo
    $Profile='rc';$profileAware=$true;$platform='windows';$policyHash=$policy.Hash
    $suiteMap=Get-NightlySuiteMap -PolicyObject $policy.Object
    $ids=@('antiphon','session-runner','agents-pty')
    if($Failure -ceq 'e2e'){$ids=@('antiphon','e2e','agents-pty')}
    foreach($id in $ids){$policy.Object.profiles.rc.exclusions += [pscustomobject]@{suite=$id;class='Fixture.Never';reason='fixture';owner='CARD-1514'}}
    $requiredSuites=$ids;$chunksBySuite=@{};$qualificationBuildSeconds=1.25
    $invocationId='preparation-run';$Attempt=1;$Sha='a'*40;$GitRef='origin/master'
    $startedAt=[datetime]::UtcNow.AddSeconds(-1)
    $coverageComplete=$true;$testsPassed=$true;$overallFailed=$false;$preflight=[ordered]@{}
    $suiteResults=@();$reasons=@();$activeNative=0;$nativeActiveMax=0
    $npmOk=$true;$lintBlocksPipeline=$false;$dotnetOk=$true
    $script:NightlyTestsOutcome = 'green'
    $script:FFPrep=[pscustomobject]@{Trace=[Collections.Generic.List[string]]::new();Log=$LogRoot;Nodes=@{};Failure=$Failure;Second=$ids[1];SeenSettlement=$false}
    foreach($id in $ids){$script:FFPrep.Nodes[$id]=@(New-FFNode ('Fixture.'+$id.Replace('-','')+'.Only'))}
    $script:NightlySeams=@{
        StartProcess={
            param($FilePath,$ArgumentList,$WorkingDirectory,$TimeoutMilliseconds,$Environment)
            $diag=Get-FFArgument $ArgumentList '--diagnostic-output-directory'
            if($ArgumentList -contains '--list-tests'){
                $sid=(Split-Path -Leaf $diag) -replace '-profile-discovery$',''
                if($sid -ceq $script:FFPrep.Second){
                    $journal=Join-Path $script:QualificationTimingRoot 'groups-executed.journal'
                    $published=@(Get-ChildItem -LiteralPath $journal -Filter '00000001.json' -Recurse -File -ErrorAction SilentlyContinue)
                    if($published.Count -gt 0){$script:FFPrep.SeenSettlement=$true;$script:FFPrep.Trace.Add('settle-antiphon')}
                }
                $script:FFPrep.Trace.Add('prepare-'+$sid)
                if($sid -ceq $script:FFPrep.Second -and $script:FFPrep.Failure -cne 'e2e'){
                    $script:FFPrep.Trace.Add('fail-'+$sid)
                    return [pscustomobject]@{ExitCode=1;TimedOut=($script:FFPrep.Failure -ceq 'timeout');ChildrenExited=$true;CleanupComplete=$true;LogText=''}
                }
                Write-FFDiagnostic $diag $script:FFPrep.Nodes[$sid] 'Discovered'
            }else{
                $sid=[string]$Environment['ANTIPHON_TEST_PHASE_SUITE'];$script:FFPrep.Trace.Add('launch-'+$sid)
                $n=$script:FFPrep.Nodes[$sid][0]
                $trx=Join-Path (Get-FFArgument $ArgumentList '--results-directory') (Get-FFArgument $ArgumentList '--report-trx-filename')
                Write-C487Trx $trx @([pscustomobject]@{Id=$n.Uid;ClassName=$n.ClassName;MethodName=$n.Method;Outcome='Passed'})
                Write-FFDiagnostic $diag @($n)
            }
            return [pscustomobject]@{ExitCode=0;TimedOut=$false;ChildrenExited=$true;CleanupComplete=$true;LogText=''}
        }
        E2EPrerequisites={
            $script:FFPrep.Trace.Add('prepare-e2e');$script:FFPrep.Trace.Add('fail-e2e')
            return [pscustomobject]@{Ok=$false;Rows=@([pscustomobject]@{name='fixture-browser';exitCode=1;detail='fixture'})}
        }
    }
    if($WriteFault){[void][IO.Directory]::CreateDirectory((Join-Path $LogRoot 'dispatch-schedule.json'));[void][IO.Directory]::CreateDirectory((Join-Path $LogRoot 'dispatch-schedule-write-failed'))}
    $publicationContext=Open-TestPhaseExecutedContext -LogRoot $LogRoot -TimingRoot $TimingRoot -RunId $invocationId -Attempt '1' -Suite '' -Source $Sha
    $script:TestPhaseExecutedContext=$publicationContext
    $args=@{DispatchSuites=$ids}
    if($HistoryKind -ceq 'nonempty'){$args.DispatchPriorityMembers=@([pscustomobject]@{suite='antiphon';chunks=@('all-group-1')});$args.DispatchHistoryDigest='d'*64}
    if($HistoryKind -ceq 'empty'){$args.DispatchPriorityMembers=@();$args.DispatchUnresolved=@()}
    $errorText=''
    try{$null = . Invoke-NightlyDispatchCoordinator @args}catch{$errorText=$_.Exception.Message;$coverageComplete=$false}
    finally{Complete-TestPhaseExecutedProjection -Context $publicationContext -Terminal 'failed-run'|Out-Null;Close-TestPhaseExecutedContext -Context $publicationContext}
    $schedule=$null;$path=Join-Path $LogRoot 'dispatch-schedule.json'
    if([IO.File]::Exists($path)){$schedule=[IO.File]::ReadAllText($path)|ConvertFrom-Json}
    $errorKind=$errorText
    if ($errorText -like 'missing diagnostic discovery log in *') { $errorKind='missing diagnostic discovery log' }
    $answer=[pscustomobject]@{trace=$script:FFPrep.Trace.ToArray();settled=$script:FFPrep.SeenSettlement;coverage=$coverageComplete;rows=$suiteResults;error=$errorText;errorKind=$errorKind;schedule=$schedule}
    Write-FFJson (Join-Path $LogRoot 'preparation-result.json') $answer
    return $answer
}
function Test-FFPreparationSequencing {
    $lazy=$true;$barrier=$true;$e2e=$true;$io=$true
    foreach($failure in @('discovery','timeout','e2e')){
        $absent=Invoke-FFPreparationArm ('lazy-'+$failure) 'absent' $failure
        $empty=Invoke-FFPreparationArm ('empty-'+$failure) 'empty' $failure
        $priority=Invoke-FFPreparationArm ('barrier-'+$failure) 'nonempty' $failure
        if(($absent.trace -join ',') -cne ($empty.trace -join ',') -or $absent.coverage -or $empty.coverage){$lazy=$false}
        if($failure -cne 'e2e' -and ($absent.trace -join ',') -cne 'prepare-antiphon,launch-antiphon,settle-antiphon,prepare-session-runner,fail-session-runner'){$lazy=$false}
        if(@($priority.trace|Where-Object {$_ -like 'launch-*'}).Count -ne 0 -or $priority.coverage -or $null -eq $priority.schedule -or $priority.schedule.preparationComplete){$barrier=$false}
        if($failure -ceq 'e2e' -and (@($absent.rows|Where-Object {$_.id -ceq 'e2e' -and $_.detail -ceq 'e2e-prerequisite-failed'}).Count -ne 1 -or $absent.trace -cnotcontains 'launch-agents-pty' -or @($priority.rows|Where-Object {$_.id -ceq 'e2e'}).Count -ne 1)){$e2e=$false}
        $fault=Invoke-FFPreparationArm ('write-fault-'+$failure) 'absent' $failure -WriteFault
        if(($fault.trace -join ',') -cne ($absent.trace -join ',') -or $fault.coverage -ne $absent.coverage -or $fault.errorKind -cne $absent.errorKind){$io=$false}
    }
    Assert-FF $lazy 'lazyPreparation'
    Assert-FF $barrier 'preparationBarrier'
    Assert-FF $e2e 'e2ePreparation'
    Assert-FF $io 'diagnosticWriteFailure'
}
function Test-FFAuthorityConservation {
    # Reuse the repository's real pinned-authority fixture, with its owned local
    # Git checkout and loopback card recipient. Import definitions, never its runner.
    $tokens=$null;$errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'test-release-gate.ps1'),[ref]$tokens,[ref]$errors)
    foreach($definition in $ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){
        Set-Item -Path ('function:script:'+ $definition.Name) -Value $definition.Body.GetScriptBlock()
    }
    . (Join-Path $PSScriptRoot 'lib/release-gate.ps1')
    . (Join-Path $PSScriptRoot 'lib/release-authority.ps1')
    $here=$PSScriptRoot;$repoRoot=$repo;$ResultsDirectory=Join-Path $root 'authority-fixture';[void][IO.Directory]::CreateDirectory($ResultsDirectory)
    $script:CandidateId='rc-20260923T083000Z';$script:CandidateRef='release/rc-20260923T083000Z'
    $script:C599RepoPolicyPath=Join-Path $repo 'tests/test-execution-policy.json';$script:C599IntentId='intent-20260923T083000Z';$script:C599RunId='rc-run-1'
    $script:C599NativeSuites=@('antiphon','session-runner','pty-host','agents-pty','messaging','e2e','markdown-pdf','observability')
    $script:C599Rosters=@{client=@('lint','build','vitest');scripts=@('test-release-gate.ps1','test-nightly.ps1')}
    $script:C599ABasePolicy=$null;$script:C599PolicyClones=[Collections.Generic.List[string]]::new();$script:C599ReleaseCardId='5b0e6c1e-8f1c-4c3e-9a55-0c5990c5990a'
    $script:C599Now=[datetime]'2026-09-23T10:00:00Z';$script:C599CardStub=$null
    $oldMode=$env:ANTIPHON_RELEASE_GATE_TEST_MODE;$env:ANTIPHON_RELEASE_GATE_TEST_MODE='1'
    try{
        $auth=New-C599PublishFx
        $dir=Get-C599AuthorityDir $auth
        $before=@{};foreach($file in @(Get-ChildItem -LiteralPath $dir -File)){$before[$file.Name]=Get-NightlyHistoryDigest $file.FullName}
        $gate=Get-C599AGate $auth
        $small=New-FFSmallFixture
        $diagnostic=Invoke-FFPreparedArm $small 'authority-diagnostic' $small.resolved.members
        $after=Get-C599AGate $auth
        $unchanged=$true;foreach($file in @(Get-ChildItem -LiteralPath $dir -File)){if($before[$file.Name] -cne (Get-NightlyHistoryDigest $file.FullName)){$unchanged=$false}}
        Assert-FF ($gate.Ok -and $after.Ok -and $unchanged -and (Test-C599NoRemoteWrite $auth)) 'authorityBytes' (@($gate.Reasons)-join ',')
        $ledger=[IO.File]::ReadAllText((Join-Path $dir 'ledger.json'))|ConvertFrom-Json
        Assert-FF ($ledger.prepublicationReceipt.kind -ceq 'prepublication' -and $before.ContainsKey('execution-plan.json') -and $before.ContainsKey('discovery.json') -and $before.ContainsKey('authority.json') -and $before.ContainsKey('ledger.json')) 'authorityBinding'
    }finally{Stop-C599CardStub;Clear-C599FixtureReadOnly;$env:ANTIPHON_RELEASE_GATE_TEST_MODE=$oldMode}
}

function Test-FFPlannedGoldenBytes {
    $script:QualificationTimingRoot=Join-Path $root 'golden-actual'
    $nodes=@([pscustomobject]@{Uid='CaseUid';ClassName='Fixture.ManifestTests';Type='ManifestTests';Namespace='Fixture';Method='Run';Categories=@()})
    $group=[pscustomobject]@{id='one';classes=@('Fixture.ManifestTests');nodes=@([pscustomobject]@{Uid='caseuid'});seconds=12.5;groupFilter='/*/Fixture/ManifestTests/*'}
    [void](Write-TestPhasePlannedManifest -LogRoot $root -RunId 'golden' -Attempt '1' -Suite 'antiphon' -SourceDigest 'source' -AssemblyDigest 'assembly' -PolicyDigest 'policy' -CostFileDigest 'cost' -AssemblyId 'fixture' -Groups @($group) -DiscoveryNodes $nodes -ProfileExclusions @())
    $expected=[IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'fixtures/nightly/c1514-failed-first-planned.json'))
    $actual=[IO.File]::ReadAllBytes((Join-Path $script:QualificationTimingRoot 'groups-planned-antiphon.json'))
    Assert-FF ([Convert]::ToBase64String($expected) -ceq [Convert]::ToBase64String($actual)) 'originalManifestBytes'
}

function Test-FFSchemaKeys($Value, [string[]]$Names) {
    $actual=@($Value.PSObject.Properties.Name)
    return ($actual.Count -eq $Names.Count -and @(Compare-Object $actual $Names -CaseSensitive).Count -eq 0)
}
function Test-FFArtifactSchemas([object[]]$Arms) {
    $ok=$true
    foreach($arm in $Arms){
        if(-not (Test-FFSchemaKeys $arm.Projection @('version','runId','attempt','suite','manufacturedPassed','chunks'))){$ok=$false}
        foreach($chunk in $arm.Projection.chunks){
            if(-not (Test-FFSchemaKeys $chunk @('chunk','runId','attempt','suite','hostWallSeconds','trxMissing','manufacturedPassed','passedCount','outcomes','trx','trxDigest','exitCode','coverageComplete','testsPassed'))){$ok=$false}
        }
        $schedule=[IO.File]::ReadAllText((Join-Path $arm.Root 'dispatch-schedule.json'))|ConvertFrom-Json
        if(-not (Test-FFSchemaKeys $schedule @('format','runId','attempt','historyDigest','canonicalPlanDigest','preparationComplete','unresolved','entries'))){$ok=$false}
        foreach($entry in $schedule.entries){if(-not (Test-FFSchemaKeys $entry @('suite','chunk','canonicalOrdinal','dispatchOrdinal','priorFailure'))){$ok=$false}}
        foreach($planText in $arm.Plans.Values){
            $plan=$planText|ConvertFrom-Json
            if(-not (Test-FFSchemaKeys $plan @('version','runId','attempt','suite','sourceDigest','assemblyDigest','policyDigest','costFileDigest','assembly','requiredPartitionExact','missingIds','extraIds','groups'))){$ok=$false}
            foreach($group in $plan.groups){if(-not (Test-FFSchemaKeys $group @('chunk','classes','filter','plannedTestIds','plannedSeconds','pricingBasis','sharedManifestOrder'))){$ok=$false}}
        }
        $pointer=[IO.File]::ReadAllText((Join-Path $arm.TimingRoot 'groups-executed.journal/current.json'))|ConvertFrom-Json
        $generation=Join-Path (Join-Path $arm.TimingRoot 'groups-executed.journal') $pointer.generation
        $meta=[IO.File]::ReadAllText((Join-Path $generation 'metadata.json'))|ConvertFrom-Json
        $state=[IO.File]::ReadAllText((Join-Path $arm.TimingRoot 'groups-executed.state.json'))|ConvertFrom-Json
        $seal=[IO.File]::ReadAllText((Join-Path $generation 'seal.json'))|ConvertFrom-Json
        if(-not (Test-FFSchemaKeys $pointer @('version','timingRoot','generation','metadataDigest')) -or
           -not (Test-FFSchemaKeys $meta @('version','generation','runId','attempt','suite','source','invocationId','writerEnvironment','seedDigest','parentGeneration','unresolved','corruptionReason')) -or
           -not (Test-FFSchemaKeys $state @('version','generation','sequence','root','publicationComplete','unresolved','reason','projectionDigest','quality'))){$ok=$false}
        if($pointer.metadataDigest -cne (Get-NightlyHistoryDigest (Join-Path $generation 'metadata.json')) -or $seal.stateDigest -cne (Get-NightlyHistoryDigest (Join-Path $arm.TimingRoot 'groups-executed.state.json')) -or $meta.runId -cne 'conservation-run' -or $meta.attempt -cne '1' -or $meta.source -cne ('a'*40) -or $meta.suite -cne '' -or $meta.seedDigest -cne '' -or $meta.invocationId -cne 'conservation-run' -or $meta.unresolved -or -not $state.publicationComplete -or $state.unresolved -or $state.quality.Count -ne 0){$ok=$false}
        foreach($recordPath in @(Get-ChildItem -LiteralPath $generation -File|Where-Object {$_.Name -match '^\d{8}\.json$'})){
            $text=[IO.File]::ReadAllText($recordPath.FullName);$record=$text|ConvertFrom-Json
            if(-not (Test-FFSchemaKeys $record @('version','kind','generation','sequence','publicationId','root','freshSuiteKey','settledSuiteKey','freshChunkKey','settledChunkKey','freshRun','freshAttempt','freshHeaderSuite','digest','rowJson','settledRowJson','writerEnvironment','reason'))){$ok=$false}
            $blank=$text.Replace(('"digest":"'+$record.digest+'"'),'"digest":""')
            if((Get-TestPhaseSha256Text $blank) -cne $record.digest -or $record.generation -cne $pointer.generation -or (ConvertFrom-TestPhaseB64 $record.freshRun) -cne 'conservation-run' -or (ConvertFrom-TestPhaseB64 $record.freshAttempt) -cne '1' -or $record.freshSuiteKey -cne $record.settledSuiteKey -or $record.freshChunkKey -cne $record.settledChunkKey -or $record.writerEnvironment -cne $meta.writerEnvironment){$ok=$false}
        }
        $summary=[IO.File]::ReadAllText((Join-Path $arm.TimingRoot 'summary.json'))|ConvertFrom-Json
        if(-not (Test-FFSchemaKeys $summary @('version','complete','quality','tornLines','malformedLines','summaryAggregationBudgetMilliseconds','summaryAggregationMilliseconds','tests','sharedFixtures')) -or $summary.complete -or $summary.quality -cnotcontains 'missing-phase-unknown' -or $summary.tornLines -ne 0 -or $summary.malformedLines -ne 0 -or $summary.summaryAggregationBudgetMilliseconds -ne 5000 -or $summary.summaryAggregationMilliseconds -lt 0 -or $summary.sharedFixtures.Count -ne 0 -or $summary.tests.Count -ne 4){$ok=$false}
        $csv=[IO.File]::ReadAllLines((Join-Path $arm.TimingRoot 'tests.csv'))
        if($csv.Count -ne 5 -or $csv[0] -notlike '*testId*duration*groupId*outcome*trxOutcome*phaseOutcome*joined*incomplete*db*host*process*fs*network*fixture*overlapMs*remainderMs*droppedPhaseCount*'){$ok=$false}
        $markdown=[IO.File]::ReadAllText((Join-Path $arm.TimingRoot 'summary.md'))
        if(-not $markdown.Contains('TRX remains authoritative. Sidecars are private and are not gate inputs.') -or -not $markdown.Contains('Shared fixture rows are separate from per-test totals.')){$ok=$false}
    }
    Assert-FF $ok 'artifactSchemas'
}

function Write-FFPhaseFixture([string]$Directory, [string]$Suite, [string]$Chunk, [object[]]$Nodes, [bool]$Stale) {
    $records=[Collections.Generic.List[string]]::new()
    $run='conservation-run'; if($Stale){$run='stale-run'}
    $header=[ordered]@{recordKind='header';version=1;pid=1;nonce='fixture';frequencyHz=10000000;epochUtc='2026-10-23T00:00:00.0000000Z';runId=$run;groupId=$Chunk;attempt='1';suite=$Suite;assembly=$Suite;sourceDigest=('a'*40)}
    $records.Add(($header|ConvertTo-Json -Depth 12 -Compress))
    foreach($n in $Nodes){
        $record=[ordered]@{recordKind='complete';testId=[string]$n.Uid;className=[string]$n.ClassName;method=[string]$n.Method;args=@();startOffsetMs=0;endOffsetMs=100;outcome='Passed';phases=@();processes=@();exclusive=[ordered]@{db=1;host=2;process=3;fs=4;network=5;fixture=6};overlapMs=7;remainderMs=8;childUnionMs=0;droppedPhaseCount=0;shared=$false;incomplete=$false}
        $records.Add(($record|ConvertTo-Json -Depth 12 -Compress))
    }
    if($Suite -ceq 'session-runner' -and $Chunk -ceq 'Priority'){
        $records.Add((([ordered]@{recordKind='fixture';fixtureId='shared-fixture';exclusive=[ordered]@{db=11;host=12;process=13;fs=14;network=15;fixture=16}})|ConvertTo-Json -Depth 12 -Compress))
    }
    [void][IO.Directory]::CreateDirectory($Directory)
    [IO.File]::WriteAllLines((Join-Path $Directory ('cases-'+$Suite+'-'+$Chunk+'.jsonl')),$records.ToArray(),[Text.UTF8Encoding]::new($false))
}
function Test-FFPhaseBindings($Fixture) {
    $ok=$true
    foreach($variant in @('phase','phase-stale')){
        $left=Invoke-FFPreparedArm $Fixture ($variant+'-canonical') @() $variant
        $right=Invoke-FFPreparedArm $Fixture ($variant+'-priority') $Fixture.resolved.members $variant
        $summaries=@()
        foreach($arm in @($left,$right)){
            $summary=[IO.File]::ReadAllText((Join-Path $arm.TimingRoot 'summary.json'))|ConvertFrom-Json
            $summaries += $summary
            if($summary.tests.Count -ne 4 -or $summary.sharedFixtures.Count -ne 1 -or $summary.sharedFixtures[0].fixtureId -cne 'shared-fixture' -or $summary.sharedFixtures[0].db -ne 11 -or $summary.sharedFixtures[0].fixture -ne 16 -or $summary.tornLines -ne 0 -or $summary.malformedLines -ne 0){$ok=$false}
            foreach($test in $summary.tests){
                $isStale=($variant -ceq 'phase-stale' -and $test.testId -ceq 'Fixture.antiphon.Priority.Run')
                if($isStale){if(-not $test.incomplete -or $test.joined){$ok=$false};continue}
                if(-not $test.joined -or $test.incomplete -or $test.outcome -cne 'Passed' -or $test.trxOutcome -cne 'Passed' -or $test.phaseOutcome -cne 'Passed' -or $test.db -ne 1 -or $test.host -ne 2 -or $test.process -ne 3 -or $test.fs -ne 4 -or $test.network -ne 5 -or $test.fixture -ne 6 -or $test.overlapMs -ne 7 -or $test.remainderMs -ne 8 -or $test.droppedPhaseCount -ne 0){$ok=$false}
            }
            if($variant -ceq 'phase' -and (-not $summary.complete -or $summary.quality.Count -ne 0)){$ok=$false}
            if($variant -ceq 'phase-stale' -and ($summary.complete -or $summary.quality -cnotcontains 'stale-input-not-joined' -or $summary.quality -cnotcontains 'missing-phase-unknown')){$ok=$false}
            $bySuite=@{}
            foreach($s in $Fixture.suites){$bySuite[$s.id]=$true}
            foreach($file in @(Get-ChildItem -LiteralPath $arm.TimingRoot -Filter 'cases-*.jsonl' -File)){
                $header=[IO.File]::ReadLines($file.FullName)|Select-Object -First 1|ConvertFrom-Json
                if(-not $bySuite.ContainsKey($header.suite) -or $header.sourceDigest -cne ('a'*40) -or $header.assembly -cne $header.suite -or $header.attempt -cne '1'){$ok=$false}
            }
        }
        $l=@($summaries[0].tests|Sort-Object testId)|ConvertTo-Json -Depth 12 -Compress
        $r=@($summaries[1].tests|Sort-Object testId)|ConvertTo-Json -Depth 12 -Compress
        if($l -cne $r -or [IO.File]::ReadAllText((Join-Path $left.TimingRoot 'tests.csv')) -cne [IO.File]::ReadAllText((Join-Path $right.TimingRoot 'tests.csv')) -or [IO.File]::ReadAllText((Join-Path $left.TimingRoot 'summary.md')) -cne [IO.File]::ReadAllText((Join-Path $right.TimingRoot 'summary.md'))){$ok=$false}
    }
    Assert-FF $ok 'phaseBindings'
}

function New-FFLoopFixture([switch]$Full) {
    if ($Full) { return New-FFGlobalFixture }
    $small = New-FFSmallFixture
    $history = [pscustomobject]@{format='antiphon-nightly-prior-failures-v1';sourceRunId='old';sourceAttempt='1';sourceSha=('a'*40);profile='rc';platform='windows';policyHash=('b'*64);createdAtUtc=[datetime]::UtcNow.ToString('o');complete=$true;evidence=@();members=@();fallbackChunks=@();membershipComplete=$true;unresolved=@()}
    foreach ($suite in $small.suites) {
        $history.members += [pscustomobject]@{suite=$suite.id;className=$suite.chunks[-1].classes[0];reasons=@('Failed');observations=@()}
    }
    return [pscustomobject]@{suites=$small.suites;history=$history}
}

function New-FFLoopHistory($Fixture) {
    $dir=Join-Path $root ('closed-'+[guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($dir)
    $rows=@()
    foreach ($suite in $Fixture.suites) {
        $members=@($Fixture.history.members | Where-Object {$_.suite -ceq $suite.id})
        $trx=Join-Path $dir ($suite.id+'-old.trx')
        $failures=@(foreach ($member in $members) {
            $n=@($suite.nodes | Where-Object {(Get-NightlyBareClassIdentity $_.ClassName) -ceq $member.className})[0]
            [pscustomobject]@{Id=$n.Uid;ClassName=$n.ClassName;MethodName=$n.Method;Outcome='Failed'}
        })
        Write-C487Trx $trx $failures
        $rows += [ordered]@{id=$suite.id;chunk='old';trx=$trx;exitCode=2;result='FAIL';timedOut=$false}
    }
    Write-FFJson (Join-Path $dir 'summary.json') ([ordered]@{runId='old';attempt='1';sha=('a'*40);profile='rc';platform='windows';policyHash=('b'*64);completedAt=[datetime]::UtcNow.ToString('o');outcome='TESTS';suites=$rows;selectedSuites=@($Fixture.suites.id)})
    $path=Join-Path $root ('history-'+[guid]::NewGuid().ToString('N')+'.json')
    $export=Export-NightlyPriorFailures $dir $path -PassThru
    return [pscustomobject]@{Path=$path;Digest=$export.Digest;History=$export.History}
}

function Invoke-FFActualLoop {
    param($Fixture, [string]$Name, $History, [string]$Variant='pass', [switch]$NoExclusions)
    $dir=Join-Path $root $Name; [void][IO.Directory]::CreateDirectory($dir)
    $p=Read-NightlyExecutionPolicy -RepoRoot $repo
    $p.Object.profiles.rc.requiredSuites=@($Fixture.suites.id)
    $p.Object | Add-Member -NotePropertyName chunks -NotePropertyValue ([pscustomobject]@{}) -Force
    foreach ($suite in $Fixture.suites) {
        $chunkRows=@(foreach($chunk in $suite.chunks){[pscustomobject]@{id=$chunk.id;classes=@($chunk.classes)}})
        if ($Fixture.PSObject.Properties.Name -contains 'replay') { $chunkRows=@([pscustomobject]@{id='all';classes=@()}) }
        $p.Object.chunks | Add-Member -NotePropertyName $suite.id -NotePropertyValue $chunkRows
    }
    # Retain actual Windows exclusions for the 19,012-node replay. The small
    # interleaving witness deliberately has NO exclusions in its first suite.
    if (-not ($Fixture.PSObject.Properties.Name -contains 'replay')) { $p.Object.profiles.rc.exclusions=@() }
    if (-not $NoExclusions) {
        foreach($suite in $Fixture.suites){$p.Object.profiles.rc.exclusions += [pscustomobject]@{suite=$suite.id;class='Fixture.Never';reason='fixture';owner='CARD-1514'}}
    }
    $p.Object.policyHash=Get-NightlyPolicyHash $p.Object
    $policyFile=Join-Path $dir 'policy.json'; Write-FFJson $policyFile $p.Object
    $script:FFLoop=[pscustomobject]@{Fixture=$Fixture;Variant=$Variant;Launches=[Collections.Generic.List[object]]::new();Discovery=[Collections.Generic.List[string]]::new();Builds=0;Active=0;Max=0;SettledBeforeReturn=$false}
    $seams=Join-Path $dir 'seams.ps1'
    [IO.File]::WriteAllText($seams, @'
$NightlySeams=@{
    HostPlatform={'windows'}
    E2EPrerequisites={return [pscustomobject]@{Ok=$true;Rows=@()}}
    StartProcess={
        param($FilePath,$ArgumentList,$WorkingDirectory,$TimeoutMilliseconds,$Environment)
        $sid='';$cid=''
        if($Environment){$sid=[string]$Environment['ANTIPHON_TEST_PHASE_SUITE'];$cid=[string]$Environment['ANTIPHON_TEST_PHASE_GROUP_ID']}
        $diag=Get-FFArgument $ArgumentList '--diagnostic-output-directory'
        if($ArgumentList -contains '--list-tests') {
            $sid=(Split-Path -Leaf $diag) -replace '-profile-discovery$','' -replace '-.*-discovery$',''
            foreach($s in $script:FFLoop.Fixture.suites){if((Split-Path -Leaf $diag).StartsWith($s.id+'-')){$sid=$s.id;break}}
            $s=@($script:FFLoop.Fixture.suites|Where-Object {$_.id -ceq $sid})[0]
            $script:FFLoop.Discovery.Add($sid)
            Write-FFDiagnostic $diag @($s.nodes) 'Discovered'
        } elseif($sid) {
            $script:FFLoop.Active++;$script:FFLoop.Max=[math]::Max($script:FFLoop.Max,$script:FFLoop.Active)
            $s=@($script:FFLoop.Fixture.suites|Where-Object {$_.id -ceq $sid})[0]
            $planPath=Join-Path $script:QualificationTimingRoot ('groups-planned-'+$sid+'.json')
            $plan=[IO.File]::ReadAllText($planPath)|ConvertFrom-Json
            $group=@($plan.groups|Where-Object {$_.chunk -ceq $cid})[0]
            $nodes=@($s.nodes|Where-Object {@($group.plannedTestIds) -ccontains $_.Uid})
            if($nodes.Count -eq 0){$nodes=@($s.nodes|Where-Object {@($group.classes) -ccontains $_.ClassName})}
            $script:FFLoop.Launches.Add([pscustomobject]@{suite=$sid;chunk=$cid;key=($sid+'/'+$cid);host=$FilePath;nodes=@($nodes.Uid);filter=(Get-FFArgument $ArgumentList '--treenode-filter')})
            $special=($sid -ceq 'antiphon' -and @($group.classes|Where-Object {$_ -clike '*Priority*'}).Count -gt 0)
            $state='Passed';$code=0;$timeout=$false
            if($special -and $script:FFLoop.Variant -ceq 'failure'){$state='Failed';$code=2}
            if($special -and $script:FFLoop.Variant -ceq 'timeout'){$timeout=$true;$code=1}
            $rows=@(foreach($n in $nodes){[pscustomobject]@{Id=$n.Uid;ClassName=$n.ClassName;MethodName=$n.Method;Outcome=$state}})
            $trx=Join-Path (Get-FFArgument $ArgumentList '--results-directory') (Get-FFArgument $ArgumentList '--report-trx-filename')
            Write-C487Trx $trx $rows;Write-FFDiagnostic $diag $nodes $state
            $existing=Join-Path $script:QualificationTimingRoot 'groups-executed.json'
            if([IO.File]::Exists($existing)){$script:FFLoop.SettledBeforeReturn=$true}
            $script:FFLoop.Active--
            return [pscustomobject]@{ExitCode=$code;TimedOut=$timeout;ChildrenExited=$true;CleanupComplete=$true;JobEmptyObserved=$true;LogText=''}
        } elseif($ArgumentList -contains 'Antiphon.sln') {$script:FFLoop.Builds++}
        return [pscustomobject]@{ExitCode=0;TimedOut=$false;ChildrenExited=$true;CleanupComplete=$true;JobEmptyObserved=$true;LogText=''}
    }
}
'@)
    $args=@{RepoRoot=$repo;LogRoot=$dir;TimingRoot=(Join-Path $dir 'timing');RunId='actual-run';Attempt=1;Sha=('a'*40);GitRef='origin/rc/2026-10-27';Profile='rc';PolicyPath=$policyFile;SeamsPath=$seams;AllowSharedTree=$true}
    if($History){$args.PriorFailuresPath=$History.Path;$args.PriorFailuresDigest=Get-NightlyHistoryDigest $History.Path}
    $result=& (Join-Path $PSScriptRoot 'nightly-tests.ps1') @args -PassThru
    $summary=[IO.File]::ReadAllText((Join-Path $dir 'summary.json'))|ConvertFrom-Json
    $schedule=[IO.File]::ReadAllText((Join-Path $dir 'dispatch-schedule.json'))|ConvertFrom-Json
    $projection=[IO.File]::ReadAllText((Join-Path $dir 'timing/groups-executed.json'))|ConvertFrom-Json
    $prepared=@(foreach($s in $Fixture.suites){$plan=[IO.File]::ReadAllText((Join-Path $dir ('timing/groups-planned-'+$s.id+'.json')))|ConvertFrom-Json;[pscustomobject]@{id=$s.id;nodes=$s.nodes;exclusions=@();chunks=@(foreach($g in $plan.groups){[pscustomobject]@{id=$g.chunk;classes=$g.classes}})}})
    return [pscustomobject]@{Result=$result;Summary=$summary;Schedule=$schedule;Projection=$projection;Trace=$script:FFLoop;Prepared=$prepared;Root=$dir}
}

function Test-ActualLoopFollowsSchedule {
    $fx=New-FFLoopFixture -Full
    $h=New-FFLoopHistory $fx
    $arm=Invoke-FFActualLoop $fx 'actual-full' $h
    $resolved=Resolve-NightlyPriorFailures $h.History $arm.Prepared Full
    $expected=New-NightlyDispatchSchedule $arm.Prepared $resolved.members
    $actual=@($arm.Trace.Launches.key);$wanted=@($expected.entries|ForEach-Object {$_.suite+'/'+$_.chunk})
    Assert-FF (($actual -join ',') -ceq ($wanted -join ',') -and $expected.entries[0].priorFailure -and @($expected.entries|Where-Object {-not $_.priorFailure}).Count -gt 0) 'actualTrace'
    $canonical=@(foreach($s in $arm.Prepared){foreach($c in $s.chunks){$s.id+'/'+$c.id}})
    Assert-FF ($actual.Count -eq $canonical.Count -and @($actual|Select-Object -Unique).Count -eq $canonical.Count -and @(Compare-Object $actual $canonical -CaseSensitive).Count -eq 0 -and $arm.Trace.Max -eq 1 -and $fx.replay.nodes.Count -eq 19012) 'permutation'
    Assert-FF ($arm.Trace.Builds -eq 1 -and $arm.Trace.Discovery.Count -eq $fx.suites.Count -and @($arm.Trace.Discovery|Select-Object -Unique).Count -eq $fx.suites.Count) 'oneBuildDiscovery'
    Assert-FF ($arm.Schedule.historyDigest -ceq (Get-NightlyHistoryDigest $h.Path) -and [IO.File]::Exists((Join-Path $arm.Root 'prior-failures.json'))) 'historyBinding'
    Assert-FF ($arm.Result.testsPassed -and $arm.Result.coverageComplete -and $arm.Result.ExitCode -eq 0 -and $arm.Summary.succeeded) 'allChunksVerdict' ($arm.Summary.reasons -join ',')
    Assert-FF ($arm.Projection.chunks.Count -eq $canonical.Count -and -not $arm.Trace.SettledBeforeReturn -and $arm.Projection.chunks[-1].chunk -ceq $arm.Trace.Launches[-1].chunk) 'finalProjection'
    $bad=Join-Path $root 'malformed.json';[IO.File]::WriteAllText($bad,'{')
    $fallback=Invoke-FFActualLoop (New-FFLoopFixture) 'fallback' ([pscustomobject]@{Path=$bad})
    Assert-FF ($fallback.Result.testsPassed -and $fallback.Schedule.historyFallbackReason -and (@($fallback.Trace.Launches.key)-join ',') -ceq (@($fallback.Schedule.entries|ForEach-Object {$_.suite+'/'+$_.chunk})-join ',')) 'fallback'
    $entries=@(foreach($sid in @('A','B')){foreach($id in 1..5){[pscustomobject]@{suite=$sid;chunk=[string]$id}}})
    $previous=[pscustomobject]@{sha=('a'*40);completedAt='2026-10-31T01:00:00Z';suites=@([pscustomobject]@{id='A';chunk='1';result='pass';exitCode=0},[pscustomobject]@{id='A';chunk='3';result='pass';exitCode=0},[pscustomobject]@{id='B';chunk='2';result='FAIL';exitCode=1})}
    $order=Resolve-NightlyPreviousRunOrder $entries $previous ('a'*40) ([datetime]'2026-10-31T06:00:00Z')
    Assert-FF ((@($order.entries|ForEach-Object {$_.suite+$_.chunk})-join ',') -ceq 'B2,A4,A5,A2,B1,B3,B4,B5,A1,A3' -and $order.reason -ceq 'previous-run-order') 'progressOrder'
}

function Test-JournalCustodySurvivesInterleaving {
    $fx=New-FFLoopFixture;$h=New-FFLoopHistory $fx
    $a=Invoke-FFActualLoop $fx 'journal' $h -Variant failure -NoExclusions
    $keys=@($a.Trace.Launches.key)
    Assert-FF (($keys -join ',') -ceq 'antiphon/Priority,session-runner/Priority,antiphon/Ordinary,session-runner/Ordinary') 'interleaving'
    Assert-FF ($a.Trace.Discovery.Count -eq 2 -and @($a.Trace.Launches|Where-Object {$_.nodes.Count -ne 1 -or $_.host -notlike ('*'+$(if($_.suite -ceq 'antiphon'){'Antiphon.Tests'}else{'Antiphon.SessionRunner.Tests'})+'*')}).Count -eq 0) 'hostDiscovery'
    $prefix=$true;$seen=$false
    foreach($row in $a.Projection.chunks){if([bool]$row.testsPassed -ne (-not $seen)){$prefix=$false};if($row.suite -ceq 'antiphon' -and $row.chunk -ceq 'Priority'){$seen=$true}}
    Assert-FF ($prefix -and -not $a.Result.testsPassed -and $a.Result.ExitCode -eq 1 -and -not $a.Summary.succeeded) 'prefixState'
    $journal=Join-Path $a.Root 'timing/groups-executed.journal';$pointer=[IO.File]::ReadAllText((Join-Path $journal 'current.json'))|ConvertFrom-Json
    $generation=Join-Path $journal $pointer.generation;$records=@(Get-ChildItem -LiteralPath $generation -Filter '????????.json'|Sort-Object Name)
    Assert-FF ($records.Count -eq 4 -and (@($a.Projection.chunks|ForEach-Object {$_.suite+'/'+$_.chunk}) -join ',') -ceq ($keys -join ',') -and $a.Projection.runId -ceq 'actual-run' -and $a.Projection.attempt -ceq '1') 'journalBindings'
    Assert-FF ([IO.File]::Exists((Join-Path $generation 'seal.json')) -and -not $a.Trace.SettledBeforeReturn -and @(Get-ChildItem -LiteralPath $journal -Directory).Count -eq 1) 'oneContextClose'
    $timeout=Invoke-FFActualLoop $fx 'timeout-priority' $h -Variant timeout
    $plain=Invoke-FFActualLoop $fx 'timeout-canonical' $null -Variant timeout
    Assert-FF ($timeout.Summary.outcome -ceq 'TIMEOUT' -and $plain.Summary.outcome -ceq 'TIMEOUT' -and -not $timeout.Summary.succeeded -and -not $plain.Summary.succeeded) 'timeoutPermutation'
    # Existing M1/M2 accessor must refuse corruption instead of manufacturing completion.
    $first=$records[0].FullName;$bytes=[IO.File]::ReadAllText($first)
    [IO.File]::WriteAllText($first,'{')
    $read=Read-TestPhaseExecutedPrefix -LogRoot $a.Root -TimingRoot (Join-Path $a.Root 'timing')
    Assert-FF ($read.Refused -or $read.Unresolved -or -not $read.PublicationComplete) 'custody'
    [IO.File]::WriteAllText($first,$bytes)
    Assert-FF ($a.Projection.suite -ceq $a.Trace.Launches[0].suite -and (@($a.Summary.suites|ForEach-Object {$_.id+'/'+$_.chunk})-join ',') -ceq ($keys-join ',') -and $a.Summary.suiteTimeline.Count -eq 2 -and [IO.File]::Exists((Join-Path $a.Root 'timing/summary.json'))) 'fieldPolicies'
}

switch -Exact ($Case) {
    'ActualLoopFollowsSchedule' { Test-ActualLoopFollowsSchedule; $expected=8 }
    'JournalCustodySurvivesInterleaving' { Test-JournalCustodySurvivesInterleaving; $expected=8 }
    'ConservationDiagnostics' { Test-FullBytesAndCoverageConserved -DiagnosticsOnly; $expected=18 }
    'FullBytesAndCoverageConserved' { Test-FullBytesAndCoverageConserved; $expected=23 }
    'GlobalReplayPriority' { Test-GlobalReplayPriority; $expected=10 }
    'EvidenceUnion' { Test-EvidenceUnion; $expected=10 }
    'RefusesIncompleteEvidence' { Test-RefusesIncompleteEvidence; $expected=47 }
    'OrdinaryFailedChunk' { Test-OrdinaryFailedChunk; $expected=4 }
    default { throw ('unknown case: '+$Case) }
}
$gitWatch=[Diagnostics.Stopwatch]::StartNew()
$sourceSha=git -C $repo rev-parse HEAD
$phases.gitSeconds=$gitWatch.Elapsed.TotalSeconds
$phases.wallSeconds=$clock.Elapsed.TotalSeconds
$load='unknown'
if ([IO.Path]::DirectorySeparatorChar -eq '/' -and [IO.File]::Exists('/proc/loadavg')) { $load=[IO.File]::ReadAllText('/proc/loadavg').Trim() }
Write-FFJson (Join-Path $root 'performance.json') ([ordered]@{ case=$Case; source=$sourceSha; load=$load; assertions=$script:FFAssertions.ToArray(); operations=$script:FFOperations.ToArray(); hostName=[Environment]::MachineName; platform=[Environment]::OSVersion.ToString(); category='qualification-tooling'; phases=$phases; docker='not-used'; database='not-used'; appHost='not-used'; http='not-used'; teardown='owned ScriptHarness cleanup; see host receipt'; unclassified='wall minus measured spans' })
# ScriptHarness owns and removes its temporary ResultsDirectory. Retain the
# synthetic inputs, digests and measured phases before returning to that owner.
$archiveWatch=[Diagnostics.Stopwatch]::StartNew()
$archive=Join-Path $repo ('.antiphon/failfirst/'+$sourceSha+'/portable/'+$Case+'-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($archive)
$inventory=[Collections.Generic.List[object]]::new()
foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File -Force)) {
    $relative=$file.FullName.Substring($root.Length).TrimStart('\','/')
    if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $file.LinkType) {
        $inventory.Add([ordered]@{path=$relative;kind='fixture-link';copied=$false})
        continue
    }
    if ($relative -ceq 'performance.json') { continue }
    $destination=Join-Path $archive $relative
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
    [IO.File]::Copy($file.FullName,$destination,$false)
    $inventory.Add([ordered]@{path=$relative;kind='regular';copied=$true;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()})
}
Write-FFJson (Join-Path $archive 'inventory.json') ([ordered]@{source=$sourceSha;case=$Case;testClass=$(if($Case -cin @('EvidenceUnion','RefusesIncompleteEvidence','OrdinaryFailedChunk')){'Antiphon.Tests.Scripts.RcFailureHistoryTests'}else{'Antiphon.Tests.Scripts.RcFailedFirstPlannerTests'});files=$inventory.ToArray()})
$performancePath=Join-Path $root 'performance.json'
$performance=[IO.File]::ReadAllText($performancePath) | ConvertFrom-Json
$performance.phases | Add-Member -NotePropertyName archiveSeconds -NotePropertyValue $archiveWatch.Elapsed.TotalSeconds
$performance.phases.wallSeconds=$clock.Elapsed.TotalSeconds
$performance | Add-Member -NotePropertyName retainedEvidence -NotePropertyValue $archive
Write-FFJson $performancePath $performance
[IO.File]::Copy($performancePath,(Join-Path $archive 'performance.json'),$false)
Write-Host ('FF EVIDENCE '+$archive)
Complete-C487Harness -ResultsDirectory $root -ExpectedRows $expected
