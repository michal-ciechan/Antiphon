using System.Text.Json;
using System.Xml.Linq;
using Antiphon.Checkpoints;

namespace Antiphon.Tests.Checkpoints;

// CARD-0885 repeat fixtures: a native-repeat TRX template and the row verb (scripts/run-checkpoint.ps1's body since unit
// 20 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md) run in process over a temporary repository whose
// TUnit project stands in for tests/Antiphon.Checkpoints.RepeatHost. A FakeDriver plays dotnet: the build succeeds and the
// run writes the template with the row's own nonce, so no build, test host or PowerShell runs.
internal static class CheckpointRepeatFixture
{
    internal const string Project = "tests/Antiphon.Checkpoints.RepeatHost";

    internal sealed record RowResult(int ExitCode, string Text, string[] Calls, string? Evidence, string? Trx);

    // Temp roots are "c723-" plus a 32-digit guid. Keeping that whole name makes the
    // deepest FilterHost artifacts file 270 characters on the release checkout. Windows
    // MAX_PATH is 260 including the terminating NUL, so the path string must stay <= 259.
    // The last 12 characters of the temp root keep the directory unique.
    internal static string OutputName(string root)
    {
        var name = Path.GetFileName(root);
        var tail = name.Length <= 12 ? name : name[^12..];
        return "bin-c885-script-" + tail;
    }

    public static string WriteTemplate(string root, int requested = 5, int? failingOrdinal = null, string[]? methods = null)
    {
        var names = methods is { Length: > 0 } ? methods : ["alpha", "beta", "gamma"];
        var ns = XNamespace.Get("http://microsoft.com/schemas/VisualStudio/TeamTest/2010");
        var rows = from ordinal in Enumerable.Range(0, requested)
                   from method in names
                   select (Ordinal: ordinal, Method: method, Outcome: failingOrdinal == ordinal && method == "alpha" ? "Failed" : "Passed");
        var cases = rows.ToList();
        var doc = new XDocument(new XElement(ns + "TestRun",
            new XElement(ns + "Results", cases.Select((item, index) =>
            {
                var key = "Antiphon.Tests.Checkpoints.Sample.0.0." + item.Method + ".0.0";
                var nativeId = key + "." + item.Ordinal;
                var marker = JsonSerializer.Serialize(new
                {
                    Version = 1, Requested = requested, Nonce = "__NONCE__", NativeId = nativeId,
                    Ordinal = item.Ordinal, CaseKey = key, Class = "Antiphon.Tests.Checkpoints.Sample",
                    Method = item.Method, HostPid = 12345,
                });
                return new XElement(ns + "UnitTestResult",
                    new XAttribute("testId", "T" + index),
                    new XAttribute("executionId", "E" + index),
                    new XAttribute("testName", "display collision"),
                    new XAttribute("outcome", item.Outcome),
                    new XAttribute("duration", "00:00:00.001"),
                    new XElement(ns + "Output",
                        new XElement(ns + "StdOut", "C885_REPEAT_START " + marker + "\nC885_REPEAT_END " + marker + "\n"),
                        item.Outcome == "Failed" ? new XElement(ns + "ErrorInfo", new XElement(ns + "Message", "deliberate-repeat-failure")) : null));
            })),
            new XElement(ns + "TestDefinitions", cases.Select((item, index) =>
                new XElement(ns + "UnitTest", new XAttribute("id", "T" + index),
                    new XElement(ns + "TestMethod", new XAttribute("className", "Antiphon.Tests.Checkpoints.Sample"),
                        new XAttribute("name", item.Method))))),
            new XElement(ns + "ResultSummary", new XElement(ns + "Counters",
                new XAttribute("total", cases.Count), new XAttribute("executed", cases.Count),
                new XAttribute("passed", cases.Count(item => item.Outcome == "Passed")),
                new XAttribute("failed", cases.Count(item => item.Outcome == "Failed"))))));
        var path = Path.Combine(root, "template.trx");
        doc.Save(path);
        return path;
    }

    /// <summary>The temporary repository a fixture row runs in: one TUnit project at <see cref="Project"/>.</summary>
    public static string WriteRepository(string root)
    {
        var project = Path.Combine(root, "repo", Project);
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "Antiphon.Checkpoints.RepeatHost.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"TUnit\" Version=\"1.44.0\" /></ItemGroup></Project>");
        return Path.Combine(root, "repo");
    }

    /// <summary>
    /// One row over the repository WriteRepository made under root: --repeat repeat, --min-executed 3, the template as the
    /// run's TRX. Calls are the dotnet argument lines, Evidence the row's report.json and Trx its run.trx.
    /// </summary>
    public static async Task<RowResult> RunRowAsync(string root, int repeat, string template, CancellationToken cancellationToken,
        bool noBuild = false, string? expectedSha = null, string? property = null)
    {
        var repo = Path.Combine(root, "repo");
        if (!Directory.Exists(repo)) WriteRepository(root);
        var results = Path.Combine(root, "results");
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            var nonce = request.Environment is not null && request.Environment.TryGetValue("ANTIPHON_CHECKPOINT_NONCE", out var value) ? value : "";
            File.WriteAllText(CheckpointFixtures.TrxFile(request), File.ReadAllText(template).Replace("__NONCE__", nonce, StringComparison.Ordinal));
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        var arguments = new List<string>
        {
            "row", "--repo-root", repo, "--name", "CP-1", "--project", Project, "--output-path", OutputName(root) + "/",
            "--filter", "/*/*/SampleTests*/*", "--min-executed", "3", "--repeat", repeat.ToString(), "--results-root", results, "--no-slot",
        };
        if (noBuild) arguments.Add("--no-build");
        if (expectedSha is not null) arguments.AddRange(["--expected-source-sha", expectedSha]);
        if (property is not null) arguments.AddRange(["--msbuild-property", property]);
        var before = Directory.Exists(results) ? Directory.GetDirectories(results, "CP-1-*") : [];
        var output = new StringWriter();
        var exit = await Antiphon.Checkpoints.Program.RunAsync(arguments.ToArray(), new CheckpointApp.Runtime
        {
            Output = output, Driver = driver, Platform = new FakePlatform { IsWindows = true },
            SourceCapture = _ => CheckpointFixtures.CleanSource(), EnvironmentLookup = _ => null, CancellationToken = cancellationToken,
        });
        // This row's own results directory: the one that was not there before it.
        var directory = Directory.Exists(results) ? Directory.GetDirectories(results, "CP-1-*").Except(before).SingleOrDefault() : null;
        return new RowResult(exit, output.ToString(),
            driver.Calls.Select(call => string.Join(" ", call.Arguments)).ToArray(),
            directory is null ? null : Path.Combine(directory, "report.json"),
            directory is null ? null : Path.Combine(directory, "run.trx"));
    }
}
