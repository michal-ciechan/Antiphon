using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

public sealed class ContextCompactionSettingsValidator : IValidateOptions<ContextCompactionSettings>
{
    public ValidateOptionsResult Validate(string? name, ContextCompactionSettings options)
    {
        var failures = new List<string>();

        if (options.IdleMinutes <= 0)
            failures.Add("ContextCompaction:IdleMinutes must be positive.");
        if (options.ContextPercent is < 1 or > 100)
            failures.Add("ContextCompaction:ContextPercent must be between 1 and 100.");
        if (options.CooldownHours <= 0)
            failures.Add("ContextCompaction:CooldownHours must be positive.");
        if (options.BoundaryTimeoutMinutes <= 0)
            failures.Add("ContextCompaction:BoundaryTimeoutMinutes must be positive.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
