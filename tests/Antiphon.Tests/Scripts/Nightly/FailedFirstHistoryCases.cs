using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Antiphon.Nightly;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S14: the failure-history fixtures of scripts/test-nightly-failed-first.ps1, in-process over the compiled
/// history (PriorFailureHistory Export, Read and Resolve with plain values): Write-FFJson, Copy-FFObject,
/// Test-FFRefusal, New-FFHistoricalFixture (36 TRX failures over four suites, a torn diagnostic, a retained plan)
/// and Test-FFChunkAccounting.
/// </summary>
internal static class FailedFirstHistoryCases
{
    public static string Repo => NightlyPlanCases.Repo;

    // ---- values -------------------------------------------------------------------------------------------------

    /// <summary>Write-FFJson: the value as indented JSON, UTF-8 without a BOM (custom objects, dictionaries, lists, scalars).</summary>
    public static void WriteJson(string path, object? value)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        File.WriteAllText(path, JsonText(value, indented: true), new UTF8Encoding(false));
    }

    /// <summary>ConvertTo-Json of plain values (PlainObject and dictionaries as objects, lists as arrays, dates round-trip).</summary>
    public static string JsonText(object? value, bool indented = false)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            Write(writer, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(Utf8JsonWriter w, object? value)
    {
        switch (value)
        {
            case null: w.WriteNullValue(); return;
            case string s: w.WriteStringValue(s); return;
            case bool b: w.WriteBooleanValue(b); return;
            case int n: w.WriteNumberValue(n); return;
            case long n: w.WriteNumberValue(n); return;
            case double n: w.WriteNumberValue(n); return;
            case decimal n: w.WriteNumberValue(n); return;
            case DateTime d: w.WriteStringValue(d.ToString("o", CultureInfo.InvariantCulture)); return;
            case DateTimeOffset d: w.WriteStringValue(d.ToString("o", CultureInfo.InvariantCulture)); return;
            case JsonElement e: e.WriteTo(w); return;
            case PlainObject plain:
                w.WriteStartObject();
                foreach (var entry in plain.Entries) { w.WritePropertyName(entry.Key); Write(w, entry.Value); }
                w.WriteEndObject();
                return;
            case IDictionary dictionary:
                w.WriteStartObject();
                foreach (DictionaryEntry entry in dictionary) { w.WritePropertyName(Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? ""); Write(w, entry.Value); }
                w.WriteEndObject();
                return;
            case IEnumerable items:
                w.WriteStartArray();
                foreach (var item in items) Write(w, item);
                w.WriteEndArray();
                return;
            default:
                if (value.GetType().IsPrimitive) { w.WriteRawValue(Convert.ToString(value, CultureInfo.InvariantCulture)!, true); return; }
                // A plain record class (a policy row): its public properties.
                w.WriteStartObject();
                foreach (var property in value.GetType().GetProperties())
                {
                    if (property.GetIndexParameters().Length != 0) continue;
                    w.WritePropertyName(property.Name);
                    Write(w, property.GetValue(value));
                }
                w.WriteEndObject();
                return;
        }
    }

    /// <summary>Copy-FFObject: a deep copy (custom objects, dictionaries and lists copied; scalars shared).</summary>
    public static object? Copy(object? value) => value switch
    {
        PlainObject plain => CopyPlain(plain),
        string s => s,
        IDictionary d => CopyDictionary(d),
        IEnumerable items => items.Cast<object?>().Select(Copy).ToArray(),
        _ => value,
    };

    private static PlainObject CopyPlain(PlainObject plain)
    {
        var copy = new PlainObject();
        foreach (var entry in plain.Entries) copy.Add(entry.Key, Copy(entry.Value));
        return copy;
    }

    private static OrderedDictionary CopyDictionary(IDictionary d)
    {
        var copy = new OrderedDictionary(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in d) copy[entry.Key] = Copy(entry.Value);
        return copy;
    }

    /// <summary>[string]$o.name.</summary>
    public static string Str(object? o, string name) => PsValue.Str(PsValue.Prop(o, name));

    /// <summary>@($o.name).</summary>
    public static object[] Items(object? o, string name)
    {
        var value = PsValue.Prop(o, name);
        return value == null ? [] : PsValue.AsArray(value);
    }

    /// <summary>[bool]$o.name.</summary>
    public static bool True(object? o, string name) => PsValue.IsTrue(PsValue.Prop(o, name));

    /// <summary>Test-FFRefusal: the action refuses with a reason containing the text (-clike *reason*).</summary>
    public static bool Refuses(Action action, string reason)
    {
        try { action(); return false; }
        catch (Exception e) { return Message(e).Contains(reason, StringComparison.Ordinal); }
    }

    /// <summary>Get-NightlyRefusalText: the innermost invocation's message.</summary>
    public static string Message(Exception e)
    {
        while (e is System.Reflection.TargetInvocationException && e.InnerException != null) e = e.InnerException;
        return e.Message;
    }

    /// <summary>git -C repo rev-parse HEAD.</summary>
    public static string HeadSha(string repo) => RunGit(repo, "rev-parse", "HEAD").Trim();

    /// <summary>git -C repo args: its standard output; a nonzero exit throws with its error output.</summary>
    public static string RunGit(string repo, params string[] args)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(repo);
        foreach (var a in args) start.ArgumentList.Add(a);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("git " + string.Join(" ", args) + " exited " + process.ExitCode + ": " + error.Result);
        return output.Result;
    }

    /// <summary>A prepared suite of one chunk 'all' holding every node's class.</summary>
    public static object[] OneChunkSuite(object[] nodes) =>
        [FailedFirstDispatchCases.Suite("antiphon", nodes,
            [new PlainObject(("id", "all"), ("classes", nodes.Select(n => (object)Str(n, "ClassName")).ToArray()), ("nodes", nodes))], [])];

    /// <summary>The production planner over one declared chunk, without a policy (the history fixtures' plans).</summary>
    public static object[] Plan(object[] nodes, string declared, string suite) =>
        NightlyTestsPlan.ProfileLaunchPlan(nodes, [new PlainObject(("id", declared), ("classes", Array.Empty<object>()))], Array.Empty<object>(), suite, null,
            NightlyPlanCases.Costs());

    /// <summary>Writes a profile discovery document of the nodes (assembly fixture-assembly).</summary>
    public static void Discovery(string path, IEnumerable<object> nodes) =>
        WriteJson(path, NightlyDiagnosticLog.NewDiscoveryDocument(nodes.ToArray(), "fixture-assembly"));

    // ---- New-FFHistoricalFixture --------------------------------------------------------------------------------

    /// <summary>The retained run: its directory, the summary (mutable rows), the nodes of each suite and the fallback classes.</summary>
    public sealed record HistoricalRun(string Run, OrderedDictionary Summary, Dictionary<string, List<object>> Nodes, string[] Classes, string[] Fallback)
    {
        public List<OrderedDictionary> Rows => (List<OrderedDictionary>)Summary["suites"]!;

        /// <summary>Write-FFJson of the summary after a row change.</summary>
        public void WriteSummary() => WriteJson(Path.Combine(Run, "summary.json"), Summary);
    }

    public static readonly string[] Suites4 = ["antiphon", "session-runner", "agents-pty", "e2e"];

    public static HistoricalRun Historical(string root)
    {
        var run = Path.Combine(root, "retained-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        string[] names =
        [
            "Application.TerminalRunnerSeatReleaseTests", "Application.SessionRuntimeActionTests", "Application.AttentionFeedNoiseTests",
            "Application.AgentReportStoreTests", "Application.CardFileGitFailureAcceptanceTests", "Application.GrokSignInRuntimeTests",
            "Application.CardFileIgnoreAcceptanceTests", "Application.StandingRestartAccountingTests", "Application.GrokRulesResumeMigrationTests",
            "Application.LandNonCodeSkipTests", "Checkpoints.CheckpointRepeatBuildIsolationTests",
            "Infrastructure.LandingGitTests", "Infrastructure.RepositoryChildRecoveryTests", "Infrastructure.WorktreeManagerTests",
            "Scripts.TestSelectionTests", "Scripts.ChangeAwareReleaseTests", "Scripts.EvidenceDiffGuardTests", "Scripts.RunCheckpointRepeatScriptTests",
            "Scripts.C1008PlatformContractTests", "Scripts.WindowsUnitPlatformAdmissionTests", "Scripts.CardRelationshipScriptTests",
            "Scripts.NightlyDeadOwnerLockTests", "Scripts.ScriptsLaneRepairTests", "Scripts.RemoteScriptContractTests", "TestHelpers.TestPhaseSummaryTests",
        ];
        var classes = names.Select(n => "Antiphon.Tests." + n)
            .Concat(["Antiphon.SessionRunner.Tests.GrokRulesHttpAcceptanceTests", "Antiphon.Agents.Pty.Tests.FakeClaudeContractTests", "Antiphon.E2E.BoardE2ETests"]).ToArray();
        var nodes = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);
        var defs = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
        var results = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < 36; i++)
        {
            var c = classes[i % 28];
            var suite = "antiphon";
            if (c.StartsWith("Antiphon.SessionRunner.", StringComparison.Ordinal)) suite = "session-runner";
            if (c.StartsWith("Antiphon.Agents.Pty.", StringComparison.Ordinal)) suite = "agents-pty";
            if (c.StartsWith("Antiphon.E2E.", StringComparison.Ordinal)) suite = "e2e";
            if (!nodes.ContainsKey(suite)) { nodes[suite] = []; defs[suite] = new(); results[suite] = new(); }
            var uid = "trx-" + i;
            var method = "Failure" + i;
            nodes[suite].Add(FailedFirstDispatchCases.Node(c, method, uid));
            defs[suite].Append("<UnitTest id=\"").Append(uid).Append("\"><TestMethod className=\"").Append(c).Append("\" name=\"").Append(method).Append("\" /></UnitTest>");
            results[suite].Append("<UnitTestResult testId=\"").Append(uid).Append("\" outcome=\"Failed\" />");
        }
        var rows = new List<OrderedDictionary>();
        foreach (var suite in Suites4)
        {
            var name = suite + "-failed.trx";
            File.WriteAllText(Path.Combine(run, name), "<TestRun><TestDefinitions>" + defs[suite] + "</TestDefinitions><Results>" + results[suite] + "</Results></TestRun>");
            rows.Add(new OrderedDictionary { { "id", suite }, { "chunk", "failed" }, { "trx", name }, { "exitCode", 1 }, { "result", "FAIL" }, { "timedOut", false } });
        }
        string[] extra = ["CheckpointOrFilterTests", "CheckpointLaunchCleanupTests", "CheckpointLaunchCleanupTests", "CheckpointLaunchCleanupTests", "CheckpointAppTests",
            "CheckpointAdmissionContractTests", "CheckpointSlotExecutorTests", "CheckpointExecutorLogTests"];
        var diag = "    version: 2.2.2\nTest framework UID: 'TUnitExtension' Version: '1.44.0.0'\n";
        var recoveredDiag = diag;
        for (var i = 0; i < 10; i++)
        {
            var type = i < 8 ? extra[i] : "RecoveredTests";
            var state = i < 7 ? "Error" : i < 9 ? "InProgress" : "Passed";
            var uid = i >= 8 ? "recovered" : "diag-" + i;
            var method = i == 7 ? "concurrent_callbacks_append_each_line_once_without_overlap" : "Case" + i;
            if (i == 9) method = "Case8";
            var record = "TestNodeUpdateMessage { TestNode = TestNode { Uid = TestNodeUid { Value = " + uid + " }, DisplayName = fixture, Properties = [ TestMethodIdentifierProperty { AssemblyFullName = Fixture, Namespace = Antiphon.Tests.Checkpoints, TypeName = " + type + ", MethodName = " + method + ", MethodArity = 0, ParameterTypeFullNames = [], ReturnTypeFullName = System.Void }, " + state + "TestNodeStateProperty { Explanation =  } ] } }\n";
            if (i < 8) diag += record; else recoveredDiag += record;
            if (i < 9) nodes["antiphon"].Add(FailedFirstDispatchCases.Node("Antiphon.Tests.Checkpoints." + type, method, uid));
        }
        Directory.CreateDirectory(Path.Combine(run, "antiphon-all-group-105-execution"));
        File.WriteAllText(Path.Combine(run, "antiphon-all-group-105-execution", "invocation.diag"), diag);
        File.WriteAllText(Path.Combine(run, "recovered.diag"), recoveredDiag);
        File.WriteAllText(Path.Combine(run, "recovered.trx"), "<TestRun><TestDefinitions><UnitTest id=\"recovered\"><TestMethod className=\"Antiphon.Tests.Checkpoints.RecoveredTests\" name=\"Case8\" /></UnitTest></TestDefinitions><Results><UnitTestResult testId=\"recovered\" outcome=\"Passed\" /></Results></TestRun>");
        rows.Add(new OrderedDictionary { { "id", "antiphon" }, { "chunk", "recovered" }, { "trx", "recovered.trx" }, { "execution", "recovered.diag" }, { "timedOut", false }, { "exitCode", 0 } });
        rows.Add(new OrderedDictionary { { "id", "antiphon" }, { "chunk", "all-group-105" }, { "trx", "antiphon-all-group-105.trx" }, { "execution", "antiphon-all-group-105-execution/invocation.diag" }, { "timedOut", true }, { "exitCode", 1 }, { "result", "FAIL" } });
        var summary = new OrderedDictionary
        {
            { "runId", "retained-run" }, { "attempt", "1" }, { "invocationId", "retained-run" }, { "completedAt", "2026-10-29T00:00:00Z" }, { "sha", new string('a', 40) },
            { "profile", "rc" }, { "platform", "windows" }, { "policyHash", new string('b', 64) }, { "selectedSuites", Suites4 }, { "suites", rows }, { "timingRoot", "timing" },
        };
        WriteJson(Path.Combine(run, "summary.json"), summary);
        Directory.CreateDirectory(Path.Combine(run, "timing"));
        var fallback = nodes["antiphon"].Where(n => Str(n, "Uid").StartsWith("diag-", StringComparison.Ordinal)).Select(n => NightlyCensus.BareClassIdentity(Str(n, "ClassName")))
            .Distinct(StringComparer.Ordinal).Append("Antiphon.Tests.Checkpoints.UnobservedCoPackedTests").ToArray();
        nodes["antiphon"].Add(FailedFirstDispatchCases.Node("Antiphon.Tests.Checkpoints.UnobservedCoPackedTests"));
        WriteJson(Path.Combine(run, "timing", "groups-planned-antiphon.json"), new OrderedDictionary
        {
            { "version", 1 }, { "suite", "antiphon" }, { "runId", "retained-run" }, { "attempt", "1" }, { "sourceDigest", new string('a', 40) }, { "policyDigest", new string('b', 64) },
            { "assemblyDigest", "fixture-assembly" },
            { "groups", new object[] { new OrderedDictionary { { "chunk", "all-group-105" }, { "classes", fallback }, { "plannedTestIds",
                nodes["antiphon"].Where(n => Str(n, "Uid").StartsWith("diag-", StringComparison.Ordinal) || Str(n, "ClassName").EndsWith("UnobservedCoPackedTests", StringComparison.Ordinal))
                    .Select(n => (object)Str(n, "Uid")).ToArray() } } } },
        });
        // The singleton is intentionally another suite. It must never define antiphon membership.
        WriteJson(Path.Combine(run, "timing", "groups-planned.json"), new OrderedDictionary
        {
            { "version", 1 }, { "suite", "observability" }, { "runId", "retained-run" }, { "attempt", "1" },
            { "groups", new object[] { new OrderedDictionary { { "chunk", "all-group-105" }, { "classes", new object[] { "Foreign.WrongSuiteTests" } }, { "plannedTestIds", new object[] { "wrong" } } } } },
        });
        foreach (var (suite, list) in nodes) Discovery(Path.Combine(run, suite + "-profile.discovery.json"), list);
        return new HistoricalRun(run, summary, nodes, classes, fallback);
    }

    // ---- Test-FFChunkAccounting ---------------------------------------------------------------------------------

    /// <summary>
    /// One chunk whose summary row (the real writer's shape: no coverageComplete, trx or execution) accounts for less
    /// than its evidence shows: the whole chunk becomes a fallback with the variant's reason and the sweep refuses;
    /// missing-bound keeps both classes resolved through the retained plan.
    /// </summary>
    public static bool ChunkAccounting(string root, string variant)
    {
        var run = Path.Combine(root, "accounting-" + variant);
        Directory.CreateDirectory(Path.Combine(run, "timing"));
        object[] nodes = [FailedFirstDispatchCases.Node("Antiphon.Tests.Accounting.FirstTests", "Run", "first"), FailedFirstDispatchCases.Node("Antiphon.Tests.Accounting.SecondTests", "Run", "second")];
        var row = new OrderedDictionary
        {
            { "id", "antiphon" }, { "chunk", "all" }, { "name", "antiphon all" }, { "log", @"C:\retired\logs\accounting\antiphon-all.log" }, { "exitCode", 0 }, { "timedOut", false },
            { "skipped", false }, { "result", "pass" },
        };
        switch (variant)
        {
            case "exit": row["exitCode"] = -1073741819; break;
            case "crash": case "crash-unbound": row["exitCode"] = -1073741819; row["result"] = "FAIL"; row["detail"] = "missing-terminal-accounting"; break;
            case "result": row["result"] = "FAIL"; break;
            case "detail": row["detail"] = "missing-terminal-accounting"; break;
        }
        WriteJson(Path.Combine(run, "summary.json"), new OrderedDictionary
        {
            { "runId", "accounting" }, { "attempt", "1" }, { "completedAt", "2026-10-29T00:00:00Z" }, { "sha", new string('a', 40) }, { "profile", "rc" }, { "platform", "windows" },
            { "policyHash", new string('b', 64) }, { "suites", new object[] { row } }, { "timingRoot", "timing" },
        });
        Discovery(Path.Combine(run, "antiphon-profile.discovery.json"), nodes);
        if (variant is not ("missing" or "crash-unbound")) WritePlan(run, "accounting", nodes);
        var defs = new StringBuilder();
        var results = new StringBuilder();
        foreach (var n in nodes)
        {
            if (variant == "short" && Str(n, "Uid") == "second") continue;
            defs.Append("<UnitTest id=\"").Append(Str(n, "Uid")).Append("\"><TestMethod className=\"").Append(Str(n, "ClassName")).Append("\" name=\"Run\" /></UnitTest>");
            results.Append("<UnitTestResult testId=\"").Append(Str(n, "Uid")).Append("\" outcome=\"Passed\" />");
        }
        if (variant is not ("missing" or "missing-bound")) WriteTrx(run, defs, results);
        var export = PriorFailureHistory.Export(run, Path.Combine(root, "accounting-" + variant + ".json"));
        var history = PriorFailureHistory.Read(Str(export, "Path"), Str(export, "Digest"), "");
        var suite = OneChunkSuite(nodes);
        var full = PriorFailureHistory.Resolve(history, suite, "Full", null, null);
        if (variant == "missing-bound")
        {
            var partial = PriorFailureHistory.Resolve(history, suite, "PriorFailureSweep", null, NightlyPlanCases.Policy());
            return True(history, "membershipComplete") && Items(full, "members").Length == 2 && Items(full, "unresolved").Length == 0
                && Items(partial, "members").Length == 2 && Items(history, "fallbackChunks").Length == 1;
        }
        var reason = variant switch
        {
            "crash" or "crash-unbound" or "detail" => "summary-detail", "exit" => "summary-nonzero-exit", "result" => "summary-failed-result",
            "short" => "short-trx", "missing" => "missing-trx", _ => "",
        };
        var classNames = nodes.Select(n => Str(n, "ClassName")).ToArray();
        var wholeChunk = variant is "missing" or "crash-unbound"
            || (Items(full, "members").Length == 2 && Items(full, "unresolved").Count(u => classNames.Contains(Str(u, "className"), StringComparer.Ordinal)) == 2);
        return wholeChunk && Items(full, "unresolved").Length > 0 && !True(history, "membershipComplete")
            && Items(history, "fallbackChunks").Count(f => Str(f, "reason").Split(',').Contains(reason, StringComparer.Ordinal)) == 1
            && Refuses(() => PriorFailureHistory.Resolve(history, suite, "PriorFailureSweep", null, NightlyPlanCases.Policy()), "prior-failure-sweep-unresolved");
    }

    /// <summary>timing/groups-planned-antiphon.json binding every node to chunk 'all'.</summary>
    public static void WritePlan(string run, string runId, object[] nodes) =>
        WriteJson(Path.Combine(run, "timing", "groups-planned-antiphon.json"), new OrderedDictionary
        {
            { "version", 1 }, { "suite", "antiphon" }, { "runId", runId }, { "attempt", "1" }, { "sourceDigest", new string('a', 40) }, { "policyDigest", new string('b', 64) },
            { "assemblyDigest", "fixture-assembly" },
            { "groups", new object[] { new OrderedDictionary { { "chunk", "all" }, { "classes", nodes.Select(n => (object)Str(n, "ClassName")).ToArray() },
                { "plannedTestIds", nodes.Select(n => (object)Str(n, "Uid")).ToArray() } } } },
        });

    /// <summary>antiphon-all.trx in the TeamTest namespace.</summary>
    public static void WriteTrx(string run, StringBuilder defs, StringBuilder results) =>
        File.WriteAllText(Path.Combine(run, "antiphon-all.trx"),
            "<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"><TestDefinitions>" + defs + "</TestDefinitions><Results>" + results + "</Results></TestRun>");
}
