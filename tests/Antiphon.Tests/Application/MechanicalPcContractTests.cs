using System.Text.RegularExpressions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0479. Instruction and owner-doc contract for mechanical PC eligibility.
/// Prose is not server-enforced eligibility. CARD-1355 anchors V-7 to the routing sentence
/// itself, paragraph-scoped, instead of free words that other lines of the same file also use.
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

    /// <summary>
    /// The CARD-0479 routing sentence each active recipe carries verbatim: binding at explicit
    /// Frontier, eligible mechanical execution at Low, one open Mutation per O.
    /// </summary>
    private const string RoutingRule =
        "Commission binding-only at explicit Frontier, then eligible mechanical-execution at Low; one open Mutation per O.";

    /// <summary>
    /// The orchestrator bundle's shortened form of that rule since CARD-1332 (6ec71d427), which
    /// dropped the tier words and kept the scope names and the one-open constraint. CARD-1355 pins
    /// this exact fragment beside its owner pointer; the bundle's other use of "eligible" (runner
    /// placement) is not a routing anchor and is not consulted.
    /// </summary>
    private const string BundleRoutingRule = "binding-only mechanical-execution; one open Mutation per O.";

    private const string BundleOwnerPointer = "Follow the full CARD-0478 recipe in docs/orchestration-loop.md.";

    [Test]
    [Arguments("docs/orchestration-loop.md")]
    [Arguments(".claude/skills/antiphon-delegate/SKILL.md")]
    [Arguments("server/Bundles/orchestrator.md")]
    [Arguments("server/Bundles/README.md")]
    [Arguments("server/Bundles/delegate-basics.md")]
    public void C479_V07_ActiveRecipesRouteBindingHighAndExecutionLow(string relative)
    {
        var text = Read(relative);
        switch (relative)
        {
            case "server/Bundles/delegate-basics.md":
            {
                var paragraph = Slice(text, "MUTATION RUNNER ONLY", "UNSOURCED MUTATION ONLY", "c1355-basics");
                paragraph.ShouldContain("bound pack", Case.Sensitive, "c1355-basics-bound-pack");
                paragraph.ShouldContain("method-scoped", Case.Sensitive, "c1355-basics-method-scoped");
                return;
            }
            case "docs/orchestration-loop.md":
            {
                // The Tiers section's role paragraph: Code, post-land Mutation, the contract link, then the rule.
                var paragraph = Paragraph(text, "Code owns implementation and ordinary V/R;", "c1355-owner");
                paragraph.ShouldContain("See docs/mechanical-pc-contract.md.", Case.Sensitive, "c1355-owner-contract-link");
                paragraph.ShouldContain(RoutingRule, Case.Sensitive, "c1355-owner-routing-rule");
                AssertMutationTierRowIsLow(text, "c1355-owner");
                Read("docs/testing-and-build.md")
                    .ShouldContain("docs/mechanical-pc-contract.md", Case.Sensitive, "c1355-testing-contract-link");
                return;
            }
            case ".claude/skills/antiphon-delegate/SKILL.md":
            {
                // The sentence stands alone directly under the role table.
                Count(text, RoutingRule).ShouldBe(1, "c1355-skill-routing-rule");
                AssertMutationTierRowIsLow(text, "c1355-skill");
                return;
            }
            case "server/Bundles/README.md":
            {
                var paragraph = Paragraph(text, "Default workflow: Code -> ordinary Review", "c1355-readme");
                paragraph.ShouldContain(RoutingRule, Case.Sensitive, "c1355-readme-routing-rule");
                return;
            }
            case "server/Bundles/orchestrator.md":
            {
                var paragraph = Paragraph(text, "Default workflow: Code -> ordinary Review", "c1355-bundle");
                paragraph.ShouldContain(BundleOwnerPointer, Case.Sensitive, "c1355-bundle-owner-pointer");
                paragraph.ShouldContain(BundleRoutingRule, Case.Sensitive, "c1355-bundle-routing-rule");
                // The pointer resolves to the complete rule in the owner it names.
                Read("docs/orchestration-loop.md")
                    .ShouldContain(RoutingRule, Case.Sensitive, "c1355-bundle-owner-routing-rule");
                return;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(relative), relative, "unknown active recipe");
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

    // ---- helpers (CARD-1355) ------------------------------------------------------------------

    /// <summary>The one Tiers-table Mutation row names Low and does not end at Frontier.</summary>
    private static void AssertMutationTierRowIsLow(string text, string label)
    {
        var row = text.ReplaceLineEndings("\n").Split('\n')
            .Single(line => line.Contains("| `Mutation` |", StringComparison.Ordinal));
        row.ShouldContain("| Low |", Case.Sensitive, label + "-mutation-row-low");
        Regex.IsMatch(row, @"\|\s*`Mutation`[^\n]*\|\s*Frontier\s*\|").ShouldBeFalse(label + "-mutation-row-not-frontier");
    }

    /// <summary>
    /// The whitespace-collapsed paragraph that opens with the unique <paramref name="marker"/>, up to
    /// the next blank line, so a sentence is pinned where the rule lives rather than anywhere in the file.
    /// </summary>
    private static string Paragraph(string text, string marker, string label)
    {
        var normalised = text.ReplaceLineEndings("\n");
        Count(normalised, marker).ShouldBe(1, label + "-marker");
        var start = normalised.IndexOf(marker, StringComparison.Ordinal);
        var end = normalised.IndexOf("\n\n", start, StringComparison.Ordinal);
        return Regex.Replace(normalised[start..(end < 0 ? normalised.Length : end)], @"\s+", " ").Trim();
    }

    /// <summary>Text between two markers that must both exist, in order.</summary>
    private static string Slice(string text, string startMarker, string endMarker, string label)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, label + "-start");
        var end = text.IndexOf(endMarker, start, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start, label + "-end");
        return text[start..end];
    }

    private static int Count(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
