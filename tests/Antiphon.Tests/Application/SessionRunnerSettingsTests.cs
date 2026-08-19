using Antiphon.Server.Application.Settings;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class SessionRunnerSettingsTests
{
    [Test]
    public void BaseUrl_defaults_to_canonical_17204()
    {
        new SessionRunnerSettings().BaseUrl.ShouldBe("http://localhost:17204");
    }
}
