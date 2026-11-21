using System.Collections;
using System.Diagnostics;
using System.Text.Json;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

// CARD-1514 V7/V8: the planned manifest of a packed launch plan, the phase-timing environment of a native child, and launch
// completion on its success and failure paths. The planner, the manifests and the summary are the production C#
// (NightlyTestsPlan, TestPhaseManifests, TestPhaseRunSummary), in-process since W2-nightly S09; the cases start the staged
// timing probe as a real child.
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TestPhaseSupervisorTests
{
    private const string HugeClass = "Antiphon.Tests.C1514.Huge";

    [Test]
    [Timeout(180_000)]
    public async Task C1514T_PlannedManifestAndEnvironment(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        var scope = JournalFixtures.Scope();
        var nodes = PackNodes(root);
        // The table the planner prices with and the planned manifest reads (NightlyTestsPlan.ClassCostTable, a copy). Two
        // nodes at 1300s sum past the 2400s lone-class budget, so the class splits, while each method group stays within
        // the launch ceiling.
        var costPath = Path.Combine(C1514Support.RepoRoot(), "scripts", "lib", "nightly-class-costs.json");
        var table = NightlyTestsPlan.ClassCostTable(costPath);
        table[HugeClass] = 1300;
        object[] exclusions = [new PlainObject(("Class", "Antiphon.Tests.C1514.Leaf"), ("Methods", new object[] { "Skip" }))];
        object[] chunks = [new PlainObject(("id", "all"), ("classes", Array.Empty<object>()))];
        var plan1 = NightlyTestsPlan.ProfileLaunchPlan(nodes, chunks, exclusions, "antiphon", null!, NightlyClassCosts.Of(table));
        var plan2 = NightlyTestsPlan.ProfileLaunchPlan(nodes, chunks, exclusions, "antiphon", null!, NightlyClassCosts.Of(table));
        PlanText(plan1).ShouldBe(PlanText(plan2), "plans-equal");
        var maxClasses = 0;
        var has30 = false;
        var sampleAlone = false;
        var hugeSeconds = 0.0;
        var packedSeconds = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var g in plan1)
        {
            var simple = PsValue.AsArray(PsValue.Prop(g, "classes")).Select(c => PsValue.Str(c).Split('.')[^1]).ToList();
            maxClasses = Math.Max(maxClasses, simple.Count);
            has30 |= simple.Count == 30;
            sampleAlone |= simple.Count == 1 && simple[0] == "Sample";
            if (simple.Contains("Huge")) hugeSeconds = PsValue.Double(PsValue.Prop(g, "seconds"));
            packedSeconds[PsValue.Str(PsValue.Prop(g, "id"))] = PsValue.Double(PsValue.Prop(g, "seconds"));
        }
        sampleAlone.ShouldBeTrue();
        maxClasses.ShouldBeLessThanOrEqualTo(30);
        has30.ShouldBeTrue();
        hugeSeconds.ShouldBe(1300d);
        var entry = new PlainObject(("Document", new PlainObject(("Nodes", nodes))), ("InProgress", Array.Empty<object>()),
            ("Views", new Dictionary<string, object>(StringComparer.Ordinal)));
        var view = NightlyDiscoveryIndex.View(entry, exclusions, true);
        var required = new HashSet<string>(PsValue.AsArray(PsValue.Prop(view, "RequiredUids")).Select(PsValue.Str), StringComparer.Ordinal);
        var planned = TestPhaseManifests.WritePlanned(new TestPhasePlannedRequest
        {
            LogRoot = root, RunId = "run-v7", Attempt = "1", Suite = "antiphon", SourceDigest = "src", AssemblyDigest = "asm", PolicyDigest = "pol",
            CostFileDigest = "cost", AssemblyId = "Antiphon.Tests", Groups = plan1, DiscoveryNodes = nodes, ProfileExclusions = exclusions,
            ClassCostPath = costPath, ClassCostTable = table,
        }, scope);
        planned.ShouldBe(Path.Combine(root, "timing", "groups-planned.json"));
        File.Exists(planned).ShouldBeTrue("missing planned-file");

        using var manifestDoc = JsonDocument.Parse(File.ReadAllText(planned));
        var manifest = manifestDoc.RootElement;
        manifest.GetProperty("requiredPartitionExact").GetBoolean().ShouldBeTrue("required-partition-exact");
        manifest.GetProperty("runId").GetString().ShouldBe("run-v7");
        required.ShouldNotContain("leaf-skip");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var hugeBasis = "";
        var sawSampleExtra = false;
        foreach (var group in manifest.GetProperty("groups").EnumerateArray())
        {
            var ids = group.GetProperty("plannedTestIds");
            ids.ValueKind.ShouldBe(JsonValueKind.Array);
            packedSeconds.TryGetValue(group.GetProperty("chunk").GetString()!, out var expectedSeconds).ShouldBeTrue();
            group.GetProperty("plannedSeconds").GetDouble().ShouldBe(expectedSeconds);
            var filter = group.GetProperty("filter").GetString() ?? "";
            filter.Contains("(Sample*)", StringComparison.Ordinal).ShouldBeFalse();
            if (filter.Contains("(SampleExtra*)", StringComparison.Ordinal))
                sawSampleExtra = true;
            var classes = string.Join(",", group.GetProperty("classes").EnumerateArray().Select(item => item.GetString()));
            if (classes.Contains(HugeClass, StringComparison.Ordinal))
                hugeBasis = group.GetProperty("pricingBasis").GetString() ?? "";
            var ordered = new List<string>();
            foreach (var id in ids.EnumerateArray())
            {
                var text = id.GetString()!;
                seen.Add(text).ShouldBeTrue("required-partition-exact");
                ordered.Add(text);
                text.ShouldNotBe("leaf-skip", "required-partition-exact");
            }

            var paramA = ordered.IndexOf("param-a");
            var paramB = ordered.IndexOf("param-b");
            if (paramA >= 0 || paramB >= 0)
            {
                paramA.ShouldBeGreaterThanOrEqualTo(0);
                paramB.ShouldBeGreaterThan(paramA);
            }
        }

        seen.Count.ShouldBe(required.Count, "required-partition-exact");
        foreach (var id in required)
            seen.ShouldContain(id);
        sawSampleExtra.ShouldBeTrue();
        hugeBasis.ShouldBe("measured");
        seen.ShouldContain("leaf-keep");
        seen.ShouldContain("huge-1");
        seen.ShouldContain("huge-2");

        // The launch environment: a child inheriting the phase variables loses them unless it is the native test child.
        var timing = Path.Combine(root, "timing");
        var inherited = new Hashtable(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry pair in Environment.GetEnvironmentVariables()) inherited[(string)pair.Key] = pair.Value;
        inherited["ANTIPHON_TEST_PHASE_ROOT"] = timing;
        inherited["ANTIPHON_TEST_PHASE_RUN_ID"] = "run-v7";
        inherited["ANTIPHON_TEST_PHASE_GROUP_ID"] = "group-v7";
        inherited["ANTIPHON_TEST_PHASE_ATTEMPT"] = "1";
        inherited["ANTIPHON_TEST_PHASE_SUITE"] = "antiphon";
        inherited.Remove("C1514_UNSET_WITNESS");
        var discovery = NightlyPolicy.SafeChildEnvironment(null!, "antiphon", inherited);
        TestPhaseEnvironment.Remove(discovery);
        var build = NightlyPolicy.SafeChildEnvironment(null!, "antiphon", inherited);
        TestPhaseEnvironment.Remove(build);
        var client = NightlyPolicy.SafeChildEnvironment(null!, "client", inherited);
        TestPhaseEnvironment.Remove(client);
        foreach (var map in new[] { discovery, build, client })
            TestPhaseEnvironment.VariableNames.Any(map.ContainsKey).ShouldBeFalse("only-native-test-child-enabled");
        var phase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry pair in discovery) phase[(string)pair.Key] = pair.Value?.ToString() ?? "";
        TestPhaseEnvironment.Enable(phase, root, "run-v7", "g1", "1", "antiphon", "rc", "", timing);
        phase["C1514_CHILD"] = "1";
        phase["C1514_EMPTY_WITNESS"] = "";
        // New-NightlyChildStartInfo's twin: the start information's environment is exactly the map.
        var start = OwnedLauncher.ChildStartInfo("dotnet", [C1514Support.StagedProbe()], root, phase);
        var launchLines = new[] { "ANTIPHON_TEST_PHASE_ROOT", "ANTIPHON_TEST_PHASE_RUN_ID", "ANTIPHON_TEST_PHASE_GROUP_ID", "ANTIPHON_TEST_PHASE_ATTEMPT",
            "ANTIPHON_TEST_PHASE_SUITE", "C1514_EMPTY_WITNESS", "C1514_UNSET_WITNESS" }
            .Select(name => name + "=" + (start.Environment.TryGetValue(name, out var value) ? value : "ABSENT")).ToList();
        launchLines.ShouldContain("ANTIPHON_TEST_PHASE_ROOT=" + timing);
        launchLines.ShouldContain("C1514_EMPTY_WITNESS=");
        launchLines.ShouldContain("C1514_UNSET_WITNESS=ABSENT");
        var ran = await C1514Support.RunProcess(start, cancellationToken);
        ran.ExitCode.ShouldBe(0, ran.Text);
        TestPhaseRunSummary.Run(root, 5, scope);
        File.Exists(Path.Combine(timing, "summary.json")).ShouldBeTrue("launch-summary-absent");
        var selfEnv = File.ReadAllText(Path.Combine(timing, "self-env.txt"));
        selfEnv.ShouldContain("ANTIPHON_TEST_PHASE_ROOT=", Case.Sensitive);
        var childEnv = File.ReadAllText(Path.Combine(timing, "child-env.txt"));
        childEnv.ShouldNotContain("ANTIPHON_TEST_PHASE_ROOT", Case.Sensitive, "only-native-test-child-enabled");
        childEnv.ShouldNotContain("ANTIPHON_TEST_PHASE_RUN_ID", Case.Sensitive, "only-native-test-child-enabled");
        childEnv.ShouldNotContain("ANTIPHON_TEST_PHASE_GROUP_ID", Case.Sensitive, "only-native-test-child-enabled");
        childEnv.ShouldNotContain("ANTIPHON_TEST_PHASE_ATTEMPT", Case.Sensitive, "only-native-test-child-enabled");
        childEnv.ShouldNotContain("ANTIPHON_TEST_PHASE_SUITE", Case.Sensitive, "only-native-test-child-enabled");
    }

    [Test]
    [Timeout(180_000)]
    public async Task C1514T_ActualAndFailurePaths(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        await C1514Support.LaunchProbe(root, enabled: true, cancellationToken);
        var scope = JournalFixtures.Scope();
        var timing = Path.Combine(root, "timing");
        Directory.CreateDirectory(timing);
        await File.WriteAllTextAsync(Path.Combine(timing, "groups-planned.json"), "{\"version\":1,\"runId\":\"run-a\",\"assembly\":\"asm1\"}", cancellationToken);
        static PlainObject Verdict(int exit) => new(("ExitCode", exit), ("coverageComplete", false), ("testsPassed", false));
        void Kept(object back, object verdict, int exit)
        {
            back.ShouldBeSameAs(verdict, "original-verdict-preserved");
            PsValue.Prop(back, "ExitCode").ShouldBe(exit, "original-verdict-preserved");
            PsValue.IsTrue(PsValue.Prop(back, "coverageComplete")).ShouldBeFalse("original-verdict-preserved");
            PsValue.IsTrue(PsValue.Prop(back, "testsPassed")).ShouldBeFalse("original-verdict-preserved");
        }
        var success = Verdict(2);
        Kept(TestPhaseManifests.CompleteLaunch(success, root, Path.Combine(root, "run.trx"), "antiphon", "success", "run-v8", "1", 1.5, null, scope), success, 2);
        var missing = Verdict(1);
        Kept(TestPhaseManifests.CompleteLaunch(missing, root, Path.Combine(root, "no-such.trx"), "antiphon", "missing", "run-v8", "1", 0, null, scope), missing, 1);
        var bad = Path.Combine(root, "bad-root");
        Directory.CreateDirectory(bad);
        await File.WriteAllTextAsync(Path.Combine(bad, "timing"), "not-a-directory", cancellationToken);
        var unwritable = Verdict(9);
        Kept(TestPhaseManifests.CompleteLaunch(unwritable, bad, Path.Combine(bad, "missing.trx"), "antiphon", "bad", "run-v8", "1", 0, null, scope), unwritable, 9);
        var throwRoot = Path.Combine(root, "throw-root");
        Directory.CreateDirectory(Path.Combine(throwRoot, "timing"));
        await File.WriteAllTextAsync(Path.Combine(throwRoot, "timing", "groups-planned.json"), "{\"version\":1,\"runId\":\"run-v8\"}", cancellationToken);
        var thrown = Verdict(4);
        Kept(TestPhaseManifests.CompleteLaunch(thrown, throwRoot, Path.Combine(root, "run.trx"), "antiphon", "throw", "run-v8", "1", 0, null, scope,
            () => throw new InvalidOperationException("summary-boom")), thrown, 4);
        File.ReadAllText(Path.Combine(throwRoot, "timing", "phase-summary-failed")).ShouldContain("phase-summary-failed", Case.Sensitive);

        using var executed = JsonDocument.Parse(File.ReadAllText(Path.Combine(timing, "groups-executed.json")));
        executed.RootElement.GetProperty("manufacturedPassed").GetBoolean().ShouldBeFalse();
        executed.RootElement.GetProperty("runId").GetString().ShouldBe("run-v8");
        JsonElement successChunk = default;
        JsonElement missingChunk = default;
        foreach (var chunk in executed.RootElement.GetProperty("chunks").EnumerateArray())
        {
            if (chunk.GetProperty("chunk").GetString() == "success")
                successChunk = chunk;
            if (chunk.GetProperty("chunk").GetString() == "missing")
                missingChunk = chunk;
        }

        successChunk.ValueKind.ShouldBe(JsonValueKind.Object);
        missingChunk.GetProperty("trxMissing").GetBoolean().ShouldBeTrue();
        missingChunk.GetProperty("manufacturedPassed").GetBoolean().ShouldBeFalse();
        missingChunk.GetProperty("outcomes").GetArrayLength().ShouldBe(0);
        var trxIds = new HashSet<string>(StringComparer.Ordinal);
        var trxPassed = 0;
        var trx = System.Xml.Linq.XDocument.Load(Path.Combine(root, "run.trx"));
        foreach (var node in trx.Descendants().Where(item => item.Name.LocalName == "UnitTestResult"))
        {
            trxIds.Add((string?)node.Attribute("testId") ?? "");
            if ((string?)node.Attribute("outcome") == "Passed")
                trxPassed++;
        }

        successChunk.GetProperty("manufacturedPassed").GetBoolean().ShouldBeFalse();
        successChunk.GetProperty("trxMissing").GetBoolean().ShouldBeFalse();
        successChunk.GetProperty("passedCount").GetInt32().ShouldBe(trxPassed);
        successChunk.GetProperty("runId").GetString().ShouldBe("run-v8");
        var failed = 0;
        foreach (var row in successChunk.GetProperty("outcomes").EnumerateArray())
        {
            trxIds.ShouldContain(row.GetProperty("testId").GetString()!);
            if (row.GetProperty("outcome").GetString() == "Failed")
                failed++;
        }

        failed.ShouldBeGreaterThan(0);
        successChunk.GetProperty("outcomes").GetArrayLength().ShouldBe(trxIds.Count);
    }

    // The discovery the pack is planned from: 40 nodes written as an antiphon-tunit-discovery-v1 document and read back by
    // the production discovery reader.
    private static object[] PackNodes(string work)
    {
        var rows = new List<object>();
        void Add(string uid, string type, string method) => rows.Add(new System.Collections.Specialized.OrderedDictionary
        {
            { "uid", uid }, { "assembly", "Antiphon.Tests" }, { "namespace", "Antiphon.Tests.C1514" }, { "type", type }, { "method", method },
            { "signature", "" }, { "state", "Discovered" }, { "categories", Array.Empty<object>() },
        });
        Add("sample-1", "Sample", "Run");
        Add("extra-1", "SampleExtra", "Run");
        for (var i = 1; i <= 31; i++)
        {
            var name = "T" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
            Add("tiny-" + name, name, "Run");
        }
        Add("huge-1", "Huge", "Work");
        Add("huge-2", "Huge", "Other");
        Add("param-a", "Param", "Run");
        Add("param-b", "Param", "Run");
        Add("leaf-keep", "Leaf", "Keep");
        Add("leaf-skip", "Leaf", "Skip");
        var doc = new System.Collections.Specialized.OrderedDictionary
        {
            { "format", "antiphon-tunit-discovery-v1" }, { "tunitVersion", "1.44.0" }, { "mtpVersion", "2.2.2" }, { "assemblyHash", "c1514-v7" },
            { "nodes", rows.ToArray() },
        };
        var path = Path.Combine(work, "discovery.json");
        TestPhaseFiles.WriteTextAtomic(path, TestPhaseJson.Text(doc, 0));
        return PsValue.AsArray(PsValue.Prop(NightlyDiagnosticLog.ReadDiscovery(path), "Nodes"));
    }

    // Get-C1514PlanText: each group's id, sorted classes, filters, carried node ids and seconds.
    private static string PlanText(object[] plan) => string.Join("\n", plan.Select(g =>
    {
        var classes = PsValue.AsArray(PsValue.Prop(g, "classes")).Select(PsValue.Str).Order(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> nodes = PsValue.Prop(g, "nodes") is null ? [] : PsValue.AsArray(PsValue.Prop(g, "nodes")).Select(n => PsValue.Str(PsValue.Prop(n, "Uid")));
        return PsValue.Str(PsValue.Prop(g, "id")) + "|" + string.Join(",", classes) + "|" + PsValue.Str(PsValue.Prop(g, "exactFilter")) + "|"
            + PsValue.Str(PsValue.Prop(g, "groupFilter")) + "|" + PsValue.Str(PsValue.Prop(g, "methodFilter")) + "|" + string.Join(",", nodes) + "|"
            + PsValue.Str(PsValue.Prop(g, "seconds"));
    }));
}
