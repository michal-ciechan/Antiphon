using Antiphon.Server.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

/// <summary>CARD-1704 S1 (T-D-7): <c>Landing:GitBackend</c>, in its own options type.</summary>
public sealed class LandGitBackendSettings
{
    public const string SectionName = "Landing";
    public const string Key = "GitBackend";
    public string GitBackend { get; set; } = LandGitBackendNames.Cli;
}

/// <summary>CARD-1704 S1 (T-D-8): the effective backend for one land operation scope.</summary>
public sealed class LandGitBackend(IOptionsMonitor<LandGitBackendSettings> monitor, ILogger<LandGitBackend> logger)
{
    public string Resolve() => monitor.CurrentValue.GitBackend;
}

public static class LandGitBackendSettingsRegistration
{
    public static IServiceCollection AddLandGitBackendSettings(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LandGitBackendSettings>();
        return services;
    }
}
