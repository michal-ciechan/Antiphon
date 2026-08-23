using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0136 S1: the pure threshold rule, the shared subscription key, and the settings defaults.
/// Snapshot tests hand-build <see cref="SubscriptionUsageSnapshot"/> so they never touch the
/// shared Postgres (the shared-Postgres rule).
/// </summary>
[Category("Unit")]
public sealed class SubscriptionQuotaGateTests
{
    private static readonly DateTime Now = new(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly SubscriptionQuotaGateSettings Defaults = new();

    [Test]
    public void Evaluate_passes_when_there_is_no_snapshot()
    {
        SubscriptionQuotaPolicy.Evaluate(null, Defaults, Now).ShouldBeNull();
    }

    [Test]
    public void Evaluate_passes_when_the_sample_is_older_than_MaxSampleAge()
    {
        var snapshot = Snap(
            remaining: 3,
            resetsAt: Now.AddHours(36),
            age: TimeSpan.FromMinutes(Defaults.MaxSampleAgeMinutes + 1));

        SubscriptionQuotaPolicy.Evaluate(snapshot, Defaults, Now).ShouldBeNull();
    }

    [Test]
    public void Evaluate_passes_when_ResetsAt_is_already_in_the_past()
    {
        var snapshot = Snap(
            remaining: 3,
            resetsAt: Now.AddHours(-1),
            age: TimeSpan.FromMinutes(5));

        SubscriptionQuotaPolicy.Evaluate(snapshot, Defaults, Now).ShouldBeNull();
    }

    [Test]
    public void Evaluate_trips_the_day_rule_at_10_percent_with_36h_left()
    {
        var snapshot = Snap(remaining: 10, resetsAt: Now.AddHours(36), age: TimeSpan.FromMinutes(5));

        var verdict = SubscriptionQuotaPolicy.Evaluate(snapshot, Defaults, Now);
        verdict.ShouldNotBeNull();
        verdict.RuleName.ShouldBe("low-with-a-day-left");
        verdict.RemainingPercent.ShouldBe(10);
        verdict.TimeToReset.ShouldBe(TimeSpan.FromHours(36));
    }

    [Test]
    public void Evaluate_does_not_trip_at_10_percent_with_6h_left()
    {
        var snapshot = Snap(remaining: 10, resetsAt: Now.AddHours(6), age: TimeSpan.FromMinutes(5));

        SubscriptionQuotaPolicy.Evaluate(snapshot, Defaults, Now).ShouldBeNull();
    }

    [Test]
    public void Evaluate_trips_the_hours_rule_at_5_percent_with_3h_left()
    {
        var snapshot = Snap(remaining: 5, resetsAt: Now.AddHours(3), age: TimeSpan.FromMinutes(5));

        var verdict = SubscriptionQuotaPolicy.Evaluate(snapshot, Defaults, Now);
        verdict.ShouldNotBeNull();
        verdict.RuleName.ShouldBe("critical-with-hours-left");
        verdict.RemainingPercent.ShouldBe(5);
    }

    [Test]
    public void Evaluate_does_not_trip_at_5_percent_with_1h_left()
    {
        var snapshot = Snap(remaining: 5, resetsAt: Now.AddHours(1), age: TimeSpan.FromMinutes(5));

        SubscriptionQuotaPolicy.Evaluate(snapshot, Defaults, Now).ShouldBeNull();
    }

    [Test]
    public void Evaluate_uses_the_assumed_week_when_ResetsAt_is_null()
    {
        var snapshot = Snap(remaining: 10, resetsAt: null, age: TimeSpan.FromMinutes(5));

        var verdict = SubscriptionQuotaPolicy.Evaluate(snapshot, Defaults, Now);
        verdict.ShouldNotBeNull();
        verdict.RuleName.ShouldBe("low-with-a-day-left");
        verdict.TimeToReset.ShouldBe(TimeSpan.FromMinutes(Defaults.AssumedMinutesToResetWhenUnknown));
    }

    [Test]
    public void Evaluate_is_inert_when_Enabled_is_false()
    {
        var snapshot = Snap(remaining: 3, resetsAt: Now.AddHours(36), age: TimeSpan.FromMinutes(5));
        var settings = new SubscriptionQuotaGateSettings { Enabled = false };

        SubscriptionQuotaPolicy.Evaluate(snapshot, settings, Now).ShouldBeNull();
    }

    [Test]
    public void KeyFor_and_SubscriptionUsageKey_agree_for_profile_and_profileless_agents()
    {
        var profileId = Guid.NewGuid();
        var withProfile = new Agent { TuiProfileId = profileId };
        var withoutProfile = new Agent { TuiProfileId = null };

        SubscriptionUsageKey.For(withProfile, AgentKind.Codex)
            .ShouldBe(profileId.ToString("D"));
        SubscriptionUsageMonitorService.KeyFor(withProfile, AgentKind.Codex)
            .ShouldBe(SubscriptionUsageKey.For(withProfile, AgentKind.Codex));

        SubscriptionUsageKey.For(withoutProfile, AgentKind.Codex).ShouldBe("Codex");
        SubscriptionUsageMonitorService.KeyFor(withoutProfile, AgentKind.Codex)
            .ShouldBe(SubscriptionUsageKey.For(withoutProfile, AgentKind.Codex));
        SubscriptionUsageKey.For(null, AgentKind.Grok).ShouldBe("Grok");
        SubscriptionUsageMonitorService.KeyFor(null, AgentKind.Grok)
            .ShouldBe(SubscriptionUsageKey.For(null, AgentKind.Grok));
    }

    [Test]
    public void defaults_match_the_plan()
    {
        Defaults.Enabled.ShouldBeTrue();
        Defaults.MaxSampleAgeMinutes.ShouldBe(180);
        Defaults.AssumedMinutesToResetWhenUnknown.ShouldBe(10_080);
        Defaults.Rules.Count.ShouldBe(2);
        Defaults.Rules[0].Name.ShouldBe("low-with-a-day-left");
        Defaults.Rules[0].MaxRemainingPercent.ShouldBe(10);
        Defaults.Rules[0].MinMinutesToReset.ShouldBe(1440);
        Defaults.Rules[1].Name.ShouldBe("critical-with-hours-left");
        Defaults.Rules[1].MaxRemainingPercent.ShouldBe(5);
        Defaults.Rules[1].MinMinutesToReset.ShouldBe(120);
    }

    [Test]
    public void validator_rejects_out_of_range_rules_and_negative_minutes()
    {
        var result = new SubscriptionQuotaGateSettingsValidator().Validate(null, new SubscriptionQuotaGateSettings
        {
            MaxSampleAgeMinutes = -1,
            AssumedMinutesToResetWhenUnknown = -5,
            Rules =
            [
                new() { Name = "bad-percent", MaxRemainingPercent = 101, MinMinutesToReset = 0 },
                new() { Name = "bad-minutes", MaxRemainingPercent = 10, MinMinutesToReset = -1 },
            ],
        });

        result.Failed.ShouldBeTrue();
        result.Failures.Count().ShouldBe(4);
    }

    [Test]
    public void validator_accepts_the_defaults()
    {
        new SubscriptionQuotaGateSettingsValidator()
            .Validate(null, new SubscriptionQuotaGateSettings())
            .Succeeded.ShouldBeTrue();
    }

    private static SubscriptionUsageSnapshot Snap(double remaining, DateTime? resetsAt, TimeSpan age) =>
        new(
            AgentKind.Codex,
            "test-key",
            "SuperPlan",
            remaining,
            resetsAt,
            ObservedAt: Now - age,
            Age: age);
}
