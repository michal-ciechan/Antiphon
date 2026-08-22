using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0143 S1 — parser against the literal measured Codex and Grok panels.</summary>
[Category("Unit")]
public sealed class SubscriptionUsageParserTests
{
    [Test]
    public void Parses_the_measured_Codex_panel()
    {
        var noisy =
            "\u001b[32mWeekly limit:         3% left\u001b[0m\n"
            + "                       (resets 22:13 on 24 Aug)";
        var parsed = SubscriptionUsageParser.Parse(
            AgentKind.Codex, noisy, TimeProvider.System, TimeZoneInfo.Utc);

        parsed.Status.ShouldBe(SubscriptionUsageParseStatus.Parsed);
        parsed.RemainingPercent.ShouldBe(3);
        parsed.ResetsAtRaw.ShouldBe("22:13 on 24 Aug");
        parsed.PlanLabel.ShouldBeNull();
    }

    [Test]
    public void Parses_the_measured_Grok_panel()
    {
        var text =
            """
            Weekly limit (SuperGrok)
            [progress bar]  1%
            Resets: August 28, 05:31
            """;
        var parsed = SubscriptionUsageParser.Parse(
            AgentKind.Grok, text, TimeProvider.System, TimeZoneInfo.Utc);

        parsed.Status.ShouldBe(SubscriptionUsageParseStatus.Parsed);
        parsed.PlanLabel.ShouldBe("SuperGrok");
        parsed.ResetsAtRaw.ShouldBe("August 28, 05:31");
        parsed.RemainingPercent.ShouldBe(1);
        SubscriptionUsageParser.PercentageIsRemaining(AgentKind.Grok)
            .ShouldBe(SubscriptionUsageParser.GrokPercentageIsRemaining);
    }

    [Test]
    public void A_reset_string_with_no_year_resolves_to_the_next_future_occurrence()
    {
        var tz = TimeZoneInfo.Utc;
        var before = new FakeTimeProvider(new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));
        var thisYear = SubscriptionUsageParser.InferResetUtc("22:13 on 24 Aug", before, tz);
        thisYear.ShouldBe(new DateTime(2026, 8, 24, 22, 13, 0, DateTimeKind.Utc));

        var after = new FakeTimeProvider(new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero));
        var nextYear = SubscriptionUsageParser.InferResetUtc("22:13 on 24 Aug", after, tz);
        nextYear.ShouldBe(new DateTime(2027, 8, 24, 22, 13, 0, DateTimeKind.Utc));

        SubscriptionUsageParser.InferResetUtc("not a date", before, tz).ShouldBeNull();

        var unparseablePanel =
            "Weekly limit:         3% left\n                       (resets 99:99 on 99 Foo)";
        var parsed = SubscriptionUsageParser.Parse(AgentKind.Codex, unparseablePanel, before, tz);
        parsed.Status.ShouldBe(SubscriptionUsageParseStatus.Parsed);
        parsed.RemainingPercent.ShouldBe(3);
        parsed.ResetsAtRaw.ShouldBe("99:99 on 99 Foo");
        parsed.ResetsAt.ShouldBeNull();
    }
}
