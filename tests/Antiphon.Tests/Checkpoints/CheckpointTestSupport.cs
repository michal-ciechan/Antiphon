using System.Net;
using System.Text;
using System.Xml.Linq;
using Antiphon.Checkpoints;
using Antiphon.Tests.Infrastructure;

namespace Antiphon.Tests.Checkpoints;

internal sealed class FakePlatform : IPlatform
{
    public bool IsWindows { get; init; }
}

internal sealed class FakeDriver : IDriver
{
    private readonly List<(Func<DriverRequest, bool> Match, Func<DriverRequest, CancellationToken, Task<DriverResult>> Run)> _scripts = [];

    public List<DriverRequest> Calls { get; } = [];
    public List<(DriverRequest Request, IReadOnlyList<DriverRequest> InFlight)> Starts { get; } = [];
    private readonly List<DriverRequest> _active = [];
    public int KillCount { get; private set; }
    public bool LastKillEntireTree { get; private set; }
    public int InFlight { get; private set; }
    public int MaxInFlight { get; private set; }

    public void When(Func<DriverRequest, bool> match, Func<DriverRequest, CancellationToken, Task<DriverResult>> run) =>
        _scripts.Add((match, run));

    public async Task<DriverResult> RunAsync(DriverRequest request, CancellationToken cancellationToken)
    {
        lock (Calls)
        {
            Calls.Add(request);
            Starts.Add((request, _active.ToList()));
            _active.Add(request);
            InFlight++;
            if (InFlight > MaxInFlight)
                MaxInFlight = InFlight;
        }

        try
        {
            foreach (var script in _scripts)
            {
                if (script.Match(request))
                    return await script.Run(request, cancellationToken).ConfigureAwait(false);
            }

            return new DriverResult(0, "", "");
        }
        finally
        {
            lock (Calls)
            {
                _active.Remove(request);
                InFlight--;
            }
        }
    }

    public void Kill(bool entireProcessTree)
    {
        KillCount++;
        LastKillEntireTree = entireProcessTree;
    }

    public int Count(Func<DriverRequest, bool> match)
    {
        lock (Calls)
            return Calls.Count(match);
    }

    public int MaxInFlightFor(Func<DriverRequest, bool> match)
    {
        lock (Calls)
            return Starts.Where(start => match(start.Request))
                .Select(start => 1 + start.InFlight.Count(match)).DefaultIfEmpty(0).Max();
    }
}

internal sealed class PidIdempotentSlotHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly Dictionary<int, string> _byPid = new();
    private readonly HashSet<string> _live = new(StringComparer.Ordinal);
    private int _next = 1;

    public int LiveCount
    {
        get { lock (_gate) return _live.Count; }
    }

    public bool IsLive(string leaseId)
    {
        lock (_gate)
            return _live.Contains(leaseId);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get)
            return Json(HttpStatusCode.OK, """{"enabled":true,"budget":4,"maxCpuCount":4}""");
        if (request.Method == HttpMethod.Delete)
        {
            var id = request.RequestUri?.Segments.LastOrDefault()?.Trim('/') ?? "";
            bool removed;
            lock (_gate)
                removed = _live.Remove(id);
            return new HttpResponseMessage(removed ? HttpStatusCode.NoContent : HttpStatusCode.NotFound);
        }

        var json = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var pid = doc.RootElement.TryGetProperty("pid", out var value) && value.TryGetInt32(out var parsed) ? parsed : 0;
        string lease;
        lock (_gate)
        {
            if (_byPid.TryGetValue(pid, out var existing) && existing is not null && _live.Contains(existing))
                lease = existing;
            else
            {
                lease = "L" + _next++;
                _byPid[pid] = lease;
                _live.Add(lease);
            }
        }

        return Json(HttpStatusCode.OK, "{\"leaseId\":\"" + lease + "\",\"maxCpuCount\":4}");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

internal sealed class ScriptedHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _next = new();
    public List<(string Method, string Uri)> Calls { get; } = [];

    public void Enqueue(HttpStatusCode status, string body = "{}") =>
        _next.Enqueue(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });

    public void EnqueueThrow() =>
        _next.Enqueue(_ => throw new HttpRequestException("refused"));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls.Add((request.Method.Method, request.RequestUri?.ToString() ?? ""));
        if (_next.Count == 0)
            throw new HttpRequestException("no script");
        return Task.FromResult(_next.Dequeue()(request));
    }
}

internal static class CheckpointFixtures
{
    public static DateTimeOffset AfterFileClock(params string[] paths)
    {
        for (var i = 0; i < 50; i++)
        {
            var now = DateTimeOffset.UtcNow;
            var pending = false;
            foreach (var path in paths)
            {
                var info = new FileInfo(path);
                info.Refresh();
                if (new DateTimeOffset(DateTime.SpecifyKind(info.LastWriteTimeUtc, DateTimeKind.Utc)) > now
                    || new DateTimeOffset(DateTime.SpecifyKind(info.CreationTimeUtc, DateTimeKind.Utc)) > now)
                    pending = true;
            }
            if (!pending)
                return now;
            Thread.Sleep(20);
        }

        throw new TimeoutException("file clock stayed ahead of UtcNow");
    }

    public static string RepoRoot => DockerStackDocuments.RepoRoot;

    /// <summary>
    /// A folder holding a runnable build of the checkpoint tool, for a test that stages a copy of it. The first
    /// <paramref name="preferred"/> folder under tools/Antiphon.Checkpoints that exists wins (a checkpoint row's
    /// own output). Otherwise the tool's own bin folder for the test's configuration and framework, but only when
    /// it is this build: every Antiphon*.dll in it (the tool and its project references) is byte-identical to the
    /// test folder's copy. About 20 MB and 40 files. Otherwise the test assembly's folder, which also holds the
    /// tool (a project reference) but is about 900 MB and 2,000 files, every one hashed by the staged copy.
    /// </summary>
    public static string ToolOutputForStaging(params string[] preferred)
    {
        var tool = Path.Combine(RepoRoot, "tools", "Antiphon.Checkpoints");
        foreach (var name in preferred)
            if (Directory.Exists(Path.Combine(tool, name)))
                return Path.Combine(tool, name);
        var testFolder = Path.GetDirectoryName(typeof(DetachedLauncher).Assembly.Location)!;
        var framework = Path.GetFileName(testFolder);
        var configuration = Path.GetFileName(Path.GetDirectoryName(testFolder));
        if (string.IsNullOrEmpty(framework) || string.IsNullOrEmpty(configuration))
            return testFolder;
        var own = Path.Combine(tool, "bin", configuration, framework);
        if (!File.Exists(Path.Combine(own, "Antiphon.Checkpoints.dll"))
            || !File.Exists(Path.Combine(own, "Antiphon.Checkpoints.runtimeconfig.json")))
            return testFolder;
        try
        {
            foreach (var file in Directory.EnumerateFiles(own, "Antiphon*.dll"))
            {
                var copy = Path.Combine(testFolder, Path.GetFileName(file));
                if (!File.Exists(copy) || !System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))
                        .AsSpan().SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(copy))))
                    return testFolder;
            }
            return own;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return testFolder;
        }
    }

    private static readonly Lazy<string> ToolAssembly =
        new(() => Path.Combine(ToolOutputForStaging(), "Antiphon.Checkpoints.dll"));

    /// <summary>
    /// Has the run-checkpoint.ps1 child answer its git reads through this build (the tool leg's own libgit2 readers,
    /// lib/checkpoint-tool-reads.ps1): its source snapshots and its confinement reads, about 30 fewer git processes
    /// per script run. For end-to-end tests of what the script does with those answers; the git reader keeps its own
    /// tests (CheckpointSourceStateTests, RunCheckpointSourceScriptTests, CheckpointScriptReaderDeadlineTests, the
    /// test-run-checkpoint.ps1 cases) and the end-to-end classes that never set it.
    /// </summary>
    public static void UseToolReads(System.Diagnostics.ProcessStartInfo start) =>
        start.Environment["ANTIPHON_CHECKPOINT_TOOL_ASSEMBLY"] = ToolAssembly.Value;

    public static SourceObservation CleanSource() => new(new string('a', 40), 0, new string('1', 64),
        DateTimeOffset.UtcNow, "known");

    public static string Fixture(string name) =>
        Path.Combine(RepoRoot, "tests", "Antiphon.Tests", "Checkpoints", "Fixtures", name);

    public static string Trx(string name) =>
        Path.Combine(RepoRoot, "scripts", "fixtures", name);

    public static void MarkRun(string directory, bool alive)
    {
        var current = new ProcessIdentityProbe().Current();
        RunOwnershipStore.Write(directory, new RunOwnership
        {
            RunId = Path.GetFileName(directory),
            RunDirectory = Path.GetFullPath(directory),
            Phase = "launched",
            Starter = current,
            Launched = alive ? current : current with { Pid = int.MaxValue, StartUtcTicks = 1 },
        });
    }

    public static string SampleYaml() => """
        schemaVersion: 1
        builds:
          - id: bin-ex
            project: tests/Antiphon.Tests
            outputPath: bin-ex/
        checkpoints:
          - id: CP-1
            after: [S1]
            build: bin-ex
            filter: "/*/*/ExampleSurfaceTests/*"
            minExecuted: 3
            estimatedMinutes: 3
        """;

    public static bool IsBuild(DriverRequest request) =>
        request.Arguments.Count > 0 && request.Arguments[0] == "build";

    public static bool IsRun(DriverRequest request) =>
        request.Arguments.Contains("--treenode-filter");

    public static string ResultsDirectory(DriverRequest request)
    {
        var index = request.Arguments.ToList().FindIndex(arg => arg == "--results-directory");
        return index >= 0 && index + 1 < request.Arguments.Count ? request.Arguments[index + 1] : "";
    }

    public static string TrxFile(DriverRequest request)
    {
        var index = request.Arguments.ToList().FindIndex(arg => arg == "--report-trx-filename");
        var name = index >= 0 && index + 1 < request.Arguments.Count ? request.Arguments[index + 1] : "run.trx";
        return Path.Combine(ResultsDirectory(request), name);
    }

    public static void WriteResults(string path, params (string Name, string Outcome)[] results)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var ns = XNamespace.Get("http://microsoft.com/schemas/VisualStudio/TeamTest/2010");
        var doc = new XDocument(new XElement(ns + "TestRun",
            new XElement(ns + "Results", results.Select((result, i) =>
                new XElement(ns + "UnitTestResult",
                    new XAttribute("testId", Id(i)),
                    new XAttribute("testName", "display " + result.Name),
                    new XAttribute("outcome", result.Outcome),
                    new XAttribute("duration", "00:00:00.0100000")))),
            new XElement(ns + "TestDefinitions", results.Select((result, i) =>
            {
                var (className, method) = Split(result.Name);
                return new XElement(ns + "UnitTest",
                    new XAttribute("id", Id(i)),
                    new XElement(ns + "TestMethod",
                        new XAttribute("className", className),
                        new XAttribute("name", method)));
            })),
            new XElement(ns + "ResultSummary",
                new XElement(ns + "Counters",
                    new XAttribute("total", results.Length),
                    new XAttribute("executed", results.Count(r => r.Outcome != "NotExecuted")),
                    new XAttribute("passed", results.Count(r => r.Outcome == "Passed")),
                    new XAttribute("failed", results.Count(r => r.Outcome is "Failed" or "Error" or "Timeout" or "Aborted"))))));
        doc.Save(path);
    }

    public static CheckpointManifest TwoRowManifest(string root, string projectA = "tests/Antiphon.Tests", string projectB = "tests/Antiphon.Tests")
    {
        var manifest = new CheckpointManifest { ResultsRoot = root };
        manifest.Builds.Add(new BuildSpec { Id = "bin-a", Project = projectA, OutputPath = "bin-a/" });
        manifest.Builds.Add(new BuildSpec { Id = "bin-b", Project = projectB, OutputPath = "bin-b/" });
        manifest.Checkpoints.Add(Row("CP-1", "bin-a"));
        manifest.Checkpoints.Add(Row("CP-2", "bin-b"));
        manifest.Checkpoints.Add(Row("CP-3", "bin-a"));
        return manifest;
    }

    public static CheckpointSpec Row(string id, string build, bool serial = false) => new()
    {
        Id = id,
        After = ["S1"],
        Build = build,
        Filter = "/*/*/" + id + "Tests/*",
        Expect = [id + "Tests"],
        MinExecuted = 1,
        EstimatedMinutes = 1,
        TimeoutMinutes = 15,
        Serial = serial,
    };

    public static async Task WaitUntil(Func<bool> ready, int milliseconds = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (!ready() && DateTime.UtcNow < deadline)
            await Task.Delay(20);
    }

    private static string Id(int i) => $"00000000-0000-0000-0000-{i + 1:000000000000}";

    private static (string ClassName, string Method) Split(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot < 0 ? (name, "method") : (name[..dot], name[(dot + 1)..]);
    }
}
