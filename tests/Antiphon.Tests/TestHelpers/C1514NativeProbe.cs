using System.Diagnostics;
using System.Text.Json;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

internal static class C1514NativeProbe
{
    internal static async Task VerifyAsync(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        var script = Path.Combine(root, "native.ps1");
        await File.WriteAllTextAsync(script, Script, cancellationToken);
        var start = new ProcessStartInfo("pwsh");
        foreach (var arg in new[] { "-NoProfile", "-File", script, C1514Support.RepoRoot(), root, C1514Support.StagedProbe() })
            start.ArgumentList.Add(arg);
        var ran = await C1514Support.RunProcess(start, cancellationToken);
        ran.ExitCode.ShouldBe(0, ran.Text);
        Console.WriteLine("C1514_ACTUAL_RUN " + root);
        foreach (var name in new[] { "groups-planned.json", "groups-executed.json", "summary.json", "tests.csv", "summary.md" })
            File.Exists(Path.Combine(root, "timing", name)).ShouldBeTrue("actual-run-five-artifacts: " + name);
        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "timing", "summary.json")));
        summary.RootElement.GetProperty("complete").GetBoolean().ShouldBeTrue("actual-run-complete");
        var tests = summary.RootElement.GetProperty("tests");
        tests.GetArrayLength().ShouldBe(9, "actual-run-exact-roster");
        foreach (var row in tests.EnumerateArray())
        {
            row.GetProperty("joined").GetBoolean().ShouldBeTrue("actual-run-joined");
            row.GetProperty("duration").GetString().ShouldNotBeNullOrWhiteSpace("actual-run-duration");
            row.GetProperty("groupId").GetString().ShouldBe("probe", "actual-run-group");
        }
    }

    private const string Script = """
        $ErrorActionPreference = 'Stop'
        $repo, $work, $probe = $args
        $lib = Join-Path $repo 'scripts/lib'
        . (Join-Path $lib 'nightly-common.ps1')
        . (Join-Path $lib 'nightly-policy.ps1')
        . (Join-Path $lib 'nightly-coverage.ps1')
        . (Join-Path $lib 'nightly-tests-impl.ps1')
        . (Join-Path $lib 'test-phase-evidence.ps1')
        . (Join-Path $lib 'nightly-owned-process.ps1')
        $environment = Get-NightlySafeChildEnvironment -PolicyObject ([pscustomobject]@{}) -SuiteId 'probe'
        Remove-TestPhaseVariables -Environment $environment
        $diag = Join-Path $work 'discovery'
        New-Item -ItemType Directory -Path $diag | Out-Null
        $discovery = Invoke-NightlyOwnedProcess -FilePath 'dotnet' -ArgumentList @($probe,'--list-tests','--no-progress','--no-ansi','--diagnostic','--diagnostic-output-directory',$diag) -WorkingDirectory $work -TimeoutMilliseconds 30000 -Environment $environment -LogPath (Join-Path $work 'discovery.log')
        if ($discovery.ExitCode -ne 0 -or $discovery.TimedOut) { throw 'probe discovery failed' }
        $parsed = ConvertFrom-NightlyDiagnosticLog -Path (Get-NightlyLatestDiagnosticLog -Directory $diag) -Kind discovery
        $nodes = @($parsed.Nodes)
        if ($nodes.Count -ne 9) { throw ('probe discovery count: ' + $nodes.Count) }
        $classes = @('TimingProbeTests','TimingPlainProbeTests','TimingAsyncProbeTests')
        $groups = @([pscustomobject]@{ id='probe'; classes=$classes; nodes=$nodes; exactFilter='/*/*/(TimingProbeTests*|TimingPlainProbeTests*|TimingAsyncProbeTests*)/*'; seconds=1 })
        $sha = (& git -C $repo rev-parse HEAD).Trim()
        [void](Write-TestPhasePlannedManifest -LogRoot $work -RunId 'run-native' -Attempt '1' -Suite 'probe' -SourceDigest $sha -AssemblyDigest (Get-NightlyFileSha256 -Path $probe) -AssemblyId 'Antiphon.TestTiming.Probe' -Groups $groups -DiscoveryNodes $nodes -ProfileExclusions @())
        $environment['ANTIPHON_TEST_PHASE_ROOT'] = Join-Path $work 'timing'
        $environment['ANTIPHON_TEST_PHASE_RUN_ID'] = 'run-native'
        $environment['ANTIPHON_TEST_PHASE_GROUP_ID'] = 'probe'
        $environment['ANTIPHON_TEST_PHASE_ATTEMPT'] = '1'
        $environment['ANTIPHON_TEST_PHASE_SUITE'] = 'probe'
        $wall = [Diagnostics.Stopwatch]::StartNew()
        $run = Invoke-NightlyOwnedProcess -FilePath 'dotnet' -ArgumentList @($probe,'--treenode-filter',$groups[0].exactFilter,'--report-trx','--report-trx-filename','probe.trx','--results-directory',$work) -WorkingDirectory $work -TimeoutMilliseconds 30000 -Environment $environment -LogPath (Join-Path $work 'probe.log')
        $wall.Stop()
        if ($run.TimedOut -or -not $run.ChildrenExited) { throw 'probe custody failed' }
        [void](Complete-TestPhaseLaunch -Verdict ([pscustomobject]@{ ExitCode=$run.ExitCode }) -LogRoot $work -TrxPath (Join-Path $work 'probe.trx') -Suite 'probe' -Chunk 'probe' -RunId 'run-native' -Attempt '1' -HostWallSeconds $wall.Elapsed.TotalSeconds)
        Invoke-TestPhaseRunSummary -RunDirectory $work
        if ($env:OS -eq 'Windows_NT') {
            $before = [IO.File]::ReadAllText((Join-Path $work 'timing/summary.json'))
            & powershell.exe -NoProfile -File (Join-Path $repo 'scripts/summarize-test-phases.ps1') -RunDirectory $work
            if ($LASTEXITCODE -ne 0) { throw 'PS51 summary failed' }
            if ($before -ne [IO.File]::ReadAllText((Join-Path $work 'timing/summary.json'))) { throw 'PS51 summary differs' }
        }
        $csv = @(Import-Csv -LiteralPath (Join-Path $work 'timing/tests.csv'))
        if ($csv.Count -ne 9) { throw ('actual CSV count: ' + $csv.Count) }
        foreach ($row in $csv) {
            if (-not $row.duration -or $row.groupId -ne 'probe' -or $row.joined -ne 'True') { throw 'actual CSV duration/group/join missing' }
        }
        """;
}
