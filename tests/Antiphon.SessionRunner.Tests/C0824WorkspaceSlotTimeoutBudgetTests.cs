using System.Reflection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
public sealed class C0824WorkspaceSlotTimeoutBudgetTests
{
    // CARD-1722: ten complete Linux class runs (190/190 passed), 2026-11-05,
    // source 388ff482046a62f813a251208bfeabe7c21808ea, host dfcc116be984.
    // Each row records case, median seconds and empirical nearest-rank p95 seconds
    // (rounded up to milliseconds). n=10 makes p95=max; provisional for Windows.
    // Full min/median/p95/max table and retained TRX provenance: task-871c4033 report.
    // All cases create real Git repositories. The commissioned 60s floor covers
    // saturated Windows process startup (b19a9880 Leased timed out at 10.13s).
    // Collect the same class in the next CARD-1722 Windows rc run to refine it.
    [Test]
    [Arguments(nameof(C0824WorkspaceSlotCustodyTests.C0824_BranchCheckedOutInAnotherSlotRefused), "single", 0.234, 0.682)]
    [Arguments(nameof(C0824WorkspaceSlotCustodyTests.C0824_ConcurrentLeasesNeverShareSlot), "single", 0.692, 1.516)]
    [Arguments(nameof(C0824WorkspaceSlotCustodyTests.C0824_CrashAtEachPhaseRecoversNeverDirty), "Leased", 0.599, 3.315)]
    [Arguments(nameof(C0824WorkspaceSlotCustodyTests.C0824_CrashAtEachPhaseRecoversNeverDirty), "Leasing", 0.531, 1.454)]
    [Arguments(nameof(C0824WorkspaceSlotCustodyTests.C0824_CrashAtEachPhaseRecoversNeverDirty), "Releasing", 0.419, 0.644)]
    [Arguments(nameof(C0824WorkspaceSlotCustodyTests.C0824_DirtQuarantinesPreservesBytesAndRehomes), "single", 0.361, 1.822)]
    [Arguments(nameof(C0824WorkspaceSlotCustodyTests.C0824_LeasedSlotNeverResetByForeignRelease), "single", 0.207, 0.774)]
    [Arguments(nameof(C0824WorkspaceSlotCustodyTests.C0824_LeaseQuarantinesFreeSlotWithHolder), "single", 0.518, 1.49)]
    [Arguments(nameof(C0824WorkspaceSlotCustodyTests.C0824_LeaseReverifiesFreeInvariant), "single", 0.737, 2.051)]
    [Arguments(nameof(C0824WorkspaceSlotCustodyTests.C0824_LeaseThroughDispatcherFrame), "single", 0.266, 0.862)]
    [Arguments(nameof(C0824WorkspaceSlotCustodyTests.C0824_LeaseWaitsForTheDispatcherMutationLock), "single", 2.354, 2.659)]
    [Arguments(nameof(C0824WorkspaceSlotCustodyTests.C0824_PublishResolvesSlotByLedger), "single", 0.386, 2.876)]
    [Arguments(nameof(C0824WorkspaceSlotCustodyTests.C0824_ReleaseOfLeasingSlotRefused), "single", 0.393, 0.577)]
    [Arguments(nameof(C0824WorkspaceSlotCustodyTests.C0824_RemoveAsyncRefusesSlotManaged), "ws-01-d2", 0.152, 1.258)]
    [Arguments(nameof(C0824WorkspaceSlotCustodyTests.C0824_RemoveAsyncRefusesSlotManaged), "ws-01", 0.146, 0.256)]
    [Arguments(nameof(C0824WorkspaceSlotCustodyTests.C0824_RestartKeepsLeasedSlotForTheServersRelease), "single", 0.307, 0.836)]
    [Arguments(nameof(C0824WorkspaceSlotCustodyTests.C0824_StaleGenerationReleaseRefused), "single", 0.582, 1.535)]
    [Arguments(nameof(C0824WorkspaceSlotCustodyTests.C0824_UnpushedReflogOnlyCommitQuarantines), "single", 0.325, 1.17)]
    [Arguments(nameof(C0824WorkspaceSlotCustodyTests.C0824_UnpushedTipQuarantines), "single", 0.29, 0.879)]
    public void C0824_BudgetCoversMeasuredCase(string methodName, string caseName,
        double medianSeconds, double p95Seconds)
    {
        var type = typeof(C0824WorkspaceSlotCustodyTests);
        var method = type.GetMethod(methodName).ShouldNotBeNull();
        var timeout = (method.GetCustomAttribute<TimeoutAttribute>()
            ?? type.GetCustomAttribute<TimeoutAttribute>()).ShouldNotBeNull().Timeout;
        var label = methodName + "(" + caseName + ")";

        medianSeconds.ShouldBeGreaterThan(0);
        p95Seconds.ShouldBeGreaterThanOrEqualTo(medianSeconds);
        timeout.TotalSeconds.ShouldBeGreaterThanOrEqualTo(1.5 * medianSeconds,
            label + " must retain measured median headroom");
        timeout.TotalSeconds.ShouldBeGreaterThanOrEqualTo(Math.Max(60, 2 * p95Seconds),
            label + " requires max(60s Git floor, 2x recorded p95)");
    }

    [Test]
    public void C0824_BudgetTableCoversEveryCustodyCase()
    {
        var recorded = typeof(C0824WorkspaceSlotTimeoutBudgetTests)
            .GetMethod(nameof(C0824_BudgetCoversMeasuredCase))!
            .GetCustomAttributes<ArgumentsAttribute>()
            .Select(row => row.Values[0] + "(" + row.Values[1] + ")")
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var actual = typeof(C0824WorkspaceSlotCustodyTests).GetMethods()
            .Where(method => method.IsDefined(typeof(TestAttribute)))
            .SelectMany(method =>
            {
                var rows = method.GetCustomAttributes<ArgumentsAttribute>().ToArray();
                return rows.Length == 0
                    ? [method.Name + "(single)"]
                    : rows.Select(row => method.Name + "(" + row.Values.Single() + ")");
            })
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();

        recorded.ShouldBe(actual, "every custody argument case needs its own measured budget row");
        recorded.Distinct(StringComparer.Ordinal).Count().ShouldBe(recorded.Length);
    }
}
