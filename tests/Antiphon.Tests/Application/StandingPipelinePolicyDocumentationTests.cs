using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class StandingPipelinePolicyDocumentationTests
{
    private static readonly string[] Copies =
    [
        "AGENTS.md",
        Path.Combine("docs", "orchestration-loop.md"),
        Path.Combine("server", "Bundles", "orchestrator.md"),
        Path.Combine(".claude", "skills", "antiphon-orchestrator", "SKILL.md"),
    ];

    private static readonly string[] Phrases =
    [
        "never two tasks in the same stage",
        "depth of two",
        "-IgnoreConcurrencyLimit",
        "axis",
    ];

    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ");

    private static string ReadRepoFile(params string[] relative) =>
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, Path.Combine(relative)));

    [Test]
    public void the_policy_phrases_are_pinned_in_every_copy()
    {
        foreach (var relative in Copies)
        {
            var text = Collapse(File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, relative)));
            foreach (var phrase in Phrases)
                text.ShouldContain(phrase, Case.Insensitive, relative);
        }
    }

    [Test]
    public void the_doc_owns_the_section_and_the_old_wip_reading_is_gone()
    {
        var loop = ReadRepoFile("docs", "orchestration-loop.md");
        var collapsed = Collapse(loop);
        collapsed.ShouldContain("### Standing pipeline policy", Case.Insensitive);
        collapsed.ShouldContain("CARD-0533", Case.Insensitive);
        loop.ShouldNotContain("TestDesign together) is 1");
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
