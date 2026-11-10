using System.Xml.Linq;
using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1546 (V-1, V-2, V-3). <c>ServerGitProcess</c> is declared by
/// <c>Antiphon.Server.Git</c>, and neither that project nor this test project
/// references <c>Antiphon.Server</c>.
/// </summary>
[Category("Unit")]
public sealed class ServerGitAssemblyTests
{
    [Test]
    public void Declaring_assembly_is_Antiphon_Server_Git()
    {
        typeof(ServerGitProcess).Assembly.GetName().Name.ShouldBe("Antiphon.Server.Git");
    }

    [Test]
    public void Project_does_not_reference_the_server()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "Antiphon.Server.Git", "Antiphon.Server.Git.csproj");
        File.Exists(path).ShouldBeTrue(path);
        var includes = XDocument.Load(path)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => (string?)element.Attribute("Include") ?? "")
            .Where(include =>
                include.Contains("server/", StringComparison.Ordinal)
                || include.Contains("Antiphon.Server.csproj", StringComparison.Ordinal))
            .ToArray();
        includes.ShouldBeEmpty();
    }

    /// <summary>
    /// CARD-1546 S3 (V-3). The mirrored project must exist, must not reference the
    /// server, and must hold the methods that left <c>Antiphon.Tests</c> plus this witness.
    /// </summary>
    [Test]
    public void Test_project_does_not_reference_the_server()
    {
        var root = RepositoryRoot();
        var path = Path.Combine(root, "tests", "Antiphon.Server.Git.Tests", "Antiphon.Server.Git.Tests.csproj");
        File.Exists(path).ShouldBeTrue(path);
        var includes = XDocument.Load(path)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => (string?)element.Attribute("Include") ?? "")
            .Where(include =>
                include.Contains("server/", StringComparison.Ordinal)
                || include.Contains("Antiphon.Server.csproj", StringComparison.Ordinal))
            .ToArray();
        includes.ShouldBeEmpty();

        var mirror = SourceFiles(Path.Combine(root, "tests", "Antiphon.Server.Git.Tests"));
        var antiphon = SourceFiles(Path.Combine(root, "tests", "Antiphon.Tests"));
        foreach (var name in MovedMethodNames)
        {
            CountDeclarations(mirror, name).ShouldBe(1, name);
            CountDeclarations(antiphon, name).ShouldBe(0, name);
        }

        const string witness = "Test_project_does_not_reference_the_server";
        CountDeclarations(mirror, witness).ShouldBe(1, witness);
        CountDeclarations(antiphon, witness).ShouldBe(0, witness);
        CountTestAttributes(mirror).ShouldBe(MovedMethodNames.Length + 1);
        foreach (var name in StayedMethodNames)
        {
            CountDeclarations(antiphon, name).ShouldBe(1, name);
            CountDeclarations(mirror, name).ShouldBe(0, name);
        }

        CountClass(antiphon, "ServerGitAssemblyTests").ShouldBe(0);
        CountClass(antiphon, "GitIndexLockTests").ShouldBe(0);
        CountClass(mirror, "ServerGitAssemblyTests").ShouldBe(1);
        CountClass(mirror, "GitIndexLockTests").ShouldBe(1);
    }

    // Methods that existed on these classes before V-3. The witness itself is the only addition.
    private static readonly string[] MovedMethodNames =
    [
        "Declaring_assembly_is_Antiphon_Server_Git",
        "Project_does_not_reference_the_server",
        "Classify_matrix",
        "C1436_census_drops_git_that_exits_mid_census",
        "C1436_census_keeps_live_holders",
        "Evaluate_path_error_is_held",
        "TryReclaimAfterKill_is_gone",
    ];

    private static readonly string[] StayedMethodNames =
    [
        "Path_resolves_main_and_linked_worktree",
        "Missing_checkout_returns_path_error",
    ];

    private static string[] SourceFiles(string directory)
    {
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

    private static int CountDeclarations(IEnumerable<string> files, string name)
    {
        var voidNeedle = "void " + name + "(";
        var taskNeedle = "Task " + name + "(";
        var count = 0;
        foreach (var file in files)
        {
            foreach (var line in File.ReadLines(file))
            {
                if (line.Contains(voidNeedle, StringComparison.Ordinal) || line.Contains(taskNeedle, StringComparison.Ordinal))
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
