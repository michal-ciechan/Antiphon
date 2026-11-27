using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 65 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the deploy-server2 command (Antiphon.Nightly's
/// Server2RollingDeploy, which replaced scripts/deploy-server2.ps1) in process. This world runs the C# port of the script's two
/// offline fakes, scripts/fixtures/c727-fake-http.ps1 (the runner and census HTTP) and c727-fake-verify.ps1 (the host cases, the
/// host jq prerequisite, the rollout lock, the recycle discovery and the two temp censuses; unit 68 moved the port to
/// Antiphon.Nightly.C727Fakes and deleted the PowerShell fakes), on the same state shapes: the generic
/// CARD-0727 scenario flags (tempDeployed, oldDraining, faultRunner, ...) and the CARD-1008 statuses/tasks shape. Each answer is
/// what the PowerShell fake printed (its values read and written back as ConvertFrom-Json and ConvertTo-Json did, a save at the
/// fake's depth), and each call appends the fake's trace row (also to TracePath). The checkout is a temporary directory (the
/// evidence root is under it), the clock a fake one that Sleep advances, and the timings the drivers' C727_TEST_* values (100 ms
/// waits, 5 ms polls). Nothing is started and nothing leaves the process.
/// </summary>
internal sealed class Server2RollingWorld : IServer2RollingWorld, IDisposable
{
    public const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public const string Token = "SENTINEL_C727_OPERATOR_TOKEN_1234567890";

    private readonly List<JsonObject> _trace = [];
    private readonly bool _ownsRoot;

    public Server2RollingWorld(JsonObject? state = null, string? root = null)
    {
        Fakes = new C727Fakes { State = state ?? new JsonObject(), Trace = Write };
        _ownsRoot = root == null;
        Root = root ?? Directory.CreateTempSubdirectory("c727-world-").FullName;
        TokenPath = Path.Combine(Root, "operator-token");
        File.WriteAllText(TokenPath, Token);
        Variables["ANTIPHON_API"] = "http://127.0.0.1:9";
        Variables["ANTIPHON_OPERATOR_TOKEN_FILE"] = TokenPath;
    }

    /// <summary>The driver's generic state for a scenario (Run-C727), with the phase's neighbours.</summary>
    public static JsonObject DriverState(string scenario, string phase = "all", JsonObject? set = null)
    {
        var state = new JsonObject
        {
            ["scenario"] = scenario, ["sha"] = Sha, ["clockMs"] = 0, ["tempDeployed"] = false, ["oldDeployed"] = false,
            ["oldDraining"] = false, ["tempDraining"] = false, ["tempRetiredAt"] = null, ["tempContainer"] = false, ["tempOffline"] = false,
            ["tempRedirectTo"] = "server2", ["tempRetireWhenIdle"] = true,
            ["faultRunner"] = "", ["faultField"] = "", ["faultKind"] = "", ["faultValue"] = null, ["failVerify"] = "",
        };
        set ??= new JsonObject();
        foreach (var (key, value) in set) state[key] = value?.DeepClone();
        // A normal rolling main replacement begins only after temp admission.
        if (phase == "redeploy-old")
        {
            if (!set.ContainsKey("tempContainer")) state["tempContainer"] = true;
            if (!set.ContainsKey("tempDeployed")) state["tempDeployed"] = true;
        }
        if ((Truthy(state["tempDeployed"]) || phase == "retire-temp") && !set.ContainsKey("tempContainer")) state["tempContainer"] = true;
        return state;
    }

    /// <summary>The fakes this world runs (Antiphon.Nightly.C727Fakes, the port of the two PowerShell fakes).</summary>
    public C727Fakes Fakes { get; }

    /// <summary>The fake's state (the PowerShell fake's C727_TEST_STATE file).</summary>
    public JsonObject State { get => Fakes.State; set => Fakes.State = value; }

    /// <summary>The checkout the command runs in (its .antiphon/rolling-server2 holds the evidence).</summary>
    public string Root { get; }

    public string TokenPath { get; }

    /// <summary>The trace file (C727_TEST_TRACE); null: kept in memory only.</summary>
    public string? TracePath { get; set; }

    /// <summary>The host's environment.</summary>
    public Dictionary<string, string?> Variables { get; } = new(StringComparer.Ordinal);

    /// <summary>The drivers' timings: C727_TEST_WAIT_MS=100, C727_TEST_POLL_MS=5, C727_TEST_LAND_WAIT_MS=100, the HTTP stub set.</summary>
    public Server2RollingTiming Timing { get; set; } = new() { WaitMs = 100, PollMs = 5, LandWaitMs = 100, StubbedReads = true };

    /// <summary>The installed jq path the proof names.</summary>
    public string HostJqDestination { get; set; } = "/usr/local/bin/jq";

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 26, 3, 0, 0, TimeSpan.Zero));

    /// <summary>Runs before each host case with its name and manifest path (the observer stubs the drivers wrapped around the fake).</summary>
    public Action<string, string>? OnCase { get; set; }

    /// <summary>Sees each runner request's and census read's full URL.</summary>
    public Action<string>? UrlObserver { get => Fakes.UrlObserver; set => Fakes.UrlObserver = value; }

    /// <summary>Answers a host case instead of the fake (its exit), null: the fake answers.</summary>
    public Func<string, string, int?>? CaseOverride { get; set; }

    /// <summary>Answers a census read before the fake does (a test's own HTTP schedule); null: the fake answers.</summary>
    public Func<string, Server2RollingHttpAnswer?>? ReadOverride { get; set; }

    /// <summary>The real transport for host jq and git (the Linux fixtures' fake ssh on PATH); null: the fake answers.</summary>
    public IServer2RollingWorld? Transport { get; set; }

    /// <summary>What the verifier children printed (the host cases' stdout) for the command's output.</summary>
    public string CaseOutput { get; set; } = "";

    /// <summary>Empties the trace (rows and file).</summary>
    public void ClearTrace()
    {
        _trace.Clear();
        if (TracePath != null && File.Exists(TracePath)) File.Delete(TracePath);
    }

    /// <summary>The trace rows of this world, in order.</summary>
    public JsonObject[] Trace => _trace.ToArray();

    private int _guid;

    /// <summary>The ids the command draws, in order (deterministic: 1, 2, 3 ... as 32 hex digits); null: random.</summary>
    public bool DeterministicIds { get; set; }

    private NightlyHost Host(StringWriter output, StringWriter error, Action<TimeSpan>? onSleep = null, Func<string, string?>? variables = null) => new()
    {
        Clock = Clock,
        Git = (_, _) => throw new InvalidOperationException("deploy-server2 runs git through its world"),
        Launcher = new RecordingLauncher(_ => throw new InvalidOperationException("deploy-server2 starts nothing on the launcher")),
        Processes = new NightlyProcessTable(),
        Probe = new NightlyHostProbe(),
        Out = output,
        Error = error,
        Trace = TextWriter.Null,
        Variables = variables ?? (name => Variables.TryGetValue(name, out var value) ? value : null),
        Sleep = wait => { onSleep?.Invoke(wait); Clock.Advance(wait); },
    };

    private Server2RollingSeams Seams() => new()
    {
        World = this,
        Timing = Timing,
        HostJqDestination = HostJqDestination,
        C994Now = () => Fakes.ClockNow() ?? Clock.GetUtcNow().UtcDateTime,
        C994Advance = Fakes.AdvanceClock,
        NewGuid = () => DeterministicIds ? new Guid(Convert.ToHexString(BitConverter.GetBytes(++_guid)).PadLeft(32, '0')) : Guid.NewGuid(),
    };

    public Server2RollingRun Run(params string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int exit;
        try
        {
            exit = Server2RollingDeploy.Run(Server2RollingOptions.Parse(arguments, Root), Host(output, error), new System.IO.Abstractions.FileSystem(), Seams());
        }
        catch (NightlyUsageException e)
        {
            error.WriteLine(e.Message);
            exit = 2;
        }
        return new Server2RollingRun(exit, output.ToString(), error.ToString(), Trace);
    }

    /// <summary>
    /// Unit 68: the same run through the W10 node harnesses' seams (Server2RollingStubOs, as the CLI binds them): both fakes in process
    /// on a state file and a trace file in Root, with the drivers' timings. Afterwards State is the state file and the trace the file's
    /// rows. Git still answers from this world (the seams' production fallback).
    /// </summary>
    public Server2RollingRun RunOnFiles(params string[] arguments)
    {
        var statePath = Path.Combine(Root, "c727-state.json");
        var tracePath = Path.Combine(Root, "c727-trace.jsonl");
        File.WriteAllText(statePath, State.ToJsonString() + Environment.NewLine);
        File.WriteAllText(tracePath, "");
        var variables = new Dictionary<string, string?>(Variables, StringComparer.Ordinal)
        {
            ["C727_TEST_HTTP_STUB"] = Server2RollingStubOs.FakeHttp, ["C727_TEST_VERIFY_STUB"] = Server2RollingStubOs.FakeVerify,
            ["C727_TEST_STATE"] = statePath, ["C727_TEST_TRACE"] = tracePath,
            ["C727_TEST_WAIT_MS"] = "100", ["C727_TEST_POLL_MS"] = "5", ["C727_TEST_LAND_WAIT_MS"] = "100",
        };
        var stub = new Server2RollingStubOs(n => variables.TryGetValue(n, out var v) ? v! : null!, this, Root);
        var output = new StringWriter();
        var error = new StringWriter();
        var host = Host(output, error, null, n => variables.TryGetValue(n, out var v) ? v : null);
        int exit;
        try
        {
            exit = Server2RollingDeploy.Run(Server2RollingOptions.Parse(arguments, Root), host, new System.IO.Abstractions.FileSystem(), new Server2RollingSeams
            {
                World = stub,
                Timing = stub.Timing,
                HostJqDestination = HostJqDestination,
                C994Now = () => stub.C994Now() ?? Clock.GetUtcNow().UtcDateTime,
                C994Advance = stub.C994Advance,
                NewGuid = () => DeterministicIds ? new Guid(Convert.ToHexString(BitConverter.GetBytes(++_guid)).PadLeft(32, '0')) : Guid.NewGuid(),
            });
        }
        catch (NightlyUsageException e)
        {
            error.WriteLine(e.Message);
            exit = 2;
        }
        State = JsonNode.Parse(File.ReadAllText(statePath))!.AsObject();
        var rows = File.ReadAllLines(tracePath).Where(l => l.Length > 0).Select(l => JsonNode.Parse(l)!.AsObject()).ToArray();
        return new Server2RollingRun(exit, output.ToString(), error.ToString(), rows);
    }

    /// <summary>One runner request on this world as a phase makes it (Server2RollingDeploy.ProbeRunnerRequest).</summary>
    public object ProbeRequest(string method, string runnerId, string suffix, PlainObject body, bool holdLift) =>
        Server2RollingDeploy.ProbeRunnerRequest(method, runnerId, suffix, body, holdLift, Sha, Root, Host(new StringWriter(), new StringWriter()),
            new System.IO.Abstractions.FileSystem(), Seams());

    /// <summary>The census alone on this world (the census probes); onSleep sees each wait before the clock advances.</summary>
    public Server2RollingCensusResult Probe(Server2RollingCensusProbe probe, Action<TimeSpan>? onSleep = null) =>
        Server2RollingDeploy.ProbeCensus(probe, Host(new StringWriter(), new StringWriter(), onSleep), new System.IO.Abstractions.FileSystem(), Seams());
    // ---- the fakes: Antiphon.Nightly.C727Fakes (unit 68 moved this world's port of c727-fake-http.ps1 and c727-fake-verify.ps1 there,
    // where the W10 node harnesses' seams run it too); this world adds the tests' hooks around it ----

    private static bool Truthy(JsonNode? node) => RollingPs.True(node == null ? null : RollingPs.Parse(node.ToJsonString()));

    private void Write(JsonObject row)
    {
        _trace.Add(row);
        if (TracePath != null) File.AppendAllText(TracePath, row.ToJsonString() + Environment.NewLine);
    }

    public Server2RollingHttpAnswer RunnerHttp(string method, string url, IReadOnlyDictionary<string, string> headers, string body) =>
        Fakes.RunnerHttp(method, url, body);

    public Server2RollingHttpAnswer ReadHttp(string path, string url, IReadOnlyDictionary<string, string> headers)
    {
        Fakes.ReadRow(path, url);
        var overridden = ReadOverride?.Invoke(path);
        if (overridden != null) return overridden;
        return C727Fakes.ReadAnswerHttp(Fakes.ReadAnswer(path));
    }

    public IServer2RollingLock EnterRolloutLock() => Fakes.EnterRolloutLock();

    public Server2RollingChild RecycleDiscover(Server2RollingCall call) => Fakes.RecycleDiscover();

    public Server2RollingChild HostJq(Server2RollingCall call) => Transport != null ? Transport.HostJq(call) : Fakes.HostJq(call);

    public Server2RollingChild TempProjectCensus(Server2RollingCall call) => Fakes.TempProjectCensus(call);

    public Server2RollingChild TempRetirementRead(Server2RollingCall call) => Fakes.TempRetirementRead(call);

    public int HostCase(string hostCase, string manifestPath, TextWriter output, TextWriter error)
    {
        OnCase?.Invoke(hostCase, manifestPath);
        if (CaseOutput.Length > 0) output.Write(CaseOutput);
        if (CaseOverride?.Invoke(hostCase, manifestPath) is int overridden) return overridden;
        return Fakes.HostCase(hostCase, manifestPath);
    }

    /// <summary>The cold marker reader (T-20: c973-marker-reader.sh through bash or WSL); null: it passes.</summary>
    public Func<string, JsonObject, int>? MarkerReader { get => Fakes.MarkerReader; set => Fakes.MarkerReader = value; }

    public Server2RollingChild Git(string repo, IReadOnlyList<string> arguments)
    {
        if (Transport != null) return Transport.Git(repo, arguments);
        if (GitAnswer != null) return GitAnswer(arguments);
        return new Server2RollingChild(0, "", "");
    }

    /// <summary>git's answer (the capture's git.cmd: STUB_GIT_HEAD and STUB_GIT_EXIT); null: no output, exit 0.</summary>
    public Func<IReadOnlyList<string>, Server2RollingChild>? GitAnswer { get; set; }

    /// <summary>The evidence directories this world's runs wrote.</summary>
    public string EvidenceRoot => Path.Combine(Root, ".antiphon", "rolling-server2");

    public void Dispose()
    {
        if (_ownsRoot && Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}

/// <summary>One in-process run: the exit code, both streams and the fake trace.</summary>
internal sealed record Server2RollingRun(int Exit, string Stdout, string Stderr, JsonObject[] Trace)
{
    /// <summary>stdout then stderr, as the drivers read a child's two streams.</summary>
    public string Output => Stdout + Stderr;

    public JsonObject[] Cases => Trace.Where(t => t["kind"]?.GetValue<string>() == "case").ToArray();

    public JsonObject[] Posts => Trace.Where(t => t["kind"]?.GetValue<string>() == "http" && t["method"]?.GetValue<string>() == "POST").ToArray();

    public bool HasCase(string name) => Cases.Any(c => c["name"]?.GetValue<string>() == name);

    public int IndexOf(JsonObject row) => Array.IndexOf(Trace, row);

    /// <summary>The run id (c727 and 12 hex) the output names, '' without one.</summary>
    public string RunId => Regex.Match(Output, "c727[0-9a-f]{12}").Value;

    public override string ToString() => "exit=" + Exit + "\n" + Stdout + "\n--- stderr\n" + Stderr;
}
