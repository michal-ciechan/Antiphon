using Antiphon.Server.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>
/// Production factory for <see cref="IZombieProcessCensus"/>.
/// </summary>
public static class ZombieCensusRegistration
{
    public static IServiceCollection AddProcessCensus(IServiceCollection services)
    {
        services.AddSingleton<IZombieProcessCensus, WindowsZombieProcessCensus>();
        return services;
    }
}
