using Antiphon.Server.Application.Settings;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class OrchestratorSettingsTests
{
    [Test]
    public void OrchestratorSettingsValidator_rejects_non_positive_values()
    {
        var validator = new OrchestratorSettingsValidator();

        var result = validator.Validate(null, new OrchestratorSettings
        {
            PollIntervalSeconds = 0,
            MaxDispatchesPerTick = 0,
            DefaultCols = 0,
            DefaultRows = 0,
            ContinuationDelayMs = 0,
            FailureBackoffBaseMs = 0,
            FailureBackoffMaxMs = 0,
            StartingSessionGraceSeconds = 0
        });

        result.Failed.ShouldBeTrue();
        result.Failures.Count().ShouldBe(8);
    }
}
