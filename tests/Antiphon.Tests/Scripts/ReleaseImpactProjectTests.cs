using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
[ClassDataSource<ReleaseImpactProjectFixture>(Shared = SharedType.PerClass)]
public sealed class ReleaseImpactProjectTests(ReleaseImpactProjectFixture fixture)
{
    [Test]
    public void DiAndInterfaceConsumersExpand()
    {
        fixture.TestAssemblyReferences.ShouldContain("Fixture.Product");
        var graph = Build(fixture.Sources);
        foreach (var path in new[] { "src/Product/Queue.cs", "src/Product/Registration.cs", "src/Product/Types.cs" })
            Members(graph, path).ShouldBe(Expected, path);
        graph.Edges.Single(e => e.Path == "src/Product/Queue.cs").Classes
            .All(c => c.Kinds.Contains("projectClosure")).ShouldBeTrue();
        Members(graph, "src/Unrelated/Other.cs").ShouldBeEmpty();
        var transitive = new Dictionary<string, string>(fixture.Sources)
        {
            ["src/Bridge/Bridge.csproj"] = "<Project><ItemGroup><ProjectReference Include=\"../Product/Product.csproj\" /></ItemGroup></Project>",
            ["tests/Consumer/Consumer.csproj"] = "<Project><ItemGroup><ProjectReference Include=\"../../src/Bridge/Bridge.csproj\" /></ItemGroup></Project>",
        };
        Members(Build(transitive), "src/Product/Queue.cs").ShouldBe(Expected);
        AssertIncomplete(graph);
    }

    [Test]
    public void ReflectionIsBoundOrUnknown()
    {
        var sources = new Dictionary<string, string>(fixture.Sources)
        {
            ["tests/Consumer/Consumer.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\" />",
            ["tests/Consumer/Reflection.cs"] = "namespace Example; internal partial class ReflectionTests { object Read() => System.Type.GetType(\"Product.Queue, Fixture.Product\"); }",
        };
        var graph = Build(sources);
        Members(graph, "src/Product/Queue.cs").ShouldContain("tests/Consumer/Consumer.csproj::Example.ReflectionTests");
        sources["tests/Consumer/Reflection.cs"] = "namespace Example; internal partial class ReflectionTests { object Read(string name) => System.Type.GetType(name); }";
        graph = Build(sources);
        graph.IncompleteReasons.ShouldContain("reflection-target-unresolved:tests/Consumer/Reflection.cs");
        sources["tests/Consumer/Reflection.cs"] = "namespace Example; internal partial class ReflectionTests { object Read() => System.Reflection.Assembly.Load(\"Missing.Plugin\"); }";
        Build(sources).IncompleteReasons.ShouldContain("reflection-target-unresolved:tests/Consumer/Reflection.cs");
        // One source per reflective load. Recognised Type.GetType / Assembly.Load stay above;
        // every other string-taking reflection entry must be incomplete, never skipped.
        foreach (var source in new[]
        {
            "namespace Example; internal partial class ReflectionTests { object Read(System.Reflection.Assembly asm) => asm.GetType(\"Product.Queue\"); }",
            "namespace Example; internal partial class ReflectionTests { object Read() => System.Activator.CreateInstance(\"Fixture.Product\", \"Product.Queue\"); }",
            "namespace Example; internal partial class ReflectionTests { object Read() => new System.Runtime.Loader.AssemblyLoadContext(\"probe\").LoadFromAssemblyName(new System.Reflection.AssemblyName(\"Fixture.Product\")); }",
            "namespace Example; internal partial class ReflectionTests { object Read() => new System.Runtime.Loader.AssemblyLoadContext(\"probe\").LoadFromAssemblyPath(\"plugin.dll\"); }",
            "namespace Example; internal partial class ReflectionTests { object Read() => System.AppDomain.CurrentDomain.Load(\"Fixture.Product\"); }",
            "using static System.Type;\nnamespace Example { internal partial class ReflectionTests { object Read() => GetType(\"Product.Queue, Fixture.Product\"); } }",
            "namespace Example; internal partial class ReflectionTests { object Read(System.Reflection.Assembly asm) => asm.LoadFrom(\"plugin.dll\"); }",
            "namespace Example; internal partial class ReflectionTests { object Read(System.Reflection.Assembly asm) => asm.LoadFile(\"plugin.dll\"); }",
            "namespace Example; internal partial class ReflectionTests { object Read(System.Reflection.Assembly asm) => asm.GetTypes(); }",
            "namespace Example; internal partial class ReflectionTests { object Read(System.Reflection.Assembly asm) => asm.GetExportedTypes(); }",
        })
        {
            sources["tests/Consumer/Reflection.cs"] = source;
            Build(sources).IncompleteReasons.ShouldContain("reflection-target-unresolved:tests/Consumer/Reflection.cs", source);
        }
        var named = new Dictionary<string, string>(fixture.Sources)
        {
            ["src/Product/Product.csproj"] = "<Project><PropertyGroup><AssemblyName>$(ConfiguredName)</AssemblyName></PropertyGroup></Project>",
        };
        var namedGraph = Build(named);
        namedGraph.IncompleteReasons.ShouldContain("project-expression-unresolved:src/Product/Product.csproj:$(ConfiguredName)");
        AssertIncomplete(namedGraph);
        AssertIncomplete(graph);
    }

    [Test]
    public void GeneratedInputsExpand()
    {
        var graph = Build(fixture.Sources);
        foreach (var path in new[] { "shared/Generated.cs", "shared/schema.json", "shared/Generator.dll", "shared/inputs.props", "shared/config.json", "Directory.Build.props" })
            Members(graph, path).ShouldBe(Expected, path);
        graph.IncompleteReasons.ShouldContain("project-evaluation-unavailable");
        graph.IncompleteReasons.ShouldContain("generated-provenance-unavailable:src/Product/Product.csproj");
        var missing = new Dictionary<string, string>(fixture.Sources);
        missing.Remove("shared/inputs.props");
        Build(missing).IncompleteReasons.ShouldContain("project-input-missing:src/Product/Product.csproj:shared/inputs.props");
        var dynamic = new Dictionary<string, string>(fixture.Sources);
        dynamic["src/Product/Product.csproj"] = "<Project><Import Project=\"$(Unknown)/inputs.props\" /></Project>";
        Build(dynamic).IncompleteReasons.ShouldContain("project-expression-unresolved:src/Product/Product.csproj:$(Unknown)/inputs.props");
        AssertIncomplete(graph);
    }

    [Test]
    public void OldAndNewConditionalReferencesUnion()
    {
        var old = new Dictionary<string, string>(fixture.Sources);
        var current = new Dictionary<string, string>(fixture.Sources);
        current["src/Replacement/Replacement.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\" />";
        current["src/Replacement/New.cs"] = "namespace Replacement; public class NewQueue {}";
        old["tests/Consumer/Consumer.csproj"] = "<Project><ItemGroup Condition=\"'$(TargetFramework)' == 'net9.0'\"><ProjectReference Include=\"../../src/Product/Product.csproj\" /></ItemGroup></Project>";
        current["tests/Consumer/Consumer.csproj"] = "<Project><ItemGroup Condition=\"'$(TargetFramework)' == 'net10.0'\"><ProjectReference Include=\"../../src/Replacement/Replacement.csproj\" /></ItemGroup></Project>";
        current.Remove("src/Product/Queue.cs");
        var graphs = new[] { Build(old), Build(current) };
        foreach (var path in new[] { "src/Product/Queue.cs", "src/Replacement/New.cs" })
            graphs.SelectMany(g => Members(g, path)).Distinct().Order(StringComparer.Ordinal).ToArray().ShouldBe(Expected, path);
        graphs.All(g => g.IncompleteReasons.Contains("project-condition-unevaluated:tests/Consumer/Consumer.csproj")).ShouldBeTrue();
        // A cycle terminates without dropping any downstream class, and malformed XML stays unknown.
        current["src/Replacement/Replacement.csproj"] = "<Project><ItemGroup><ProjectReference Include=\"../../tests/Consumer/Consumer.csproj\" /></ItemGroup></Project>";
        Members(Build(current), "src/Replacement/New.cs").ShouldBe(Expected);
        current["src/Replacement/Replacement.csproj"] = "<Project>";
        Build(current).IncompleteReasons.ShouldContain("project-parse-unresolved:src/Replacement/Replacement.csproj");
    }

    private static string[] Expected => ["tests/Consumer/Consumer.csproj::Example.FirstTests", "tests/Consumer/Consumer.csproj::Example.SecondTests"];
    private static string[] Members(ReleaseImpactGraph graph, string path) => graph.Edges.Where(e => e.Path == path)
        .SelectMany(e => e.Classes).Select(c => c.Project + "::" + c.FullName).Distinct().Order(StringComparer.Ordinal).ToArray();
    private static void AssertIncomplete(ReleaseImpactGraph graph)
    {
        graph.Complete.ShouldBeFalse();
        graph.Fallback.ShouldBe("Full");
        graph.IncompleteReasons.ShouldContain("project-evaluation-unavailable");
    }

    private static ReleaseImpactGraph Build(IReadOnlyDictionary<string, string> sources)
    {
        var watch = Stopwatch.StartNew();
        var graph = ReleaseImpactProjectGraph.Build(sources);
        Console.WriteLine($"C1820 phase=project-closure elapsedMs={watch.ElapsedMilliseconds}");
        return graph;
    }
}

public sealed class ReleaseImpactProjectFixture
{
    public Dictionary<string, string> Sources { get; } = new(StringComparer.Ordinal)
    {
        ["Directory.Build.props"] = "<Project />",
        ["src/Product/Product.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><AssemblyName>Fixture.Product</AssemblyName></PropertyGroup><Import Project=\"../../shared/inputs.props\" /><ItemGroup><Compile Include=\"../../shared/Generated.cs\" /><AdditionalFiles Include=\"../../shared/schema.json\" /><Analyzer Include=\"../../shared/Generator.dll\" /><Content Include=\"../../shared/config.json\" /></ItemGroup></Project>",
        ["src/Product/Queue.cs"] = "namespace Product; public class Queue : IQueue { }",
        ["src/Product/Types.cs"] = "namespace Product; public interface IQueue { } public enum Mode { One } public struct Options { } public delegate void Notify(); public class MarkerAttribute : System.Attribute { }",
        ["src/Product/Registration.cs"] = "namespace Product; public class Registration { public object Resolve() => new Queue(); }",
        ["shared/Generated.cs"] = "namespace Product; public class Generated { }",
        ["shared/schema.json"] = "{}",
        ["shared/Generator.dll"] = "opaque fixture generator identity",
        ["shared/config.json"] = "{}",
        ["shared/inputs.props"] = "<Project><ItemGroup><None Include=\"config.json\" /></ItemGroup></Project>",
        ["tests/Consumer/Consumer.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><AssemblyName>Fixture.Tests</AssemblyName></PropertyGroup><ItemGroup><ProjectReference Include=\"../../src/Product/Product.csproj\" /></ItemGroup></Project>",
        ["tests/Consumer/First.cs"] = "namespace Example; internal partial class FirstTests { object Run() => new Product.Registration().Resolve(); }",
        ["tests/Consumer/Second.cs"] = "using Alias = Product.IQueue; namespace Example { [Product.Marker] internal partial class SecondTests { Alias value; Product.Mode mode; Product.Options options; Product.Notify callback; } } namespace Shadow { public interface IQueue {} }",
        ["src/Unrelated/Unrelated.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\" />",
        ["src/Unrelated/Other.cs"] = "namespace Other; public class OtherQueue {}",
    };
    public string[] TestAssemblyReferences { get; }

    public ReleaseImpactProjectFixture()
    {
        // Real source-bound assemblies, compiled once per host in memory. No SDK process or disk fixture.
        var core = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var product = Compile("Fixture.Product", Sources.Where(p => p.Key.StartsWith("src/Product/") && p.Key.EndsWith(".cs"))
            .Append(new KeyValuePair<string, string>("shared/Generated.cs", Sources["shared/Generated.cs"])), [core]);
        var tests = Compile("Fixture.Tests", Sources.Where(p => p.Key.StartsWith("tests/Consumer/") && p.Key.EndsWith(".cs")),
            [core, MetadataReference.CreateFromImage(product)]);
        using var stream = new MemoryStream(tests);
        var watch = Stopwatch.StartNew();
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        TestAssemblyReferences = reader.AssemblyReferences.Select(h => reader.GetString(reader.GetAssemblyReference(h).Name)).ToArray();
        Console.WriteLine($"C1820 phase=fixture-metadata elapsedMs={watch.ElapsedMilliseconds}");
    }

    private static byte[] Compile(string name, IEnumerable<KeyValuePair<string, string>> sources, MetadataReference[] references)
    {
        var watch = Stopwatch.StartNew();
        var compilation = CSharpCompilation.Create(name, sources.Select(p => CSharpSyntaxTree.ParseText(p.Value, path: p.Key)), references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes = new MemoryStream();
        var result = compilation.Emit(bytes);
        result.Success.ShouldBeTrue(string.Join("\n", result.Diagnostics));
        Console.WriteLine($"C1820 phase=fixture-compile assembly={name} elapsedMs={watch.ElapsedMilliseconds}");
        return bytes.ToArray();
    }
}
