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
            .Configure(settings => BindReplacingAllowlists(settings, configuration))
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

    /// <summary>
    /// The configuration binder appends indexed values onto an array that already holds the
    /// built-in defaults, so a narrower list cannot remove entries. Clear first, then restore
    /// each default only when that key was not configured.
    /// </summary>
    internal static void BindReplacingAllowlists(ResilienceSettings settings, IConfiguration configuration)
    {
        var section = configuration.GetSection(ResilienceSettings.SectionName);
        settings.Http ??= new ResilienceHttpSettings();
        settings.Database ??= new ResilienceDatabaseSettings();
        settings.Http.AllowedStatusCodes = [];
        settings.Http.AllowedSocketErrors = [];
        settings.Database.AllowedSqlStates = [];
        section.Bind(settings);
        if (!section.GetSection("Http:AllowedStatusCodes").Exists())
            settings.Http.AllowedStatusCodes = [.. ResilienceHttpSettings.DefaultStatusCodes];
        if (!section.GetSection("Http:AllowedSocketErrors").Exists())
            settings.Http.AllowedSocketErrors = [.. ResilienceHttpSettings.DefaultSocketErrors];
        if (!section.GetSection("Database:AllowedSqlStates").Exists())
            settings.Database.AllowedSqlStates = [.. ResilienceDatabaseSettings.DefaultSqlStates];
    }

    public static MeterProviderBuilder AddAntiphonResilienceMetrics(this MeterProviderBuilder builder) =>
        builder.AddMeter(ResilienceTelemetry.MeterName);
}
