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
        new AlarmSettingsValidator().Validate(null, settings).Succeeded.ShouldBeTrue();

        AssertNamed(new AlarmSettings { RunnerGraceSeconds = 0 }, "Alarms:RunnerGraceSeconds");
        AssertNamed(new AlarmSettings { SweepMinutes = 0 }, "Alarms:SweepMinutes");
        AssertNamed(new AlarmSettings { JournalStaleMinutes = -1 }, "Alarms:JournalStaleMinutes");
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
