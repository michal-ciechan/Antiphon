using System.Xml.Linq;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

// A solution build and a ProjectReference are one MSBuild project only when their global
// properties match. LandGraphAudit is single-targeted, so the reference must not set
// TargetFramework: that global property is absent on the solution node, and the two nodes
// then compile the same obj/Debug/net10.0 together (CS2012, MSB4018).
[Category("Unit")]
public sealed class LandGraphAuditProjectInstanceTests
{
    private const string ToolProject = "tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj";

    [Test]
    public void Solution_and_test_reference_share_one_target_framework_global_property_set()
    {
        var tool = XDocument.Parse(DockerStackDocuments.Read(ToolProject));
        var frameworks = tool.Descendants("TargetFramework").Select(element => element.Value.Trim()).ToList();
        frameworks.ShouldBe(["net10.0"]);
        tool.Descendants("TargetFrameworks").ShouldBeEmpty();

        var solution = DockerStackDocuments.Read("Antiphon.sln");
        solution.Replace('\\', '/').ShouldContain(ToolProject);
        var entry = solution.IndexOf("\"Antiphon.LandGraphAudit\", \"tools\\Antiphon.LandGraphAudit\\Antiphon.LandGraphAudit.csproj\"", StringComparison.Ordinal);
        entry.ShouldBeGreaterThanOrEqualTo(0);
        var block = solution[entry..];
        var lineEnd = block.IndexOf('\n');
        lineEnd.ShouldBeGreaterThan(0);
        block[(lineEnd + 1)..].TrimStart().ShouldStartWith("EndProject");

        var reference = XDocument.Parse(DockerStackDocuments.Read("tests/Antiphon.Tests/Antiphon.Tests.csproj"))
            .Descendants("ProjectReference")
            .Single(element => IncludePath(element).EndsWith(ToolProject, StringComparison.Ordinal));

        // Skip keeps the net9.0 test project from selecting a framework. The tool's own
        // TargetFramework then applies, which is the same unset global property the solution uses.
        ((string?)reference.Attribute("SkipGetTargetFrameworkProperties")).ShouldBe("true");

        var referenceGlobals = GlobalAssignments(reference, "SetTargetFramework")
            .Concat(GlobalAssignments(reference, "AdditionalProperties"))
            .Concat(GlobalAssignments(reference, "Properties"))
            .ToList();
        referenceGlobals.Any(assignment => assignment.Name.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase))
            .ShouldBeFalse("SetTargetFramework on this single-targeted reference is a second project instance: "
                + string.Join("; ", referenceGlobals.Select(assignment => assignment.Name + "=" + assignment.Value)));
    }

    private static string IncludePath(XElement reference) =>
        ((string?)reference.Attribute("Include") ?? "").Replace('\\', '/');

    private static IEnumerable<(string Name, string Value)> GlobalAssignments(XElement reference, string attributeName)
    {
        var raw = (string?)reference.Attribute(attributeName);
        if (string.IsNullOrWhiteSpace(raw))
            yield break;
        foreach (var assignment in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var equals = assignment.IndexOf('=');
            equals.ShouldBeGreaterThan(0, attributeName + " assignment '" + assignment + "'");
            yield return (assignment[..equals].Trim(), assignment[(equals + 1)..].Trim());
        }
    }
}
