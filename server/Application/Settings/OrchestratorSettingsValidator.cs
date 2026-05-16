using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

public sealed class OrchestratorSettingsValidator : IValidateOptions<OrchestratorSettings>
{
    public ValidateOptionsResult Validate(string? name, OrchestratorSettings options)
    {
        var failures = new List<string>();

        if (options.PollIntervalSeconds <= 0)
            failures.Add("Orchestrator:PollIntervalSeconds must be positive.");
        if (options.MaxDispatchesPerTick <= 0)
            failures.Add("Orchestrator:MaxDispatchesPerTick must be positive.");
        if (options.DefaultCols <= 0)
            failures.Add("Orchestrator:DefaultCols must be positive.");
        if (options.DefaultRows <= 0)
            failures.Add("Orchestrator:DefaultRows must be positive.");
        if (options.ContinuationDelayMs <= 0)
            failures.Add("Orchestrator:ContinuationDelayMs must be positive.");
        if (options.FailureBackoffBaseMs <= 0)
            failures.Add("Orchestrator:FailureBackoffBaseMs must be positive.");
        if (options.FailureBackoffMaxMs <= 0)
            failures.Add("Orchestrator:FailureBackoffMaxMs must be positive.");
        if (options.StartingSessionGraceSeconds <= 0)
            failures.Add("Orchestrator:StartingSessionGraceSeconds must be positive.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
