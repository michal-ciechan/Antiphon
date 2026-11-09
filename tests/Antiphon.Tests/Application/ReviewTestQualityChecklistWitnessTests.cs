using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1809. The rc run 231f573c test-quality failures became Review checks R1-R5 in
/// docs/testing-and-build.md. The stage-review bundle has no room for its own line, so the
/// checklist sits inside the section its existing <c>#performance-standards</c> pointer opens.
/// Each test deletes a pinned phrase in memory and requires the assertion to fail.
/// </summary>
[Category("Unit")]
public sealed class ReviewTestQualityChecklistWitnessTests
{
    private const string Heading = "### Review test-quality checklist (CARD-1809)";
    private const string Anchor = "<a id=\"review-test-quality-checklist\"></a>";
    private const string BundlePointer = "docs/testing-and-build.md#os-branch-witnesses, #";
    private const string OwnerLink = "testing-and-build.md#review-test-quality-checklist";

    private static readonly string[] Checks =
    [
        // R1 poll-vs-assert (rc group 4, Card1277PtyHostFixtureTests).
        "**R1 Poll covers every assertion.**",
        "its done-predicate covers every assertion after it",
        "ends on the LAST record asserted, never the first record of a multi-record turn",
        "Compare sibling tests of the same class",
        // R2 generated-text newlines (rc group 96, Card1773TranscriptPromptStampMigrationTests).
        "**R2 Generated text is line-ending neutral.**",
        "does not hard-code `\\n` or `\\r\\n`",
        "`Environment.NewLine` or `ReplaceLineEndings`",
        "Linux-only Review and checkpoints cannot see this",
        "a Windows-lane owed note",
        // R3 state-change callers (rc group 71, PreDispatchFailureDeliveryTests).
        "**R3 Every caller of a changed terminal state.**",
        "every test class that reaches that service through any caller",
        "`git grep` the service and its state enum in `tests/**`",
        "not only the name-prefix family",
        "lists the callers it checked",
        // R4 limiter (rc group 2, ProcessSpawnLimitTests).
        "**R4 Limiter change runs the limiter census.**",
        "carrying a `ParallelLimiter` attribute",
        "runs `ProcessSpawnLimitTests` in the Review selection",
        "until CARD-1807 lands",
        // R5 repeat flakes (C1759, RemoteWorkspacePreparerTests cancel, C467_V18).
        "**R5 A repeated passes-alone red gets a Debug card.**",
        "seen twice for the same method in review or merge reports",
        "files or links a Debug card",
    ];

    [Test]
    public void C1809_the_checklist_states_every_check_inside_the_section_the_review_bundle_opens()
    {
        var doc = Read("docs/testing-and-build.md");
        AssertChecklist(doc);
        foreach (var phrase in Checks.Append(Heading).Append(Anchor))
            RejectWithout(doc, phrase, AssertChecklist);

        // Moving the checklist below the next H2 takes it out of what #performance-standards opens.
        var moved = doc.Replace(Anchor + "\n" + Heading, "", StringComparison.Ordinal)
            + "\n## Elsewhere\n\n" + Anchor + "\n" + Heading + "\n";
        moved.ShouldNotBe(doc);
        Should.Throw<ShouldAssertException>(() => AssertChecklist(moved));
    }

    [Test]
    public void C1809_stage_review_opens_the_section_that_holds_the_checklist()
    {
        var bundle = Read("server/Bundles/stage-review.md");
        var doc = Read("docs/testing-and-build.md");
        AssertBundle(bundle, doc);
        RejectWithout(bundle, ", #performance-standards", b => AssertBundle(b, doc));

        // The checklist moved under another H2 is no longer what the bundle's pointer opens.
        var moved = doc.Replace(Anchor + "\n" + Heading, "", StringComparison.Ordinal)
            + "\n## Elsewhere\n\n" + Anchor + "\n" + Heading + "\n";
        Should.Throw<ShouldAssertException>(() => AssertBundle(bundle, moved));
    }

    [Test]
    public void C1809_review_rounds_owner_applies_the_checklist_every_round()
    {
        var doc = Read("docs/orchestration-loop.md");
        AssertReviewRounds(doc);
        RejectWithout(doc, OwnerLink, AssertReviewRounds);
        RejectWithout(doc, "round 3 reports a breach only when it is a blocker", AssertReviewRounds);
    }

    [Test]
    public void C1809_orchestrator_skill_review_briefs_name_the_checklist()
    {
        var skill = Read(".claude/skills/antiphon-orchestrator/SKILL.md");
        AssertSkill(skill);
        RejectWithout(skill, "Each Review brief names the [Review test-quality checklist]", AssertSkill);
    }

    private static void AssertChecklist(string doc)
    {
        var section = H2Section(doc, "## Performance standards");
        section.ShouldContain(Anchor + "\n" + Heading, Case.Sensitive, "the checklist sits under ## Performance standards");
        var checklist = section[section.IndexOf(Heading, StringComparison.Ordinal)..];
        foreach (var phrase in Checks)
            checklist.ShouldContain(phrase, Case.Sensitive, $"CARD-1809 check missing: {phrase}");
    }

    private static void AssertBundle(string bundle, string doc)
    {
        // The bundle's second anchor is the GitHub slug of the H2 that holds the checklist.
        var at = doc.IndexOf(Anchor + "\n" + Heading, StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, "the checklist heading exists");
        var h2 = doc.LastIndexOf("\n## ", at, StringComparison.Ordinal);
        h2.ShouldBeGreaterThanOrEqualTo(0, "the checklist sits under an H2");
        var heading = doc[(h2 + 1)..doc.IndexOf('\n', h2 + 1)];
        bundle.ShouldContain(BundlePointer + Slug(heading) + ".", Case.Sensitive, $"stage-review opens {heading}");
    }

    private static void AssertReviewRounds(string doc)
    {
        var start = doc.IndexOf("<a id=\"review-rounds\"></a>", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, "the review-rounds anchor exists");
        var end = doc.IndexOf("\n### ", doc.IndexOf("\n### ", start, StringComparison.Ordinal) + 5, StringComparison.Ordinal);
        var section = end < 0 ? doc[start..] : doc[start..end];
        section.ShouldContain(OwnerLink, Case.Sensitive);
        section.ShouldContain("CARD-1809 R1-R5", Case.Sensitive);
        section.ShouldContain("round 3 reports a breach only when it is a blocker", Case.Sensitive);
    }

    private static void AssertSkill(string skill)
    {
        skill.ShouldContain("Each Review brief names the [Review test-quality checklist]", Case.Sensitive);
        skill.ShouldContain("../../../docs/" + OwnerLink, Case.Sensitive);
        skill.ShouldContain("CARD-1809", Case.Sensitive);
    }

    private static string H2Section(string doc, string heading)
    {
        var start = doc.IndexOf("\n" + heading + "\n", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, heading);
        var end = doc.IndexOf("\n## ", start + heading.Length + 1, StringComparison.Ordinal);
        return end < 0 ? doc[start..] : doc[start..end];
    }

    private static string Slug(string heading) =>
        Regex.Replace(heading.TrimStart('#').Trim().ToLowerInvariant(), "[^a-z0-9 -]", "").Replace(' ', '-');

    private static void RejectWithout(string text, string phrase, Action<string> assert)
    {
        var mutant = text.Replace(phrase, "", StringComparison.Ordinal);
        mutant.ShouldNotBe(text, $"phrase not present: {phrase}");
        Should.Throw<ShouldAssertException>(() => assert(mutant));
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, relative)).ReplaceLineEndings("\n");
}
