using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;

namespace Antiphon.Tests.Scripts;

internal sealed class ProvenReplayWorld : IDisposable
{
    private const string Left = "tests/L/L.csproj";
    private const string Right = "tests/R/R.csproj";
    internal const string Changed = "tests/R/B.cs";
    internal const string AsOf = "2026-11-25T12:00:00Z";
    private readonly NightlyFixture _fixture = NightlyFixture.Create("c2076-replay");
    private readonly string[] _shas = new string[6];
    private readonly string[] _graphs = new string[6];
    private readonly HashSet<string> _observedFailures = new(StringComparer.Ordinal);

    internal string Repository => _fixture.PathOf("repo");
    internal string Output(string name) => _fixture.PathOf("output-" + name);
    internal string Sha(int run) => _shas[run];
    internal static string RunId(int run) => $"run-{RunKey(run)}-attempt";

    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        const string north = "tests/L/A.cs";
        const string leftUse = "tests/L/Use.cs";
        const string rightUse = "tests/R/Use.cs";
        Directory.CreateDirectory(Repository);
        Git("init", "-q", "-b", "main");
        Git("config", "core.autocrlf", "false");
        using var pack = new ReleaseImpactTypeProofFixture();
        const string attributes = """
            namespace TUnit.Core;
            public sealed class TestAttribute : System.Attribute {}
            [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true)]
            public sealed class ArgumentsAttribute : System.Attribute
            {
                public ArgumentsAttribute(params object[] arguments) {}
            }
            """;
        var tunit = pack.Emit("TUnit.Core", attributes, []);
        const string left = """
            namespace L;
            public class LeftTests
            {
                [TUnit.Core.Test, TUnit.Core.Arguments("l1"), TUnit.Core.Arguments("l2")]
                public void M(string key)
                {
                    _ = typeof(North.Probe);
                    if (System.IO.File.ReadAllText(System.IO.Path.Combine("tests","R","B.cs")).Contains("public int Value;"))
                        throw new System.Exception(key);
                }
            }
            """;
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [".gitignore"] = ".antiphon/selector-replay/\n",
            ["Directory.Build.props"] = "<Project />\n",
            [Left] = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
            [Right] = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
            [north] = "namespace North; public class Probe {}\n",
            [Changed] = "namespace South; public class Probe {}\n",
            [leftUse] = left + "\n",
            [rightUse] = "namespace R; public class RightTests { [TUnit.Core.Test, TUnit.Core.Arguments(\"r1\")] public void M(string key) { _ = typeof(South.Probe); } }\n",
        };
        foreach (var (path, text) in files) Put(path, text);
        const string policy = "scripts/lib/rc-trusted-projects.json";
        Write(policy, new { format = "antiphon-rc-trusted-projects-v1", schema = 1, entries = Array.Empty<object>() });
        Write("tests/rc-project-categories.json", new
        {
            schemaVersion = 1,
            projects = new Dictionary<string, string[]> { [Left] = ["other"], [Right] = ["other"] },
            batches = new[] { Left, Right },
        });
        Write("tests/derived-impact-index.json", new
        {
            schema = 1, cap = 25, cappedPaths = Array.Empty<object>(),
            edges = new[] { north, Changed }.Select(path => new { path, classes = new[] { "LeftTests", "RightTests" } }),
        });
        ReleaseImpactTypeProofFixture.Context[] contexts =
        [
            new(Left, "L", [north, leftUse], [], [pack.Core, tunit]),
            new(Right, "R", [Changed, rightUse], [], [pack.Core, tunit]),
        ];
        for (var run = 0; run < _shas.Length; run++)
        {
            if (run != 0 && run != 3)
                Put("Directory.Build.props", files["Directory.Build.props"] = $"<Project><!-- run {run} --></Project>\n");
            if (run is 3 or 4)
                Put(Changed, files[Changed] = run == 3
                    ? "namespace South; public class Probe { public int Value; }\n"
                    : "namespace South; public class Probe {}\n");
            Git("add", "--all");
            Git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "commit", "-qm", "fixture");
            var sha = _shas[run] = Git("rev-parse", "HEAD").Trim();
            var (manifest, pin) = pack.PinEndpoint(Repository, sha, files, contexts);
            var output = Path.Combine(Repository, ".antiphon", "selector-replay", sha + ".json");
            var driver = Path.Combine(AppContext.BaseDirectory, "derived-impact-index", "Antiphon.DerivedImpactIndex.dll");
            var start = new ProcessStartInfo("dotnet", [driver, "--release-graph", Repository, sha, output,
                "--proven-subtraction", "--compiler-manifest", manifest, "--expected-compiler-manifest-sha256", pin])
            {
                WorkingDirectory = Repository,
            };
            var result = await C1514Support.RunProcess(start, cancellationToken);
            result.ExitCode.ShouldBe(0, result.Text);
            _graphs[run] = output;
        }
        await ObserveLeftAsync([left, files[north], attributes], cancellationToken);
    }

    private async Task ObserveLeftAsync(string[] sources, CancellationToken cancellationToken)
    {
        const string harness = """
            public static class Program
            {
                public static int Main(string[] args)
                {
                    try { new L.LeftTests().M(args[0]); return 0; }
                    catch (System.Exception failure) { return failure.Message == args[0] ? 1 : 42; }
                }
            }
            """;
        var output = _fixture.PathOf("executed-probe");
        Directory.CreateDirectory(output);
        var entry = Path.Combine(output, "probe.dll");
        var compilation = CSharpCompilation.Create("probe", sources.Append(harness).Select(code => CSharpSyntaxTree.ParseText(code)),
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, deterministic: true));
        using (var stream = File.Create(entry))
        {
            var result = compilation.Emit(stream, cancellationToken: cancellationToken);
            result.Success.ShouldBeTrue(string.Join(Environment.NewLine, result.Diagnostics));
        }
        NightlyFixture.WriteJson(Path.Combine(output, "probe.runtimeconfig.json"), new
        {
            runtimeOptions = new { tfm = "net9.0", framework = new { name = "Microsoft.NETCore.App", version = "9.0.0" } },
        });
        var changedPath = Path.Combine(output, Changed.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(changedPath)!);
        for (var run = 2; run <= 3; run++)
        {
            var actual = Git("show", _shas[run] + ":" + Changed);
            await File.WriteAllTextAsync(changedPath, actual, cancellationToken);
            foreach (var key in new[] { "l1", "l2" })
            {
                var execution = await C1514Support.RunProcess(new ProcessStartInfo("dotnet", [entry, key])
                    { WorkingDirectory = output }, cancellationToken);
                execution.ExitCode.ShouldBe(run == 3 ? 1 : 0, $"P.absent-reader run {run} case {key}: {execution.Text}");
                if (execution.ExitCode == 1) _observedFailures.Add(key);
            }
        }
    }

    internal string Input(string name, bool fails, bool carryHasEvidence = true)
    {
        var root = _fixture.PathOf("input-" + name);
        for (var run = 0; run < _shas.Length; run++)
        {
            var sha = _shas[run];
            var graph = Path.Combine(root, "type-graphs", sha + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(graph)!);
            File.Copy(_graphs[run], graph);
            var directory = Path.Combine(root, "runs", RunKey(run));
            var logs = Path.Combine(directory, "logs");
            var rows = new List<object>();
            var chunks = new List<object>();
            foreach (var (side, project, assembly, type, keys) in new[]
            {
                ("left", Left, "L", "L.LeftTests", new[] { "l1", "l2" }),
                ("right", Right, "R", "R.RightTests", new[] { "r1" }),
            })
            {
                var members = keys.Select(key => new { assembly, @class = type, method = "M", @case = key }).ToArray();
                var content = RcChunkReceipt.ContentDigest("antiphon", "native",
                    keys.Select(key => new RcCaseIdentity(assembly, type, "M", key)).ToArray());
                chunks.Add(new { suite = "antiphon", chunk = side, project, classes = new[] { type }, members });
                var hit = run == 3 && side == "left";
                if (hit && !carryHasEvidence) continue;
                var fail = hit && fails && keys.All(_observedFailures.Contains);
                var trx = side + ".trx";
                NightlyFixture.WriteTrx(Path.Combine(logs, trx), keys.Select(key =>
                    new NightlyTrxRow(key, type, "M", fail ? "Failed" : "Passed")).ToArray());
                rows.Add(new
                {
                    id = "antiphon", chunk = side, result = fail ? "FAIL" : "pass", exitCode = fail ? 1 : 0, sha,
                    disposition = "executed", contentDigest = content, platform = "windows", architecture = "x64", profile = "rc", trx,
                });
            }
            NightlyFixture.WriteJson(Path.Combine(logs, "groups-planned.json"), new { chunks, census = Array.Empty<object>() });
            NightlyFixture.WriteJson(Path.Combine(logs, "summary.json"), new { sha, suites = rows });
            NightlyFixture.WriteJson(Path.Combine(directory, "attempt", "selection-attempt.json"), new { attemptId = RunId(run), runId = RunId(run) });
        }
        NightlyFixture.WriteJson(Path.Combine(root, "custody.json"), new
        {
            asOf = AsOf,
            runs = Enumerable.Range(1, 5).Select(run => new { run8 = RunKey(run), runId = RunId(run), stamp = $"2026-11-2{run}-0100", sha = _shas[run] }),
            predecessor = new { run8 = RunKey(0), runId = RunId(0), stamp = "2026-11-20-0100", sha = _shas[0] },
            repository = "card-2076-fixture", line = "rc", missing = Array.Empty<string>(),
        });
        NightlyFixture.WriteJson(Path.Combine(root, "state", "qualification-cohort.json"), new
        {
            asOf = AsOf, repository = "card-2076-fixture", line = "rc",
            runIds = Enumerable.Range(1, 5).Select(RunId), predecessorRun = RunId(0), predecessorSha = _shas[0],
        });
        Seal(root);
        return root;
    }

    internal static void Seal(string root)
    {
        var manifests = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => path != "MANIFEST.sha256").Order(StringComparer.Ordinal)
            .Select(path => Digest(File.ReadAllBytes(Path.Combine(root, path))) + "  " + path);
        File.WriteAllText(Path.Combine(root, "MANIFEST.sha256"), string.Join("\n", manifests) + "\n", new UTF8Encoding(false));
    }

    internal static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string RunKey(int run) => run == 0 ? "p0000000" : $"r{run}000000";
    private string Git(params string[] args) => NightlyRcChain.Git(Repository, args);
    private void Put(string path, string text)
    {
        var full = Path.Combine(Repository, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text, new UTF8Encoding(false));
    }

    private void Write(string path, object data) => NightlyFixture.WriteJson(Path.Combine(Repository, path), data);
    public void Dispose() => _fixture.Dispose();
}
