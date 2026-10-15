using Antiphon.Server.Application.Services;
using Antiphon.Agents.Pty.Tests;
using System.Security.Cryptography;
using System.Text;
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
    public void V0160_cup_status_retains_remaining_and_reset()
    {
        const string raw = "Weekly\u001b[1;8Hlimit: 3% left\r\n"
            + "(resets\u001b[2;9H22:13\u001b[2;15Hon\u001b[2;18H24\u001b[2;21HAug)";
        AssertDatedStatus(raw, "cup");
        AssertDatedStatus("Weekly limit: 3% left\r\n(resets 22:13 on 24 Aug)", "plain");
    }

    [Test]
    public void V0160_cuf_status_retains_remaining_and_reset()
    {
        // Collect independent failures so the cursor witness cannot mask the vendor bar defect.
        Should.SatisfyAllConditions(
            () => AssertDatedStatus("Weekly\u001b[1Climit: 3% left\r\n"
                + "(resets\u001b[1C22:13\u001b[1Con\u001b[1C24\u001b[1CAug)", "cuf"),
            () =>
            {
                var panel = ReadVendorStatusPanel();
                var parsed = SubscriptionUsageParser.Parse(AgentKind.Codex, panel, Clock(), TimeZoneInfo.Utc);
                parsed.Status.ShouldBe(SubscriptionUsageParseStatus.Parsed, "C907-vendor-panel-parsed");
                parsed.RemainingPercent.ShouldBe(70, "C907-vendor-panel-remaining");
                parsed.ResetsAt.ShouldBeNull("C907-vendor-panel-time-only-reset");
                parsed.RawExcerpt.Length.ShouldBeLessThanOrEqualTo(SubscriptionUsageParser.RawExcerptCapChars,
                    "C907-vendor-panel-excerpt-cap");
                parsed.RawExcerpt.ShouldNotContain("#", "C907-vendor-panel-header-excluded");
            },
            () =>
            {
                string[] invalid = ["three", "1..3", "[] 3", "[arbitrary text] 3",
                    "[██\n░░] 3", "[" + new string('█', 81) + "] 3"];
                foreach (var value in invalid)
                    SubscriptionUsageParser.Parse(AgentKind.Codex, "Weekly limit: " + value + "% left", Clock(), TimeZoneInfo.Utc)
                        .Status.ShouldBe(SubscriptionUsageParseStatus.Unparsed, "C907-invalid-number-or-bar-" + value);
                var malformed = SubscriptionUsageParser.Parse(AgentKind.Codex,
                    "Weekly limit: [██░░] 3% left (resets 99:99 on 99 Foo)", Clock(), TimeZoneInfo.Utc);
                malformed.Status.ShouldBe(SubscriptionUsageParseStatus.Parsed, "C907-malformed-date-percentage");
                malformed.ResetsAt.ShouldBeNull("C907-malformed-date-reset");
            });
    }

    private static FakeTimeProvider Clock() =>
        new(new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));

    private static void AssertDatedStatus(string raw, string label)
    {
        var parsed = SubscriptionUsageParser.Parse(AgentKind.Codex, raw, Clock(), TimeZoneInfo.Utc);
        parsed.Status.ShouldBe(SubscriptionUsageParseStatus.Parsed, "C907-" + label + "-status-parsed");
        parsed.RemainingPercent.ShouldBe(3, "C907-" + label + "-remaining");
        parsed.ResetsAtRaw.ShouldBe("22:13 on 24 Aug", "C907-" + label + "-reset-text");
        parsed.ResetsAt.ShouldBe(new DateTime(2026, 8, 24, 22, 13, 0, DateTimeKind.Utc),
            "C907-" + label + "-reset-utc");
    }

    private static string ReadVendorStatusPanel()
    {
        var fixture = File.ReadAllText(Path.Combine(CodexStartupFixtures.DirectoryPath, "v0160-status-panel.txt"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var parts = fixture.Split("--- payload ---\n", 2, StringSplitOptions.None);
        parts.Length.ShouldBe(2, "C907-setup-vendor-panel-envelope");
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parts[1]))).ToLowerInvariant();
        parts[0].ShouldContain("# payload-sha256: " + digest, "C907-setup-vendor-panel-digest");
        return parts[1];
    }

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
