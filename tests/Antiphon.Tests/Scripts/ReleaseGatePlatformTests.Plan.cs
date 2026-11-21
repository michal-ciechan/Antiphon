using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using TUnit.Core;
using static Antiphon.Tests.Scripts.Nightly.NightlyPlanCases;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The rc platform cases of scripts/test-rc-platform.ps1 whose subject is the launch plan (CARD-1415 skip exclusions and
/// the Linux session-runner admission), in-process since W2-nightly S11: the shipped policy, the census, the coverage
/// verdict and NightlyTestsPlan. The rows keep their C1323 names.
/// </summary>
public sealed partial class ReleaseGatePlatformTests
{
    private static object[] CoverageControl(IEnumerable<(string Identity, string Outcome)> rows, PolicyFile policy) =>
        [NightlyChunkVerdict.CoverageVerdict(rows.Select(r => (object)new PlainObject(("Identity", r.Identity), ("Outcome", r.Outcome))).ToArray(), 0, "a", "a", "r", "r",
            policy.Hash, policy.Hash, Array.Empty<string>(), Array.Empty<object>(), false, "", null, null)];

    // The uids a plan launches: a whole-class chunk launches every node of its classes, an exact chunk its own nodes.
    private static List<string> Launched(object[] plan, IReadOnlyList<PlainObject> nodes)
    {
        var launched = new List<string>();
        foreach (var group in plan)
        {
            if (string.IsNullOrWhiteSpace(Str(group, "exactFilter")))
            {
                var classes = Classes(group);
                launched.AddRange(nodes.Where(n => classes.Contains((string)n["ClassName"]!, StringComparer.Ordinal)).Select(n => (string)n["Uid"]!));
            }
            else launched.AddRange(PsValue.AsArray(PsValue.Prop(group, "nodes")).Select(n => Str(n, "Uid")));
        }
        return launched;
    }

    [Test]
    public void C1415_SkipExclusions()
    {
        // CARD-1415: the shipped rc policy excludes exactly the by-design skipped identities, so a Windows rc launch never
        // runs them and its census stays complete; siblings stay required. CARD-1425: the CARD-1281 producer-pack cases
        // run from an in-test fixture, so they are required on every platform and only the two Windows-only rows remain.
        var rows = new NightlyCaseRows();
        var shipped = Policy();
        const string ns = "Antiphon.Tests.Application";
        var methods = new (string Class, string[] Methods)[]
        {
            ("AdvisoryChangePolicyTests", ["C1303_R12_LandRefusesUnavailableCorrectedEvidence", "C1281_ReceiptCorrection", "C1281_IncompleteNeverClean", "C1281_AdvisoryDoesNotRerun"]),
            ("PipelineSpeedIntegrationTests", ["C1281_B1Authorization", "C1281_B1ReceiptImport", "C1281_B1CorrectionTransaction", "C1281_WiredRoundtrip", "C1281_WiredRoundtripDelivery"]),
            ("AgentTaskDispatchBaseGuardTests", ["C1213_RemoteSourceLandingKeepsLegacyIdentity", "C1213_SourceLandingKeepsLegacyIdentity"]),
            ("AgentTaskDispatchFailureTests", ["a_git_timeout_fails_one_task_not_the_tick", "A_failing_dispatch_does_not_block_the_queue"]),
        };
        var nodes = new List<PlainObject>();
        foreach (var (cls, list) in methods)
            foreach (var m in list)
            {
                // C1303 R12 is argument-expanded: three UIDs, one method segment.
                var cases = m == "C1303_R12_LandRefusesUnavailableCorrectedEvidence" ? new[] { "missing-blob", "missing-bundle", "changed-blob" } : new[] { "" };
                foreach (var c in cases)
                {
                    var node = Node(ns + "." + cls, cls, m, cls + "." + m + "(" + c + ")", "Integration");
                    node["State"] = "Discovered";
                    nodes.Add(node);
                }
            }
        var packMethods = new[] { "C1303_R12_LandRefusesUnavailableCorrectedEvidence", "C1281_ReceiptCorrection", "C1281_IncompleteNeverClean", "C1281_B1Authorization",
            "C1281_B1ReceiptImport", "C1281_B1CorrectionTransaction", "C1281_WiredRoundtrip" };
        var pack = nodes.Where(n => packMethods.Contains((string)n["Method"]!, StringComparer.Ordinal)).Select(n => (string)n["Uid"]!).ToArray();
        var windowsOnly = nodes.Where(n => (string)n["Method"]! is "C1213_RemoteSourceLandingKeepsLegacyIdentity" or "a_git_timeout_fails_one_task_not_the_tick")
            .Select(n => (string)n["Uid"]!).ToArray();
        var exclusionRows = shipped.Object.Profiles["rc"].Exclusions.Where(r => r.Owner == "CARD-1415").ToArray();
        rows.Check(exclusionRows.Length == 2 && exclusionRows.All(r => r.Platform == "windows")
            && !exclusionRows.Any(r => string.IsNullOrWhiteSpace(r.Reason) || r.Suite != "antiphon" || r.Methods.Length == 0),
            "C1323 c1415-two-method-scoped-rows", string.Join(";", exclusionRows.Select(r => r.Suite + "/" + r.Class + "/" + r.Platform)));
        var missing = new List<string>();
        var application = Path.Combine(Repo, "tests", "Antiphon.Tests", "Application");
        foreach (var row in exclusionRows)
            foreach (var m in row.Methods)
            {
                var hit = Directory.EnumerateFiles(application, row.Class + "*.cs", SearchOption.TopDirectoryOnly)
                    .Any(f => File.ReadLines(f).Any(line => line.Contains("Task " + m + "(", StringComparison.OrdinalIgnoreCase)));
                if (!hit) missing.Add(row.Class + "." + m);
            }
        rows.Check(missing.Count == 0, "C1323 c1415-excluded-methods-exist", string.Join(",", missing));
        var packLaunched = new List<string>();
        var nodeArray = nodes.Cast<object>().ToArray();
        foreach (var platform in new[] { "windows", "linux" })
        {
            var exclusions = Exclusions(shipped, "rc", "antiphon", platform);
            var expected = platform == "windows" ? windowsOnly : Array.Empty<string>();
            var census = NightlyCensus.Census(nodeArray, exclusions, true);
            var excludedRows = PsValue.AsArray(PsValue.Prop(census, "Excluded"));
            var excluded = excludedRows.Select(r => Str(r, "uid")).ToArray();
            rows.Check(SameSet(excluded, expected) && excludedRows.All(r => Str(r, "owner") == "CARD-1415"), "C1323 c1415-" + platform + "-excludes-exactly", string.Join(",", excluded));
            var plan = NightlyTestsPlan.SuiteChunks(nodeArray, AllChunk(), exclusions, true, true, "antiphon", null, Costs());
            var launched = Launched(plan, nodes);
            var required = nodes.Select(n => (string)n["Uid"]!).Where(u => !expected.Contains(u, StringComparer.Ordinal)).ToArray();
            rows.Check(SameSet(launched, required), "C1323 c1415-" + platform + "-launches-only-required", string.Join(",", launched));
            packLaunched.AddRange(pack.Where(u => launched.Contains(u, StringComparer.Ordinal)).Select(u => platform + "/" + u));
            // The executor reports only launched cases; all passing leaves no skipped row.
            var verdict = CoverageControl(launched.Select(u => (u, "Passed")), shipped)[0];
            rows.Check(PsValue.IsTrue(PsValue.Prop(verdict, "coverageComplete")) && PsValue.IsTrue(PsValue.Prop(verdict, "testsPassed")) && Num(verdict, "skipped") == 0,
                "C1323 c1415-" + platform + "-census-complete");
        }
        // CARD-1425: all nine producer-pack cases are launched on both platforms.
        rows.Check(pack.Length == 9 && packLaunched.Count == 18, "C1323 c1425-pack-methods-required", string.Join(",", packLaunched));
        // Control: the same identities run and skipped (no exclusion) leave the census incomplete.
        var control = CoverageControl(nodes.Select(n => ((string)n["Uid"]!, windowsOnly.Contains((string)n["Uid"]!, StringComparer.Ordinal) ? "Skipped" : "Passed")), shipped)[0];
        rows.Check(!PsValue.IsTrue(PsValue.Prop(control, "coverageComplete")) && Num(control, "skipped") == 2, "C1323 c1415-launched-skips-stay-incomplete");
        rows.ShouldAllPass(10, "C1323 c1415-two-method-scoped-rows", "C1323 c1415-excluded-methods-exist",
            "C1323 c1415-windows-excludes-exactly", "C1323 c1415-windows-launches-only-required",
            "C1323 c1415-windows-census-complete", "C1323 c1415-linux-excludes-exactly",
            "C1323 c1425-pack-methods-required", "C1323 c1415-launched-skips-stay-incomplete");
    }

    [Test]
    public void LinuxSessionRunnerAdmission()
    {
        var rows = new NightlyCaseRows();
        var shipped = Policy();
        // Independent expanded roster; check it against source rather than deriving expectations from the exclusions
        // under test. These are Linux checkpoint obligations, not Windows ones.
        var roster = new (string Class, (string Method, int Count)[] Methods)[]
        {
            ("UnixPtyArgvAdmissionTests", [("Malformed_inputs_reach_existing_runtime_validation", 1), ("Null_exe_keeps_existing_validation_message_on_launch_routes", 2),
                ("Nul_request_is_refused_before_effects", 4), ("Nul_refusal_is_named_on_both_launch_routes", 2), ("Phone_home_nul_refusal_is_named", 2),
                ("Platform_and_backend_boundaries_keep_existing_verdicts", 4)]),
            ("AgentPinPublicationTests", [("V34_PublicationAdmission", 1), ("V14_PublicationReplay", 1), ("V14_PublicationFences", 1),
                ("V13_PublicationCustody", 1), ("V15_PublicationCleanup", 1), ("V14_PublicationSerialization", 1)]),
            ("AgentPinWorkspaceStoreTests", [("V32_InspectPosixPaths", 1), ("V13_InspectPathSafety", 1), ("V34_InspectProtocolAdmission", 1)]),
        };
        var sourceMethod = new Regex(@"(?s)\[Test\]\s*(?<attrs>(?:\[Arguments\([^\r\n]*\)\]\s*)*)public async Task (?<method>\w+)\(");
        var nodes = new List<PlainObject>();
        foreach (var (cls, methods) in roster)
        {
            var source = File.ReadAllText(Path.Combine(Repo, "tests", "Antiphon.SessionRunner.Tests", cls + ".cs"));
            var found = sourceMethod.Matches(source);
            var actual = found.Select(m => m.Groups["method"].Value).ToArray();
            var countsMatch = found.All(m =>
            {
                var count = Math.Max(1, Regex.Matches(m.Groups["attrs"].Value, @"\[Arguments\(").Count);
                return methods.Any(x => x.Method == m.Groups["method"].Value && x.Count == count);
            });
            rows.Check(SameSet(actual, methods.Select(x => x.Method)) && countsMatch, "C1323 linux-runner-source-roster-" + cls);
            foreach (var (method, count) in methods)
                for (var i = 0; i < count; i++)
                {
                    var node = Node("Antiphon.SessionRunner.Tests." + cls, cls, method, cls + "." + method + "/" + i, "Integration");
                    node["State"] = "Discovered";
                    nodes.Add(node);
                }
        }
        var linuxUids = nodes.Select(n => (string)n["Uid"]!).ToArray();
        var sibling = Node("Antiphon.SessionRunner.Tests.PortableSiblingTests", "PortableSiblingTests", "Runs", "portable-sibling", "Integration");
        sibling["State"] = "Discovered";
        nodes.Add(sibling);
        var rosterClasses = roster.Select(r => r.Class).ToArray();
        var policyRows = shipped.Object.Profiles["rc"].Exclusions.Where(r => r.Suite == "session-runner" && rosterClasses.Contains(r.Class, StringComparer.Ordinal)).ToArray();
        rows.Check(policyRows.Length == 3 && SameSet(policyRows.Select(r => r.Class), rosterClasses)
            && !policyRows.Any(r => r.Platform != "windows" || r.Methods.Any(m => !string.IsNullOrEmpty(m)) || string.IsNullOrEmpty(r.Reason) || string.IsNullOrEmpty(r.Owner)),
            "C1323 linux-runner-three-windows-rows");
        var nodeArray = nodes.Cast<object>().ToArray();
        foreach (var platform in new[] { "windows", "linux" })
        {
            var exclusions = Exclusions(shipped, "rc", "session-runner", platform);
            var expectedRequired = platform == "windows" ? new[] { "portable-sibling" } : nodes.Select(n => (string)n["Uid"]!).ToArray();
            var expectedExcluded = platform == "windows" ? linuxUids : Array.Empty<string>();
            var census = NightlyCensus.Census(nodeArray, exclusions, true);
            var required = PsValue.AsArray(PsValue.Prop(census, "Required")).Select(r => Str(r, "uid")).ToArray();
            var excludedRows = PsValue.AsArray(PsValue.Prop(census, "Excluded"));
            rows.Check(PsValue.IsTrue(PsValue.Prop(census, "Ok")) && SameSet(required, expectedRequired) && SameSet(excludedRows.Select(r => Str(r, "uid")), expectedExcluded),
                "C1323 linux-runner-" + platform + "-census", "required=" + Num(census, "RequiredCount") + " excluded=" + Num(census, "ExcludedCount"));
            rows.Check(!excludedRows.Any(r => string.IsNullOrEmpty(Str(r, "reason")) || string.IsNullOrEmpty(Str(r, "owner"))), "C1323 linux-runner-" + platform + "-attribution");
            var chunks = NightlyTestsPlan.SuiteChunks(nodeArray, AllChunk(), exclusions, true, true, "antiphon", null, Costs());
            var launched = Launched(chunks, nodes);
            rows.Check(SameSet(launched, expectedRequired), "C1323 linux-runner-" + platform + "-launch-plan", string.Join(",", launched));
        }
        // Frozen RC authority always resolves Windows, including on a Linux coordinator.
        var authority = ReleaseAuthority.PolicyFromBytes(File.ReadAllBytes(Path.Combine(Repo, "tests", "test-execution-policy.json")));
        // The authority reader takes JSON-shaped values (a dictionary per node), not custom objects.
        var authorityNodes = nodes.Select(n => (object)new Dictionary<string, object?> { ["uid"] = n["Uid"], ["class"] = n["ClassName"], ["method"] = n["Method"] }).ToArray();
        rows.Check(SameSet(ReleaseAuthority.RequiredUids(authorityNodes, authority.Exclusions, "session-runner"), ["portable-sibling"]), "C1323 linux-runner-frozen-windows-authority");
        // A skip alone is insufficient: the coverage verdict deliberately keeps it incomplete.
        var control = CoverageControl(nodes.Select(n => ((string)n["Uid"]!, linuxUids.Contains((string)n["Uid"]!, StringComparer.Ordinal) ? "Skipped" : "Passed")), shipped)[0];
        rows.Check(!PsValue.IsTrue(PsValue.Prop(control, "coverageComplete")) && Num(control, "skipped") == 24, "C1323 linux-runner-skip-alone-incomplete");
        rows.Check(linuxUids.Length == 24 && shipped.Object.Profiles["rc"].RequiredSuites.Contains("session-runner", StringComparer.Ordinal), "C1323 linux-runner-24-obligations-retained");
        rows.ShouldAllPass(13);
    }
}
