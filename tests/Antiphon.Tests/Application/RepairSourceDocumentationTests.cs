using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class RepairSourceDocumentationTests
{
    [Test]
    public void C603_RepairSourceFailedOwnerRecipeNamesOwnerBoundReview()
    {
        var root = FindRepoRoot();
        var orchestration = File.ReadAllText(Path.Combine(root, "docs", "orchestration-loop.md"));
        var anchor = "<a id=\"repairsource-failed-owner-recovery\"></a>";
        var title = "**RepairSource succeeded; owner Failed.**";
        var anchorAt = orchestration.IndexOf(anchor, StringComparison.Ordinal);
        anchorAt.ShouldBeGreaterThanOrEqualTo(0);
        var titleAt = orchestration.IndexOf(title, anchorAt, StringComparison.Ordinal);
        titleAt.ShouldBeGreaterThan(anchorAt);
        var nextTopic = orchestration.IndexOf("\n**", titleAt + title.Length, StringComparison.Ordinal);
        nextTopic.ShouldBeGreaterThan(titleAt);
        var recipe = string.Join(" ", orchestration[titleAt..nextTopic].Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));
        AssertRecipe(recipe);

        var ops = File.ReadAllText(Path.Combine(root, "docs", "ops-http.md"));
        ops.ShouldContain("orchestration-loop.md#repairsource-failed-owner-recovery");
        ops.ShouldContain("owner-bound Clean");

        var bundle = File.ReadAllText(Path.Combine(root, "server", "Bundles", "orchestrator.md"));
        var bundleAt = bundle.IndexOf(title, StringComparison.Ordinal);
        bundleAt.ShouldBeGreaterThanOrEqualTo(0);
        var bundleEnd = bundle.IndexOf("\nWhen you are working a board", bundleAt, StringComparison.Ordinal);
        bundleEnd.ShouldBeGreaterThan(bundleAt);
        var bundledRecipe = string.Join(" ", bundle[bundleAt..bundleEnd].Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));
        AssertRecipe(bundledRecipe);
    }

    private static void AssertRecipe(string recipe)
    {
        recipe.ShouldContain("owner Failed");
        recipe.ShouldContain("subjectTaskId");
        recipe.ShouldContain("original Code/Worktree owner");
        recipe.ShouldContain("Clean Final/Full Review");
        recipe.ShouldContain("-Land <owner-guid> -ExpectedSourceSha <full-sha> -ReviewEvidenceId <review-evidence-id> -RecoverReviewedSource");
        recipe.ShouldContain("Failed status");
        recipe.ShouldContain("-StartRef");
        recipe.ShouldContain("-FromTask");
        recipe.ShouldContain("-RepairSource");
    }

    [Test]
    public void C675_DocsNameTheStartRefRepairLandingRoute()
    {
        var root = FindRepoRoot();
        var orchestration = File.ReadAllText(Path.Combine(root, "docs", "orchestration-loop.md"));
        orchestration.ShouldContain("-FromTask");
        orchestration.ShouldContain("adopt_source_lineage");
        orchestration.ShouldContain("repair_source_owner_diverged");
        File.ReadAllText(Path.Combine(root, "docs", "ops-http.md"))
            .ShouldContain("repair_source_owner_remote_ahead");
        File.ReadAllText(Path.Combine(root, "server", "Bundles", "orchestrator.md"))
            .ShouldContain("-FromTask");
        File.ReadAllText(Path.Combine(root, ".claude", "skills", "antiphon-delegate", "SKILL.md"))
            .ShouldContain("-FromTask");
        File.ReadAllText(Path.Combine(root, "docs", "antiphon-api.md"))
            .ShouldContain("adoptFromTaskId");
    }
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
