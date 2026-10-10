using System.Text.RegularExpressions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0479. Instruction and owner-doc contract for mechanical PC eligibility.
/// Prose is not server-enforced eligibility.
/// </summary>
[Category("Unit")]
public sealed class MechanicalPcContractTests
{
    private static string Compose(AgentTaskRole role) =>
        InstructionBundleComposer.Compose(InstructionBundles.ForDelegate(AgentTaskKind.Worker, role)).Text;

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, relative));

    [Test]
    public void C479_V05_MutationBundleFailsClosedAtLow()
    {
        var mutation = Compose(AgentTaskRole.Mutation);
        foreach (var phrase in new[]
                 {
                     "binding", "mechanical-execution", "higher-tier-analysis",
                     "mechanically-eligible", "higher-tier-required",
                     "binding-missing", "missing pack digest",
                     "Low never locates sites, authors diffs or discovers controls.",
                 })
            mutation.ShouldContain(phrase);
    }

    [Test]
    public void C479_V05b_CodePreparesUnboundCandidates()
    {
        var code = Compose(AgentTaskRole.Code);
        code.ShouldContain("docs/superpowers/mutations/");
        code.ShouldContain("candidate");
        code.ShouldContain("unbound");
        code.ShouldContain("pending for Mutation");
    }

    [Test]
    public void C479_V05c_ReviewJudgesAdequacyWithoutMutants()
    {
        var review = Compose(AgentTaskRole.Review);
        review.ShouldContain("adequacy");
        review.ShouldContain("PCs may remain pending");
    }

    [Test]
    public void C479_V06_OwnerDocHoldsTheContract()
    {
        var doc = Read("docs/mechanical-pc-contract.md");
        doc.ShouldContain("Contract version: 1");
        foreach (var heading in new[]
                 {
                     "## Identity and classification",
                     "## Exact mutation",
                     "## Resolved execution manifest",
                     "## Decisive oracle",
                     "## Reachability",
                     "## Restoration and evidence",
                 })
            doc.ShouldContain(heading);
        foreach (var token in new[] { "snapshotRoot", "evidenceRoot", "taskId", "runId", "pcId", "phase" })
            doc.ShouldContain(token);
        foreach (var reason in new[]
                 {
                     "binding-missing", "source-mismatch", "artifact-mismatch", "patch-unresolved",
                     "patch-mismatch", "selector-unresolved", "selector-drift", "oracle-unresolved",
                     "unexpected-red", "reachability-unproven", "surviving-mutant", "backend-race-unproven",
                     "prerequisite-missing", "baseline-red", "stale-output", "restore-mismatch",
                     "restored-red", "adequacy-gap",
                 })
            doc.ShouldContain(reason);

        var never = doc.Split('\n').Single(line => line.Contains("Never use", StringComparison.Ordinal));
        never.ShouldContain("git apply --3way");
        never.ShouldContain("git checkout .");
        never.ShouldContain("whole-worktree reset");

        foreach (var step in new[] { "Preflight", "Baseline", "Mutant", "Restore", "Restored run", "Settle" })
            doc.ShouldContain(step);
        foreach (var exit in new[] { "exit 0", "exit 1", "exit 2", "exit 3" })
            doc.ShouldContain(exit);

        var lines = doc.ReplaceLineEndings("\n").Split('\n');
        var labels = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "```powershell" && i + 1 < lines.Length)
                labels.Add(lines[i + 1]);
        }
        labels.Count.ShouldBe(5);
        foreach (var name in new[] { "preflight", "instantiate", "apply", "oracle", "restore" })
            labels.ShouldContain("# mechanical-pc-contract v1: " + name);
    }

    [Test]
    public void C479_V06b_WorkedExamplesAreIllustrative()
    {
        var doc = Read("docs/mechanical-pc-contract.md");
        var at = doc.IndexOf("## Worked examples", StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0);
        var section = doc[at..];
        foreach (var phrase in new[] { "illustrative", "reservation window", "equivalent", "zero-addition", "PC-83" })
            section.ShouldContain(phrase);
    }

    [Test]
    [Arguments("docs/orchestration-loop.md")]
    [Arguments(".claude/skills/antiphon-delegate/SKILL.md")]
    [Arguments("server/Bundles/orchestrator.md")]
    [Arguments("server/Bundles/README.md")]
    [Arguments("server/Bundles/delegate-basics.md")]
    public void C479_V07_ActiveRecipesRouteBindingHighAndExecutionLow(string relative)
    {
        var text = Read(relative);
        if (relative.EndsWith("delegate-basics.md", StringComparison.Ordinal))
        {
            var start = text.IndexOf("MUTATION RUNNER ONLY", StringComparison.Ordinal);
            var end = text.IndexOf("UNSOURCED MUTATION ONLY", start, StringComparison.Ordinal);
            start.ShouldBeGreaterThanOrEqualTo(0);
            end.ShouldBeGreaterThan(start);
            var paragraph = text[start..end];
            paragraph.ShouldContain("bound pack");
            paragraph.ShouldContain("method-scoped");
            return;
        }

        foreach (var phrase in new[] { "binding-only", "eligible", "mechanical-execution", "one open Mutation per O" })
            text.ShouldContain(phrase);

        if (relative.EndsWith("orchestration-loop.md", StringComparison.Ordinal)
            || relative.EndsWith("SKILL.md", StringComparison.Ordinal))
        {
            var row = text.Split('\n').Single(line => line.Contains("| `Mutation` |", StringComparison.Ordinal));
            row.ShouldContain("| Low |");
            Regex.IsMatch(row, @"\|\s*`Mutation`[^\n]*\|\s*Frontier\s*\|").ShouldBeFalse();
        }

        if (relative.EndsWith("orchestration-loop.md", StringComparison.Ordinal))
        {
            text.ShouldContain("docs/mechanical-pc-contract.md");
            Read("docs/testing-and-build.md").ShouldContain("docs/mechanical-pc-contract.md");
        }
    }

    [Test]
    public void C479_R06_LowNeverDiscoversAndTierRowsStayLow()
    {
        var mutation = Compose(AgentTaskRole.Mutation);
        mutation.IndexOf("Discover", StringComparison.Ordinal)
            .ShouldBeGreaterThan(mutation.IndexOf("higher-tier-analysis", StringComparison.Ordinal));
        Compose(AgentTaskRole.TestDesign).ShouldContain("binding required after Code");
        foreach (var relative in new[]
                 {
                     "docs/orchestration-loop.md",
                     ".claude/skills/antiphon-delegate/SKILL.md",
                 })
        {
            var tiers = Read(relative);
            Regex.IsMatch(tiers, @"\|\s*`Mutation`[^\n]*\|\s*Frontier\s*\|").ShouldBeFalse();
            tiers.Split('\n').Single(line => line.Contains("| `Mutation` |", StringComparison.Ordinal))
                .ShouldContain("Low");
        }
    }
}
