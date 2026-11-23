using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0599 V-1, in process. Unit 15 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md deleted
/// scripts/test-release-gate.ps1; these are its Test-C599_E2EWatchdog, Test-C599_MetadataParsers,
/// Test-C599_EligibilityCensus and Test-C599_ExpandedCoverage with their row names. Every row drives the PRODUCTION policy
/// reader, the production discovery/diagnostic parsers, the production census and the production native suite verdict
/// against a real policy file whose canonical hash the reader itself validates (New-C599Policy:
/// NightlyPolicyTests.Policy); nothing here asserts on source text.
///
/// <para>The decisive behaviour: in a profile lane <c>OptIn</c> alone no longer excludes a case,
/// so an unclassified UID stays required and fails the census instead of silently inheriting
/// manual status - while the default nightly lane keeps its old meaning exactly.</para>
/// </summary>
[Category("Integration")]
public sealed class ReleaseGatePolicyTests
{
    private static readonly string ShaA = new('a', 40);

    [Test]
    public void C599_E2EWatchdog()
    {
        // CARD-1345: the shipped policy resolves the rc e2e suite to 120 minutes. The rc session-runner suite gets 30 minutes;
        // antiphon and the fallback stay put.
        var rows = new NightlyCaseRows();
        var ship = NightlyPolicy.Read(NightlyPlanCases.Repo, "").Object;
        var e2e = Antiphon.Nightly.NightlyWatchdog.PolicyWatchdogMilliseconds(ship, "e2e", 3600000);
        var antiphon = Antiphon.Nightly.NightlyWatchdog.PolicyWatchdogMilliseconds(ship, "antiphon", 3600000);
        var session = Antiphon.Nightly.NightlyWatchdog.PolicyWatchdogMilliseconds(ship, "session-runner", 3600000);
        var missing = Antiphon.Nightly.NightlyWatchdog.PolicyWatchdogMilliseconds(ship, "not-a-suite", 3600000);
        rows.Check(e2e == 7200000, "C599 resolved e2e watchdog is 7200000", e2e.ToString());
        rows.Check(antiphon == 3600000, "C599 antiphon watchdog stays 3600000", antiphon.ToString());
        rows.Check(session == 1800000, "C599 resolved session-runner watchdog is 1800000", session.ToString());
        rows.Check(missing == 3600000, "C599 missing watchdog key keeps the 3600000 fallback", missing.ToString());
        rows.ShouldAllPass(4,
            "C599 resolved e2e watchdog is 7200000",
            "C599 antiphon watchdog stays 3600000",
            "C599 resolved session-runner watchdog is 1800000",
            "C599 missing watchdog key keeps the 3600000 fallback");
    }

    [Test]
    public void C599_MetadataParsers()
    {
        using var fx = NightlyFixture.Create("c599-metadata");
        var rows = new NightlyCaseRows();
        var exclusions = RcExclusions(fx);

        // The JSON discovery document and the MTP diagnostic records must agree on UIDs and must both PRESERVE the raw
        // category rather than consuming it.
        object[] jsonNodes =
        [
            DiscoveryNode("u1", "BoardE2ETests", "Board_loads", "E2E", "OptIn"),
            DiscoveryNode("u2", "SmokeE2ETests", "Home_renders", "E2E"),
        ];
        var discovery = fx.PathOf("discovery.json");
        WriteDiscovery(discovery, jsonNodes);
        var nodes = PsValue.AsArray(PsValue.Prop(NightlyDiagnosticLog.ReadDiscovery(discovery), "Nodes"));
        rows.Check(nodes.Length == 2, "C599 MetadataParsers json parser reads both nodes", nodes.Length.ToString());
        var optin = nodes.First(n => NightlyPlanCases.Str(n, "Uid") == "u1");
        rows.Check(NightlyPlanCases.Strings(optin, "Categories").Contains("OptIn"), "C599 MetadataParsers json parser preserves the raw OptIn category");
        rows.Check(PsValue.IsTrue(PsValue.Prop(optin, "Excluded")), "C599 MetadataParsers json OptIn still excluded in the default lane");

        const string record = "TestNodeUid { Value = u1 }, TypeName = BoardE2ETests, MethodName = Board_loads, Namespace = E2E, TestMetadataProperty { Key = OptIn }";
        var diagnostic = NightlyDiagnosticLog.Record(record, "Discovered", null);
        rows.Check(NightlyPlanCases.Str(diagnostic, "Uid") == "u1", "C599 MetadataParsers diagnostic parser reads the same uid", NightlyPlanCases.Str(diagnostic, "Uid"));
        rows.Check(NightlyPlanCases.Strings(diagnostic, "Categories").Contains("OptIn"), "C599 MetadataParsers diagnostic parser preserves the raw category");
        rows.Check(NightlyPlanCases.Str(diagnostic, "ClassName") == NightlyPlanCases.Str(optin, "ClassName"), "C599 MetadataParsers both parsers agree on the class identity",
            NightlyPlanCases.Str(diagnostic, "ClassName") + " vs " + NightlyPlanCases.Str(optin, "ClassName"));

        // Profile-aware: the SAME nodes now resolve by declared rows, not by category.
        foreach (var node in nodes)
        {
            var defaultExcluded = NightlyCensus.Excluded(node, null, false);
            var profileExcluded = NightlyCensus.Excluded(node, exclusions, true);
            if (NightlyPlanCases.Str(node, "Uid") == "u1")
                rows.Check(defaultExcluded && !profileExcluded, "C599 MetadataParsers OptIn excluded by default but required in a profile",
                    "default=" + defaultExcluded + " profile=" + profileExcluded);
            else
                rows.Check(!defaultExcluded && !profileExcluded, "C599 MetadataParsers plain node required in both lanes");
        }

        var jsonUids = NightlyCensus.RequiredUids(nodes, [], exclusions, true).Select(PsValue.Str).Order(StringComparer.Ordinal).ToArray();
        var diagnosticUids = NightlyCensus.RequiredUids([diagnostic, nodes[1]], [], exclusions, true).Select(PsValue.Str).Order(StringComparer.Ordinal).ToArray();
        rows.Check(jsonUids.SequenceEqual(diagnosticUids), "C599 MetadataParsers json and diagnostic required uid sets are identical",
            string.Join(",", jsonUids) + " vs " + string.Join(",", diagnosticUids));

        // A truncated record is an error, never a silently dropped row.
        var truncated = Thrown(() => NightlyDiagnosticLog.Record("TestNodeUid { Value = u9 }", "Discovered", null));
        rows.Check(truncated.Contains("truncated", StringComparison.OrdinalIgnoreCase), "C599 MetadataParsers truncated diagnostic record throws", truncated);

        var duplicate = fx.PathOf("dup.json");
        WriteDiscovery(duplicate, [jsonNodes[0], jsonNodes[0]]);
        var duplicated = Thrown(() => NightlyDiagnosticLog.ReadDiscovery(duplicate));
        rows.Check(duplicated.Contains("duplicate discovery uid", StringComparison.OrdinalIgnoreCase), "C599 MetadataParsers duplicate uid throws", duplicated);
        rows.ShouldAllPass(11,
            "C599 MetadataParsers json parser preserves the raw OptIn category",
            "C599 MetadataParsers diagnostic parser preserves the raw category",
            "C599 MetadataParsers OptIn excluded by default but required in a profile",
            "C599 MetadataParsers json and diagnostic required uid sets are identical",
            "C599 MetadataParsers truncated diagnostic record throws",
            "C599 MetadataParsers duplicate uid throws");
    }

    [Test]
    public void C599_EligibilityCensus()
    {
        using var fx = NightlyFixture.Create("c599-eligibility");
        var rows = new NightlyCaseRows();
        var exclusions = RcExclusions(fx);
        var nodes = Nodes(false);

        var census = NightlyCensus.Census(nodes, exclusions, true);
        rows.Check(PsValue.IsTrue(PsValue.Prop(census, "Ok")), "C599 EligibilityCensus profile census is well formed", string.Join(",", NightlyPlanCases.Strings(census, "Duplicates")));
        var requiredCount = (int)NightlyPlanCases.Num(census, "RequiredCount");
        var excludedCount = (int)NightlyPlanCases.Num(census, "ExcludedCount");
        rows.Check(requiredCount + excludedCount == nodes.Length, "C599 EligibilityCensus every uid has exactly one disposition",
            "req=" + requiredCount + " exc=" + excludedCount + " total=" + nodes.Length);

        var required = Uids(census, "Required");
        var excluded = Uids(census, "Excluded");
        rows.Check(excluded.Contains("u-live-1"), "C599 EligibilityCensus named live class is excluded", string.Join(",", excluded));
        rows.Check(excluded.Contains("u-mixed-live"), "C599 EligibilityCensus named live METHOD is excluded", string.Join(",", excluded));
        rows.Check(required.Contains("u-mixed-offline"), "C599 EligibilityCensus sibling method of a mixed class stays required", string.Join(",", required));
        rows.Check(required.Contains("u-optin-1") && required.Contains("u-optin-2"), "C599 EligibilityCensus OptIn alone does not exclude in the rc profile", string.Join(",", required));
        foreach (var row in PsValue.AsArray(PsValue.Prop(census, "Excluded")))
        {
            var reason = NightlyPlanCases.Str(row, "reason");
            var owner = NightlyPlanCases.Str(row, "owner");
            rows.Check(!string.IsNullOrWhiteSpace(reason) && !string.IsNullOrWhiteSpace(owner),
                "C599 EligibilityCensus exclusion " + NightlyPlanCases.Str(row, "uid") + " records reason and owner", reason + "/" + owner);
        }

        // A brand new unclassified case must stay required, not inherit manual status.
        var withNew = NightlyCensus.Census(Nodes(true), exclusions, true);
        var required2 = Uids(withNew, "Required");
        rows.Check(required2.Contains("u-brand-new"), "C599 EligibilityCensus a new unclassified case stays required", string.Join(",", required2));
        rows.Check(NightlyPlanCases.Str(withNew, "Digest") != NightlyPlanCases.Str(census, "Digest"), "C599 EligibilityCensus a new case changes the census digest",
            NightlyPlanCases.Str(withNew, "Digest"));

        // The default nightly lane is unchanged by any of this.
        var defaultRequired = Uids(NightlyCensus.Census(nodes, null, false), "Required");
        rows.Check(!defaultRequired.Contains("u-optin-1"), "C599 EligibilityCensus default lane still excludes OptIn", string.Join(",", defaultRequired));
        rows.Check(defaultRequired.Contains("u-mixed-live"), "C599 EligibilityCensus default lane ignores profile method rows", string.Join(",", defaultRequired));

        var duplicates = NightlyPlanCases.Strings(NightlyCensus.Census([nodes[0], nodes[0]], exclusions, true), "Duplicates");
        rows.Check(duplicates.Length == 1, "C599 EligibilityCensus duplicate uid is reported", string.Join(",", duplicates));
        var allExcluded = NightlyCensus.Census([nodes[0]], exclusions, true);
        rows.Check(!PsValue.IsTrue(PsValue.Prop(allExcluded, "Ok")) && (int)NightlyPlanCases.Num(allExcluded, "RequiredCount") == 0,
            "C599 EligibilityCensus zero required uids is not ok", NightlyPlanCases.Str(allExcluded, "RequiredCount"));
        rows.Check(NightlyPlanCases.Str(census, "Digest") == NightlyPlanCases.Str(NightlyCensus.Census(nodes, exclusions, true), "Digest"),
            "C599 EligibilityCensus digest is stable for the same roster", NightlyPlanCases.Str(census, "Digest"));
        rows.ShouldAllPass(15,
            "C599 EligibilityCensus every uid has exactly one disposition",
            "C599 EligibilityCensus named live class is excluded",
            "C599 EligibilityCensus named live METHOD is excluded",
            "C599 EligibilityCensus sibling method of a mixed class stays required",
            "C599 EligibilityCensus OptIn alone does not exclude in the rc profile",
            "C599 EligibilityCensus a new unclassified case stays required",
            "C599 EligibilityCensus default lane still excludes OptIn",
            "C599 EligibilityCensus zero required uids is not ok");
    }

    [Test]
    public void C599_ExpandedCoverage()
    {
        using var fx = NightlyFixture.Create("c599-expanded");
        var rows = new NightlyCaseRows();
        var exclusions = RcExclusions(fx);
        object[] discoveryNodes =
        [
            DiscoveryNode("e1", "BoardE2ETests", "Board_loads", "E2E", "OptIn"),
            DiscoveryNode("e2", "BoardE2ETests", "Board_filters", "E2E", "OptIn"),
            DiscoveryNode("e3", "LiveCanaryTests", "Runs_live", "E2E", "OptIn"),
        ];
        NightlyTrxRow[] trxRows = [new("e1", "E2E.BoardE2ETests", "Board_loads"), new("e2", "E2E.BoardE2ETests", "Board_filters")];
        NightlyTestNode[] executed =
        [
            new("e1", "BoardE2ETests", "Board_loads", ClassName: "E2E.BoardE2ETests"), new("e2", "BoardE2ETests", "Board_filters", ClassName: "E2E.BoardE2ETests"),
        ];
        var before = DateTime.UtcNow.AddMinutes(-5);
        object Verdict(string run, string diagnostics, object? profile, bool aware) => NightlyChunkVerdict.NativeSuiteVerdict(Path.Combine(run, "e2e.trx"),
            Path.Combine(run, "e2e.discovery.json"), diagnostics, run, before, 0, ShaA, ShaA, "r", "r", "h", "h", [], profile, aware, null, "", null);
        string Run(string name, NightlyTrxRow[] trx, object[] discovery, NightlyTestNode[] execution)
        {
            var run = fx.PathOf(name);
            Directory.CreateDirectory(Path.Combine(run, "exec"));
            NightlyFixture.WriteTrx(Path.Combine(run, "e2e.trx"), trx);
            WriteDiscovery(Path.Combine(run, "e2e.discovery.json"), discovery);
            NightlyFixture.WriteExecution(Path.Combine(run, "exec", "log.diag"), execution);
            return run;
        }
        string Reasons(object verdict) => string.Join(";", NightlyPlanCases.Strings(verdict, "reasons"));
        bool Flag(object verdict, string name) => PsValue.IsTrue(PsValue.Prop(verdict, name));
        int Count(object verdict, string name) => (int)NightlyPlanCases.Num(PsValue.Prop(verdict, "census"), name);

        var run1 = Run("run", trxRows, discoveryNodes, executed);
        var verdict = Verdict(run1, Path.Combine(run1, "exec"), exclusions, true);
        rows.Check(Flag(verdict, "coverageComplete"), "C599 ExpandedCoverage control: both required rows executed is complete", Reasons(verdict));
        rows.Check(PsValue.Prop(verdict, "census") != null && Count(verdict, "RequiredCount") == 2, "C599 ExpandedCoverage census reports 2 required uids",
            NightlyPlanCases.Str(PsValue.Prop(verdict, "census"), "RequiredCount"));
        rows.Check(Count(verdict, "ExcludedCount") == 1, "C599 ExpandedCoverage census reports the 1 named exclusion", NightlyPlanCases.Str(PsValue.Prop(verdict, "census"), "ExcludedCount"));

        // Remove one expanded required row: incomplete.
        var run2 = Run("run2", [trxRows[0]], discoveryNodes, [executed[0]]);
        var missing = Verdict(run2, Path.Combine(run2, "exec"), exclusions, true);
        rows.Check(!Flag(missing, "coverageComplete"), "C599 ExpandedCoverage a removed expanded row is incomplete", Reasons(missing));
        rows.Check(Reasons(missing).Contains("missing-expanded-row", StringComparison.Ordinal), "C599 ExpandedCoverage names the missing expanded row", Reasons(missing));

        // In the DEFAULT lane the same evidence would have zero required rows, which is exactly the silent-green the
        // profile lane must refuse.
        var defaultVerdict = Verdict(run1, Path.Combine(run1, "exec"), null, false);
        rows.Check(PsValue.Prop(defaultVerdict, "census") != null && Count(defaultVerdict, "RequiredCount") == 0, "C599 ExpandedCoverage default lane would require zero uids here",
            NightlyPlanCases.Str(PsValue.Prop(defaultVerdict, "census"), "RequiredCount"));

        // A profile lane whose every node is excluded is never green.
        var run3 = Run("run3", [new("e3", "E2E.LiveCanaryTests", "Runs_live")], [discoveryNodes[2]],
            [new("e3", "LiveCanaryTests", "Runs_live", ClassName: "E2E.LiveCanaryTests")]);
        var zero = Verdict(run3, Path.Combine(run3, "exec"), exclusions, true);
        rows.Check(!Flag(zero, "coverageComplete") && !Flag(zero, "testsPassed"), "C599 ExpandedCoverage zero required uids is never green", Reasons(zero));
        rows.Check(Reasons(zero).Contains("zero-required-uid", StringComparison.Ordinal), "C599 ExpandedCoverage names zero-required-uid", Reasons(zero));

        // Missing execution diagnostics is incomplete even with a green TRX.
        var noDiagnostics = Verdict(run1, "", exclusions, true);
        rows.Check(!Flag(noDiagnostics, "coverageComplete"), "C599 ExpandedCoverage missing execution diagnostics is incomplete", Reasons(noDiagnostics));
        rows.ShouldAllPass(9,
            "C599 ExpandedCoverage control: both required rows executed is complete",
            "C599 ExpandedCoverage a removed expanded row is incomplete",
            "C599 ExpandedCoverage names the missing expanded row",
            "C599 ExpandedCoverage default lane would require zero uids here",
            "C599 ExpandedCoverage zero required uids is never green",
            "C599 ExpandedCoverage missing execution diagnostics is incomplete");
    }

    // New-C599Policy's rc exclusions (the live class LiveCanaryTests, the live method MixedTests.Live_row), read from a real
    // policy file through the production reader, as the driver's @(...) of them.
    internal static object[] RcExclusions(NightlyFixture fx)
    {
        var policy = NightlyPolicy.Read(NightlyPlanCases.Repo, NightlyPolicyTests.Write(fx.Root, "policy", NightlyPolicyTests.Policy(2))).Object;
        return NightlyPolicy.ProfileExclusions(policy, "rc", "", "windows").Cast<object>().ToArray();
    }

    // New-C599Nodes: the live class, two OptIn nodes, a plain node and a mixed class's live and offline methods (and, with
    // the unclassified flag, a brand new OptIn class no row names).
    private static object[] Nodes(bool withUnclassified)
    {
        var nodes = new List<object>
        {
            NightlyPlanCases.Node("E2E.LiveCanaryTests", "LiveCanaryTests", "Runs_live", "u-live-1", "OptIn"),
            NightlyPlanCases.Node("E2E.BoardE2ETests", "BoardE2ETests", "Board_loads", "u-optin-1", "OptIn"),
            NightlyPlanCases.Node("E2E.BoardE2ETests", "BoardE2ETests", "Board_filters", "u-optin-2", "OptIn"),
            NightlyPlanCases.Node("E2E.SmokeE2ETests", "SmokeE2ETests", "Home_renders", "u-plain-1"),
            NightlyPlanCases.Node("A.MixedTests", "MixedTests", "Live_row", "u-mixed-live"),
            NightlyPlanCases.Node("A.MixedTests", "MixedTests", "Offline_row", "u-mixed-offline"),
        };
        if (withUnclassified) nodes.Add(NightlyPlanCases.Node("E2E.BrandNewTests", "BrandNewTests", "Never_seen", "u-brand-new", "OptIn"));
        return nodes.ToArray();
    }

    private static string[] Uids(object census, string name) => PsValue.AsArray(PsValue.Prop(census, name)).Select(row => NightlyPlanCases.Str(row, "uid")).ToArray();

    // One node of Write-C487Discovery's document as the driver gave it (uid, type, method, namespace, categories, state).
    private static object DiscoveryNode(string uid, string type, string method, string ns, params string[] categories) =>
        new { uid, type, method, @namespace = ns, categories, state = "Discovered" };

    // Write-C487Discovery: an antiphon-tunit-discovery-v1 document holding the nodes as given.
    private static void WriteDiscovery(string path, object[] nodes) => NightlyFixture.WriteJson(path, new
    {
        format = NightlyDiagnosticLog.DiscoveryFormat, tunitVersion = NightlyDiagnosticLog.SupportedTunit, mtpVersion = NightlyDiagnosticLog.SupportedMtp,
        assemblyHash = "c487-test-hash", nodes,
    });

    // Get-NightlyRefusalText of a throw: the message (a reflection wrapper's inner one); "" when nothing was thrown.
    private static string Thrown(Action action)
    {
        try { action(); }
        catch (System.Reflection.TargetInvocationException e) when (e.InnerException != null) { return e.InnerException.Message; }
        catch (Exception e) { return e.Message; }
        return "";
    }
}
