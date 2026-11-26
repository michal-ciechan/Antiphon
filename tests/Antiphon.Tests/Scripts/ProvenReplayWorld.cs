using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
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
    internal const string RenamedFrom = "tests/R/data-before.txt";
    internal const string RenamedTo = "tests/R/data-after.txt";
    internal const string AsOf = "2026-11-25T12:00:00Z";
    private readonly NightlyFixture _fixture = NightlyFixture.Create("c2076-replay");
    private readonly string[] _shas = new string[6];
    private readonly string[] _graphs = new string[6];
    private readonly Dictionary<int, (string Sha, string Graph)> _heldEndpoints = [];
    private (string Sha, string Graph)? _renameEndpoint;
    private readonly HashSet<string> _observedFailures = new(StringComparer.Ordinal);
    private readonly bool _alias;
    private readonly string[] _historicalGraphs = new string[6];

    internal ProvenReplayWorld(bool alias = false) => _alias = alias;

    internal string Repository => _fixture.PathOf("repo");
    internal string Output(string name) => _fixture.PathOf("output-" + name);
    internal string Sha(int run) => _shas[run];
    internal static string RunId(int run) => $"run-{RunKey(run)}-attempt";

    internal async Task InitializeAsync(CancellationToken cancellationToken, bool includeGateEndpoints = false,
        bool includeRenameEndpoint = false, bool reuseCommit = false)
    {
        var clock = Stopwatch.StartNew();
        const string north = "tests/L/A.cs";
        const string leftUse = "tests/L/Use.cs";
        const string rightUse = "tests/R/Use.cs";
        Directory.CreateDirectory(Repository);
        Git("init", "-q", "-b", "main");
        Git("config", "core.autocrlf", "false");
        using var pack = new ReleaseImpactTypeProofFixture();
        var tunit = pack.Emit("TUnit.Core", ReleaseImpactTypeProofFixture.TUnitAttributes, []);
        const string reader = """
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
        const string alias = """
            using Pick = South.Probe;
            namespace L;
            public class LeftTests
            {
                [TUnit.Core.Test, TUnit.Core.Arguments("l1"), TUnit.Core.Arguments("l2")]
                public void M(string key)
                {
                    if (Pick.Value != 0) throw new System.Exception(key);
                }
            }
            """;
        var left = _alias ? alias : reader;
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [".gitignore"] = ".antiphon/selector-replay/\n",
            ["Directory.Build.props"] = "<Project />\n",
            [Left] = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
            [Right] = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
            [north] = _alias ? "namespace North; public class Probe { public const int Value = 0; }\n" : "namespace North; public class Probe {}\n",
            [Changed] = RightSource(false),
            [RenamedFrom] = "fixture payload retained across rename\n",
            [leftUse] = left + "\npublic class DecoyTests { [TUnit.Core.Test, TUnit.Core.Arguments(\"d1\")] public void M(string key) { _ = typeof(North.Probe); } }\n",
            [rightUse] = "namespace R; public class RightTests { [TUnit.Core.Test, TUnit.Core.Arguments(\"r1\")] public void M(string key) { _ = typeof(South.Probe); } }\n",
        };
        foreach (var (path, text) in files) Put(path, text);
        const string policy = "scripts/lib/rc-trusted-projects.json";
        Write(policy, new
        {
            format = "antiphon-rc-trusted-projects-v1", schema = 1,
            entries = new[]
            {
                new { project = Right, configurations = new[] { "Release" }, downstream = new[] { Left },
                    contractProducer = "fixture", ownedTests = new[] { new { chunk = "right", cases = new[] { "r1" } } } },
            },
        });
        Write("tests/rc-project-categories.json", new
        {
            schemaVersion = 1,
            projects = new Dictionary<string, string[]> { [Left] = ["other"], [Right] = ["other"] },
            batches = new[] { Left, Right },
        });
        Write("tests/derived-impact-index.json", new
        {
            schema = 1, cap = 25, cappedPaths = Array.Empty<object>(),
            edges = new[] { north, Changed }.Select(path => new { path, classes = new[] { "DecoyTests", "LeftTests", "RightTests" } }),
        });
        ReleaseImpactTypeProofFixture.Context[] contexts =
        [
            new(Left, "L", [north, leftUse], [], [pack.Core, tunit]),
            new(Right, "R", [Changed, rightUse], [], [pack.Core, tunit]),
        ];
        var endpointCount = reuseCommit ? 1 : _shas.Length;
        for (var run = 0; run < endpointCount; run++)
        {
            if (run != 0 && run != 3)
                Put("Directory.Build.props", files["Directory.Build.props"] = $"<Project><!-- run {run} --></Project>\n");
            if (run is 3 or 4) Put(Changed, files[Changed] = RightSource(run == 3));
            if (_alias)
                contexts[0] = contexts[0] with { References = [pack.Core, tunit, pack.Emit("R", files[Changed], []) with { Project = Right }] };
            (_shas[run], _graphs[run]) = CommitEndpoint();
            if (_alias) _historicalGraphs[run] = PreserveHistoricalRule(_graphs[run], north);
        }
        if (reuseCommit)
        {
            Array.Fill(_shas, _shas[0]);
            Array.Fill(_graphs, _graphs[0]);
            Array.Fill(_historicalGraphs, _historicalGraphs[0]);
        }
        else
        {
            await ObserveLeftAsync([files[leftUse], files[north], ReleaseImpactTypeProofFixture.TUnitAttributes], pack, cancellationToken);
        }
        if (includeGateEndpoints)
        {
            Git("checkout", "--detach", _shas[3]);
            Put("Directory.Build.props", files["Directory.Build.props"] = "<Project><!-- hold baseline --></Project>\n");
            Put(Left, files[Left] = "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../R/R.csproj\" /></ItemGroup></Project>\n");
            Put(Changed, files[Changed] = "namespace South; public class Probe {}\n");
            _heldEndpoints.Add(4, CommitEndpoint());
            Put(Changed, files[Changed] = "namespace South; public class Probe { private int GateValue; }\n");
            _heldEndpoints.Add(5, CommitEndpoint());
        }
        if (includeRenameEndpoint)
        {
            Git("checkout", "--detach", _shas[3]);
            files[Left] = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n";
            files["Directory.Build.props"] = "<Project><!-- run 2 --></Project>\n";
            files[Changed] = "namespace South; public class Probe { public int Value; }\n";
            File.Move(Path.Combine(Repository, RenamedFrom), Path.Combine(Repository, RenamedTo));
            files[RenamedTo] = files[RenamedFrom];
            files.Remove(RenamedFrom);
            _renameEndpoint = CommitEndpoint();
        }
        Console.WriteLine($"C2076 replay fixture=setup alias={_alias} gate={includeGateEndpoints} rename={includeRenameEndpoint} elapsedMs={clock.ElapsedMilliseconds}");

        string RightSource(bool changed) => _alias
            ? "namespace South; public class Probe { public const int Value = " + (changed ? "1" : "0") + "; }\n"
            : changed ? "namespace South; public class Probe { public int Value; }\n" : "namespace South; public class Probe {}\n";

        (string Sha, string Graph) CommitEndpoint()
        {
            Git("add", "--all");
            Git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "commit", "-qm", "fixture");
            var sha = Git("rev-parse", "HEAD").Trim();
            var output = Path.Combine(Repository, ".antiphon", "selector-replay", sha + ".json");
            pack.WriteReleaseGraph(Repository, sha, output, files, contexts, cancellationToken);
            return (sha, output);
        }
    }

    private string PreserveHistoricalRule(string artifact, string north)
    {
        var historical = JsonNode.Parse(File.ReadAllText(artifact))!;
        historical["producerBinarySha256"] = Digest(Encoding.UTF8.GetBytes("fixture archived first-candidate alias rule"));
        var sites = historical["typeSites"]!.AsArray().Where(site =>
            (string?)site!["origin"]!["consumer"]!["fullName"] == "L.LeftTests" &&
            site["origin"]!["candidates"]!.AsArray().Any(candidate => (string?)candidate == Changed)).ToArray();
        sites.ShouldNotBeEmpty("P.wrongly-dropped real alias provenance");
        foreach (var site in sites)
        {
            site!["origin"]!["candidates"]!.AsArray().Select(candidate => (string?)candidate)
                .ShouldContain(north, "P.wrongly-dropped original conservative decoy");
            site["retained"]!.AsArray().Select(candidate => (string?)candidate)
                .ShouldContain(Changed, "P.wrongly-dropped correct pre-archive winner");
            site["winners"] = new JsonArray(north);
            site["retained"] = new JsonArray(north);
            site["metadataName"] = "North.Probe";
            site["assembly"] = "L, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";
        }
        var affected = historical["graph"]!["edges"]!.AsArray()
            .Single(edge => (string?)edge!["path"] == Changed)!["classes"]!.AsArray();
        var left = affected.Single(row => (string?)row!["fullName"] == "L.LeftTests")!;
        left["kinds"]!.AsArray().Select(kind => (string?)kind).SequenceEqual(["type"])
            .ShouldBeTrue("P.wrongly-dropped the archived rule removes only the type contribution");
        affected.Remove(left);
        var edges = historical["graph"]!["edges"]!.AsArray();
        var decoy = edges.SingleOrDefault(edge => (string?)edge!["path"] == north);
        if (decoy is null)
        {
            var at = edges.TakeWhile(edge => string.CompareOrdinal((string?)edge!["path"], north) < 0).Count();
            decoy = new JsonObject { ["path"] = north, ["classes"] = new JsonArray() };
            edges.Insert(at, decoy);
        }
        decoy["classes"]!.AsArray().Add(left);
        var path = Path.ChangeExtension(artifact, ".historical.json");
        File.WriteAllText(path, historical.ToJsonString());
        return path;
    }

    private async Task ObserveLeftAsync(string[] sources, ReleaseImpactTypeProofFixture pack, CancellationToken cancellationToken)
    {
        const string harness = """
            public static class Program
            {
                public static int Main(string[] args)
                {
                    var failures = 0;
                    for (var index = 0; index < args.Length; index++)
                    {
                        try
                        {
                            if (args[index] == "d1") new L.DecoyTests().M(args[index]);
                            else new L.LeftTests().M(args[index]);
                        }
                        catch (System.Exception failure)
                        {
                            if (failure.Message != args[index]) return 42;
                            failures |= 1 << index;
                        }
                    }
                    return failures;
                }
            }
            """;
        var output = _fixture.PathOf("executed-probe");
        Directory.CreateDirectory(output);
        var entry = Path.Combine(output, "probe.dll");
        var syntax = sources.Append(harness).Select(code => CSharpSyntaxTree.ParseText(code)).ToArray();
        var core = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        if (!_alias) Emit([core]);
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
            if (_alias) Emit([core, pack.Open(pack.Emit("R", actual, []))]);
            string[] keys = ["l1", "l2", "d1"];
            var execution = await C1514Support.RunProcess(new ProcessStartInfo("dotnet", [entry, ..keys])
                { WorkingDirectory = output }, cancellationToken);
            execution.ExitCode.ShouldBe(run == 3 ? 3 : 0, $"P.absent-reader run {run}: {execution.Text}");
            for (var index = 0; index < keys.Length; index++)
                if ((execution.ExitCode & (1 << index)) != 0) _observedFailures.Add(keys[index]);
        }

        void Emit(MetadataReference[] references)
        {
            var compilation = CSharpCompilation.Create("probe", syntax, references,
                new CSharpCompilationOptions(OutputKind.ConsoleApplication, deterministic: true));
            using var stream = File.Create(entry);
            var result = compilation.Emit(stream, cancellationToken: cancellationToken);
            result.Success.ShouldBeTrue(string.Join(Environment.NewLine, result.Diagnostics));
        }
    }

    internal string Input(string name, bool fails, bool carryHasEvidence = true, bool held = false, bool gateRed = false,
        bool direct = false, bool rename = false, bool historical = false, bool extraDecoy = false)
    {
        var root = _fixture.PathOf("input-" + name);
        var members = new List<(string Chunk, string Project, string Assembly, string Class, string[] Keys)>
        {
            ("left", Left, "L", "L.LeftTests", ["l1", "l2"]),
            ("right", Right, "R", "R.RightTests", ["r1"]),
        };
        if (extraDecoy) members.Add(("decoy", Left, "L", "L.DecoyTests", ["d1"]));
        var expectedContents = new List<string>
        {
            RcChunkReceipt.ContentDigest("antiphon", "native", [new("L", "L.LeftTests", "M", "l1"), new("L", "L.LeftTests", "M", "l2")]),
            RcChunkReceipt.ContentDigest("antiphon", "native", [new("R", "R.RightTests", "M", "r1")]),
        };
        if (extraDecoy)
            expectedContents.Add(RcChunkReceipt.ContentDigest("antiphon", "native", [new("L", "L.DecoyTests", "M", "d1")]));
        var copiedGraphs = new HashSet<string>(StringComparer.Ordinal);
        for (var run = 0; run < _shas.Length; run++)
        {
            var (sha, sourceGraph) = Endpoint(run);
            var graph = Path.Combine(root, "type-graphs", sha + ".json");
            if (copiedGraphs.Add(sha))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(graph)!);
                File.Copy(sourceGraph, graph);
            }
            var directory = Path.Combine(root, "runs", RunKey(run));
            var logs = Path.Combine(directory, "logs");
            var rows = new List<object>();
            var chunks = new List<object>();
            foreach (var (side, project, assembly, type, keys) in members)
            {
                var roster = keys.Select(key => new { assembly, @class = type, method = "M", @case = key }).ToArray();
                var content = RcChunkReceipt.ContentDigest("antiphon", "native",
                    keys.Select(key => new RcCaseIdentity(assembly, type, "M", key)).ToArray());
                chunks.Add(new { suite = "antiphon", chunk = side, project, classes = new[] { type }, members = roster,
                    consumes = held && run >= 4 && side == "left" ? new[] { Right } : Array.Empty<string>() });
                var hit = run == 3 && side == "left";
                if (hit && !carryHasEvidence) continue;
                var fail = hit && fails && keys.All(_observedFailures.Contains) || held && run == 5 && side == "right" && gateRed;
                var trx = side + ".trx";
                NightlyFixture.WriteTrx(Path.Combine(logs, trx), keys.Select(key =>
                    new NightlyTrxRow(key, type, "M", fail ? "Failed" : "Passed")).ToArray());
                rows.Add(new
                {
                    id = "antiphon", chunk = side, result = fail ? "FAIL" : "pass", exitCode = fail ? 1 : 0, sha,
                    disposition = "executed", contentDigest = content, platform = "windows", architecture = "x64", profile = "rc", trx,
                });
            }
            var census = direct && run == 5
                ? new[] { new { reader = "L.LeftTests", globs = new[] { Changed } } } : Array.Empty<object>();
            NightlyFixture.WriteJson(Path.Combine(logs, "groups-planned.json"), new { chunks, census });
            NightlyFixture.WriteJson(Path.Combine(logs, "summary.json"), new { sha, suites = rows });
            NightlyFixture.WriteJson(Path.Combine(directory, "attempt", "selection-attempt.json"), new { attemptId = RunId(run), runId = RunId(run) });
            if (held && run == 5)
                NightlyFixture.WriteJson(Path.Combine(directory, "attempt", "contracts.json"), new[] { "A", "B" }.Select(side => new
                {
                    project = Right, side, extractorVersion = "fixture", contractHash = Digest(Encoding.UTF8.GetBytes("same contract")),
                }).ToArray());
        }
        NightlyFixture.WriteJson(Path.Combine(root, "custody.json"), new
        {
            asOf = AsOf,
            runs = Enumerable.Range(1, 5).Select(run => new { run8 = RunKey(run), runId = RunId(run), stamp = $"2026-11-2{run}-0100", sha = Endpoint(run).Sha }),
            predecessor = new { run8 = RunKey(0), runId = RunId(0), stamp = "2026-11-20-0100", sha = _shas[0] },
            repository = "card-2076-fixture", line = "rc", missing = Array.Empty<string>(),
        });
        NightlyFixture.WriteJson(Path.Combine(root, "state", "qualification-cohort.json"), new
        {
            asOf = AsOf, repository = "card-2076-fixture", line = "rc",
            runIds = Enumerable.Range(1, 5).Select(RunId), predecessorRun = RunId(0), predecessorSha = _shas[0],
            rosters = Enumerable.Range(0, _shas.Length).Select(run => new
            {
                run = RunId(run), attempt = RunId(run), sha = Endpoint(run).Sha,
                contents = expectedContents,
            }),
        });
        Seal(root);
        return root;

        (string Sha, string Graph) Endpoint(int run)
        {
            if (held && run is 4 or 5) return _heldEndpoints[run];
            if (rename && run == 3) return _renameEndpoint ?? throw new InvalidOperationException("Rename endpoint was not initialized.");
            return (_shas[run], historical ? _historicalGraphs[run] : _graphs[run]);
        }
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
