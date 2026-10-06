using Antiphon.Server.Application.Settings;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-1079 V-5. Attention defaults validate, and a bad bound names its setting.</summary>
[Category("Unit")]
public sealed class AttentionSettingsValidatorTests
{
    [Test]
    public void C1079_Defaults_pass_and_error_not_above_warning_fails()
    {
        var settings = new AttentionSettings();
        settings.SeatWatchEnabled.ShouldBeTrue();
        settings.SeatIdleWarningMinutes.ShouldBe(30);
        settings.SeatIdleErrorMinutes.ShouldBe(180);
        settings.OccupancySampleIntervalSeconds.ShouldBe(60);
        settings.OccupancySampleRetentionDays.ShouldBe(14);
        settings.InventoryTimeoutMs.ShouldBe(3000);
        new AttentionSettingsValidator().Validate(null, settings).Succeeded.ShouldBeTrue();

        AssertNamed(new AttentionSettings { SeatIdleErrorMinutes = 30 }, "Attention:SeatIdleErrorMinutes");
        AssertNamed(new AttentionSettings { SeatIdleErrorMinutes = 29 }, "Attention:SeatIdleErrorMinutes");
        AssertNamed(new AttentionSettings { SeatIdleWarningMinutes = 0 }, "Attention:SeatIdleWarningMinutes");
    }

    [Test]
    public void C1079_Interval_retention_and_timeout_bounds_are_enforced()
    {
        AssertNamed(new AttentionSettings { OccupancySampleIntervalSeconds = 9 }, "Attention:OccupancySampleIntervalSeconds");
        AssertNamed(new AttentionSettings { OccupancySampleIntervalSeconds = 3601 }, "Attention:OccupancySampleIntervalSeconds");
        AssertNamed(new AttentionSettings { OccupancySampleRetentionDays = 0 }, "Attention:OccupancySampleRetentionDays");
        AssertNamed(new AttentionSettings { OccupancySampleRetentionDays = 366 }, "Attention:OccupancySampleRetentionDays");
        AssertNamed(new AttentionSettings { InventoryTimeoutMs = 0 }, "Attention:InventoryTimeoutMs");

        var validator = new AttentionSettingsValidator();
        validator.Validate(null, new AttentionSettings { OccupancySampleIntervalSeconds = 10 }).Succeeded.ShouldBeTrue();
        validator.Validate(null, new AttentionSettings { OccupancySampleIntervalSeconds = 3600 }).Succeeded.ShouldBeTrue();
        validator.Validate(null, new AttentionSettings { OccupancySampleRetentionDays = 1 }).Succeeded.ShouldBeTrue();
        validator.Validate(null, new AttentionSettings { OccupancySampleRetentionDays = 365 }).Succeeded.ShouldBeTrue();
        validator.Validate(null, new AttentionSettings { InventoryTimeoutMs = 1 }).Succeeded.ShouldBeTrue();
    }

    private static void AssertNamed(AttentionSettings settings, string key)
    {
        var result = new AttentionSettingsValidator().Validate(null, settings);
        result.Failed.ShouldBeTrue();
        result.Failures.ShouldNotBeNull();
        result.Failures!.Count().ShouldBe(1);
        result.Failures.Single().ShouldContain(key);
    }
}
