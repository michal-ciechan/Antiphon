using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

public sealed class StaleWorkingTranscriptSettings
{
    public const string SectionName = "StaleWorkingTranscript";
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 120;
    public int PollSeconds { get; set; } = 15;
    public int ProbeTimeoutSeconds { get; set; } = 10;
    public int MaxConcurrentProbes { get; set; } = 4;
    public int PageSize { get; set; } = 128;
}

public sealed class StaleWorkingTranscriptSettingsValidator : IValidateOptions<StaleWorkingTranscriptSettings>
{
    public ValidateOptionsResult Validate(string? name, StaleWorkingTranscriptSettings settings)
    {
        var errors = new List<string>();
        if (settings.IntervalSeconds < 30 || settings.IntervalSeconds > 3600) errors.Add("StaleWorkingTranscript:IntervalSeconds must be 30..3600.");
        if (settings.PollSeconds < 1 || settings.PollSeconds > 30) errors.Add("StaleWorkingTranscript:PollSeconds must be 1..30.");
        if (settings.PollSeconds > settings.IntervalSeconds) errors.Add("StaleWorkingTranscript:PollSeconds must not exceed IntervalSeconds.");
        if (settings.ProbeTimeoutSeconds < 1 || settings.ProbeTimeoutSeconds > 30) errors.Add("StaleWorkingTranscript:ProbeTimeoutSeconds must be 1..30.");
        if (settings.MaxConcurrentProbes < 1 || settings.MaxConcurrentProbes > 4) errors.Add("StaleWorkingTranscript:MaxConcurrentProbes must be 1..4.");
        if (settings.PageSize < 1 || settings.PageSize > 128) errors.Add("StaleWorkingTranscript:PageSize must be 1..128.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
