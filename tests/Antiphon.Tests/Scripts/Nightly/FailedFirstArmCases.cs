using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using H = Antiphon.Tests.Scripts.Nightly.FailedFirstHistoryCases;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S14: the conservation fixtures of scripts/test-nightly-failed-first.ps1, in-process: Invoke-FFPreparedArm
/// (prepared suites through NightlyDispatch.RunPrepared over the real suite runner, a fake launcher writing each chunk's
/// TRX and diagnostic, one failure variant keyed to a chunk), New-FFSmallFixture, the phase fixture, the artifact
/// schemas, the planned golden, the authority conservation (a release authority frozen by the production
/// ReleaseAuthority.Freeze and Plan in a real git checkout) and the retained archive of a case's fixture (CARD-1590).
/// </summary>
internal static class FailedFirstArmCases
{
    private const string RunId = "conservation-run";
    private static readonly string Sha = new('a', 40);

    // ---- fixtures -----------------------------------------------------------------------------------------------

    /// <summary>The suites an arm prepares, the priority members, and the discovery indexes and plans its arms share.</summary>
    public sealed class ArmFixture
    {
        public required object[] Suites { get; init; }
        public required object[] Members { get; init; }
        /// <summary>The 19,012-node global fixture: its arms write no phase summary.</summary>
        public bool Replay { get; init; }
        public object[] Anchors { get; init; } = [];
        public Dictionary<string, NightlyDiscoveryIndex> Index { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> PlannedText { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>New-FFSmallFixture: antiphon and session-runner, each an Ordinary and a Priority chunk of one class.</summary>
    public static ArmFixture Small()
    {
        var policy = NightlyPlanCases.Policy();
        var suites = new List<object>();
        foreach (var sid in new[] { "antiphon", "session-runner" })
        {
            object[] nodes = [FailedFirstDispatchCases.Node("Fixture." + sid.Replace("-", "") + ".Ordinary"), FailedFirstDispatchCases.Node("Fixture." + sid.Replace("-", "") + ".Priority")];
            var chunks = nodes.Select(n =>
            {
                var c = (PlainObject)NightlyTestsPlan.ProfileLaunchPlan([n], NightlyPlanCases.AllChunk(), Array.Empty<object>(), sid, policy.Object, NightlyPlanCases.Costs())[0];
                c["id"] = H.Str(n, "Type");
                return (object)c;
            }).ToArray();
            suites.Add(FailedFirstDispatchCases.Suite(sid, nodes, chunks, []));
        }
        return new ArmFixture
        {
            Suites = suites.ToArray(),
            Members = [new PlainObject(("suite", "antiphon"), ("chunks", new object[] { "Priority" })), new PlainObject(("suite", "session-runner"), ("chunks", new object[] { "Priority" }))],
        };
    }

    /// <summary>New-FFGlobalFixture's suites, members and its four anchors (canonical ordinals 23, 99, 101 and 118).</summary>
    public static ArmFixture Global()
    {
        var fx = FailedFirstDispatchCases.Global();
        var chunks = (object[])fx.Replay["chunks"]!;
        var anchors = new[] { 23, 99, 101, 118 }.Select(ordinal =>
        {
            var anchor = chunks[ordinal - 1];
            return (object)new PlainObject(("ordinal", ordinal), ("chunk", H.Str(anchor, "id")), ("className", NightlyCensus.BareClassIdentity(PsValue.Str(H.Items(anchor, "classes")[0]))));
        }).ToArray();
        return new ArmFixture { Suites = fx.Suites, Members = H.Items(fx.Resolved, "members"), Replay = true, Anchors = anchors };
    }

    // ---- the prepared arm ---------------------------------------------------------------------------------------

    public sealed record Launch(string Suite, string Chunk, string Key, string Filter, int Watchdog, string Host);

    /// <summary>Invoke-FFPreparedArm's answer.</summary>
    public sealed class Arm
    {
        public required string Root { get; init; }
        public required string TimingRoot { get; init; }
        public required object Schedule { get; init; }
        public required List<Launch> Launches { get; init; }
        public required Dictionary<string, string> Plans { get; init; }
        public bool PlansStable { get; init; }
        public JsonElement Projection { get; init; }
        public required object[] Rows { get; init; }
        public required string[] Reasons { get; init; }
        public bool Coverage { get; init; }
        public bool Passed { get; init; }
        public required string Error { get; init; }
        public required NightlyPreparedSuite[] States { get; init; }
        public required TimingReport Timing { get; init; }
        public required TestPhaseJournalContext Context { get; init; }
        public int NativeMax { get; init; }
        public required string Outcome { get; init; }
        public bool OverallFailed { get; init; }
        public string[] Keys => Launches.Select(l => l.Key).ToArray();
    }

    static string Arg(NightlyLaunch launch, string flag)
    {
        var args = launch.Arguments ?? [];
        for (var i = 0; i + 1 < args.Count; i++) if (args[i] == flag) return args[i + 1];
        return "";
    }

    static string Env(NightlyLaunch launch, string name) =>
        launch.Environment != null && launch.Environment.TryGetValue(name, out var value) ? value ?? "" : "";

    /// <summary>
    /// Invoke-FFPreparedArm: the fixture's suites prepared (discovery document, census, the once-published plan) and run
    /// through the compiled prepared schedule; the keyed chunk (the first suite's last chunk) takes the variant: failure,
    /// skip, unknown, missing, timeout, publication (a commit fault), teardown (no owned-child exit), phase, phase-stale.
    /// </summary>
    public static Arm Prepared(NightlyFixture fixture, ArmFixture fx, string name, object[]? members = null, string variant = "pass", FakeGit? git = null)
    {
        members ??= [];
        var logRoot = fixture.PathOf(name);
        Directory.CreateDirectory(logRoot);
        var timingRoot = Path.Combine(logRoot, "timing");
        var policy = NightlyPlanCases.Policy();
        // The conservation fixture compares observations under an unexhausted reader clock (an unstarted watch);
        // production keeps its 200 ms deadline.
        var scope = new TestPhasePublicationScope(timingRoot, "Core", "7.5.0", null, null) { TrxReadWatch = new Stopwatch() };
        var launches = new List<Launch>();
        var armChunks = new Dictionary<string, object[]>(StringComparer.Ordinal);
        var failureKey = "antiphon/" + H.Str(H.Items(fx.Suites[0], "chunks")[^1], "id");
        var launcher = new RecordingLauncher(launch =>
        {
            var suiteKey = Env(launch, "ANTIPHON_TEST_PHASE_SUITE");
            var chunkKey = Env(launch, "ANTIPHON_TEST_PHASE_GROUP_ID");
            var key = suiteKey + "/" + chunkKey;
            if (!armChunks.TryGetValue(key, out var nodes)) throw new InvalidOperationException("unkeyed-launch:" + key);
            launches.Add(new Launch(suiteKey, chunkKey, key, Arg(launch, "--treenode-filter"), launch.TimeoutMilliseconds, launch.FileName));
            var rows = new List<NightlyTrxRow>();
            var terminals = new List<object>();
            var exitCode = 0;
            var timedOut = false;
            var special = key == failureKey;
            foreach (var n in nodes)
            {
                var state = "Passed";
                if (special && variant == "failure") { state = "Failed"; exitCode = 2; }
                if (special && variant == "skip") state = "Skipped";
                rows.Add(new NightlyTrxRow(H.Str(n, "Uid"), H.Str(n, "ClassName"), H.Str(n, "Method"), state));
                terminals.Add(new PlainObject(("Uid", H.Str(n, "Uid")), ("Namespace", H.Str(n, "Namespace")), ("Type", H.Str(n, "Type")), ("Method", H.Str(n, "Method")), ("Outcome", state)));
            }
            if (special && variant == "unknown")
                terminals.Add(new PlainObject(("Uid", "foreign"), ("Namespace", H.Str(nodes[0], "Namespace")), ("Type", H.Str(nodes[0], "Type")), ("Method", H.Str(nodes[0], "Method")), ("Outcome", "Passed")));
            if (variant is "phase" or "phase-stale")
                PhaseFixture(Env(launch, "ANTIPHON_TEST_PHASE_ROOT"), suiteKey, chunkKey, nodes, special && variant == "phase-stale");
            var trx = Path.Combine(Arg(launch, "--results-directory"), Arg(launch, "--report-trx-filename"));
            if (!(special && variant == "missing")) NightlyFixture.WriteTrx(trx, rows);
            FailedFirstDispatchCases.Diagnostic(Arg(launch, "--diagnostic-output-directory"), terminals, "Passed");
            if (special && variant == "timeout") { timedOut = true; exitCode = 1; }
            if (special && variant == "publication") scope.CommitFault = new PlainObject(("Stage", "Rename"), ("Remaining", 2));
            var children = !(special && variant == "teardown");
            return new NightlyLaunchResult { ExitCode = exitCode, TimedOut = timedOut, ChildrenExited = children, CleanupComplete = children, Pid = 1, LogText = "" };
        });
        var host = fixture.Host(git: git, launcher: launcher, clock: TimeProvider.System);
        var states = new List<NightlyPreparedSuite>();
        var planned = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in fx.Suites)
        {
            var sid = H.Str(s, "id");
            var nodes = H.Items(s, "nodes");
            var chunks = H.Items(s, "chunks");
            var exclusions = H.Items(s, "exclusions");
            var discPath = Path.Combine(logRoot, sid + "-profile.discovery.json");
            var doc = NightlyDiagnosticLog.NewDiscoveryDocument(nodes, "fixture");
            NightlyDiagnosticLog.WriteDiscoveryDocument(discPath, doc);
            if (!fx.Index.TryGetValue(sid, out var index)) fx.Index[sid] = index = new NightlyDiscoveryIndex();
            var entry = NightlyDiscoveryIndex.EntryOf(index, discPath);
            var census = PsValue.Prop(NightlyDiscoveryIndex.View(entry, exclusions, true), "Census");
            var variables = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["suiteId"] = sid, ["suite"] = new PlainObject(("assembly", sid)), ["label"] = sid, ["mode"] = "unattended", ["isNative"] = true, ["chunks"] = chunks,
                ["profileExclusions"] = exclusions, ["suiteHost"] = new PlainObject(("FilePath", "fixture-" + sid), ("Prefix", Array.Empty<object>()), ("AssemblyPath", "fixture")),
                ["suiteDiscoveryNodes"] = nodes, ["profileDiscovery"] = doc, ["profileDiscoveryPath"] = discPath, ["suiteDiscoveryIndex"] = index, ["suiteCensus"] = census,
                ["suiteDiscoverySeconds"] = 0.25, ["suiteTerminalNodes"] = Array.Empty<object>(), ["chunkIndex"] = 0, ["phaseAssembly"] = sid, ["phaseAssemblyDigest"] = "fixture",
                ["phaseCostDigest"] = "fixture",
            };
            states.Add(new NightlyPreparedSuite(NightlySuiteState.FromVariables(variables), chunks));
            var planFile = "groups-planned-" + sid + ".json";
            if (!fx.PlannedText.TryGetValue(sid, out var shared))
            {
                WritePlanned(new TestPhasePlannedRequest
                {
                    LogRoot = logRoot, RunId = RunId, Attempt = "1", Suite = sid, SourceDigest = Sha, AssemblyDigest = "fixture", PolicyDigest = policy.Hash, CostFileDigest = "fixture",
                    AssemblyId = sid, Groups = chunks, DiscoveryNodes = nodes, ProfileExclusions = exclusions, DiscoveryIndex = index, DiscoveryPath = discPath,
                }, scope);
                fx.PlannedText[sid] = File.ReadAllText(Path.Combine(timingRoot, planFile));
            }
            else
            {
                // Both arms consume the same once-published immutable plans; only mutable execution/journal/sidecar
                // state has a fresh root.
                TestPhaseFiles.WriteTextAtomic(Path.Combine(timingRoot, planFile), shared);
                TestPhaseFiles.WriteTextAtomic(Path.Combine(timingRoot, "groups-planned.json"), shared);
                using var sharedPlan = JsonDocument.Parse(shared);
                var filters = sharedPlan.RootElement.GetProperty("groups").EnumerateArray().Select(g => (object)(g.GetProperty("filter").GetString() ?? "")).ToArray();
                TestPhaseManifests.UpdatePlannedIndex(timingRoot, sid, planFile, RunId, "1", sid, Sha, policy.Hash, "fixture", "fixture", filters);
            }
            planned[sid] = File.ReadAllText(Path.Combine(timingRoot, planFile));
            var byUid = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in nodes) byUid[H.Str(n, "Uid")] = n;
            using var plan = JsonDocument.Parse(planned[sid]);
            foreach (var g in plan.RootElement.GetProperty("groups").EnumerateArray())
                armChunks[sid + "/" + g.GetProperty("chunk").GetString()] = g.GetProperty("plannedTestIds").EnumerateArray().Select(u => byUid.GetValueOrDefault(u.GetString() ?? "")!).ToArray();
        }
        var context = TestPhasePublication.Open(logRoot, timingRoot, RunId, "1", "", Sha, RunId, false, false, scope);
        if (context.Refused) throw new InvalidOperationException("context-refused:" + context.Reason);
        var schedule = NightlyDispatch.Schedule(states.Cast<object>().ToArray(), members, new string('d', 64), null, true, RunId, "1", null, "", DateTime.UtcNow, "");
        NightlyDispatch.WriteSchedule(schedule, logRoot);
        var state = new NightlyRunState
        {
            Host = host, RepoRoot = H.Repo, LogRoot = logRoot, TimingRoot = timingRoot, QualificationTimingRoot = timingRoot, InvocationId = RunId, Attempt = "1",
            Profile = "rc", Sha = Sha, GitRef = "origin/master", Policy = policy.Object, PolicyHash = policy.Hash, Platform = "windows",
            StartedAt = DateTime.UtcNow.AddSeconds(-1), BuildSeconds = 1.25, ProfileAware = true, TestsOutcome = "green",
            PublishObservation = seen => TestPhaseManifests.WriteExecuted(seen.Verdict, seen.LogRoot, seen.TrxPath, seen.Suite, seen.Chunk, seen.RunId, seen.Attempt,
                seen.HostWallSeconds, context, scope),
            PublicationScope = () => scope,
            Warning = _ => { },
        };
        var error = "";
        var coverage = true; var passed = true; var overallFailed = false;
        var reasons = new List<string>();
        try
        {
            try { NightlyDispatch.RunPrepared(state, new NightlySuiteRunner(state).Steps(), schedule); }
            catch (Exception e) { error = H.Message(e); }
            coverage = state.CoverageComplete; passed = state.TestsPassed; overallFailed = state.OverallFailed;
            reasons.AddRange(state.Reasons.Select(PsValue.Str));
            if (error.Length > 0) { coverage = false; overallFailed = true; reasons.Add(error); }
        }
        finally
        {
            var terminal = overallFailed || !passed ? "failed-run" : "success";
            TestPhasePublication.CompleteProjection(context, terminal, scope);
            TestPhasePublication.Close(context);
            scope.CommitFault = null;
        }
        if (!fx.Replay) TestPhaseRunSummary.Run(logRoot, 50, scope);
        var timing = QualificationTiming.Report(logRoot, timingRoot);
        JsonElement projection;
        using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(timingRoot, "groups-executed.json")))) projection = doc.RootElement.Clone();
        var stable = fx.Suites.All(s => File.ReadAllText(Path.Combine(timingRoot, "groups-planned-" + H.Str(s, "id") + ".json")) == planned[H.Str(s, "id")]);
        return new Arm
        {
            Root = logRoot, TimingRoot = timingRoot, Schedule = schedule, Launches = launches, Plans = planned, PlansStable = stable, Projection = projection,
            Rows = state.SuiteResults.ToArray(), Reasons = reasons.ToArray(), Coverage = coverage, Passed = passed, Error = error, States = states.ToArray(),
            Timing = timing, Context = context, NativeMax = state.NativeActiveMax, Outcome = state.TestsOutcome, OverallFailed = overallFailed,
        };
    }

    /// <summary>Write-FFPlannedManifest: the compiled planned manifest, priced with the production cost file and no table.</summary>
    public static string WritePlanned(TestPhasePlannedRequest request, TestPhasePublicationScope scope)
    {
        request.ClassCostPath = NightlyPlanCases.CostPath;
        request.ClassCostTable = null;
        return TestPhaseManifests.WritePlanned(request, scope);
    }

    /// <summary>Write-FFPhaseFixture: a phase journal (header, one complete record per node, the shared fixture on session-runner/Priority).</summary>
    public static void PhaseFixture(string directory, string suite, string chunk, object[] nodes, bool stale)
    {
        var records = new List<string>();
        var run = stale ? "stale-run" : RunId;
        records.Add(H.JsonText(new OrderedDictionary
        {
            { "recordKind", "header" }, { "version", 1 }, { "pid", 1 }, { "nonce", "fixture" }, { "frequencyHz", 10000000 }, { "epochUtc", "2026-10-23T00:00:00.0000000Z" },
            { "runId", run }, { "groupId", chunk }, { "attempt", "1" }, { "suite", suite }, { "assembly", suite }, { "sourceDigest", Sha },
        }));
        foreach (var n in nodes)
            records.Add(H.JsonText(new OrderedDictionary
            {
                { "recordKind", "complete" }, { "testId", H.Str(n, "Uid") }, { "className", H.Str(n, "ClassName") }, { "method", H.Str(n, "Method") }, { "args", Array.Empty<object>() },
                { "startOffsetMs", 0 }, { "endOffsetMs", 100 }, { "outcome", "Passed" }, { "phases", Array.Empty<object>() }, { "processes", Array.Empty<object>() },
                { "exclusive", new OrderedDictionary { { "db", 1 }, { "host", 2 }, { "process", 3 }, { "fs", 4 }, { "network", 5 }, { "fixture", 6 } } },
                { "overlapMs", 7 }, { "remainderMs", 8 }, { "childUnionMs", 0 }, { "droppedPhaseCount", 0 }, { "shared", false }, { "incomplete", false },
            }));
        if (suite == "session-runner" && chunk == "Priority")
            records.Add(H.JsonText(new OrderedDictionary
            {
                { "recordKind", "fixture" }, { "fixtureId", "shared-fixture" },
                { "exclusive", new OrderedDictionary { { "db", 11 }, { "host", 12 }, { "process", 13 }, { "fs", 14 }, { "network", 15 }, { "fixture", 16 } } },
            }));
        Directory.CreateDirectory(directory);
        File.WriteAllLines(Path.Combine(directory, "cases-" + suite + "-" + chunk + ".jsonl"), records, new UTF8Encoding(false));
    }

    // ---- JSON readings ------------------------------------------------------------------------------------------

    public static JsonElement ReadJson(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    public static string S(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        JsonValueKind.True => "True",
        JsonValueKind.False => "False",
        _ => v.GetRawText(),
    } : "";

    public static bool B(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.Number => v.GetDouble() != 0,
        JsonValueKind.String => (v.GetString() ?? "").Length > 0,
        JsonValueKind.Array => v.GetArrayLength() > 0,
        JsonValueKind.Object => true,
        _ => false,
    };

    public static double N(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN;

    public static JsonElement[] A(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToArray() : [];

    /// <summary>The raw JSON of a property, "null" when absent (ConvertTo-Json -Compress of $o.name).</summary>
    public static string Raw(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.GetRawText() : "null";

    /// <summary>Test-FFSchemaKeys: exactly these property names (in any order).</summary>
    public static bool Keys(JsonElement e, params string[] names)
    {
        if (e.ValueKind != JsonValueKind.Object) return false;
        var actual = e.EnumerateObject().Select(p => p.Name).ToArray();
        return actual.Length == names.Length && NightlyPlanCases.SameSet(actual, names);
    }

    // ---- assertions shared by the conservation cases ------------------------------------------------------------

    /// <summary>Test-FFPhaseBindings: the phase journals of both arms join every TRX row with the same summary bytes; a stale header is not joined.</summary>
    public static void PhaseBindings(NightlyFixture fixture, ArmFixture fx, NightlyCaseRows rows, string caseName)
    {
        var ok = true;
        foreach (var variant in new[] { "phase", "phase-stale" })
        {
            var left = Prepared(fixture, fx, variant + "-canonical", [], variant);
            var right = Prepared(fixture, fx, variant + "-priority", fx.Members, variant);
            var summaries = new List<JsonElement>();
            foreach (var arm in new[] { left, right })
            {
                var summary = ReadJson(Path.Combine(arm.TimingRoot, "summary.json"));
                summaries.Add(summary);
                var shared = A(summary, "sharedFixtures");
                if (A(summary, "tests").Length != 4 || shared.Length != 1 || S(shared[0], "fixtureId") != "shared-fixture" || N(shared[0], "db") != 11 || N(shared[0], "fixture") != 16
                    || N(summary, "tornLines") != 0 || N(summary, "malformedLines") != 0) ok = false;
                foreach (var test in A(summary, "tests"))
                {
                    if (variant == "phase-stale" && S(test, "testId") == "Fixture.antiphon.Priority.Run")
                    {
                        if (!B(test, "incomplete") || B(test, "joined")) ok = false;
                        continue;
                    }
                    if (!B(test, "joined") || B(test, "incomplete") || S(test, "outcome") != "Passed" || S(test, "trxOutcome") != "Passed" || S(test, "phaseOutcome") != "Passed"
                        || N(test, "db") != 1 || N(test, "host") != 2 || N(test, "process") != 3 || N(test, "fs") != 4 || N(test, "network") != 5 || N(test, "fixture") != 6
                        || N(test, "overlapMs") != 7 || N(test, "remainderMs") != 8 || N(test, "droppedPhaseCount") != 0) ok = false;
                }
                var quality = A(summary, "quality").Select(q => q.GetString()).ToArray();
                if (variant == "phase" && (!B(summary, "complete") || quality.Length != 0)) ok = false;
                if (variant == "phase-stale" && (B(summary, "complete") || !quality.Contains("stale-input-not-joined") || !quality.Contains("missing-phase-unknown"))) ok = false;
                var bySuite = fx.Suites.Select(s => H.Str(s, "id")).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var file in Directory.EnumerateFiles(arm.TimingRoot, "cases-*.jsonl"))
                {
                    using var header = JsonDocument.Parse(File.ReadLines(file).First());
                    var h = header.RootElement;
                    if (!bySuite.Contains(S(h, "suite")) || S(h, "sourceDigest") != Sha || S(h, "assembly") != S(h, "suite") || S(h, "attempt") != "1") ok = false;
                }
            }
            static string Tests(JsonElement summary) => string.Join("\n", A(summary, "tests").OrderBy(t => S(t, "testId"), NightlyPlanCases.SortObject).Select(t => t.GetRawText()));
            if (Tests(summaries[0]) != Tests(summaries[1])
                || File.ReadAllText(Path.Combine(left.TimingRoot, "tests.csv")) != File.ReadAllText(Path.Combine(right.TimingRoot, "tests.csv"))
                || File.ReadAllText(Path.Combine(left.TimingRoot, "summary.md")) != File.ReadAllText(Path.Combine(right.TimingRoot, "summary.md"))) ok = false;
        }
        rows.Check(ok, "FF " + caseName + ".phaseBindings");
    }

    /// <summary>Test-FFPlannedGoldenBytes: the planned manifest of one fixture group equals scripts/fixtures/nightly/c1514-failed-first-planned.json byte for byte.</summary>
    public static void PlannedGoldenBytes(NightlyFixture fixture, NightlyCaseRows rows, string caseName)
    {
        var timing = fixture.PathOf("golden-actual");
        object[] nodes = [new PlainObject(("Uid", "CaseUid"), ("ClassName", "Fixture.ManifestTests"), ("Type", "ManifestTests"), ("Namespace", "Fixture"), ("Method", "Run"), ("Categories", Array.Empty<object>()))];
        var group = new PlainObject(("id", "one"), ("classes", new object[] { "Fixture.ManifestTests" }), ("nodes", new object[] { new PlainObject(("Uid", "caseuid")) }), ("seconds", 12.5),
            ("groupFilter", "/*/Fixture/ManifestTests/*"));
        WritePlanned(new TestPhasePlannedRequest
        {
            LogRoot = fixture.Root, RunId = "golden", Attempt = "1", Suite = "antiphon", SourceDigest = "source", AssemblyDigest = "assembly", PolicyDigest = "policy",
            CostFileDigest = "cost", AssemblyId = "fixture", Groups = new object[] { group }, DiscoveryNodes = nodes, ProfileExclusions = Array.Empty<object>(),
        }, new TestPhasePublicationScope(timing, "Core", "7.5.0", null, null));
        var expected = File.ReadAllBytes(Path.Combine(H.Repo, "scripts", "fixtures", "nightly", "c1514-failed-first-planned.json"));
        var actual = File.ReadAllBytes(Path.Combine(timing, "groups-planned-antiphon.json"));
        rows.Check(expected.AsSpan().SequenceEqual(actual), "FF " + caseName + ".originalManifestBytes");
    }

    /// <summary>Test-FFArtifactSchemas: the projection, schedule, plan, journal, state, seal, records and phase summary keep their exact keys and bindings.</summary>
    public static void ArtifactSchemas(IEnumerable<Arm> arms, NightlyCaseRows rows, string caseName)
    {
        var ok = true;
        foreach (var arm in arms)
        {
            if (!Keys(arm.Projection, "version", "runId", "attempt", "suite", "manufacturedPassed", "chunks")) ok = false;
            foreach (var chunk in A(arm.Projection, "chunks"))
                if (!Keys(chunk, "chunk", "runId", "attempt", "suite", "hostWallSeconds", "trxMissing", "manufacturedPassed", "passedCount", "outcomes", "trx", "trxDigest", "exitCode",
                    "coverageComplete", "testsPassed")) ok = false;
            var schedule = ReadJson(Path.Combine(arm.Root, "dispatch-schedule.json"));
            if (!Keys(schedule, "format", "runId", "attempt", "historyDigest", "canonicalPlanDigest", "preparationComplete", "unresolved", "entries")) ok = false;
            foreach (var entry in A(schedule, "entries")) if (!Keys(entry, "suite", "chunk", "canonicalOrdinal", "dispatchOrdinal", "priorFailure")) ok = false;
            foreach (var text in arm.Plans.Values)
            {
                using var plan = JsonDocument.Parse(text);
                if (!Keys(plan.RootElement, "version", "runId", "attempt", "suite", "sourceDigest", "assemblyDigest", "policyDigest", "costFileDigest", "assembly", "requiredPartitionExact",
                    "missingIds", "extraIds", "groups")) ok = false;
                foreach (var group in A(plan.RootElement, "groups"))
                    if (!Keys(group, "chunk", "classes", "filter", "plannedTestIds", "plannedSeconds", "pricingBasis", "sharedManifestOrder")) ok = false;
            }
            var journal = Path.Combine(arm.TimingRoot, "groups-executed.journal");
            var pointer = ReadJson(Path.Combine(journal, "current.json"));
            var generation = Path.Combine(journal, S(pointer, "generation"));
            var meta = ReadJson(Path.Combine(generation, "metadata.json"));
            var state = ReadJson(Path.Combine(arm.TimingRoot, "groups-executed.state.json"));
            var seal = ReadJson(Path.Combine(generation, "seal.json"));
            if (!Keys(pointer, "version", "timingRoot", "generation", "metadataDigest")
                || !Keys(meta, "version", "generation", "runId", "attempt", "suite", "source", "invocationId", "writerEnvironment", "seedDigest", "parentGeneration", "unresolved", "corruptionReason")
                || !Keys(state, "version", "generation", "sequence", "root", "publicationComplete", "unresolved", "reason", "projectionDigest", "quality")) ok = false;
            if (S(pointer, "metadataDigest") != PriorFailureHistory.Digest(Path.Combine(generation, "metadata.json"))
                || S(seal, "stateDigest") != PriorFailureHistory.Digest(Path.Combine(arm.TimingRoot, "groups-executed.state.json")) || S(meta, "runId") != RunId || S(meta, "attempt") != "1"
                || S(meta, "source") != Sha || S(meta, "suite") != "" || S(meta, "seedDigest") != "" || S(meta, "invocationId") != RunId || B(meta, "unresolved")
                || !B(state, "publicationComplete") || B(state, "unresolved") || A(state, "quality").Length != 0) ok = false;
            foreach (var recordPath in Records(generation))
            {
                var text = File.ReadAllText(recordPath);
                using var parsed = JsonDocument.Parse(text);
                var record = parsed.RootElement;
                if (!Keys(record, "version", "kind", "generation", "sequence", "publicationId", "root", "freshSuiteKey", "settledSuiteKey", "freshChunkKey", "settledChunkKey", "freshRun",
                    "freshAttempt", "freshHeaderSuite", "digest", "rowJson", "settledRowJson", "writerEnvironment", "reason")) ok = false;
                var blank = text.Replace("\"digest\":\"" + S(record, "digest") + "\"", "\"digest\":\"\"");
                if (TestPhaseRecords.Sha256Text(blank) != S(record, "digest") || S(record, "generation") != S(pointer, "generation") || TestPhaseText.FromB64(S(record, "freshRun")) != RunId
                    || TestPhaseText.FromB64(S(record, "freshAttempt")) != "1" || S(record, "freshSuiteKey") != S(record, "settledSuiteKey") || S(record, "freshChunkKey") != S(record, "settledChunkKey")
                    || S(record, "writerEnvironment") != S(meta, "writerEnvironment")) ok = false;
            }
            var summary = ReadJson(Path.Combine(arm.TimingRoot, "summary.json"));
            if (!Keys(summary, "version", "complete", "quality", "tornLines", "malformedLines", "summaryAggregationBudgetMilliseconds", "summaryAggregationMilliseconds", "tests", "sharedFixtures")
                || B(summary, "complete") || !A(summary, "quality").Any(q => q.GetString() == "missing-phase-unknown") || N(summary, "tornLines") != 0 || N(summary, "malformedLines") != 0
                || N(summary, "summaryAggregationBudgetMilliseconds") != 5000 || N(summary, "summaryAggregationMilliseconds") < 0 || A(summary, "sharedFixtures").Length != 0
                || A(summary, "tests").Length != 4) ok = false;
            var csv = File.ReadAllLines(Path.Combine(arm.TimingRoot, "tests.csv"));
            if (csv.Length != 5 || !NightlyPlanCases.Like(csv[0].ToLowerInvariant(),
                "*testid*duration*groupid*outcome*trxoutcome*phaseoutcome*joined*incomplete*db*host*process*fs*network*fixture*overlapms*remainderms*droppedphasecount*")) ok = false;
            var markdown = File.ReadAllText(Path.Combine(arm.TimingRoot, "summary.md"));
            if (!markdown.Contains("TRX remains authoritative. Sidecars are private and are not gate inputs.", StringComparison.Ordinal)
                || !markdown.Contains("Shared fixture rows are separate from per-test totals.", StringComparison.Ordinal)) ok = false;
        }
        rows.Check(ok, "FF " + caseName + ".artifactSchemas");
    }

    /// <summary>The numbered journal records of a generation, by name.</summary>
    public static string[] Records(string generation) =>
        Directory.EnumerateFiles(generation).Where(f => Regex.IsMatch(Path.GetFileName(f), "^[0-9]{8}\\.json$")).OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).ToArray();

    // ---- the authority conservation -----------------------------------------------------------------------------

    private const string CandidateId = "rc-20260923T083000Z";
    private const string CandidateRef = "release/rc-20260923T083000Z";
    private const string IntentId = "intent-20260923T083000Z";
    private const string AuthorityRunId = "rc-run-1";
    private const string ReleaseCardId = "5b0e6c1e-8f1c-4c3e-9a55-0c5990c5990a";
    private const string Repository = "michal-ciechan/Antiphon";
    private static readonly DateTime Now = new(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);
    private static readonly string[] NativeSuites = ["antiphon", "session-runner", "pty-host", "agents-pty", "messaging", "e2e", "markdown-pdf", "observability", "server-git", "resilience", "git-in-process"];

    /// <summary>The C599 release authority: a real policy checkout, the frozen authority and plan, one TRX per chunk, the ledger and the RC green set.</summary>
    public sealed record Authority(string Root, string Checkout, string CandidateRoot, string Dir, string Head, JsonElement Green, JsonElement Candidate, JsonElement Card);

    /// <summary>
    /// New-C599PublishFx's authority (CARD-0599 D-15) through the production functions: the release clone is a real git
    /// repository whose commit holds the live policy; ReleaseAuthority.Freeze and Plan write authority, policy, discovery
    /// and the execution plan; every native chunk gets a real TRX; the ledger binds them; the release card (revision 4)
    /// carries the run's correlation line.
    /// </summary>
    public static Authority NewAuthority(string root)
    {
        Directory.CreateDirectory(root);
        var checkout = Path.Combine(root, "checkout");
        Directory.CreateDirectory(Path.Combine(checkout, "tests"));
        File.Copy(Path.Combine(H.Repo, "tests", "test-execution-policy.json"), Path.Combine(checkout, "tests", "test-execution-policy.json"));
        string RealGit(params string[] args)
        {
            var r = ReleaseAuthority.Git(checkout, args);
            if (r.ExitCode != 0) throw new InvalidOperationException("fixture git " + string.Join(" ", args) + " failed: " + r.Error);
            return r.Text.Trim();
        }
        RealGit("init", "-q");
        RealGit("config", "core.autocrlf", "false");
        RealGit("remote", "add", "origin", "https://github.com/" + Repository);
        RealGit("add", "--", "tests/test-execution-policy.json");
        RealGit("-c", "user.name=c599", "-c", "user.email=c599@example.invalid", "commit", "-q", "-m", "policy");
        var head = RealGit("rev-parse", "HEAD");
        NightlyFixture.WriteJson(Path.Combine(checkout, ".antiphon-release-owned"), new { kind = "release-clone" });
        var releaseRoot = Path.Combine(root, "releases");
        var candidateRoot = ReleaseGateCredit.CandidateRoot(releaseRoot, CandidateId);
        var scripts = Path.Combine(H.Repo, "scripts");
        var auth = ReleaseAuthority.Freeze(checkout, candidateRoot, head, Repository, CandidateId, CandidateRef, IntentId, ReleaseCardId,
            new DateTime(2026, 9, 23, 8, 30, 30, DateTimeKind.Utc), scripts);
        var discovery = new Hashtable(StringComparer.OrdinalIgnoreCase);
        foreach (var suite in NativeSuites)
        {
            var tag = Regex.Replace(suite, "[^a-z]", "");
            var nodes = new List<object>
            {
                new Hashtable { ["uid"] = suite + "-u1", ["class"] = tag + "AlphaTests", ["method"] = "One" },
                new Hashtable { ["uid"] = suite + "-u2", ["class"] = tag + "AlphaTests", ["method"] = "Two" },
                new Hashtable { ["uid"] = suite + "-u3", ["class"] = tag + "BetaTests", ["method"] = "Three(1)" },
            };
            if (suite == "antiphon")
            {
                nodes.Add(new Hashtable { ["uid"] = "antiphon-u4", ["class"] = "antiphonGammaTests", ["method"] = "Four" });
                nodes.Add(new Hashtable { ["uid"] = "antiphon-x1", ["class"] = "ClaudeAdapterIntegrationTests", ["method"] = "Live" });
            }
            if (suite == "e2e") nodes.Add(new Hashtable { ["uid"] = "e2e-x1", ["class"] = "DelegationPipelineE2ETests", ["method"] = "Live" });
            discovery[suite] = nodes.ToArray();
        }
        var rosters = new Hashtable(StringComparer.OrdinalIgnoreCase) { ["client"] = new object[] { "lint", "build", "vitest" }, ["scripts"] = new object[] { "test-release-gate.ps1", "test-nightly.ps1" } };
        var plan = ReleaseAuthority.Plan(candidateRoot, discovery, rosters);
        var dir = ReleaseAuthority.Dir(candidateRoot);
        var byUid = new Dictionary<string, Hashtable>(StringComparer.Ordinal);
        foreach (DictionaryEntry suite in discovery) foreach (Hashtable n in (object[])suite.Value!) byUid[(string)n["uid"]!] = n;
        var chunks = new List<object>();
        var minute = 0;
        foreach (IDictionary suite in (IEnumerable)plan["suites"]!)
        {
            if ((string?)suite["kind"] != "native") continue;
            foreach (IDictionary c in (IEnumerable)suite["chunks"]!)
            {
                minute++;
                var rel = "evidence/" + c["id"] + ".trx";
                var trx = Path.Combine(candidateRoot, rel);
                var trxRows = ((IEnumerable)c["uids"]!).Cast<object>().Select(u => new NightlyTrxRow((string)u, (string)byUid[(string)u]["class"]!, (string)byUid[(string)u]["method"]!)).ToArray();
                NightlyFixture.WriteTrx(trx, trxRows);
                var start = new DateTime(2026, 9, 23, 8, 31, 0, DateTimeKind.Utc).AddMinutes(minute);
                chunks.Add(new OrderedDictionary
                {
                    { "suite", suite["id"] }, { "chunk", c["id"] }, { "intentId", IntentId }, { "candidateId", CandidateId }, { "sha", head }, { "runId", AuthorityRunId },
                    { "exitCode", 0 }, { "startedAt", start.ToString("o", CultureInfo.InvariantCulture) }, { "completedAt", start.AddSeconds(30).ToString("o", CultureInfo.InvariantCulture) },
                    { "trx", rel }, { "trxSha256", ReleaseAuthority.FileSha256(trx) }, { "executed", trxRows.Length }, { "passed", trxRows.Length }, { "failed", 0 }, { "skipped", 0 },
                });
            }
        }
        var rosterRows = new[] { "client", "scripts" }.Select(id => (object)new OrderedDictionary
        {
            { "suite", id }, { "intentId", IntentId }, { "candidateId", CandidateId }, { "sha", head }, { "runId", AuthorityRunId }, { "exitCode", 0 },
            { "startedAt", "2026-09-23T09:30:00Z" }, { "completedAt", "2026-09-23T09:35:00Z" },
            { "entries", ((object[])rosters[id]!).Select(e => (object)new OrderedDictionary { { "id", e }, { "outcome", "passed" } }).ToArray() },
        }).ToArray();
        H.WriteJson(Path.Combine(dir, "ledger.json"), new OrderedDictionary
        {
            { "schemaVersion", 1 }, { "intentId", IntentId }, { "candidateId", CandidateId }, { "candidateRef", CandidateRef }, { "sha", head }, { "runId", AuthorityRunId },
            { "authorityDigest", ReleaseAuthority.FileSha256(Path.Combine(dir, "authority.json")) },
            { "executionPlanDigest", ReleaseAuthority.FileSha256(Path.Combine(dir, "execution-plan.json")) },
            { "startedAt", "2026-09-23T08:31:00Z" }, { "completedAt", "2026-09-23T09:50:00Z" }, { "teardownSucceeded", true }, { "seamed", false }, { "noReport", false },
            { "diagnostic", false }, { "selection", "full" },
            { "prepublicationReceipt", new OrderedDictionary
                {
                    { "kind", "prepublication" }, { "recipient", new OrderedDictionary { { "kind", "release-card" }, { "cardId", ReleaseCardId }, { "revision", 4 } } },
                    { "correlation", new OrderedDictionary { { "intentId", IntentId }, { "candidateId", CandidateId }, { "sha", head }, { "runId", AuthorityRunId } } },
                    { "acceptedAt", "2026-09-23T09:52:00Z" },
                } },
            { "buildHash", "build-1" }, { "bundleHash", "bundle-1" },
            { "prerequisites", new object[]
                {
                    new OrderedDictionary { { "id", "build" }, { "exitCode", 0 }, { "succeeded", true } },
                    new OrderedDictionary { { "id", "client-bundle" }, { "exitCode", 0 }, { "succeeded", true } },
                    new OrderedDictionary { { "id", "script-census" }, { "exitCode", 0 }, { "succeeded", true } },
                } },
            { "chunks", chunks.ToArray() }, { "rosters", rosterRows },
        });
        // New-C599RcGreen: the candidate journal, summary and complete-green, all on the same candidate, SHA and policy hash.
        H.WriteJson(Path.Combine(candidateRoot, "candidate.json"), new OrderedDictionary
        {
            { "schemaVersion", 1 }, { "candidateId", CandidateId }, { "ref", CandidateRef }, { "sha", head }, { "cutUtc", "2026-09-23T08:30:00Z" },
            { "scheduleSlot", "u/lndcobra/antiphon_release_candidates" }, { "pushed", true },
        });
        var summaryPath = Path.Combine(candidateRoot, "summary.json");
        H.WriteJson(summaryPath, new OrderedDictionary
        {
            { "profile", "rc" }, { "policyHash", auth.PolicyHash }, { "requiredSuites", new object[] { "antiphon", "e2e" } },
            { "suites", new object[]
                {
                    new OrderedDictionary { { "id", "antiphon" }, { "chunk", "all" }, { "result", "pass" }, { "exitCode", 0 }, { "skipped", false }, { "requiredUidCount", 12 }, { "excludedUidCount", 1 }, { "censusDigest", "d1" }, { "exclusions", Array.Empty<object>() } },
                    new OrderedDictionary { { "id", "e2e" }, { "chunk", "all" }, { "result", "pass" }, { "exitCode", 0 }, { "skipped", false }, { "requiredUidCount", 8 }, { "excludedUidCount", 3 }, { "censusDigest", "d2" },
                        { "exclusions", new object[] { new OrderedDictionary { { "uid", "u1" }, { "class", "LiveCanaryTests" }, { "reason", "real headed provider" }, { "owner", "CARD-0599" } } } } },
                } },
        });
        H.WriteJson(Path.Combine(candidateRoot, "complete-green.json"), new OrderedDictionary
        {
            { "runId", AuthorityRunId }, { "policyHash", auth.PolicyHash }, { "profile", "rc" }, { "trigger", "rc" }, { "ref", CandidateRef }, { "coverageComplete", true },
            { "testsPassed", true }, { "reportDelivered", true }, { "exitCode", 0 }, { "sha", head }, { "expectedSha", head }, { "candidateId", CandidateId },
            { "startedAt", "2026-09-23T08:31:00Z" }, { "completedAt", "2026-09-23T09:50:00Z" }, { "summaryPath", summaryPath }, { "capabilities", new object[] { "land-v2" } },
        });
        var card = JsonSerializer.SerializeToElement(new
        {
            id = ReleaseCardId, identifier = "CARD-0601", title = "Release " + CandidateId,
            description = "Release candidate " + CandidateId + "\n\nrelease-correlation: intentId=" + IntentId + " candidateId=" + CandidateId + " sha=" + head + " runId=" + AuthorityRunId
                + "\n\nTests: all eight suites passed.",
            revisionCount = 4, status = "Review", concurrencyToken = "0b0e6c1e-8f1c-4c3e-9a55-0c5990c59900",
        });
        return new Authority(root, checkout, candidateRoot, dir, head, ReadJson(Path.Combine(candidateRoot, "complete-green.json")), ReadJson(Path.Combine(candidateRoot, "candidate.json")), card);
    }

    /// <summary>Get-C599AGate's authority half: ReleaseAuthority.Check with the bound card read back from the fixture (no HTTP).</summary>
    public static AuthorityCheck Gate(Authority authority)
    {
        var previous = ReleaseAuthority.CardReader;
        ReleaseAuthority.CardReader = (_, cardId) => new CardReadback { Ok = string.Equals(cardId, ReleaseCardId, StringComparison.OrdinalIgnoreCase), Card = authority.Card };
        try
        {
            return ReleaseAuthority.Check(authority.CandidateRoot, authority.Checkout, authority.Green, authority.Candidate, Repository, Now, "http://127.0.0.1:1/fx/card-api",
                Path.Combine(H.Repo, "scripts"));
        }
        finally { ReleaseAuthority.CardReader = previous; }
    }

    /// <summary>
    /// Test-FFAuthorityConservation: a prepared arm with priority members leaves every authority file byte-identical
    /// and the authority valid before and after, with no git write; the ledger keeps its prepublication receipt.
    /// </summary>
    public static void AuthorityConservation(NightlyFixture fixture, NightlyCaseRows rows, string caseName)
    {
        var git = new FakeGit();
        var authority = NewAuthority(fixture.PathOf("authority-fixture"));
        var before = Directory.EnumerateFiles(authority.Dir).ToDictionary(Path.GetFileName, PriorFailureHistory.Digest, StringComparer.Ordinal);
        var gate = Gate(authority);
        var small = Small();
        Prepared(fixture, small, "authority-diagnostic", small.Members, git: git);
        var after = Gate(authority);
        var unchanged = Directory.EnumerateFiles(authority.Dir).All(f => before.TryGetValue(Path.GetFileName(f), out var digest) && digest == PriorFailureHistory.Digest(f));
        // No remote write: the run's git seam saw no push or tag.
        var noRemoteWrite = !git.Calls.Any(c => c.Arguments.Count > 0 && c.Arguments[0] is "push" or "tag");
        rows.Check(gate.Ok && after.Ok && unchanged && noRemoteWrite, "FF " + caseName + ".authorityBytes", string.Join(",", gate.Reasons.Concat(after.Reasons)));
        var ledger = ReadJson(Path.Combine(authority.Dir, "ledger.json"));
        rows.Check(S(ledger.GetProperty("prepublicationReceipt"), "kind") == "prepublication" && before.ContainsKey("execution-plan.json") && before.ContainsKey("discovery.json")
            && before.ContainsKey("authority.json") && before.ContainsKey("ledger.json"), "FF " + caseName + ".authorityBinding");
    }

    // ---- the retained archive (CARD-1590) -----------------------------------------------------------------------

    /// <summary>
    /// The driver's epilogue: the case's synthetic inputs copied and hashed under .antiphon/failfirst/&lt;sha&gt;/portable/
    /// &lt;case&gt;-&lt;guid&gt;/ with an inventory. A whole .git path segment (a nested checkout's admin files) is listed but never
    /// copied, so no repository lands in the worktree; a link is listed, not followed. Returns the archive directory.
    /// </summary>
    public static string Retain(string root, string caseName, string testClass)
    {
        var source = H.HeadSha(H.Repo);
        var archive = Path.Combine(H.Repo, ".antiphon", "failfirst", source, "portable", caseName + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(archive);
        var inventory = new List<object>();
        var pending = new Stack<DirectoryInfo>();
        var files = new List<FileInfo>();
        pending.Push(new DirectoryInfo(root));
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var item in directory.EnumerateFileSystemInfos().OrderBy(i => i.Name, StringComparer.Ordinal))
            {
                if (item is DirectoryInfo child) { if (!child.Attributes.HasFlag(FileAttributes.ReparsePoint)) pending.Push(child); }
                else files.Add((FileInfo)item);
            }
        }
        foreach (var file in files.OrderBy(f => f.FullName, StringComparer.Ordinal))
        {
            var relative = file.FullName[root.Length..].TrimStart('\\', '/');
            if (file.Attributes.HasFlag(FileAttributes.ReparsePoint)) { inventory.Add(new OrderedDictionary { { "path", relative }, { "kind", "fixture-link" }, { "copied", false } }); continue; }
            if (relative == "performance.json") continue;
            if (relative.Split('\\', '/').Contains(".git", StringComparer.OrdinalIgnoreCase))
            {
                inventory.Add(new OrderedDictionary { { "path", relative }, { "kind", "git-dir" }, { "copied", false } });
                continue;
            }
            var destination = Path.Combine(archive, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file.FullName, destination, false);
            inventory.Add(new OrderedDictionary { { "path", relative }, { "kind", "regular" }, { "copied", true }, { "sha256", PriorFailureHistory.Digest(file.FullName) } });
        }
        H.WriteJson(Path.Combine(archive, "inventory.json"), new OrderedDictionary { { "source", source }, { "case", caseName }, { "testClass", testClass }, { "files", inventory.ToArray() } });
        return archive;
    }

    /// <summary>The C1590_ArchiveSegments fixture: a git checkout and files whose names merely contain .git.</summary>
    public static readonly string[] ArchiveSegmentFiles = [".gitignore", ".github/workflow.yml", "foo.git.txt", "checkout/.gitignore", "checkout/tracked.txt"];

    public static void ArchiveSegments(string root, NightlyCaseRows rows)
    {
        var checkout = Path.Combine(root, "checkout");
        Directory.CreateDirectory(checkout);
        var init = 0;
        try { H.RunGit(checkout, "init", "-q", "-b", "fixture"); } catch (InvalidOperationException) { init = 1; }
        foreach (var relative in ArchiveSegmentFiles)
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, relative);
        }
        var written = ArchiveSegmentFiles.Count(f => File.Exists(Path.Combine(root, f)));
        rows.Check(init == 0 && File.Exists(Path.Combine(checkout, ".git", "HEAD")) && written == ArchiveSegmentFiles.Length, "FF C1590_ArchiveSegments.fixture",
            "git init exit=" + init + " written=" + written);
    }
}
