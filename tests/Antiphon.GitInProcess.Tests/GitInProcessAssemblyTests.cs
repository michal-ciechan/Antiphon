using System.Xml.Linq;
using Antiphon.GitInProcess;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1830. The five GitInProcess classes leave <c>Antiphon.Tests</c> for
/// <c>Antiphon.GitInProcess.Tests</c>. Namespaces stay. The mirror's assembly hook
/// is local, because a <c>[Before(Assembly)]</c> in another assembly does not fire here.
/// </summary>
[Category("Unit")]
public sealed class GitInProcessAssemblyTests
{
    private static readonly string[] MovedClasses =
    [
        "InProcessGitCommandsTests",
        "LibGit2VsGitDifferentialTests",
        "LibGit2VsGitJunctionTests",
        "LibGit2VsGitWorkingTreeVerdictTests",
        "LibGit2VsGitWorktreeListTests",
    ];

    [Test]
    public void Mirror_holds_the_gitinprocess_tests()
    {
        typeof(InProcessGitReads).Assembly.GetName().Name.ShouldBe("Antiphon.GitInProcess");

        var root = RepositoryRoot();
        var project = Path.Combine(root, "tests", "Antiphon.GitInProcess.Tests", "Antiphon.GitInProcess.Tests.csproj");
        File.Exists(project).ShouldBeTrue(project);
        ProjectReferences(project).ShouldContain(include =>
            include.Contains("Antiphon.GitInProcess.csproj", StringComparison.Ordinal)
            && !include.Contains("Antiphon.GitInProcess.Tests", StringComparison.Ordinal));

        var production = Path.Combine(root, "src", "Antiphon.GitInProcess", "Antiphon.GitInProcess.csproj");
        File.ReadAllText(production).ShouldContain("<InternalsVisibleTo Include=\"Antiphon.GitInProcess.Tests\" />");

        var mirror = SourceFiles(Path.Combine(root, "tests", "Antiphon.GitInProcess.Tests"));
        var antiphon = SourceFiles(Path.Combine(root, "tests", "Antiphon.Tests"));
        foreach (var name in MovedClasses)
        {
            CountClass(mirror, name).ShouldBe(1, name);
            CountClass(antiphon, name).ShouldBe(0, name);
        }

        CountClass(mirror, "GitInProcessAssemblyTests").ShouldBe(1);
        CountClass(antiphon, "GitInProcessAssemblyTests").ShouldBe(0);
        foreach (var consumer in new[] { "ServerGitProcessTests", "LandingGitBatchingTests" })
        {
            CountClass(antiphon, consumer).ShouldBe(1, consumer);
            CountClass(mirror, consumer).ShouldBe(0, consumer);
        }

        const string ns = "namespace Antiphon.Tests.Infrastructure;";
        foreach (var name in MovedClasses)
        {
            var file = mirror.Single(path => CountClass([path], name) == 1);
            File.ReadAllText(file).ShouldContain(ns);
        }

        CountTestAttributes(mirror).ShouldBe(31 + 3);
    }

    [Test]
    public void No_GitInProcess_test_remains_in_Antiphon_Tests()
    {
        var root = RepositoryRoot();
        var tests = Path.Combine(root, "tests", "Antiphon.Tests");
        var importers = SourceFiles(tests)
            .Where(path => File.ReadLines(path).Any(line => line.Trim() == "using Antiphon.GitInProcess;"))
            .Select(path => Path.GetRelativePath(tests, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        // The two server-git consumers stay: their system under test is server git. The
        // isolation helper stays because Antiphon.Tests itself still reads through libgit2.
        importers.ShouldBe(
        [
            "Infrastructure/LandingGitBatchingTests.cs",
            "Infrastructure/ServerGitProcessTests.cs",
            "TestHelpers/LibGit2ConfigIsolation.cs",
        ]);
    }

    [Test]
    public void Assembly_hook_is_local_to_the_mirror()
    {
        var root = RepositoryRoot();
        var path = Path.Combine(root, "tests", "Antiphon.GitInProcess.Tests", "AssemblyHooks.cs");
        File.Exists(path).ShouldBeTrue(path);
        var text = File.ReadAllText(path);
        text.ShouldContain("[Before(Assembly)]");
        text.ShouldContain("ProductionRunnerGuard.PointEveryProgramBootAwayFromTheProductionRunner");
        text.ShouldContain("PtyBackendEnvGuard.ClearInheritedPtyBackend");
        text.ShouldContain("CheckpointTempRootSweep");
        text.ShouldContain("LibGit2ConfigIsolation.IsolateLibGit2Config");
        CountClass(SourceFiles(Path.Combine(root, "tests", "Antiphon.Tests")), "AssemblyHooks").ShouldBe(0);

        // The libgit2 isolation attribute stays in an Antiphon.Tests-only hook file; the shared
        // helper carries none, so the mirror's hook is the only one that fires there.
        var helper = File.ReadAllText(Path.Combine(root, "tests", "Antiphon.Tests", "TestHelpers", "LibGit2ConfigIsolation.cs"));
        helper.ShouldNotContain("[Before(Assembly)]");
        var antiphonHook = File.ReadAllText(Path.Combine(root, "tests", "Antiphon.Tests", "TestHelpers", "LibGit2ConfigIsolationAssemblyHook.cs"));
        antiphonHook.ShouldContain("[Before(Assembly)]");
        antiphonHook.ShouldContain("LibGit2ConfigIsolation.IsolateLibGit2Config");
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
