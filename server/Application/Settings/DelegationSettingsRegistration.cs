using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

/// <summary>CARD-0824 S1c (T-D-2). The one registration path for <see cref="DelegationSettings"/>: Program and the tests call it.</summary>
public static class DelegationSettingsRegistration
{
    public static IServiceCollection AddDelegationSettings(this IServiceCollection services, IConfiguration configuration)
    {
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
    private const string Key = "WorkspaceSlotRoles";

    public ValidateOptionsResult Validate(string? name, DelegationSettings options)
    {
        var value = configuration.GetSection("Delegation").GetChildren()
            .FirstOrDefault(child => string.Equals(child.Key, Key, StringComparison.OrdinalIgnoreCase));
        if (value is null || (value.Value is not null && !value.GetChildren().Any()))
            return ValidateOptionsResult.Success;
        return ValidateOptionsResult.Fail(
            $"{WorkspaceSlotRoles.SettingName} must be a single string ('*', 'none' or 'Plan,Docs'), not an array or object.");
    }
}
