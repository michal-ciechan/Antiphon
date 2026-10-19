using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

public sealed class GitInspectionSettingsValidator : IValidateOptions<GitSettings>
{
    public ValidateOptionsResult Validate(string? name, GitSettings settings) =>
        settings.WorktreeBaseInspectionTimeoutSeconds > 0
        && settings.WorktreeBaseMaxCandidates > 0 && settings.WorktreeBaseMaxGitCommands > 0
        && settings.WorktreeBaseMaxInventoryRows > 0
        && settings.WorktreeBaseMaxInventoryRows >= settings.WorktreeBaseMaxCandidates
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Git inspection limits must be positive; inventory rows must be at least the candidate limit.");
}
