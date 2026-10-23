using System.Diagnostics;
using System.Text.Json;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TestPhaseSupervisorTests
{
    [Test]
    [Timeout(180_000)]
    public async Task C1514T_PlannedManifestAndEnvironment(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        var pack = await RunPwsh(PackScript, cancellationToken, C1514Support.RepoRoot(), root);
        pack.ExitCode.ShouldBe(0, pack.Text);
        var planned = Path.Combine(root, "timing", "groups-planned.json");
        File.Exists(planned).ShouldBeTrue("missing planned-file");

        using var packDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "pack.json")));
        var packRoot = packDoc.RootElement;
        packRoot.GetProperty("plansEqual").GetBoolean().ShouldBeTrue();
        packRoot.GetProperty("sampleAlone").GetBoolean().ShouldBeTrue();
        packRoot.GetProperty("maxClasses").GetInt32().ShouldBeLessThanOrEqualTo(30);
        packRoot.GetProperty("hasOperandCapGroup").GetBoolean().ShouldBeTrue();
        packRoot.GetProperty("hugeSeconds").GetDouble().ShouldBeGreaterThan(2400d);

        using var manifestDoc = JsonDocument.Parse(File.ReadAllText(planned));
        var manifest = manifestDoc.RootElement;
        manifest.GetProperty("requiredPartitionExact").GetBoolean().ShouldBeTrue("required-partition-exact");
        manifest.GetProperty("runId").GetString().ShouldBe("run-v7");
        var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in packRoot.GetProperty("requiredIds").EnumerateArray())
            required.Add(id.GetString()!);
        required.ShouldNotContain("leaf-skip");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var hugeBasis = "";
        var sawSampleExtra = false;
        foreach (var group in manifest.GetProperty("groups").EnumerateArray())
        {
            var ids = group.GetProperty("plannedTestIds");
            ids.ValueKind.ShouldBe(JsonValueKind.Array);
            double? expectedSeconds = null;
            foreach (var packed in packRoot.GetProperty("groups").EnumerateArray())
            {
                if (packed.GetProperty("id").GetString() == group.GetProperty("chunk").GetString())
                    expectedSeconds = packed.GetProperty("seconds").GetDouble();
            }

            expectedSeconds.ShouldNotBeNull();
            group.GetProperty("plannedSeconds").GetDouble().ShouldBe(expectedSeconds!.Value);
            var filter = group.GetProperty("filter").GetString() ?? "";
            filter.Contains("(Sample*)", StringComparison.Ordinal).ShouldBeFalse();
            if (filter.Contains("(SampleExtra*)", StringComparison.Ordinal))
                sawSampleExtra = true;
            var classes = string.Join(",", group.GetProperty("classes").EnumerateArray().Select(item => item.GetString()));
            if (classes.Contains("Antiphon.Tests.C1514.Huge", StringComparison.Ordinal))
                hugeBasis = group.GetProperty("pricingBasis").GetString() ?? "";
            var ordered = new List<string>();
            foreach (var id in ids.EnumerateArray())
            {
                var text = id.GetString()!;
                seen.Add(text).ShouldBeTrue("required-partition-exact");
                ordered.Add(text);
                text.ShouldNotBe("leaf-skip", "required-partition-exact");
            }

            var paramA = ordered.IndexOf("param-a");
            var paramB = ordered.IndexOf("param-b");
            if (paramA >= 0 || paramB >= 0)
            {
                paramA.ShouldBeGreaterThanOrEqualTo(0);
                paramB.ShouldBeGreaterThan(paramA);
            }
        }

        seen.Count.ShouldBe(required.Count, "required-partition-exact");
        foreach (var id in required)
            seen.ShouldContain(id);
        sawSampleExtra.ShouldBeTrue();
        hugeBasis.ShouldBe("measured");
        seen.ShouldContain("leaf-keep");
        seen.ShouldContain("huge-1");

        var launch = await RunPwsh(LaunchScript, cancellationToken, C1514Support.RepoRoot(), root, C1514Support.StagedProbe());
        launch.ExitCode.ShouldBe(0, launch.Text);
        using var launchDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "launch.json")));
        launchDoc.RootElement.GetProperty("exitCode").GetInt32().ShouldBe(0);
        launchDoc.RootElement.GetProperty("discoveryAbsent").GetBoolean().ShouldBeTrue();
        launchDoc.RootElement.GetProperty("buildAbsent").GetBoolean().ShouldBeTrue();
        launchDoc.RootElement.GetProperty("clientAbsent").GetBoolean().ShouldBeTrue();
        var launchEnv = File.ReadAllText(Path.Combine(root, "launch-env.txt"));
        launchEnv.ShouldContain("ANTIPHON_TEST_PHASE_ROOT=" + Path.Combine(root, "timing"), Case.Sensitive);
        var launchLines = launchEnv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        launchLines.ShouldContain("C1514_EMPTY_WITNESS=");
        launchLines.ShouldContain("C1514_UNSET_WITNESS=ABSENT");
        var selfEnv = File.ReadAllText(Path.Combine(root, "timing", "self-env.txt"));
        selfEnv.ShouldContain("ANTIPHON_TEST_PHASE_ROOT=", Case.Sensitive);
        var childEnv = File.ReadAllText(Path.Combine(root, "timing", "child-env.txt"));
        childEnv.ShouldNotContain("ANTIPHON_TEST_PHASE_ROOT", Case.Sensitive, "only-native-test-child-enabled");
        childEnv.ShouldNotContain("ANTIPHON_TEST_PHASE_RUN_ID", Case.Sensitive, "only-native-test-child-enabled");
        childEnv.ShouldNotContain("ANTIPHON_TEST_PHASE_GROUP_ID", Case.Sensitive, "only-native-test-child-enabled");
        childEnv.ShouldNotContain("ANTIPHON_TEST_PHASE_ATTEMPT", Case.Sensitive, "only-native-test-child-enabled");
        childEnv.ShouldNotContain("ANTIPHON_TEST_PHASE_SUITE", Case.Sensitive, "only-native-test-child-enabled");
    }

    [Test]
    [Timeout(180_000)]
    public async Task C1514T_ActualAndFailurePaths(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        await C1514Support.LaunchProbe(root, enabled: true, cancellationToken);
        var ran = await RunPwsh(ActualScript, cancellationToken, C1514Support.RepoRoot(), root);
        ran.ExitCode.ShouldBe(0, ran.Text);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "v8.json")));
        var body = doc.RootElement;
        body.GetProperty("successExit").GetInt32().ShouldBe(2, "original-verdict-preserved");
        body.GetProperty("successCoverage").GetBoolean().ShouldBeFalse("original-verdict-preserved");
        body.GetProperty("successTests").GetBoolean().ShouldBeFalse("original-verdict-preserved");
        body.GetProperty("missingExit").GetInt32().ShouldBe(1, "original-verdict-preserved");
        body.GetProperty("missingCoverage").GetBoolean().ShouldBeFalse("original-verdict-preserved");
        body.GetProperty("unwritableExit").GetInt32().ShouldBe(9, "original-verdict-preserved");
        body.GetProperty("throwExit").GetInt32().ShouldBe(4, "original-verdict-preserved");
        body.GetProperty("throwCoverage").GetBoolean().ShouldBeFalse("original-verdict-preserved");
        body.GetProperty("throwTests").GetBoolean().ShouldBeFalse("original-verdict-preserved");
        File.ReadAllText(Path.Combine(root, "throw-root", "timing", "phase-summary-failed")).ShouldContain("phase-summary-failed", Case.Sensitive);

        using var executed = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "timing", "groups-executed.json")));
        executed.RootElement.GetProperty("manufacturedPassed").GetBoolean().ShouldBeFalse();
        executed.RootElement.GetProperty("runId").GetString().ShouldBe("run-v8");
        JsonElement success = default;
        JsonElement missing = default;
        foreach (var chunk in executed.RootElement.GetProperty("chunks").EnumerateArray())
        {
            if (chunk.GetProperty("chunk").GetString() == "success")
                success = chunk;
            if (chunk.GetProperty("chunk").GetString() == "missing")
                missing = chunk;
        }

        success.ValueKind.ShouldBe(JsonValueKind.Object);
        missing.GetProperty("trxMissing").GetBoolean().ShouldBeTrue();
        missing.GetProperty("manufacturedPassed").GetBoolean().ShouldBeFalse();
        missing.GetProperty("outcomes").GetArrayLength().ShouldBe(0);
        var trxIds = new HashSet<string>(StringComparer.Ordinal);
        var trxPassed = 0;
        var trx = System.Xml.Linq.XDocument.Load(Path.Combine(root, "run.trx"));
        foreach (var node in trx.Descendants().Where(item => item.Name.LocalName == "UnitTestResult"))
        {
            trxIds.Add((string?)node.Attribute("testId") ?? "");
            if ((string?)node.Attribute("outcome") == "Passed")
                trxPassed++;
        }

        success.GetProperty("manufacturedPassed").GetBoolean().ShouldBeFalse();
        success.GetProperty("trxMissing").GetBoolean().ShouldBeFalse();
        success.GetProperty("passedCount").GetInt32().ShouldBe(trxPassed);
        success.GetProperty("runId").GetString().ShouldBe("run-v8");
        var failed = 0;
        foreach (var row in success.GetProperty("outcomes").EnumerateArray())
        {
            var id = row.GetProperty("testId").GetString();
            trxIds.ShouldContain(id!);
            if (row.GetProperty("outcome").GetString() == "Failed")
                failed++;
            if (row.GetProperty("outcome").GetString() == "Passed")
                continue;
        }

        failed.ShouldBeGreaterThan(0);
        success.GetProperty("outcomes").GetArrayLength().ShouldBe(trxIds.Count);
    }

    private static async Task<(int ExitCode, string Text)> RunPwsh(
        string script, CancellationToken cancellationToken, params string[] args)
    {
        var path = Path.Combine(Path.GetTempPath(), "c1514-" + Guid.NewGuid().ToString("N") + ".ps1");
        await File.WriteAllTextAsync(path, script, cancellationToken);
        var start = new ProcessStartInfo("pwsh") { WorkingDirectory = Path.GetTempPath() };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(path);
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        try
        {
            return await C1514Support.RunProcess(start, cancellationToken);
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    private const string PackScript = """
        $ErrorActionPreference = 'Stop'
        $repo = $args[0]
        $work = $args[1]
        $lib = Join-Path $repo 'scripts/lib'
        . (Join-Path $lib 'nightly-common.ps1')
        . (Join-Path $lib 'nightly-policy.ps1')
        . (Join-Path $lib 'nightly-coverage.ps1')
        . (Join-Path $lib 'nightly-tests-impl.ps1')
        . (Join-Path $lib 'test-phase-evidence.ps1')
        $script:C1514Nodes = New-Object System.Collections.Generic.List[object]
        function Add-C1514Node {
            param([string]$Uid, [string]$Type, [string]$Method)
            $script:C1514Nodes.Add([ordered]@{
                uid = $Uid
                assembly = 'Antiphon.Tests'
                'namespace' = 'Antiphon.Tests.C1514'
                type = $Type
                method = $Method
                signature = ''
                state = 'Discovered'
                categories = @()
            })
        }
        Add-C1514Node 'sample-1' 'Sample' 'Run'
        Add-C1514Node 'extra-1' 'SampleExtra' 'Run'
        for ($i = 1; $i -le 31; $i++) {
            $name = 'T' + $i.ToString('00')
            Add-C1514Node ('tiny-' + $name) $name 'Run'
        }
        Add-C1514Node 'huge-1' 'Huge' 'Work'
        Add-C1514Node 'param-a' 'Param' 'Run'
        Add-C1514Node 'param-b' 'Param' 'Run'
        Add-C1514Node 'leaf-keep' 'Leaf' 'Keep'
        Add-C1514Node 'leaf-skip' 'Leaf' 'Skip'
        $doc = [ordered]@{
            format = 'antiphon-tunit-discovery-v1'
            tunitVersion = '1.44.0'
            mtpVersion = '2.2.2'
            assemblyHash = 'c1514-v7'
            nodes = @($script:C1514Nodes.ToArray())
        }
        $discPath = Join-Path $work 'discovery.json'
        Write-TestPhaseTextAtomic -Path $discPath -Text (ConvertTo-TestPhaseJsonText -Value $doc)
        $parsed = Read-NightlyDiscoveryDocument -Path $discPath
        $table = Get-NightlyClassCostTable
        $hugeKey = 'Antiphon.Tests.C1514.Huge'
        $had = $table.ContainsKey($hugeKey)
        $prev = $null
        if ($had) { $prev = $table[$hugeKey] }
        $table[$hugeKey] = 3000
        try {
            $exclusions = @([pscustomobject]@{ Class = 'Antiphon.Tests.C1514.Leaf'; Methods = @('Skip') })
            $chunks = @([pscustomobject]@{ id = 'all'; classes = @() })
            $plan1 = @(Get-NightlyProfileLaunchPlan -DiscoveryNodes @($parsed.Nodes) -Chunks $chunks -Exclusions $exclusions)
            $plan2 = @(Get-NightlyProfileLaunchPlan -DiscoveryNodes @($parsed.Nodes) -Chunks $chunks -Exclusions $exclusions)
            function Get-C1514PlanText {
                param($Plan)
                $lines = New-Object System.Collections.Generic.List[string]
                foreach ($g in @($Plan)) {
                    $classes = @($g.classes | ForEach-Object { [string]$_ } | Sort-Object)
                    $nodes = @()
                    if ($g.nodes) { $nodes = @($g.nodes | ForEach-Object { [string]$_.Uid }) }
                    $lines.Add(([string]$g.id) + '|' + ($classes -join ',') + '|' + [string]$g.exactFilter + '|' + [string]$g.groupFilter + '|' + [string]$g.methodFilter + '|' + ($nodes -join ',') + '|' + [string]$g.seconds)
                }
                return ($lines.ToArray() -join "`n")
            }
            $equal = (Get-C1514PlanText -Plan $plan1) -ceq (Get-C1514PlanText -Plan $plan2)
            $groups = New-Object System.Collections.Generic.List[object]
            $maxClasses = 0
            $has30 = $false
            $sampleAlone = $false
            $hugeSeconds = 0.0
            foreach ($g in @($plan1)) {
                $classNames = New-Object System.Collections.Generic.List[string]
                $simpleNames = New-Object System.Collections.Generic.List[string]
                foreach ($className in @($g.classes)) {
                    $text = [string]$className
                    $classNames.Add($text)
                    $parts = $text.Split('.')
                    $simpleNames.Add($parts[$parts.Length - 1])
                }
                if ($classNames.Count -gt $maxClasses) { $maxClasses = $classNames.Count }
                if ($classNames.Count -eq 30) { $has30 = $true }
                if ($simpleNames.Contains('Sample') -and -not $simpleNames.Contains('SampleExtra') -and $simpleNames.Count -eq 1) { $sampleAlone = $true }
                if ($simpleNames.Contains('Huge')) { $hugeSeconds = [double]$g.seconds }
                $groups.Add([ordered]@{
                    id = [string]$g.id
                    classes = @($classNames.ToArray())
                    exactFilter = [string]$g.exactFilter
                    groupFilter = [string]$g.groupFilter
                    methodFilter = [string]$g.methodFilter
                    seconds = [double]$g.seconds
                })
            }
            $entry = [pscustomobject]@{
                Document = [pscustomobject]@{ Nodes = @($parsed.Nodes) }
                InProgress = @()
                Views = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
            }
            $view = Get-NightlyDiscoveryIndexView -Entry $entry -ProfileExclusions $exclusions -ProfileAware
            $required = @($view.RequiredUids | ForEach-Object { [string]$_ })
            $watch = [Diagnostics.Stopwatch]::StartNew()
            $written = Write-TestPhasePlannedManifest -LogRoot $work -RunId 'run-v7' -Attempt '1' -Suite 'antiphon' -SourceDigest 'src' -AssemblyDigest 'asm' -PolicyDigest 'pol' -CostFileDigest 'cost' -AssemblyId 'Antiphon.Tests' -Groups $plan1 -DiscoveryNodes @($parsed.Nodes) -ProfileExclusions $exclusions
            $watch.Stop()
            $report = [ordered]@{
                plansEqual = [bool]$equal
                maxClasses = [int]$maxClasses
                hasOperandCapGroup = [bool]$has30
                sampleAlone = [bool]$sampleAlone
                hugeSeconds = [double]$hugeSeconds
                writeMs = [math]::Round($watch.Elapsed.TotalMilliseconds, 3)
                requiredIds = @($required)
                groups = @($groups.ToArray())
                plannedPath = [string]$written
            }
            Write-TestPhaseTextAtomic -Path (Join-Path $work 'pack.json') -Text (ConvertTo-TestPhaseJsonText -Value $report)
        } finally {
            if ($had) { $table[$hugeKey] = $prev } else { [void]$table.Remove($hugeKey) }
        }
        """;

    private const string LaunchScript = """
        $ErrorActionPreference = 'Stop'
        $repo = $args[0]
        $work = $args[1]
        $probe = $args[2]
        $lib = Join-Path $repo 'scripts/lib'
        . (Join-Path $lib 'nightly-common.ps1')
        . (Join-Path $lib 'nightly-policy.ps1')
        . (Join-Path $lib 'nightly-coverage.ps1')
        . (Join-Path $lib 'nightly-tests-impl.ps1')
        . (Join-Path $lib 'test-phase-evidence.ps1')
        $timing = Join-Path $work 'timing'
        if (-not (Test-Path -LiteralPath $timing)) { New-Item -ItemType Directory -Path $timing -Force | Out-Null }
        $env:ANTIPHON_TEST_PHASE_ROOT = $timing
        $env:ANTIPHON_TEST_PHASE_RUN_ID = 'run-v7'
        $env:ANTIPHON_TEST_PHASE_GROUP_ID = 'group-v7'
        $env:ANTIPHON_TEST_PHASE_ATTEMPT = '1'
        $env:ANTIPHON_TEST_PHASE_SUITE = 'antiphon'
        $policy = [pscustomobject]@{}
        function Test-C1514PhaseAbsent {
            param($Map)
            foreach ($name in @($script:TestPhaseVariableNames)) {
                if ($Map.ContainsKey([string]$name)) { return $false }
            }
            return $true
        }
        $discovery = Get-NightlySafeChildEnvironment -PolicyObject $policy -SuiteId 'antiphon'
        Remove-TestPhaseVariables -Environment $discovery
        $build = Get-NightlySafeChildEnvironment -PolicyObject $policy -SuiteId 'antiphon'
        Remove-TestPhaseVariables -Environment $build
        $client = Get-NightlySafeChildEnvironment -PolicyObject $policy -SuiteId 'client'
        Remove-TestPhaseVariables -Environment $client
        $phase = @{}
        foreach ($key in @($discovery.Keys)) { $phase[[string]$key] = [string]$discovery[$key] }
        Enable-TestPhaseExecutionEnvironment -Environment $phase -LogRoot $work -RunId 'run-v7' -GroupId 'g1' -Attempt '1' -Suite 'antiphon' -Profile 'rc'
        $phase['C1514_CHILD'] = '1'
        $phase['C1514_EMPTY_WITNESS'] = ''
        $script:C1514Lib = $lib
        $script:C1514LaunchEnv = Join-Path $work 'launch-env.txt'
        $script:NightlySeams = @{}
        $script:NightlySeams.StartProcess = {
            param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
            . (Join-Path $script:C1514Lib 'nightly-owned-process.ps1')
            $start = New-NightlyChildStartInfo -FilePath $FilePath -ArgumentList $ArgumentList -WorkingDirectory $WorkingDirectory -Environment $Environment
            $lines = New-Object System.Collections.Generic.List[string]
            foreach ($name in @('ANTIPHON_TEST_PHASE_ROOT','ANTIPHON_TEST_PHASE_RUN_ID','ANTIPHON_TEST_PHASE_GROUP_ID','ANTIPHON_TEST_PHASE_ATTEMPT','ANTIPHON_TEST_PHASE_SUITE','C1514_EMPTY_WITNESS','C1514_UNSET_WITNESS')) {
                if (-not $start.Environment.ContainsKey($name)) { $lines.Add($name + '=ABSENT') }
                else { $lines.Add($name + '=' + [string]$start.Environment[$name]) }
            }
            [System.IO.File]::WriteAllText($script:C1514LaunchEnv, ($lines.ToArray() -join "`n"))
            $start.UseShellExecute = $false
            $start.RedirectStandardOutput = $true
            $start.RedirectStandardError = $true
            $proc = [Diagnostics.Process]::Start($start)
            $stdout = $proc.StandardOutput.ReadToEnd()
            $stderr = $proc.StandardError.ReadToEnd()
            $proc.WaitForExit()
            return [pscustomobject]@{ ExitCode = [int]$proc.ExitCode; TimedOut = $false; ChildrenExited = $true; LogText = ($stdout + $stderr) }
        }
        $launchWatch = [Diagnostics.Stopwatch]::StartNew()
        $run = Invoke-NightlyOwnedProcess -FilePath 'dotnet' -ArgumentList @($probe) -WorkingDirectory $work -TimeoutMilliseconds 120000 -Environment $phase -LogPath (Join-Path $work 'probe.log')
        $launchWatch.Stop()
        $summaryWatch = [Diagnostics.Stopwatch]::StartNew()
        Invoke-TestPhaseRunSummary -RunDirectory $work -Top 5
        $summaryWatch.Stop()
        $result = [ordered]@{
            exitCode = [int]$run.ExitCode
            launchMs = [math]::Round($launchWatch.Elapsed.TotalMilliseconds, 3)
            summaryMs = [math]::Round($summaryWatch.Elapsed.TotalMilliseconds, 3)
            discoveryAbsent = [bool](Test-C1514PhaseAbsent -Map $discovery)
            buildAbsent = [bool](Test-C1514PhaseAbsent -Map $build)
            clientAbsent = [bool](Test-C1514PhaseAbsent -Map $client)
        }
        Write-TestPhaseTextAtomic -Path (Join-Path $work 'launch.json') -Text (ConvertTo-TestPhaseJsonText -Value $result)
        """;

    private const string ActualScript = """
        $ErrorActionPreference = 'Stop'
        $repo = $args[0]
        $work = $args[1]
        $lib = Join-Path $repo 'scripts/lib'
        . (Join-Path $lib 'nightly-common.ps1')
        . (Join-Path $lib 'nightly-policy.ps1')
        . (Join-Path $lib 'nightly-coverage.ps1')
        . (Join-Path $lib 'test-phase-evidence.ps1')
        $timing = Join-Path $work 'timing'
        if (-not (Test-Path -LiteralPath $timing)) { New-Item -ItemType Directory -Path $timing -Force | Out-Null }
        [System.IO.File]::WriteAllText((Join-Path $timing 'groups-planned.json'), '{"version":1,"runId":"run-a","assembly":"asm1"}')
        $success = [pscustomobject]@{ ExitCode = 2; coverageComplete = $false; testsPassed = $false }
        $successBack = Complete-TestPhaseLaunch -Verdict $success -LogRoot $work -TrxPath (Join-Path $work 'run.trx') -Suite 'antiphon' -Chunk 'success' -RunId 'run-v8' -Attempt '1' -HostWallSeconds 1.5
        $missing = [pscustomobject]@{ ExitCode = 1; coverageComplete = $false; testsPassed = $false }
        $missingBack = Complete-TestPhaseLaunch -Verdict $missing -LogRoot $work -TrxPath (Join-Path $work 'no-such.trx') -Suite 'antiphon' -Chunk 'missing' -RunId 'run-v8' -Attempt '1'
        $bad = Join-Path $work 'bad-root'
        New-Item -ItemType Directory -Path $bad -Force | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $bad 'timing'), 'not-a-directory')
        $unwritable = [pscustomobject]@{ ExitCode = 9; coverageComplete = $false; testsPassed = $false }
        $unwritableBack = Complete-TestPhaseLaunch -Verdict $unwritable -LogRoot $bad -TrxPath (Join-Path $bad 'missing.trx') -Suite 'antiphon' -Chunk 'bad' -RunId 'run-v8' -Attempt '1'
        $throwRoot = Join-Path $work 'throw-root'
        New-Item -ItemType Directory -Path (Join-Path $throwRoot 'timing') -Force | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $throwRoot 'timing/groups-planned.json'), '{"version":1,"runId":"run-v8"}')
        $script:TestPhaseSeams = @{ Summary = { throw 'summary-boom' } }
        try {
            $thrown = [pscustomobject]@{ ExitCode = 4; coverageComplete = $false; testsPassed = $false }
            $thrownBack = Complete-TestPhaseLaunch -Verdict $thrown -LogRoot $throwRoot -TrxPath (Join-Path $work 'run.trx') -Suite 'antiphon' -Chunk 'throw' -RunId 'run-v8' -Attempt '1'
        } finally {
            $script:TestPhaseSeams = $null
        }
        $result = [ordered]@{
            successExit = [int]$successBack.ExitCode
            successCoverage = [bool]$successBack.coverageComplete
            successTests = [bool]$successBack.testsPassed
            missingExit = [int]$missingBack.ExitCode
            missingCoverage = [bool]$missingBack.coverageComplete
            missingTests = [bool]$missingBack.testsPassed
            unwritableExit = [int]$unwritableBack.ExitCode
            throwExit = [int]$thrownBack.ExitCode
            throwCoverage = [bool]$thrownBack.coverageComplete
            throwTests = [bool]$thrownBack.testsPassed
        }
        Write-TestPhaseTextAtomic -Path (Join-Path $work 'v8.json') -Text (ConvertTo-TestPhaseJsonText -Value $result)
        """;
}
