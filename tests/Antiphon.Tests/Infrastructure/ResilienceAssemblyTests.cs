using System.Xml.Linq;
using Antiphon.Resilience;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1829. The seven Resilience classes leave <c>Antiphon.Tests</c> for
/// <c>Antiphon.Resilience.Tests</c>. Namespaces stay. The mirror's assembly hook
/// is local, because a <c>[Before(Assembly)]</c> in another assembly does not fire here.
/// </summary>
[Category("Unit")]
public sealed class ResilienceAssemblyTests
{
    private static readonly string[] MovedClasses =
    [
        "DatabaseReadResilienceTests",
        "HttpReadResilienceTests",
        "HttpResilienceRegistrationTests",
        "ResilienceBudgetTests",
        "ResilienceCircuitTests",
        "ResilienceOptionsAndTelemetryTests",
        "ResiliencePredicateTests",
    ];

    [Test]
    public void Mirror_holds_the_resilience_tests()
    {
        typeof(ResilienceBudget).Assembly.GetName().Name.ShouldBe("Antiphon.Resilience");

        var root = RepositoryRoot();
        var project = Path.Combine(root, "tests", "Antiphon.Resilience.Tests", "Antiphon.Resilience.Tests.csproj");
        File.Exists(project).ShouldBeTrue(project);
        var references = ProjectReferences(project);
        references.ShouldContain(include =>
            include.Contains("Antiphon.Resilience.csproj", StringComparison.Ordinal)
            && !include.Contains("Antiphon.Resilience.Tests", StringComparison.Ordinal));

        var production = Path.Combine(root, "src", "Antiphon.Resilience", "Antiphon.Resilience.csproj");
        File.ReadAllText(production).ShouldContain("Antiphon.Resilience.Tests");

        var mirror = SourceFiles(Path.Combine(root, "tests", "Antiphon.Resilience.Tests"));
        var antiphon = SourceFiles(Path.Combine(root, "tests", "Antiphon.Tests"));
        foreach (var name in MovedClasses)
        {
            CountClass(mirror, name).ShouldBe(1, name);
            CountClass(antiphon, name).ShouldBe(0, name);
        }

        CountClass(mirror, "ResilienceTestHost").ShouldBe(1);
        CountClass(antiphon, "ResilienceTestHost").ShouldBe(0);
        CountClass(mirror, "ResilienceAssemblyTests").ShouldBe(1);
        CountClass(antiphon, "ResilienceAssemblyTests").ShouldBe(0);
        CountClass(antiphon, "StaleWorkingTranscriptWatchdogTests").ShouldBe(1);
        CountClass(mirror, "StaleWorkingTranscriptWatchdogTests").ShouldBe(0);
        File.Exists(Path.Combine(root, "tests", "Antiphon.E2E", "Fixtures", "PipelineContractScenario.cs")).ShouldBeTrue();

        const string ns = "namespace Antiphon.Tests.Infrastructure.Resilience;";
        foreach (var name in MovedClasses)
        {
            var file = mirror.Single(path => CountClass([path], name) == 1);
            File.ReadAllText(file).ShouldContain(ns);
        }

        CountTestAttributes(mirror).ShouldBe(55 + 2);
    }

    [Test]
    public void Assembly_hook_is_local_to_the_mirror()
    {
        var root = RepositoryRoot();
        var path = Path.Combine(root, "tests", "Antiphon.Resilience.Tests", "AssemblyHooks.cs");
        File.Exists(path).ShouldBeTrue(path);
        var text = File.ReadAllText(path);
        text.ShouldContain("[Before(Assembly)]");
        text.ShouldContain("[After(Assembly)]");
        text.ShouldContain("ProductionRunnerGuard.PointEveryProgramBootAwayFromTheProductionRunner");
        text.ShouldContain("PtyBackendEnvGuard.ClearInheritedPtyBackend");
        text.ShouldContain("CheckpointTempRootSweep");
        text.ShouldContain("TestDbFixture.InitializeAsync");
        text.ShouldContain("TestDbFixture.DisposeAsync");
        CountClass(SourceFiles(Path.Combine(root, "tests", "Antiphon.Tests")), "AssemblyHooks").ShouldBe(0);
    }

    private static string[] ProjectReferences(string path) =>
        XDocument.Load(path)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => (string?)element.Attribute("Include") ?? "")
            .ToArray();

    private static string[] SourceFiles(string directory)
    {
        if (!Directory.Exists(directory))
            return [];
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var child in Directory.EnumerateDirectories(current))
            {
                var name = Path.GetFileName(child);
                if (name is "bin" or "obj" || name.StartsWith("bin-", StringComparison.Ordinal))
                    continue;
                pending.Push(child);
            }

            foreach (var file in Directory.EnumerateFiles(current, "*.cs"))
                files.Add(file);
        }

        return files.ToArray();
    }

    private static int CountTestAttributes(IEnumerable<string> files)
    {
        var count = 0;
        foreach (var file in files)
        {
            foreach (var line in File.ReadLines(file))
            {
                if (line.Trim() == "[Test]")
                    count++;
            }
        }

        return count;
    }

    private static int CountClass(IEnumerable<string> files, string name)
    {
        var needle = "class " + name;
        var count = 0;
        foreach (var file in files)
        {
            foreach (var line in File.ReadLines(file))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;
                var index = trimmed.IndexOf(needle, StringComparison.Ordinal);
                if (index < 0)
                    continue;
                var after = index + needle.Length;
                if (after < trimmed.Length && (char.IsLetterOrDigit(trimmed[after]) || trimmed[after] == '_'))
                    continue;
                count++;
            }
        }

        return count;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
