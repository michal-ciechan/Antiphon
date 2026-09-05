using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class UsageLimitWallParserTests
{
    // 2026-07-15 16:00 UTC is 17:00 BST (Europe/London UTC+1). 6:10pm London that day is 17:10 UTC.
    private static readonly DateTime SummerAfternoonUtc =
        new(2026, 7, 15, 16, 0, 0, DateTimeKind.Utc);

    // 2026-01-15 16:00 UTC is 16:00 GMT. 6:10pm London that day is 18:10 UTC.
    private static readonly DateTime WinterAfternoonUtc =
        new(2026, 1, 15, 16, 0, 0, DateTimeKind.Utc);

    [Test]
    public void Session_limit_fixture_is_session_limit_with_named_zone()
    {
        var wall = UsageLimitWallParser.Parse(
            SummerAfternoonUtc, UsageLimitWallParser.SessionLimitFixtureText, "fable");

        wall.ShouldNotBeNull();
        wall!.Kind.ShouldBe(UsageLimitWallKind.SessionLimit);
        wall.ModelAlias.ShouldBe("fable");
        wall.ResetZoneId.ShouldBe("Europe/London");
        wall.ResetAt.ShouldBe(new DateTime(2026, 7, 15, 17, 10, 0, DateTimeKind.Utc));
    }

    [Test]
    public void London_summer_is_not_the_UTC_clock()
    {
        var wall = UsageLimitWallParser.Parse(
            SummerAfternoonUtc, UsageLimitWallParser.SessionLimitFixtureText, "fable");

        wall!.ResetAt.ShouldNotBe(new DateTime(2026, 7, 15, 18, 10, 0, DateTimeKind.Utc),
            "6:10pm Europe/London in July is 17:10 UTC, not 18:10");
    }

    [Test]
    public void London_winter_is_GMT()
    {
        var wall = UsageLimitWallParser.Parse(
            WinterAfternoonUtc, UsageLimitWallParser.SessionLimitFixtureText, "fable");

        wall!.ResetAt.ShouldBe(new DateTime(2026, 1, 15, 18, 10, 0, DateTimeKind.Utc));
    }

    [Test]
    public void A_reset_already_past_today_rolls_to_tomorrow()
    {
        var now = new DateTime(2026, 7, 15, 17, 15, 0, DateTimeKind.Utc); // 18:15 BST
        var wall = UsageLimitWallParser.Parse(
            now, UsageLimitWallParser.SessionLimitFixtureText, "fable");

        wall!.ResetAt.ShouldBe(new DateTime(2026, 7, 16, 17, 10, 0, DateTimeKind.Utc));
    }

    [Test]
    public void Fable_5_incident_is_model_cap_on_fable()
    {
        var wall = UsageLimitWallParser.Parse(
            SummerAfternoonUtc, UsageLimitWallParser.FableModelCapIncidentText, fallbackAlias: "opus");

        wall.ShouldNotBeNull();
        wall!.Kind.ShouldBe(UsageLimitWallKind.ModelCap);
        wall.ModelAlias.ShouldBe("fable", "the stub names Fable 5; fallback must not win");
        wall.ResetAt.ShouldBeNull();
        wall.RawText.ShouldBe(UsageLimitWallParser.FableModelCapIncidentText);
    }

    [Test]
    [Arguments("You've reached your Sonnet 5 limit.", "sonnet")]
    [Arguments("You've reached your Haiku 4.5 limit.", "haiku")]
    [Arguments("You've reached your Opus 5 limit.", "opus")]
    public void Named_family_limits_map_to_canonical_aliases(string text, string alias)
    {
        var wall = UsageLimitWallParser.Parse(SummerAfternoonUtc, text, fallbackAlias: "fable");
        wall!.Kind.ShouldBe(UsageLimitWallKind.ModelCap);
        wall.ModelAlias.ShouldBe(alias);
        wall.ResetAt.ShouldBeNull();
    }

    [Test]
    public void Fallback_alias_is_used_when_the_text_names_no_model()
    {
        var wall = UsageLimitWallParser.Parse(
            SummerAfternoonUtc, "You've hit your session limit · resets 18:10 (Europe/London)", "sonnet");

        wall!.ModelAlias.ShouldBe("sonnet");
        wall.Kind.ShouldBe(UsageLimitWallKind.SessionLimit);
    }

    [Test]
    public void Unparseable_reset_degrades_to_model_cap_not_the_30_minute_ladder()
    {
        var wall = UsageLimitWallParser.Parse(
            SummerAfternoonUtc, "You've hit your session limit · try again later", "fable");

        wall!.Kind.ShouldBe(UsageLimitWallKind.ModelCap);
        wall.ResetAt.ShouldBeNull();
        wall.ModelAlias.ShouldBe("fable");
    }

    [Test]
    public void No_alias_at_all_returns_null()
    {
        UsageLimitWallParser.Parse(
            SummerAfternoonUtc, "You've hit a wall", fallbackAlias: null)
            .ShouldBeNull();
        UsageLimitWallParser.Parse(
            SummerAfternoonUtc, "You've hit a wall", fallbackAlias: "<synthetic>")
            .ShouldBeNull();
    }

    [Test]
    public void LooksLikeCapacity_matches_grok_vocabulary_and_the_card_text()
    {
        UsageLimitWallParser.LooksLikeCapacity("Grok Build usage balance exhausted").ShouldBeTrue();
        UsageLimitWallParser.LooksLikeCapacity("out of credits or over your spending limit").ShouldBeTrue();
        UsageLimitWallParser.LooksLikeCapacity("usage limit reached").ShouldBeTrue();
        UsageLimitWallParser.LooksLikeCapacity("team has exhausted its credits or reached its monthly spending limit")
            .ShouldBeTrue();
        UsageLimitWallParser.LooksLikeCapacity("permission denied: not allowed").ShouldBeFalse();
        UsageLimitWallParser.LooksLikeCapacity(null).ShouldBeFalse();
    }

    [Test]
    public void Grok_402_format_reason_is_the_capacity_form()
    {
        var wall = UsageLimitWallParser.Parse(
            SummerAfternoonUtc,
            "API error (status 402 Payment Required): Grok Build usage balance exhausted",
            "grok-4.6");

        wall.ShouldNotBeNull();
        wall!.Kind.ShouldBe(UsageLimitWallKind.ModelCap);
        wall.ModelAlias.ShouldBe("grok-4.6");
        wall.ResetAt.ShouldBeNull();
        var reason = UsageLimitWallParser.FormatReason(wall);
        reason.ShouldContain("grok-4.6 provider capacity");
        reason.ShouldContain("HTTP 402 Payment Required");
        reason.ShouldContain("usage balance exhausted");
        reason.ShouldContain("no reset stated");
    }

    [Test]
    public void A_trailing_retry_suffix_leaves_402_status_phrase_and_detail_unchanged()
    {
        var withSuffix = UsageLimitWallParser.Parse(
            SummerAfternoonUtc,
            "API error (status 402 Payment Required): Grok Build usage balance exhausted [after 3 retries]",
            "grok-4.6");
        var without = UsageLimitWallParser.Parse(
            SummerAfternoonUtc,
            "API error (status 402 Payment Required): Grok Build usage balance exhausted",
            "grok-4.6");

        withSuffix.ShouldNotBeNull();
        without.ShouldNotBeNull();
        UsageLimitWallParser.FormatReason(withSuffix!).ShouldBe(UsageLimitWallParser.FormatReason(without!));
        var reason = UsageLimitWallParser.FormatReason(withSuffix!);
        reason.ShouldContain("HTTP 402 Payment Required");
        reason.ShouldContain("usage balance exhausted");
        reason.ShouldNotContain("after 3 retries");
    }

    [Test]
    public void Twenty_four_hour_reset_form_parses()
    {
        var wall = UsageLimitWallParser.Parse(
            SummerAfternoonUtc, "resets 18:10 (Europe/London)", "fable");

        wall!.Kind.ShouldBe(UsageLimitWallKind.SessionLimit);
        wall.ResetAt.ShouldBe(new DateTime(2026, 7, 15, 17, 10, 0, DateTimeKind.Utc));
    }
}
