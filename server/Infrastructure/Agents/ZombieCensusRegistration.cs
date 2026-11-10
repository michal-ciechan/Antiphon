using Antiphon.Server.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>
/// Production factory for <see cref="IZombieProcessCensus"/>.
/// </summary>
public static class ZombieCensusRegistration
{
    public static IServiceCollection AddProcessCensus(IServiceCollection services) =>
        AddProcessCensus(services, OperatingSystem.IsWindows());

    public static IServiceCollection AddProcessCensus(IServiceCollection services, bool isWindows)
    {
        if (isWindows)
            services.AddSingleton<IZombieProcessCensus, WindowsZombieProcessCensus>();
        else
            services.AddSingleton<IZombieProcessCensus, ZombieCensusUnsupportedPlatform>();
        return services;
    }
}
