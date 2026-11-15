using System.Diagnostics;
using System.Text.Json;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
public sealed class ReleaseImpactRuntimeTests
{
    [Test]
    public void HelperChainReachesPoolTests()
    {
        var inputs = Fixture();
        inputs["tests/Consumer/TestDbFixture.cs"] = "namespace Example; class TestDbFixture { TestDbFixtureLifecycle lifecycle; }";
        inputs["tests/Consumer/TestDbFixtureLifecycle.cs"] = "namespace Example; class TestDbFixtureLifecycle { }";
        inputs["tests/Consumer/Pool.cs"] = "namespace Example; class AgentTaskPoolTests { TestDbFixture fixture; }";
        var graph = Build(inputs);
        Members(graph, "tests/Consumer/TestDbFixtureLifecycle.cs").ShouldBe(ConsumerClasses);
        Members(graph, "tests/Consumer/TestDbFixture.cs").ShouldBe(ConsumerClasses);
        Members(graph, "src/Unrelated/Other.cs").ShouldBeEmpty();
        graph.Edges.Single(e => e.Path == "tests/Consumer/TestDbFixtureLifecycle.cs").Classes
            .All(c => c.Kinds.Contains("projectClosure")).ShouldBeTrue();
        AssertIncomplete(graph);
    }

    [Test]
    public void AssemblyHookExpandsAssembly()
    {
        var inputs = Fixture();
        inputs["tests/Consumer/Hook.cs"] = "namespace Example; class TestDbFixtureAssemblyHook { [Before(Assembly)] public void Start() {} }";
        var graph = Build(inputs);
        Members(graph, "tests/Consumer/Hook.cs").ShouldBe(ConsumerClasses);
        // No test references the hook. A downstream test project and a linked helper
        // still receive the conservative closure, without a 25-class cost cap.
        inputs["tests/Downstream/Downstream.csproj"] = "<Project><ItemGroup><ProjectReference Include=\"../Consumer/Consumer.csproj\" /></ItemGroup></Project>";
        for (var n = 0; n < 26; n++)
            inputs[$"tests/Downstream/T{n}.cs"] = $"namespace Downstream; class T{n}Tests {{}}";
        inputs["tests/Consumer/Consumer.csproj"] = "<Project><ItemGroup><Compile Include=\"../../shared/Hook.cs\" /></ItemGroup></Project>";
        inputs["shared/Hook.cs"] = inputs["tests/Consumer/Hook.cs"];
        graph = Build(inputs);
        Members(graph, "shared/Hook.cs").Length.ShouldBe(28);
        Members(graph, "shared/Hook.cs").ShouldContain("tests/Downstream/Downstream.csproj::Downstream.T25Tests");
        AssertIncomplete(graph);
    }

    [Test]
    public void AppsettingsAndContentExpand()
    {
        var inputs = Fixture();
        inputs["src/Host/appsettings.json"] = "{}";
        inputs["src/Host/appsettings.Development.json"] = "{}";
        inputs["external/fixtures/old.json"] = "old";
        inputs["tests/Consumer/Host.cs"] = "namespace Example; class HostLauncher { object Start(string host) => System.Diagnostics.Process.Start(host); }";
        inputs["tests/selector-runtime-inputs.json"] = Manifest(
            Declaration("host", "host", ["src/Host/**"], ["tests/Consumer/Host.cs"]),
            Declaration("fixtures", "fixture", ["external/fixtures/**/*.json"], ["tests/Consumer/Host.cs"]));
        var old = Build(inputs);
        foreach (var path in new[] { "src/Host/Program.cs", "src/Host/appsettings.json", "src/Host/appsettings.Development.json", "external/fixtures/old.json" })
        {
            Members(old, path).ShouldBe(ConsumerClasses, path);
            old.Edges.Single(e => e.Path == path).Classes.All(c => c.Kinds.Any(k => k.StartsWith("runtimeInput:", StringComparison.Ordinal))).ShouldBeTrue();
        }
        Members(old, "src/Unrelated/Other.cs").ShouldBeEmpty();
        inputs.Remove("external/fixtures/old.json");
        inputs["external/fixtures/nested/new.json"] = "new";
        var current = Build(inputs);
        Members(current, "external/fixtures/old.json").ShouldBeEmpty();
        var union = ReleaseImpactProjectGraph.Union(old, current);
        Members(union, "external/fixtures/old.json").ShouldBe(ConsumerClasses);
        Members(union, "external/fixtures/nested/new.json").ShouldBe(ConsumerClasses);
        // Evaluated-item source inventory includes linked config/content; no filename heuristics.
        inputs["tests/Consumer/Consumer.csproj"] = "<Project><ItemGroup><Content Include=\"../../external/fixtures/**/*.json\" /><None Include=\"../../src/Host/appsettings*.json\" /></ItemGroup></Project>";
        Members(Build(inputs), "external/fixtures/nested/new.json").ShouldBe(ConsumerClasses);
        inputs["tests/Consumer/Read.cs"] = "namespace Example; class Reader { string Read() => System.IO.File.ReadAllText(\"external/fixtures/nested/new.json\"); }";
        AssertReason(Build(inputs), "external/fixtures/nested/new.json", "runtimeInput:literal");
        // Class declarations preserve full names, even when another namespace uses the
        // same short name. Shared helpers still require assembly scope.
        inputs["tests/Consumer/Shadow.cs"] = "namespace Shadow; class AgentTaskPoolTests {}";
        inputs["tests/selector-runtime-inputs.json"] = Manifest(Declaration("one-class", "content",
            ["src/Unrelated/Other.cs"], ["tests/Consumer/Pool.cs"], "class", "Example.AgentTaskPoolTests"));
        Members(Build(inputs), "src/Unrelated/Other.cs").ShouldBe(
            ["tests/Consumer/Consumer.csproj::Example.AgentTaskPoolTests"]);
        AssertRepositoryDeclarations();
        AssertIncomplete(current);
    }

    [Test]
    public void UnboundRuntimeInputIsIncomplete()
    {
        var inputs = Fixture();
        foreach (var expression in new[]
        {
            "System.IO.File.ReadAllText(name)", "System.IO.Directory.GetFiles(name)",
            "config.AddJsonFile(name)", "System.Diagnostics.Process.Start(name)",
            "System.Environment.GetEnvironmentVariable(name)",
            "System.Environment.GetEnvironmentVariable(\"SELECTOR_RUNTIME_FIXTURE\")",
            "ReadAllText(name)", "new System.IO.StreamReader(name)",
            "System.IO.File.ReadAllText(\"missing.json\")",
        })
        {
            inputs["tests/Consumer/Read.cs"] = $"namespace Example; class Reader {{ object Read(string name) => {expression}; }}";
            var graph = Build(inputs);
            graph.IncompleteReasons.ShouldContain(r => r.StartsWith("runtime-reader-unresolved:tests/Consumer/Read.cs:", StringComparison.Ordinal), expression);
            AssertIncomplete(graph);
        }
        inputs.Remove("tests/Consumer/Read.cs");
        inputs["tests/selector-runtime-inputs.json"] = Manifest(Declaration("missing", "fixture", ["absent/**"], ["tests/Consumer/Pool.cs"]));
        Build(inputs).IncompleteReasons.ShouldContain("runtime-declaration-input-missing:missing:absent/**");
        foreach (var manifest in new[]
        {
            "{}", "{\"schema\":2,\"declarations\":[]}", "{\"schema\":1,\"schema\":1,\"declarations\":[]}",
            "{\"schema\":1,\"declarations\":[],\"ignoreUnknown\":true}",
            Manifest(Declaration("escape", "fixture", ["../outside/**"], ["tests/Consumer/Pool.cs"])),
            Manifest(Declaration("wrong-kind", "ignore", ["src/Host/**"], ["tests/Consumer/Pool.cs"])),
            Manifest(Declaration("duplicate", "fixture", ["src/Host/**"], ["tests/Consumer/Pool.cs"]),
                Declaration("duplicate", "fixture", ["src/Host/**"], ["tests/Consumer/Pool.cs"])),
        })
        {
            inputs["tests/selector-runtime-inputs.json"] = manifest;
            Build(inputs).IncompleteReasons.ShouldContain("runtime-declarations-invalid");
        }
        inputs["tests/selector-runtime-inputs.json"] = Manifest(Declaration("witness", "fixture", ["src/Host/**"], ["tests/Consumer/Pool.cs"]))
            .Replace("AppsettingsAndContentExpand", "MissingWitness", StringComparison.Ordinal);
        Build(inputs).IncompleteReasons.ShouldContain("runtime-declaration-witness-missing:witness");
        inputs["tests/selector-runtime-inputs.json"] = Manifest(Declaration("class", "content",
            ["src/Host/**"], ["tests/Consumer/Pool.cs"], "class", "Missing.AgentTaskPoolTests"));
        Build(inputs).IncompleteReasons.ShouldContain("runtime-declaration-consumer-missing:class");
        inputs["tests/selector-runtime-inputs.json"] = Manifest(Declaration("source", "content",
            ["src/Host/**"], ["missing.cs"]));
        Build(inputs).IncompleteReasons.ShouldContain("runtime-declaration-source-unresolved:source");
        inputs["tests/Consumer/Helper.cs"] = "namespace Example; class Helper {}";
        inputs["tests/selector-runtime-inputs.json"] = Manifest(Declaration("helper", "fixture",
            ["src/Host/**"], ["tests/Consumer/Helper.cs"], "class", "Example.AgentTaskPoolTests"));
        Build(inputs).IncompleteReasons.ShouldContain("runtime-declaration-source-unresolved:helper");
        inputs["tests/selector-runtime-inputs.json"] = Manifest(Declaration("environment", "environment",
            ["src/Host/**"], ["tests/Consumer/Pool.cs"]));
        Build(inputs).IncompleteReasons.ShouldContain("runtime-environment-identity-unavailable:environment");
        inputs.Remove("tests/selector-runtime-inputs.json");
        Build(inputs).IncompleteReasons.ShouldContain("runtime-declarations-missing");
    }

    private static void AssertReason(ReleaseImpactGraph graph, string path, string reason) =>
        graph.Edges.Single(e => e.Path == path).Classes.All(c => c.Kinds.Contains(reason)).ShouldBeTrue();

    private static string[] ConsumerClasses => ["tests/Consumer/Consumer.csproj::Example.AgentTaskPoolTests", "tests/Consumer/Consumer.csproj::Example.SessionMessageQueueServiceTests"];
    private static object Declaration(string id, string kind, string[] paths, string[] sources,
        string scope = "assembly", string consumerClass = "") => new
    {
        id, kind, consumerProject = "tests/Consumer/Consumer.csproj", scope, consumerClass,
        environmentNames = kind == "environment" ? new[] { "SELECTOR_RUNTIME_FIXTURE" } : Array.Empty<string>(),
        sourcePaths = sources, inputs = paths, rationale = "Owned fixture inputs for the consumer assembly.",
        witness = new { path = "tests/Witness/Witness.cs", method = "AppsettingsAndContentExpand" },
    };
    private static string Manifest(params object[] declarations) => JsonSerializer.Serialize(new { schema = 1, declarations });
    private static Dictionary<string, string> Fixture() => new(StringComparer.Ordinal)
    {
        ["tests/Consumer/Consumer.csproj"] = "<Project />",
        ["tests/Consumer/Pool.cs"] = "namespace Example; class AgentTaskPoolTests {}",
        ["tests/Consumer/Queue.cs"] = "namespace Example; class SessionMessageQueueServiceTests {}",
        ["src/Host/Host.csproj"] = "<Project />",
        ["src/Host/Program.cs"] = "namespace Host; class Program {}",
        ["src/Unrelated/Other.csproj"] = "<Project />",
        ["src/Unrelated/Other.cs"] = "namespace Unrelated; class Other {}",
        ["tests/Witness/Witness.csproj"] = "<Project />",
        ["tests/Witness/Witness.cs"] = "namespace Witness; class Contract { [Test] public void AppsettingsAndContentExpand() {} }",
        ["tests/selector-runtime-inputs.json"] = Manifest(),
    };
    private static string[] Members(ReleaseImpactGraph graph, string path) => graph.Edges.Where(e => e.Path == path)
        .SelectMany(e => e.Classes).Select(c => c.Project + "::" + c.FullName).Order(StringComparer.Ordinal).ToArray();
    private static void AssertIncomplete(ReleaseImpactGraph graph)
    {
        graph.Complete.ShouldBeFalse();
        graph.Fallback.ShouldBe("Full");
        graph.IncompleteReasons.ShouldContain("project-evaluation-unavailable");
    }
    private static ReleaseImpactGraph Build(IReadOnlyDictionary<string, string> inputs)
    {
        var timer = Stopwatch.StartNew();
        var result = ReleaseImpactRuntimeInputs.Build(inputs);
        Console.WriteLine($"C1820 phase=runtime-inputs elapsedMs={timer.ElapsedMilliseconds}");
        return result;
    }

    private static void AssertRepositoryDeclarations()
    {
        var timer = Stopwatch.StartNew();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln")))
            directory = directory.Parent;
        var root = directory?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln");
        // Real declaration/readers, isolated source inventory: never start the declared host.
        var paths = new[]
        {
            "tests/selector-runtime-inputs.json", "tests/Antiphon.Tests/Antiphon.Tests.csproj",
            "tests/Antiphon.Tests/TestHelpers/AntiphonWebAppFactory.cs",
            "tests/Antiphon.Tests/Agents/GrokStartupReadinessTests.cs",
            "tests/Antiphon.Tests/Agents/Fixtures/card0778/startup-frames.json",
            "tests/Antiphon.Tests/Scripts/ReleaseImpactRuntimeTests.cs",
            "server/Antiphon.Server.csproj", "server/Program.cs", "server/appsettings.json",
        };
        var inputs = paths.ToDictionary(p => p, p => File.ReadAllText(Path.Combine(root, p)), StringComparer.Ordinal);
        Console.WriteLine($"C1820 phase=declaration-fixture-io elapsedMs={timer.ElapsedMilliseconds}");
        using var manifest = JsonDocument.Parse(inputs["tests/selector-runtime-inputs.json"]);
        manifest.RootElement.GetProperty("declarations").EnumerateArray().Select(d => d.GetProperty("id").GetString())
            .Order(StringComparer.Ordinal).ShouldBe(["grok-startup-fixture", "server-host"]);
        var graph = Build(inputs);
        graph.IncompleteReasons.Where(r => r.StartsWith("runtime-declaration", StringComparison.Ordinal)).ShouldBeEmpty();
        string[] expected =
        [
            "tests/Antiphon.Tests/Antiphon.Tests.csproj::Antiphon.Tests.Agents.GrokStartupReadinessTests",
            "tests/Antiphon.Tests/Antiphon.Tests.csproj::Antiphon.Tests.Scripts.ReleaseImpactRuntimeTests",
        ];
        foreach (var path in new[] { "server/Program.cs", "server/appsettings.json" })
        {
            Members(graph, path).ShouldBe(expected);
            AssertReason(graph, path, "runtimeInput:server-host");
        }
        var fixture = "tests/Antiphon.Tests/Agents/Fixtures/card0778/startup-frames.json";
        Members(graph, fixture).ShouldBe(expected);
        AssertReason(graph, fixture, "runtimeInput:grok-startup-fixture");
        // A stale witness must refuse that declaration; existing project/type positives survive.
        inputs["tests/selector-runtime-inputs.json"] = inputs["tests/selector-runtime-inputs.json"]
            .Replace("AppsettingsAndContentExpand", "AbsentMethod", StringComparison.Ordinal);
        var missingWitness = Build(inputs);
        missingWitness.IncompleteReasons.ShouldContain("runtime-declaration-witness-missing:server-host");
        missingWitness.IncompleteReasons.ShouldContain("runtime-declaration-witness-missing:grok-startup-fixture");
        missingWitness.Edges.SelectMany(e => e.Classes).SelectMany(c => c.Kinds)
            .ShouldNotContain("runtimeInput:server-host");
        AssertIncomplete(graph);
    }
}
