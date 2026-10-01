using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class StandingPipelinePolicyDocumentationTests
{
    private static readonly string[] Phrases =
    [
        "never more tasks in one stage than its cap",
        "depth cap",
        "up to four",
        "at most six",
        "effective concurrency limits",
        "server2",
        "-Runner server2",
        "absolutely requires",
        "use the lower effective stage cap",
        "CARD-0881",
        "GET /api/hosts",
        "-IgnoreConcurrencyLimit",
        "axis",
    ];

    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ");

    private static string ReadRepoFile(params string[] relative) =>
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, Path.Combine(relative)));

    private static string PolicyCopy(string relative)
    {
        var text = ReadRepoFile(relative);
        if (relative == "AGENTS.md")
            return text.Split('\n').Single(line => line.StartsWith("- An orchestrator working a board"));

        var (start, end) = relative switch
        {
            "docs/orchestration-loop.md" => ("### Standing pipeline policy", "### Post-land miss retrospective"),
            "server/Bundles/orchestrator.md" => ("When you are working a board through its pipeline", "Model-tier names are"),
            ".claude/skills/antiphon-orchestrator/SKILL.md" => ("The owner is `docs/orchestration-loop.md` §1", "## 1. Dispatch the next stage"),
            _ => throw new ArgumentOutOfRangeException(nameof(relative)),
        };
        var from = text.IndexOf(start, StringComparison.Ordinal);
        var to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        return text[from..to];
    }

    [Test]
    [Arguments("AGENTS.md")]
    [Arguments("docs/orchestration-loop.md")]
    [Arguments("server/Bundles/orchestrator.md")]
    [Arguments(".claude/skills/antiphon-orchestrator/SKILL.md")]
    public void the_policy_phrases_are_pinned_in_every_copy(string relative)
    {
        var text = Collapse(PolicyCopy(relative));
        foreach (var phrase in Phrases)
            text.ShouldContain(phrase, Case.Insensitive, relative);
    }

    [Test]
    public void the_doc_owns_the_section_and_the_old_wip_reading_is_gone()
    {
        var loop = ReadRepoFile("docs", "orchestration-loop.md");
        var collapsed = Collapse(loop);
        collapsed.ShouldContain("### Standing pipeline policy", Case.Insensitive);
        collapsed.ShouldContain("CARD-0533", Case.Insensitive);
        loop.ShouldNotContain("TestDesign together) is 1");
        loop.ShouldContain("PUT /api/hosts/server2/budget");
        loop.ShouldContain("\"maxInFlight\": <n>, \"reason\": \"<why>\"");
        loop.ShouldNotContain("GET/PUT /api/hosts/server2/budget");
    }

    [Test]
    public void the_bundle_no_longer_states_the_old_override_rule()
    {
        var bundle = ReadRepoFile("server", "Bundles", "orchestrator.md");
        bundle.ShouldNotContain("only when the user asked for parallel work this turn");
        Collapse(bundle).ShouldContain("same source area", Case.Insensitive);
    }

    [Test]
    public void the_agents_index_points_at_the_owner()
    {
        Collapse(ReadRepoFile("AGENTS.md")).ShouldContain("§1 (CARD-0533)", Case.Insensitive);
    }
}
