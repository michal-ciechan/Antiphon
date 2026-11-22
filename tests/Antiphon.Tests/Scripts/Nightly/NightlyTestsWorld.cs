using System.Text;
using System.Text.Json;
using Antiphon.Nightly;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S15: one nightly tests run's world for NightlyTests.Run in-process, the twin of the C487 driver fixture of
/// scripts/test-nightly-tests.ps1: New-Efx (an owned clone with the policy and every project the policy names, so the
/// project universe matches), Write-C487Seams (a trace file both the host and the fakes append to, a git answering the
/// fixture sha, a launcher recording "EXEC" lines and passing, the fake Docker and disk rows) and Invoke-E (the run's
/// defaults). Behaviour replaces the launch answer (C487NativePass is Write-C487NativePassSeams); Variables overrides the
/// environment the run reads (PATH for the npm fixture) without changing this process's.
/// </summary>
public sealed class NightlyTestsWorld : IDisposable
{
    /// <summary>The fixture sha (Invoke-E's -Sha, the git seam's answer).</summary>
    public static readonly string Sha = new('a', 40);

    private readonly Dictionary<string, string?> _variables = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A world with the policy and the policy's projects copied into the clone (copyProjects false: the policy only).</summary>
    public NightlyTestsWorld(string prefix = "s15-tests", bool copyProjects = true)
    {
        Fixture = NightlyFixture.Create(prefix);
        Clone = Fixture.NewOwnedClone();
        Directory.CreateDirectory(Path.Combine(Clone, "tests"));
        File.Copy(Path.Combine(NightlyPlanCases.Repo, "tests", "test-execution-policy.json"), PolicyPath, true);
        if (copyProjects) CopyPolicyProjects(Clone);
        TracePath = Fixture.PathOf("trace.log");
        Logs = Fixture.PathOf("logs");
        Launcher = new RecordingLauncher(Answer);
    }

    public NightlyFixture Fixture { get; }
    public string Root => Fixture.Root;
    public string Clone { get; }
    public string TracePath { get; }
    public string Logs { get; }
    public string PolicyPath => Path.Combine(Clone, "tests", "test-execution-policy.json");
    public RecordingLauncher Launcher { get; }
    public FakeGit Git { get; } = new();
    public FakeHostProbe Probe { get; } = new();

    /// <summary>A probe answering instead of Probe (null: Probe).</summary>
    public INightlyHostProbe? ProbeOverride { get; set; }

    /// <summary>The launch answer after the EXEC line (null: Write-C487Seams' pass).</summary>
    public Func<NightlyLaunch, NightlyLaunchResult>? Behaviour { get; set; }

    /// <summary>The run's clock: the system clock, as the seams file sets no UtcNow.</summary>
    public TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>The trace file's text ("" before anything was traced).</summary>
    public string TraceText => File.Exists(TracePath) ? File.ReadAllText(TracePath) : "";

    /// <summary>Overrides one environment variable the run reads (null: unset).</summary>
    public NightlyTestsWorld Variable(string name, string? value)
    {
        _variables[name] = value;
        return this;
    }

    /// <summary>The host: this world's clock, git, launcher, probe and process table, writers, trace file and variables.</summary>
    public NightlyHost Host() => new()
    {
        Clock = Clock,
        Git = (directory, arguments) =>
        {
            Append("GIT " + string.Join(" ", arguments));
            return Git.Run(directory, arguments);
        },
        Launcher = Launcher,
        Processes = new FakeProcessTable(),
        Probe = ProbeOverride ?? Probe,
        Out = Fixture.Out,
        Error = Fixture.Error,
        Trace = new AppendingTraceWriter(TracePath),
        Variables = name => _variables.TryGetValue(name, out var value) ? value : Environment.GetEnvironmentVariable(name),
    };

    /// <summary>Invoke-E's options: the clone, the log root, the clone's policy, the fixture sha, a fresh run id, the shared tree allowed.</summary>
    public NightlyTestsOptions Options(Action<NightlyTestsOptions>? extra = null)
    {
        var options = new NightlyTestsOptions
        {
            RepoRoot = Clone, LogRoot = Logs, PolicyPath = PolicyPath, Sha = Sha, GitRef = "origin/master", Trigger = "scheduled",
            RunId = Guid.NewGuid().ToString("N"), AllowSharedTree = true,
        };
        extra?.Invoke(options);
        return options;
    }

    /// <summary>Invoke-E: the run of these options (the log root created first), with the evidence recorder suppressed as under the seams.</summary>
    public NightlyTestsResult Run(Action<NightlyTestsOptions>? extra = null)
    {
        Directory.CreateDirectory(Logs);
        return NightlyTests.Run(Options(extra), Host(), SeamedBindings());
    }

    /// <summary>The bindings of a seamed run: the shadow evidence recorder suppressed (Get-NightlyRcRunEvidence under the seams).</summary>
    public static NightlyTestsBindings SeamedBindings() => new() { RcEvidence = new RcRunEvidenceRecorder(Environment.GetEnvironmentVariable("ANTIPHON_RC_RUN_EVIDENCE"), true, false, null) };

    /// <summary>The summary the run wrote, or default when there is none.</summary>
    public static JsonElement Summary(NightlyTestsResult result)
    {
        if (string.IsNullOrEmpty(result.SummaryPath) || !File.Exists(result.SummaryPath)) return default;
        using var doc = JsonDocument.Parse(File.ReadAllText(result.SummaryPath));
        return doc.RootElement.Clone();
    }

    /// <summary>The summary's reasons ("" entries for a missing summary).</summary>
    public static string[] SummaryReasons(NightlyTestsResult result)
    {
        var summary = Summary(result);
        if (summary.ValueKind != JsonValueKind.Object || !summary.TryGetProperty("reasons", out var reasons) || reasons.ValueKind != JsonValueKind.Array) return [];
        return reasons.EnumerateArray().Select(r => r.ToString()).ToArray();
    }

    /// <summary>Copies every project the shipped policy names (ProjectUniverse) from this checkout into the clone.</summary>
    public static void CopyPolicyProjects(string clone)
    {
        var policy = NightlyPlanCases.Policy();
        foreach (var project in NightlyPolicy.ProjectUniverse(policy.Object))
        {
            var source = Path.Combine(NightlyPlanCases.Repo, project);
            if (!File.Exists(source)) continue;
            var target = Path.Combine(clone, project.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, true);
        }
    }

    /// <summary>
    /// Write-C487NativePassSeams: from the argv, what a passing native TUnit run leaves (FakeTestHost.NativePass), the files
    /// written in the seams file's shapes: the TRX of the rows, &lt;name&gt;.discovery.json beside it unless omitDiscovery, and
    /// log.diag in the diagnostic directory (the discovery document under --list-tests, else the execution document of
    /// the rows, whose nodes carry no namespace); -JsonResultPath gets the npm runner's one-pass JSON line.
    /// </summary>
    public static Func<NightlyLaunch, NightlyLaunchResult> C487NativePass(IReadOnlyList<NightlyTrxRow> rows, IReadOnlyList<NightlyTestNode> nodes, bool omitDiscovery = false) =>
        launch =>
        {
            var args = launch.Arguments ?? [];
            string Value(string flag)
            {
                for (var i = 0; i + 1 < args.Count; i++)
                    if (args[i] == flag) return args[i + 1];
                return "";
            }
            var trx = Value("--report-trx-filename");
            var results = Value("--results-directory");
            var diagnostics = Value("--diagnostic-output-directory");
            if (trx.Length > 0 && (trx.Contains('/') || trx.Contains('\\'))) return Result(5, FakeTestHost.InvocationLog + " trx-filename-not-basename");
            if (trx.Length > 0 && results.Length > 0) trx = Path.Combine(results, trx);
            if (trx.Length > 0)
            {
                NightlyFixture.WriteTrx(trx, rows);
                if (!omitDiscovery) WriteSeamJson(Path.ChangeExtension(trx, ".discovery.json"), NightlyFixture.DiscoveryDocument(nodes));
            }
            if (diagnostics.Length > 0)
            {
                Directory.CreateDirectory(diagnostics);
                var log = Path.Combine(diagnostics, "log.diag");
                if (args.Contains("--list-tests")) WriteSeamJson(log, NightlyFixture.DiscoveryDocument(nodes));
                else WriteSeamJson(log, new
                {
                    format = NightlyDiagnosticLog.ExecutionFormat,
                    tunitVersion = NightlyDiagnosticLog.SupportedTunit,
                    mtpVersion = NightlyDiagnosticLog.SupportedMtp,
                    nodes = rows.Select(r => new { uid = r.Id, state = r.Outcome, type = r.ClassName, method = r.MethodName, className = r.ClassName }).ToArray(),
                });
            }
            var json = Value("-JsonResultPath");
            if (json.Length > 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(json))!);
                File.WriteAllText(json, "{ \"numPassedTests\": 1, \"numFailedTests\": 0 }" + Environment.NewLine);
            }
            return Result(0, FakeTestHost.InvocationLog);
        };

    /// <summary>A launch answer with the seams' shape (pid 2, children exited).</summary>
    public static NightlyLaunchResult Result(int exitCode, string log = FakeTestHost.InvocationLog) =>
        new() { ExitCode = exitCode, TimedOut = false, Pid = 2, ChildrenExited = true, LogText = log };

    public void Dispose() => Fixture.Dispose();

    // ConvertTo-Json | Set-Content -Encoding UTF8: indented UTF-8 text and a line end.
    private static void WriteSeamJson(string path, object value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));

    private NightlyLaunchResult Answer(NightlyLaunch launch)
    {
        Append("EXEC " + launch.CommandText);
        return Behaviour?.Invoke(launch) ?? Result(0);
    }

    private void Append(string line)
    {
        lock (_variables) File.AppendAllText(TracePath, line + Environment.NewLine, Encoding.ASCII);
    }
}
