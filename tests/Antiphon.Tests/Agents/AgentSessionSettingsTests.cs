using Antiphon.Server.Application.Settings;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Unit")]
public class AgentSessionSettingsTests
{
    [Test]
    public void AgentSessionSettingsValidator_rejects_non_positive_values()
    {
        var validator = new AgentSessionSettingsValidator();

        var result = validator.Validate(null, new AgentSessionSettings
        {
            SignalRMaxChunkChars = 0,
            ReplayBufferMaxChars = 0,
            SessionLogPath = "",
            FirstDeltaTimeoutMs = 0,
            KillGraceMs = 0,
            StallTimeoutMs = 0,
            StallScanIntervalMs = 0
        });

        result.Failed.ShouldBeTrue();
        result.Failures.Count().ShouldBe(7);
    }
}
