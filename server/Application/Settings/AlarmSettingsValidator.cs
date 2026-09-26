using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

public sealed class AlarmSettingsValidator : IValidateOptions<AlarmSettings>
{
    public ValidateOptionsResult Validate(string? name, AlarmSettings options)
    {
        var failures = new List<string>();
        if (options.RunnerGraceSeconds <= 0)
            failures.Add("Alarms:RunnerGraceSeconds must be positive.");
        if (options.SweepMinutes <= 0)
            failures.Add("Alarms:SweepMinutes must be positive.");
        if (options.JournalStaleMinutes <= 0)
            failures.Add("Alarms:JournalStaleMinutes must be positive.");
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
