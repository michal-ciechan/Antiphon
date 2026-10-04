using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class PublishedRoutingDocumentationTests
{
    [Test]
    [Arguments("docs/orchestration-loop.md")]
    [Arguments("docs/agent-kinds.md")]
    [Arguments("docs/ai-agent-tui-configuration.md")]
    public void Required_pair_is_published_for_both_roles(string relative)
    {
        var expected = relative switch
        {
            "docs/orchestration-loop.md" => "Policy on release: Review and Debug on every platform, including Linux, follow the same policy; use Human Required pins with Grok/High then ClaudeCode/High (`grok-4.7`, then `opus`).",
            "docs/agent-kinds.md" => "effective pin reads confirm Review and Debug on every platform use Human Required Grok/High then ClaudeCode/High.",
            "docs/ai-agent-tui-configuration.md" => "effective reads that day confirmed Human Required Grok/High then ClaudeCode/High for Review and Debug on every platform.",
            _ => throw new ArgumentOutOfRangeException(nameof(relative)),
        };

        RoutingSection(Read(relative), relative, "published-required-pair")
            .ShouldContain(expected, customMessage: $"{relative}: published-required-pair");
    }

    [Test]
    [Arguments("docs/orchestration-loop.md")]
    [Arguments("docs/agent-kinds.md")]
    [Arguments("docs/ai-agent-tui-configuration.md")]
    public void Current_guides_do_not_claim_codex_first_review_or_debug(string relative)
    {
        var text = NormalizeClaims(Read(relative));
        foreach (var obsolete in new[] { "Debug is Codex-first", "Review is Codex-first" })
            text.ShouldNotContain(obsolete, Case.Insensitive, $"{relative}: obsolete-codex-first ({obsolete})");
    }

    [Test]
    [Arguments("docs/orchestration-loop.md")]
    [Arguments("docs/agent-kinds.md")]
    [Arguments("docs/ai-agent-tui-configuration.md")]
    public void Publication_is_landed_with_post_activation_acceptance(string relative)
    {
        var document = Read(relative);
        var section = RoutingSection(document, relative, "published-status");
        section.ShouldContain("The prompt change landed", customMessage: $"{relative}: published-status");
        if (relative == "docs/orchestration-loop.md")
            section.ShouldContain("CARD-1011 published policy", customMessage: $"{relative}: published-status");

        foreach (var expected in new[]
                 {
                     "WQ-1",
                     "operator-excluded",
                     relative == "docs/orchestration-loop.md" ? "excluded (not passed)" : "not passed",
                     "WQ-4 remains post-activation acceptance work",
                 })
            section.ShouldContain(expected, customMessage: $"{relative}: qualification-status ({expected})");

        var text = NormalizeClaims(document);
        foreach (var obsolete in new[]
                 {
                     "CARD-1011 publication hold",
                     "approved future role-wide Debug policy is gated",
                     "proposed Debug policy is inactive until",
                 })
            text.ShouldNotContain(obsolete, Case.Insensitive, $"{relative}: obsolete-publication-hold ({obsolete})");
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, relative));

    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ");

    private static string NormalizeClaims(string text) => Collapse(text.Replace("`", ""));

    private static string RoutingSection(string document, string relative, string guard)
    {
        var heading = relative switch
        {
            "docs/orchestration-loop.md" => "## Windows Review and Debug routing",
            "docs/agent-kinds.md" => "## 5. Grok (xAI Grok Build TUI)",
            "docs/ai-agent-tui-configuration.md" => "## Local Grok Build TUI profile",
            _ => throw new ArgumentOutOfRangeException(nameof(relative)),
        };
        var text = "\n" + document.Replace("\r\n", "\n");
        var start = text.IndexOf("\n" + heading + "\n", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, $"{relative}: {guard} (missing heading: {heading})");
        var end = text.IndexOf("\n## ", start + heading.Length + 1, StringComparison.Ordinal);
        return Collapse(end < 0 ? text[start..] : text[start..end]);
    }
}
