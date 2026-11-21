using System.Collections.Specialized;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Antiphon.Nightly;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S13: the fixtures of the failed-first cases moved in-process from scripts/test-nightly-failed-first.ps1
/// (GlobalReplayPriority, PreparationSequencing, PreviousOrder), over the compiled dispatch (NightlyDispatch,
/// NightlySuiteRunner, DispatchSchedule) with plain values: New-FFNode, New-FFReplay (the 19,012-node listing replay),
/// New-FFGlobalFixture, Write-FFDiagnostic and Invoke-FFPreparationArm.
/// </summary>
internal static class FailedFirstDispatchCases
{
    public static string Repo => NightlyPlanCases.Repo;

    /// <summary>New-FFNode $Class $Method $Uid: { ClassName, Namespace, Type, Method, Uid, Categories, Excluded }.</summary>
    public static PlainObject Node(string cls, string method = "Run", string uid = "", params object[] categories)
    {
        var bare = NightlyCensus.BareClassIdentity(cls);
        var dot = bare.LastIndexOf('.');
        if (uid.Length == 0) uid = cls + "." + method;
        return new PlainObject(("ClassName", cls), ("Namespace", bare[..dot]), ("Type", cls[(dot + 1)..]), ("Method", method), ("Uid", uid),
            ("Categories", categories), ("Excluded", false));
    }

    /// <summary>FFText.ReplayNodes: class|S-or-other|method=count,... lines; a bare count names its methods M0000, M0001, ...</summary>
    public static List<object> ReplayNodes(string listingPath)
    {
        var nodes = new List<object>();
        foreach (var line in File.ReadAllLines(listingPath))
        {
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var fields = line.Split('|');
            var slow = fields.Length > 1 && fields[1] == "S";
            var i = 0;
            foreach (var entry in fields[2].Split(','))
            {
                var eq = entry.IndexOf('=');
                string method;
                int count;
                if (eq >= 0) { method = entry[..eq]; count = int.Parse(entry[(eq + 1)..], CultureInfo.InvariantCulture); }
                else { method = "M" + i.ToString("0000", CultureInfo.InvariantCulture); count = int.Parse(entry, CultureInfo.InvariantCulture); }
                i++;
                for (var k = 0; k < count; k++)
                    nodes.Add(Node(fields[0], method, fields[0] + "." + method + "." + k.ToString(CultureInfo.InvariantCulture), slow ? ["Slow"] : []));
            }
        }
        return nodes;
    }

    /// <summary>A prepared suite record { id, nodes, chunks, exclusions }.</summary>
    public static PlainObject Suite(string id, object[] nodes, object[] chunks, object[] exclusions) =>
        new(("id", id), ("nodes", nodes), ("chunks", chunks), ("exclusions", exclusions));

    /// <summary>New-FFReplay: the 19,012-node antiphon listing planned for rc on Windows.</summary>
    public static PlainObject Replay(PolicyFile policy)
    {
        var nodes = ReplayNodes(Path.Combine(Repo, "scripts", "fixtures", "nightly", "c1410-antiphon-listing.txt")).ToArray();
        var exclusions = NightlyPlanCases.Exclusions(policy, "rc", "antiphon", "windows");
        var chunks = NightlyTestsPlan.SuiteChunks(nodes, NightlyPlanCases.AllChunk(), exclusions, true, true, "antiphon", policy.Object, NightlyPlanCases.Costs());
        return Suite("antiphon", nodes, chunks, exclusions);
    }

    /// <summary>New-FFGlobalFixture: the replay plus three companion suites, a history of four anchors and three companions, resolved and scheduled.</summary>
    public sealed record GlobalFixture(object[] Suites, PlainObject Replay, object Resolved, object Schedule);

    public static GlobalFixture Global()
    {
        var policy = NightlyPlanCases.Policy();
        var replay = Replay(policy);
        var all = new List<object> { replay };
        foreach (var (suiteId, cls) in new[] { ("session-runner", "Antiphon.SessionRunner.Tests.GrokRulesHttpAcceptanceTests"), ("agents-pty", "Antiphon.Agents.Pty.Tests.FakeClaudeContractTests"), ("e2e", "Antiphon.E2E.BoardE2ETests") })
        {
            object[] nodes = [Node(cls), Node(cls + "Ordinary")];
            var chunks = NightlyTestsPlan.ProfileLaunchPlan(nodes, NightlyPlanCases.AllChunk(), Array.Empty<object>(), suiteId, policy.Object, NightlyPlanCases.Costs());
            // Separate companion classes by exact production class filter, preserving the production planner's prices
            // and watchdog. Supplemental, not replay.
            if (chunks.Length == 1)
            {
                var split = new List<object>();
                foreach (var node in nodes)
                    split.AddRange(NightlyTestsPlan.ProfileLaunchPlan([node], NightlyPlanCases.AllChunk(), Array.Empty<object>(), suiteId, policy.Object, NightlyPlanCases.Costs()));
                chunks = split.ToArray();
                ((PlainObject)chunks[1])["id"] = "ordinary";
            }
            all.Add(Suite(suiteId, nodes, chunks, Array.Empty<object>()));
        }
        var members = new List<object>();
        var replayChunks = (object[])replay["chunks"]!;
        foreach (var ordinal in new[] { 23, 99, 101, 118 })
        {
            var anchor = replayChunks[ordinal - 1];
            var cls = NightlyCensus.BareClassIdentity(PsValue.Str(PsValue.AsArray(PsValue.Prop(anchor, "classes"))[0]));
            if (!members.Any(m => PsValue.Str(PsValue.Prop(m, "className")) == cls)) members.Add(Member("antiphon", cls));
        }
        foreach (var suite in all.Skip(1))
            members.Add(Member(PsValue.Str(PsValue.Prop(suite, "id")), PsValue.Str(PsValue.Prop(PsValue.AsArray(PsValue.Prop(suite, "nodes"))[0], "ClassName"))));
        var history = new PlainObject(("format", "antiphon-nightly-prior-failures-v1"), ("sourceRunId", "old"), ("sourceAttempt", "1"), ("sourceSha", new string('b', 40)),
            ("profile", "rc"), ("platform", "windows"), ("policyHash", new string('b', 64)), ("createdAtUtc", "2026-10-30T00:00:00Z"), ("complete", true),
            ("evidence", Array.Empty<object>()), ("members", members.ToArray()), ("fallbackChunks", Array.Empty<object>()), ("membershipComplete", true),
            ("unresolved", Array.Empty<object>()));
        var suites = all.ToArray();
        var resolved = PriorFailureHistory.Resolve(history, suites, "Full", null, null);
        var schedule = NightlyDispatch.Schedule(suites, PsValue.AsArray(PsValue.Prop(resolved, "members")), new string('d', 64), null, true, "new", "1", null, "",
            DateTime.UtcNow, "");
        return new GlobalFixture(suites, replay, resolved, schedule);
    }

    private static PlainObject Member(string suite, string cls) =>
        new(("suite", suite), ("className", cls), ("reasons", new object[] { "Failed" }), ("observations", Array.Empty<object>()));

    /// <summary>
    /// Test-FFCanonicalDigestBytes: the digest of the canonical rows holds every value kind a chunk can: an equal plan
    /// built again digests equally, and a changed nested value changes it.
    /// </summary>
    public static bool CanonicalDigestBytes(object[] suites)
    {
        static PlainObject Shape(string method) => new(("id", "shape"),
            ("text", "q\"b\\s" + (char)10 + (char)0 + (char)0x2028 + (char)0xe9 + (char)0xd800), ("wrapped", "w"), ("classes", new object[] { "A.B" }),
            ("n", new object[] { -3, 9007199254740993L, 1.0, 2.5, 1e20, -0.0, double.NaN, 1.1f, 1.50m, 'x', DayOfWeek.Monday }),
            ("h", new OrderedDictionary { { "b", 1 }, { "a", new object[] { new object[] { 1, "x" }, new PlainObject(("k", null)) } } }),
            ("noted", 7), ("when", new DateTime(2026, 11, 19, 10, 11, 12, 500, DateTimeKind.Utc)),
            ("nodes", new object[] { new PlainObject(("Uid", "u"), ("ClassName", "A.X"), ("Method", method), ("Categories", new object[] { "OptIn" })) }));
        string Digest(string method) =>
            PsValue.Str(PsValue.Prop(NightlyDispatch.Schedule([.. suites, Suite("digest-shape", [], [Shape(method)], [])]), "canonicalPlanDigest"));
        var first = Digest("M");
        return System.Text.RegularExpressions.Regex.IsMatch(first, "^[a-f0-9]{64}$") && Digest("M") == first && Digest("N") != first;
    }

    // ---- the preparation arm ------------------------------------------------------------------------------------

    /// <summary>Write-FFDiagnostic: TestNodeUpdateMessage lines (each node's outcome, else the state) in &lt;directory&gt;/fixture.diag.</summary>
    public static void Diagnostic(string directory, IEnumerable<object> nodes, string state)
    {
        Directory.CreateDirectory(directory);
        var text = new StringBuilder("Version: '1.44.0.0'\nVersion: 2.2.2\n");
        foreach (var n in nodes)
        {
            var outcome = PsValue.Prop(n, "Outcome") != null ? PsValue.Str(PsValue.Prop(n, "Outcome")) : state;
            text.Append("TestNodeUpdateMessage { TestNode = TestNode { Uid = TestNodeUid { Value = ").Append(NightlyPlanCases.Str(n, "Uid"))
                .Append(" }, DisplayName = fixture, Properties = [ TestMethodIdentifierProperty { AssemblyFullName = Fixture, Namespace = ").Append(NightlyPlanCases.Str(n, "Namespace"))
                .Append(", TypeName = ").Append(NightlyPlanCases.Str(n, "Type"))
                .Append(", MethodName = ").Append(NightlyPlanCases.Str(n, "Method"))
                .Append(", MethodArity = 0, ParameterTypeFullNames = [], ReturnTypeFullName = System.Void }, ").Append(outcome)
                .Append("TestNodeStateProperty { Explanation =  } ] } }\n");
        }
        File.WriteAllText(Path.Combine(directory, "fixture.diag"), text.ToString());
    }

    /// <summary>Invoke-FFPreparationArm's answer.</summary>
    public sealed record PreparationArm(string[] Trace, bool Settled, bool Coverage, object[] Rows, string Error, string ErrorKind, JsonElement? Schedule);

    /// <summary>
    /// Invoke-FFPreparationArm: three rc suites (antiphon, the failing second, agents-pty; antiphon, e2e, agents-pty for the
    /// e2e failure), each with a fixture exclusion so its profile discovery runs, through the real coordinator and suite
    /// runner on a fake host: the second suite's discovery fails (or times out), or the E2E prerequisites fail. History
    /// absent, empty or nonempty (a priority member: the barrier). A write fault puts a directory where the schedule goes.
    /// </summary>
    public static PreparationArm Preparation(NightlyFixture fixture, string name, string historyKind, string failure, bool writeFault = false)
    {
        var logRoot = fixture.PathOf(name);
        Directory.CreateDirectory(logRoot);
        var timingRoot = Path.Combine(logRoot, "timing");
        var policy = NightlyPlanCases.Policy();
        string[] ids = failure == "e2e" ? ["antiphon", "e2e", "agents-pty"] : ["antiphon", "session-runner", "agents-pty"];
        var rc = policy.Object.Profiles["rc"];
        rc.Exclusions = rc.Exclusions.Concat(ids.Select(id => new ProfileExclusion { Suite = id, Class = "Fixture.Never", Reason = "fixture", Owner = "CARD-1514" })).ToArray();
        var trace = new List<string>();
        var settled = false;
        var nodes = ids.ToDictionary(id => id, id => (object)Node("Fixture." + id.Replace("-", "") + ".Only"), StringComparer.Ordinal);
        var second = ids[1];
        string Arg(NightlyLaunch launch, string flag)
        {
            var args = launch.Arguments ?? [];
            for (var i = 0; i + 1 < args.Count; i++) if (args[i] == flag) return args[i + 1];
            return "";
        }
        NightlyLaunchResult Pass() => new() { ExitCode = 0, TimedOut = false, ChildrenExited = true, CleanupComplete = true, LogText = "" };
        var launcher = new RecordingLauncher(launch =>
        {
            var diag = Arg(launch, "--diagnostic-output-directory");
            if ((launch.Arguments ?? []).Contains("--list-tests"))
            {
                var sid = Path.GetFileName(diag);
                if (sid.EndsWith("-profile-discovery", StringComparison.Ordinal)) sid = sid[..^"-profile-discovery".Length];
                if (sid == second)
                {
                    var journal = Path.Combine(timingRoot, "groups-executed.journal");
                    if (Directory.Exists(journal) && Directory.EnumerateFiles(journal, "00000001.json", SearchOption.AllDirectories).Any())
                    {
                        settled = true;
                        trace.Add("settle-antiphon");
                    }
                }
                trace.Add("prepare-" + sid);
                if (sid == second && failure != "e2e")
                {
                    trace.Add("fail-" + sid);
                    return new NightlyLaunchResult { ExitCode = 1, TimedOut = failure == "timeout", ChildrenExited = true, CleanupComplete = true, LogText = "" };
                }
                Diagnostic(diag, [nodes[sid]], "Discovered");
            }
            else
            {
                var sid = launch.Environment != null && launch.Environment.TryGetValue("ANTIPHON_TEST_PHASE_SUITE", out var value) ? value ?? "" : "";
                trace.Add("launch-" + sid);
                var node = nodes[sid];
                var trx = Path.Combine(Arg(launch, "--results-directory"), Arg(launch, "--report-trx-filename"));
                NightlyFixture.WriteTrx(trx, [new NightlyTrxRow(NightlyPlanCases.Str(node, "Uid"), NightlyPlanCases.Str(node, "ClassName"), NightlyPlanCases.Str(node, "Method"))]);
                Diagnostic(diag, [node], "Passed");
            }
            return Pass();
        });
        var host = fixture.Host(launcher: launcher, clock: TimeProvider.System);
        if (writeFault)
        {
            Directory.CreateDirectory(Path.Combine(logRoot, "dispatch-schedule.json"));
            Directory.CreateDirectory(Path.Combine(logRoot, "dispatch-schedule-write-failed"));
        }
        var scope = new TestPhasePublicationScope(timingRoot, "Core", "7.5.0", null, null);
        var context = TestPhasePublication.Open(logRoot, timingRoot, "preparation-run", "1", "", new string('a', 40), "", false, false, scope);
        var state = new NightlyRunState
        {
            Host = host,
            RepoRoot = Repo,
            LogRoot = logRoot,
            TimingRoot = timingRoot,
            QualificationTimingRoot = timingRoot,
            InvocationId = "preparation-run",
            Attempt = "1",
            Profile = "rc",
            Sha = new string('a', 40),
            GitRef = "origin/master",
            Policy = policy.Object,
            PolicyHash = policy.Hash,
            Platform = "windows",
            StartedAt = DateTime.UtcNow.AddSeconds(-1),
            BuildSeconds = 1.25,
            ProfileAware = true,
            RequiredSuites = ids,
            ChunksBySuite = NightlyPlanCases.Table(),
            ClassCostPath = NightlyPlanCases.CostPath,
            ExplicitPhaseRoot = "",
            ProduceDiscovery = request => ProduceDiscovery(host, request),
            E2EPrerequisites = (_, _, _, _) =>
            {
                trace.Add("prepare-e2e");
                trace.Add("fail-e2e");
                return new PlainObject(("Ok", false), ("Rows", new object[] { new PlainObject(("name", "fixture-browser"), ("exitCode", 1), ("detail", "fixture")) }));
            },
            PublishObservation = seen => TestPhaseManifests.WriteExecuted(seen.Verdict, seen.LogRoot, seen.TrxPath, seen.Suite, seen.Chunk, seen.RunId, seen.Attempt,
                seen.HostWallSeconds, context, scope),
            PublicationScope = () => scope,
            Warning = _ => { },
        };
        object[]? priority = null;
        object[]? unresolved = null;
        var digest = "";
        if (historyKind == "nonempty")
        {
            priority = [new PlainObject(("suite", "antiphon"), ("chunks", new object[] { "all-group-1" }))];
            digest = new string('d', 64);
        }
        if (historyKind == "empty") { priority = []; unresolved = []; }
        var error = "";
        try
        {
            NightlyDispatch.Coordinate(state, new NightlySuiteRunner(state).Steps(), ids, priority, unresolved, digest, null, null, "");
        }
        catch (Exception ex)
        {
            error = ex.Message;
            state.CoverageComplete = false;
        }
        finally
        {
            TestPhasePublication.CompleteProjection(context, "failed-run", scope);
            TestPhasePublication.Close(context);
        }
        JsonElement? schedule = null;
        var schedulePath = Path.Combine(logRoot, "dispatch-schedule.json");
        if (File.Exists(schedulePath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(schedulePath));
            schedule = doc.RootElement.Clone();
        }
        var kind = error.StartsWith("missing diagnostic discovery log in ", StringComparison.Ordinal) ? "missing diagnostic discovery log" : error;
        return new PreparationArm(trace.ToArray(), settled, state.CoverageComplete, state.SuiteResults.ToArray(), error, kind, schedule);
    }

    /// <summary>
    /// Invoke-NightlyProduceDiscovery's steps on the host (its PowerShell is S15's): the --list-tests launch, its timeout,
    /// the diagnostic log, the parse, and the discovery document written and returned.
    /// </summary>
    public static object ProduceDiscovery(NightlyHost host, NightlyDiscoveryRequest request)
    {
        Directory.CreateDirectory(request.DiagnosticDirectory);
        var args = new List<string>(request.ArgumentPrefix.Where(a => !string.IsNullOrWhiteSpace(a)))
        {
            "--list-tests", "--no-progress", "--no-ansi", "--diagnostic", "--diagnostic-output-directory", request.DiagnosticDirectory,
        };
        var run = host.Launch(NightlyChunkRunner.OwnedLaunch(request.ExePath, args, request.WorkingDirectory, 600000, request.Environment, request.LogPath));
        if (run.TimedOut) throw new InvalidOperationException("discovery list-tests timed out");
        var diagPath = NightlyDiagnosticLog.Latest(request.DiagnosticDirectory);
        if (string.IsNullOrWhiteSpace(diagPath)) throw new InvalidOperationException("missing diagnostic discovery log in " + request.DiagnosticDirectory);
        var nodes = PsValue.AsArray(PsValue.Prop(NightlyDiagnosticLog.Read(diagPath, "discovery"), "Nodes"));
        if (nodes.Length == 0) throw new InvalidOperationException("empty discovery");
        var hash = NightlyDiagnosticLog.AssemblyHash(string.IsNullOrWhiteSpace(request.AssemblyPath) ? request.ExePath : request.AssemblyPath);
        var document = NightlyDiagnosticLog.NewDiscoveryDocument(nodes, hash);
        NightlyDiagnosticLog.WriteDiscoveryDocument(request.OutputPath, document);
        return document;
    }

    /// <summary>The rows' "suite/chunk" keys of a schedule's entries.</summary>
    public static string[] Keys(object schedule) =>
        PsValue.AsArray(PsValue.Prop(schedule, "entries")).Select(e => PsValue.Str(PsValue.Prop(e, "suite")) + "/" + PsValue.Str(PsValue.Prop(e, "chunk"))).ToArray();
}
