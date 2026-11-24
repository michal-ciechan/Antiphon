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
/// C1391_LintGate (W2-nightly S12) ran the production entry scripts/nightly-tests.ps1 end to end on a seamed npm fixture;
/// since S17 it runs NightlyTests.Run in-process on the same fixture.
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
        var sawLater = launches.Any(j => Regex.IsMatch(j, "(^| )run lint($| )") || Regex.IsMatch(j, @"Antiphon\.sln") || Regex.IsMatch(j, "(^| )test-client( |$)"));
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
    /// New-C1341NpmFixture and Write-C1391LintSeams, in-process since W2-nightly S17 (the entry scripts/nightly-tests.ps1 is
    /// deleted): a clone with the policy, a stub per policy project and a client directory; npm.cmd under a "Program Files"
    /// directory first on PATH (NightlyTestsRunTests.NpmWorld); a launcher that records each launch's file and argv and fails
    /// "run lint". Then NightlyTests.Run for the client suite under the profile, read back from its summary; the exit is the
    /// command's.
    /// </summary>
    private static LintRun RunLintFixture(string profile)
    {
        using var world = NightlyTestsRunTests.NpmWorld(out _, out _);
        world.Behaviour = launch => NightlyTestsWorld.Result(Regex.IsMatch(string.Join(" ", launch.Arguments), "(^| )run lint($| )") ? 2 : 0, "");
        var result = world.Run(o =>
        {
            o.Suites = ["client"];
            o.Profile = profile;
            o.RunId = "c1391-" + profile + "-" + Guid.NewGuid().ToString("N")[..8];
        });
        var launches = string.Join("\n", world.Launcher.Launches.Select(l => JsonSerializer.Serialize(new { file = l.FileName, args = l.Arguments })));
        var summary = NightlyTestsWorld.Summary(result);
        if (summary.ValueKind != JsonValueKind.Object) return new LintRun(result.ProcessExitCode, false, false, [], launches, world.Fixture.Out + "\n" + world.Fixture.Error);
        return new LintRun(result.ProcessExitCode, summary.GetProperty("testsPassed").GetBoolean(), summary.GetProperty("coverageComplete").GetBoolean(),
            NightlyTestsWorld.SummaryReasons(result), launches, world.Fixture.Out + "\n" + world.Fixture.Error);
    }
    /// <summary>
    /// W2-nightly S12 (was the scripts/test-nightly-tests.ps1 case): nightly keeps dotnet and the client suite after lint
    /// fails; every other profile stops.
    /// </summary>
    [Test]
    public void C1391_LintGate()
    {
        var nightly = RunLintFixture("nightly");
        var nightlySln = Regex.IsMatch(nightly.Launches, @"Antiphon\.sln");
        var nightlySuite = nightly.Launches.Contains("\"test-client\"", StringComparison.Ordinal);
        var nightlyReason = nightly.Reasons.Contains("client-lint-failed", StringComparer.Ordinal);
        var nightlyPrereq = nightly.Reasons.Contains("prerequisite-failed:client lint", StringComparer.Ordinal);
        var nightlyOk = nightly.ExitCode != 0 && !nightly.TestsPassed && !nightly.CoverageComplete && nightlyReason && !nightlyPrereq && nightlySln && nightlySuite;

        var rc = RunLintFixture("rc");
        var rcSln = Regex.IsMatch(rc.Launches, @"Antiphon\.sln");
        var rcSuite = rc.Launches.Contains("\"test-client\"", StringComparison.Ordinal);
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
