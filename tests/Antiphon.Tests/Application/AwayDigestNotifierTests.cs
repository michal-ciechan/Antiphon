using Antiphon.Server.Application.Settings;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public class AwayDigestNotifierTests
{
    [Test]
    public void Defaults_are_the_two_confirmed_london_send_times()
    {
        var settings = new DigestSettings();
        settings.TimeZone.ShouldBe("Europe/London");
        settings.SendTimesLocal.ShouldBe(["08:00", "18:00"]);
    }

    [Test]
    public void Invalid_timezone_fails_startup_validation()
    {
        new DigestSettingsValidator().Validate(null, new DigestSettings { TimeZone = "not/a-timezone" }).Succeeded.ShouldBeFalse();
    }
}
