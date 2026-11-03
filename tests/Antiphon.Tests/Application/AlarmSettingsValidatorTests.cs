using Antiphon.Server.Application.Settings;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0726 V-1. <c>Alarms</c> defaults validate; a non-positive key is named in the failure.</summary>
[Category("Unit")]
public sealed class AlarmSettingsValidatorTests
{
    [Test]
    public void defaults_validate_and_nonpositive_values_are_named()
    {
        var settings = new AlarmSettings();
        settings.Enabled.ShouldBeTrue();
        settings.JournalEnabled.ShouldBeTrue();
        settings.RunnerGraceSeconds.ShouldBe(180);
        settings.SweepMinutes.ShouldBe(15);
        settings.JournalStaleMinutes.ShouldBe(5);
        settings.RunnerDrainMaxMinutes.ShouldBe(30);
        new AlarmSettingsValidator().Validate(null, settings).Succeeded.ShouldBeTrue();

        AssertNamed(new AlarmSettings { RunnerGraceSeconds = 0 }, "Alarms:RunnerGraceSeconds");
        AssertNamed(new AlarmSettings { SweepMinutes = 0 }, "Alarms:SweepMinutes");
        AssertNamed(new AlarmSettings { JournalStaleMinutes = -1 }, "Alarms:JournalStaleMinutes");
        AssertNamed(new AlarmSettings { RunnerDrainMaxMinutes = 0 }, "Alarms:RunnerDrainMaxMinutes");
    }

    [Test]
    public void C1671_the_drain_bound_is_at_most_seven_days()
    {
        // CARD-1671 (a): a huge drain bound would switch the CARD-1666 drain alarm off.
        new AlarmSettingsValidator().Validate(null, new AlarmSettings { RunnerDrainMaxMinutes = 7 * 24 * 60 })
            .Succeeded.ShouldBeTrue();
        AssertNamed(new AlarmSettings { RunnerDrainMaxMinutes = 7 * 24 * 60 + 1 }, "Alarms:RunnerDrainMaxMinutes");
        AssertNamed(new AlarmSettings { RunnerDrainMaxMinutes = int.MaxValue }, "Alarms:RunnerDrainMaxMinutes");
    }

    private static void AssertNamed(AlarmSettings settings, string key)
    {
        var result = new AlarmSettingsValidator().Validate(null, settings);
        result.Failed.ShouldBeTrue();
        result.Failures.ShouldNotBeNull();
        result.Failures!.Count().ShouldBe(1);
        result.Failures.Single().ShouldContain(key);
    }
}
