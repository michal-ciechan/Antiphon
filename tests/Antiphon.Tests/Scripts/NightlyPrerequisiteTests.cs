using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1350. The prerequisite cases. C487_PrerequisiteGate runs in-process on NightlyTests.Run since W2-nightly S15 (it was
/// the scripts/test-nightly-tests.ps1 case); C487_PrerequisiteSettle on NightlyRun.Run since S19 (the nightly-run harness case).
/// C1391_LintGate (W2-nightly S12) runs the production entry scripts/nightly-tests.ps1 end to end on a seamed npm fixture,
/// from C#, until S17 wires the C#.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlyPrerequisiteTests
{
    /// <summary>A failed npm build is a prerequisite. Later steps must not run, and the summary must not claim the tests passed.</summary>
    [Test]
    public void C487_PrerequisiteGate()
    {
        using var world = NightlyTestsRunTests.NpmWorld(out _, out _);
        world.Behaviour = launch => NightlyTestsWorld.Result(Regex.IsMatch(string.Join(" ", launch.Arguments), "(^| )run build($| )") ? 2 : 0, "");
        var result = world.Run(o => o.Suites = ["client"]);
        var launches = world.Launcher.Launches.Select(l => string.Join(" ", l.Arguments)).ToArray();
        var sawCi = launches.Any(j => Regex.IsMatch(j, "(^| )ci($| )"));
        var sawBuild = launches.Any(j => Regex.IsMatch(j, "(^| )run build($| )"));
        var sawLater = launches.Any(j => Regex.IsMatch(j, "(^| )run lint($| )") || Regex.IsMatch(j, @"Antiphon\.sln") || Regex.IsMatch(j, @"test-client\.ps1"));
        var summary = NightlyTestsWorld.Summary(result);
        var hasSummary = summary.ValueKind == JsonValueKind.Object;
        var reasons = NightlyTestsWorld.SummaryReasons(result);
        var reason = reasons.Contains("prerequisite-failed:npm run build", StringComparer.Ordinal);
        var summaryPass = hasSummary && summary.GetProperty("testsPassed").GetBoolean();
        var summaryComplete = hasSummary && summary.GetProperty("coverageComplete").GetBoolean();
        var rows = new NightlyCaseRows();
        rows.Check(sawCi && sawBuild && !sawLater && result.ExitCode != 0 && !result.TestsPassed && hasSummary && !summaryPass && !summaryComplete && reason,
            "C487 prerequisite npm build stops the pipeline and summary testsPassed is false",
            "ci=" + sawCi + " build=" + sawBuild + " later=" + sawLater + " exit=" + result.ExitCode + " pass=" + result.TestsPassed + " summaryPass=" + (hasSummary ? summaryPass.ToString() : "missing")
            + " reasons=" + string.Join(",", reasons));
        rows.ShouldAllPass(1);
    }

    /// <summary>
    /// In-process since W2-nightly S19 (was the scripts/test-nightly-run.ps1 case, on NightlyRun.Run): a child that exits
    /// nonzero while summary.json still says testsPassed must not leave that success flag settled beside last-run.json.
    /// </summary>
    [Test]
    public void C487_PrerequisiteSettle()
    {
        using var w = new NightlyRunWorld("c487-settle");
        w.Clock = NightlyRunWorld.At("2026-09-17T23:35:00Z");
        w.Behaviour = NightlyRunWorld.TestsHop(NightlyRunWorld.C545Summary(true), 2);
        var result = w.Run(o => { o.NoSync = true; o.NoReport = false; o.RunId = "c545-npm-build-failed-" + Guid.NewGuid().ToString("N")[..8]; });
        var last = w.OutLines[^1];
        using var record = JsonDocument.Parse(last);
        var summaryPath = record.RootElement.GetProperty("summaryPath").GetString() ?? "";
        var summaryPassed = true;
        if (summaryPath.Length > 0 && File.Exists(summaryPath))
        {
            using var summary = JsonDocument.Parse(File.ReadAllText(summaryPath));
            summaryPassed = summary.RootElement.GetProperty("testsPassed").GetBoolean();
        }
        var recordPassed = record.RootElement.GetProperty("testsPassed").GetBoolean();
        var statePassed = NightlyRunWorld.Json(w.LastRunPath) is { } state && state.GetProperty("testsPassed").GetBoolean();
        var rows = new NightlyCaseRows();
        rows.Check(!recordPassed && !statePassed && !summaryPassed, "C487 prerequisite settle summary testsPassed matches last-run",
            "exit=" + result.ExitCode + " record=" + recordPassed + " state=" + statePassed + " summary=" + summaryPassed + " path=" + summaryPath);
        rows.ShouldAllPass(1);
    }

    private sealed record LintRun(int ExitCode, bool TestsPassed, bool CoverageComplete, string[] Reasons, string Launches, string Output);

    /// <summary>
    /// New-C1341NpmFixture and Write-C1391LintSeams: a clone with the policy, a stub per policy project and a client
    /// directory; npm.cmd under a "Program Files" directory first on PATH; seams that record each launch's argv and fail
    /// "run lint". Then scripts/nightly-tests.ps1 for the client suite under the profile, read back from its summary.
    /// </summary>
    private static async Task<LintRun> RunLintFixtureAsync(string profile, CancellationToken cancellationToken)
    {
        using var fixture = NightlyFixture.Create("c1391-" + profile);
        var repo = NightlyPlanCases.Repo;
        var clone = fixture.NewOwnedClone();
        var tests = Path.Combine(clone, "tests");
        Directory.CreateDirectory(tests);
        var policyPath = Path.Combine(tests, "test-execution-policy.json");
        File.Copy(Path.Combine(repo, "tests", "test-execution-policy.json"), policyPath, true);
        using (var policy = JsonDocument.Parse(File.ReadAllText(policyPath)))
            foreach (var row in policy.RootElement.GetProperty("projectDispositions").EnumerateArray())
            {
                var stub = Path.Combine(clone, row.GetProperty("project").GetString()!.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(stub)!);
                File.WriteAllText(stub, "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>\r\n");
            }
        Directory.CreateDirectory(Path.Combine(clone, "client"));
        var launcherDir = fixture.PathOf("Program Files", "nodejs");
        Directory.CreateDirectory(launcherDir);
        File.WriteAllText(Path.Combine(launcherDir, "npm.cmd"), "@echo off\r\nexit /b 0\r\n");
        var trace = fixture.PathOf("trace.log");
        var record = fixture.PathOf("npm-launches.jsonl");
        var seams = fixture.PathOf("seams.ps1");
        var logs = fixture.PathOf("logs");
        Directory.CreateDirectory(logs);
        File.WriteAllText(seams, """
            $NightlySeams = @{
                TracePath = '__TRACE__'
                Git = {
                    param($WorkingDirectory, $Arguments)
                    return @{ ExitCode = 0; Output = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' }
                }
                StartProcess = {
                    param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
                    $encoded = @()
                    foreach ($item in @($ArgumentList)) {
                        $encoded += (ConvertTo-Json -InputObject ([string]$item) -Compress)
                    }
                    $line = '{"file":' + (ConvertTo-Json -InputObject ([string]$FilePath) -Compress) + ',"args":[' + ($encoded -join ',') + ']}'
                    Add-Content -LiteralPath '__RECORD__' -Value $line -Encoding ASCII
                    $joined = (@($ArgumentList) -join ' ')
                    $code = 0
                    if ($joined -match '(^| )run lint($| )') { $code = 2 }
                    return @{ ExitCode = $code; TimedOut = $false; Pid = 2; ChildrenExited = $true; LogText = '' }
                }
                DockerInfo = { return @{ exitCode = 0; version = 'fake'; detail = 'ok' } }
                DiskFree = { return @{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB } }
            }
            """.Replace("__TRACE__", trace).Replace("__RECORD__", record));
        var start = new ProcessStartInfo("pwsh") { WorkingDirectory = fixture.Root };
        foreach (var argument in new[]
        {
            "-NoProfile", "-File", Path.Combine(repo, "scripts", "nightly-tests.ps1"), "-RepoRoot", clone, "-LogRoot", logs, "-SeamsPath", seams, "-PolicyPath", policyPath,
            "-Sha", new string('a', 40), "-GitRef", "origin/master", "-Trigger", "scheduled", "-RunId", "c1391-" + profile + "-" + Guid.NewGuid().ToString("N")[..8],
            "-Suites", "client", "-Profile", profile, "-AllowSharedTree",
        }) start.ArgumentList.Add(argument);
        start.Environment["PATH"] = launcherDir + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        var ran = await C1514Support.RunProcess(start, cancellationToken);
        var summaryPath = Path.Combine(logs, "summary.json");
        if (!File.Exists(summaryPath)) return new LintRun(ran.ExitCode, false, false, [], File.Exists(record) ? File.ReadAllText(record) : "", ran.Text);
        using var summary = JsonDocument.Parse(File.ReadAllText(summaryPath));
        var root = summary.RootElement;
        var reasons = root.GetProperty("reasons").ValueKind == JsonValueKind.Array ? root.GetProperty("reasons").EnumerateArray().Select(r => r.ToString()).ToArray() : [];
        return new LintRun(ran.ExitCode, root.GetProperty("testsPassed").GetBoolean(), root.GetProperty("coverageComplete").GetBoolean(), reasons,
            File.Exists(record) ? File.ReadAllText(record) : "", ran.Text);
    }

    /// <summary>
    /// W2-nightly S12 (was the scripts/test-nightly-tests.ps1 case): nightly keeps dotnet and the client suite after lint
    /// fails; every other profile stops.
    /// </summary>
    [Test]
    [Timeout(600_000)]
    public async Task C1391_LintGate(CancellationToken cancellationToken)
    {
        var nightly = await RunLintFixtureAsync("nightly", cancellationToken);
        var nightlySln = Regex.IsMatch(nightly.Launches, @"Antiphon\.sln");
        var nightlySuite = Regex.IsMatch(nightly.Launches, @"test-client\.ps1");
        var nightlyReason = nightly.Reasons.Contains("client-lint-failed", StringComparer.Ordinal);
        var nightlyPrereq = nightly.Reasons.Contains("prerequisite-failed:client lint", StringComparer.Ordinal);
        var nightlyOk = nightly.ExitCode != 0 && !nightly.TestsPassed && !nightly.CoverageComplete && nightlyReason && !nightlyPrereq && nightlySln && nightlySuite;

        var rc = await RunLintFixtureAsync("rc", cancellationToken);
        var rcSln = Regex.IsMatch(rc.Launches, @"Antiphon\.sln");
        var rcSuite = Regex.IsMatch(rc.Launches, @"test-client\.ps1");
        var rcReason = rc.Reasons.Contains("prerequisite-failed:client lint", StringComparer.Ordinal);
        var rcNightlyReason = rc.Reasons.Contains("client-lint-failed", StringComparer.Ordinal);
        var rcOk = rc.ExitCode != 0 && !rc.TestsPassed && rcReason && !rcNightlyReason && !rcSln && !rcSuite;
        var rows = new NightlyCaseRows();
        rows.Check(nightlyOk && rcOk, "C1391 nightly lint continues and rc lint stops",
            "nightly=" + nightlyOk + " exit=" + nightly.ExitCode + " reason=" + nightlyReason + " prereq=" + nightlyPrereq + " sln=" + nightlySln + " suite=" + nightlySuite
            + " reasons=" + string.Join(",", nightly.Reasons) + " rc=" + rcOk + " rcExit=" + rc.ExitCode + " rcReason=" + rcReason + " rcSln=" + rcSln + " rcSuite=" + rcSuite
            + " rcReasons=" + string.Join(",", rc.Reasons) + (nightlyOk && rcOk ? "" : "\n" + nightly.Output + "\n" + rc.Output));
        rows.ShouldAllPass(1);
    }
}
