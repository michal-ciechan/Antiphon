using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class ProviderQuotaRefusalTests
{
    private static readonly DateTime Evidence = new(2026, 9, 25, 16, 34, 7, DateTimeKind.Utc);
    private const string Linux = "You’ve hit your usage limit. Visit https://chatgpt.com/codex/settings/usage to purchase more credits or try again at Sep 26th, 2026 11:16 AM.";
    private const string Windows = "Error running remote compact task: You’ve hit your usage limit. Visit https://chatgpt.com/codex/settings/usage to purchase more credits or try again at Sep 26th, 2026 12:16 PM.";
    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Test]
    public void Structural_quota_without_text_is_wall() =>
        ApiErrorClassifier.Classify("usage_limit_exceeded", null, null).ShouldBe(ApiErrorClassification.Wall);

    [Test]
    public void Straight_apostrophe_usage_diagnostic_is_wall() =>
        ApiErrorClassifier.Classify("other", null, "You've hit your usage limit. Try again later.").ShouldBe(ApiErrorClassification.Wall);

    [Test]
    public void Curly_apostrophe_usage_diagnostic_is_wall() =>
        ApiErrorClassifier.Classify("other", null, Linux).ShouldBe(ApiErrorClassification.Wall);

    [Test]
    public void Compact_wrapper_usage_diagnostic_is_wall() =>
        ApiErrorClassifier.Classify("transport", null, Windows).ShouldBe(ApiErrorClassification.Wall);

    [Test]
    public void Claude_session_limit_diagnostic_is_wall() =>
        ApiErrorClassifier.Classify("other", null, UsageLimitWallParser.SessionLimitProductionText).ShouldBe(ApiErrorClassification.Wall);

    [Test]
    public void Claude_named_model_limit_diagnostic_is_wall() =>
        ApiErrorClassifier.Classify("other", null, UsageLimitWallParser.FableModelCapIncidentText).ShouldBe(ApiErrorClassification.Wall);

    [Test]
    public void Grok_exhausted_credits_diagnostic_is_wall() =>
        ApiErrorClassifier.Classify("other", 403, "API error (status 403 Forbidden): exhausted credits").ShouldBe(ApiErrorClassification.Wall);

    [Test]
    public void Grok_spending_limit_diagnostic_is_wall() =>
        ApiErrorClassifier.Classify("other", 403, "API error (status 403 Forbidden): monthly spending limit").ShouldBe(ApiErrorClassification.Wall);

    [Test]
    public void Codex_dated_reset_uses_utc_evidence_zone()
    {
        var wall = UsageLimitWallParser.Parse(Evidence, Linux, "fable", "Etc/UTC")!;
        wall.ResetAt.ShouldBe(Utc(2026, 9, 26, 11, 16));
        wall.ResetZoneId.ShouldBe("Etc/UTC");
        wall.RawText.ShouldBe(Linux);
    }

    [Test]
    public void Windows_dated_reset_uses_london_evidence_zone()
    {
        var wall = UsageLimitWallParser.Parse(Evidence, Windows, "fable", "Europe/London")!;
        wall.ResetAt.ShouldBe(Utc(2026, 9, 26, 11, 16));
        wall.ResetZoneId.ShouldBe("Europe/London");
    }

    [Test]
    public void Dated_reset_accepts_ordinal_variants()
    {
        foreach (var day in new[] { "1st", "2nd", "3rd", "4th" })
        {
            var text = $"You've hit your usage limit. Try again at Oct {day}, 2026 1:02 PM.";
            UsageLimitWallParser.Parse(Evidence, text, "fable", "Etc/UTC")!.ResetAt.ShouldNotBeNull();
        }
    }

    [Test]
    public void Dated_reset_noon_is_twelve()
    {
        var wall = UsageLimitWallParser.Parse(Evidence, "try again at Sep 26th, 2026 12:00 PM", "fable", "Etc/UTC")!;
        wall.ResetAt.ShouldBe(Utc(2026, 9, 26, 12, 0));
    }

    [Test]
    public void Dated_reset_midnight_is_zero()
    {
        var wall = UsageLimitWallParser.Parse(Evidence, "try again at Sep 26th, 2026 12:00 AM", "fable", "Etc/UTC")!;
        wall.ResetAt.ShouldBe(Utc(2026, 9, 26, 0, 0));
    }

    [Test]
    public void Explicit_date_is_not_rolled_across_year_end()
    {
        var later = Utc(2027, 1, 1, 0, 0);
        var wall = UsageLimitWallParser.Parse(later, "try again at Dec 31st, 2026 11:59 PM", "fable", "Etc/UTC")!;
        wall.ResetAt.ShouldBe(Utc(2026, 12, 31, 23, 59));
    }

    [Test]
    public void Invalid_calendar_and_clock_are_unparsed()
    {
        foreach (var text in new[] { "try again at Feb 30th, 2026 1:00 PM", "try again at Sep 26th, 2026 13:00 PM" })
        {
            var wall = UsageLimitWallParser.Parse(Evidence, text, "fable", "Etc/UTC")!;
            wall.ResetAt.ShouldBeNull();
            wall.ResetParseFailure.ShouldNotBeNull();
        }
    }

    [Test]
    public void Dated_reset_without_timezone_is_honest_fallback()
    {
        var wall = UsageLimitWallParser.Parse(Evidence, Linux, "fable")!;
        wall.ResetAt.ShouldBeNull();
        wall.ResetParseFailure.ShouldContain("timezone unavailable");
    }

    [Test]
    public void Dated_reset_dst_gap_and_fold_follow_zone_rules()
    {
        var gap = UsageLimitWallParser.Parse(Evidence, "try again at Mar 29th, 2026 1:30 AM", "fable", "Europe/London")!;
        gap.ResetAt.ShouldBeNull();
        gap.ResetParseFailure.ShouldContain("nonexistent");
        var fold = UsageLimitWallParser.Parse(Evidence, "try again at Oct 25th, 2026 1:30 AM", "fable", "Europe/London")!;
        fold.ResetAt.ShouldBe(Utc(2026, 10, 25, 1, 30));
    }

    [Test]
    public void Delayed_replay_preserves_original_dated_reset()
    {
        var wall = UsageLimitWallParser.Parse(Utc(2026, 10, 1, 0, 0), Linux, "fable", "Etc/UTC")!;
        wall.ResetAt.ShouldBe(Utc(2026, 9, 26, 11, 16));
    }
}
