using System.Diagnostics;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseImpactRelationTests
{
    [Test]
    public void AllTwentySixConsumersSurvive()
    {
        var sources = Sources();
        sources["shared/input.txt"] = "input";
        sources["tests/A/Consumers.cs"] = string.Join('\n', Enumerable.Range(0, 26).Select(i =>
            $"namespace Example {{ public class Consumer{i:00}Tests {{ [Test] public void Runs() {{ var path = \"shared/input.txt\"; }} }} }}"));
        var graph = Build(sources);
        Members(graph, "shared/input.txt").ShouldBe(Enumerable.Range(0, 26)
            .Select(i => $"tests/A/A.csproj::Example.Consumer{i:00}Tests"));
        AssertIncomplete(graph);
    }

    [Test]
    public void GlobalInputExpandsConsumers()
    {
        var sources = Sources();
        sources["tests/A/First.cs"] = "namespace A; public class FirstTests { [Test] public void Runs() {} }";
        sources["tests/B/B.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\" />";
        sources["tests/B/Second.cs"] = "namespace B; public class SecondTests { [Test] public void Runs() {} }";
        string[] expected = ["tests/A/A.csproj::A.FirstTests", "tests/B/B.csproj::B.SecondTests"];
        foreach (var path in new[] { "Antiphon.sln", "Directory.Build.props", "Directory.Build.targets", "global.json" })
            sources[path] = "global input";
        var graph = Build(sources);
        foreach (var path in new[] { "Antiphon.sln", "Directory.Build.props", "Directory.Build.targets", "global.json" })
            Members(graph, path).ShouldBe(expected, path);
        AssertIncomplete(graph);
    }

    [Test]
    public void QualifiedDuplicateNamesSurvive()
    {
        var sources = Sources();
        sources["shared/input.txt"] = "input";
        sources["tests/A/One.cs"] = "namespace One; public partial class ProbeTests { [Test] public void Runs() { var path = \"shared/input.txt\"; } }";
        sources["tests/A/Partial.cs"] = "namespace One; public partial class ProbeTests { }";
        sources["tests/A/Two.cs"] = "namespace Two; public class ProbeTests { [Test] public void Runs() { var path = \"shared/input.txt\"; } }";
        sources["tests/B/B.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\" />";
        sources["tests/B/One.cs"] = sources["tests/A/One.cs"];
        var graph = Build(sources);
        Members(graph, "shared/input.txt").ShouldBe(new[]
        {
            "tests/A/A.csproj::One.ProbeTests", "tests/A/A.csproj::Two.ProbeTests", "tests/B/B.csproj::One.ProbeTests",
        });
        var declarations = graph.GetProperty("universe").EnumerateArray()
            .Single(x => Identity(x) == "tests/A/A.csproj::One.ProbeTests").GetProperty("declarationPaths");
        declarations.EnumerateArray().Select(x => x.GetString()).ShouldBe(new[] { "tests/A/One.cs", "tests/A/Partial.cs" });
        AssertIncomplete(graph);
    }

    [Test]
    public void RawStringIsNotAClass()
    {
        var sources = Sources();
        sources["tests/A/Real.cs"] = """"
            namespace Census;
            public class Container<T> {
                public partial class Actual<U> {
                    [Test] public void Runs() { var text = """public class PhantomTests { [Test] public void Fake() {} }"""; }
                }
            }
            // public class CommentTests {}
            """";
        sources["tests/A/Other.cs"] = "namespace Census; public partial class Container<T> { public partial class Actual<U> {} }";
        var graph = Build(sources);
        graph.GetProperty("universe").EnumerateArray().Select(Identity)
            .ShouldBe(new[] { "tests/A/A.csproj::Census.Container`1+Actual`1" });
        Members(graph, "tests/A/Real.cs").ShouldBe(new[] { "tests/A/A.csproj::Census.Container`1+Actual`1" });
        AssertIncomplete(graph);
    }

    private static Dictionary<string, string> Sources() => new(StringComparer.Ordinal)
    {
        ["tests/A/A.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\" />",
    };

    private static string Identity(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString()! : value.GetProperty("project").GetString() + "::" + value.GetProperty("fullName").GetString();

    private static string[] Members(JsonElement graph, string path) => graph.GetProperty("edges").EnumerateArray()
        .Where(x => x.GetProperty("path").GetString() == path).SelectMany(x => x.GetProperty("classes").EnumerateArray())
        .Select(Identity).Order(StringComparer.Ordinal).ToArray();

    private static void AssertIncomplete(JsonElement graph)
    {
        graph.GetProperty("complete").GetBoolean().ShouldBeFalse();
        graph.GetProperty("fallback").GetString().ShouldBe("Full");
        graph.GetProperty("incompleteReasons").GetArrayLength().ShouldBeGreaterThan(0);
    }

    private static JsonElement Build(Dictionary<string, string> sources)
    {
        // Red-first adapter exercises the real ordinary capped relation before release support.
        // Retain these four isolated inputs with the failing run; no fixture deletion is needed.
        var root = Path.Combine(RepoRoot(), ".antiphon/selector-evidence/ee621172/red-inputs", Guid.NewGuid().ToString("N"));
        foreach (var (path, content) in sources)
        {
            var full = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        Git(root, "init", "-q");
        Git(root, "add", ".");
        using var ordinary = JsonDocument.Parse(DerivedImpactIndexBuilder.Build(root).Utf8);
        var edges = ordinary.RootElement.GetProperty("edges").Clone();
        return JsonSerializer.SerializeToElement(new
        {
            edges,
            universe = edges.EnumerateArray().SelectMany(x => x.GetProperty("classes").EnumerateArray()).Select(x => x.GetString()).Distinct().ToArray(),
            complete = false, fallback = "Full", incompleteReasons = new[] { "ordinary-index-is-advisory" },
        });
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln");
    }

    private static void Git(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var key in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE" }) start.Environment.Remove(key);
        var watch = Stopwatch.StartNew();
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000)) { process.Kill(true); process.WaitForExit(30_000); throw new TimeoutException("fixture Git"); }
        Task.WhenAll(output, error).Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
        process.ExitCode.ShouldBe(0, error.Result);
        Console.WriteLine($"C1820 phase=fixture-git elapsedMs={watch.ElapsedMilliseconds}");
    }
}
