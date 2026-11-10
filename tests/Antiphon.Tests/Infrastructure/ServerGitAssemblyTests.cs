using System.Xml.Linq;
using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1546 S1 (V-1, V-2). Red while <c>ServerGitProcess</c> still lives in
/// <c>Antiphon.Server</c> and <c>src/Antiphon.Server.Git</c> does not exist.
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
    /// CARD-1546 S3 (V-3). Red while <c>tests/Antiphon.Server.Git.Tests</c> does not exist.
    /// </summary>
    [Test]
    public void Test_project_does_not_reference_the_server()
    {
        var path = Path.Combine(RepositoryRoot(), "tests", "Antiphon.Server.Git.Tests", "Antiphon.Server.Git.Tests.csproj");
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

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
