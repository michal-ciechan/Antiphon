using Antiphon.Server.Application.Settings;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class CheckCompactionContinuationSettingsTests
{
    [Test]
    public void Only_zero_or_at_least_ten_minutes_validate()
    {
        var validator = new DelegationSettingsValidator();
        for (var minutes = 1; minutes <= 9; minutes++)
        {
            var rejected = validator.Validate(null, Settings(minutes));
            rejected.Succeeded.ShouldBeFalse();
        }

        validator.Validate(null, Settings(-1)).Succeeded.ShouldBeFalse();
        validator.Validate(null, Settings(0)).Succeeded.ShouldBeTrue();
        validator.Validate(null, Settings(10)).Succeeded.ShouldBeTrue();
        validator.Validate(null, Settings(11)).Succeeded.ShouldBeTrue();
    }

    private static DelegationSettings Settings(int minutes) =>
        new() { CheckCompactionContinuationWaitMinutes = minutes };
}
