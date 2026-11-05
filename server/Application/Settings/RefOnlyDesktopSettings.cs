using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

public sealed class RefOnlyDesktopSettings
{
    public const string SectionName = "Delegation:RefOnlyDesktop";
    public bool Enabled { get; set; }
    public string[] RunnerIds { get; set; } = [];
    public string Roles { get; set; } = "none";
    internal List<string> ConfigurationErrors { get; } = [];
}

public static class RefOnlyDesktopSettingsRegistration
{
    public static IServiceCollection AddRefOnlyDesktopSettings(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(RefOnlyDesktopSettings.SectionName);
        services.AddSingleton<IOptionsChangeTokenSource<RefOnlyDesktopSettings>>(
            new ConfigurationChangeTokenSource<RefOnlyDesktopSettings>(Options.DefaultName, section));
        services.AddSingleton<IValidateOptions<RefOnlyDesktopSettings>, RefOnlyDesktopSettingsValidator>();
        services.AddOptions<RefOnlyDesktopSettings>().Configure(options => Read(section, options)).ValidateOnStart();
        return services;
    }

    // Parse before options validation so malformed scalar conversion is an OptionsValidationException,
    // including on reload. The normal binder's InvalidOperationException would escape the fail-closed gate.
    private static void Read(IConfigurationSection section, RefOnlyDesktopSettings options)
    {
        foreach (var child in section.GetChildren())
        {
            var key = RefOnlyDesktopSettings.SectionName + ":" + child.Key;
            switch (child.Key.ToLowerInvariant())
            {
                case "enabled":
                    if (child.GetChildren().Any() || !bool.TryParse(child.Value, out var enabled))
                        options.ConfigurationErrors.Add(key + " must be a boolean.");
                    else options.Enabled = enabled;
                    break;
                case "roles":
                    if (child.GetChildren().Any() || child.Value is null)
                        options.ConfigurationErrors.Add(key + " must be a single role-list string.");
                    else options.Roles = child.Value;
                    break;
                case "runnerids":
                    var entries = child.GetChildren().ToArray();
                    if (child.Value is not null
                        || entries.Where((entry, index) => entry.Key != index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            || entry.GetChildren().Any() || string.IsNullOrWhiteSpace(entry.Value)).Any())
                        options.ConfigurationErrors.Add(key + " must be an array of nonempty runner ids.");
                    else options.RunnerIds = entries.Select(entry => entry.Value!).ToArray();
                    break;
                default:
                    options.ConfigurationErrors.Add(key + " is not a recognized setting.");
                    break;
            }
        }
    }
}

public sealed class RefOnlyDesktopSettingsValidator : IValidateOptions<RefOnlyDesktopSettings>
{
    public ValidateOptionsResult Validate(string? name, RefOnlyDesktopSettings options)
    {
        var errors = options.ConfigurationErrors.ToList();
        if (!WorkspaceSlotRoles.TryParse(options.Roles, out _, out var roleError))
            errors.Add(roleError.Replace(WorkspaceSlotRoles.SettingName, RefOnlyDesktopSettings.SectionName + ":Roles", StringComparison.Ordinal));
        if (options.RunnerIds is null || options.RunnerIds.Any(id => string.IsNullOrWhiteSpace(id)
                || id.Length > 64 || id != id.Trim() || id.Any(char.IsControl))
            || options.RunnerIds.Distinct(StringComparer.Ordinal).Count() != options.RunnerIds.Length)
            errors.Add(RefOnlyDesktopSettings.SectionName + ":RunnerIds must contain distinct, exact runner ids of at most 64 characters.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
