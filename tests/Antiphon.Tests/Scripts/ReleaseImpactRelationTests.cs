using System.Text.Json;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
public sealed class ReleaseImpactRelationTests
{

    [Test]
    [Timeout(30_000)]
    public void UniqueBindingsPruneOnlyTheirOwnContributions()
    {
        const string project = "tests/T/T.csproj";
        const string north = "tests/T/A.cs";
        const string south = "tests/T/B.cs";
        const string use = "tests/T/Use.cs";
        using var fixture = new ReleaseImpactTypeProofFixture();
        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [project] = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
            [north] = "namespace North; public class Probe {}\n",
            [south] = "namespace South; public class Probe {}\n",
            [use] = "using North; namespace Cases; public class SpecTests { public void Check() { _ = typeof(Probe); } }\n",
        };
        var binding = fixture.Capture(sources, new Dictionary<string, string>(),
            [new(project, "T", [north, south, use], [], [fixture.Core])]);
        binding.Eligible.ShouldBeTrue("Q.fixture: " + binding.Reason);
        var proof = ReleaseImpactGraphBuilder.BuildWithTypeProof(sources, binding);
        TypePaths(proof.Conservative).ShouldBe([north, south], "Q.before");
        TypePaths(proof.Refined).ShouldBe([north], "Q.unique");

        static IEnumerable<string> TypePaths(ReleaseImpactGraph graph) => graph.Edges
            .Where(edge => edge.Classes.Any(consumer => consumer.Project == project && consumer.FullName == "Cases.SpecTests"
                && consumer.Kinds.Contains("type", StringComparer.Ordinal)))
            .Select(edge => edge.Path);
    }
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
            internal partial class Container<T> {
                internal partial class Actual<U> {
                    [Test] public void Runs() { var text = """public class PhantomTests { [Test] public void Fake() {} }"""; }
                }
            }
            // public class CommentTests {}
            """";
        sources["tests/A/Other.cs"] = "namespace Census; internal partial class Container<T> { internal partial class Actual<U> {} }";
        var graph = Build(sources);
        graph.GetProperty("universe").EnumerateArray().Select(Identity)
            .ShouldBe(new[] { "tests/A/A.csproj::Census.Container`1+Actual`1" });
        Members(graph, "tests/A/Real.cs").ShouldBe(new[] { "tests/A/A.csproj::Census.Container`1+Actual`1" });
        AssertIncomplete(graph);
    }

    // CARD-1868 A-1. A delegate is not a BaseTypeDeclarationSyntax, so a file declaring only
    // delegates (top-level, generic or nested in a class no consumer names) got no type edge.
    [Test]
    public void DelegateOnlyFileGetsTypeEdge()
    {
        var sources = Sources();
        sources["shared/Callbacks.cs"] = "namespace Shared; public delegate void ProbeCallback(int value); public delegate T ProbeFactory<T>();";
        sources["shared/Holder.cs"] = "namespace Shared; public class ProbeHolder { public delegate void ProbeNested(); }";
        sources["tests/A/Consumer.cs"] = """
            namespace A;
            public class CallbackTests { [Test] public void Runs() { ProbeCallback callback = _ => { }; } }
            public class FactoryTests { [Test] public void Runs() { ProbeFactory<int> factory = () => 1; } }
            public class NestedTests : Shared.ProbeBase { [Test] public void Runs() { ProbeNested nested = () => { }; } }
            """;
        var graph = Build(sources);
        Members(graph, "shared/Callbacks.cs").ShouldBe(new[] { "tests/A/A.csproj::A.CallbackTests", "tests/A/A.csproj::A.FactoryTests" });
        Kinds(graph, "shared/Callbacks.cs").ShouldBe(new[] { "type" });
        Members(graph, "shared/Holder.cs").ShouldBe(new[] { "tests/A/A.csproj::A.NestedTests" });
        Kinds(graph, "shared/Holder.cs").ShouldBe(new[] { "type" });
        AssertIncomplete(graph);
    }

    private static string[] Kinds(JsonElement graph, string path) => graph.GetProperty("edges").EnumerateArray()
        .Where(x => x.GetProperty("path").GetString() == path).SelectMany(x => x.GetProperty("classes").EnumerateArray())
        .SelectMany(x => x.GetProperty("kinds").EnumerateArray()).Select(x => x.GetString()!).Distinct().Order(StringComparer.Ordinal).ToArray();

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
        using var document = JsonDocument.Parse(ReleaseImpactGraphBuilder.Serialize(ReleaseImpactGraphBuilder.Build(sources)));
        return document.RootElement.Clone();
    }
}
