using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Configuration;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0552 V-552-76..78. The ceilings are the whole throttle, so a value that cannot be parsed
/// must refuse boot rather than silently run the sweep unthrottled.
/// </summary>
[Category("Unit")]
public sealed class MutationAutoDispatchSettingsTests
{
    [Test]
    public void C552_C01_Defaults()
    {
        var settings = new DelegationSettings().MutationAutoDispatch;
        settings.Enabled.ShouldBeTrue();
        settings.SweepMinutes.ShouldBe(5);
        settings.DailyBudgetUsd.ShouldBe(75m);
        settings.ExpectedMinutes.ShouldBe(720);
        settings.ActiveWindow.ShouldBeNull();
        settings.TimeZoneId.ShouldBeNull();
    }

    [Test]
    public void C552_C02_BindsFromConfiguration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Delegation:MutationAutoDispatch:DailyBudgetUsd"] = "10",
            ["Delegation:MutationAutoDispatch:Enabled"] = "false",
            ["Delegation:MutationAutoDispatch:ActiveWindow"] = "22:00-06:00",
            ["Delegation:MutationAutoDispatch:TimeZoneId"] = "Europe/London",
        }).Build();

        var settings = new DelegationSettings();
        configuration.GetSection("Delegation").Bind(settings);

        settings.MutationAutoDispatch.DailyBudgetUsd.ShouldBe(10m);
        settings.MutationAutoDispatch.Enabled.ShouldBeFalse();
        settings.MutationAutoDispatch.ActiveWindow.ShouldBe("22:00-06:00");
        settings.MutationAutoDispatch.TimeZoneId.ShouldBe("Europe/London");
        settings.MutationAutoDispatch.SweepMinutes.ShouldBe(5);
    }

    [Test]
    [Arguments("DailyBudgetUsd", "-1")]
    [Arguments("SweepMinutes", "0")]
    [Arguments("ExpectedMinutes", "0")]
    [Arguments("ActiveWindow", "25:00-06:00")]
    [Arguments("ActiveWindow", "0600-2200")]
    [Arguments("TimeZoneId", "Mars/Olympus")]
    public void C552_C03_ValidatorRefusesBadValues(string field, string value)
    {
        var settings = new DelegationSettings();
        var sweep = settings.MutationAutoDispatch;
        switch (field)
        {
            case "DailyBudgetUsd": sweep.DailyBudgetUsd = decimal.Parse(value); break;
            case "SweepMinutes": sweep.SweepMinutes = int.Parse(value); break;
            case "ExpectedMinutes": sweep.ExpectedMinutes = int.Parse(value); break;
            case "ActiveWindow": sweep.ActiveWindow = value; break;
            case "TimeZoneId": sweep.ActiveWindow = "22:00-06:00"; sweep.TimeZoneId = value; break;
        }

        var result = new DelegationSettingsValidator().Validate(null, settings);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Delegation:MutationAutoDispatch:" + field);
    }

    [Test]
    public void C552_C04_DefaultsValidate() =>
        new DelegationSettingsValidator().Validate(null, new DelegationSettings()).Failed.ShouldBeFalse();
}
