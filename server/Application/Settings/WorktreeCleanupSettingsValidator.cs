using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

public sealed class WorktreeCleanupSettingsValidator : IValidateOptions<WorktreeCleanupSettings>
{
    public ValidateOptionsResult Validate(string? name, WorktreeCleanupSettings options) => ValidateOptionsResult.Success;
}
