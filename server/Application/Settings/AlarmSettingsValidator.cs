using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

public sealed class AlarmSettingsValidator : IValidateOptions<AlarmSettings>
{
    public ValidateOptionsResult Validate(string? name, AlarmSettings options) =>
        ValidateOptionsResult.Success;
}
