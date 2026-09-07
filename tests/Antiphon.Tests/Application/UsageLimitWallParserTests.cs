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

    [Test]
    [Arguments("2026-09-06T07:35:00Z", "You've hit your session limit · resets 9am (Europe/London)", "2026-09-06T08:00:00Z")]
    [Arguments("2026-09-06T11:53:00Z", "You've hit your session limit · resets 2pm (Europe/London)", "2026-09-06T13:00:00Z")]
    [Arguments("2026-09-06T08:00:00Z", "resets 9am (Europe/London)", "2026-09-06T08:00:00Z")]
    [Arguments("2026-09-06T08:07:48Z", "resets 9am (Europe/London)", "2026-09-07T08:00:00Z")]
    public void Card0412_V01_hour_only_am_pm_vectors(string evidenceIso, string text, string expectedResetIso)
    {
        var evidence = DateTime.Parse(evidenceIso, null, System.Globalization.DateTimeStyles.RoundtripKind);
        var expected = DateTime.Parse(expectedResetIso, null, System.Globalization.DateTimeStyles.RoundtripKind);
        var wall = UsageLimitWallParser.Parse(evidence, text, "opus");

        wall.ShouldNotBeNull();
        wall!.Kind.ShouldBe(UsageLimitWallKind.SessionLimit);
        wall.ModelAlias.ShouldBe("opus");
        wall.ResetZoneId.ShouldBe("Europe/London");
        wall.ResetAt.ShouldBe(expected);
        wall.ResetParseFailure.ShouldBeNull();
        UsageLimitWallParser.FormatReason(wall).ShouldContain("session-limit resets");
    }

    [Test]
    [Arguments("reset at 9 AM", "2026-01-15T08:00:00Z", "2026-01-15T09:00:00Z")]
    [Arguments("resets 09:00", "2026-01-15T08:00:00Z", "2026-01-15T09:00:00Z")]
    public void Card0412_V01_utc_when_zone_omitted(string suffix, string evidenceIso, string expectedResetIso)
    {
        var evidence = DateTime.Parse(evidenceIso, null, System.Globalization.DateTimeStyles.RoundtripKind);
        var expected = DateTime.Parse(expectedResetIso, null, System.Globalization.DateTimeStyles.RoundtripKind);
        var wall = UsageLimitWallParser.Parse(evidence, suffix, "fable");

        wall.ShouldNotBeNull();
        wall!.Kind.ShouldBe(UsageLimitWallKind.SessionLimit);
        wall.ResetZoneId.ShouldBeNull();
        wall.ResetAt.ShouldBe(expected);
    }

    [Test]
    public void Card0412_V01_minute_bearing_london_dst()
    {
        var summer = UsageLimitWallParser.Parse(
            SummerAfternoonUtc, "resets 6:10pm (Europe/London)", "fable");
        summer!.Kind.ShouldBe(UsageLimitWallKind.SessionLimit);
        summer.ResetAt.ShouldBe(new DateTime(2026, 7, 15, 17, 10, 0, DateTimeKind.Utc));

        var winter = UsageLimitWallParser.Parse(
            WinterAfternoonUtc, "resets 6:10pm (Europe/London)", "fable");
        winter!.Kind.ShouldBe(UsageLimitWallKind.SessionLimit);
        winter.ResetAt.ShouldBe(new DateTime(2026, 1, 15, 18, 10, 0, DateTimeKind.Utc));
    }

    [Test]
    public void Card0412_V01_noon_and_midnight()
    {
        var noonEvidence = new DateTime(2026, 9, 6, 10, 0, 0, DateTimeKind.Utc);
        var noon = UsageLimitWallParser.Parse(noonEvidence, "resets 12pm (Europe/London)", "fable");
        noon!.Kind.ShouldBe(UsageLimitWallKind.SessionLimit);
        noon.ResetAt.ShouldBe(new DateTime(2026, 9, 6, 11, 0, 0, DateTimeKind.Utc));

        var midnightEvidence = new DateTime(2026, 9, 6, 22, 0, 0, DateTimeKind.Utc);
        var midnight = UsageLimitWallParser.Parse(midnightEvidence, "resets 12am (Europe/London)", "fable");
        midnight!.Kind.ShouldBe(UsageLimitWallKind.SessionLimit);
        midnight.ResetAt.ShouldBe(new DateTime(2026, 9, 6, 23, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    public void Card0412_V01_dst_gap_is_model_cap_with_parse_failure()
    {
        var evidence = new DateTime(2026, 3, 29, 0, 0, 0, DateTimeKind.Utc);
        var wall = UsageLimitWallParser.Parse(evidence, "resets 1:30am (Europe/London)", "fable");

        wall.ShouldNotBeNull();
        wall!.Kind.ShouldBe(UsageLimitWallKind.ModelCap);
        wall.ResetAt.ShouldBeNull();
        wall.ResetParseFailure.ShouldNotBeNull();
        wall.ResetParseFailure.ShouldContain("nonexistent local time");
        UsageLimitWallParser.FormatReason(wall).ShouldContain("reset unparseable");
        UsageLimitWallParser.FormatReason(wall).ShouldNotContain("no reset stated");
    }

    [Test]
    [Arguments("2026-10-25T00:00:00Z")]
    [Arguments("2026-10-25T00:45:00Z")]
    public void Card0412_V01_dst_fold_chooses_later_utc(string evidenceIso)
    {
        var evidence = DateTime.Parse(evidenceIso, null, System.Globalization.DateTimeStyles.RoundtripKind);
        var wall = UsageLimitWallParser.Parse(evidence, "resets 1:30am (Europe/London)", "fable");

        wall.ShouldNotBeNull();
        wall!.Kind.ShouldBe(UsageLimitWallKind.SessionLimit);
        wall.ResetAt.ShouldBe(new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc));
    }

    [Test]
    [Arguments("resets 9:7am (Europe/London)")]
    [Arguments("resets 9:000am (Europe/London)")]
    [Arguments("resets 9amjunk (Europe/London)")]
    [Arguments("resets 25:00 (Europe/London)")]
    [Arguments("resets 0am (Europe/London)")]
    [Arguments("resets 13pm (Europe/London)")]
    [Arguments("resets 9 (Europe/London)")]
    [Arguments("resets 9am (")]
    [Arguments("resets 9am ()")]
    [Arguments("resets 9am (Mars/Olympus)")]
    public void Card0412_V01_malformed_tokens_do_not_partial_match(string text)
    {
        var evidence = new DateTime(2026, 9, 6, 7, 35, 0, DateTimeKind.Utc);
        var wall = UsageLimitWallParser.Parse(evidence, text, "fable");

        wall.ShouldNotBeNull();
        wall!.Kind.ShouldBe(UsageLimitWallKind.ModelCap);
        wall.ResetAt.ShouldBeNull();
        wall.ResetParseFailure.ShouldNotBeNull();
        var reason = UsageLimitWallParser.FormatReason(wall);
        reason.ShouldContain("reset unparseable");
        reason.ShouldNotContain("no reset stated");
    }

    [Test]
    public void Card0412_V01_absent_reset_is_distinct_from_invalid_reset()
    {
        var evidence = new DateTime(2026, 9, 6, 7, 35, 0, DateTimeKind.Utc);
        var absent = UsageLimitWallParser.Parse(
            evidence, "You've hit your session limit · try again later", "fable");
        var invalid = UsageLimitWallParser.Parse(
            evidence, "You've hit your session limit · resets 9amjunk (Europe/London)", "fable");

        absent!.Kind.ShouldBe(UsageLimitWallKind.ModelCap);
        absent.ResetParseFailure.ShouldBeNull();
        UsageLimitWallParser.FormatReason(absent).ShouldContain("no reset stated");
        UsageLimitWallParser.FormatReason(absent).ShouldNotContain("reset unparseable");

        invalid!.Kind.ShouldBe(UsageLimitWallKind.ModelCap);
        invalid.ResetParseFailure.ShouldNotBeNull();
        UsageLimitWallParser.FormatReason(invalid).ShouldContain("reset unparseable");
        UsageLimitWallParser.FormatReason(invalid).ShouldNotContain("no reset stated");
    }

    [Test]
    public void Card0412_V01_unknown_zone_does_not_silently_use_utc()
    {
        var evidence = new DateTime(2026, 9, 6, 7, 35, 0, DateTimeKind.Utc);
        var wall = UsageLimitWallParser.Parse(evidence, "resets 9am (Mars/Olympus)", "fable");

        wall!.Kind.ShouldBe(UsageLimitWallKind.ModelCap);
        wall.ResetAt.ShouldBeNull();
        wall.ResetParseFailure.ShouldContain("Mars/Olympus");
        UsageLimitWallParser.FormatReason(wall).ShouldContain("Mars/Olympus");
    }
}
