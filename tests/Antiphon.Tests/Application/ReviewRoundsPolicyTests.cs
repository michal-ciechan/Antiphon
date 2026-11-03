using System.Text.RegularExpressions;
using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1647, the three CARD-1635 follow-ups. Review rounds are capped at three per subject
/// (docs/orchestration-loop.md#review-rounds): the guard flags a fourth at creation, the
/// stage-review evidence block carries the round and range, and the owner text is pinned.
/// </summary>
[Category("Unit")]
public sealed class ReviewRoundsPolicyTests
{
    // ---- item 1: the round guard ----------------------------------------------------------------

    [Test]
    public void C1647_a_declared_fourth_round_is_flagged_and_round_three_is_not()
    {
        var fourth = ReviewRoundGuard.Evaluate("CARD-0001", "round: 4 of 3; stringency: blockers only\nrange: a..b", []);
        fourth.ShouldNotBeNull();
        fourth.ShouldContain(ReviewRoundGuard.Code);
        fourth.ShouldContain("round 4");
        fourth.ShouldContain("next: decide");

        ReviewRoundGuard.Evaluate("CARD-0001", "round: 3 of 3; stringency: blockers only", ["aaaaaaaa", "bbbbbbbb", "cccccccc"])
            .ShouldBeNull("a declared round 3 is inside the limit");
    }

    [Test]
    public void C1647_three_completed_reviews_flag_an_undeclared_round()
    {
        var flagged = ReviewRoundGuard.Evaluate("CARD-0001", "Review the change.", ["aaaaaaaa", "bbbbbbbb", "cccccccc"]);
        flagged.ShouldNotBeNull();
        flagged.ShouldContain(ReviewRoundGuard.Code);
        flagged.ShouldContain("CARD-0001 already has 3 completed Review tasks (aaaaaaaa, bbbbbbbb, cccccccc)");
        flagged.ShouldContain("next: decide");

        ReviewRoundGuard.Evaluate("CARD-0001", "Review the change.", ["aaaaaaaa", "bbbbbbbb"])
            .ShouldBeNull("two completed Reviews leave room for round 3");
    }

    /// <summary>
    /// CARD-1647 D-1. A round written with a non-ASCII decimal digit (fullwidth U+FF14, Arabic-Indic
    /// U+0664) is not a declared round: the guard never throws and falls back to the count rule.
    /// </summary>
    [Test]
    [Arguments("round: ４ of 3; stringency: blockers only")]
    [Arguments("round: ٤ of 3; stringency: blockers only")]
    public void C1647_a_non_ascii_round_digit_never_throws_and_falls_back_to_the_count(string goal)
    {
        string? none = "unset", counted = null;
        Should.NotThrow(() => none = ReviewRoundGuard.Evaluate("CARD-0001", goal, []));
        none.ShouldBeNull("no declared round and no completed Reviews");
        Should.NotThrow(() => counted = ReviewRoundGuard.Evaluate("CARD-0001", goal, ["aaaaaaaa", "bbbbbbbb", "cccccccc"]));
        counted.ShouldNotBeNull();
        counted.ShouldContain("CARD-0001 already has 3 completed Review tasks");
    }

    /// <summary>CARD-1647 D-1 (review A-1). <c>round:</c> never reads its number from the next line.</summary>
    [Test]
    public void C1647_a_round_label_never_reads_the_next_lines_number()
    {
        ReviewRoundGuard.DeclaredRound("round:\n7 of 3").ShouldBeNull();
        ReviewRoundGuard.Evaluate("CARD-0001", "round:\n7 of 3", []).ShouldBeNull("the 7 is on the next line, not a round 7");
        ReviewRoundGuard.DeclaredRound("  Round:\t4;").ShouldBe(4);
    }

    /// <summary>
    /// CARD-1676. The round shapes briefs really write have no colon: the guard reads <c>round N</c> as well
    /// as <c>round: N</c>, case-insensitive, at the start of a line or after <c>REVIEW </c>.
    /// </summary>
    [Test]
    [Arguments("REVIEW ROUND 2 OF 3 (diff-only; previous reviewed SHA abc)", 2)]
    [Arguments("Review round 2 (diff-only): CARD-1647 round guard fix", 2)]
    [Arguments("Round 2 of 3", 2)]
    [Arguments("round: 3", 3)]
    [Arguments("CLEANUP RULE: exact paths only.\nREVIEW ROUND 3 OF 3 (blockers only)", 3)]
    [Arguments("CLEANUP RULE: exact paths only.\r\n  round 2 of 3; stringency: diff only", 2)]
    [Arguments("CODE REVIEW ROUND 3 OF 3 (blockers only)", 3)]
    public void C1676_each_real_brief_round_shape_declares_its_round(string goal, int round)
    {
        ReviewRoundGuard.DeclaredRound(goal).ShouldBe(round);
        ReviewRoundGuard.Evaluate("CARD-0001", goal, ["aaaaaaaa", "bbbbbbbb", "cccccccc"])
            .ShouldBeNull("a declared round inside the limit is not flagged, whatever the count");
    }

    /// <summary>CARD-1676. A colon-less fourth round warns with no completed Reviews to count.</summary>
    [Test]
    [Arguments("ROUND 4 OF 3 (blockers only)", 4)]
    [Arguments("Review round 5 (diff-only)", 5)]
    [Arguments("REVIEW ROUND 10 OF 3", 10)]
    public void C1676_a_colon_less_fourth_round_warns(string goal, int round)
    {
        var flagged = ReviewRoundGuard.Evaluate("CARD-0001", goal, []);
        flagged.ShouldNotBeNull();
        flagged.ShouldContain(ReviewRoundGuard.Code);
        flagged.ShouldContain($"declares round {round}");
    }

    /// <summary>
    /// CARD-1676. The colon-less shape keeps the CARD-1647 invariants: non-ASCII digits declare nothing,
    /// a number on the next line is not read, and prose that only mentions rounds declares nothing.
    /// </summary>
    [Test]
    [Arguments("REVIEW ROUND ４ OF 3")]
    [Arguments("Round ٤ of 3")]
    [Arguments("Round\n7 of 3")]
    [Arguments("REVIEW ROUND\n7 OF 3")]
    [Arguments("Review round\r\n7")]
    [Arguments("Review the change in 4 rounds")]
    [Arguments("Rounds 4 of the suite ran green")]
    [Arguments("Review 5 files around 6 call sites")]
    [Arguments("round 12345")]
    [Arguments("Round 3rd pass")]
    [Arguments("PREVIEW ROUND 4 OF 3")]
    public void C1676_a_colon_less_round_keeps_the_ascii_and_same_line_rules(string goal)
    {
        int? declared = -1;
        Should.NotThrow(() => declared = ReviewRoundGuard.DeclaredRound(goal));
        declared.ShouldBeNull();
        string? counted = null;
        Should.NotThrow(() => counted = ReviewRoundGuard.Evaluate("CARD-0001", goal, ["aaaaaaaa", "bbbbbbbb", "cccccccc"]));
        counted.ShouldNotBeNull();
        counted.ShouldContain("CARD-0001 already has 3 completed Review tasks", Case.Sensitive, "an undeclared round falls back to the count");
    }

    /// <summary>
    /// CARD-1683 (sweep 974b0c17 B3, B11). The first ASCII round line declares the round: a fullwidth digit
    /// line is not a round line, so it never hides a later valid one, and a later line never overrides the first.
    /// </summary>
    [Test]
    [Arguments("REVIEW ROUND ４ OF 3\nround: 4", 4)]
    [Arguments("round: 2\nrange: a..b\nround: 5", 2)]
    public void C1683_the_first_ascii_round_line_declares_the_round(string goal, int round)
    {
        ReviewRoundGuard.DeclaredRound(goal).ShouldBe(round);
        var verdict = ReviewRoundGuard.Evaluate("CARD-0001", goal, []);
        if (round > ReviewRoundGuard.MaxRounds)
            verdict.ShouldNotBeNull().ShouldContain($"declares round {round}");
        else
            verdict.ShouldBeNull("the first line declares a round inside the limit");
    }

    // ---- item 2: the stage-review evidence block carries the round ------------------------------

    [Test]
    public void C1647_review_bundle_evidence_block_carries_round_and_range()
    {
        var text = InstructionBundles.TextOf(InstructionBundles.StageReview).ReplaceLineEndings("\n");
        var lines = text.Split('\n');
        var heading = Array.IndexOf(lines, ReviewEvidence.Heading);
        heading.ShouldBeGreaterThanOrEqualTo(0);
        var block = lines.Skip(heading + 1).TakeWhile(l => l.Contains(':') && !l.StartsWith(' ')).ToList();
        block.ShouldContain(l => l.StartsWith("reviewRound: <", StringComparison.Ordinal), "stage-review evidence block names reviewRound");
        block.ShouldContain(l => l.StartsWith("reviewedRange: <", StringComparison.Ordinal), "stage-review evidence block names reviewedRange");

        var parsed = ReviewEvidence.TryParse(string.Join('\n', new[] { ReviewEvidence.Heading }.Concat(block))
            .Replace("<reviewed source task's full GUID: owner unless brief names -FromTask>", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")
            .Replace("<full SHA actually reviewed>", new string('a', 40))
            .Replace("<Full|Interim|None>", "Full"));
        parsed.Usable.ShouldBeTrue("the round lines leave the evidence block parseable");
    }

    // ---- item 3: the rule text and the audit's extra rules --------------------------------------

    [Test]
    public void C1647_review_rounds_owner_text_keeps_every_rule()
    {
        var section = Section(Read("docs", "orchestration-loop.md"), "<a id=\"review-rounds\"></a>");
        foreach (var rule in new[]
        {
            "**Round 1 is the whole change**",
            "**Round 2 reviews only the diff `<round-1 reviewed SHA>..<new tip>`**",
            "It adds no new attack classes",
            "reports blockers only.**",
            "There is no round 4",
            "lows and advisories become cards and never hold the land",
            "A fix brief states the invariant the fix protects",
            "Every fix has a red-first witness",
        })
            section.ShouldContain(rule, Case.Sensitive, $"review-rounds keeps: {rule}");

        Collapse(Read("AGENTS.md")).ShouldContain("Review rounds: at most three per subject; round 1 full, rounds 2 and 3 only the diff from the previous reviewed SHA (round 3 blockers only)");
        var orchestrator = Collapse(Read(".claude", "skills", "antiphon-orchestrator", "SKILL.md"));
        orchestrator.ShouldContain("no round 4");
        orchestrator.ShouldContain("lows become cards");
        orchestrator.ShouldContain("fix briefs state the invariant; every fix has a red-first witness");
    }

    private static string Section(string text, string anchor)
    {
        var start = text.IndexOf(anchor, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, anchor);
        var end = text.IndexOf("\n### ", text.IndexOf("### ", start, StringComparison.Ordinal) + 4, StringComparison.Ordinal);
        return Collapse(end < 0 ? text[start..] : text[start..end]);
    }

    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ");

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([RepoRoot(), .. parts]));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found.");
    }
}
