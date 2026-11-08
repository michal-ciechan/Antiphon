using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1781. The rc failure loop names the five why-missed items in the owner section,
/// the rc-qualification skill, and the note template.
/// </summary>
[Category("Unit")]
public sealed class WhyMissedWriteUpContractTests
{
    private static readonly string[] FiveItems =
    [
        "introducing commit/card",
        "approving review(s) with scope (Full/Interim) and the selection they actually ran",
        "why the failing test was outside that selection (lane/category/path)",
        "path-glob -> class-filter entry for the impacted-classes table of CARD-1782",
        "same cause as an already written note",
    ];

    [Test]
    public void rc_failure_loop_names_the_five_why_missed_items()
    {
        var root = DelegateScriptRunner.RepoRoot;
        var loop = File.ReadAllText(Path.Combine(root, "docs", "orchestration-loop.md"));
        var section = Slice(
            loop,
            "### Why-missed write-up for a new rc failure cause (CARD-1781)",
            "\n### Post-land miss retrospective (CARD-0811)");
        section.ShouldContain("WHY-MISSED");
        section.ShouldContain("docs/investigations/<date>-why-missed-<cause>.md");
        section.ShouldContain("docs/investigations/why-missed-template.md");

        var skill = File.ReadAllText(Path.Combine(
            root, ".claude", "skills", "antiphon-rc-qualification", "SKILL.md"));
        var skillSection = Slice(
            skill,
            "**Why-missed write-up (CARD-1781).**",
            "\n4. **Integrate repairs through a serial fix pipeline.**");
        skillSection.ShouldContain("WHY-MISSED");
        skillSection.ShouldContain("docs/orchestration-loop.md#why-missed-write-up-card-1781");

        var template = File.ReadAllText(Path.Combine(root, "docs", "investigations", "why-missed-template.md"));
        template.ShouldContain("WHY-MISSED");
        template.ShouldContain("docs/investigations/<date>-why-missed-<cause>.md");

        foreach (var item in FiveItems)
        {
            section.ShouldContain(item, Case.Sensitive, "orchestration-loop section");
            skillSection.ShouldContain(item, Case.Sensitive, "rc-qualification skill");
            template.ShouldContain(item, Case.Sensitive, "why-missed template");
        }
    }

    private static string Slice(string text, string start, string end)
    {
        var at = text.IndexOf(start, StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, start);
        var stop = text.IndexOf(end, at + start.Length, StringComparison.Ordinal);
        stop.ShouldBeGreaterThan(at, end);
        return text[at..stop];
    }
}
