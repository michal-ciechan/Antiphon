using Antiphon.Server.Application.Review;
using Antiphon.Server.Application.Review.Checks;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

/// <summary>The rounds policy (design 5), the route after a round, grouping (4.5) and anchor resolution (4.8): pure rules.</summary>
[Category("Unit")]
public class ReviewRoundsPolicyTests
{
    private static readonly ReviewPolicySettings Policy = new();

    [Test]
    public void The_bar_rises_by_round_and_the_last_row_holds_for_later_rounds()
    {
        ReviewRoundsPolicy.MinSeverity(ReviewRoundsPolicy.BarFor(Policy, 1)).ShouldBe(ReviewSeverity.Nit);
        ReviewRoundsPolicy.MinSeverity(ReviewRoundsPolicy.BarFor(Policy, 2)).ShouldBe(ReviewSeverity.Minor);
        ReviewRoundsPolicy.MinSeverity(ReviewRoundsPolicy.BarFor(Policy, 3)).ShouldBe(ReviewSeverity.Major);
        ReviewRoundsPolicy.BarFor(Policy, 9).MinConfidence.ShouldBe(0.8);

        var round3 = ReviewRoundsPolicy.BarFor(Policy, 3);
        ReviewRoundsPolicy.AboveBar(ReviewSeverity.Minor, 0.95, bypass: false, round3).ShouldBeFalse();
        ReviewRoundsPolicy.AboveBar(ReviewSeverity.Minor, 0.3, bypass: true, round3).ShouldBeTrue("a bypass category posts in any round (D-35)");
        ReviewRoundsPolicy.AboveBar(ReviewSeverity.Major, 0.7, bypass: false, round3).ShouldBeFalse("under round 3's confidence");
        ReviewRoundsPolicy.IsBypass(["Data-Loss"], Policy).ShouldBeTrue();
        ReviewRoundsPolicy.IsBypass(["perf"], Policy).ShouldBeFalse();
    }

    [Test]
    public void Round_one_nits_go_to_the_summary_up_to_max_nits_and_off_diff_lines_are_never_inline()
    {
        var round1 = ReviewRoundsPolicy.BarFor(Policy, 1);
        var nits = 0;
        var placements = Enumerable.Range(0, 7).Select(_ => ReviewRoundsPolicy.Place(ReviewSeverity.Nit, 0.9, false, true, round1, ref nits)).ToList();
        placements.Count(p => p == ReviewPlacement.SummaryOnly).ShouldBe(5);
        placements.Skip(5).ShouldAllBe(p => p == ReviewPlacement.BelowBar);

        nits = 0;
        ReviewRoundsPolicy.Place(ReviewSeverity.Major, 0.9, false, lineInDiff: true, round1, ref nits).ShouldBe(ReviewPlacement.Inline);
        ReviewRoundsPolicy.Place(ReviewSeverity.Major, 0.9, false, lineInDiff: false, round1, ref nits).ShouldBe(ReviewPlacement.SummaryOnly);
        ReviewRoundsPolicy.Place(ReviewSeverity.Major, 0.4, false, lineInDiff: true, round1, ref nits).ShouldBe(ReviewPlacement.BelowBar);
        ReviewRoundsPolicy.Place(ReviewSeverity.Nit, 0.9, false, true, ReviewRoundsPolicy.BarFor(Policy, 2), ref nits).ShouldBe(ReviewPlacement.BelowBar);
    }

    [Test]
    public void Blocking_follows_the_current_round_and_the_route_counts_bot_only_rounds()
    {
        var nit = Finding(ReviewSeverity.Nit, 0.9);
        ReviewRoundsPolicy.IsBlocking(nit, 1, Policy).ShouldBeTrue();
        ReviewRoundsPolicy.IsBlocking(nit, 2, Policy).ShouldBeFalse("an open round-1 nit is below round 2's bar");
        ReviewRoundsPolicy.IsBlocking(Finding(ReviewSeverity.Blocker, 0.1), 5, Policy).ShouldBeTrue("an open blocker always blocks (D-21)");
        ReviewRoundsPolicy.IsBlocking(Finding(ReviewSeverity.Minor, 0.2, bypass: true), 4, Policy).ShouldBeTrue();
        ReviewRoundsPolicy.IsBlocking(Finding(ReviewSeverity.Major, 0.9, ReviewFindingStatus.Fixed), 1, Policy).ShouldBeFalse();
        ReviewRoundsPolicy.IsBlocking(Finding(ReviewSeverity.Major, 0.9, ReviewFindingStatus.WillFix), 1, Policy).ShouldBeTrue();

        ReviewRoundsPolicy.Route(0, 5, Policy).ShouldBe("clean");
        ReviewRoundsPolicy.Route(1, 2, Policy).ShouldBe("findings");
        ReviewRoundsPolicy.Route(1, 3, Policy).ShouldBe("bot-round-limit");
    }

    [Test]
    public void Grouping_keeps_bypass_categories_alone_and_packs_the_rest_by_category_within_the_budget()
    {
        var checks = new[]
        {
            Check("C1", "correctness", ReviewSeverity.Major), Check("C2", "concurrency", ReviewSeverity.Minor),
            Check("S1", "web", ReviewSeverity.Blocker, "security"), Check("S2", "auth", ReviewSeverity.Minor, "security"),
            Check("D1", "data", ReviewSeverity.Blocker, "data-loss"), Check("T1", "tests", ReviewSeverity.Minor),
        };

        var roomy = ReviewGrouping.Group(checks, Policy.EffectiveBypassCategories, tokenBudget: 10_000);
        roomy.Select(g => (g.Key, string.Join(",", g.CheckIds), g.Bypass)).ShouldBe(
        [
            ("security", "S1,S2", true), ("data-loss", "D1", true), ("concurrency+correctness+tests", "C2,C1,T1", false),
        ]);
        roomy[2].Tier.ShouldBe(ReviewGrouping.DeepTier, "its most severe check is major");

        var tight = ReviewGrouping.Group(checks, Policy.EffectiveBypassCategories, tokenBudget: ReviewGrouping.Tokens(checks[0]) + 1);
        tight.Where(g => !g.Bypass).Select(g => g.Key).ShouldBe(["concurrency", "correctness", "tests"]);
        tight.Where(g => g.Bypass).Select(g => g.Key).ShouldBe(["security", "security-2", "data-loss"]);
        tight.SelectMany(g => g.CheckIds).Order().ShouldBe(checks.Select(c => c.Id).Order(), "each check is in exactly one group");
    }

    [Test]
    public void Anchors_resolve_to_new_file_lines_in_the_diff_and_fall_back_to_the_file()
    {
        const string diff = "diff --git a/src/A.cs b/src/A.cs\n--- a/src/A.cs\n+++ b/src/A.cs\n@@ -5,3 +5,4 @@ class A\n     int x;\n-    int y;\n+    int y = 1;\n+    int z;\n     }\n";

        var hit = ReviewDiffAnchors.Locate(diff, "src/A.cs", "int z;").ShouldNotBeNull();
        (hit.Line, hit.InDiff).ShouldBe((7, true));
        hit.Hunk!.ShouldStartWith("@@ -5,3 +5,4 @@");
        ReviewDiffAnchors.Locate(diff, "src/A.cs", "int y")!.Line.ShouldBe(6, "a trimmed substring match");
        ReviewDiffAnchors.Locate(diff, "src/A.cs", "return 0;").ShouldBeNull();
        var file = ReviewDiffAnchors.Locate(diff, "src/A.cs", "return 0;", "class A\n{\n    return 0;\n}").ShouldNotBeNull();
        (file.Line, file.InDiff).ShouldBe((3, false));
    }

    private static ReviewFinding Finding(ReviewSeverity severity, double confidence, ReviewFindingStatus status = ReviewFindingStatus.Open,
        bool bypass = false) => new() { Severity = severity, Confidence = confidence, Status = status, BypassesRoundBar = bypass };

    private static ReviewCheck Check(string id, string category, ReviewSeverity severity, params string[] flags) =>
        new(id, $"Check {id}", category, severity, flags, "active", false, ReviewCheckTriggers.None, [],
            [new ReviewCheckSection("model", "Prompt", new string('x', 400))], "", id);
}
