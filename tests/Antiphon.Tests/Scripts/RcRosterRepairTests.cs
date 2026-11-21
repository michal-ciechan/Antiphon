using System.Collections;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using TUnit.Core;
using static Antiphon.Tests.Scripts.Nightly.NightlyPlanCases;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Offline CARD-1561/1566/1567 witnesses (the cases of the retired scripts/test-rc-roster-repairs.ps1), in-process since
/// W2-nightly S11 against the shipped policy, the census and NightlyTestsPlan. No provider or qualification launch.
/// </summary>
[Category("Integration")]
public sealed class RcRosterRepairTests
{
    private static PlainObject RosterNode(string type, string method, string uid) =>
        new(("ClassName", "Antiphon.Tests.Application." + type), ("Namespace", "Antiphon.Tests.Application"), ("Type", type), ("Method", method), ("Uid", uid),
            ("Categories", new object[] { "Integration" }), ("Excluded", false));

    private static int Count(object census, string name) => (int)PsValue.Double(PsValue.Prop(census, name));

    [Test]
    public void Mirror()
    {
        // Pin the 27 methods identified by the Windows diagnosis, rather than excluding the entire evolving class and
        // concealing new portable tests.
        string[] methods =
        [
            "C1017_MirrorBehindPushedTip", "C1017_ReopenBetweenEndpoints", "C1017_MultipleMirrorsRemainDistinct",
            "C1032_AdmissionCurrentEligibility", "C1032_AdmissionArtifactSurvival", "C1032_ReportRecognitionCaseTable",
            "C1032_ReportAddedAfterProducerInspection", "C1032_ReportAbsentAllowsCleanup", "C1032_AdmissionIntentOrdering",
            "C1032_AdmissionBoundRebaseProof", "C1017_ReceiptMustMatchOperation", "C1017_ReceiptMustMatchEndpoint",
            "C1017_ReceiptMustMatchStore", "C1017_ReceiptMustMatchTaskAttempt", "C1017_AckIsNotRemoval",
            "C1017_UnsupportedNeverFallsBack", "C1032_ReceiptCompleteBoundReceipt", "C1032_WitnessLandingOwnership",
            "C1032_WitnessLandingCompleteness", "C1032_WitnessPersistedReplayTamper", "C1032_WitnessUnsupportedRemovalAfterInspection",
            "C1017_MirrorCompletesIndependently", "C1017_LocalProgressSurvivesOffline", "C1017_LostAckReusesOperation",
            "C1032_RecoveryQueuesBusyAndReady", "C1032_RecoveryCrashHandoffs", "C1032_RecoveryInertServerComposition",
        ];
        var nodes = methods.Select(m => (object)RosterNode("CardWorktreeMirrorCleanupTests", m, m)).ToList();
        nodes.Add(RosterNode("CardWorktreeMirrorCleanupTests", "FuturePortableSibling", "sibling"));
        var policy = Policy();
        var win = Exclusions(policy, "rc", "antiphon", "windows");
        var linux = Exclusions(policy, "rc", "antiphon", "linux");
        var wc = NightlyCensus.Census(nodes.ToArray(), win, true);
        var lc = NightlyCensus.Census(nodes.ToArray(), linux, true);
        var rows = new NightlyCaseRows();
        rows.Check(Count(wc, "RequiredCount") == 1 && Count(wc, "ExcludedCount") == 27 && Count(lc, "RequiredCount") == 28 && Count(lc, "ExcludedCount") == 0,
            "C1561 platform required UID sets", "windows required=" + Count(wc, "RequiredCount") + " excluded=" + Count(wc, "ExcludedCount"));
        var owned = PsValue.AsArray(PsValue.Prop(wc, "Excluded")).Count(r =>
            string.Equals(Str(r, "owner"), "CARD-1561", StringComparison.OrdinalIgnoreCase) && Str(r, "reason").Contains("Linux", StringComparison.OrdinalIgnoreCase));
        rows.Check(owned == 27, "C1561 all excluded UIDs have Linux obligation", "owned=" + owned);
        rows.ShouldAllPass(2);
    }

    [Test]
    public void Explicit()
    {
        // This is exactly the lossy diagnostic-export shape in the diagnosis: Integration only, excluded=false. RC must
        // not depend on an absent flag.
        var live = RosterNode("RunnerGrokAdapterReadyTestsPty", "C1011_real_fresh_worktree_ready_and_one_turn", "live");
        live["ClassName"] = "Antiphon.Tests.Agents.RunnerGrokAdapterReadyTestsPty";
        var offline = RosterNode("RunnerGrokAdapterReadyTestsPty", "OfflineSibling", "offline");
        offline["ClassName"] = live["ClassName"];
        var policy = Policy();
        var rows = new NightlyCaseRows();
        foreach (var platform in new[] { "windows", "linux" })
        {
            var ex = Exclusions(policy, "rc", "antiphon", platform);
            var c = NightlyCensus.Census([live, offline], ex, true);
            var d = NightlyCensus.Disposition(live, ex, true);
            rows.Check(Count(c, "RequiredCount") == 1 && Count(c, "ExcludedCount") == 1 && string.Equals(Str(d, "Owner"), "CARD-1566", StringComparison.OrdinalIgnoreCase)
                && Str(d, "Reason").Contains("WQ-3", StringComparison.OrdinalIgnoreCase), "C1566 owned manual disposition " + platform);
            var chunks = NightlyTestsPlan.ProfileLaunchPlan([live, offline], AllChunk(), ex, "antiphon", null, NightlyPlanCases.Costs());
            rows.Check(chunks.Length == 1 && Str(chunks[0], "exactFilter").EndsWith("/OfflineSibling", StringComparison.OrdinalIgnoreCase),
                "C1566 only offline sibling launched " + platform);
        }
        rows.ShouldAllPass(4);
    }

    [Test]
    public void Costs()
    {
        // Seconds are independent lower bounds from the retained Debug R/P tables. The test must fail if any of these
        // measured heavy classes falls back to the cheap per-node model again.
        var bounds = new Dictionary<string, double>
        {
            ["LandNonCodeSkipTests"] = 2088, ["AgentTaskLandHalfResetTests"] = 1776, ["PostLandMutationCleanupTests"] = 1398,
            ["PostLandMutationDeliveryTests"] = 1662, ["DispatchBaseNotificationTests"] = 336, ["AgentTaskLandConcurrencyTests"] = 576,
            ["AgentTaskLandApprovalRecoveryTests"] = 480, ["AgentTaskLandBoundaryControlledTests"] = 1218, ["DelegationWorktreeTests"] = 636,
            ["DispatcherSiblingInspectionSchedulingTests"] = 576, ["AgentTaskWorktreeLockOutcomeTests"] = 948, ["AgentTaskLandSourceFreshnessTests"] = 630,
            ["AgentTaskLandAdmissionTests"] = 528, ["AgentTaskLandRemoteSourceRetentionCTests"] = 456,
        };
        var costRows = CostRows();
        var missing = bounds.Where(b => !costRows.TryGetValue("Antiphon.Tests.Application." + b.Key, out var row) || RowDouble(row, "seconds") < b.Value)
            .Select(b => b.Key).ToArray();
        var rows = new NightlyCaseRows();
        rows.Check(missing.Length == 0, "C1567 measured heavy prices retained", string.Join(",", missing));
        // One indivisible parameterized method above 40 minutes cannot be split by this planner. Refuse it before any
        // owned test process is launched.
        var node = RosterNode("HugeTests", "Indivisible", "huge");
        NightlyClassCosts Priced(double seconds) => NightlyClassCosts.Of(new Hashtable(StringComparer.OrdinalIgnoreCase) { ["Antiphon.Tests.Application.HugeTests"] = seconds });
        var refusal = Refusal(() => NightlyTestsPlan.ProfileLaunchPlan([node], AllChunk(), Array.Empty<object>(), "antiphon", null, Priced(2401.0)));
        rows.Check(refusal != null && refusal.StartsWith("launch-estimate-ceiling", StringComparison.OrdinalIgnoreCase), "C1567 excessive launch refused", refusal ?? "accepted");
        var accepted = NightlyTestsPlan.ProfileLaunchPlan([node], AllChunk(), Array.Empty<object>(), "antiphon", null, Priced(2400.0));
        rows.Check(accepted.Length == 1 && Num(accepted[0], "seconds") == 2400, "C1567 ceiling boundary accepted");
        rows.Check(Policy().Object.Watchdogs.TryGetValue("antiphon", out var watchdog) && watchdog == 3600000, "C1567 watchdog unchanged");
        rows.ShouldAllPass(4);
    }
}
