using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class CheckpointToolProjectContractTests
{
    [Test]
    public void csproj_packs_as_tool_named_antiphon_checkpoints()
    {
        var text = DockerStackDocuments.Read("tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj");
        text.ShouldContain("<PackAsTool>true</PackAsTool>");
        text.ShouldContain("<ToolCommandName>antiphon-checkpoints</ToolCommandName>");
        text.ShouldContain("<PackageId>Antiphon.Checkpoints</PackageId>");
        text.ShouldContain("<IsPackable>true</IsPackable>");
    }

    [Test]
    public void csproj_has_no_project_references()
    {
        DockerStackDocuments.Read("tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj")
            .ShouldNotContain("<ProjectReference");
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
}
