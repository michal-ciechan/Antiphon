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

    [Test]
    public void operational_autonomy_owner_pins_authority_checks_and_human_only_steps()
    {
        const string policySentence = "An orchestrator MAY restart or upgrade the AppHost, restart session runners, and run docker-based server2 rollouts without asking a human.";
        var document = ReadRepoFile("docs", "orchestration-loop.md");
        var start = document.IndexOf("## Orchestrator operational autonomy (restart, rollout)", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var end = document.IndexOf("\n---", start, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start);
        var loop = Collapse(document[start..end]);
        loop.ShouldContain(policySentence);
        foreach (var phrase in new[]
        {
            "git pull --rebase", "queued lands have finished", "no worktree is half-reset",
            "GET /api/version", "/health", "child journal", "-AllowWorktree",
            "never pull while a land runs", "user's untracked files",
            "cold first Seed", "deploy-server2.ps1 -Rolling", "deploy-temp", "drain-old", "redeploy-old",
            "Still requires a human", "Reset", "Prune", "retire-temp",
            "donor tars", "other sessions or alwaysOn agents", "budgets, routing pins, or settings",
            "spend beyond a sanctioned canary", "secrets", "standing server2 runner container outside",
        })
            loop.ShouldContain(phrase, Case.Insensitive);

        // Remove the authorization sentence in a scratch copy: the same pin must go red.
        var scratch = loop.Replace(policySentence, "", StringComparison.Ordinal);
        Should.Throw<ShouldAssertException>(() => scratch.ShouldContain(policySentence));
        loop.ShouldContain(policySentence); // The original copy was never changed.
    }

    [Test]
    public void operational_autonomy_pointers_reach_the_owner()
    {
        const string anchor = "orchestration-loop.md#orchestrator-operational-autonomy-restart-rollout";
        foreach (var path in new[]
        {
            "AGENTS.md", "server/Bundles/orchestrator.md",
            ".claude/skills/antiphon-orchestrator/SKILL.md",
            "docs/apphost-runbook.md", "docs/bootstrap.md",
        })
            ReadRepoFile(path).ShouldContain(anchor, path);
    }
}
