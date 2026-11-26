using Antiphon.Tests.Infrastructure;
using System.Xml.Linq;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Invariants;

[Category("RepositoryInvariant")]
[Category("Unit")]
public sealed class CheckpointToolProjectContractTests
{
    private const string ToolProject = "tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj";
    private const string HostCleanupProject = "src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj";
    private const string GitInProcessProject = "src/Antiphon.GitInProcess/Antiphon.GitInProcess.csproj";

    [Test]
    public void csproj_packs_as_tool_named_antiphon_checkpoints()
    {
        var text = DockerStackDocuments.Read(ToolProject);
        text.ShouldContain("<PackAsTool>true</PackAsTool>");
        text.ShouldContain("<ToolCommandName>antiphon-checkpoints</ToolCommandName>");
        text.ShouldContain("<PackageId>Antiphon.Checkpoints</PackageId>");
        text.ShouldContain("<IsPackable>true</IsPackable>");
    }

    [Test]
    public void csproj_references_only_isolated_libraries()
    {
        // The tool uses HostCleanup for custody and GitInProcess for source identity.
        References(ToolProject, "ProjectReference").ShouldBe([
            "../../src/Antiphon.GitInProcess/Antiphon.GitInProcess.csproj",
            "../../src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj",
        ]);
    }

    [Test]
    public void host_cleanup_references_only_git_in_process()
    {
        References(HostCleanupProject, "ProjectReference")
            .ShouldBe(["../Antiphon.GitInProcess/Antiphon.GitInProcess.csproj"]);
    }

    [Test]
    public void git_in_process_has_no_project_references()
    {
        References(GitInProcessProject, "ProjectReference").ShouldBeEmpty();
    }

    [Test]
    public void git_in_process_references_only_libgit2sharp()
    {
        References(GitInProcessProject, "PackageReference").ShouldBe(["LibGit2Sharp"]);
    }

    [Test]
    public void solution_lists_the_tool()
    {
        DockerStackDocuments.Read("Antiphon.sln").ShouldContain("tools\\Antiphon.Checkpoints\\Antiphon.Checkpoints.csproj");
    }

    [Test]
    public void tests_project_references_the_tool()
    {
        DockerStackDocuments.Read("tests/Antiphon.Tests/Antiphon.Tests.csproj")
            .ShouldContain("tools\\Antiphon.Checkpoints\\Antiphon.Checkpoints.csproj");
    }

    private static string?[] References(string project, string itemName) =>
        XDocument.Parse(DockerStackDocuments.Read(project))
            .Descendants()
            .Where(element => element.Name.LocalName == itemName)
            .Select(element => (string?)element.Attribute("Include"))
            .Order(StringComparer.Ordinal)
            .ToArray();
}
