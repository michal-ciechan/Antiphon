using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

/// <summary>CARD-0824 S1c (T-D-2). The one registration path for <see cref="DelegationSettings"/>: Program and the tests call it.</summary>
public static class DelegationSettingsRegistration
{
    public static IServiceCollection AddDelegationSettings(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRefOnlyDesktopSettings(configuration);
        services.AddSingleton<IValidateOptions<DelegationSettings>, DelegationSettingsValidator>();
        services.AddSingleton<IValidateOptions<DelegationSettings>>(new DelegationSettingsShapeValidator(configuration));
        services.AddOptions<DelegationSettings>()
            .Bind(configuration.GetSection("Delegation"))
            .ValidateOnStart();
        return services;
    }
}

/// <summary>
/// CARD-0824 S1c (T-D-2). <c>Delegation:WorkspaceSlotRoles</c> is a single string. The binder silently ignores an array or
/// object written there and keeps the default <c>*</c>, so <c>[]</c> would turn the soft rollback into every role. This
/// refuses the key when it is present with children or with no value. It enumerates the children of <c>Delegation</c>:
/// <c>GetSection(...).Exists()</c> reports an empty array as absent.
/// </summary>
public sealed class DelegationSettingsShapeValidator(IConfiguration configuration) : IValidateOptions<DelegationSettings>
{
    public ValidateOptionsResult Validate(string? name, DelegationSettings options)
    {
        var children = configuration.GetSection("Delegation").GetChildren().ToList();
        var errors = new List<string>();
        if (ScalarRefused(children, "WorkspaceSlotRoles"))
            errors.Add($"{WorkspaceSlotRoles.SettingName} must be a single string ('*', 'none' or 'Plan,Docs'), not an array or object.");
        if (ScalarRefused(children, "WindowsSlotRoles"))
            errors.Add("Delegation:WindowsSlotRoles must be a single string ('*', 'none' or 'Plan,Docs'), not an array or object.");
        if (IdArrayRefused(children, "WindowsSlotRunnerIds"))
            errors.Add("Delegation:WindowsSlotRunnerIds must be an array of nonempty runner ids.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static bool ScalarRefused(List<IConfigurationSection> children, string key)
    {
        var value = children.FirstOrDefault(child => string.Equals(child.Key, key, StringComparison.OrdinalIgnoreCase));
        return value is not null && !(value.Value is not null && !value.GetChildren().Any());
    }

    private static bool IdArrayRefused(List<IConfigurationSection> children, string key)
    {
        var value = children.FirstOrDefault(child => string.Equals(child.Key, key, StringComparison.OrdinalIgnoreCase));
        if (value is null)
            return false;
        var entries = value.GetChildren().ToArray();
        return value.Value is not null
            || entries.Where((entry, index) => entry.Key != index.ToString(CultureInfo.InvariantCulture)
                || entry.GetChildren().Any() || string.IsNullOrWhiteSpace(entry.Value)).Any();
    }
}
