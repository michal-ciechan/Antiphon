using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

public sealed class WorkspaceDiskAlarmSettingsValidator : IValidateOptions<WorkspaceDiskAlarmSettings>
{
    public ValidateOptionsResult Validate(string? name, WorkspaceDiskAlarmSettings options)
    {
        // CARD-1677 S3 red shim: no bound is checked yet.
        return ValidateOptionsResult.Success;
    }
}
