using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using static Antiphon.Tests.Scripts.Nightly.FailedFirstArmCases;
using H = Antiphon.Tests.Scripts.Nightly.FailedFirstHistoryCases;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S14: the actual-loop fixtures of scripts/test-nightly-failed-first.ps1. Invoke-FFActualLoop runs the nightly
/// tests run (NightlyTests.Run since W2-nightly S17; the entry nightly-tests.ps1 is deleted) in this process on seams whose
/// StartProcess stands in for the test hosts (LoopSeam: it writes each chunk's TRX and diagnostic from the planned ids and
/// records every launch, discovery, build and active identity); the loop fixtures and the loop history are here. Unit 62 of
/// the 2026-11-22 remaining-ps1 plan moved the run in process (it was a pwsh child on a seams file, through
/// scripts/lib/c487-nightly-tests.ps1). The seams read the active identity and the planned cost through the compiled
/// heartbeat (QualificationHeartbeat, W2-nightly S10).
/// </summary>
internal static class FailedFirstLoopCases
{
    // ---- fixtures -----------------------------------------------------------------------------------------------

    /// <summary>New-FFLoopFixture: the global replay (Full) or the small fixture, with the history of each suite's failing class.</summary>
    public sealed record LoopFixture(object[] Suites, PlainObject History, bool Replay);

    public static LoopFixture Loop(bool full = false)
    {
        if (full)
        {
            var global = FailedFirstDispatchCases.Global();
            return new LoopFixture(global.Suites, global.History, true);
        }
        var small = Small();
        var members = small.Suites.Select(s => (object)new PlainObject(("suite", H.Str(s, "id")), ("className", PsValue.Str(H.Items(H.Items(s, "chunks")[^1], "classes")[0])),
            ("reasons", new object[] { "Failed" }), ("observations", Array.Empty<object>()))).ToArray();
        var history = new PlainObject(("format", "antiphon-nightly-prior-failures-v1"), ("sourceRunId", "old"), ("sourceAttempt", "1"), ("sourceSha", new string('a', 40)),
            ("profile", "rc"), ("platform", "windows"), ("policyHash", new string('b', 64)), ("createdAtUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)),
            ("complete", true), ("evidence", Array.Empty<object>()), ("members", members), ("fallbackChunks", Array.Empty<object>()), ("membershipComplete", true),
            ("unresolved", Array.Empty<object>()));
        return new LoopFixture(small.Suites, history, false);
    }

    public sealed record LoopHistory(string Path, string Digest, object History);

    /// <summary>New-FFLoopHistory: a closed run whose TRX fails the first node of each history member's class, exported.</summary>
    public static LoopHistory History(string root, LoopFixture fx)
    {
        var dir = System.IO.Path.Combine(root, "closed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var rows = new List<object>();
        foreach (var suite in fx.Suites)
        {
            var sid = H.Str(suite, "id");
            var members = H.Items(fx.History, "members").Where(m => H.Str(m, "suite") == sid).ToArray();
            var trx = System.IO.Path.Combine(dir, sid + "-old.trx");
            var nodes = H.Items(suite, "nodes");
            var failures = members.Select(member =>
            {
                var n = nodes.FirstOrDefault(x => NightlyCensus.BareClassIdentity(H.Str(x, "ClassName")) == H.Str(member, "className"));
                return new NightlyTrxRow(H.Str(n, "Uid"), H.Str(n, "ClassName"), H.Str(n, "Method"), "Failed");
            }).ToArray();
            NightlyFixture.WriteTrx(trx, failures);
            rows.Add(new OrderedDictionary { { "id", sid }, { "chunk", "old" }, { "trx", trx }, { "exitCode", 2 }, { "result", "FAIL" }, { "timedOut", false } });
        }
        H.WriteJson(System.IO.Path.Combine(dir, "summary.json"), new OrderedDictionary
        {
            { "runId", "old" }, { "attempt", "1" }, { "sha", new string('a', 40) }, { "profile", "rc" }, { "platform", "windows" }, { "policyHash", new string('b', 64) },
            { "completedAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) }, { "outcome", "TESTS" }, { "suites", rows.ToArray() },
            { "selectedSuites", fx.Suites.Select(s => (object)H.Str(s, "id")).ToArray() },
        });
        var path = System.IO.Path.Combine(root, "history-" + Guid.NewGuid().ToString("N") + ".json");
        var export = PriorFailureHistory.Export(dir, path);
        return new LoopHistory(path, H.Str(export, "Digest"), PsValue.Prop(export, "History")!);
    }

    // ---- Invoke-FFActualLoop ------------------------------------------------------------------------------------

    public sealed record LoopLaunch(string Suite, string Chunk, string Key, string Host, string Launcher, string[] Nodes, string Filter,
        double? LegacyCost, double? QualifiedCost, double? PlannedCost, bool ProvenBinding, JsonElement? Identity, bool Settled);

    public sealed class LoopTrace
    {
        public List<LoopLaunch> Launches { get; } = new();
        public List<string> Discovery { get; } = new();
        public int Builds { get; set; }
        public int Max { get; set; }
        public bool SettledBeforeReturn => Launches.Any(l => l.Settled);
        public string[] Keys => Launches.Select(l => l.Key).ToArray();
    }

    public sealed class LoopRun
    {
        public required string Root { get; init; }
        public int ExitCode { get; init; }
        public bool TestsPassed { get; init; }
        public bool CoverageComplete { get; init; }
        public JsonElement Summary { get; init; }
        /// <summary>summary.json as the run's readers see it (ConvertFrom-Json values).</summary>
        public object? SummaryValue { get; init; }
        public JsonElement Schedule { get; init; }
        public JsonElement Projection { get; init; }
        public required LoopTrace Trace { get; init; }
        public required object[] Prepared { get; init; }
        public required string Output { get; init; }
    }

    /// <summary>
    /// W2-nightly S14: the actual-loop seams of the failed-first cases (Invoke-FFActualLoop), in this process since unit 62 of
    /// the 2026-11-22 remaining-ps1 plan (they were a seams file a pwsh child ran through scripts/lib/c487-nightly-tests.ps1).
    /// Its StartProcess stands in for the test hosts: from the planned ids it writes each chunk's TRX and diagnostic, and it
    /// records every launch, discovery, build and active identity in the trace. The seams read the active identity and the
    /// planned cost through the compiled heartbeat (QualificationHeartbeat, W2-nightly S10).
    /// </summary>
    private sealed class LoopSeam(LoopFixture fx, string variant, string timing)
    {
        private sealed record Node(string Uid, string ClassName, string Namespace, string Type, string Method);

        private sealed record Suite(string Id, Node[] Nodes, Dictionary<string, Node> ByUid);

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> ToolchainText = new(StringComparer.Ordinal);
        private readonly object _gate = new();
        private readonly Suite[] _suites = fx.Suites.Select(s =>
        {
            var nodes = H.Items(s, "nodes").Select(n => new Node(H.Str(n, "Uid"), H.Str(n, "ClassName"), H.Str(n, "Namespace"), H.Str(n, "Type"), H.Str(n, "Method"))).ToArray();
            var byUid = new Dictionary<string, Node>(StringComparer.Ordinal);
            foreach (var n in nodes) byUid.Add(n.Uid, n);
            return new Suite(H.Str(s, "id"), nodes, byUid);
        }).ToArray();
        private readonly Dictionary<string, Dictionary<string, JsonElement>> _plans = new(StringComparer.Ordinal);
        private int _active;

        public LoopTrace Trace { get; } = new();

        /// <summary>The real probes (Get-ToolchainFloorVersionText), run once per process.</summary>
        public static string ToolchainVersions(string tool) => ToolchainText.GetOrAdd(tool, t => new NightlyHostProbe().ToolVersion(t));

        public object StartProcess(NightlyLaunch launch)
        {
            var args = launch.Arguments ?? [];
            string Variable(string name) => launch.Environment is { } environment && environment.TryGetValue(name, out var value) ? value ?? "" : "";
            var sid = Variable("ANTIPHON_TEST_PHASE_SUITE");
            var cid = Variable("ANTIPHON_TEST_PHASE_GROUP_ID");
            var diag = SeamedNightlyRun.Argument(args, "--diagnostic-output-directory");
            if (args.Contains("--list-tests", StringComparer.OrdinalIgnoreCase))
            {
                if (variant == "identity-io") Directory.CreateDirectory(System.IO.Path.Combine(timing, "active-chunk.json"));
                var leaf = System.IO.Path.GetFileName(diag);
                sid = Regex.Replace(Regex.Replace(leaf, "-profile-discovery$", "", RegexOptions.IgnoreCase), "-.*-discovery$", "", RegexOptions.IgnoreCase);
                foreach (var s in _suites) if (leaf.StartsWith(s.Id + "-", StringComparison.Ordinal)) { sid = s.Id; break; }
                lock (_gate) Trace.Discovery.Add(sid);
                WriteDiagnostic(diag, _suites.FirstOrDefault(s => s.Id == sid)?.Nodes ?? [], "Discovered");
            }
            else if (sid.Length > 0)
            {
                var active = QualificationHeartbeat.ReadActiveChunkIdentity(timing);
                var depth = Interlocked.Increment(ref _active);
                var suite = _suites.First(s => s.Id == sid);
                var group = PlanGroup(System.IO.Path.Combine(timing, "groups-planned-" + sid + ".json"), cid);
                var classes = group is { } g ? A(g, "classes").Select(c => c.GetString() ?? "").ToArray() : [];
                var nodes = group is { } p
                    ? A(p, "plannedTestIds").Select(uid => suite.ByUid.TryGetValue(uid.GetString() ?? "", out var n) ? n : null).OfType<Node>().ToArray() : [];
                if (nodes.Length == 0) nodes = suite.Nodes.Where(n => classes.Contains(n.ClassName, StringComparer.Ordinal)).ToArray();
                double? legacyCost = null, qualifiedCost = null;
                if (!fx.Replay)
                {
                    var lookup = System.IO.Path.Combine(timing, "groups-planned.json");
                    legacyCost = QualificationHeartbeat.PlannedCost(lookup, cid).Seconds;
                    qualifiedCost = QualificationHeartbeat.PlannedCost(lookup, cid, sid, "actual-run", "1").Seconds;
                }
                var proven = active is not null && Regex.IsMatch(active.SourceDigest ?? "", "^[a-f0-9]{40}$") && Regex.IsMatch(active.PolicyDigest ?? "", "^[a-f0-9]{64}$")
                    && Regex.IsMatch(active.CostFileDigest ?? "", "^[a-f0-9]{64}$") && Regex.IsMatch(active.AssemblyDigest ?? "", "^[a-f0-9]{64}$");
                // With UseAppHost=false the host is dotnet exec <assembly>: retain the assembly as well as its launcher.
                var testHost = args.Count > 1 && args[0] == "exec" ? args[1] : launch.FileName;
                var special = sid == "antiphon" && classes.Any(c => c.Contains("Priority", StringComparison.Ordinal));
                var outcome = "Passed";
                var code = 0;
                var timeout = false;
                if (special && variant == "failure") { outcome = "Failed"; code = 2; }
                if (special && variant == "timeout") { timeout = true; code = 1; }
                WriteNodeTrx(System.IO.Path.Combine(SeamedNightlyRun.Argument(args, "--results-directory"), SeamedNightlyRun.Argument(args, "--report-trx-filename")), nodes, outcome);
                WriteDiagnostic(diag, nodes, outcome);
                var settled = File.Exists(System.IO.Path.Combine(timing, "groups-executed.json"));
                // The identity under the active-chunk.json names.
                JsonElement? identity = active is null ? null : JsonSerializer.SerializeToElement(new OrderedDictionary
                {
                    { "format", active.Format }, { "state", active.State }, { "generation", active.Generation }, { "runId", active.RunId }, { "attempt", active.Attempt },
                    { "suite", active.Suite }, { "chunk", active.Chunk }, { "planFile", active.PlanFile }, { "planDigest", active.PlanDigest },
                    { "sourceDigest", active.SourceDigest }, { "policyDigest", active.PolicyDigest }, { "costFileDigest", active.CostFileDigest },
                    { "assemblyDigest", active.AssemblyDigest }, { "identityDigest", active.IdentityDigest },
                });
                double? planned = group is { } q && q.TryGetProperty("plannedSeconds", out var seconds) && seconds.ValueKind == JsonValueKind.Number ? seconds.GetDouble() : null;
                lock (_gate)
                {
                    Trace.Launches.Add(new LoopLaunch(sid, cid, sid + "/" + cid, testHost, launch.FileName, nodes.Select(n => n.Uid).ToArray(),
                        SeamedNightlyRun.Argument(args, "--treenode-filter"), legacyCost, qualifiedCost, planned, proven, identity, settled));
                    Trace.Max = Math.Max(Trace.Max, depth);
                }
                Interlocked.Decrement(ref _active);
                return Record(code, timeout);
            }
            else if (args.Contains("Antiphon.sln", StringComparer.OrdinalIgnoreCase)) lock (_gate) Trace.Builds++;
            return Record(0, false);
        }

        // The seams' record: no pid, the children exited and the job observed empty.
        private static PlainObject Record(int code, bool timeout) =>
            new(("ExitCode", code), ("TimedOut", timeout), ("ChildrenExited", true), ("CleanupComplete", true), ("JobEmptyObserved", true), ("LogText", ""));

        // One parse per written plan, and one chunk index per parse: the first group of each chunk id.
        private JsonElement? PlanGroup(string planPath, string chunk)
        {
            var info = new FileInfo(planPath);
            var key = info.FullName + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
            Dictionary<string, JsonElement>? byChunk;
            lock (_gate)
            {
                if (!_plans.TryGetValue(key, out byChunk))
                {
                    byChunk = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                    foreach (var g in A(ReadJson(planPath), "groups"))
                        if (g.TryGetProperty("chunk", out var id) && id.ValueKind == JsonValueKind.String) byChunk.TryAdd(id.GetString()!, g);
                    _plans[key] = byChunk;
                }
            }
            return byChunk.TryGetValue(chunk, out var group) ? group : null;
        }

        // Write-FFDiagnostic: the diagnostic directory's fixture.diag, one node update per node.
        private static void WriteDiagnostic(string directory, Node[] nodes, string state)
        {
            Directory.CreateDirectory(directory);
            var text = new StringBuilder("Version: '1.44.0.0'\nVersion: 2.2.2\n");
            foreach (var n in nodes)
                text.Append("TestNodeUpdateMessage { TestNode = TestNode { Uid = TestNodeUid { Value = ").Append(n.Uid).Append(" }, DisplayName = fixture, Properties = [ TestMethodIdentifierProperty { AssemblyFullName = Fixture, Namespace = ")
                    .Append(n.Namespace).Append(", TypeName = ").Append(n.Type).Append(", MethodName = ").Append(n.Method)
                    .Append(", MethodArity = 0, ParameterTypeFullNames = [], ReturnTypeFullName = System.Void }, ").Append(state).Append("TestNodeStateProperty { Explanation =  } ] } }\n");
            File.WriteAllText(System.IO.Path.Combine(directory, "fixture.diag"), text.ToString());
        }

        // Write-FFNodeTrx: a TRX of the nodes, each with the outcome.
        private static void WriteNodeTrx(string path, Node[] nodes, string outcome)
        {
            var definitions = new StringBuilder("<TestDefinitions>");
            var results = new StringBuilder("<Results>");
            foreach (var n in nodes)
            {
                definitions.Append("<UnitTest id=\"").Append(n.Uid).Append("\" name=\"").Append(n.ClassName).Append('.').Append(n.Method).Append("\"><TestMethod className=\"")
                    .Append(n.ClassName).Append("\" name=\"").Append(n.Method).Append("\" /></UnitTest>");
                results.Append("<UnitTestResult testId=\"").Append(n.Uid).Append("\" testName=\"").Append(n.ClassName).Append('.').Append(n.Method).Append("\" outcome=\"")
                    .Append(outcome).Append("\" />");
            }
            definitions.Append("</TestDefinitions>");
            results.Append("</Results>");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "<?xml version=\"1.0\" encoding=\"utf-8\"?><TestRun>" + definitions + results + "</TestRun>");
        }
    }

    /// <summary>
    /// Invoke-FFActualLoop: the fixture's suites as the policy's rc required suites (their chunks as the declared
    /// chunks; the replay keeps its Windows exclusions, the small fixture none), a Fixture.Never exclusion per suite
    /// unless noExclusions, run as the nightly tests run (NightlyTests.Run) on the loop seams; history, when given, is
    /// its prior-failures file.
    /// </summary>
    public static Task<LoopRun> Run(string root, LoopFixture fx, string name, string? historyPath, string variant = "pass", bool noExclusions = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dir = System.IO.Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        var timing = System.IO.Path.Combine(dir, "timing");
        var policy = JsonNode.Parse(File.ReadAllText(NightlyPolicy.PolicyPath(H.Repo)))!.AsObject();
        var rc = policy["profiles"]!["rc"]!.AsObject();
        rc["requiredSuites"] = new JsonArray(fx.Suites.Select(s => (JsonNode?)JsonValue.Create(H.Str(s, "id"))).ToArray());
        var chunks = new JsonObject();
        foreach (var suite in fx.Suites)
        {
            var rows = fx.Replay
                ? new JsonArray(new JsonObject { ["id"] = "all", ["classes"] = new JsonArray() })
                : new JsonArray(H.Items(suite, "chunks").Select(c => (JsonNode?)new JsonObject
                {
                    ["id"] = H.Str(c, "id"),
                    ["classes"] = new JsonArray(H.Items(c, "classes").Select(k => (JsonNode?)JsonValue.Create(PsValue.Str(k))).ToArray()),
                    ["seconds"] = JsonValue.Create(PsValue.Double(PsValue.Prop(c, "seconds"))),
                }).ToArray());
            chunks[H.Str(suite, "id")] = rows;
        }
        policy.Remove("chunks");
        policy["chunks"] = chunks;
        // The replay keeps the actual Windows exclusions; the small interleaving witness has none in its first suite.
        if (!fx.Replay) rc["exclusions"] = new JsonArray();
        if (!noExclusions)
            foreach (var suite in fx.Suites)
                rc["exclusions"]!.AsArray().Add(new JsonObject { ["suite"] = H.Str(suite, "id"), ["class"] = "Fixture.Never", ["reason"] = "fixture", ["owner"] = "CARD-1514" });
        policy["policyHash"] = NightlyPolicy.HashText(policy.ToJsonString());
        var policyFile = System.IO.Path.Combine(dir, "policy.json");
        File.WriteAllText(policyFile, policy.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        var seam = new LoopSeam(fx, variant, timing);
        var run = new SeamedNightlyRun
        {
            StartProcess = seam.StartProcess, HostPlatform = () => "windows", ToolchainVersions = LoopSeam.ToolchainVersions,
            E2EPrerequisites = (_, _, _, _) => new PlainObject(("Ok", true), ("Rows", Array.Empty<object>())),
        };
        var options = new NightlyTestsOptions
        {
            RepoRoot = H.Repo, LogRoot = dir, TimingRoot = timing, RunId = "actual-run", Attempt = 1, Sha = new string('a', 40), GitRef = "origin/rc/2026-10-27",
            Profile = "rc", PolicyPath = policyFile, AllowSharedTree = true,
        };
        if (historyPath != null)
        {
            options.PriorFailuresPath = historyPath;
            options.PriorFailuresDigest = File.Exists(historyPath) ? PriorFailureHistory.Digest(historyPath) : "";
        }
        var (result, _) = run.Run(options);
        var output = run.Output.ToString();
        if (result is null) throw new InvalidOperationException("the nightly tests run wrote no result (" + name + "):\n" + output);
        var trace = seam.Trace;
        var summaryPath = System.IO.Path.Combine(dir, "summary.json");
        var prepared = fx.Suites.Select(s =>
        {
            var plan = ReadJson(System.IO.Path.Combine(timing, "groups-planned-" + H.Str(s, "id") + ".json"));
            return (object)FailedFirstDispatchCases.Suite(H.Str(s, "id"), H.Items(s, "nodes"),
                A(plan, "groups").Select(g => (object)new PlainObject(("id", S(g, "chunk")), ("classes", A(g, "classes").Select(c => (object)(c.GetString() ?? "")).ToArray()))).ToArray(), []);
        }).ToArray();
        return Task.FromResult(new LoopRun
        {
            Root = dir, ExitCode = result.ExitCode, TestsPassed = result.TestsPassed, CoverageComplete = result.CoverageComplete,
            Summary = ReadJson(summaryPath), SummaryValue = NightlyJson.ReadFile(summaryPath, false), Schedule = ReadJson(System.IO.Path.Combine(dir, "dispatch-schedule.json")),
            Projection = ReadJson(System.IO.Path.Combine(timing, "groups-executed.json")), Trace = trace, Prepared = prepared, Output = output,
        });
    }

    /// <summary>The "suite/chunk" keys of a schedule document's entries.</summary>
    public static string[] Entries(JsonElement schedule) => A(schedule, "entries").Select(e => S(e, "suite") + "/" + S(e, "chunk")).ToArray();

    /// <summary>Copy-Item -Recurse of a directory.</summary>
    public static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source)) CopyTree(directory, Path.Combine(target, Path.GetFileName(directory)));
    }
}
