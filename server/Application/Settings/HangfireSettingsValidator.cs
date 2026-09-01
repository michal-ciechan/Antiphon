using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

public sealed class HangfireSettingsValidator : IValidateOptions<HangfireSettings>
{
    public ValidateOptionsResult Validate(string? name, HangfireSettings options)
    {
        if (options.HistoryRetentionDays <= 0)
            return ValidateOptionsResult.Fail("Hangfire:HistoryRetentionDays must be positive.");

        return ValidateOptionsResult.Success;
    }
}
