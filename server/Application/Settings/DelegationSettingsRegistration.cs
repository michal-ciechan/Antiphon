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
        services.AddOptions<DelegationSettings>()
            .Bind(configuration.GetSection("Delegation"))
            .ValidateOnStart();
        return services;
    }
}
