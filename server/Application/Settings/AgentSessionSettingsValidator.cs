using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

public sealed class AgentSessionSettingsValidator : IValidateOptions<AgentSessionSettings>
{
    public ValidateOptionsResult Validate(string? name, AgentSessionSettings options)
    {
        var failures = new List<string>();

        if (options.SignalRMaxChunkChars <= 0)
            failures.Add("AgentSessions:SignalRMaxChunkChars must be positive.");
        if (options.ReplayBufferMaxChars <= 0)
            failures.Add("AgentSessions:ReplayBufferMaxChars must be positive.");
        if (string.IsNullOrWhiteSpace(options.SessionLogPath))
            failures.Add("AgentSessions:SessionLogPath must not be empty.");
        if (options.FirstDeltaTimeoutMs <= 0)
            failures.Add("AgentSessions:FirstDeltaTimeoutMs must be positive.");
        if (options.KillGraceMs <= 0)
            failures.Add("AgentSessions:KillGraceMs must be positive.");
        if (options.StallTimeoutMs <= 0)
            failures.Add("AgentSessions:StallTimeoutMs must be positive.");
        if (options.StallScanIntervalMs <= 0)
            failures.Add("AgentSessions:StallScanIntervalMs must be positive.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
