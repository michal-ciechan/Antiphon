using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

public sealed class PhoneHomeRunnerSettingsValidator : IValidateOptions<PhoneHomeRunnerSettings>
{
    public ValidateOptionsResult Validate(string? name, PhoneHomeRunnerSettings options)
    {
        var failures = PhoneHomeRunnerSettingsRules.Validate(options);
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
