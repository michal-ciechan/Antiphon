using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;

namespace Antiphon.Resilience;

public interface IResilienceJitter
{
    double Next();
}

public sealed class SystemResilienceJitter : IResilienceJitter
{
    public double Next() => Random.Shared.NextDouble();
}

public sealed class FixedResilienceJitter(double sample) : IResilienceJitter
{
    public double Next() => sample;
}

public static class ResilienceRegistration
{
    public static IServiceCollection AddAntiphonResilience(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ResilienceSettings>()
            .Bind(configuration.GetSection(ResilienceSettings.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ResilienceSettings>, ResilienceSettingsValidator>());
        services.TryAddSingleton<ResilienceTelemetry>();
        services.TryAddSingleton<IResilienceJitter, SystemResilienceJitter>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<ResiliencePipelineCache>();
        services.TryAddSingleton<DatabaseAttemptExecutor>();

        foreach (var clientName in ResilienceClientNames.All)
        {
            var dependency = ResilienceDependencies.ForClient(clientName);
            services.AddHttpClient(clientName)
                .ConfigureHttpClient((sp, client) =>
                {
                    if (sp.GetRequiredService<IOptions<ResilienceSettings>>().Value.Enabled)
                        client.Timeout = Timeout.InfiniteTimeSpan;
                })
                .AddHttpMessageHandler(sp => new AdmittedReadHandler(
                    dependency,
                    sp.GetRequiredService<IOptionsMonitor<ResilienceSettings>>(),
                    sp.GetRequiredService<ResiliencePipelineCache>(),
                    sp.GetRequiredService<ResilienceTelemetry>(),
                    sp.GetRequiredService<TimeProvider>(),
                    sp.GetRequiredService<ILogger<AdmittedReadHandler>>()));
        }

        return services;
    }

    public static MeterProviderBuilder AddAntiphonResilienceMetrics(this MeterProviderBuilder builder) =>
        builder.AddMeter(ResilienceTelemetry.MeterName);
}
