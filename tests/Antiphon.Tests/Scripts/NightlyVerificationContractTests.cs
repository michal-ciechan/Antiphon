using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0544 D-6 (V-9/V-10 subset owned by CARD-0544). Each method runs the matching
/// <c>Test-C544_*</c> case of <c>scripts/test-nightly-health.ps1</c> in a fresh results directory and
/// requires exit 0 plus its named assertion inventory: every expected PASS row is present and no row
/// failed. The notification/outage/recipient controls (PC-78..86, 95..98) moved into the CARD-0545 watchdog and run
/// in-process on <see cref="C545World"/> (see NightlyVerificationContractTests.C545.cs).
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed partial class NightlyVerificationContractTests
{
    [Test]
    public Task C544_ProductionJobAdapter() => RunCaseAsync("C544_ProductionJobAdapter", 22,
        "C544 ProductionJobAdapter real HTTP request shape",
        "C544 ProductionJobAdapter get_result requests bounded to scheduled completed rows",
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

    private static Task RunCaseAsync(string caseName, int expectedRows, params string[] requiredRows) =>
        RunHarnessCaseAsync("test-nightly-health.ps1", "C544", caseName, expectedRows, requiredRows);

    /// <summary>Forwards to <see cref="ScriptHarness.RunHarnessCaseAsync"/> (CARD-0585 S6 lifted the body out).</summary>
    private static Task RunHarnessCaseAsync(string harness, string prefix, string caseName, int expectedRows, params string[] requiredRows) =>
        ScriptHarness.RunHarnessCaseAsync(harness, prefix, caseName, expectedRows, requiredRows);
}
