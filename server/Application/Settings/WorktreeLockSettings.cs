using Microsoft.Extensions.Options;
namespace Antiphon.Server.Application.Settings;

public sealed class WorktreeLockSettings
{
    public string? HandleExecutablePath { get; set; }
}

public sealed class WorktreeLockSettingsValidator : IValidateOptions<WorktreeLockSettings>
{
    public ValidateOptionsResult Validate(string? name, WorktreeLockSettings options) =>
        string.IsNullOrEmpty(options.HandleExecutablePath) || Path.IsPathFullyQualified(options.HandleExecutablePath)
            ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail("HandleExecutablePath must be an absolute trusted executable path.");
}
