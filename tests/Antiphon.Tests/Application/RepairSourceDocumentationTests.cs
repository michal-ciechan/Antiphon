using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class RepairSourceDocumentationTests
{
    [Test]
    public void C499_V31_DocsAndSkillNameTheRepairContract()
    {
        var root = FindRepoRoot();
        File.ReadAllText(Path.Combine(root, "docs", "orchestration-loop.md"))
            .ShouldContain("-RepairSource");
        File.ReadAllText(Path.Combine(root, "docs", "orchestration-loop.md"))
            .ShouldContain("repair_source_landing_owner_required");
        File.ReadAllText(Path.Combine(root, "docs", "ops-http.md"))
            .ShouldContain("repairSourceTaskId");
        File.ReadAllText(Path.Combine(root, "docs", "ops-http.md"))
            .ShouldContain("progressEvidence");
        File.ReadAllText(Path.Combine(root, "docs", "antiphon-api.md"))
            .ShouldContain("repairSourceTaskId");
        File.ReadAllText(Path.Combine(root, "docs", "antiphon-api.md"))
            .ShouldContain("progressEvidence");
        File.ReadAllText(Path.Combine(root, ".claude", "skills", "antiphon-delegate", "SKILL.md"))
            .ShouldContain("-RepairSource");
        var code = File.ReadAllText(Path.Combine(root, "server", "Bundles", "stage-code.md"));
        var formatter = File.ReadAllText(Path.Combine(root, "server", "Application", "Services", "DelegationReportFormatter.cs"));
        (code.Contains("[antiphon-progress:") || formatter.Contains("[antiphon-progress:")).ShouldBeTrue();
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Antiphon.sln")) || File.Exists(Path.Combine(dir, "docs", "orchestration-loop.md")))
                return dir;
            dir = Path.GetDirectoryName(dir)!;
        }
        return Directory.GetCurrentDirectory();
    }
}
