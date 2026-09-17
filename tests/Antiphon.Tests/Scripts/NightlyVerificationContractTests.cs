using System.Diagnostics;
using System.Text.RegularExpressions;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0544 D-6 (V-9/V-10 subset owned by CARD-0544). Each method runs the matching
/// <c>Test-C544_*</c> case of <c>scripts/test-nightly-health.ps1</c> in a fresh results directory and
/// requires exit 0 plus its named assertion inventory: every expected PASS row is present and no row
/// failed. The notification/outage/recipient cases (PC-78..86, 95..98) are deferred to CARD-0545.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlyVerificationContractTests
{
    [Test]
    public Task C544_ProductionJobAdapter() => RunCaseAsync("C544_ProductionJobAdapter", 21,
        "C544 ProductionJobAdapter real HTTP request shape",
        "C544 ProductionJobAdapter status j-running",
        "C544 ProductionJobAdapter status j-missing-result",
        "C544 ProductionJobAdapter j-running never qualifies as scheduled green",
        "C544 ProductionJobAdapter crossed-due-day never qualifies",
        "C544 ProductionJobAdapter control success row with matching native run is ready");

    [Test]
    public Task C544_DailyValidity() => RunCaseAsync("C544_DailyValidity", 6,
        "C544 DailyValidity green completed eight hours ago is ready",
        "C544 DailyValidity yesterday-only after the deadline is unready");

    [Test]
    public Task C544_MorningBoundary() => RunCaseAsync("C544_MorningBoundary", 9,
        "C544 MorningBoundary bridge while today runs 2026-09-17 07:59:59",
        "C544 MorningBoundary no bridge at or after deadline 2026-09-17 08:00:00",
        "C544 MorningBoundary today green at the deadline is ready");

    [Test]
    public Task C544_NewerFailure() => RunCaseAsync("C544_NewerFailure", 6,
        "C544 NewerFailure control: in-progress run keeps the bridge",
        "C544 NewerFailure newer red attempt revokes the bridge",
        "C544 NewerFailure newer incomplete attempt revokes the bridge",
        "C544 NewerFailure failed scheduled job revokes even without native state");

    [Test]
    public Task C544_ScheduledIdentity() => RunCaseAsync("C544_ScheduledIdentity", 7,
        "C544 ScheduledIdentity manual green after deadline is unready",
        "C544 ScheduledIdentity partial scheduled run is unready",
        "C544 ScheduledIdentity control scheduled green is ready");

    [Test]
    public Task C544_LondonDates() => RunCaseAsync("C544_LondonDates", 15,
        "C544 LondonDates due 2026-03-30",
        "C544 LondonDates due 2026-10-25",
        "C544 LondonDates morning deadline 2026-03-29",
        "C544 LondonDates 01:00 BST is overdue");

    [Test]
    public Task C544_ScheduleHealth() => RunCaseAsync("C544_ScheduleHealth", 7,
        "C544 ScheduleHealth missing at grace equality is unready",
        "C544 ScheduleHealth queued overdue-start",
        "C544 ScheduleHealth stalled run is not terminated");

    [Test]
    public Task C544_CoverageRequired() => RunCaseAsync("C544_CoverageRequired", 4,
        "C544 CoverageRequired coverageComplete=false is unready");

    [Test]
    public Task C544_GreenRequired() => RunCaseAsync("C544_GreenRequired", 4,
        "C544 GreenRequired testsPassed=false is unready");

    [Test]
    public Task C544_ReportReceiptRequired() => RunCaseAsync("C544_ReportReceiptRequired", 4,
        "C544 ReportReceiptRequired reportDelivered=false is unready");

    private static async Task RunCaseAsync(string caseName, int expectedRows, params string[] requiredRows)
    {
        var results = Path.Combine(Path.GetTempPath(), "c544-nightly-" + Guid.NewGuid().ToString("N"));
        var script = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "test-nightly-health.ps1");
        var startInfo = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", script, "-Case", caseName, "-ResultsDirectory", results })
            startInfo.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("pwsh did not start.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout + await stderr;

            process.ExitCode.ShouldBe(0, output);
            output.ShouldContain("C487 HARNESS EXIT CODE: 0", Case.Sensitive, output);
            var lines = output.ReplaceLineEndings("\n").Split('\n');
            lines.ShouldNotContain(l => l.StartsWith("FAIL ", StringComparison.Ordinal), output);
            var passed = lines.Where(l => l.StartsWith("PASS C544 ", StringComparison.Ordinal)).Select(l => l[5..]).ToList();
            passed.Count.ShouldBe(expectedRows, $"{caseName} named assertion inventory\n{output}");
            foreach (var row in requiredRows)
                passed.ShouldContain(row, $"{caseName} must assert '{row}'\n{output}");
            Regex.IsMatch(output, $@"C487: {expectedRows} passed, 0 failed, {expectedRows} rows").ShouldBeTrue(output);
        }
        finally
        {
            try { Directory.Delete(results, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
