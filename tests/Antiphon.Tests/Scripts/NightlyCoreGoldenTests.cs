using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Scripts.NightlyCoreGolden;

namespace Antiphon.Tests.Scripts;

// The compiled nightly core (scripts/lib/Antiphon.Nightly) called in-process with plain values, each area against the
// golden its PowerShell copy produced (scripts/fixtures/nightly/core-golden): census, plan, discovery index view, the
// TRX/diagnostic cross-check, the evidence JSON writer, the diagnostic record parser, the summary helpers and
// writer, the evidence records, coverage parts, the active-chunk marker, the verdicts, packing and launch groups, the
// project walk, evidence files, documents, source gaps, TRX identities, JSON files, the discovery writer, history
// links, the planned manifest and the view key. These were the NC cases of scripts/test-nightly-core.ps1.
[Category("Integration")]
public sealed class NightlyCoreGoldenTests
{
    private static void Golden(string name, string actual, params string[] witnesses)
    {
        var (ok, detail) = Compare(name, actual, witnesses);
        ok.ShouldBeTrue(detail);
    }

    private static string Refusal(Action action)
    {
        try { action(); }
        catch (Exception ex) { return ex.Message; }
        return "accepted";
    }

    /// <summary>Get-NCOutcome: the value, or REFUSED and the refusal text.</summary>
    private static object? Outcome(Func<object?> body)
    {
        try { return body(); }
        catch (Exception ex) { return "REFUSED " + ex.Message; }
    }

    // ---- Census, plan, discovery index view, cross-check, the evidence JSON writer (Test-NCCoreMatchesFallback).

    private sealed record CensusFixture(object[] Nodes, object[] Rules, string[] Classes, PolicyFile Policy);

    private static CensusFixture CensusInputs()
    {
        var policy = NightlyPolicy.Read(Repo, "");
        var nodes = FixtureNodes(4000);
        // Default-lane categories and a duplicate UID, so the non-profile and duplicate paths run too.
        nodes[3]["Categories"] = new object[] { "OptIn" };
        nodes[5]["Categories"] = new object[] { "Explicit" };
        nodes[7]["Excluded"] = true;
        nodes.Add(nodes[11]);
        var real = NightlyPolicy.ProfileExclusions(policy.Object, "rc", "antiphon", "windows");
        // Method rows, a whole-class row after a method row (first declaring row wins), an absent method list.
        var rules = real.Cast<object>().Concat(new object[]
        {
            Obj(("Class", nodes[200]["Type"]), ("Methods", new object[] { nodes[200]["Method"]! }), ("Reason", "method"), ("Owner", "a")),
            Obj(("Class", nodes[200]["Type"]), ("Methods", Array.Empty<object>()), ("Reason", "class"), ("Owner", "b")),
            Obj(("Class", nodes[900]["ClassName"]), ("Methods", null), ("Reason", "absent"), ("Owner", "c")),
        }).ToArray();
        var classes = nodes.Select(n => (string)n["ClassName"]!).Distinct(StringComparer.Ordinal).Take(9).ToArray();
        return new CensusFixture(nodes.Cast<object>().ToArray(), rules, classes, policy);
    }

    [Test]
    public void Census_and_required_uids_match_the_golden()
    {
        var f = CensusInputs();
        var body = Ordered(
            ("census", NightlyCensus.Census(f.Nodes, f.Rules, true)),
            ("defaultCensus", NightlyCensus.Census(f.Nodes, null, false)),
            ("required", NightlyCensus.RequiredUids(f.Nodes, f.Classes, f.Rules, true)),
            ("defaultRequired", NightlyCensus.RequiredUids(f.Nodes, [], null, false)));
        Golden("census", PsJson.Pipeline([body]));
    }

    [Test]
    public void Profile_launch_plan_matches_the_golden()
    {
        var f = CensusInputs();
        var chunks = new object[] { Obj(("id", "all"), ("classes", Array.Empty<object>())) };
        var plan = ProfileLaunchPlan(f.Nodes, chunks, f.Rules, "antiphon", f.Policy.Object);
        Golden("plan", PsJson.Pipeline(plan));
    }

    /// <summary>
    /// Get-NightlyProfileLaunchPlan (what Resolve-NightlySuiteChunks runs for a native profile with exclusions): the
    /// leaves, the compiled launch groups, each launch's estimate ceiling and watchdog admission noted on it.
    /// </summary>
    internal static List<object?> ProfileLaunchPlan(object[] nodes, object[] chunks, object[] exclusions, string suiteId, object policyObject)
    {
        var leaves = LaunchPlanner.ProfileExecutionChunks(nodes, chunks, exclusions);
        var costTable = LaunchPlanner.NeedsCostTable(leaves) ? ClassCostTable() : null;
        var result = LaunchPlanner.ProfileLaunchGroups(leaves, nodes, null, costTable, 0.5, 1.5, 4, 2, 900, 2400, 30);
        var error = PsValue.Prop(result, "Error");
        if (error != null) throw new InvalidOperationException((string)error);
        var plan = PsValue.AsArray(PsValue.Prop(result, "Groups")).ToList();
        foreach (var launch in plan)
        {
            var seconds = PsValue.Double(PsValue.Prop(launch, "seconds"));
            var classes = PsValue.AsArray(PsValue.Prop(launch, "classes")).Select(PsValue.Str).ToArray();
            var e2e = classes.Any(c => c.StartsWith("Antiphon.E2E.", StringComparison.Ordinal));
            var ceiling = e2e ? 1800 : 2400;
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0 || seconds > ceiling)
                throw new InvalidOperationException("launch-estimate-ceiling id=" + PsValue.Str(PsValue.Prop(launch, "id")));
            var admission = Antiphon.Nightly.NightlyWatchdog.ResolveLaunch(e2e ? "e2e" : suiteId, seconds, "default", "", null, null, policyObject);
            if (!(bool)PsValue.Prop(admission, "Admitted")!) throw new InvalidOperationException("watchdog-admission-refused");
            PsValue.AddMemberForce(launch, "watchdogSeconds", (double)PsValue.Prop(admission, "WatchdogSeconds")!);
            PsValue.AddMemberForce(launch, "watchdogReason", (string)PsValue.Prop(admission, "Reason")!);
            PsValue.AddMemberForce(launch, "watchdogAdmitted", (bool)PsValue.Prop(admission, "Admitted")!);
        }
        return plan;
    }

    /// <summary>Get-NightlyClassCostTable: bare class -> measured seconds per node (scripts/lib/nightly-class-costs.json).</summary>
    internal static Hashtable ClassCostTable()
    {
        var table = Table();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo, "scripts", "lib", "nightly-class-costs.json")));
        doc.RootElement.GetProperty("format").GetString().ShouldBe("antiphon-nightly-class-costs-v1");
        foreach (var row in doc.RootElement.GetProperty("classes").EnumerateObject())
        {
            var nodes = (int)Math.Round(row.Value.GetProperty("nodes").GetDouble(), MidpointRounding.ToEven);
            var seconds = row.Value.GetProperty("seconds").GetDouble();
            table[row.Name] = seconds / nodes;
        }
        return table;
    }

    [Test]
    public void Discovery_index_view_matches_the_golden()
    {
        var f = CensusInputs();
        var entry = Obj(("Document", Obj(("Nodes", f.Nodes))), ("Views", new Dictionary<string, object>(StringComparer.Ordinal)));
        var view = NightlyDiscoveryIndex.View(entry, f.Rules, true);
        var body = Ordered(("excluded", PsValue.AsArray(PsValue.Prop(view, "Excluded"))), ("byClass", PsValue.Prop(view, "ByClass")),
            ("required", PsValue.Prop(view, "RequiredUids")), ("classes", PsValue.Prop(view, "Classes")),
            ("census", PsValue.Prop(PsValue.Prop(view, "Census"), "Digest")));
        Golden("indexView", PsJson.Pipeline([body]));
    }

    /// <summary>Get-NCSortedCross: the mismatch list sorted (it comes out in hashtable order).</summary>
    private static PlainObject SortedCross(object cross)
    {
        var mismatch = PsValue.AsArray(PsValue.Prop(cross, "Mismatch")).Select(PsValue.Str).ToArray();
        Array.Sort(mismatch, StringComparer.Ordinal);
        return Obj(("Ok", PsValue.Prop(cross, "Ok")), ("Mismatch", mismatch), ("ExtraClasses", PsValue.Prop(cross, "ExtraClasses")));
    }

    [Test]
    public void Trx_diagnostic_cross_check_matches_the_golden()
    {
        var f = CensusInputs();
        var trx = f.Nodes.Take(60).Select(n => (object)Obj(("ClassName", PsValue.Prop(n, "ClassName")), ("MethodName", PsValue.Prop(n, "Method")))).ToArray();
        var cross = SortedCross(NightlyVerdicts.CrossCheck(trx, f.Nodes.Take(50).ToArray(), f.Classes));
        Golden("crossCheck", PsJson.Pipeline([cross]));
    }

    [Test]
    public void Evidence_json_writer_matches_the_golden()
    {
        // The driver's samples in order. Two cannot be built without PowerShell: 'noted' was a string carrying an
        // Add-Member note (the writer writes the string), and 'sw' a [switch] (written as its [string], "True").
        object deep = "leaf";
        for (var i = 0; i < 30; i++) deep = Table(("d" + i.ToString(CultureInfo.InvariantCulture), deep));
        var samples = new (string Key, object? Value)[]
        {
            ("nul", null), ("t", true), ("i8", (byte)200), ("i64", 9007199254740993L), ("dec", 1.1m), ("dbl", 0.1 + 0.2), ("flt", 1.5f),
            ("u32", 7u), ("str", "q \" b \\ n \n r \r t \t c \u0001 u \u00e9"), ("noted", "text"), ("one", new object[] { "only" }),
            ("none", Array.Empty<object>()), ("pso", Obj(("z", 1), ("y", new object[] { 1, new object[] { 2 } }), ("x", null))), ("deep", deep),
            ("date", new DateTime(2026, 1, 2, 3, 4, 5)), ("enum", FileAttributes.Hidden), ("ch", 'x'), ("dbnull", DBNull.Value), ("sw", null),
        };
        var lines = new List<string>();
        foreach (var (key, value) in samples) lines.Add(TestPhaseJson.Text(value, 0) + "|" + TestPhaseJson.Text(value, 20));
        lines.Add(TestPhaseJson.String(null));
        var expected = JsonSerializer.Deserialize<string[]>(Read("json"))!;
        expected.Length.ShouldBe(lines.Count);
        var sw = Array.FindIndex(samples, s => s.Key == "sw");
        for (var i = 0; i < lines.Count; i++)
            if (i != sw) lines[i].ShouldBe(expected[i], "json sample " + (i < samples.Length ? samples[i].Key : "String(null)"));
        // Everything but the [switch] line, as the one compressed text the golden holds.
        lines[sw] = expected[sw];
        Golden("json", PsJson.Pipeline(lines.Cast<object?>().ToList()));
    }

    // ---- Diagnostic records and logs (Test-NCDiagnosticsMatchFallback). ----

    /// <summary>New-D13StateRecord: the reflected short TestNodeUpdateMessage envelope of the C1325 fixtures.</summary>
    private static string D13StateRecord(string uid, string typeName, string method, string stateName, string explanation, string exceptionText = "System.Exception: outer")
    {
        var state = stateName + "TestNodeStateProperty { Explanation = " + explanation;
        if (stateName is not ("Passed" or "Skipped" or "Discovered" or "InProgress")) state += ", Exception = " + exceptionText;
        if (stateName == "Timeout") state += ", Timeout = 00:00:00.0250000";
        state += " }";
        return "TestNodeUpdateMessage { DisplayName = TestNode update, Description = This data is used to report a TestNode state change., Properties = [], ParentTestNodeUid = <null>, TestNode = TestNode { Uid = TestNodeUid { Value = "
            + uid + " }, DisplayName = " + method + ", Properties = [ TestMethodIdentifierProperty { AssemblyFullName = Probe, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null, Namespace = Probe, TypeName = "
            + typeName + ", MethodName = " + method + ", MethodArity = 0, ParameterTypeFullNames = [], ReturnTypeFullName = System.Void }, " + state + " ] } }";
    }

    /// <summary>
    /// The driver's records and logs. The C1325/C1375/D13 records its PowerShell fixtures built are captured in
    /// core-golden/diagnosticInputs.json; the state records, truncations and logs are built here as it built them.
    /// </summary>
    private static (List<string> Records, List<(string Name, string Text)> Logs) DiagnosticInputs()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(GoldenPath("diagnosticInputs"), Encoding.UTF8));
        var root = doc.RootElement;
        var records = root.GetProperty("fixtureRecords").EnumerateArray().Select(e => e.GetString()!).ToList();
        var d13 = root.GetProperty("d13FailureRecord").GetString()!;
        var c1375 = root.GetProperty("c1375Records").EnumerateArray().Select(e => e.GetString()!).ToList();
        var header = root.GetProperty("header").GetString()!;
        var i = 0;
        foreach (var state in new[] { "Passed", "Skipped", "Discovered", "InProgress", "Failed", "Error", "Timeout", "Cancelled", "Unheard" })
        {
            foreach (var type in new[] { "Plain", "Ctor(Arg, Other)", "Bad,Comma", "Open(Arg" })
            {
                i++;
                records.Add(D13StateRecord("Probe.NC." + i.ToString(CultureInfo.InvariantCulture), type, "Run", state,
                    "[Test Failure] boom " + i.ToString(CultureInfo.InvariantCulture), "System.Exception: inner " + i.ToString(CultureInfo.InvariantCulture)));
            }
        }
        // Truncations ([int] of half the length rounds half to even, as PowerShell's [int] does) and a damaged identity.
        foreach (var r in records.ToArray()) records.Add(r[..(int)Math.Round(r.Length / 2.0, MidpointRounding.ToEven)]);
        records.Add(d13.Replace("TestNodeUid { Value = ", "TestNodeUid { Valu = ", StringComparison.Ordinal));
        records.Add(d13.Replace("MethodName = ", "MethodNam = ", StringComparison.Ordinal));
        var logs = new List<(string, string)>
        {
            ("all", header + string.Join("\n", records)),
            ("first-three", header + string.Join("\n", records.Take(3))),
            ("duplicate", header + d13 + "\n" + d13),
            ("no-version", d13),
            ("c1375", header + c1375[0] + "\n" + c1375[1]),
            // Raw discovered-marker count at the text's edges and back to back; leading Unicode whitespace and a
            // zero-width space before the JSON/text decision; whitespace-only text.
            ("marker-edges", "DiscoveredTestNodeStateProperty {" + header + d13 + "\nDiscoveredTestNodeStateProperty {DiscoveredTestNodeStateProperty {"),
            ("lead-space", " \t" + (char)0x2003 + (char)0x00A0 + (char)0x0085 + header + d13),
            ("zwsp-json", " " + (char)0x200B + "{\"Nodes\":[]}"),
            ("blank", " \r\n" + (char)0x3000),
        };
        return (records, logs);
    }

    [Test]
    public void Diagnostic_records_and_logs_match_the_goldens()
    {
        var (records, logs) = DiagnosticInputs();
        var root = NewRoot();
        try
        {
            var paths = logs.Select(log =>
            {
                var path = Path.Combine(root, "nc-" + log.Name + ".diag");
                File.WriteAllText(path, log.Text);
                return path;
            }).ToList();
            var perRecord = new List<object?>();
            foreach (var record in records)
            {
                var property = NightlyDiagnostics.StateProperty(record);
                var state = NightlyDiagnostics.StateName(PsValue.Str(PsValue.Prop(property, "Name")));
                perRecord.Add(Ordered(("property", property), ("state", state), ("split", NightlyDiagnostics.Records(record)),
                    ("asState", Outcome(() => NightlyDiagnosticLog.Record(record, state.Length > 0 ? state : "Failed", property))),
                    ("asDiscovered", Outcome(() => NightlyDiagnosticLog.Record(record, "Discovered", property)))));
            }
            Golden("diagnosticRecords", PsJson.Pipeline([Ordered(("records", perRecord.ToArray()))]));
            // ConvertFrom-NightlyDiagnosticLog has no PowerShell record loop left: whole logs against the golden it produced.
            var whole = paths.Select(path => (object?)Ordered(
                ("execution", Outcome(() => NightlyDiagnosticLog.Read(path, "execution"))),
                ("discovery", Outcome(() => NightlyDiagnosticLog.Read(path, "discovery"))))).ToList();
            // The golden stores Windows paths as <root>\\nc-....diag (JSON's escaped separator); only that placeholder
            // separator is folded, so a Linux run's <root>/nc-....diag compares too.
            var actual = Normalize(PsJson.Pipeline(whole), root).Replace("<root>\\\\", "<root>/", StringComparison.Ordinal);
            var expected = Read("diagnosticLogs").Replace("<root>\\\\", "<root>/", StringComparison.Ordinal);
            actual.ShouldBe(expected, "diagnosticLogs");
            foreach (var witness in new[] { "discovery-count-mismatch", "empty diagnostic" }) expected.ShouldContain(witness);
        }
        finally { DeleteRoot(root); }
    }

    // ---- Summary helpers, the fast rows and the summary writer (Test-NCDiagnosticsMatchFallback.summaryHelpers). ----

    [Test]
    public void Summary_helpers_and_writer_match_the_goldens()
    {
        var root = NewRoot();
        try
        {
            var hashed = Path.Combine(root, "nc-hash.bin");
            File.WriteAllBytes(hashed, Enumerable.Range(0, 256).Select(b => (byte)b).ToArray());
            object?[] values = [null, "", "plain", "=SUM(A1)", "+1", "-2", "@x", "say \"hi\"", true, false, 42L, 3.5, new DateTime(2026, 1, 2, 3, 4, 5)];
            var row = Ordered(("testId", "=id"), ("duration", null), ("joined", true), ("db", 7L), ("outcome", "say \"x\""));
            var fields = row.Keys.Cast<string>().ToArray();
            var stdout = "noise\r\n  ANTIPHON_TEST_PHASE_ID  a.b.c  \r\nC1514_NATIVE 12 x.y\nC1514_NATIVE nospace\nANTIPHON_TEST_PHASE_ID a.b.c\nC1514_NATIVE 1 ";
            var body = Ordered(
                ("cells", values.Select(v => (object)TestPhaseSummary.Cell(v)).ToArray()),
                ("native", TestPhaseSummary.NativeIds(stdout)),
                ("nativeEmpty", TestPhaseSummary.NativeIds("")),
                // The third argument was @('M', BagKey(...)): an array PowerShell passes to the string parameter joined with a space.
                ("bag", new object[] { NightlyClassNames.BagKey("A.B.C(x)", "T", "M " + NightlyClassNames.BagKey(" ", "N.Type", "Run")) }),
                ("outcomes", new[] { "Passed", " FAIL ", "error", "NotRunnable", "pending", "", "weird" }.Select(o => (object)NightlyClassNames.NormalizedOutcome(o)).ToArray()),
                ("sha", NightlyClassNames.FileSha256(hashed)),
                ("csvRow", string.Join(",", fields.Select(k => TestPhaseSummary.Cell(row[k])))));
            var text = PsJson.Pipeline([body]);
            Golden("summaryHelpers", text);
            var rowCore = TestPhaseSummary.Row(row, fields);
            text.ShouldContain(rowCore.Replace("\"", "\\\"", StringComparison.Ordinal));

            // TestPhaseSummaryFast: the whole-roster tests.csv lines and the [bool]$test.incomplete scan.
            var rowTwo = Ordered(("testId", "b"), ("duration", "1.5"), ("joined", false), ("db", 0), ("outcome", ""));
            var fastRows = TestPhaseSummaryFast.Rows(new object[] { row, rowTwo }, fields);
            fastRows.Length.ShouldBe(2);
            fastRows[0].ShouldBe(rowCore);
            fastRows[1].ShouldBe(TestPhaseSummary.Row(rowTwo, fields));
            TestPhaseSummaryFast.Rows(Array.Empty<object>(), ["a"]).Length.ShouldBe(0);
            using var golden = JsonDocument.Parse(Read("summaryWrite"));
            var incomplete = golden.RootElement.GetProperty("incomplete").EnumerateArray().Select(e => e.GetBoolean()).ToArray();
            object?[] flags = [null, false, true, 0, 1, "", "False", Array.Empty<object>(), new object[] { false }];
            incomplete.Length.ShouldBe(flags.Length);
            for (var i = 0; i < flags.Length; i++)
            {
                var tests = new List<object> { Ordered(("incomplete", false)), Ordered(("incomplete", flags[i])), Obj(("other", 1)) };
                TestPhaseSummaryFast.AnyIncomplete(tests).ShouldBe(incomplete[i], "incomplete " + i);
            }
            SummaryWrite(golden.RootElement);
        }
        finally { DeleteRoot(root); }
    }

    /// <summary>
    /// Test-NCSummaryWrite: summary.json against ConvertTo-Json -Depth 50 -Compress, the two sorts against Sort-Object
    /// (the inputs a summary never carries included: doubles, custom objects, Int64 overflow), as PowerShell wrote them
    /// (core-golden/summaryWrite.json).
    /// </summary>
    private static void SummaryWrite(JsonElement golden)
    {
        var nested = Ordered(("a", new object?[] { 1, 2L, null, Array.Empty<object>() }), ("b", new List<object> { "x", Ordered(("deep", Table(("k", "v")))) }));
        var doc = Ordered(("version", 1), ("complete", false), ("quality", new object[] { "a", "b" }), ("none", Array.Empty<object>()), ("one", new object[] { "only" }),
            ("text", "q \" b \\ n \n r \r t \t c " + (char)1 + " u " + (char)0xE9 + " " + (char)0x2028),
            ("dbl", 0.1 + 0.2), ("whole", 1.0), ("big", 1e20), ("tiny", 5e-324), ("neg", -2.5), ("nan", double.NaN),
            ("inf", double.PositiveInfinity), ("ninf", double.NegativeInfinity), ("flt", 1.5f), ("fltWhole", 3f),
            ("dec", 1.1m), ("decWhole", 5m), ("i8", (byte)200), ("u32", 7u), ("u64", ulong.MaxValue),
            ("i64", long.MinValue), ("ch", 'x'), ("dbnull", DBNull.Value),
            ("pso", Obj(("z", 1), ("y", new object[] { 1, new object[] { 2 } }), ("x", null), ("w", Obj(("v", "in"))))),
            ("nested", nested), ("tests", new object[] { Ordered(("testId", "b"), ("db", 1L)), Ordered(("testId", "a"), ("joined", true)) }));
        TestPhaseSummaryWrite.SummaryJson(doc).ShouldBe(golden.GetProperty("json").GetString(), "summary.json");
        foreach (var unsupported in new object[] { Ordered(("when", new DateTime(2026, 1, 2))), Table((1, "int key")) })
            Should.Throw<ArgumentException>(() => TestPhaseSummaryWrite.SummaryJson(unsupported));
        // Ties, culture order, a missing testId, custom objects and nulls; totals that leave Int64 (a sum, the
        // negation of Int64.MinValue) and continue in Double, [long] of strings, doubles and nulls.
        const long max = long.MaxValue;
        object Totals(object? testId, object? db, object? host, object? process, object? fs, object? remainder, bool plain = false)
        {
            (string, object?)[] entries = [("testId", testId), ("db", db), ("host", host), ("process", process), ("fs", fs), ("network", 0), ("fixture", 0), ("overlapMs", 0), ("remainderMs", remainder)];
            return plain ? Obj(entries) : Ordered(entries);
        }
        var rows = new object[]
        {
            Totals("b", max, 1, 0, 0, 0), Totals("B", long.MinValue, 0, 0, 0, 0), Totals("a", 5, "7", 2.5, null, 0),
            Totals((char)0xE4, 12, 0, 0, 0, 0, plain: true), Obj(("other", 1)), Totals("a", max, max, max, 0, 1),
            Totals("c", 12, 0, 0, 0, 0), Totals("C", -max, -max, 0, 0, 0),
        };
        string Tag(IEnumerable<object> items) => string.Join(",", items.Select(i => PsValue.Str(PsValue.Prop(i, "testId")) + "/" + PsValue.Str(PsValue.Prop(i, "db"))));
        var sorted = TestPhaseSummaryWrite.SortByTestId(rows);
        Tag(sorted).ShouldBe(golden.GetProperty("sorted").GetString(), "Sort-Object { [string]$_.testId }");
        Tag(TestPhaseSummaryWrite.RankByTotal(sorted)).ShouldBe(golden.GetProperty("ranked").GetString(), "Sort-Object by -1 * total");
        TestPhaseSummaryWrite.SortByTestId(Array.Empty<object>()).Length.ShouldBe(0);
        TestPhaseSummaryWrite.RankByTotal(Array.Empty<object>()).Length.ShouldBe(0);
    }

    // ---- Executed-chunk outcome rows (Test-NCDiagnosticsMatchFallback.outcomeRows). ----

    /// <summary>ConvertTo-TestPhaseExecutedChunk (test-phase-evidence.ps1) around TestPhaseSummary.OutcomeRows.</summary>
    private static object ExecutedChunk(object? item, string chunk, string runId, string attempt, string suite, int passedCount, object[]? outcomes)
    {
        var prior = new List<object>();
        if (outcomes != null) prior.AddRange(TestPhaseSummary.OutcomeRows(outcomes, "testId", "outcome", true));
        string chunkId = chunk, run = runId, att = attempt, su = suite, trx = "", digest = "";
        double wall = 0;
        bool missing = false;
        var passed = passedCount;
        object? exitCode = null, coverage = null, tests = null;
        if (item != null)
        {
            var names = PsValue.PropertyNames(item);
            bool Has(string name) => names.Contains(name, StringComparer.OrdinalIgnoreCase);
            if (Has("chunk")) chunkId = PsValue.Str(PsValue.Prop(item, "chunk"));
            if (Has("runId")) run = PsValue.Str(PsValue.Prop(item, "runId"));
            if (Has("attempt")) att = PsValue.Str(PsValue.Prop(item, "attempt"));
            if (Has("suite")) su = PsValue.Str(PsValue.Prop(item, "suite"));
            if (Has("hostWallSeconds") && PsValue.Prop(item, "hostWallSeconds") != null) wall = PsValue.Double(PsValue.Prop(item, "hostWallSeconds"));
            if (Has("trxMissing")) missing = PsValue.IsTrue(PsValue.Prop(item, "trxMissing"));
            if (Has("passedCount") && PsValue.Prop(item, "passedCount") != null) passed = Convert.ToInt32(PsValue.Prop(item, "passedCount"), CultureInfo.InvariantCulture);
            if (Has("trx")) trx = PsValue.Str(PsValue.Prop(item, "trx"));
            if (Has("trxDigest")) digest = PsValue.Str(PsValue.Prop(item, "trxDigest"));
            if (Has("exitCode")) exitCode = PsValue.Prop(item, "exitCode");
            if (Has("coverageComplete")) coverage = PsValue.Prop(item, "coverageComplete");
            if (Has("testsPassed")) tests = PsValue.Prop(item, "testsPassed");
            prior = [];
            if (Has("outcomes") && PsValue.Prop(item, "outcomes") != null)
                prior.AddRange(TestPhaseSummary.OutcomeRows(PsValue.AsArray(PsValue.Prop(item, "outcomes")), "testId", "outcome", true));
        }
        return Ordered(("chunk", chunkId), ("runId", run), ("attempt", att), ("suite", su), ("hostWallSeconds", wall), ("trxMissing", missing),
            ("manufacturedPassed", false), ("passedCount", passed), ("outcomes", prior.ToArray()), ("trx", trx), ("trxDigest", digest),
            ("exitCode", exitCode), ("coverageComplete", coverage), ("testsPassed", tests));
    }

    [Test]
    public void Executed_chunk_outcome_rows_match_the_golden()
    {
        var trxRows = new object[]
        {
            Obj(("TestId", "a"), ("Outcome", "Passed")), Obj(("TestId", "b"), ("Outcome", "passed")), Obj(("TestId", "c"), ("Outcome", "Failed")),
            Obj(("TestId", 7), ("Outcome", null)), Obj(("Other", 1)),
        };
        var item = NightlyJson.ParseText("{\"chunk\":\"c1\",\"outcomes\":[{\"testId\":\"x\",\"outcome\":\"Passed\"},null,{\"testId\":5,\"outcome\":\"Skipped\"},{\"outcome\":\"Failed\"}]}", 64, false);
        item.ShouldNotBeNull();
        var rows = TestPhaseSummary.OutcomeRows(trxRows, "TestId", "Outcome", false);
        var passed = TestPhaseSummary.PassedCount(rows);
        var body = Ordered(
            ("manifest", ExecutedChunk(null, "c", "r", "1", "s", passed, rows.Concat(new object?[] { null, Table(("testId", "h"), ("outcome", "Passed")) }).ToArray()!)),
            ("item", ExecutedChunk(item, "c", "r", "1", "s", 0, [])));
        Golden("outcomeRows", PsJson.Pipeline([body]), "\"passedCount\":2");
    }

    // ---- Evidence records: density guard, journal envelope, owner observation (Test-NCEvidenceRecordsMatchFallback). ----

    /// <summary>Write-TestPhaseOwnerObservation into a timing directory: naming, the existing-file refusal and the write.</summary>
    private static void OwnerObservation(object? verdict, string timing, string suite, string chunk, string runId, string attempt)
    {
        try
        {
            var doc = TestPhaseRecords.OwnerDocument(verdict, suite, chunk, runId, attempt);
            if (doc == null) return;
            var path = Path.Combine(timing, "owner-" + suite + "-" + chunk + "-" + PsValue.Str(doc["runId"]) + "-" + PsValue.Str(doc["attempt"]) + ".json");
            if (PsValue.Str(PsValue.Prop(TestPhaseFiles.PathIdentity(path), "Kind")) == "regular") return;
            File.WriteAllText(path, TestPhaseJson.Text(doc, 0), new UTF8Encoding(false));
        }
        catch (Exception)
        {
            // Owner stamps are diagnostic: a failure does not change the verdict.
        }
    }

    [Test]
    public void Evidence_records_match_the_golden()
    {
        var root = NewRoot();
        try
        {
            var dir = Path.Combine(root, "nc-records");
            Directory.CreateDirectory(dir);
            const int arm = 1;
            var verdicts = new object?[]
            {
                Obj(("RootExitedAtUtc", "2026-11-18T01:00:00Z"), ("RootStartedAtUtc", new DateTime(2026, 11, 18, 0, 59, 0)), ("JobEmpty", "false"),
                    ("DrainMilliseconds", "12.5"), ("ExecutionMilliseconds", 40), ("ChildrenExited", 1), ("OutputDrained", true), ("RemainingProcessCount", "2"),
                    ("MissedDescendantPids", new object[] { "7", 9, " " }),
                    ("Descendants", new object?[] { Obj(("Pid", "7"), ("Image", "dotnet")), null,
                        Obj(("pid", 12.5), ("ProcessName", " "), ("StartedAtUtc", "s"), ("ExitedAtUtc", new DateTime(2026, 1, 2, 3, 4, 5))) })),
                Obj(("jobEmpty", true), ("DescendantIdentities", new object[] { "41:2026-11-18T00:00:00Z", "bad", "42:x" }), ("DescendantImages", new object[] { "a", "" }),
                    ("DescendantExitedAtUtc", new object?[] { null, "e2" }), ("missedDescendantPids", 42)),
                Obj(("Descendants", new object[] { Obj(("Pid", "abc")) })),
                Obj(("unrelated", 1)),
                null,
            };
            var densities = new[] { "", "a,b:c", string.Join(",", Enumerable.Range(1, 10)), "{" + string.Join(":", Enumerable.Range(1, 6)) + "}" }
                .Select(text => (object)TestPhaseRecords.Dense(text, 5, null, 0)).ToArray();
            var generation = new string('a', 32);
            const string timingRoot = "C:\\t \"x\"\\timing";
            var fresh = "{\"a\":\"" + "\\" + "u00e9\"}";
            var envelopes = new object[]
            {
                TestPhaseRecords.Envelope("chunk", 12, "p1", generation, timingRoot, "", "antiphon", "all-group-1", "antiphon", "all-group-1", "r", "1", "antiphon", fresh, "", ""),
                TestPhaseRecords.Envelope("gap", 1, "", generation, timingRoot, "why\n", "", "", "", "", "", "", "", "", "", ""),
            };
            var owners = new List<object>();
            for (var i = 0; i < verdicts.Length; i++)
            {
                foreach (var ids in new[] { new[] { "antiphon", "all-group-1", "run-1", "2" }, new[] { "Session-Runner", "Chunk.A", "bad id", "" }, new[] { "bad suite", "c", "r", "1" } })
                {
                    var timing = Path.Combine(dir, "owner-" + arm + "-" + i.ToString(CultureInfo.InvariantCulture) + "-" + ids[0].Replace(' ', '_'));
                    Directory.CreateDirectory(timing);
                    OwnerObservation(verdicts[i], timing, ids[0], ids[1], ids[2], ids[3]);
                    owners.Add(string.Join("|", new DirectoryInfo(timing).GetFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(f => f.Name + "=" + File.ReadAllText(f.FullName))));
                }
            }
            var body = Ordered(("densities", densities), ("envelopes", envelopes), ("owners", owners.ToArray()));
            Golden("evidenceRecords", PsJson.Pipeline([body]), "missedDescendantPids", "owner-Session-Runner-Chunk.A-run-0.json");
        }
        finally { DeleteRoot(root); }
    }

    // ---- Coverage parts: roster digest, latest diagnostic, present-row verdicts (Test-NCCoveragePartsMatchFallback). ----

    /// <summary>
    /// FileAttributes.Hidden is a Windows attribute: on Linux SetAttributes(Hidden) leaves the file visible, so a
    /// hidden file would win by timestamp. The probe uses one exact file.
    /// </summary>
    private static bool HiddenAttributePersists(string parent)
    {
        var dir = Path.Combine(parent, "hidden-capability");
        var file = Path.Combine(dir, "probe.diag");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(file, "probe");
            try { File.SetAttributes(file, FileAttributes.Hidden); } catch (Exception) { return false; }
            return (File.GetAttributes(file) & FileAttributes.Hidden) != 0;
        }
        finally
        {
            if (File.Exists(file)) { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); }
            Directory.Delete(dir);
        }
    }

    [Test]
    public void Coverage_parts_match_the_golden()
    {
        var root = NewRoot();
        try
        {
            // A hashtable entry's reasonClass key is not a property, so it reads as 'platform'; the last roster's
            // 'Platform' fails the case-sensitive check and must refuse.
            var rosters = new object?[]
            {
                new object?[]
                {
                    Obj(("platform", "windows"), ("test", "N.C.M"), ("reasonPrefix", "p"), ("reasonClass", "live-context")), null,
                    Obj(("platform", "linux"), ("test", "A.B.C"), ("reasonPrefix", "q")),
                    Table(("platform", "windows"), ("test", "H.T.M"), ("reasonPrefix", "h"), ("reasonClass", "bogus")),
                    Obj(("platform", "windows"), ("test", "N.C.A"), ("reasonPrefix", "r"), ("ReasonClass", "  ")),
                },
                Array.Empty<object>(),
                null,
                new object[] { Obj(("platform", "windows"), ("test", "X.Y.Z"), ("reasonPrefix", "z"), ("reasonClass", "Platform")) },
            };
            var logs = Path.Combine(root, "nc-logs");
            var cases = new List<(string Name, (string File, int Seconds, bool Hidden)[] Files)>
            {
                ("empty", []),
                ("diagNewest", [("a.diag", -30, false), ("b.diag", -10, false), ("c.log", 0, false)]),
                ("noDiag", [("a.log", -20, false), ("b.txt", -5, false)]),
                ("diagTie", [("a.diag", -10, false), ("b.diag", -10, false)]),
                ("upperExt", [("A.DIAG", -10, false), ("b.log", 0, false)]),
                ("hidden", [("a.diag", -10, false), ("h.diag", 0, true)]),
            };
            // Windows builds the hidden file and the golden requires hidden=a.diag (Get-ChildItem without -Force skipped
            // Hidden). The case is left out only where this host cannot keep the attribute.
            var hiddenSupported = HiddenAttributePersists(root);
            if (!hiddenSupported) cases.RemoveAll(c => c.Name == "hidden");
            var baseTime = DateTime.UtcNow.AddHours(-1);
            foreach (var (name, files) in cases)
            {
                var d = Path.Combine(logs, name);
                Directory.CreateDirectory(d);
                foreach (var (file, seconds, hidden) in files)
                {
                    var p = Path.Combine(d, file);
                    File.WriteAllText(p, file);
                    File.SetLastWriteTimeUtc(p, baseTime.AddSeconds(seconds));
                    if (hidden) File.SetAttributes(p, FileAttributes.Hidden);
                }
            }
            var rows = new object?[] { Obj(("Identity", "a"), ("Outcome", "NotExecuted")), null, Obj(("Identity", "b"), ("Outcome", "Passed")) };
            var digests = rosters.Select(roster => (object)(Refusal(() => NightlyChunkVerdict.RosterDigest(roster)) is var refused && refused != "accepted"
                ? "throw:" + refused : NightlyChunkVerdict.RosterDigest(roster))).ToArray();
            var latest = cases.Select(c =>
            {
                var found = NightlyDiagnosticLog.Latest(Path.Combine(logs, c.Name));
                return (object)(c.Name + "=" + (found == null ? "null" : Path.GetFileName(found)));
            }).ToArray();
            var verdicts = new List<object>();
            foreach (var exit in new[] { 0, 8 })
            {
                foreach (var set in new[] { rows, new object?[] { null }, Array.Empty<object?>() })
                {
                    var v = NightlyChunkVerdict.CoverageVerdict(set!, exit, "a", "a", "r", "r", "p", "p", [], [], false, "windows",
                        new object[] { Obj(("platform", "windows"), ("test", "a"), ("reasonPrefix", "p")) }, null);
                    verdicts.Add(Ordered(("exit", exit), ("tolerated", PsValue.Prop(v, "processExitTolerated")), ("tests", PsValue.Prop(v, "testsPassed")),
                        ("coverage", PsValue.Prop(v, "coverageComplete")), ("reasons", PsValue.AsArray(PsValue.Prop(v, "reasons"))), ("digest", PsValue.Prop(v, "rosterDigest"))));
                }
            }
            var actual = PsJson.Pipeline([Ordered(("digests", digests), ("latest", latest), ("verdicts", verdicts.ToArray()))]);
            var expected = Read("coverageParts");
            foreach (var witness in new[] { "throw:expected-skip roster reasonClass Platform", "diagNewest=b.diag", "diagTie=a.diag", "hidden=a.diag" })
                expected.ShouldContain(witness);
            if (!hiddenSupported)
            {
                // The live list has no hidden row: drop only that golden field (the witness above still requires it).
                var stripped = expected.Replace(",\"hidden=a.diag\"", "", StringComparison.Ordinal);
                stripped.ShouldNotBe(expected);
                actual.ShouldNotContain("\"hidden=");
                expected = stripped;
            }
            actual.ShouldBe(expected, "coverageParts");
        }
        finally { DeleteRoot(root); }
    }

    // ---- The active-chunk marker (Test-NCActiveChunkGolden). ----

    /// <summary>
    /// ConvertFrom-Json of a text the core does not read strictly (a comment, a trailing comma): what PowerShell then
    /// handed TestPhaseRecords.ActiveChunkIdentity. Objects are PlainObjects, whole numbers Int64, others Double.
    /// </summary>
    private static object? LenientJson(string text)
    {
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        static object? Value(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.Object => new PlainObject(e.EnumerateObject().Select(p => (p.Name, Value(p.Value)!)).ToArray()),
            JsonValueKind.Array => e.EnumerateArray().Select(Value).ToArray(),
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.GetRawText().IndexOfAny(['.', 'e', 'E']) < 0 && e.TryGetInt64(out var whole) ? (object)whole : e.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
        return Value(doc.RootElement);
    }

    /// <summary>
    /// Read-QualificationActiveChunkIdentity: the record in active-chunk.json (as the core parses it, else as
    /// ConvertFrom-Json read it) checked and stamped with the digest of exactly its bytes; null where it gives null.
    /// </summary>
    private static object? ActiveChunkIdentity(string directory)
    {
        try
        {
            var path = Path.Combine(directory, "active-chunk.json");
            if (PsValue.Str(PsValue.Prop(TestPhaseFiles.PathIdentity(path), "Kind")) != "regular") return null;
            var bytes = File.ReadAllBytes(path);
            var text = TestPhaseRecords.DecodeText(bytes);
            var record = NightlyJson.ParseText(text, 1024, true) ?? LenientJson(text);
            return TestPhaseRecords.ActiveChunkIdentity(record, bytes);
        }
        catch (Exception) { return null; }
    }

    private static string TypeList(object o) => string.Join(",", PsValue.Properties(o).Select(p => p.Key + ":" + (p.Value == null ? "null" : p.Value.GetType().Name)));

    [Test]
    public void Active_chunk_marker_matches_the_goldens()
    {
        var root = NewRoot();
        try
        {
            var dir = Path.Combine(root, "nc-active");
            Directory.CreateDirectory(dir);
            var h = new string('a', 64);
            (string, object?)[] valid = [("format", "antiphon-qualification-active-chunk-v1"), ("state", "Active"), ("generation", 3), ("runId", "run-1"), ("attempt", "2"),
                ("suite", "antiphon"), ("chunk", "all-group-1"), ("planFile", "groups-planned-antiphon.json"), ("planDigest", h), ("sourceDigest", new string('b', 40)),
                ("policyDigest", h), ("costFileDigest", h), ("assemblyDigest", h)];
            string Variant((string Key, object? Value)[] changes, params string[] drop)
            {
                var v = Ordered();
                foreach (var (key, value) in valid) if (!drop.Contains(key)) v[key] = value;
                foreach (var (key, value) in changes) v[key] = value;
                return PsJson.Indented(v, 5);
            }
            var texts = new (string Name, string Text)[]
            {
                ("valid", Variant([])), ("idle", Variant([("state", "Idle"), ("generation", 1)])), ("stateCase", Variant([("state", "active")])),
                ("gen0", Variant([("generation", 0)])), ("genStr", Variant([("generation", "1")])), ("genDouble", Variant([("generation", 1.5)])),
                ("genBig", Variant([("generation", 99999999999L)])), ("noGen", Variant([], "generation")), ("noFormat", Variant([], "format")),
                ("runIdDate", Variant([("runId", "2026-01-01T00:00:00")])), ("attempt0", Variant([("attempt", "0")])),
                ("suiteUpper", Variant([("suite", "Antiphon"), ("planFile", "groups-planned-Antiphon.json")])), ("planFile", Variant([("planFile", "groups-planned-other.json")])),
                ("digestUpper", Variant([("planDigest", new string('A', 64))])), ("sourceNum", Variant([("sourceDigest", 5)])),
                ("hasIdentity", Variant([("identityDigest", "x")])), ("extra", Variant([("extra", new object[] { 1, 2 })])),
                ("bom", (char)0xFEFF + Variant([])), ("array", "[1,2]"), ("comment", "{ /* c */ " + Variant([])[1..]),
                ("trailing", Variant([]).TrimEnd().TrimEnd('}') + ",}"), ("empty", ""),
            };
            foreach (var (name, text) in texts)
            {
                var d = Path.Combine(dir, name);
                Directory.CreateDirectory(d);
                // LF on every platform, so the identity digests in the golden do not depend on the line ends.
                File.WriteAllText(Path.Combine(d, "active-chunk.json"), text.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
            }
            var ids = texts.Select(t =>
            {
                var r = ActiveChunkIdentity(Path.Combine(dir, t.Name));
                return (object)(t.Name + "=" + (r == null ? "null" : PsJson.Compress(r, 5) + "|" + TypeList(r)));
            }).ToArray();
            var planTexts = new[] { "{\"version\":1,\"groups\":[{\"chunk\":\"a\",\"plannedSeconds\":1.5}],\"runId\":\"r\",\"attempt\":\"1\",\"at\":\"2026-01-01T00:00:00Z\"}", "{\"a\":1,\"A\":2}", "{\"a\":[]}", " {\"x\": null} " };
            var expectedPlans = JsonDocument.Parse(Read("activeChunk")).RootElement.GetProperty("plans").EnumerateArray().Select(e => e.GetString()!).ToArray();
            var plans = new List<object>();
            for (var i = 0; i < planTexts.Length; i++)
            {
                var p = Path.Combine(dir, "plan-" + (i + 1).ToString(CultureInfo.InvariantCulture) + ".json");
                File.WriteAllText(p, planTexts[i]);
                // Read-QualificationPlanJson: a strict object parses in the core; anything else was ConvertFrom-Json's.
                var parsed = NightlyJson.ParseText(File.ReadAllText(p), 1024, true);
                if (parsed == null)
                {
                    expectedPlans[i].ShouldStartWith("throw:", customMessage: "plan " + i + " was handed to ConvertFrom-Json, which refused it");
                    plans.Add(expectedPlans[i]);
                }
                else plans.Add(PsJson.Compress(parsed, 5) + "|" + TypeList(parsed));
            }
            using var jsonGolden = JsonDocument.Parse(Read("activeChunkJson"));
            var convertToJson = jsonGolden.RootElement.GetProperty("texts").EnumerateArray().Select(e => e.GetString()!).ToArray();
            var records = new[]
            {
                Ordered(("format", "antiphon-qualification-active-chunk-v1"), ("state", "Active"), ("generation", 1), ("runId", "r"), ("attempt", "1"), ("suite", "s"), ("chunk", "c"), ("planFile", "f"), ("planDigest", h)),
                Ordered(("format", "x"), ("generation", 12L), ("s", "quote\"in")),
                // Every escape ConvertTo-Json makes, and characters it leaves alone (DEL, apostrophe, HTML, non-ASCII, a
                // surrogate pair), in a value and in a key.
                Ordered(("k\"\t\\", "a\\b" + new string(new[] { (char)8, (char)9, (char)10, (char)12, (char)13, (char)1, (char)31, (char)127, (char)0x85, (char)0x2028, (char)0x2029, (char)0xE9 }) + "'<>&" + char.ConvertFromUtf32(0x1F600)), ("n", -7)),
            };
            var jsons = new List<object>();
            for (var i = 0; i < records.Length; i++)
            {
                var t = TestPhaseRecords.ActiveChunkJson(records[i]);
                // ConvertTo-Json ends lines with the platform's line end; the golden holds them as LF.
                var lines = t.Replace(Environment.NewLine, "\n", StringComparison.Ordinal);
                lines.ShouldNotContain("\r");
                jsons.Add(lines == convertToJson[i] ? "same-as-ConvertTo-Json" : "DIFFERS " + t);
            }
            // Not strings or integers: the writer never builds these, so the core refuses them.
            foreach (var rec in new[] { Ordered(("format", "x"), ("d", 1.5)), Ordered(("format", "x"), ("b", true)), Ordered() })
                jsons.Add(Refusal(() => TestPhaseRecords.ActiveChunkJson(rec)) is var message && message != "accepted" ? "throw:" + message : "DIFFERS");
            Golden("activeChunk", PsJson.Pipeline([Ordered(("ids", ids), ("plans", plans.ToArray()), ("jsons", jsons.ToArray()))]),
                "valid={\\\"format\\\"", "identityDigest", "genStr=null", "runIdDate=null", "same-as-ConvertTo-Json", "throw:active-record-value d", "throw:active-record-empty");
        }
        finally { DeleteRoot(root); }
    }

    // ---- Verdicts: coverage verdict rows, cross-check, required classes, expanded rows (Test-NCVerdictsMatchFallback). ----

    [Test]
    public void Chunk_verdicts_match_the_goldens()
    {
        var policy = NightlyPolicy.Read(Repo, "");
        var nodes = FixtureNodes(1500);
        object?[] outcomes = ["Passed", "Failed", "pass", " Error ", "Skipped", "NotExecuted", "pending", "weird", "", null];
        var trx = new List<object?>();
        var terminal = new List<object?>();
        for (var i = 0; i < 400; i++)
        {
            var n = nodes[i];
            trx.Add(Obj(("ClassName", n["ClassName"]), ("MethodName", n["Method"]), ("Outcome", outcomes[i % outcomes.Length]),
                ("Identity", (string)n["ClassName"]! + "." + (string)n["Method"]!)));
            // The diagnostic side drops some rows and doubles others, so the bags disagree both ways.
            if (i % 17 != 0) terminal.Add(Obj(("Uid", n["Uid"]), ("ClassName", n["ClassName"]), ("Type", n["Type"]), ("Method", n["Method"]), ("Outcome", outcomes[(i + 3) % outcomes.Length])));
            if (i % 29 == 0) terminal.Add(Obj(("Uid", n["Uid"]), ("ClassName", n["ClassName"]), ("Type", n["Type"]), ("Method", ""), ("MethodName", n["Method"]), ("Outcome", "Passed")));
        }
        trx.Add(null);
        terminal.Add(null);
        var classes = nodes.Select(n => (string)n["ClassName"]!).Distinct(StringComparer.Ordinal).ToArray();
        var classSets = new[]
        {
            Array.Empty<string>(), new[] { "" }, classes.Take(4).ToArray(),
            classes.Take(2).Concat(new[] { "No.Such.Class", classes[5].Split('(')[0] }).ToArray(),
        };
        var nodeArray = nodes.Cast<object>().ToArray();
        // ConvertTo-NightlyCoverageVerdict has no PowerShell copy left: its rows against the golden the copy produced.
        var coverage = Ordered(
            ("verdict", NightlyChunkVerdict.CoverageVerdict(trx.ToArray()!, 0, "a", "A", "r", "r", "p", "p", [], [], false, "", null, null)),
            ("verdictNull", NightlyChunkVerdict.CoverageVerdict(null!, 1, "a", "b", "", "", "", "", [], [], false, "", null, null)),
            ("verdictEmpty", NightlyChunkVerdict.CoverageVerdict([], 0, "", "", "", "", "", "", [], [], false, "", null, null)));
        Golden("coverageVerdicts", PsJson.Pipeline([coverage]), "failed-row", "zero-terminal-rows", "sha-mismatch");
        var entry = Obj(("Document", Obj(("Nodes", nodeArray))), ("Views", new Dictionary<string, object>(StringComparer.Ordinal)));
        var view = NightlyDiscoveryIndex.View(entry, NightlyPolicy.ProfileExclusions(policy.Object, "rc", "antiphon", "windows"), true);
        var body = Ordered(
            ("perClassSet", classSets.Select(set => (object)Ordered(
                ("cross", SortedCross(NightlyVerdicts.CrossCheck(trx.ToArray(), terminal.ToArray(), set))),
                ("present", NightlyChunkVerdict.RequiredClassesPresent(trx.ToArray()!, set)),
                ("required", NightlyDiscoveryIndex.RequiredUids(view, set)))).ToArray()),
            ("expanded", NightlyChunkVerdict.ExpandedRowsPresent(nodeArray, terminal.ToArray()!, nodes.Take(300).Select(n => (string)n["Uid"]!).ToArray())),
            ("expandedNull", NightlyChunkVerdict.ExpandedRowsPresent(nodeArray, null!, null!)),
            ("crossNull", NightlyVerdicts.CrossCheck(null!, null!, null!)));
        var text = PsJson.Pipeline([body]);
        Golden("verdicts", text);
        text.ShouldContain(" trx=");
    }

    // ---- Launch plan helpers: class group filter, first-fit packing (Test-NCLaunchPlanMatchesFallback). ----

    [Test]
    public void Group_filter_and_packing_match_the_goldens()
    {
        var cases = new (string[]? Classes, string Filter)[]
        {
            (["N.S.Alpha", "N.S.Beta"], "/*/N.S/(Alpha*)|(Beta*)/*"), (["N.S.Alpha", "N.S.AlphaTwo"], "/*/N.S/(Alpha*)|(AlphaTwo*)/*"),
            (["N.S.Alpha(ctor)", "N.S.Beta"], "/*/N.S/(Alpha*)|(Beta*)/*"), (["N.S.Alpha", "M.S.Beta"], "/*/N.S/(Alpha*)|(Beta*)/*"),
            (["N.S.Alpha", "N.S.alpha"], "/*/N.S/(Alpha*)|(alpha*)/*"), (["N.S.Alpha"], "/*/N.S/(Alpha*)|(Beta*)/*"),
            (["N.S.Alpha", "N.S.Beta", "N.S.Gamma"], "/*/N.S/(Alpha*)|(Beta*)|(Gamma*)/*"), (["N.S.Alpha", "N.S.Beta"], "/*/N.S/(Alpha*)/*"),
            (["N.S.Alpha", "N.S.Beta"], "/*/N.S/Alpha*|Beta*/*"), (["N.S.Alpha", "N.S.Alpha"], "/*/N.S/(Alpha*)|(Beta*)/*"),
            (null, "/*/N.S/(Alpha*)|(Beta*)/*"), (["N.S.Alpha", "N.S.Beta"], ""),
        };
        var filters = cases.Select(c => (object?)NightlyLaunchPlan.ClassGroupFilter(c.Classes!, c.Filter)).ToList();
        var text = PsJson.Pipeline(filters);
        Golden("groupFilter", text);
        text.ShouldContain("true");
        text.ShouldContain("false");
        // Split-NightlyItemsBySeconds (NightlyLaunchPlan.FirstFitBins): first fit with ties, items with no Seconds (the
        // 0.5 default), a string Seconds, items at or over the target, and the operand cap.
        var items = new object[]
        {
            Obj(("Key", "b"), ("Seconds", 30.0)), Obj(("Key", "a"), ("Seconds", 30.0)), Obj(("Key", "big"), ("Seconds", 600.0)), Obj(("Key", "edge"), ("Seconds", 120.0)),
            Obj(("Key", "none")), Obj(("Key", "text"), ("Seconds", "45.5")), Obj(("Key", "c"), ("Seconds", 0.1)), Obj(("Key", "d"), ("Seconds", 0.1)), Obj(("Key", "e"), ("Seconds", 89.9)),
        };
        var bins = new List<object?>();
        foreach (var cap in new[] { 2, 3, 0 })
        {
            foreach (var bin in NightlyPlannerParts.SplitBySeconds(items, 120, cap, 0.5))
            {
                var members = (ArrayList)PsValue.Prop(bin, "Items")!;
                bins.Add(Ordered(("cap", cap), ("keys", string.Join(",", members.Cast<object>().Select(m => PsValue.Str(PsValue.Prop(m, "Key"))))),
                    ("seconds", PsValue.Prop(bin, "Seconds")), ("closed", PsValue.Prop(bin, "Closed")), ("type", members.GetType().Name)));
            }
        }
        Golden("packing", PsJson.Pipeline(bins), "\"closed\":true");
    }

    // ---- The test project walk (Test-NCProjectWalkMatchesFallback). ----

    private const string TestProjectText = "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>";

    [Test]
    [ParallelLimiter<GitProcessSpawnLimit>]
    public void Project_walk_matches_the_golden()
    {
        var root = NewRoot();
        try
        {
            var tree = Path.Combine(root, "nc-projects");
            foreach (var relative in new[] { "a/One.csproj", "a/b/Two.csproj", "bin/Skip.csproj", "BIN/Skip.csproj", "obj/Skip.csproj", "x/obj/Skip.csproj",
                "bin-debug/Skip.csproj", "a/bin-named.csproj", "Bin-Upper/Kept.csproj", "objects/Kept.csproj", "binary/Kept.csproj",
                ".hidden/Skip.csproj", "c/NotTest.csproj", "c/Other.txt" })
            {
                var path = Path.Combine(tree, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, relative.StartsWith("c/", StringComparison.Ordinal) ? "<Project />" : TestProjectText);
            }
            // Hidden is the attribute on Windows and the leading dot on Linux, which ignores the attribute; .NET reports both.
            var hidden = new DirectoryInfo(Path.Combine(tree, ".hidden"));
            hidden.Attributes |= FileAttributes.Hidden;
            var text = PsJson.Pipeline(NightlyPolicy.TestProjectPaths(tree).Cast<object?>().ToList());
            Golden("projectWalk", text);
            text.ShouldContain("Kept");
            text.ShouldNotContain("Skip");
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    [ParallelLimiter<GitProcessSpawnLimit>]
    public void Git_ignored_project_walk_matches_the_golden()
    {
        var root = NewRoot();
        try
        {
            // In a git checkout the walk skips ignored directories (nested ones too); untracked projects are kept.
            var repo = Path.Combine(root, "nc-git-projects");
            foreach (var relative in new[] { "kept/Untracked.csproj", "deep/a/Kept.csproj", "out/Skip.csproj", "deep/cache/Skip.csproj", "deep/a/scratch/Skip.csproj" })
            {
                var path = Path.Combine(repo, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, TestProjectText);
            }
            File.WriteAllText(Path.Combine(repo, ".gitignore"), "out/\ncache/\n");
            File.WriteAllText(Path.Combine(repo, "deep/a/.gitignore"), "scratch/\n");
            using (var git = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git") { ArgumentList = { "-C", repo, "init", "-q" }, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!)
            {
                git.StandardOutput.ReadToEnd();
                git.StandardError.ReadToEnd();
                git.WaitForExit();
                git.ExitCode.ShouldBe(0, "git init");
            }
            var text = PsJson.Pipeline(NightlyPolicy.TestProjectPaths(repo).Cast<object?>().ToList());
            Golden("gitIgnoredWalk", text);
            text.ShouldBe("[\"deep/a/Kept.csproj\",\"kept/Untracked.csproj\"]");
        }
        finally { DeleteRoot(root); }
    }

    // ---- The parent process read (Test-NCParentProcessMatchesFallback). ----

    [Test]
    public void Parent_process_read_finds_a_live_parent_and_answers_none_for_a_missing_process()
    {
        // Get-NightlyParentProcess: the compiled parent id, its start time; a missing process answers Id 0, no start time.
        var parent = NightlyProcesses.ParentProcessId(Environment.ProcessId);
        parent.ShouldBeGreaterThan(0, "this process has a parent");
        using var live = System.Diagnostics.Process.GetProcessById(parent);
        live.StartTime.ToUniversalTime().ShouldBeLessThanOrEqualTo(DateTime.UtcNow, "the parent has a start time");
        NightlyProcesses.ParentProcessId(int.MaxValue).ShouldBeLessThan(0, "a missing process has no parent (Id 0, no start time)");
    }

    // ---- Evidence files: path identity, bounded read, queued writes (Test-NCEvidenceFilesMatchFallback). ----

    /// <summary>
    /// The Windows goldens hold &lt;root&gt;\\nc-files\\... paths. Off Windows the path separators are folded, and a file
    /// the host could not mark Hidden reports its real length (Get-Item without -Force read 0 for a hidden file).
    /// </summary>
    private static string WindowsPathText(string text, string root) =>
        OperatingSystem.IsWindows() ? text : Normalize(text, root).Replace("<root>/nc-files/nope/", "<root>\\\\nc-files\\\\nope\\\\", StringComparison.Ordinal)
            .Replace("<root>/nc-files/", "<root>\\\\nc-files\\\\", StringComparison.Ordinal);

    [Test]
    [NotInParallel]
    public void Evidence_files_match_the_goldens()
    {
        var root = NewRoot();
        try
        {
            var dir = Path.Combine(root, "nc-files");
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            File.WriteAllText(Path.Combine(dir, "small.json"), "{\"a\":1}");
            File.WriteAllText(Path.Combine(dir, "empty.json"), "");
            File.WriteAllBytes(Path.Combine(dir, "big.bin"), Enumerable.Range(1, 200).Select(b => (byte)b).ToArray());
            File.WriteAllBytes(Path.Combine(dir, "exact.bin"), Enumerable.Range(1, 64).Select(b => (byte)b).ToArray());
            File.WriteAllText(Path.Combine(dir, "hidden.json"), "hidden");
            File.SetAttributes(Path.Combine(dir, "hidden.json"), File.GetAttributes(Path.Combine(dir, "hidden.json")) | FileAttributes.Hidden);
            var hiddenKept = (File.GetAttributes(Path.Combine(dir, "hidden.json")) & FileAttributes.Hidden) != 0;
            File.WriteAllText(Path.Combine(dir, "readonly.json"), "ro");
            File.SetAttributes(Path.Combine(dir, "readonly.json"), FileAttributes.ReadOnly);
            var names = new[] { "", "missing.json", "sub", "small.json", "empty.json", "big.bin", "exact.bin", "hidden.json", "readonly.json", "nope/x.json" };
            var paths = names.Select(n => n.Length == 0 ? "" : Path.Combine(dir, n.Replace('/', Path.DirectorySeparatorChar))).ToArray();
            // Get-TestPhasePathIdentity against its PowerShell body (core-golden/pathIdentity.json).
            var identity = PsJson.Pipeline(paths.Select(p =>
            {
                var i = TestPhaseFiles.PathIdentity(p);
                return (object?)Ordered(("path", p), ("kind", PsValue.Prop(i, "Kind")), ("size", PsValue.Prop(i, "Size")));
            }).ToList());
            var expectedIdentity = Read("pathIdentity");
            if (!hiddenKept) expectedIdentity = expectedIdentity.Replace("hidden.json\",\"kind\":\"regular\",\"size\":0", "hidden.json\",\"kind\":\"regular\",\"size\":6", StringComparison.Ordinal);
            WindowsPathText(Normalize(identity, root), root).ShouldBe(expectedIdentity, "pathIdentity");
            // Read-TestPhaseBoundedBytes (compiled for a real stopwatch) against the golden its PowerShell body produced.
            var rows = new List<object?>();
            foreach (var p in paths)
            {
                foreach (var max in new[] { 64, 262144 })
                {
                    var b = TestPhaseFiles.BoundedRead(p, max, 5000, System.Diagnostics.Stopwatch.StartNew());
                    rows.Add(Ordered(("path", p), ("max", max), ("bytes", PsValue.Prop(b, "Bytes")), ("complete", PsValue.Prop(b, "Complete")),
                        ("refused", PsValue.Prop(b, "Refused")), ("kind", PsValue.Prop(b, "Kind"))));
                }
            }
            var spent = System.Diagnostics.Stopwatch.StartNew();
            Thread.Sleep(20);
            var budget = TestPhaseFiles.BoundedRead(Path.Combine(dir, "small.json"), 262144, 5, spent);
            rows.Add(Ordered(("budget", true), ("bytes", PsValue.Prop(budget, "Bytes")), ("complete", PsValue.Prop(budget, "Complete")),
                ("refused", PsValue.Prop(budget, "Refused")), ("kind", PsValue.Prop(budget, "Kind"))));
            var (ok, detail) = Compare("boundedRead", WindowsPathText(Normalize(PsJson.Pipeline(rows), root), root));
            ok.ShouldBeTrue(detail);
            File.SetAttributes(Path.Combine(dir, "readonly.json"), FileAttributes.Normal);

            // Queued evidence writes answer as the landed files will: identity, existence, text, bytes and hash.
            var queued = Path.Combine(dir, "queued");
            Directory.CreateDirectory(queued);
            string Observe(bool deferred) => PsJson.Compress(new[] { "a.json", "b.json", "lines.txt" }.Select(n =>
            {
                var p = Path.Combine(queued, n);
                var i = TestPhaseFiles.PathIdentity(p);
                return (object)Ordered(("name", n), ("kind", PsValue.Prop(i, "Kind")), ("size", PsValue.Prop(i, "Size")),
                    ("exists", deferred ? EvidenceFiles.PathExists(p) : File.Exists(p) || Directory.Exists(p)),
                    ("leaf", deferred ? EvidenceFiles.FileExists(p) : File.Exists(p)),
                    ("text", deferred ? EvidenceFiles.ReadAllText(p) : File.ReadAllText(p)),
                    ("bytes", deferred ? EvidenceFiles.ReadAllBytes(p) : File.ReadAllBytes(p)),
                    ("lines", deferred ? EvidenceFiles.ReadAllLines(p) : File.ReadAllLines(p)),
                    ("hash", deferred ? EvidenceFiles.Sha256(p) : NightlyClassNames.FileSha256(p)));
            }).ToArray(), 6);
            string before;
            EvidenceFiles.EnableWriteBehind();
            try
            {
                EvidenceFiles.WriteAtomic(Path.Combine(queued, "a.json"), "{\"queued\":\"caf" + (char)0xE9 + "\"}", true);
                EvidenceFiles.WriteAtomic(Path.Combine(queued, "b.json"), "first", true);
                EvidenceFiles.WriteAtomic(Path.Combine(queued, "b.json"), "second, longer", true);
                EvidenceFiles.WriteAllLines(Path.Combine(queued, "lines.txt"), ["one", "two"]);
                before = Observe(true);
            }
            finally { EvidenceFiles.DisableWriteBehind().ShouldBeEmpty("deferred write failures"); }
            var after = Observe(false);
            before.ShouldBe(after, "queued evidence answers as the landed files");
            after.ShouldContain("second, longer");
        }
        finally { DeleteRoot(root); }
    }

    // ---- Discovery documents and canonical JSON (Test-NCDocumentsMatchFallback). ----

    [Test]
    public void Discovery_documents_and_canonical_json_match_the_goldens()
    {
        var source = FixtureNodes(3000);
        var nodes = new List<object>();
        var i = 0;
        foreach (var n in source)
        {
            var row = Obj(("uid", n["Uid"]), ("assembly", "Antiphon.Tests"), ("namespace", n["Namespace"]), ("type", n["Type"]), ("method", n["Method"]),
                ("signature", "(" + i.ToString(CultureInfo.InvariantCulture) + ")"), ("state", ""), ("categories", n["Categories"]));
            if (i % 7 == 0) row.Add("excluded", true);
            else if (i % 11 == 0) row.Add("excluded", "false");
            if (i % 13 == 0) row["categories"] = new object[] { "OptIn" };
            if (i % 17 == 0) row["categories"] = "explicit";
            if (i % 19 == 0) row["namespace"] = "";
            if (i % 23 == 0) row["state"] = "Discovered";
            nodes.Add(row);
            i++;
        }
        nodes.Add(Ordered(("uid", "dict-node"), ("type", "T"), ("method", "M"), ("excluded", true), ("categories", new object[] { "A", "B" })));
        var doc = NightlyDiagnosticLog.NewDiscoveryDocument(nodes.ToArray(), "abc");
        var json = PsJson.Compress(doc, 12);
        // $json | ConvertFrom-Json: the core's strict reader gives what ConvertFrom-Json gives for this document.
        var parsed = NightlyJson.ParseText(json, 1024, true);
        parsed.ShouldNotBeNull();
        var back = NightlyDiagnosticLog.DiscoveryDocument(parsed, NightlyDiagnosticLog.DiscoveryFormat);
        var refusals = new object?[]
        {
            new object[] { Obj(("uid", "Same"), ("type", "T"), ("method", "M")), Obj(("uid", "same"), ("type", "T"), ("method", "M")) },
            new object[] { Obj(("uid", ""), ("type", "T"), ("method", "M")) },
            new object[] { Obj(("uid", "u"), ("type", "T"), ("method", " ")) },
            null,
        }.Select(bad => (object)Refusal(() => NightlyDiagnosticLog.DiscoveryDocument(Obj(("format", NightlyDiagnosticLog.DiscoveryFormat),
            ("tunitVersion", NightlyDiagnosticLog.SupportedTunit), ("mtpVersion", NightlyDiagnosticLog.SupportedMtp), ("assemblyHash", "h"), ("nodes", bad)),
            NightlyDiagnosticLog.DiscoveryFormat))).ToArray();
        Golden("discoveryDocuments", PsJson.Pipeline([Ordered(("json", json), ("back", back), ("canonical", NightlyDocuments.CanonicalJson(doc)), ("refusals", refusals))]));
        var mixed = Ordered(("zeta", 1), ("Alpha", 2L), ("beta", 1.5), ("Gamma", 2.25m), ("", "dropped"), ("flag", false), ("none", null),
            ("text", "q\"uote\\back\r\n\t" + (char)0xE9), ("list", new object?[] { 1, "two", null, new object[] { 3 } }), ("single", new object[] { "one" }),
            ("obj", Obj(("b", 2), ("a", 1))), ("table", Table(("Y", "y"), ("x", "x"))), ("ordered", Ordered(("m", Table()), ("l", Array.Empty<object>()))),
            ("small", (byte)7), ("float", 0.5f));
        // ConvertTo-NightlyCanonicalJson has no PowerShell copy left: the compiled text against the golden the copy produced.
        Golden("canonicalJson", PsJson.Pipeline([NightlyDocuments.CanonicalJson(mixed), NightlyDocuments.CanonicalJson(new object[] { 1, 2 }),
            NightlyDocuments.CanonicalJson("plain"), NightlyDocuments.CanonicalJson(null)]), "Alpha", "plain");
    }

    // ---- Expected-skip source gaps and the source and assembly hash caches (Test-NCSourceGapsMatchFallback). ----

    [Test]
    public void Source_gaps_and_read_caches_match_the_golden()
    {
        var root = NewRoot();
        try
        {
            var fixture = Path.Combine(root, "source-gaps");
            var tests = Path.Combine(fixture, "tests");
            foreach (var d in new[] { "A", "A/bin", "A/obj", "B/bin-x", "B/Deep", "C" }) Directory.CreateDirectory(Path.Combine(tests, d));
            var files = new (string Path, string Text)[]
            {
                ("A/One.cs", "namespace N { public sealed class Alpha { public void Run() { } void Helper () { } } }"),
                ("A/bin/Hidden.cs", "class Hidden { void Run() { } }"),
                ("A/obj/Gen.cs", "class Generated { void Run() { } }"),
                ("B/bin-x/Skip.cs", "class Skipped { void Run() { } }"),
                ("B/Deep/Two.cs", "partial class Beta { async Task Go(){} } class AlphaBeta { void Other() { } }"),
                ("C/Three.cs", "// class Gamma mentioned only\nclass gamma { void Run() { } }"),
            };
            foreach (var (relative, content) in files) File.WriteAllText(Path.Combine(tests, relative), content);
            var entries = new[] { "N.Alpha.Run", "N.Alpha.Helper", "N.Alpha.Missing", "N.ALPHA.run", "N.Hidden.Run", "N.Generated.Run", "N.Skipped.Run", "X.Beta.Go",
                "Beta.Go", "Beta", "", ".Go", "N.AlphaBeta.Other", "N.Gamma.Run", "N.gamma.Run", "N.Alpha.Run" }
                .Select(t => (object?)Obj(("platform", "windows"), ("test", t)))
                .Concat(new object?[] { null, Obj(("platform", "linux"), ("test", "N.Nope.Run")) }).ToArray();
            var text = PsJson.Pipeline(NightlyExpectedSkips.SourceGaps(fixture, "windows", entries, null).Cast<object?>().ToList());
            Golden("sourceGaps", text);
            text.ShouldContain("N.Alpha.Missing");
            text.ShouldNotContain("\"N.Alpha.Run\"");
            // A same-length rewrite of a settled (cached) file must be seen by the next scan.
            var cached = Path.Combine(tests, "C/Cache.cs");
            File.WriteAllText(cached, "class Cached { void Alpha() { } }");
            File.SetLastWriteTimeUtc(cached, DateTime.UtcNow.AddHours(-1));
            NightlyTestSources.ExpectedSkipSourceGaps(tests, ["N.Cached.Alpha"]).Length.ShouldBe(0, "settled");
            File.WriteAllText(cached, "class Cached { void Omega() { } }");
            NightlyTestSources.ExpectedSkipSourceGaps(tests, ["N.Cached.Alpha"]).Length.ShouldBe(1, "rewritten");
            NightlyTestSources.ReadText(cached).ShouldContain("Omega");
            // Assembly digest cache: a same-length rewrite of a settled (cached) file is rehashed, and a file still inside
            // the racy window is never served from the cache.
            var dll = Path.Combine(fixture, "Fixture.dll");
            File.WriteAllText(dll, "assembly-one");
            File.SetLastWriteTimeUtc(dll, DateTime.UtcNow.AddHours(-1));
            var h1 = NightlyClassNames.CachedFileSha256(dll);
            var h1Again = NightlyClassNames.CachedFileSha256(dll);
            File.WriteAllText(dll, "assembly-two");
            var h2 = NightlyClassNames.CachedFileSha256(dll);
            File.WriteAllText(dll, "assembly-xyz");
            var h3 = NightlyClassNames.CachedFileSha256(dll);
            h1Again.ShouldBe(h1);
            h1.ShouldBe(Sha256Text("assembly-one"));
            h2.ShouldBe(Sha256Text("assembly-two"));
            h3.ShouldBe(Sha256Text("assembly-xyz"));
            h2.ShouldNotBe(h1);
        }
        finally { DeleteRoot(root); }
    }

    // ---- TRX identities (Test-NCTrxMatchesFallback). ----

    /// <summary>Write-C487Trx: a minimal TRX of the rows (an empty outcome is Passed).</summary>
    private static void WriteTrx(string path, IEnumerable<(string Id, string ClassName, string MethodName, string Outcome)> rows)
    {
        var defs = new StringBuilder("<TestDefinitions>");
        var results = new StringBuilder("<Results>");
        foreach (var (id, cls, method, outcome) in rows)
        {
            defs.Append(CultureInfo.CurrentCulture, $"<UnitTest id=\"{id}\" name=\"{cls}.{method}\"><TestMethod className=\"{cls}\" name=\"{method}\" /></UnitTest>");
            results.Append(CultureInfo.CurrentCulture, $"<UnitTestResult testId=\"{id}\" testName=\"{cls}.{method}\" outcome=\"{(string.IsNullOrWhiteSpace(outcome) ? "Passed" : outcome)}\" />");
        }
        File.WriteAllText(path, "<?xml version=\"1.0\" encoding=\"utf-8\"?><TestRun>" + defs + "</TestDefinitions>" + results + "</Results></TestRun>");
    }

    [Test]
    public void Trx_identities_match_the_golden()
    {
        var root = NewRoot();
        try
        {
            var dir = Path.Combine(root, "trx");
            Directory.CreateDirectory(dir);
            const string vs = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
            static string Def(string id, string cls, string method) => $"<UnitTest id=\"{id}\" name=\"{cls}.{method}\"><TestMethod className=\"{cls}\" name=\"{method}\" /></UnitTest>";
            static string Res(string id, string name, string outcome) => $"<UnitTestResult testId=\"{id}\" testName=\"{name}\" outcome=\"{outcome}\" />";
            var cases = new (string Name, string Text)[]
            {
                ("ns-plain", $"<TestRun xmlns=\"{vs}\"><TestDefinitions>" + Def("a", "N.C", "M1") + Def("b", "N.C", "M2") + "</TestDefinitions><Results>" + Res("a", "M1", "Passed") + Res("b", "M2", "Failed") + "</Results></TestRun>"),
                ("bare", "<TestRun><TestDefinitions>" + Def("a", "N.C", "M1") + "</TestDefinitions><Results>" + Res("a", "M1", "Skipped") + "</Results></TestRun>"),
                ("no-results-wrapper", "<TestRun><TestDefinitions>" + Def("a", "N.C", "M1") + "</TestDefinitions><Other>" + Res("a", "M1", "Passed") + "</Other></TestRun>"),
                ("debug-trace", $"<TestRun xmlns=\"{vs}\"><TestDefinitions>" + Def("a", "N.C", "M1") + "</TestDefinitions><Results><UnitTestResult testId=\"a\" testName=\"M1\" outcome=\"Failed\"><Output><StdOut>x</StdOut><DebugTrace>first &amp; <b>bold</b></DebugTrace><DebugTrace>second</DebugTrace></Output><output><DebugTrace>lower</DebugTrace></output><Output><debugtrace>no</debugtrace><DebugTrace>last</DebugTrace></Output></UnitTestResult></Results></TestRun>"),
                ("method-case", "<TestRun><TestDefinitions><UnitTest id=\"a\" name=\"x\"> <testmethod className=\"N.C\" name=\"M1\" /><TestMethod className=\"N.D\" name=\"M2\" /></UnitTest></TestDefinitions><Results>" + Res("a", "M1", "Passed") + "</Results></TestRun>"),
                ("id-case", "<TestRun><TestDefinitions>" + Def("A", "N.C", "Up") + Def("a", "N.C", "Low") + "</TestDefinitions><Results>" + Res("A", "x", "Passed") + "</Results></TestRun>"),
                ("duplicate", "<TestRun><TestDefinitions>" + Def("a", "N.C", "M1") + "</TestDefinitions><Results>" + Res("a", "M1", "Passed") + Res("A", "M1", "Passed") + "</Results></TestRun>"),
                ("missing-def", "<TestRun><TestDefinitions>" + Def("a", "N.C", "M1") + "</TestDefinitions><Results>" + Res("z", "Zed", "Passed") + "</Results></TestRun>"),
                ("missing-id", "<TestRun><TestDefinitions><UnitTest name=\"n\"><TestMethod className=\"N.C\" name=\"M\" /></UnitTest></TestDefinitions><Results><UnitTestResult testName=\"t\" outcome=\"Passed\" /></Results></TestRun>"),
                ("no-method", "<TestRun><TestDefinitions><UnitTest id=\"a\" name=\"n\" /></TestDefinitions><Results>" + Res("a", "n", "Passed") + "</Results></TestRun>"),
                ("blank-class", "<TestRun><TestDefinitions>" + Def("a", " ", "M1") + "</TestDefinitions><Results>" + Res("a", "M1", "Passed") + "</Results></TestRun>"),
                ("empty", $"<TestRun xmlns=\"{vs}\"><TestDefinitions /><Results /></TestRun>"),
                ("one-row", "<TestRun><TestDefinitions>" + Def("a", "N.C(Arg)", "M1") + "</TestDefinitions><Results>" + Res("a", "M1", "") + "</Results></TestRun>"),
            };
            var paths = new List<string>();
            foreach (var (name, text) in cases)
            {
                var path = Path.Combine(dir, name + ".trx");
                File.WriteAllText(path, text);
                paths.Add(path);
            }
            var big = Path.Combine(dir, "big.trx");
            WriteTrx(big, FixtureNodes(2500).Select(n => ((string)n["Uid"]!, (string)n["ClassName"]!, (string)n["Method"]!, "Passed")));
            paths.Add(big);
            var outcomes = new List<object?>();
            foreach (var path in paths)
            {
                var name = Path.GetFileName(path);
                try
                {
                    var rows = NightlyTrx.ReadIdentities(path);
                    // The 2500-row file as a count and digest; every other file's rows as one element.
                    outcomes.Add(name == "big.trx"
                        ? Ordered(("file", name), ("count", rows.Length), ("sha256", Sha256Text(PsJson.Pipeline(rows.Cast<object?>().ToList(), 4))))
                        : rows);
                }
                catch (Exception ex) { outcomes.Add("refused: " + ex.Message.Replace(path, name, StringComparison.Ordinal)); }
            }
            var actual = PsJson.Pipeline(outcomes);
            Golden("trxIdentities", actual, "refused: ");
            CountOf(actual, "refused: ").ShouldBe(5);
        }
        finally { DeleteRoot(root); }
    }

    private static int CountOf(string text, string part)
    {
        var count = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    // ---- JSON files: the strict reader, omitted paths, the writer (Test-NCJsonFilesMatchFallback). ----

    /// <summary>The golden's text with this machine's local rendering of the +02:00 date put back (it was written as local time).</summary>
    private static string LocalDates(JsonElement golden, string field)
    {
        var generated = golden.GetProperty("offsetLocal").GetString()!;
        var here = PsJson.Compress(new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.FromHours(2)).LocalDateTime);
        return golden.GetProperty(field).GetString()!.Replace(generated[1..^1], here[1..^1], StringComparison.Ordinal);
    }

    [Test]
    public void Json_files_match_the_goldens()
    {
        var root = NewRoot();
        try
        {
            var dir = Path.Combine(root, "json-files");
            Directory.CreateDirectory(dir);
            var e = ((char)0xE9).ToString();
            var docs = new (string Name, string Text)[]
            {
                ("plain", "{\"s\":\"a\\\"b\\\\c" + "\\" + "u00e9" + e + "\",\"i\":1,\"big\":123456789012345678901234567890,\"neg\":-5,\"d\":1.0,\"x\":1e3,\"t\":true,\"f\":false,\"n\":null,\"o\":{\"a\":[],\"b\":[1,[2,{}]],\"c\":{}},\"one\":[\"only\"]}"),
                ("dates", "{\"z\":\"2026-11-17T10:00:00Z\",\"frac\":\"2026-11-17T10:00:00.1234567Z\",\"off\":\"2026-11-17T10:00:00+02:00\",\"none\":\"2026-11-17T10:00:00\",\"day\":\"2026-11-17\",\"ms\":\"\\/Date(1700000000000)\\/\",\"word\":\"2026-11-17Txx:00:00\"}"),
                ("comment", "{\"a\":1 /* c */}"), ("rootArray", "[{\"a\":1}]"), ("repeated", "{\"a\":1,\"A\":2}"), ("trailing", "{\"a\":1} x"),
            };
            var paths = new List<string>();
            foreach (var (name, text) in docs) { var p = Path.Combine(dir, name + ".json"); File.WriteAllText(p, text); paths.Add(p); }
            var bom = Path.Combine(dir, "bom.json"); File.WriteAllText(bom, (char)0xFEFF + "{\"a\":1}", new UTF8Encoding(true)); paths.Add(bom);
            var utf16 = Path.Combine(dir, "utf16.json"); File.WriteAllText(utf16, "{\"a\":\"w\"}", Encoding.Unicode); paths.Add(utf16);
            // Read-NightlyJsonFile: what the core reads is what ConvertFrom-Json read (core-golden/jsonRead.json); every
            // document it does not read strictly (a comment, a root array, a repeated name, trailing content, a second
            // byte-order mark, a date the strict reader leaves to ConvertFrom-Json, UTF-16) it hands to ConvertFrom-Json.
            using var readGolden = JsonDocument.Parse(Read("jsonRead"));
            using var expected = JsonDocument.Parse(LocalDates(readGolden.RootElement, "text"));
            var expectedItems = expected.RootElement.EnumerateArray().Select(x => x.GetRawText()).ToArray();
            var index = 0;
            var declined = new List<string>();
            foreach (var path in paths)
            {
                foreach (var strip in new[] { false, true })
                {
                    var read = NightlyJson.ReadFile(path, strip);
                    if (read == null) declined.Add(Path.GetFileName(path) + (strip ? "+strip" : ""));
                    else PsJson.Compress(read).ShouldBe(expectedItems[index], Path.GetFileName(path) + " strip=" + strip);
                    index++;
                }
            }
            index.ShouldBe(expectedItems.Length);
            string.Join(",", declined).ShouldBe("dates.json,dates.json+strip,comment.json,comment.json+strip,rootArray.json,rootArray.json+strip,repeated.json,repeated.json+strip,trailing.json,trailing.json+strip,bom.json,utf16.json,utf16.json+strip");
            expected.RootElement.GetRawText().ShouldContain("refused: ");

            var summary = Path.Combine(dir, "summary.json");
            File.WriteAllText(summary, "{\"outcome\":\"PASS\",\"suites\":[{\"id\":\"a\",\"exclusions\":[{\"uid\":\"u\"}],\"trx\":\"a.trx\"}],\"dispatchProgress\":{\"liveness\":\"closed\",\"suites\":[{\"id\":\"a\",\"exclusions\":[]}]}}");
            PsJson.Compress(NightlyJson.ReadFile(summary, false), 8).ShouldContain("\"uid\":\"u\"");
            PsJson.Compress(NightlyJson.ReadFile(summary, false, ["suites[].exclusions", "dispatchProgress.suites[].exclusions"]), 8)
                .ShouldBe("{\"outcome\":\"PASS\",\"suites\":[{\"id\":\"a\",\"trx\":\"a.trx\"}],\"dispatchProgress\":{\"liveness\":\"closed\",\"suites\":[{\"id\":\"a\"}]}}");
            PsJson.Compress(NightlyJson.ReadFile(summary, false, ["suites", "dispatchProgress"]), 8).ShouldBe("{\"outcome\":\"PASS\"}");

            var value = Ordered(
                ("s", "q \" \\ / \t " + e + " " + (char)0x2028 + " <&>"), ("i", 1), ("l", 5000000000L), ("d", 1.0), ("d2", 0.1), ("m", 1.1m), ("nan", double.NaN),
                ("big", System.Numerics.BigInteger.Parse("123456789012345678901234567890", CultureInfo.InvariantCulture)), ("t", true), ("n", null), ("dbn", DBNull.Value), ("ch", 'x'),
                ("utc", new DateTime(2026, 11, 17, 10, 0, 0, 123, DateTimeKind.Utc)), ("none", new DateTime(2026, 11, 17, 10, 0, 0)),
                ("off", new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.FromHours(2))), ("enum", FileAttributes.Hidden),
                ("h", Table(("a", 1))), ("e", Array.Empty<object>()), ("one", new object[] { 1 }), ("nested", new object[] { new object[] { 1, 2 }, Array.Empty<object>() }),
                ("list", new List<object> { 1, "two" }), ("pso", Obj(("z", 1), ("y", Ordered(("x", Array.Empty<object>()))))),
                ("rows", Enumerable.Range(1, 3).Select(r => (object)Ordered(("id", "r" + r), ("exclusions", new object[] { Ordered(("uid", "u" + r)) }))).ToArray()));
            // The core writes it; read back as ConvertFrom-Json reads it, it is what ConvertTo-Json -Depth 8 wrote and read back.
            var written = Path.Combine(dir, "written.json");
            NightlyJson.WriteFile(written, value, 8).ShouldBeTrue();
            var wrote = PsJson.Compress(NightlyJson.ParseText(File.ReadAllText(written), 1024, true));
            using var writeGolden = JsonDocument.Parse(Read("jsonWrite"));
            wrote.ShouldBe(LocalDates(writeGolden.RootElement, "reference"), "jsonWrite");
            wrote.ShouldContain("\"id\":\"r3\"");
            // An arbitrary object and nesting past the depth are refused (there is no ConvertTo-Json fallback).
            NightlyJson.WriteFile(Path.Combine(dir, "refused-0.json"), Ordered(("a", new DirectoryInfo(dir))), 1).ShouldBeFalse();
            NightlyJson.WriteFile(Path.Combine(dir, "refused-1.json"), Table(("deep", Table(("a", Table(("b", Table(("c", 1)))))))), 2).ShouldBeFalse();
        }
        finally { DeleteRoot(root); }
    }

    // ---- Planner parts and profile launch groups (Test-NCPlannerPartsGolden, Test-NCProfileLaunchGroupsGolden). ----

    /// <summary>Runs <paramref name="body"/> under en-US: Sort-Object and Group-Object order by the current culture.</summary>
    private static T InUsCulture<T>(Func<T> body)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            return body();
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Test]
    public void Planner_parts_match_the_golden()
    {
        var output = InUsCulture(() =>
        {
            // The Split-NightlyItemsBySeconds order (unstable for ties, current culture case-insensitive keys) and
            // Get-NightlyPrefixUnsafeNames.
            var keys = new[] { "b", "B", "a", "A", "ab", "Ab", "", ((char)0xE9).ToString(), "e" + (char)0x301, "E", ((char)0xDF).ToString(), "ss", "SS", ((char)0x130).ToString(),
                "i", "I", "z10", "z2", "Alpha", "alpha" };
            var seconds = new[] { 30.0, 30.0, 0.5, 45.5, 120.0, 600.0, 30.0 };
            var items = new object[60];
            for (var i = 0; i < 60; i++)
            {
                var o = new PlainObject(("Key", keys[i * 7 % keys.Length]));
                if (i % 11 != 3) o.Add("Seconds", seconds[i * 5 % seconds.Length]);
                o.Add("Idx", i);
                items[i] = o;
            }
            var result = new List<object?>();
            foreach (var cap in new[] { 1000, 3 })
            {
                foreach (var bin in NightlyPlannerParts.SplitBySeconds(items, cap == 3 ? 120 : 1e9, cap, 0.5))
                    result.Add(Ordered(("cap", cap), ("idx", string.Join(",", ((ArrayList)PsValue.Prop(bin, "Items")!).Cast<object>().Select(m => PsValue.Str(PsValue.Prop(m, "Idx"))))),
                        ("seconds", PsValue.Prop(bin, "Seconds")), ("closed", PsValue.Prop(bin, "Closed"))));
            }
            var e = ((char)0xE9).ToString();
            foreach (var names in new[] { new[] { "Alpha", "AlphaBeta", "alpha", "Beta", "Beta", "" }, new[] { "Run", "Run2", "Runner", "Ru" }, new[] { e, e + "x", "e" }, Array.Empty<string>() })
            {
                var unsafeNames = NightlyPlannerParts.PrefixUnsafeNames(names);
                var sorted = unsafeNames.ToArray();
                Array.Sort(sorted, StringComparer.Ordinal);
                result.Add(Ordered(("type", unsafeNames.GetType().Name), ("unsafe", sorted)));
            }
            return result;
        });
        Golden("planner-parts", PsJson.Pipeline(output), "\"cap\":3", "\"unsafe\":[\"\",\"Alpha\"]", "\"unsafe\":[\"Ru\",\"Run\"]");
    }

    /// <summary>ConvertTo-NightlyProfileLaunchGroups (no -BoundaryAware): the compiled planner, its error thrown.</summary>
    private static object[] ProfileLaunchGroups(object[] leaves, object[] nodes, Hashtable costTable, double target, double budget, int operands)
    {
        var plan = LaunchPlanner.ProfileLaunchGroups(leaves, nodes, null, LaunchPlanner.NeedsCostTable(leaves) ? costTable : null,
            0.5, 1.5, 4, 2, target, budget, operands);
        var error = PsValue.Prop(plan, "Error");
        if (error != null) throw new InvalidOperationException((string)error);
        return PsValue.AsArray(PsValue.Prop(plan, "Groups"));
    }

    [Test]
    public void Profile_launch_groups_match_the_golden()
    {
        // LaunchPlanner.cs: ids, class order, filters, estimates and value types against what the PowerShell body returned
        // when it was ported. Culture ties, case-only and non-ASCII class and method names, a case-only namespace,
        // prefix-unsafe names, a class split by method, the E2E budget, a measured class, duplicate leaves, exact leaves
        // (Add-Member -Force moves seconds last) and the namespace error.
        var sh = ((char)0xAD).ToString();
        var ee = ((char)0xE9).ToString();
        var acute = ((char)0x301).ToString();
        var ss = ((char)0xDF).ToString();
        static PlainObject Node(string cls, string method, string uid, bool slow) =>
            new(("ClassName", cls), ("Method", method), ("Uid", uid), ("Categories", slow ? new object[] { "Slow" } : Array.Empty<object>()));
        static IEnumerable<object> Many(string cls, string[] methods, int count, bool slow)
        {
            foreach (var m in methods)
                for (var k = 0; k < count; k++) yield return Node(cls, m, cls + "/" + m + "/" + k.ToString(CultureInfo.InvariantCulture), slow);
        }
        static PlainObject Leaf(string id, string cls, string exact, IEnumerable<object> nodes) =>
            new(("id", id), ("classes", new object[] { cls }), ("exactFilter", exact), ("nodes", nodes.ToArray()));
        static IEnumerable<object> Leaves(IEnumerable<object> nodes, string prefix)
        {
            var seen = new List<string>();
            foreach (var n in nodes)
            {
                var b = NightlyCensus.BareClassIdentity((string)PsValue.Prop(n, "ClassName")!);
                if (!seen.Contains(b)) seen.Add(b);
            }
            var i = 0;
            foreach (var b in seen) yield return Leaf(prefix + "-profile-" + (++i).ToString(CultureInfo.InvariantCulture), b, "", []);
        }
        static object Project(string name, object[] plan) => Ordered(("scenario", name), ("groups", plan.Select(g =>
        {
            var uids = PsValue.AsArray(PsValue.Prop(g, "nodes")).Where(n => n != null).Select(n => PsValue.Str(PsValue.Prop(n, "Uid"))).ToArray();
            var classesValue = PsValue.Prop(g, "classes");
            return (object)Ordered(("props", string.Join(",", PsValue.PropertyNames(g))), ("id", PsValue.Prop(g, "id")),
                ("classes", PsValue.AsArray(classesValue).Select(c => (object)PsValue.Str(c)).ToArray()), ("exactFilter", PsValue.Prop(g, "exactFilter")),
                ("groupFilter", PsValue.Prop(g, "groupFilter")), ("methodFilter", PsValue.Prop(g, "methodFilter")),
                ("nodes", uids.Length.ToString(CultureInfo.InvariantCulture) + ":" + Sha256Text(string.Join("|", uids))),
                ("nodeCount", PsValue.Prop(g, "nodeCount")), ("seconds", PsValue.Prop(g, "seconds")),
                ("types", string.Join(",", new[] { PsValue.Prop(g, "nodeCount"), PsValue.Prop(g, "seconds"), classesValue }.Select(v => v == null ? "null" : v.GetType().Name))));
        }).ToArray()));
        var output = InUsCulture(() =>
        {
            var table = ClassCostTable();
            var result = new List<object?>();
            // Namespaces that tie under the current culture would follow the per-process hash order of the @{} keys, so none here do.
            var names = new[] { "Alpha", "alpha", "Beta", "Gamma", "Run", "Runner", "RunnerTests", "Caf" + ee, "Cafe" + acute, "Stra" + ss + "e", "Strasse", "A" + sh + "B", "AB",
                "Zeta", "Eta", "Theta", "Iota", "Kappa", "Lambda" };
            var nodes = names.SelectMany(t => Many("N.S." + t, ["M"], 2, false)).Concat(Many("n.s.Delta", ["M"], 3, false)).Concat(Many("M.S.Omega", ["M"], 1, true))
                .Concat(Many("N.S.Gamma(ctor)", ["M"], 1, false)).ToArray();
            result.Add(Project("ties", ProfileLaunchGroups(Leaves(nodes, "one").Concat(Leaves(nodes, "two")).ToArray(), nodes, table, 900, 2400, 30)));
            table["N.Big.Measured"] = 2.5;
            nodes = Many("N.Big.LandHeavyTests", ["Run", "run", "RunAll", "Ru", "Go", "Go2", "A" + sh + "B", "AB", "Caf" + ee, "Cafe" + acute], 3, true)
                .Concat(Many("N.Big.LandHeavyTests", ["Zeta", "Eta", "Theta", "Iota"], 2, false)).Concat(Many("N.Big.Measured", ["A", "B"], 2, false))
                .Concat(Many("N.Big.Small", ["A"], 2, false)).ToArray();
            result.Add(Project("split", ProfileLaunchGroups(Leaves(nodes, "big").ToArray(), nodes, table, 4, 8, 3)));
            nodes = Many("Antiphon.E2E.Fixture.LandE2ETests", ["A", "B", "C", "D"], 50, true).Concat(Many("Antiphon.E2Ex.Fixture.LandE2ETests", ["A", "B", "C", "D"], 50, true)).ToArray();
            result.Add(Project("e2e", ProfileLaunchGroups(Leaves(nodes, "e2e").ToArray(), nodes, table, 900, 1000000, 30)));
            nodes = Many("N.X.Matrix", ["Run", "Other"], 2, false).Concat(Many("N.X.Plain", ["A"], 2, false)).ToArray();
            var moved = new PlainObject(("id", "x-profile-2"), ("seconds", "old"), ("classes", new object[] { "N.X.Matrix" }), ("exactFilter", "/*/N.X/Matrix/Other"),
                ("nodes", nodes.Where(n => (string)PsValue.Prop(n, "Method")! == "Other").ToArray()));
            var exactLeaves = new object[]
            {
                Leaf("x-profile-1", "N.X.Matrix", "/*/N.X/Matrix/Run", nodes.Where(n => (string)PsValue.Prop(n, "Method")! == "Run")),
                Leaf("x-profile-3", "N.X.Plain", "", []), moved, Leaf("x-profile-4", "NoNamespace", "/*/x", []),
            };
            result.Add(Project("exact", ProfileLaunchGroups(exactLeaves, nodes, table, 900, 2400, 30)));
            try
            {
                ProfileLaunchGroups([Leaf("y-profile-1", "N.X.Plain", "", []), Leaf("y-profile-2", "NoNamespace", "", [])], nodes, table, 900, 2400, 30);
                result.Add(Ordered(("scenario", "error"), ("error", "")));
            }
            // A PowerShell throw of the text: its FullyQualifiedErrorId is the text too.
            catch (InvalidOperationException ex) { result.Add(Ordered(("scenario", "error"), ("error", ex.Message), ("id", ex.Message))); }
            return result;
        });
        Golden("profile-launch-groups", PsJson.Pipeline(output), "\"scenario\":\"split\"", "\"methodFilter\":\"/*/N.Big/LandHeavyTests/(", "-split-",
            "\"error\":\"profile-discovery-namespace-required\"", "props\":\"id,classes,exactFilter,nodes,seconds\"");
    }

    // ---- The discovery writer against ConvertTo-Json (Test-NCDiscoveryWriterMatchesConvertToJson). ----

    [Test]
    public void Discovery_writer_writes_what_convert_to_json_wrote()
    {
        // DiscoveryWriter.cs (Write-NightlyDiscoveryDocument): the written bytes against the UTF-8 (no BOM) of
        // ConvertTo-Json -Depth 12 PowerShell wrote for the same document (core-golden/discoveryWriter.json, line ends
        // as LF): every string escape class, surrogate pairs and lone surrogates, categories, excluded nodes, fixture
        // nodes, an empty document, both the create and the replace branch. A value the compiled writer does not claim
        // is refused (no ConvertTo-Json fallback): no file is created, an existing file keeps its bytes, and no
        // temporary file is left.
        var root = NewRoot();
        try
        {
            static string C(int n) => ((char)n).ToString();
            var weird = new[] { "\"q\"", "back\\slash", "t" + C(9) + "n" + C(10) + "r" + C(13), "b" + C(8) + "f" + C(12), "nul" + C(0) + C(1) + C(31),
                "del" + C(127) + C(0x80), "nel" + C(0x85) + "ls" + C(0x2028) + "ps" + C(0x2029), "caf" + C(0xE9), "pair" + C(0xD83D) + C(0xDE00),
                "hi" + C(0xD800), "lo" + C(0xDC00), "/<>&'", "", "Slow" };
            var nodes = new object[weird.Length];
            for (var i = 0; i < weird.Length; i++)
            {
                var w = weird[i];
                nodes[i] = Obj(("Uid", "u" + i.ToString(CultureInfo.InvariantCulture) + w), ("Assembly", w), ("Namespace", "N." + w), ("Type", "T" + w), ("Method", "M" + w),
                    ("Signature", w), ("State", i % 3 == 0 ? "" : w), ("Excluded", i % 2 == 0), ("Categories", weird.Skip(i).Take(1 + i % 3).ToArray()));
            }
            var docs = new (string Name, System.Collections.Specialized.OrderedDictionary Doc)[]
            {
                ("weird", NightlyDiagnosticLog.NewDiscoveryDocument(nodes, "h" + C(9) + "\"")),
                ("fixture", NightlyDiagnosticLog.NewDiscoveryDocument(FixtureNodes(300).Cast<object>().ToArray(), "fixture-hash")),
                ("empty", NightlyDiagnosticLog.NewDiscoveryDocument([], "h")),
            };
            using var golden = JsonDocument.Parse(Read("discoveryWriter"));
            foreach (var (name, doc) in docs)
            {
                var path = Path.Combine(root, "discovery-writer-" + name + ".json");
                foreach (var round in new[] { "create", "replace" })
                {
                    NightlyDiagnosticLog.WriteDiscoveryDocument(path, doc);
                    var text = Encoding.UTF8.GetString(File.ReadAllBytes(path));
                    // ConvertTo-Json ends lines with the platform's line end; the golden holds them as LF.
                    var lf = text.Replace(Environment.NewLine, "\n", StringComparison.Ordinal);
                    lf.ShouldNotContain("\r", customMessage: name + " " + round + " line ends");
                    var bytes = File.ReadAllBytes(path);
                    var lfBytes = Environment.NewLine == "\n" ? bytes : ReplaceCrLf(bytes);
                    ("sha256:" + TestPhaseRecords.Sha256Hex(lfBytes) + " bytes:" + lfBytes.Length).ShouldBe(golden.RootElement.GetProperty(name).GetString(), name + " " + round);
                }
                DiscoveryWriter.Prepare(doc).ShouldNotBeNull(name + " claimed");
            }
            var unclaimed = Ordered(("format", "f"), ("count", 5), ("nodes", Array.Empty<object>()));
            DiscoveryWriter.Prepare(unclaimed).ShouldBeNull("unclaimed");
            var refusedPath = Path.Combine(root, "discovery-writer-unclaimed.json");
            foreach (var round in new[] { "create", "replace" })
            {
                if (round == "replace") File.WriteAllText(refusedPath, "prior");
                Refusal(() => NightlyDiagnosticLog.WriteDiscoveryDocument(refusedPath, unclaimed)).ShouldStartWith("nightly-discovery-write-failed unsupported-value ");
                if (round == "create") File.Exists(refusedPath).ShouldBeFalse("a refused create leaves no file");
                else File.ReadAllText(refusedPath).ShouldBe("prior", "a refused replace keeps the file");
            }
            new DirectoryInfo(root).GetFiles("discovery-writer-*").Where(f => !f.Name.EndsWith(".json", StringComparison.Ordinal)).ShouldBeEmpty("temporary files");
        }
        finally { DeleteRoot(root); }
    }

    private static byte[] ReplaceCrLf(byte[] bytes)
    {
        var result = new List<byte>(bytes.Length);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == 13 && i + 1 < bytes.Length && bytes[i + 1] == 10) continue;
            result.Add(bytes[i]);
        }
        return result.ToArray();
    }

    // ---- History links: identities, link refusals, the parallel walk (Test-NCHistoryLinks). ----

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string fileName, string existingFileName, IntPtr securityAttributes);

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int PosixLink(string existing, string link);

    private static void HardLink(string link, string target)
    {
        var ok = OperatingSystem.IsWindows() ? CreateHardLinkW(link, target, IntPtr.Zero) : PosixLink(target, link) == 0;
        ok.ShouldBeTrue("hard link " + link);
    }

    private static void Junction(string link, string target)
    {
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", Path.GetFullPath(link), Path.GetFullPath(target) }) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, "mklink /J: " + error);
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void History_links_identify_refuse_and_walk_as_checked_in_turn()
    {
        // HistoryLinks.cs: one file read twice is one identity, two files differ, a hard link's two names share one, the
        // file index fills its low 32 bits. Refusals: a hard link (both names), file and directory symbolic links, a
        // junction on Windows, a cached directory replaced by another. The walk, whose reads for a directory above the
        // parallel threshold run in parallel, gives what checking each entry in turn gives: the same entries in the same
        // order and the same first refusal.
        var root = NewRoot();
        try
        {
            var tree = Path.Combine(root, "history-links");
            foreach (var d in new[] { "many", "sub/deeper", "empty" }) Directory.CreateDirectory(Path.Combine(tree, d));
            File.WriteAllText(Path.Combine(tree, "a.txt"), "a");
            File.WriteAllText(Path.Combine(tree, "sub/b.txt"), "b");
            File.WriteAllText(Path.Combine(tree, "sub/deeper/c.txt"), "c");
            for (var i = 0; i < 200; i++) File.WriteAllText(Path.Combine(tree, "many/f" + i.ToString("000", CultureInfo.InvariantCulture) + ".txt"), i.ToString(CultureInfo.InvariantCulture));
            HistoryNodeId Id(string rel) => HistoryNodeIdentity.Read(Path.Combine(tree, rel), false);
            string? RefusalOf(string rel) => new HistoryLinkCache().Check(Path.Combine(tree, rel), false).Refusal;
            // Each entry checked in turn, breadth first, after the root itself (as the seed export checks it first).
            string OneByOne()
            {
                var cache = new HistoryLinkCache();
                cache.Check(tree, false);
                var seen = new List<string>();
                var queue = new Queue<string>();
                queue.Enqueue(tree);
                while (queue.Count > 0)
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(queue.Dequeue()))
                    {
                        var one = cache.Check(entry, false);
                        if (one.Refusal != null) { seen.Add("refused " + one.Refusal); return string.Join("|", seen); }
                        var full = HistoryLinkCache.FullPath(entry);
                        if (one.Leaf.Directory && !one.Leaf.Linked) queue.Enqueue(full); else seen.Add(full);
                    }
                }
                return string.Join("|", seen);
            }
            string Walked()
            {
                var cache = new HistoryLinkCache();
                cache.Check(tree, false);
                var walk = cache.Walk([tree], false);
                var text = walk.Files.Select(f => f.FullName).ToList();
                if (walk.Refusal != null) text.Add("refused " + walk.Refusal);
                return string.Join("|", text);
            }
            var clean = OneByOne();
            Walked().ShouldBe(clean, "walk clean");
            clean.Split('|').Length.ShouldBe(203);
            clean.ShouldNotContain("refused");
            var a1 = Id("a.txt");
            var a2 = Id("a.txt");
            var b = Id("sub/b.txt");
            var dir = Id("sub");
            var gone = Id("sub/none.txt");
            (a1.Dev == a2.Dev && a1.Ino == a2.Ino && a1.Ino != b.Ino && !a1.Linked && !a1.Directory && !a1.Missing).ShouldBeTrue("identity file");
            ((a1.Ino & 0xFFFFFFFFUL) != 0 && (b.Ino & 0xFFFFFFFFUL) != 0).ShouldBeTrue("identity index low bits");
            (dir.Directory && !dir.Linked && gone.Missing && !gone.Linked && !gone.Unreadable).ShouldBeTrue("identity dir/missing");
            // A hard link: both names refused, one identity; the walk stops at the first name it meets, as checking in turn does.
            HardLink(Path.Combine(tree, "many/f100-hard.txt"), Path.Combine(tree, "many/f100.txt"));
            var h1 = Id("many/f100.txt");
            var h2 = Id("many/f100-hard.txt");
            (h1.Linked && h2.Linked && h1.Dev == h2.Dev && h1.Ino == h2.Ino).ShouldBeTrue("hard link identity");
            RefusalOf("many/f100.txt").ShouldBe("evidence-link");
            RefusalOf("many/f100-hard.txt").ShouldBe("evidence-link");
            var hard = OneByOne();
            Walked().ShouldBe(hard, "walk hard link");
            hard.ShouldEndWith("|refused evidence-link");
            File.Delete(Path.Combine(tree, "many/f100-hard.txt"));
            // Symbolic links (a file, a directory) and, on Windows, a junction.
            var links = new List<(string Rel, string Kind)> { ("sub/file-link.txt", "File"), ("sub/dir-link", "Directory") };
            if (OperatingSystem.IsWindows()) links.Add(("sub/junction", "Junction"));
            foreach (var (rel, kind) in links)
            {
                var path = Path.Combine(tree, rel);
                if (kind == "File") File.CreateSymbolicLink(path, Path.Combine(tree, "a.txt"));
                else if (kind == "Directory") Directory.CreateSymbolicLink(path, Path.Combine(tree, "empty"));
                else Junction(path, Path.Combine(tree, "empty"));
                var linked = Id(rel);
                var one = OneByOne();
                linked.Linked.ShouldBeTrue(kind + " link identity");
                RefusalOf(rel).ShouldBe("evidence-link", kind + " link check");
                Walked().ShouldBe(one, kind + " link walk");
                one.ShouldEndWith("|refused evidence-link");
                if (kind == "File") File.Delete(path); else Directory.Delete(path);
            }
            // A cached directory replaced by another (not a link): the parent's recheck sees the new identity.
            var swapCache = new HistoryLinkCache();
            var before = swapCache.Check(Path.Combine(tree, "sub/deeper/c.txt"), false).Refusal;
            Directory.Move(Path.Combine(tree, "sub/deeper"), Path.Combine(tree, "sub/deeper-held"));
            Directory.CreateDirectory(Path.Combine(tree, "sub/deeper"));
            File.WriteAllText(Path.Combine(tree, "sub/deeper/c.txt"), "forged");
            var after = swapCache.Check(Path.Combine(tree, "sub/deeper/c.txt"), false).Refusal;
            before.ShouldBeNull("swap before");
            after.ShouldBe("evidence-link", "swap after");
            Walked().ShouldBe(OneByOne(), "walk after swap");
        }
        finally { DeleteRoot(root); }
    }

    // ---- The planned manifest partition and the census digest order (Test-NCPlannedManifestMatchesFallback). ----

    /// <summary>Get-TestPhaseClassBasisMap: class -> basis from scripts/lib/nightly-class-costs.json.</summary>
    private static Hashtable ClassBasisMap()
    {
        var map = Table();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo, "scripts", "lib", "nightly-class-costs.json")));
        if (doc.RootElement.TryGetProperty("classes", out var classes))
            foreach (var row in classes.EnumerateObject())
                map[row.Name] = row.Value.TryGetProperty("basis", out var basis) && basis.ValueKind == JsonValueKind.String ? basis.GetString() : "";
        return map;
    }

    /// <summary>
    /// Write-TestPhasePlannedManifest's document text (groups-planned-&lt;suite&gt;.json): each group's planned ids (its
    /// carried nodes, else the view's required UIDs of its classes), pricing basis, filter and seconds, and the
    /// compiled duplicate / missing / extra partition against the view's required UIDs.
    /// </summary>
    private static string PlannedManifest(object[] groups, object[] nodes, object[] exclusions)
    {
        var allPlanned = new List<string>();
        var entry = Obj(("Document", Obj(("Nodes", nodes))), ("InProgress", Array.Empty<object>()), ("Views", new Dictionary<string, object>(StringComparer.Ordinal)));
        var view = NightlyDiscoveryIndex.View(entry, exclusions, true);
        var basisMap = ClassBasisMap();
        var costTable = ClassCostTable();
        var groupRows = new List<object>();
        foreach (var group in groups)
        {
            var ids = new List<string>();
            var names = PsValue.PropertyNames(group);
            bool Has(string name) => names.Contains(name, StringComparer.OrdinalIgnoreCase);
            var carried = Has("nodes") && PsValue.Prop(group, "nodes") != null ? PsValue.AsArray(PsValue.Prop(group, "nodes")) : [];
            if (carried.Length > 0)
            {
                foreach (var node in carried)
                {
                    var uid = PsValue.Str(PsValue.Prop(node, "Uid"));
                    if (!string.IsNullOrWhiteSpace(uid)) ids.Add(uid);
                }
            }
            else
            {
                var classes = PsValue.AsArray(PsValue.Prop(group, "classes")).Select(PsValue.Str).ToArray();
                PlannedManifestCheck.AddStrings(ids, NightlyDiscoveryIndex.RequiredUids(view, classes));
            }
            allPlanned.AddRange(ids);
            var bases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var className in PsValue.AsArray(PsValue.Prop(group, "classes")))
            {
                var key = PsValue.Str(className);
                bases.Add(basisMap.ContainsKey(key) ? PsValue.Str(basisMap[key]) : costTable.ContainsKey(key) ? "measured" : "model");
            }
            var pricing = bases.Count == 1 ? bases.First() : bases.Count > 1 ? "mixed" : "model";
            var filter = "";
            if (Has("exactFilter") && !string.IsNullOrWhiteSpace(PsValue.Str(PsValue.Prop(group, "exactFilter")))) filter = PsValue.Str(PsValue.Prop(group, "exactFilter"));
            else if (Has("groupFilter")) filter = PsValue.Str(PsValue.Prop(group, "groupFilter"));
            object seconds = 0;
            if (Has("seconds") && PsValue.Prop(group, "seconds") != null) seconds = PsValue.Double(PsValue.Prop(group, "seconds"));
            groupRows.Add(Ordered(("chunk", PsValue.Str(PsValue.Prop(group, "id"))), ("classes", PsValue.AsArray(PsValue.Prop(group, "classes")).Select(c => (object)PsValue.Str(c)).ToArray()),
                ("filter", filter), ("plannedTestIds", ids.ToArray()), ("plannedSeconds", seconds), ("pricingBasis", pricing), ("sharedManifestOrder", true)));
        }
        var partition = PlannedManifestCheck.Check(allPlanned, PsValue.AsArray(PsValue.Prop(view, "RequiredUids")));
        var missing = (object[])PsValue.Prop(partition, "Missing")!;
        var extra = (object[])PsValue.Prop(partition, "Extra")!;
        var manifest = Ordered(("version", 1), ("runId", "r"), ("attempt", "1"), ("suite", "antiphon"), ("sourceDigest", "s"), ("assemblyDigest", "a"),
            ("policyDigest", "p"), ("costFileDigest", "c"), ("assembly", "x"),
            ("requiredPartitionExact", !(bool)PsValue.Prop(partition, "Duplicate")! && missing.Length == 0 && extra.Length == 0),
            ("missingIds", missing), ("extraIds", extra), ("groups", groupRows.ToArray()));
        return TestPhaseJson.Text(manifest, 0);
    }

    [Test]
    public void Planned_manifest_and_census_ties_match_the_goldens()
    {
        // The planned-manifest partition (duplicates differing only by case, missing in RequiredUids order, one non-ASCII
        // extra, an InvariantCultureIgnoreCase-only match) against the text the PowerShell copy wrote; and the census
        // digest order (OrdinalIgnoreCase) over culture-equal and non-ASCII UIDs against the census fallback's order.
        var sh = ((char)0xAD).ToString();
        var tr = ((char)0x130).ToString();
        var ss = ((char)0xDF).ToString();
        var uu = ((char)0xDC).ToString();
        var ee = ((char)0xE9).ToString();
        static PlainObject Node(string uid, string cls) =>
            new(("Uid", uid), ("ClassName", cls), ("Type", cls.Split('.')[^1]), ("Namespace", "Ns"), ("Method", "M"), ("Categories", Array.Empty<object>()));
        static object Group(string id, string[] classes, string[]? uids)
        {
            var g = new PlainObject(("id", id), ("classes", classes.Cast<object>().ToArray()), ("seconds", 3), ("groupFilter", "/*/" + id));
            if (uids != null) g.Add("nodes", uids.Select(u => (object)new PlainObject(("Uid", u))).ToArray());
            return g;
        }
        var nodes = new object[]
        {
            Node("Ns.C.A", "Ns.C"), Node("Ns.C.b", "Ns.C"), Node("Ns.C." + uu, "Ns.C"), Node("Ns.C.a", "Ns.C"),
            Node("Ns.D.x", "Ns.D"), Node("Ns.D." + tr, "Ns.D"), Node("Ns.D.a", "Ns.D"), Node("Ns.Ex.q", "Ns.Ex"),
        };
        var rules = new object[] { Obj(("Class", "Ns.Ex"), ("Methods", Array.Empty<object>()), ("Reason", "r"), ("Owner", "o")) };
        var scenarios = new (string Name, object[] Groups)[]
        {
            ("exact", [Group("g1", ["Ns.C"], null), Group("g2", ["Ns.D"], null)]),
            ("dupCase", [Group("g1", ["Ns.C"], null), Group("g2", ["Ns.D"], ["Ns.D.x", "Ns.D." + tr, "Ns.D.a", "ns.c.b"])]),
            ("missing", [Group("g1", ["Ns.C"], ["Ns.C.A", "Ns.C." + uu]), Group("g2", ["Ns.D"], null)]),
            ("extra", [Group("g1", ["Ns.C"], null), Group("g2", ["Ns.D"], ["Ns.D.x", "Ns.D." + tr, "Ns.D.a", "Ns.Zz." + ee])]),
            ("invariantOnly", [Group("g1", ["Ns.C"], ["Ns.C.A", "Ns.C.b", "Ns.C." + uu, "Ns.D.a" + sh]), Group("g2", ["Ns.D"], ["Ns.D.x", "Ns.D." + tr])]),
        };
        var texts = Ordered(scenarios.Select(s => (s.Name, (object?)PlannedManifest(s.Groups, nodes, rules))).ToArray());
        Golden("plannedManifest", PsJson.Pipeline([texts]), "requiredPartitionExact\\\":true", "missingIds\\\":[\\\"Ns.C.b\\\"]", "extraIds\\\":[\\\"Ns.Zz.");
        var alpha = new[] { "a", "A", "b", "Z", "-", "_", ".", sh, tr, "i", "I", ss, "ss", uu, "u" + (char)0x308, ee, "E", "1", "10" };
        var rng = new Random(11);
        var tieNodes = new object[1500];
        for (var i = 0; i < tieNodes.Length; i++)
        {
            var s = "";
            for (var k = rng.Next(1, 5); k > 0; k--) s += alpha[rng.Next(alpha.Length)];
            tieNodes[i] = Node("Ns." + s, i % 4 == 0 ? "Ns.Ex" : "Ns.C");
        }
        Golden("censusTies", PsJson.Pipeline([NightlyCensus.Census(tieNodes, rules, true)]));
    }

    // ---- The discovery view key and the InProgress scan (Test-NCDiscoveryViewKey). ----

    [Test]
    public void Discovery_view_key_is_as_fine_as_the_json_key()
    {
        // DiscoveryViewKey.cs: two exclusion lists may share a key only where their JSON was equal (order, case, an added
        // or removed field, a value's type), equal content in new objects shares it, and a shape the walk does not vouch
        // for keeps the JSON key. The JSON keys are PowerShell's (core-golden/viewKey.json). Two of the driver's lists
        // cannot be built without PowerShell and are left out: 'added' (an Add-Member note on a reader row) and
        // 'm-wrapped' (a [psobject]-wrapped string).
        var policy = NightlyPolicy.Read(Repo, "");
        object[] Fresh() => NightlyPolicy.ProfileExclusions(policy.Object, "rc", "antiphon", "windows").Cast<object>().ToArray();
        static object[] Row(object? methods) => [Obj(("Suite", "antiphon"), ("Class", "C"), ("Methods", methods), ("Reason", "r"), ("Owner", "o"))];
        object deep = "leaf";
        for (var k = 0; k < 10; k++) deep = Obj(("inner", deep));
        var lists = new List<(string Name, object? List)> { ("real", Fresh()), ("again", Fresh()), ("empty", Array.Empty<object>()), ("null", null), ("single", Fresh()[0]) };
        var reversed = Fresh(); Array.Reverse(reversed); lists.Add(("reversed", reversed));
        var upper = Fresh(); ((ExclusionRow)upper[0]).Class = ((ExclusionRow)upper[0]).Class.ToUpperInvariant(); lists.Add(("upper", upper));
        var removed = Fresh(); var first = (ExclusionRow)removed[0];
        removed[0] = Obj(("Suite", first.Suite), ("Class", first.Class), ("Methods", first.Methods), ("Reason", first.Reason)); lists.Add(("removed", removed));
        lists.Add(("fewer", Fresh().Skip(1).ToArray()));
        lists.Add(("hash", Fresh().Select(r => (object)Ordered(("Suite", ((ExclusionRow)r).Suite), ("Class", ((ExclusionRow)r).Class), ("Methods", ((ExclusionRow)r).Methods),
            ("Reason", ((ExclusionRow)r).Reason), ("Owner", ((ExclusionRow)r).Owner))).ToArray()));
        var values = new (string Name, object? Value)[]
        {
            ("none", null), ("empty", Array.Empty<object>()), ("a", new object[] { "a" }), ("upperA", new object[] { "A" }), ("scalar", "a"), ("ab", new object[] { "a", "b" }),
            ("ba", new object[] { "b", "a" }), ("joined", new object[] { "ab" }), ("int", new object[] { 1 }), ("long", new object[] { 1L }), ("text", new object[] { "1" }),
            ("true", new object[] { true }), ("nullItem", new object?[] { null }), ("blank", new object[] { "" }), ("quote", new object[] { "a\"b" }),
            ("accent", new object[] { "caf" + (char)0xE9 }), ("nul", new object[] { "a" + (char)0 + "b" }), ("highA", new object[] { "x" + (char)0xD800 }),
            ("highB", new object[] { "x" + (char)0xD801 }), ("list", new List<string> { "a", "b" }), ("enum", new object[] { DayOfWeek.Monday }), ("deep", deep),
        };
        foreach (var (name, value) in values) lists.Add(("m-" + name, Row(value)));
        using var golden = JsonDocument.Parse(Read("viewKey"));
        var jsonKeys = golden.RootElement.GetProperty("jsonKeys");
        var key = new Dictionary<string, string>(StringComparer.Ordinal);
        var json = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, list) in lists)
        {
            json[name] = "json#" + jsonKeys.GetProperty(name).GetString();
            key[name] = DiscoveryViewKey.PolicyKey(list) ?? json[name];
        }
        var bad = new List<string>();
        var names = lists.Select(l => l.Name).ToArray();
        for (var a = 0; a < names.Length; a++)
            for (var b = a + 1; b < names.Length; b++)
                if (key[names[a]] == key[names[b]] && json[names[a]] != json[names[b]]) bad.Add("shared " + names[a] + "/" + names[b]);
        if (!System.Text.RegularExpressions.Regex.IsMatch(key["real"], "^profile#[0-9a-f]{64}$") || key["real"] != key["again"]) bad.Add("real rows: " + key["real"]);
        foreach (var name in new[] { "m-enum", "m-deep" }) if (key[name] != json[name]) bad.Add("vouched " + name);
        foreach (var name in new[] { "reversed", "upper", "removed", "fewer", "empty", "null", "single" }) if (key[name] == key["real"]) bad.Add("same as real: " + name);
        // One entry: equal content in new objects gets the same view object, different content another one.
        var entry = Obj(("Document", Obj(("Nodes", FixtureNodes(400).Cast<object>().ToArray()))), ("Views", new Dictionary<string, object>(StringComparer.Ordinal)));
        var firstView = NightlyDiscoveryIndex.View(entry, Fresh(), true);
        if (!ReferenceEquals(firstView, NightlyDiscoveryIndex.View(entry, Fresh(), true))) bad.Add("equal content: second view");
        if (ReferenceEquals(firstView, NightlyDiscoveryIndex.View(entry, Fresh().Skip(1).ToArray(), true))) bad.Add("different content: same view");
        if (((Dictionary<string, object>)entry["Views"]!).Count != 2) bad.Add("views " + ((Dictionary<string, object>)entry["Views"]!).Count);
        static PlainObject State(object? state) => new(("Uid", "u"), ("State", state));
        var mixed = new object?[] { State("InProgress"), State("inprogress"), State("InProgress "), State("Passed"), State(null), null, State("InProgress" + (char)0x200B), State(1),
            Table(("State", "InProgress")), "InProgress" };
        var inProgress = golden.RootElement.GetProperty("inProgress");
        foreach (var (name, set) in new (string, object?)[] { ("mixed", mixed), ("empty", Array.Empty<object>()), ("null", null) })
        {
            var members = PsValue.AsArray(set);
            var loop = inProgress.GetProperty(name).EnumerateArray().Select(e => members[e.GetInt32()]).ToArray();
            var core = DiscoveryViewKey.InProgressNodes(set);
            if (core == null || core.Length != loop.Length || core.Where((n, k) => !ReferenceEquals(n, loop[k])).Any()) bad.Add("inProgress " + name);
        }
        // A node or State that is a list: PowerShell would enumerate or filter it, so the core leaves it to the caller's loop.
        if (DiscoveryViewKey.InProgressNodes(new object[] { State(new object[] { "InProgress", "x" }) }) != null) bad.Add("inProgress stateList answered");
        if (DiscoveryViewKey.InProgressNodes(new object[] { new object[] { State("InProgress") } }) != null) bad.Add("inProgress nodeList answered");
        bad.ShouldBeEmpty();
    }

    // ---- The discovery cache against a timestamp-preserving replace (Test-NCDiscoveryCacheAgrees). ----

    private static string DiscoveryInventoryText(string uid) =>
        "{\"format\":\"antiphon-tunit-discovery-v1\",\"tunitVersion\":\"1.44.0\",\"mtpVersion\":\"2.2.2\",\"assemblyHash\":\"hash\",\"nodes\":[{\"uid\":\"" + uid
        + "\",\"type\":\"C\",\"namespace\":\"N\",\"method\":\"M\",\"categories\":[],\"state\":\"Discovered\"}]}";

    [Test]
    [NotInParallel]
    public void Discovery_cache_agrees_with_the_disk_after_a_same_length_replace()
    {
        // Read a settled discovery whose required UID is u1, then atomically replace it with an equal-length document whose
        // required UID is u2 and put the old timestamp back. The same discovery index and a fresh read must require u2,
        // and the expanded-rows check must agree for a terminal Passed row of u1 (cachedOk=True diskOk=False, missing=u2).
        var root = NewRoot();
        try
        {
            var path = Path.Combine(root, "discovery-cache", "suite.discovery.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var u1 = DiscoveryInventoryText("u1");
            var u2 = DiscoveryInventoryText("u2");
            var utf8 = new UTF8Encoding(false);
            File.WriteAllBytes(path, utf8.GetBytes(u1));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-1));
            var kept = File.GetLastWriteTimeUtc(path);
            NightlyClassNames.ClearHashes();
            var index = new NightlyDiscoveryIndex();
            var first = NightlyDiscoveryIndex.EntryOf(index, path);
            var firstUids = NightlyCensus.RequiredUids(PsValue.AsArray(PsValue.Prop(first.Document, "Nodes")), [], null, false);
            string.Join(",", firstUids).ShouldBe("u1", "primed");
            u1.Length.ShouldBe(u2.Length);
            var len1 = new FileInfo(path).Length;
            len1.ShouldBe(utf8.GetBytes(u1).Length);
            File.WriteAllBytes(path + ".tmp", utf8.GetBytes(u2));
            File.Replace(path + ".tmp", path, path + ".bak");
            File.Delete(path + ".bak");
            File.SetLastWriteTimeUtc(path, kept);
            var again = NightlyDiscoveryIndex.EntryOf(index, path);
            var againUids = NightlyCensus.RequiredUids(PsValue.AsArray(PsValue.Prop(again.Document, "Nodes")), [], null, false);
            var disk = NightlyDiagnosticLog.ReadDiscovery(path);
            var diskUids = NightlyCensus.RequiredUids(PsValue.AsArray(PsValue.Prop(disk, "Nodes")), [], null, false);
            var row = Obj(("Uid", "u1"), ("Outcome", "Passed"), ("ClassName", "N.C"), ("Type", "C"), ("Method", "M"));
            var cachedCover = NightlyChunkVerdict.ExpandedRowsPresent(PsValue.AsArray(PsValue.Prop(again.Document, "Nodes")), [row], againUids.Select(PsValue.Str).ToArray());
            var diskCover = NightlyChunkVerdict.ExpandedRowsPresent(PsValue.AsArray(PsValue.Prop(disk, "Nodes")), [row], diskUids.Select(PsValue.Str).ToArray());
            var cachedUid = PsValue.Str(againUids.FirstOrDefault());
            var diskUid = PsValue.Str(diskUids.FirstOrDefault());
            var detail = "cachedUid=" + cachedUid + " diskUid=" + diskUid + " cachedOk=" + PsValue.Prop(cachedCover, "Ok") + " diskOk=" + PsValue.Prop(diskCover, "Ok")
                + " missing=" + string.Join(",", PsValue.AsArray(PsValue.Prop(diskCover, "Missing")));
            cachedUid.ShouldBe(diskUid, detail);
            PsValue.IsTrue(PsValue.Prop(cachedCover, "Ok")).ShouldBe(PsValue.IsTrue(PsValue.Prop(diskCover, "Ok")), detail);
            diskUid.ShouldBe("u2", detail);
            PsValue.IsTrue(PsValue.Prop(diskCover, "Ok")).ShouldBeFalse(detail);
            new FileInfo(path).Length.ShouldBe(len1, detail);
        }
        finally { DeleteRoot(root); }
    }

    // ---- The discovery reader's tolerance (Test-NCDiscoveryParserTolerance). ----

    private static void WriteWide(string path, string text, bool bigEndian, bool bom)
    {
        var encoding = new UnicodeEncoding(bigEndian, bom);
        var payload = encoding.GetBytes(text);
        File.WriteAllBytes(path, (bom ? encoding.GetPreamble() : []).Concat(payload).ToArray());
    }

    private static (bool Ok, int Count, string Uid, string Error) ReadDiscoveryOutcome(string path)
    {
        try
        {
            var nodes = PsValue.AsArray(PsValue.Prop(NightlyDiagnosticLog.ReadDiscovery(path), "Nodes"));
            return (true, nodes.Length, nodes.Length > 0 ? PsValue.Str(PsValue.Prop(nodes[0], "Uid")) : "", "");
        }
        catch (Exception ex) { return (false, 0, "", ex.Message); }
    }

    [Test]
    public void Discovery_reader_accepts_bom_trailing_comma_and_utf16_and_refuses_comments()
    {
        // The discovery writer (DiscoveryWriter.WriteFile) emits UTF-8 without a BOM, with commas only between values and
        // no comments. The reader accepts a UTF-8 BOM, a trailing comma, and a UTF-16 document that starts with a BOM (FF
        // FE or FE FF). A UTF-16 document without a BOM is discovery-unreadable with reason utf-16, and a comment names
        // this file and the reason. No ConvertFrom-Json fallback.
        var root = NewRoot();
        try
        {
            var dir = Path.Combine(root, "discovery-parser");
            Directory.CreateDirectory(dir);
            var text = DiscoveryInventoryText("u1");
            var produced = Path.Combine(dir, "produced.discovery.json");
            NightlyDiagnosticLog.WriteDiscoveryDocument(produced, NightlyDiagnosticLog.NewDiscoveryDocument(
                [Obj(("uid", "u1"), ("type", "C"), ("namespace", "N"), ("method", "M"), ("categories", Array.Empty<object>()), ("state", "Discovered"))], "hash"));
            var bytes = File.ReadAllBytes(produced);
            var producedText = Encoding.UTF8.GetString(bytes);
            (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF).ShouldBeFalse("producer bom");
            (producedText.Contains("/*", StringComparison.Ordinal) || producedText.Contains("//", StringComparison.Ordinal)).ShouldBeFalse("producer comment");
            System.Text.RegularExpressions.Regex.IsMatch(producedText, ",\\s*[}\\]]").ShouldBeFalse("producer trailing comma");
            ReadDiscoveryOutcome(produced).ShouldBe((true, 1, "u1", ""), "producer");
            var utf8 = new UTF8Encoding(false);
            var plain = Path.Combine(dir, "plain.discovery.json");
            File.WriteAllBytes(plain, utf8.GetBytes(text));
            ReadDiscoveryOutcome(plain).ShouldBe((true, 1, "u1", ""), "utf8");
            var bomPath = Path.Combine(dir, "bom.discovery.json");
            File.WriteAllBytes(bomPath, new UTF8Encoding(true).GetPreamble().Concat(utf8.GetBytes(text)).ToArray());
            ReadDiscoveryOutcome(bomPath).ShouldBe((true, 1, "u1", ""), "utf8-bom");
            var trailPath = Path.Combine(dir, "trail.discovery.json");
            File.WriteAllBytes(trailPath, utf8.GetBytes(text.Insert(text.LastIndexOf('}'), ",")));
            ReadDiscoveryOutcome(trailPath).ShouldBe((true, 1, "u1", ""), "trailing-comma");
            var utf16 = Path.Combine(dir, "utf16.discovery.json");
            WriteWide(utf16, text, false, true);
            File.ReadAllBytes(utf16).Take(2).ShouldBe(new byte[] { 0xFF, 0xFE }, "utf16-le bom");
            ReadDiscoveryOutcome(utf16).ShouldBe((true, 1, "u1", ""), "utf16-le");
            var utf16Be = Path.Combine(dir, "utf16-be.discovery.json");
            WriteWide(utf16Be, text, true, true);
            File.ReadAllBytes(utf16Be).Take(2).ShouldBe(new byte[] { 0xFE, 0xFF }, "utf16-be bom");
            ReadDiscoveryOutcome(utf16Be).ShouldBe((true, 1, "u1", ""), "utf16-be");
            var bare = Path.Combine(dir, "utf16-bare.discovery.json");
            WriteWide(bare, text, false, false);
            var bareBytes = File.ReadAllBytes(bare);
            (bareBytes[0] == 0xFF && bareBytes[1] == 0xFE || bareBytes[0] == 0xFE && bareBytes[1] == 0xFF).ShouldBeFalse("utf16 without bom");
            ReadDiscoveryOutcome(bare).ShouldBe((false, 0, "", "discovery-unreadable: " + bare + ": utf-16"), "utf16-nobom");
            var empty = Path.Combine(dir, "utf16-empty.discovery.json");
            File.WriteAllBytes(empty, [0xFF, 0xFE]);
            ReadDiscoveryOutcome(empty).ShouldBe((false, 0, "", "empty discovery " + empty), "utf16-empty");
            var wideComment = Path.Combine(dir, "utf16-comment.discovery.json");
            WriteWide(wideComment, text.Insert(1, "/* retained inventory */"), false, true);
            ReadDiscoveryOutcome(wideComment).ShouldBe((false, 0, "", "discovery-unreadable: " + wideComment + ": comment"), "utf16-comment");
            var comment = Path.Combine(dir, "comment.discovery.json");
            File.WriteAllBytes(comment, utf8.GetBytes(text.Insert(1, "/* retained inventory */")));
            ReadDiscoveryOutcome(comment).ShouldBe((false, 0, "", "discovery-unreadable: " + comment + ": comment"), "comment");
        }
        finally { DeleteRoot(root); }
    }

    // ---- The cache file identity (scripts/test-nightly-fileid.ps1, Test-NCDiscoveryCacheFileId). ----

    [Test]
    [NotInParallel]
    public void Cache_file_identity_prefers_the_128_bit_id_and_never_trusts_unknown()
    {
        // The cache identity on Windows prefers the 128-bit FileIdInfo id and falls back to the 64-bit file index. Unknown
        // is never equal, including to itself. A different id does not hit the hash cache. HistoryNodeIdentity.Read is the
        // production factory.
        var root = NewRoot();
        try
        {
            var path = Path.Combine(root, "discovery-file-id", "suite.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var a = Encoding.UTF8.GetBytes("alpha-content-one");
            var b = Encoding.UTF8.GetBytes("bravo-content-two");
            var c = Encoding.UTF8.GetBytes("charlie-content-x");
            File.WriteAllBytes(path, a);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-1));
            var kept = File.GetLastWriteTimeUtc(path);
            var id128 = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
            var other = (byte[])id128.Clone();
            other[15] = 99;
            var full = CacheFileIdentity.Decide(true, 9, id128, true, 9, 100);
            var fullOtherIndex = CacheFileIdentity.Decide(true, 9, id128, true, 9, 999);
            var different = CacheFileIdentity.Decide(true, 9, other, true, 9, 100);
            var otherVolume = CacheFileIdentity.Decide(true, 8, id128, true, 9, 100);
            var fallback = CacheFileIdentity.Decide(false, 0, null, true, 9, 100);
            var fallbackOther = CacheFileIdentity.Decide(false, 0, null, true, 9, 101);
            var unknown = CacheFileIdentity.Decide(false, 0, null, false, 0, 0);
            var malformed = CacheFileIdentity.Decide(true, 9, new byte[15], true, 9, 100);
            var malformedUnknown = CacheFileIdentity.Decide(true, 9, new byte[15], false, 9, 100);
            (full.Known && full.Kind.ToString() == "FileId128" && CacheIdentity.Same(full, fullOtherIndex)).ShouldBeTrue("fileId128 kind=" + full.Kind);
            CacheIdentity.Same(full, different).ShouldBeFalse("different 128-bit id compared equal");
            CacheIdentity.Same(full, otherVolume).ShouldBeFalse("different volume compared equal");
            (fallback.Known && fallback.Kind.ToString() == "FileIndex64" && CacheIdentity.Same(fallback, fallback) && !CacheIdentity.Same(fallback, fallbackOther)).ShouldBeTrue("fallback kind=" + fallback.Kind);
            CacheIdentity.Same(full, fallback).ShouldBeFalse("128-bit id compared equal to the 64-bit fallback");
            (!unknown.Known && !CacheIdentity.Same(unknown, unknown) && !CacheIdentity.Same(unknown, fallback) && !CacheIdentity.Same(unknown, full)).ShouldBeTrue("unknown identity compared equal");
            (malformed.Known && malformed.Kind.ToString() == "FileIndex64" && CacheIdentity.Same(malformed, fallback) && !malformedUnknown.Known).ShouldBeTrue("malformed kind=" + malformed.Kind);
            var node = HistoryNodeIdentity.Read(path, false);
            (node.CacheId != null && node.CacheId.Known && !node.Missing && !node.Linked).ShouldBeTrue("read-cache-id");
            NightlyClassNames.ClearHashes();
            CacheFileIdentity.TestOverride = null;
            try
            {
                CacheFileIdentity.TestOverride = full;
                var hash1 = NightlyClassNames.CachedFileSha256(path);
                File.WriteAllBytes(path, b);
                File.SetLastWriteTimeUtc(path, kept);
                CacheFileIdentity.TestOverride = different;
                var hash2 = NightlyClassNames.CachedFileSha256(path);
                var diskB = NightlyClassNames.FileSha256(path);
                (a.Length == b.Length && hash2 == diskB && hash2 != hash1).ShouldBeTrue("different-id-miss hash1=" + hash1 + " hash2=" + hash2 + " disk=" + diskB);
                NightlyClassNames.CachedFileSha256(path).ShouldBe(hash2, "same-id-hit");
                File.WriteAllBytes(path, c);
                File.SetLastWriteTimeUtc(path, kept);
                CacheFileIdentity.TestOverride = unknown;
                var hash4 = NightlyClassNames.CachedFileSha256(path);
                var diskC = NightlyClassNames.FileSha256(path);
                (c.Length == b.Length && hash4 == diskC && hash4 != hash2).ShouldBeTrue("unknown-miss hash4=" + hash4 + " disk=" + diskC + " stale=" + hash2);
            }
            finally
            {
                CacheFileIdentity.TestOverride = null;
                NightlyClassNames.ClearHashes();
            }
        }
        finally { DeleteRoot(root); }
    }
}
