using System.Text.Json;
using Antiphon.Nightly;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S12: one chunk run's world for NightlyChunkRunner in-process (the twin of a C487 seams run of one suite):
/// a NightlyFixture host over a RecordingLauncher (FakeTestHost.NativePass by default), the shipped policy, a log root
/// and a timing root, and the still-PowerShell collaborators recorded (the executed observations, the active-chunk
/// identities, the discovery requests).
/// </summary>
public sealed class NightlyChunkWorld : IDisposable
{
    /// <summary>The one passing TRX row of the default fake host (its class qualified, as the discovery node's namespace and type make it).</summary>
    public static readonly NightlyTrxRow[] Rows = [new("uid-1", "Ns.SampleClass", "SampleMethod")];

    /// <summary>Its discovery node.</summary>
    public static readonly NightlyTestNode[] Nodes = [new("uid-1", "SampleClass", "SampleMethod", "Ns")];

    private static readonly Lazy<PolicyFile> ShippedPolicy = new(NightlyPlanCases.Policy);

    /// <summary>A world whose launches the behaviour answers (null: a passing native host writing Rows and Nodes).</summary>
    public NightlyChunkWorld(Func<NightlyLaunch, NightlyLaunchResult>? behaviour = null, string prefix = "s12-chunk")
    {
        Fixture = NightlyFixture.Create(prefix);
        Launcher = new RecordingLauncher(behaviour ?? FakeTestHost.NativePass(Rows, Nodes));
        Host = Fixture.Host(launcher: Launcher);
        LogRoot = Fixture.PathOf("logs");
        TimingRoot = Fixture.PathOf("timing-root");
        Directory.CreateDirectory(LogRoot);
    }

    public NightlyFixture Fixture { get; }
    public RecordingLauncher Launcher { get; }
    public NightlyHost Host { get; }
    public string LogRoot { get; }
    public string TimingRoot { get; }
    public List<NightlyExecutedObservation> Published { get; } = [];
    public List<NightlyActiveChunk> Active { get; } = [];
    public List<NightlyDiscoveryRequest> Discoveries { get; } = [];

    /// <summary>The shipped policy (tests/test-execution-policy.json).</summary>
    public static PolicyFile Policy => ShippedPolicy.Value;

    /// <summary>The run invocation id every chunk names.</summary>
    public const string InvocationId = "s12-run";

    /// <summary>A nightly-profile Windows run of this world, green so far.</summary>
    public NightlyChunkContext Context() => new()
    {
        Host = Host,
        RepoRoot = Fixture.Root,
        LogRoot = LogRoot,
        TimingRoot = TimingRoot,
        PhaseTimingDirectory = TimingRoot,
        InvocationId = InvocationId,
        Attempt = "1",
        Profile = "nightly",
        Sha = new string('a', 40),
        GitRef = "origin/master",
        Policy = Policy.Object,
        PolicyHash = Policy.Hash,
        Platform = "windows",
        StartedAt = DateTime.UtcNow.AddMinutes(-1),
        RcEvidence = new RcRunEvidenceRecorder("off", false, false, null),
        PublishObservation = Published.Add,
        ActiveChunk = Active.Add,
        ProduceDiscovery = Discoveries.Add,
    };

    /// <summary>A prepared suite: native ones get this world's test host and a fresh discovery index.</summary>
    public NightlySuiteState Suite(string id = "antiphon", bool native = true) => new()
    {
        SuiteId = id,
        Label = id,
        IsNative = native,
        Suite = Policy.Object.Suites.TryGetValue(id, out var suite) ? suite : null,
        Host = native ? new NightlyTestHost(Fixture.PathOf("bin", "host.exe"), [], Fixture.PathOf("bin", "host.dll")) : null,
        DiscoveryIndex = new NightlyDiscoveryIndex(),
        ProfileExclusions = Array.Empty<object>(),
    };

    /// <summary>A declared chunk ({ id, classes }).</summary>
    public static PlainObject Chunk(string id = "all", params string[] classes) => new(("id", id), ("classes", classes.Cast<object>().ToArray()));

    /// <summary>A path under the log root.</summary>
    public string LogPath(string name) => Path.Combine(LogRoot, name);

    /// <summary>The chunk's timing sidecar (LogRoot/&lt;suite&gt;-&lt;chunk&gt;.timing.json), or default when it is missing.</summary>
    public JsonElement Timing(string suite, string chunk)
    {
        var path = LogPath(suite + "-" + chunk + ".timing.json");
        if (!File.Exists(path)) return default;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    /// <summary>A text field of a timing sidecar ("" when absent).</summary>
    public static string Field(JsonElement timing, string name) =>
        timing.ValueKind == JsonValueKind.Object && timing.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    /// <summary>The fake native host, then this answer instead of its pass.</summary>
    public static Func<NightlyLaunch, NightlyLaunchResult> NativeThen(Func<NightlyLaunch, NightlyLaunchResult> answer)
    {
        var host = FakeTestHost.NativePass(Rows, Nodes);
        return launch =>
        {
            host(launch);
            return answer(launch);
        };
    }

    public void Dispose() => Fixture.Dispose();
}
