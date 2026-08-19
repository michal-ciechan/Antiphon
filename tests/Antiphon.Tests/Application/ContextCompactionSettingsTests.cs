using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0082 S2 — global settings, validator, and per-agent override fall-through.</summary>
[Category("Unit")]
public class ContextCompactionSettingsTests
{
    [Test]
    public void defaults_match_the_plan()
    {
        var settings = new ContextCompactionSettings();

        settings.Enabled.ShouldBeTrue();
        settings.IdleMinutes.ShouldBe(480);
        settings.ContextPercent.ShouldBe(50);
        settings.CooldownHours.ShouldBe(24);
        settings.BoundaryTimeoutMinutes.ShouldBe(10);
    }

    [Test]
    public void validator_accepts_the_defaults()
    {
        var result = new ContextCompactionSettingsValidator().Validate(null, new ContextCompactionSettings());

        result.Succeeded.ShouldBeTrue();
    }

    [Test]
    public void validator_rejects_non_positive_and_out_of_range_values()
    {
        var result = new ContextCompactionSettingsValidator().Validate(null, new ContextCompactionSettings
        {
            IdleMinutes = 0,
            ContextPercent = 0,
            CooldownHours = 0,
            BoundaryTimeoutMinutes = 0,
        });

        result.Failed.ShouldBeTrue();
        result.Failures.Count().ShouldBe(4);
    }

    [Test]
    public void null_on_the_agent_columns_falls_through_to_the_global_settings()
    {
        var settings = new ContextCompactionSettings
        {
            Enabled = true,
            IdleMinutes = 480,
            ContextPercent = 50,
        };
        var agent = new Agent();

        agent.AutoCompactEnabled.ShouldBeNull();
        agent.AutoCompactIdleMinutes.ShouldBeNull();
        agent.AutoCompactContextPercent.ShouldBeNull();

        var resolved = ContextCompaction.Resolve(settings, agent);

        resolved.Enabled.ShouldBeTrue();
        resolved.IdleMinutes.ShouldBe(480);
        resolved.ContextPercent.ShouldBe(50);
    }

    [Test]
    public void a_non_null_agent_override_wins()
    {
        var settings = new ContextCompactionSettings();
        var agent = new Agent
        {
            AutoCompactEnabled = false,
            AutoCompactIdleMinutes = 60,
            AutoCompactContextPercent = 80,
        };

        var resolved = ContextCompaction.Resolve(settings, agent);

        resolved.Enabled.ShouldBeFalse();
        resolved.IdleMinutes.ShouldBe(60);
        resolved.ContextPercent.ShouldBe(80);
    }

    [Test]
    public void mixed_overrides_fall_through_per_field()
    {
        var settings = new ContextCompactionSettings();
        var agent = new Agent { AutoCompactEnabled = false };

        var resolved = ContextCompaction.Resolve(settings, agent);

        resolved.Enabled.ShouldBeFalse();
        resolved.IdleMinutes.ShouldBe(480);
        resolved.ContextPercent.ShouldBe(50);
    }
}
