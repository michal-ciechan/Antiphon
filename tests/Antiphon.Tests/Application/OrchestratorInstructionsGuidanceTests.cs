using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0822. The generated-file sentence is appended to each policy copy, and the bundle swap
/// drops the three unpinned paragraphs. Phrases are listed here rather than read from
/// <see cref="StandingPipelinePolicyDocumentationTests"/>.
/// </summary>
[Category("Unit")]
public sealed class OrchestratorInstructionsGuidanceTests
{
    private const string LiveSettingsSentence =
        "ANTIPHON_ORCHESTRATOR_INSTRUCTIONS";

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

    private const string WorkspaceRule =
        "Live operating settings: read the file at $env:ANTIPHON_ORCHESTRATOR_INSTRUCTIONS (or GET /api/orchestrator-instructions) at session start, after compaction and when a settings-changed note arrives; it outranks the numbers in AGENTS.md.";

    [Test]
    public void The_bundle_names_the_file_route_compaction_and_precedence_and_dropped_the_swapped_sentences()
    {
        var bundle = Read("server", "Bundles", "orchestrator.md");
        bundle.ShouldContain("ANTIPHON_ORCHESTRATOR_INSTRUCTIONS");
        bundle.ShouldContain("GET /api/orchestrator-instructions");
        bundle.ShouldContain("after compaction");
        bundle.ShouldContain("outranks");
        bundle.ShouldContain("Never edit it.");
        bundle.ShouldNotContain("The reasons are in docs/orchestration-loop.md");
        bundle.ShouldNotContain("Delegates run directly in the working directory");
        bundle.ShouldNotContain("rather than trying to run its steps yourself");
    }

    [Test]
    public void The_policy_copies_keep_every_pinned_phrase_and_each_names_the_file()
    {
        foreach (var relative in new[]
        {
            "AGENTS.md",
            "docs/orchestration-loop.md",
            "server/Bundles/orchestrator.md",
            ".claude/skills/antiphon-orchestrator/SKILL.md",
        })
        {
            var text = Collapse(PolicyCopy(relative));
            text.ShouldContain(LiveSettingsSentence, Case.Insensitive, relative);
            foreach (var phrase in Phrases)
                text.ShouldContain(phrase, Case.Insensitive, relative);
        }
    }

    [Test]
    public void The_workspace_script_writes_the_rule_and_the_new_matcher()
    {
        var script = Read("scripts", "orchestrator-workspace.ps1");
        var claude = Slice(script, "function Write-ClaudeContext", "function Write-AgentsContext");
        var agents = Slice(script, "function Write-AgentsContext", "function Write-ClaudeSettings");
        claude.ShouldContain(WorkspaceRule);
        agents.ShouldContain(WorkspaceRule);
        script.ShouldContain("startup|resume|compact");
    }

    [Test]
    public void The_contract_doc_owns_the_generated_file()
    {
        var doc = Read("docs", "agent-instruction-file-contract.md");
        doc.ShouldContain("## Generated orchestrator instructions (CARD-0822)");
        doc.ShouldContain("path rule");
        doc.ShouldContain("stamp line");
        doc.ShouldContain("InstructionFiles");
        doc.ShouldContain("precedence rule");
    }

    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ");

    private static string Read(params string[] relative) =>
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, Path.Combine(relative)));

    private static string PolicyCopy(string relative)
    {
        var text = Read(relative.Split('/', StringSplitOptions.RemoveEmptyEntries));
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

    private static string Slice(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        var to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        return text[from..to];
    }
}
