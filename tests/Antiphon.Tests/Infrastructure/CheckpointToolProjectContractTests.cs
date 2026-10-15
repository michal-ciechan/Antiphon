using System.Xml.Linq;
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
    public void csproj_references_only_host_cleanup()
    {
        // CARD-0987 custody refresh D-3 requires the one-way tool -> HostCleanup reference.
        // Keep the exact set closed: missing, duplicate and unrelated references must fail.
        var references = XDocument.Parse(
                DockerStackDocuments.Read("tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj"))
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .ToArray();

        references.ShouldBe(["../../src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj"]);
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
