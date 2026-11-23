using System.Collections;
using System.Collections.Specialized;
using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1545: the rc project planner (Antiphon.Nightly.RcProjectSelection) on a fixture repository, in process. The
/// cases of the deleted scripts/test-rc-project-selection.ps1 (2026-11-22 plan, unit 8), with its row names: ownership and
/// downstream closure, batches and categories, every fail-closed reason, historical edges at B, and consumer safety
/// (linked content, globs, imports, the batch census). The planner reads git objects only.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcProjectSelectionTests
{
    private static readonly OrderedDictionary Projects = new()
    {
        ["src/Messaging/Messaging.csproj"] = new[] { "messaging" },
        ["src/Server/Server.csproj"] = new[] { "server", "git-operations" },
        ["src/Runner/Runner.csproj"] = new[] { "claude-runner", "codex-runner", "shared-pty-runner" },
        ["tests/Server/Server.csproj"] = new[] { "server", "git-operations" },
        ["tests/Runner/Runner.csproj"] = new[] { "claude-runner", "codex-runner", "shared-pty-runner" },
        ["tests/Other/Other.csproj"] = new[] { "other" },
    };

    [Test]
    public void C1545_Closure()
    {
        using var fx = new Repo();
        var b = fx.Baseline();
        fx.Put("src/Messaging/Message.cs", "changed");
        var a = fx.Commit();
        var projectReads = 0;
        var sharedReads = 0;
        // Every project and shared start input is read through the census batch: every path asked of git, one read each.
        Dictionary<string, string> Counted(string root, string sha, string[] paths)
        {
            projectReads += paths.Count(p => p.EndsWith(".csproj", StringComparison.Ordinal));
            sharedReads += paths.Count(p => p == "Directory.Build.props");
            return TestSelectionGit.ReadBlobBatch(root, sha, paths);
        }
        var s = RcProjectSelection.Select(fx.Root, b, a, Counted);
        var reads = projectReads;
        fx.Put("Directory.Build.props", "not XML");
        var invalidBase = fx.Commit();
        fx.Put("src/Messaging/Message.cs", "invalid shared input candidate");
        var invalidCandidate = fx.Commit();
        var invalid = RcProjectSelection.Select(fx.Root, invalidBase, invalidCandidate, Counted);
        var projectBlobs = fx.Git("ls-tree", "-r", a).Split('\n').Where(l => l.EndsWith(".csproj", StringComparison.Ordinal))
            .Select(l => l.Split((char[]?)null, 4, StringSplitOptions.RemoveEmptyEntries)[2]).Distinct().Count();
        var rows = new NightlyCaseRows();
        rows.Check(reads == projectBlobs && sharedReads == 1 && Text(invalid, "mode") == "Full"
            && Strings(invalid, "fallbackReasons").Any(r => r.StartsWith("graph:Directory.Build.props:", StringComparison.Ordinal)),
            "C1545 immutable-blob-read-once", "reads=" + reads + " blobs=" + projectBlobs + " shared=" + sharedReads + " " + string.Join(",", Strings(invalid, "fallbackReasons")));
        rows.Check(Text(s, "mode") == "Scoped", "C1545 scoped", string.Join(",", Strings(s, "fallbackReasons")));
        rows.Check(Same(Strings(s, "changedProjects"), "src/Messaging/Messaging.csproj"), "C1545 owners");
        rows.Check(Same(Strings(s, "affectedProjects"), "src/Messaging/Messaging.csproj", "src/Server/Server.csproj", "src/Runner/Runner.csproj",
            "tests/Server/Server.csproj", "tests/Runner/Runner.csproj"), "C1545 downstream");
        rows.Check(Same(BatchProjects(s), "tests/Server/Server.csproj", "tests/Runner/Runner.csproj"), "C1545 batches");
        rows.Check(Same(Strings(s, "categories"), "messaging", "server", "git-operations", "claude-runner", "codex-runner", "shared-pty-runner"), "C1545 categories");
        var messaging = Project(s, a, "src/Messaging/Messaging.csproj");
        rows.Check(messaging != null && Same(Strings(messaging, "downstream"), "src/Server/Server.csproj", "src/Runner/Runner.csproj", "tests/Server/Server.csproj",
            "tests/Runner/Runner.csproj"), "C1545 manifest-closure");
        rows.Check(Text(s, "effectiveMode") == "Full" && s["gateCredit"] is false && s["executionEvidence"] is false && Text(s, "activation") == "shadow-only", "C1545 no-credit");
        var again = RcProjectSelection.Select(fx.Root, b, a);
        rows.Check(TestSelectionJson.Canonical(s) == TestSelectionJson.Canonical(again), "C1545 deterministic");
        rows.ShouldAllPass(9);
    }

    [Test]
    public void C1545_FailClosed()
    {
        using var fx = new Repo();
        var b = fx.Baseline();
        var rows = new NightlyCaseRows();
        fx.Put("unowned/file.txt", "new");
        var s = RcProjectSelection.Select(fx.Root, b, fx.Commit());
        rows.Check(Text(s, "mode") == "Full" && Strings(s, "fallbackReasons").Contains("unknown-path:unowned/file.txt") && Items(s["batches"]).Length == 3, "C1545 unknown-full");
        fx.Put("Directory.Build.props", "<Project />");
        s = RcProjectSelection.Select(fx.Root, b, fx.Commit());
        rows.Check(Strings(s, "fallbackReasons").Contains("shared-input:Directory.Build.props"), "C1545 shared-full");
        fx.Put("src/Runner/Runner.csproj", "<Project><ItemGroup><ProjectReference Include=\"$(Unknown)/Runner.csproj\" /></ItemGroup></Project>");
        s = RcProjectSelection.Select(fx.Root, b, fx.Commit());
        rows.Check(Text(s, "mode") == "Full" && Strings(s, "fallbackReasons").Any(r => r.Contains("unresolved-input", StringComparison.Ordinal)), "C1545 dynamic-full");
        var projects = new OrderedDictionary();
        foreach (DictionaryEntry entry in Projects) if ((string)entry.Key != "tests/Other/Other.csproj") projects[entry.Key] = entry.Value;
        fx.Catalog(projects, "tests/Server/Server.csproj", "tests/Runner/Runner.csproj", "tests/Other/Other.csproj");
        s = RcProjectSelection.Select(fx.Root, b, fx.Commit());
        rows.Check(Text(s, "mode") == "Full" && Strings(s, "fallbackReasons").Contains("unknown-project:tests/Other/Other.csproj"), "C1545 category-full");
        rows.ShouldAllPass(4);
    }

    [Test]
    public void C1545_HistoricalEdges()
    {
        using var fx = new Repo();
        var b = fx.Baseline();
        var rows = new NightlyCaseRows();
        fx.Put("tests/Server/Linked.cs", "changed");
        var s = RcProjectSelection.Select(fx.Root, b, fx.Commit());
        rows.Check(Same(BatchProjects(s), "tests/Server/Server.csproj", "tests/Runner/Runner.csproj"), "C1545 linked-consumer");
        fx.Put("src/Server/Server.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var a = fx.Commit();
        s = RcProjectSelection.Select(fx.Root, b, a);
        var oldServer = Project(s, b, "src/Server/Server.csproj");
        var newServer = Project(s, a, "src/Server/Server.csproj");
        rows.Check(Strings(s, "affectedProjects").Contains("tests/Server/Server.csproj") && oldServer != null && Strings(oldServer, "references").Contains("src/Messaging/Messaging.csproj")
            && newServer != null && Strings(newServer, "references").Length == 0, "C1545 old-edge");
        File.Delete(fx.PathOf("src/Messaging/Message.cs"));
        s = RcProjectSelection.Select(fx.Root, b, fx.Commit());
        rows.Check(Strings(s, "affectedProjects").Contains("src/Runner/Runner.csproj"), "C1545 deleted-owner");
        fx.Put("src/Runner/Runner.csproj", "<Project><ItemGroup><ProjectReference Include=\"../Server/Server.csproj\" /></ItemGroup></Project>");
        fx.Put("src/Server/Server.csproj", "<Project><ItemGroup><ProjectReference Include=\"../Runner/Runner.csproj\" /></ItemGroup></Project>");
        s = RcProjectSelection.Select(fx.Root, b, fx.Commit());
        rows.Check(Text(s, "mode") == "Full" && Strings(s, "fallbackReasons").Any(r => r.Contains("cycle", StringComparison.Ordinal)), "C1545 cycle-full");
        rows.ShouldAllPass(4);
    }

    [Test]
    public void C1545_ConsumerSafety()
    {
        using var fx = new Repo();
        fx.Baseline();
        var rows = new NightlyCaseRows();
        const string Runner = "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../../src/Runner/Runner.csproj\" />";
        fx.Put("tests/Runner/Runner.csproj", Runner + "<Content Include=\"../Server/Data.txt\" /></ItemGroup></Project>");
        fx.Put("tests/Server/Data.txt", "baseline");
        var cb = fx.Commit();
        fx.Put("tests/Server/Data.txt", "changed");
        var s = RcProjectSelection.Select(fx.Root, cb, fx.Commit());
        rows.Check(Text(s, "mode") == "Scoped" && Same(BatchProjects(s), "tests/Server/Server.csproj", "tests/Runner/Runner.csproj"), "C1545 content-consumer");
        fx.Put("tests/Runner/Runner.csproj", Runner + "<Content Include=\"../Server/Fixtures/*.json\" /></ItemGroup></Project>");
        fx.Put("tests/Server/Fixtures/example.json", "{}");
        cb = fx.Commit();
        fx.Put("tests/Server/Fixtures/example.json", "{\"changed\":true}");
        s = RcProjectSelection.Select(fx.Root, cb, fx.Commit());
        var wildcard = Text(s, "mode") == "Scoped" && Same(BatchProjects(s), "tests/Server/Server.csproj", "tests/Runner/Runner.csproj");
        fx.Put("tests/Runner/Runner.csproj", Runner + "<Content Include=\"../Server/[sample].json\" /></ItemGroup></Project>");
        fx.Put("tests/Server/[sample].json", "{}");
        cb = fx.Commit();
        fx.Put("tests/Server/[sample].json", "{\"changed\":true}");
        s = RcProjectSelection.Select(fx.Root, cb, fx.Commit());
        rows.Check(wildcard && Text(s, "mode") == "Scoped" && Same(BatchProjects(s), "tests/Server/Server.csproj", "tests/Runner/Runner.csproj"), "C1545 glob-consumer");
        fx.Put("src/Server/Server.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><Import Project=\"Dependencies.props\" /></Project>");
        fx.Put("src/Server/Dependencies.props", "<Project><ItemGroup><ProjectReference Include=\"../Messaging/Messaging.csproj\" /></ItemGroup></Project>");
        cb = fx.Commit();
        fx.Put("src/Messaging/Message.cs", "imported consumer change");
        s = RcProjectSelection.Select(fx.Root, cb, fx.Commit());
        rows.Check(Text(s, "mode") == "Scoped" && BatchProjects(s).Contains("tests/Server/Server.csproj"), "C1545 imported-reference", string.Join(",", Strings(s, "fallbackReasons")));
        fx.Put("tests/Runner/Runner.csproj", Runner + "<PackageReference Include=\"TUnit\" Version=\"1.0\" /></ItemGroup></Project>");
        fx.Catalog(Projects, "tests/Server/Server.csproj", "tests/Other/Other.csproj");
        cb = fx.Commit();
        fx.Put("src/Messaging/Message.cs", "missing batch change");
        s = RcProjectSelection.Select(fx.Root, cb, fx.Commit());
        rows.Check(Text(s, "mode") == "Full" && Strings(s, "fallbackReasons").Contains("unbatched-test-project:tests/Runner/Runner.csproj"), "C1545 batch-census");
        rows.ShouldAllPass(4);
    }

    /// <summary>The command: the selection's canonical JSON on stdout and once into --output-path; a refusal is exit 2 with one line.</summary>
    [Test]
    public void Command_prints_writes_once_and_refuses()
    {
        using var fx = new Repo();
        var b = fx.Baseline();
        fx.Put("src/Messaging/Message.cs", "changed");
        var a = fx.Commit();
        var output = fx.PathOf("out", "batches.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var options = SelectRcProjectBatchesOptions.Parse(["--baseline-sha", b, "--candidate-sha", a, "--repository-root", fx.Root, "--output-path", output], "unused");
        var (code, stdout, stderr) = Run(options);
        var rows = new NightlyCaseRows();
        using (var document = JsonDocument.Parse(stdout))
            rows.Check(code == 0 && document.RootElement.GetProperty("format").GetString() == RcProjectSelection.Format
                && document.RootElement.GetProperty("mode").GetString() == "Scoped" && File.ReadAllText(output) == stdout.TrimEnd('\r', '\n') + "\n", "printed-and-written", stderr);
        var (again, _, againError) = Run(options);
        rows.Check(again == 2 && againError.StartsWith("project selection refused: ", StringComparison.Ordinal), "existing-output-refused", againError);
        var (refused, _, why) = Run(SelectRcProjectBatchesOptions.Parse(["--baseline-sha", new string('0', 40), "--candidate-sha", a, "--repository-root", fx.Root], "unused"));
        rows.Check(refused == 2 && why.Trim() == "project selection refused: full-commit-required", "missing-commit-refused", why);
        rows.ShouldAllPass(3);
    }

    private static (int Code, string Out, string Error) Run(SelectRcProjectBatchesOptions options)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = RcProjectSelection.Run(options, NightlyHost.Production(output, error));
        return (code, output.ToString(), error.ToString());
    }

    private static string Text(IDictionary document, string name) => PsValue.Str(document[name]);

    private static string[] Strings(IDictionary document, string name) => Items(document[name]).Select(PsValue.Str).ToArray();

    private static object[] Items(object? value) => value is IEnumerable items and not string ? items.Cast<object>().ToArray() : value == null ? [] : [value];

    private static string[] BatchProjects(IDictionary selection) => Items(selection["batches"]).Select(r => PsValue.Str(((IDictionary)r)["project"])).ToArray();

    private static IDictionary? Project(IDictionary selection, string sha, string path) =>
        Items(selection["graphs"]).Cast<IDictionary>().Where(g => PsValue.Str(g["sha"]) == sha)
            .SelectMany(g => Items(g["projects"]).Cast<IDictionary>()).FirstOrDefault(p => PsValue.Str(p["path"]) == path);

    private static bool Same(IEnumerable<string> actual, params string[] expected) =>
        string.Join(",", TestSelectionJson.Sorted(actual.ToArray())) == string.Join(",", TestSelectionJson.Sorted(expected));

    /// <summary>The fixture repository of the deleted driver: six projects, their categories and three batches.</summary>
    private sealed class Repo : IDisposable
    {
        private readonly NightlyFixture _fixture = NightlyFixture.Create("c1545-projects");

        public Repo()
        {
            Directory.CreateDirectory(Root);
            Git("init", "-q", "-b", "main");
            Git("config", "core.autocrlf", "false");
        }

        public string Root => _fixture.PathOf("repository with spaces");

        public string PathOf(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

        public string Git(params string[] arguments) => NightlyRcChain.Git(Root, arguments);

        public void Put(string path, string text)
        {
            var full = PathOf(path.Split('/'));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text, new UTF8Encoding(false));
        }

        public string Commit()
        {
            Git("add", "--all");
            Git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "commit", "-qm", "fixture", "--allow-empty");
            return Git("rev-parse", "HEAD").Trim();
        }

        public void Catalog(IDictionary projects, params string[] batches) =>
            Put("tests/rc-project-categories.json", JsonSerializer.Serialize(new Dictionary<string, object> { ["schemaVersion"] = 1, ["projects"] = projects, ["batches"] = batches }));

        /// <summary>The baseline commit: the catalog, the six projects (references, a linked Compile item) and two sources.</summary>
        public string Baseline()
        {
            Catalog(Projects, "tests/Server/Server.csproj", "tests/Runner/Runner.csproj", "tests/Other/Other.csproj");
            Put("src/Messaging/Messaging.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Put("src/Server/Server.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../Messaging/Messaging.csproj\" /></ItemGroup></Project>");
            Put("src/Runner/Runner.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../Messaging/Messaging.csproj\" /></ItemGroup></Project>");
            Put("tests/Server/Server.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../../src/Server/Server.csproj\" /></ItemGroup></Project>");
            Put("tests/Runner/Runner.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../../src/Runner/Runner.csproj\" /><Compile Include=\"../Server/Linked.cs\" /></ItemGroup></Project>");
            Put("tests/Other/Other.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Put("src/Messaging/Message.cs", "baseline");
            Put("tests/Server/Linked.cs", "baseline");
            return Commit();
        }

        public void Dispose() => _fixture.Dispose();
    }
}
