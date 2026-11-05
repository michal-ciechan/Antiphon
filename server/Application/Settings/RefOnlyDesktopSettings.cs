using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Server.Application.Settings;

public sealed class RefOnlyDesktopSettings
{
    public const string SectionName = "Delegation:RefOnlyDesktop";
    public bool Enabled { get; set; }
    public string[] RunnerIds { get; set; } = [];
    public string Roles { get; set; } = "none";
}

public static class RefOnlyDesktopSettingsRegistration
{
    public static IServiceCollection AddRefOnlyDesktopSettings(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RefOnlyDesktopSettings>().Bind(configuration.GetSection(RefOnlyDesktopSettings.SectionName));
        return services;
    }
}
