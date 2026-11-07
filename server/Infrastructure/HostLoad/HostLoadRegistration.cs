using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Antiphon.Server.Infrastructure.HostLoad;

public static class HostLoadRegistration
{
    public static IServiceCollection AddHostLoadHistory(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<HostLoadHistorySettings>().Bind(configuration.GetSection(HostLoadHistorySettings.SectionName))
            .PostConfigure(s => s.Validate()).ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<HostLoadCounterCache>(); services.AddSingleton<HostLoadShadowCache>();
        services.AddSingleton<HostLoadRecommendation>();
        services.AddScoped<IHostLoadCapacityReader, HostLoadCapacityReader>();
        services.AddScoped<HostLoadHistoryService>(); services.AddScoped<HostLoadIngestService>();
        services.AddScoped<HostLoadTaskWindowObserver>(); services.AddScoped<HostLoadDerivedService>();
        services.AddSingleton<HostLoadIngestPump>();
        services.AddHostedService(sp => sp.GetRequiredService<HostLoadIngestPump>());
        services.AddHostedService<HostLoadDerivedWorker>();
        return services;
    }
}
