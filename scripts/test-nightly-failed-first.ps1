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
function Write-FFJson($Path, $Value) { [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 40), [Text.UTF8Encoding]::new($false)) }
function Assert-FF([bool]$Ok, [string]$Name, [string]$Detail = '') { Assert-C487 $Ok ('FF ' + $Case + '.' + $Name) $Detail }
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
    Write-FFJson (Join-Path $root 'replay.json') ([ordered]@{ nodes=$nodes.Count; chunks=$chunks.Count; listingDigest=(Get-FileHash -LiteralPath $listing).Hash; costDigest=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'lib/nightly-class-costs.json')).Hash; policyHash=$policy.Hash })
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
    $classes = @($names | ForEach-Object { 'Antiphon.Tests.' + $_ }) + @('Antiphon.SessionRunner.Tests.GrokRulesHttpAcceptanceTests','Antiphon.Agents.Pty.Tests.FakeClaudeContractTests','Antiphon.E2E.DispatchBaseWarningDeliveryE2ETests')
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
    for ($i=0; $i -lt 10; $i++) {
        $type = if ($i -lt 8) { $extra[$i] } else { 'RecoveredTests' }
        $state = if ($i -lt 7) { 'Error' } elseif ($i -lt 9) { 'InProgress' } else { 'Passed' }
        $uid = if ($i -ge 8) { 'recovered' } else { 'diag-'+$i }
        $method = if ($i -eq 7) { 'concurrent_callbacks_append_each_line_once_without_overlap' } else { 'Case'+$i }
        if ($i -eq 9) { $method='Case8' }
        $diag += 'TestNodeUpdateMessage { TestNode = TestNode { Uid = TestNodeUid { Value = '+$uid+' }, Properties = [ TestMethodIdentifierProperty { Namespace = Antiphon.Tests.Checkpoints, TypeName = '+$type+', MethodName = '+$method+', MethodArity = 0 }, '+$state+'TestNodeStateProperty { Explanation =  } ] } }' + "`n"
        if ($i -lt 8) { $nodesBySuite.antiphon += New-FFNode ('Antiphon.Tests.Checkpoints.'+$type) $method $uid }
    }
    [void][IO.Directory]::CreateDirectory((Join-Path $run 'antiphon-all-group-105-execution'))
    [IO.File]::WriteAllText((Join-Path $run 'antiphon-all-group-105-execution/invocation.diag'),$diag)
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
switch -Exact ($Case) {
    'EvidenceUnion' { Test-EvidenceUnion; $expected=10 }
    default { throw ('unknown case: '+$Case) }
}
$phases.wallSeconds=$clock.Elapsed.TotalSeconds
Write-FFJson (Join-Path $root 'performance.json') ([ordered]@{ case=$Case; source=(git -C $repo rev-parse HEAD); hostName=[Environment]::MachineName; platform=[Environment]::OSVersion.ToString(); category='qualification-tooling'; phases=$phases; docker='not-used'; database='not-used'; appHost='not-used'; http='not-used'; teardown='owned ScriptHarness cleanup; see host receipt'; unclassified='wall minus measured spans' })
Complete-C487Harness -ResultsDirectory $root -ExpectedRows $expected
