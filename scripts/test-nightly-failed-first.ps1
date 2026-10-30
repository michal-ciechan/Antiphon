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
        $suites += [ordered]@{ id=$suite; chunk='failed'; trx=$name; exitCode=1; timedOut=$false }
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
    $suites += [ordered]@{ id='antiphon'; chunk='all-group-105'; trx='antiphon-all-group-105.trx'; execution='antiphon-all-group-105-execution/invocation.diag'; timedOut=$true; exitCode=1 }
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
        'result' { $row.result='FAIL' }
        'detail' { $row.detail='missing-terminal-accounting' }
    }
    $summary=[ordered]@{runId='accounting';attempt='1';completedAt='2026-10-29T00:00:00Z';sha=('a'*40);profile='rc';platform='windows';policyHash=('b'*64);suites=@($row);timingRoot='timing'}
    Write-FFJson (Join-Path $run 'summary.json') $summary
    Write-FFJson (Join-Path $run 'antiphon-profile.discovery.json') (ConvertTo-NightlyDiscoveryDocument -Nodes $nodes -AssemblyHash 'fixture-assembly')
    Write-FFJson (Join-Path $run 'timing/groups-planned-antiphon.json') ([ordered]@{version=1;suite='antiphon';runId='accounting';attempt='1';sourceDigest=('a'*40);policyDigest=('b'*64);assemblyDigest='fixture-assembly';groups=@([ordered]@{chunk='all';classes=@($nodes.ClassName);plannedTestIds=@($nodes.Uid)})})
    $defs=''; $results=''
    foreach ($n in $nodes) {
        if ($Variant -ceq 'short' -and $n.Uid -ceq 'second') { continue }
        $defs += '<UnitTest id="'+$n.Uid+'"><TestMethod className="'+$n.ClassName+'" name="Run" /></UnitTest>'
        $results += '<UnitTestResult testId="'+$n.Uid+'" outcome="Passed" />'
    }
    if ($Variant -cne 'missing') { [IO.File]::WriteAllText((Join-Path $run 'antiphon-all.trx'),('<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><TestDefinitions>'+$defs+'</TestDefinitions><Results>'+$results+'</Results></TestRun>')) }
    $exp=Export-NightlyPriorFailures $run (Join-Path $root ('accounting-'+$Variant+'.json')) -PassThru
    $history=Read-NightlyPriorFailures $exp.Path $exp.Digest
    $suite=@([pscustomobject]@{id='antiphon';nodes=$nodes;chunks=@([pscustomobject]@{id='all';classes=@($nodes.ClassName);nodes=$nodes});exclusions=@()})
    $full=Resolve-NightlyPriorFailures $history $suite Full
    $reason=@{exit='summary-nonzero-exit';result='summary-failed-result';detail='summary-detail';short='short-trx';missing='missing-trx'}[$Variant]
    return (@($full.unresolved).Count -gt 0 -and -not $history.membershipComplete -and @($history.fallbackChunks | Where-Object { $_.reason.Split(',') -ccontains $reason }).Count -eq 1 -and (Test-FFRefusal { Resolve-NightlyPriorFailures $history $suite PriorFailureSweep } 'prior-failure-sweep-unresolved'))
}
function Test-RefusesIncompleteEvidence {
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
    $control=Resolve-NightlyPriorFailures $h $current PriorFailureSweep
    Assert-FF (@($control.members).Count -ge 34 -and @($control.unresolved).Count -eq 0 -and $h.sourceSha -cne ('c'*40) -and $h.policyHash -cne ('d'*64)) 'validControl'
    foreach ($variant in 'exit','result','detail','short') { Assert-FF (Test-FFChunkAccounting $variant) ('chunk-'+$variant) }
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
    $policyRows=@(Get-NightlyProfileExclusions -PolicyObject $policy.Object -Profile rc -SuiteId antiphon -Platform linux)
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
    Assert-FF (@($r.members[0].requiredUids).Count -eq 1 -and @($r.excludedUids).Count -eq 3 -and $r.exclusionProfile -ceq 'rc' -and $r.policyDigest -ceq $policy.Hash -and $r.exclusionPolicyFileDigest -ceq (Get-NightlyHistoryDigest $policy.Path) -and $r.exclusionPlatform -ceq 'linux' -and @($r.excludedUids | Where-Object { $_.reason -cne $methodRow.Reason -or $_.owner -cne $methodRow.Owner }).Count -eq 0) 'methodExclusions'
    Assert-FF (Test-FFRefusal { Resolve-NightlyPriorFailures -History $exHistory -Suites $exSuite -QualificationMode PriorFailureSweep -ExclusionContext ([pscustomobject]@{profile='nightly'}) } 'partial-exclusion-profile-mismatch') 'exclusionMismatch'
    $noChunk=Copy-FFObject $h
    foreach ($m in $noChunk.members) { foreach ($o in $m.observations) { $o.chunk='' } }
    $r=Resolve-NightlyPriorFailures $noChunk $current PriorFailureSweep
    Assert-FF (@($r.members | Where-Object { $_.className -ceq $name }).Count -eq 1) 'noOldChunk'
    $empty=Copy-FFObject $h; $empty.members=@(); $empty.unresolved=@(); $empty.fallbackChunks=@()
    Assert-FF (@((Resolve-NightlyPriorFailures $empty $current Full).members).Count -eq 0 -and (Test-FFRefusal { Resolve-NightlyPriorFailures $empty $current PriorFailureSweep } 'empty-history')) 'empty'
    Assert-FF ((Test-FFRefusal { Read-NightlyPriorFailures $path ('0'*64) } 'history-digest-mismatch') -and (Test-FFRefusal { Read-NightlyPriorFailures -Path '' } 'empty-history-path')) 'digest'
    $bad=Copy-FFObject $h; $bad.format='unsupported'; $badPath=Join-Path $root 'bad.json'; Write-FFJson $badPath $bad
    Assert-FF (Test-FFRefusal { Read-NightlyPriorFailures $badPath } 'history-schema') 'schema'
    $bad=Copy-FFObject $h; $bad.sourceSha='c'*40; Write-FFJson $badPath $bad
    Assert-FF (Test-FFRefusal { Read-NightlyPriorFailures $badPath } 'history-provenance') 'provenance'
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
switch -Exact ($Case) {
    'EvidenceUnion' { Test-EvidenceUnion; $expected=10 }
    'RefusesIncompleteEvidence' { Test-RefusesIncompleteEvidence; $expected=32 }
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
Write-FFJson (Join-Path $archive 'inventory.json') ([ordered]@{source=$sourceSha;case=$Case;testClass='Antiphon.Tests.Scripts.RcFailureHistoryTests';files=$inventory.ToArray()})
$performancePath=Join-Path $root 'performance.json'
$performance=[IO.File]::ReadAllText($performancePath) | ConvertFrom-Json
$performance.phases | Add-Member -NotePropertyName archiveSeconds -NotePropertyValue $archiveWatch.Elapsed.TotalSeconds
$performance.phases.wallSeconds=$clock.Elapsed.TotalSeconds
$performance | Add-Member -NotePropertyName retainedEvidence -NotePropertyValue $archive
Write-FFJson $performancePath $performance
[IO.File]::Copy($performancePath,(Join-Path $archive 'performance.json'),$false)
Write-Host ('FF EVIDENCE '+$archive)
Complete-C487Harness -ResultsDirectory $root -ExpectedRows $expected
