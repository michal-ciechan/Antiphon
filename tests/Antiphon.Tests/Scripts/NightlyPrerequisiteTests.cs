using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1350. The prerequisite cases run from the harness default loop and from these wrappers. C1391_LintGate (W2-nightly
/// S12) runs the production entry scripts/nightly-tests.ps1 end to end on a seamed npm fixture, from C#.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlyPrerequisiteTests
{
    [Test]
    public Task C487_PrerequisiteGate() =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-tests.ps1", "C487",
            nameof(C487_PrerequisiteGate), 1,
            ["C487 prerequisite npm build stops the pipeline and summary testsPassed is false"]);

    [Test]
    public Task C487_PrerequisiteSettle() =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-run.ps1", "C487",
            nameof(C487_PrerequisiteSettle), 1,
            ["C487 prerequisite settle summary testsPassed matches last-run"]);

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
