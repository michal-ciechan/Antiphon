using Antiphon.Server.Application.Settings;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0672 D-8 (review 3488192e (3)): the land yield budget is validated with the other
/// Delegation options, 0 (disabled) through 600 seconds, and a failure names the key.
/// </summary>
[Category("Unit")]
public sealed class DelegationLeaseSettingsTests
{
    private const string Key = "Delegation:LandYieldToDispatchMaxSeconds";

    [Test]
    [Arguments(-1)]
    [Arguments(601)]
    [Arguments(int.MaxValue)]
    public void LandYieldToDispatchMaxSeconds_outside_zero_to_600_is_a_failure_naming_the_key(int seconds)
    {
        var result = new DelegationSettingsValidator().Validate(null, new DelegationSettings { LandYieldToDispatchMaxSeconds = seconds });

        result.Succeeded.ShouldBeFalse();
        result.Failures.ShouldNotBeNull().ShouldContain(f => f.Contains(Key));
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(90)]
    [Arguments(600)]
    public void LandYieldToDispatchMaxSeconds_from_zero_to_600_validates(int seconds)
    {
        var result = new DelegationSettingsValidator().Validate(null, new DelegationSettings { LandYieldToDispatchMaxSeconds = seconds });

        result.Succeeded.ShouldBeTrue(string.Join("; ", result.Failures ?? []));
    }

    [Test]
    public void The_default_budget_is_90_seconds()
    {
        new DelegationSettings().LandYieldToDispatchMaxSeconds.ShouldBe(90);
    }
}
