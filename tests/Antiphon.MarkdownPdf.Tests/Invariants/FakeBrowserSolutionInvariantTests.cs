using System.Xml.Linq;
using Shouldly;
using TUnit.Core;

namespace Antiphon.MarkdownPdf.Tests.Invariants;

[Category("RepositoryInvariant")]
[Category("Unit")]
public sealed class FakeBrowserSolutionInvariantTests
{
    [Test]
    public void The_release_solution_builds_the_fake_browser_dependency()
    {
        var root = FakeBrowserSolutionReleaseTests.FindRepository();
        var lines = File.ReadAllLines(Path.Combine(root, "Antiphon.sln"));
        const string producerName = "Antiphon.MarkdownPdf.FakeBrowser";
        const string producerProject = @"tests\Antiphon.MarkdownPdf.FakeBrowser\Antiphon.MarkdownPdf.FakeBrowser.csproj";
        var project = lines.Single(line => line.StartsWith("Project(", StringComparison.Ordinal)
            && line.Contains("\"" + producerName + "\", \"" + producerProject + "\"", StringComparison.Ordinal));
        var projectId = project.Split(',').Last().Trim().Trim('"');
        lines.Select(line => line.Trim()).ShouldContain(projectId + ".Release|Any CPU.Build.0 = Release|Any CPU");

        var consumer = XDocument.Load(Path.Combine(root, "tests/Antiphon.MarkdownPdf.Tests/Antiphon.MarkdownPdf.Tests.csproj"));
        var target = consumer.Descendants("Target").Single(element => (string?)element.Attribute("Name") == "CopyFakeBrowser");
        var build = target.Elements("MSBuild").Single();
        ((string?)build.Attribute("Projects")).ShouldBe("../Antiphon.MarkdownPdf.FakeBrowser/Antiphon.MarkdownPdf.FakeBrowser.csproj");
        ((string?)build.Attribute("Targets")).ShouldBe("GetTargetPath");
        ((string?)build.Element("Output")?.Attribute("PropertyName")).ShouldBe("FakeBrowserTargetPath");
        ((string?)target.Elements("Copy").Single().Attribute("DestinationFolder")).ShouldBe("$(TargetDir)fake-browser/");
    }
}
