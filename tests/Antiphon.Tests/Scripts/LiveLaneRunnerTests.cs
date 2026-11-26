using System.Collections.Specialized;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Checkpoints;
using Antiphon.LiveLane;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1545 offline admission contracts; boundaries never contact a provider. W2 slice S21: the cases of the deleted
/// scripts/test-live-lane-run.ps1 run in process against Antiphon.LiveLane (LiveLaneRun, LiveLaneInputDigest) with
/// fixture adapters, the real shared native-run lock under a scratch coordination root, and a virtual lane clock. The
/// protected-input pin is checked here (WrapperOnlyPaidManifest/full-inputs): it is LiveLaneInputDigest.Pinned, printed by
/// scripts/antiphon live-lane-input-digest.
/// </summary>
[Category("Integration")]
public sealed class LiveLaneRunnerTests
{
    private static readonly string Repo = DelegateScriptRunner.RepoRoot;

    // The CLI's census reader (scripts/lib/Antiphon.Scripts/LiveLaneInputCensusReader.cs, compiled into this assembly).
    private static readonly LiveLaneCensusReader CensusReader = global::Antiphon.Scripts.LiveLaneInputCensusReader.Read;

    // The old harness's fixture (New-Fixture): an inert roster, fixture adapters that trace every call, and six seeded
    // legacy state files that no run may touch.
    private sealed class Fixture
    {
        private static readonly string[] LegacyState =
            ["last-run.json", "last-complete-green.json", "last-monitor.json", "interim-qualification-receipt.json", "rc-gate.json", "rc-authority.json"];

        public Fixture(C1545Scratch scratch)
        {
            Root = scratch.NewDirectory();
            foreach (var name in LegacyState)
            {
                var path = Path.Combine(Root, name);
                File.WriteAllText(path, "seeded-state-" + name);
                State[path] = File.ReadAllBytes(path);
            }
            Request = new LiveLaneRequest
            {
                Mode = "Fixture", Lane = "fixture-live", RunId = "run-A", Attempt = 1, Source = new string('a', 40), Uids = ["fixture.A.0"],
                Executable = "owned-fixture-executable", Profile = "owned-fixture-profile", Commission = "fixture-commission", BudgetSeconds = 120,
                CoordinationRoot = Path.Combine(Root, "coord"), ResultsRoot = Path.Combine(Root, "diagnostic"),
                Inherited = new Dictionary<string, string> { ["PATH"] = "fixture-path", ["ANTHROPIC_API_KEY"] = "synthetic-not-a-secret", ["ANTIPHON_TASK_TOKEN"] = "synthetic-task" }
            };
            Boundary = new LiveLaneBoundary
            {
                Candidate = r => new LiveLaneCandidate { Sha = r.Source, Clean = true },
                Clock = () => Tick,
                Trace = Trace.Add,
                Slot = r =>
                {
                    var record = NightlyLockRecord.Read(LiveLaneNativeLockSeam.LockPath(r.CoordinationRoot));
                    Trace.Add(record?.RunId == r.RunId ? "slot:locked" : "slot:unlocked");
                    return !DenySlot;
                },
                WriteContext = WriteContext,
                Start = (context, start, manifest) =>
                {
                    Starts++;
                    Trace.Add("start:checkpoint-A");
                    Admitted = context;
                    StartInfo = start;
                    if (Fault == "launch-failure") throw new InvalidOperationException("injected before creation");
                    if (!File.Exists(manifest)) throw new InvalidOperationException("no materialized manifest");
                    return new LiveLaneChild { RunId = "checkpoint-A", Children = ["child-A", "child-B"] };
                },
                Wait = id =>
                {
                    Waits++;
                    Trace.Add("wait:" + id);
                    if (Fault == "wait-failure") throw new InvalidOperationException("injected");
                    Tick += Deadline ? 80 : 1;
                    return Waits <= 2 || Deadline ? 75 : 0;
                },
                ReadTerminal = id =>
                {
                    Trace.Add("terminal:" + id);
                    if (Fault == "terminal-read") throw new InvalidOperationException("injected");
                    return new JsonObject
                    {
                        ["SchemaVersion"] = 1, ["RunId"] = id, ["ChildExit"] = 0, ["Complete"] = true, ["Evidence"] = "Complete",
                        ["Lane"] = "fixture-live", ["Attempt"] = 1, ["Source"] = new string('a', 40), ["Build"] = "Succeeded", ["Platform"] = "Fixture",
                        ["Authority"] = "diagnostic", ["InputBefore"] = "input-A", ["InputAfter"] = "input-A", ["ExecutableBefore"] = "exe-A",
                        ["ExecutableAfter"] = "exe-A", ["Outcome"] = "Passed",
                        ["Rows"] = LiveLaneJsonFixtures.Array(new JsonObject { ["Uid"] = "fixture.A.0", ["State"] = "Passed", ["Setup"] = "Ready" })
                    };
                },
                Stop = child => Trace.Add("stop:" + child),
                Join = (child, seconds) => { Trace.Add("join:" + child); return JoinOk; },
                WriteReceipt = (path, receipt) =>
                {
                    Trace.Add("receipt");
                    if (Fault == "receipt-write") throw new InvalidOperationException("injected");
                    File.WriteAllText(path, receipt.ToJson());
                },
                BeforeRelease = r =>
                {
                    if (Change != "foreign-lock") return;
                    var path = LiveLaneNativeLockSeam.LockPath(r.CoordinationRoot);
                    var owner = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                    owner["runId"] = "foreign";
                    owner["processStartedAt"] = "2000-01-01T00:00:00Z";
                    Foreign = owner.ToJsonString();
                    File.WriteAllText(path, Foreign);
                }
            };
        }

        public string Root { get; }
        public List<string> Trace { get; } = [];
        public double Tick { get; set; }
        public int Waits { get; private set; }
        public int Starts { get; private set; }
        public string Fault { get; set; } = "";
        public string Change { get; set; } = "";
        public bool JoinOk { get; set; } = true;
        public bool DenySlot { get; set; }
        public bool Deadline { get; set; }
        public string Foreign { get; private set; } = "";
        public Dictionary<string, byte[]> State { get; } = new(StringComparer.Ordinal);
        public JsonNode? Admitted { get; private set; }
        public LiveLaneChildStart? StartInfo { get; private set; }
        public LiveLaneRequest Request { get; }
        public LiveLaneBoundary Boundary { get; }
        public LiveLaneNativeLockSeam Lock { get; } = new();

        private string WriteContext(string path, string json)
        {
            Trace.Add("context-write");
            if (Fault == "context-write") throw new InvalidOperationException("injected");
            var c = JsonNode.Parse(json)!.AsObject();
            var owner = c["owner"]!.AsObject();
            switch (Change)
            {
                case "context-missing": return "";
                case "context-lane": c["lane"] = "foreign"; break;
                case "context-uid": c["uids"] = LiveLaneJsonFixtures.Array("foreign.0"); break;
                case "context-source": c["source"] = new string('b', 40); break;
                case "context-executable": c["executable"] = "foreign"; break;
                case "context-profile": c["profile"] = "foreign"; break;
                case "context-tuple-pid": owner["pid"] = owner["pid"]!.GetValue<int>() + 1; break;
                case "context-tuple-runId": owner["runId"] = "foreign"; break;
                case "context-tuple-lane": owner["lane"] = "foreign"; break;
                case "context-tuple-start": owner["processStartedAt"] = "2000-01-01T00:00:00Z"; break;
                case "context-owner":
                    var lockPath = LiveLaneNativeLockSeam.LockPath(Request.CoordinationRoot);
                    var record = JsonNode.Parse(File.ReadAllText(lockPath))!.AsObject();
                    record["runId"] = "foreign";
                    Foreign = record.ToJsonString();
                    File.WriteAllText(lockPath, Foreign);
                    break;
                case "context-forged": path += ".unissued"; break;
            }
            File.WriteAllText(path, c.ToJsonString());
            return path;
        }

        /// <summary>Run-Fixture: one admission run; the seeded legacy state is byte for byte unchanged after it.</summary>
        public LiveLaneRunResult Run()
        {
            var result = LiveLaneRun.Run(Request, Boundary, Lock);
            foreach (var (path, bytes) in State)
                File.ReadAllBytes(path).ShouldBe(bytes, "legacy state changed: " + Path.GetFileName(path));
            return result;
        }

        public bool NoStarts => Starts == 0 && !Trace.Any(t => t.StartsWith("slot:", StringComparison.Ordinal));

        public bool LockExists => File.Exists(LiveLaneNativeLockSeam.LockPath(Request.CoordinationRoot));
    }

    [Test]
    public void LockBeforeSlot()
    {
        var cases = new C1545Cases(nameof(LockBeforeSlot));
        using var scratch = new C1545Scratch("c1545-run-");
        var q = new Fixture(scratch);
        q.Run();
        cases.Check("ordered", q.Starts == 1 && q.Trace.Contains("slot:locked") && q.Trace.IndexOf("slot:locked") < q.Trace.IndexOf("start:checkpoint-A"));

        q = new Fixture(scratch);
        var foreign = LiveLaneNativeLockSeam.Foreign();
        var owner = foreign.Enter(q.Request.CoordinationRoot, "foreign", "fixture-live");
        var bytes = File.ReadAllText(owner.Path);
        var r = q.Run();
        cases.Check("busy", r.ExitCode == 21 && q.NoStarts && File.ReadAllText(owner.Path) == bytes);
        foreign.Exit(q.Request.CoordinationRoot, "foreign");

        q = new Fixture(scratch) { DenySlot = true };
        r = q.Run();
        cases.Check("slot-timeout", r.ExitCode == 4 && q.Trace.Contains("slot:locked") && q.Starts == 0 && !q.LockExists);

        foreach (var id in new[] { "commission", "budget" })
        {
            q = new Fixture(scratch);
            if (id == "commission") q.Request.Commission = ""; else q.Request.BudgetSeconds = 0;
            r = q.Run();
            cases.Check(id, r.ExitCode == 20 && q.NoStarts);
        }
        foreach (var id in new[] { "candidate-sha", "candidate-dirty" })
        {
            q = new Fixture(scratch);
            q.Boundary.Candidate = id == "candidate-sha"
                ? _ => new LiveLaneCandidate { Sha = new string('b', 40), Clean = true }
                : request => new LiveLaneCandidate { Sha = request.Source, Clean = false };
            r = q.Run();
            cases.Check(id, r.ExitCode == 2 && q.NoStarts);
        }
        cases.Complete("ordered", "busy", "slot-timeout", "commission", "budget", "candidate-sha", "candidate-dirty");
    }

    [Test]
    public async Task WrapperOnlyPaidManifest()
    {
        var yaml = await File.ReadAllTextAsync(Path.Combine(Repo, "tests/live-lane-checkpoints/live-claude-core-windows.yaml"));
        var rejected = false;
        try { ManifestLoader.LoadYaml(yaml, Repo); }
        catch (ManifestValidationException ex) { rejected = ex.Field == "schemaVersion"; }
        rejected.ShouldBeTrue("The real manifest loader must refuse the wrapper spec before any executor.");

        var cases = new C1545Cases(nameof(WrapperOnlyPaidManifest));
        using var scratch = new C1545Scratch("c1545-run-");
        var q = new Fixture(scratch);
        var r = q.Run();
        var admitted = q.Admitted;
        cases.Check("valid-context", q.Starts == 1 && LiveLaneValues.Text(admitted?["lane"]) == "fixture-live" && LiveLaneValues.Text(admitted?["source"]) == new string('a', 40)
            && LiveLaneValues.Text(admitted?["uids"]?[0]) == "fixture.A.0" && LiveLaneValues.Text(admitted?["owner"]?["runId"]) == "run-A"
            && LiveLaneValues.Text(admitted?["attempt"]) == "1" && r.TerminalComplete);
        foreach (var id in new[] { "context-missing", "context-lane", "context-uid", "context-source", "context-executable", "context-profile", "context-owner",
                     "context-forged", "context-tuple-pid", "context-tuple-runId", "context-tuple-lane", "context-tuple-start" })
        {
            q = new Fixture(scratch) { Change = id };
            r = q.Run();
            var code = 2;
            var reason = "context-mismatch";
            if (id == "context-missing") { code = 20; reason = "lane-context-required"; }
            if (id == "context-owner" || id.StartsWith("context-tuple-", StringComparison.Ordinal)) reason = "context-stale";
            if (id == "context-forged") reason = "context-unissued";
            cases.Check(id, r.ExitCode == code && r.Reason == reason && q.Starts == 0, r.Reason);
        }
        q = new Fixture(scratch);
        q.Request.Mode = "Native";
        r = q.Run();
        cases.Check("native-disabled", r.ExitCode == 20 && r.Reason == "native-unqualified" && q.NoStarts);

        var digest = LiveLaneInputDigest.Digest(Repo);
        var full = FullInputs(scratch, Repo, out var after);
        cases.Check("full-inputs", full, $"census inputs digest {digest} (its CRLF copy must match), pinned {LiveLaneInputDigest.Pinned}, C680/ordinary"
            + " sentinels Required and live sentinel Excluded; if this census/policy input change is deliberate and reviewed, set"
            + " LiveLaneInputDigest.Pinned in scripts/lib/Antiphon.LiveLane/LiveLaneInputDigest.cs to the value printed by:"
            + " scripts/antiphon live-lane-input-digest");
        after ??= Census(Repo);

        var before = Path.Combine(scratch.Root, "pinned");
        CopyProtectedInputs(Repo, before, "\n");
        var policyPath = Path.Combine(before, LiveLaneInputDigest.PolicyPath);
        var original = File.ReadAllText(policyPath);
        AddPolicyExclusion(before);
        cases.Check("policy-edit", Census(before).ToJson() != after.ToJson() && !LiveLaneInputDigest.MatchesPin(before));
        File.WriteAllText(policyPath, original);
        // The census is compiled, so no edit to the policy reader's source or to the compiled census source changes a
        // census read in this process: only the pin can see either.
        File.AppendAllText(Path.Combine(before, "scripts/lib/Antiphon.Nightly/NightlyPolicy.cs"), "\n// fixture edit\n");
        var readerEdit = !LiveLaneInputDigest.MatchesPin(before);
        var coreRoot = Path.Combine(scratch.Root, "pinned-core");
        CopyProtectedInputs(Repo, coreRoot, "\n");
        var coreEdit = LiveLaneInputDigest.MatchesPin(coreRoot);
        File.AppendAllText(Path.Combine(coreRoot, "scripts/lib/Antiphon.Nightly/NightlyCore.cs"), "\n// fixture edit\n");
        coreEdit = coreEdit && !LiveLaneInputDigest.MatchesPin(coreRoot);
        cases.Check("reader-edit", readerEdit && coreEdit);

        // Committed edits, as a clean CI, nightly or rc checkout sees them (LF text), each with a recomputed policyHash.
        // The first excludes an unrelated class: no sentinel moves, so only the pin catches it. The second adds the
        // reviewer's C680 exclusion on top, which the sentinels catch as well.
        var committed = Path.Combine(scratch.Root, "committed");
        CopyProtectedInputs(Repo, committed, "\n");
        AddPolicyExclusion(committed, "Antiphon.Tests.Fixture.UnrelatedTests");
        var neutralRed = !FullInputs(scratch, committed, out _) && LiveLaneInputDigest.SentinelsHold(Census(committed));
        AddPolicyExclusion(committed);
        cases.Check("committed-edit", neutralRed && !FullInputs(scratch, committed, out _) && !LiveLaneInputDigest.SentinelsHold(Census(committed)));

        // A CRLF checkout of the unchanged inputs keeps the verdict.
        var crlf = Path.Combine(scratch.Root, "crlf-checkout");
        CopyProtectedInputs(Repo, crlf, "\r\n");
        cases.Check("crlf-inputs", LiveLaneInputDigest.MatchesPin(crlf) && Census(crlf).ToJson() == after.ToJson());

        // C680 moved from Required to Excluded must not satisfy the sentinels.
        var moved = new LiveLaneInputCensus(after.Hash, after.Required.Where(uid => uid != "sentinel-c680"), after.Excluded.Append("sentinel-c680"));
        cases.Check("sentinel-moved", LiveLaneInputDigest.SentinelsHold(after) && !LiveLaneInputDigest.SentinelsHold(moved));

        q = new Fixture(scratch);
        q.Run();
        cases.Check("legacy-state", q.Starts == 1 && q.Trace.Contains("receipt"));
        cases.Complete("valid-context", "context-missing", "context-lane", "context-uid", "context-source", "context-executable", "context-profile",
            "context-owner", "context-forged", "context-tuple-pid", "context-tuple-runId", "context-tuple-lane", "context-tuple-start",
            "native-disabled", "full-inputs", "policy-edit", "reader-edit", "committed-edit", "crlf-inputs", "sentinel-moved", "legacy-state");
    }

    [Test]
    public void ChildEnvironmentIsScoped()
    {
        var cases = new C1545Cases(nameof(ChildEnvironmentIsScoped));
        using var scratch = new C1545Scratch("c1545-run-");
        var windows = OperatingSystem.IsWindows();
        var inherited = new Dictionary<string, string>
        {
            ["PATH"] = "fixture-path", ["ANTHROPIC_API_KEY"] = "synthetic", ["ANTIPHON_TASK_TOKEN"] = "synthetic", ["CLAUDE_CONFIG_DIR"] = "foreign", ["HOME"] = "foreign"
        };
        var selected = new Dictionary<string, string>
        {
            ["CLAUDE_CONFIG_DIR"] = "owned-profile", ["ANTIPHON_LIVE_LANE_CONTEXT"] = "owned-context", ["ANTIPHON_LIVE_CLAUDE_EXE"] = "owned-executable"
        };
        var start = LiveLaneRun.ChildStart(inherited, selected, scratch.Root, windows);
        var env = start.Environment;
        cases.Check("allowlist", env["PATH"] == "fixture-path" && env["CLAUDE_CONFIG_DIR"] == "owned-profile" && env["ANTIPHON_LIVE_LANE_CONTEXT"] == "owned-context"
            && !env.ContainsKey("ANTHROPIC_API_KEY") && !env.ContainsKey("ANTIPHON_TASK_TOKEN") && !env.ContainsKey("HOME")
            && start.FileName == "fixture-only-no-executable" && start.Arguments.Count == 0);

        var ok = true;
        string[] sessionKeys = ["CLAUDE_CODE_CHILD_SESSION", "CLAUDE_CODE_SESSION_ID", "CLAUDE_CODE_BRIDGE_SESSION_ID", "CLAUDECODE"];
        foreach (var key in sessionKeys)
        {
            inherited[key] = "hostile-inherited-session";
            foreach (var state in new[] { "absent", "empty", "value" })
            {
                var values = new Dictionary<string, string>(selected);
                if (state != "absent") values[key] = state == "empty" ? "" : "fixture-session";
                start = LiveLaneRun.ChildStart(inherited, values, scratch.Root, windows);
                ok = state == "absent"
                    ? ok && !start.Environment.ContainsKey(key)
                    : ok && start.Environment.TryGetValue(key, out var value) && value == values[key];
            }
        }
        cases.Check("empty", ok);

        // Presence-aware snapshots of the parent's live-lane and session variables, compared without printing values:
        // a success, a refusal and a throw leave them as they were.
        static Dictionary<string, string> Snapshot()
        {
            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (System.Collections.DictionaryEntry pair in Environment.GetEnvironmentVariables())
            {
                var name = (string)pair.Key;
                if (name.StartsWith("CLAUDE", StringComparison.OrdinalIgnoreCase) || name.StartsWith("ANTIPHON_LIVE", StringComparison.OrdinalIgnoreCase))
                    snapshot[name] = (string?)pair.Value ?? "";
            }
            return snapshot;
        }
        var beforeRuns = Snapshot();
        foreach (var kind in new[] { "success", "refusal", "throw" })
        {
            var q = new Fixture(scratch);
            if (kind == "refusal") q.Request.Commission = "";
            if (kind == "throw") q.Fault = "wait-failure";
            q.Run();
        }
        var afterRuns = Snapshot();
        cases.Check("parent", beforeRuns.Count == afterRuns.Count && beforeRuns.All(pair => afterRuns.TryGetValue(pair.Key, out var v) && v == pair.Value));

        // The copied build-driver policy is the shared one (scripts/lib/BuildDriverEnvironment.cs, CARD-1506): all six
        // overrides on Windows, none on Linux (the child is never dotnet).
        var shared = typeof(Antiphon.Checkpoints.ProcessDriver).Assembly.GetType("Antiphon.Build.BuildDriverEnvironment")!;
        dynamic policy = Activator.CreateInstance(shared)!;
        Dictionary<string, string> expected = policy.GetOverrides(new StringDictionary(), true);
        var windowsStart = LiveLaneRun.ChildStart(inherited, selected, scratch.Root, true);
        var linuxStart = LiveLaneRun.ChildStart(inherited, selected, scratch.Root, false);
        cases.Check("build-driver", expected.Count == 6 && LiveLaneRun.BuildDriverOverrides(true).OrderBy(p => p.Key).SequenceEqual(expected.OrderBy(p => p.Key))
            && expected.All(pair => windowsStart.Environment.TryGetValue(pair.Key, out var v) && v == pair.Value)
            && expected.Keys.All(key => !linuxStart.Environment.ContainsKey(key)));
        cases.Complete("allowlist", "empty", "parent", "build-driver");
    }

    [Test]
    public void DeadlineJoinsOwnedChildren()
    {
        var cases = new C1545Cases(nameof(DeadlineJoinsOwnedChildren));
        using var scratch = new C1545Scratch("c1545-run-");
        var q = new Fixture(scratch);
        var r = q.Run();
        cases.Check("wait75", q.Starts == 1 && q.Waits == 3 && q.Trace.Count(t => t == "wait:checkpoint-A") == 3
            && q.Trace.IndexOf("receipt") > q.Trace.IndexOf("terminal:checkpoint-A") && r.TerminalComplete);

        q = new Fixture(scratch) { Deadline = true };
        r = q.Run();
        cases.Check("deadline", r.ExitCode == 20 && r.Reason == "deadline"
            && Regex.IsMatch(string.Join(",", q.Trace), "stop:child-A,stop:child-B,join:child-A,join:child-B,(?:receipt,)?release") && !q.LockExists,
            string.Join(",", q.Trace));

        q = new Fixture(scratch) { JoinOk = false };
        r = q.Run();
        cases.Check("cleanup-unknown", r.ExitCode == 20 && r.Reason == "cleanup-incomplete" && q.LockExists && !r.TerminalComplete);

        foreach (var fault in new[] { "context-write", "launch-failure", "wait-failure", "terminal-read", "receipt-write" })
        {
            q = new Fixture(scratch) { Fault = fault };
            r = q.Run();
            var marker = fault switch
            {
                "receipt-write" => "receipt",
                "terminal-read" => "terminal:checkpoint-A",
                "launch-failure" => "start:checkpoint-A",
                "wait-failure" => "wait:checkpoint-A",
                _ => "context-write"
            };
            var ok = r.ExitCode != 0 && !r.TerminalComplete && q.Starts <= 1 && !q.LockExists && q.Trace.Contains(marker);
            if (fault is "wait-failure" or "terminal-read" or "receipt-write") ok = ok && q.Trace.Contains("join:child-A") && q.Trace.Contains("join:child-B");
            cases.Check(fault, ok, string.Join(",", q.Trace));
        }

        q = new Fixture(scratch) { Change = "foreign-lock" };
        q.Run();
        var path = LiveLaneNativeLockSeam.LockPath(q.Request.CoordinationRoot);
        cases.Check("foreign-lock", q.Trace.Contains("join:child-B") && File.Exists(path) && File.ReadAllText(path) == q.Foreign);
        cases.Complete("wait75", "deadline", "cleanup-unknown", "context-write", "launch-failure", "wait-failure", "terminal-read", "receipt-write", "foreign-lock");
    }

    // The exact line and exit code run-live-lane.ps1's native mode gave (captured before it was deleted, with and without
    // its six parameters): the CLI command live-lane-run keeps both, and its flags cannot change the answer. W2 S22 added
    // the last field, the Claude version the run recorded: always null here, since the native mode asks nothing.
    private const string NativeLine = "{\"ExitCode\":20,\"Reason\":\"native-unqualified\",\"DiagnosticOnly\":true,\"GateCredit\":false,\"TerminalComplete\":false,"
        + "\"RawChildExit\":null,\"CheckpointRunId\":null,\"Classification\":\"INCONCLUSIVE\",\"Rows\":[],\"RawChildOutcome\":null,\"ChildExits\":[],\"Probes\":[],\"Claude\":null}";

    [Test]
    public void NativeModeIsUnqualified()
    {
        foreach (var argv in new[]
                 {
                     Array.Empty<string>(),
                     ["--lane", "live-claude-core-windows", "--candidate-ref", "HEAD", "--expected-sha", new string('a', 40), "--manifest", "m.json",
                      "--coordination-root", "C:/nowhere/coord", "--results-root", "C:/nowhere/results"]
                 })
        {
            var output = new StringWriter();
            LiveLaneRunCommand.Run(LiveLaneRunOptions.Parse(argv), output).ShouldBe(20);
            output.ToString().ShouldBe(NativeLine + Environment.NewLine);
        }
        foreach (var bad in new[] { new[] { "--bogus" }, ["--lane"], ["--lane", "a", "--lane", "b"], ["positional"], ["-Lane", "a"], ["--claude"] })
            Should.Throw<LiveLaneUsageException>(() => LiveLaneRunOptions.Parse(bad));
        // S22: --claude names the executable (ANTIPHON_LIVE_CLAUDE_EXE is the CLI's default); the native mode still asks
        // it nothing, so the record is the same line.
        LiveLaneRunOptions.Parse(["--claude", "C:/tools/claude.exe"], "C:/env/claude.exe").Claude.ShouldBe("C:/tools/claude.exe");
        LiveLaneRunOptions.Parse([], "C:/env/claude.exe").Claude.ShouldBe("C:/env/claude.exe");
        LiveLaneRunOptions.Parse([], " ").Claude.ShouldBeNull();
        var named = new StringWriter();
        LiveLaneRunCommand.Run(LiveLaneRunOptions.Parse(["--claude", "C:/tools/claude.exe"]), named).ShouldBe(20);
        named.ToString().ShouldBe(NativeLine + Environment.NewLine);
    }

    // S22: the fixture run asks the executable it targets for its version through the process seam, before it starts
    // anything, and records it (the receipt writer sees it); without a seam it records nothing, and a failed probe is unknown.
    [Test]
    public void RunRecordsTheClaudeVersion()
    {
        var cases = new C1545Cases(nameof(RunRecordsTheClaudeVersion));
        using var scratch = new C1545Scratch("c1545-run-version-");
        var q = new Fixture(scratch);
        var calls = new List<string>();
        q.Boundary.Process = new VersionProcess(calls, 0, "2.1.3 (Claude Code)\n");
        var r = q.Run();
        cases.Check("recorded", r.Claude is { Version: "2.1.3", Raw: "2.1.3 (Claude Code)" } && calls.SequenceEqual(["owned-fixture-executable --version"])
            && r.ToJson().EndsWith(",\"Claude\":{\"Version\":\"2.1.3\",\"Raw\":\"2.1.3 (Claude Code)\"}}", StringComparison.Ordinal), r.ToJson());
        q = new Fixture(scratch);
        r = q.Run();
        cases.Check("not-asked", r.Claude == null && r.ToJson().EndsWith(",\"Claude\":null}", StringComparison.Ordinal), r.ToJson());
        q = new Fixture(scratch);
        q.Boundary.Process = new VersionProcess(calls, 1, "boom");
        r = q.Run();
        cases.Check("failed-probe", r.Claude is { Version: "unknown", Raw: null } && r.ExitCode == new Fixture(scratch).Run().ExitCode, r.ToJson());
        cases.Complete("recorded", "not-asked", "failed-probe");
    }

    private sealed class VersionProcess(List<string> calls, int exit, string stdout) : ILiveLaneProcess
    {
        public LiveLaneProcessResult Run(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout)
        {
            calls.Add(fileName + " " + string.Join(" ", arguments));
            return new LiveLaneProcessResult(exit, stdout);
        }
    }

    [Test]
    public void InputDigestCommandPrintsThePin()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        LiveLaneInputDigestCommand.Run(LiveLaneInputDigestOptions.Parse([], Repo), CensusReader, output, error).ShouldBe(0);
        output.ToString().ShouldBe(LiveLaneInputDigest.Pinned + Environment.NewLine);
        output = new StringWriter();
        LiveLaneInputDigestCommand.Run(LiveLaneInputDigestOptions.Parse(["--root", Repo, "--check"], null), CensusReader, output, error).ShouldBe(0);
        output.ToString().ShouldBe(LiveLaneInputDigest.Pinned + Environment.NewLine);
        error.ToString().ShouldBeEmpty();

        using var scratch = new C1545Scratch("c1545-pin-");
        var edited = Path.Combine(scratch.Root, "edited");
        CopyProtectedInputs(Repo, edited, "\n");
        File.AppendAllText(Path.Combine(edited, LiveLaneInputDigest.ProtectedInputs[0]), "\n// fixture edit\n");
        output = new StringWriter();
        LiveLaneInputDigestCommand.Run(LiveLaneInputDigestOptions.Parse(["--root", edited, "--check"], null), CensusReader, output, error).ShouldBe(1);
        error.ToString().ShouldContain("is not the pin " + LiveLaneInputDigest.Pinned);
        var missing = Path.Combine(scratch.Root, "missing");
        Directory.CreateDirectory(missing);
        output = new StringWriter();
        LiveLaneInputDigestCommand.Run(LiveLaneInputDigestOptions.Parse(["--root", missing], null), CensusReader, output, error).ShouldBe(0);
        output.ToString().ShouldBe(Environment.NewLine);
        foreach (var bad in new[] { new[] { "--root" }, ["--root", ""], ["--check", "--check"], ["--digest"] })
            Should.Throw<LiveLaneUsageException>(() => LiveLaneInputDigestOptions.Parse(bad, Repo));
    }

    private static LiveLaneInputCensus Census(string root) => LiveLaneInputDigest.Census(root, CensusReader);

    /// <summary>Test-FullInputs: the pin covers both line endings, and a census of the root keeps its sentinels.</summary>
    private static bool FullInputs(C1545Scratch scratch, string root, out LiveLaneInputCensus? census)
    {
        census = null;
        if (!LiveLaneInputDigest.MatchesPin(root)) return false;
        var crlf = Path.Combine(scratch.Root, "crlf-" + Guid.NewGuid().ToString("N"));
        CopyProtectedInputs(root, crlf, "\r\n");
        if (!LiveLaneInputDigest.MatchesPin(crlf)) return false;
        census = Census(root);
        return LiveLaneInputDigest.SentinelsHold(census);
    }

    /// <summary>Copy-ProtectedInputs: every protected input, its text with this line ending.</summary>
    private static void CopyProtectedInputs(string from, string to, string newLine)
    {
        foreach (var path in LiveLaneInputDigest.ProtectedInputs)
        {
            var text = LiveLaneInputDigest.LfText(File.ReadAllText(Path.Combine(from, path)));
            var destination = Path.Combine(to, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, text.Replace("\n", newLine));
        }
    }

    /// <summary>A real candidate policy edit: one rc exclusion, with the policy hash recomputed.</summary>
    private static void AddPolicyExclusion(string root, string className = "Antiphon.LiveClaude.Tests.ClaudeDialogWatchdogCanaryTests")
    {
        var path = Path.Combine(root, LiveLaneInputDigest.PolicyPath);
        var policy = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        policy["profiles"]!["rc"]!["exclusions"]!.AsArray()
            .Add(new JsonObject { ["suite"] = "antiphon", ["class"] = className, ["reason"] = "fixture", ["owner"] = "CARD-1545" });
        policy["policyHash"] = NightlyPolicy.HashText(policy.ToJsonString());
        File.WriteAllText(path, policy.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
