using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>Executable pins for the CARD-0811 post-land retrospective instructions.</summary>
[Category("Unit")]
public sealed class PostLandRetrospectiveContractTests
{
    [Test]
    public void C811_V1_LoopStatesTriggerGateAndDurableRecord()
    {
        var loop = RetrospectiveSection();
        foreach (var phrase in new[]
        {
            "### Post-land miss retrospective (CARD-0811)",
            "stage-outcomes?cardId=", "supersedesId: null", "landing.remoteConfirmedAt",
            "git merge-base --is-ancestor", "post-land-retrospective:<review-outcome-guid>",
            "label `post-land-retrospective`", "review_evidence_superseded",
            "landing.cleanup", "override pending", "is not a retrospective"
        })
            loop.ShouldContain(phrase);
    }

    [Test]
    public void C811_V2_LoopStatesRolesClassificationAndDisposition()
    {
        var loop = RetrospectiveSection();
        foreach (var phrase in new[]
        {
            "-Role Investigate -Worktree -Card <companion-guid>",
            "-Role Docs -Kind ClaudeCode -Level Low", "-post-land-retrospective.md",
            "not-designed", "designed-not-asserted", "asserted-wrong-layer", "bundle-gap",
            "reviewer-deviation", "environment-only", "no-instruction-change", "doc-change",
            "bundle-change", "escalate", "Instruction gap:", "label `instruction-gap`",
            "NeedsDecision", "Never apply a Low-tier proposal to a bundle directly"
        })
            loop.ShouldContain(phrase);
    }

    [Test]
    public void C811_V3_OrchestratorBundleCarriesTheSibling()
    {
        var composed = InstructionBundleComposer.Compose(
            InstructionBundles.ForDelegate(AgentTaskKind.Orchestrator, AgentTaskRole.Custom)).Text;
        foreach (var phrase in new[]
        {
            "Post-land retrospective: <identifier>", "label `post-land-retrospective`",
            "is not a retrospective", "File a Backlog card the moment"
        })
            composed.ShouldContain(phrase);

        var source = InstructionBundles.All["orchestrator"].Text;
        const string before = "File a Backlog card the moment";
        const string after = "A 409 `concurrency_limit`";
        var start = source.IndexOf(before, StringComparison.Ordinal);
        var end = source.IndexOf(after, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        end.ShouldBeGreaterThan(start);
        foreach (var character in source[start..end])
            ((int)character).ShouldBeLessThan(128);
    }

    [Test]
    public void C811_V4_SkillPointsAtTheOwner()
    {
        var skill = File.ReadAllText(RepoFile(".claude/skills/antiphon-orchestrator/SKILL.md"));
        skill.ShouldContain("post-land retrospective", Case.Insensitive);
        skill.ShouldContain("CARD-0811");
        skill.ShouldContain("docs/orchestration-loop.md");
    }

    private static string RetrospectiveSection()
    {
        var loop = File.ReadAllText(RepoFile("docs/orchestration-loop.md"));
        const string heading = "### Post-land miss retrospective (CARD-0811)";
        var start = loop.IndexOf(heading, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var end = loop.IndexOf("\n### ", start + heading.Length, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start);
        return loop[start..end];
    }

    private static string RepoFile(string relative)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        root.ShouldNotBeNull();
        return Path.Combine(root.FullName, relative);
    }
}
