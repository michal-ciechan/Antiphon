using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

public sealed class TrackerSettingsValidator : IValidateOptions<TrackerSettings>
{
    public ValidateOptionsResult Validate(string? name, TrackerSettings options)
    {
        if (options.CardStatePushTimeoutSeconds < 1)
        {
            return ValidateOptionsResult.Fail(
                "Tracker:CardStatePushTimeoutSeconds must be at least 1.");
        }

        return ValidateOptionsResult.Success;
    }
}
