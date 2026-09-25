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
                InFlight--;
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
    public static string RepoRoot => DockerStackDocuments.RepoRoot;

    public static string Fixture(string name) =>
        Path.Combine(RepoRoot, "tests", "Antiphon.Tests", "Checkpoints", "Fixtures", name);

    public static string Trx(string name) =>
        Path.Combine(RepoRoot, "scripts", "fixtures", name);

    public static string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "c723-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
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
                    new XAttribute("total", results.Count(r => r.Outcome != "NotExecuted")),
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
