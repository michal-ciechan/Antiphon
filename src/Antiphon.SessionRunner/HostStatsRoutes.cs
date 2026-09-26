using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0718. Registration and <c>/host-stats</c> routes, shared by <c>Program.cs</c> and the
/// loopback test host. Disabled answers 404. A bad metric or window is 400 before the store throws.
/// </summary>
public static class HostStatsRoutes
{
    public const string InvalidQueryCode = "invalid_host_stats_query";

    public static IServiceCollection AddHostStats(this IServiceCollection services, IConfiguration configuration, bool startSampler = true)
    {
        services.AddOptions<HostStatsSettings>()
            .Bind(configuration.GetSection(HostStatsSettings.SectionName))
            .PostConfigure(settings =>
            {
                if (settings.Volumes is { Length: 1 } && settings.Volumes[0].Contains(',', StringComparison.Ordinal))
                {
                    settings.Volumes = settings.Volumes[0]
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                }

                if (settings.Volumes is null || settings.Volumes.Length == 0)
                {
                    var log = configuration["SessionRunner:SessionLogPath"];
                    settings.Volumes = string.IsNullOrWhiteSpace(log)
                        ? [Directory.GetCurrentDirectory()]
                        : [Directory.GetCurrentDirectory(), log];
                }

                settings.Validate();
            })
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IProcessCpuProbe, SystemProcessCpuProbe>();
        services.TryAddSingleton<IHostStatsProbe>(sp =>
            new SystemHostStatsProbe(sp.GetRequiredService<IOptions<HostStatsSettings>>().Value));
        // Beats BuildSlotRoutes' TryAddSingleton either way, so the broker and the page share one probe.
        services.AddSingleton<IHostMemoryProbe>(sp => (IHostMemoryProbe)sp.GetRequiredService<IHostStatsProbe>());
        services.AddSingleton(sp => new HostStatsStore(
            sp.GetRequiredService<IOptions<HostStatsSettings>>().Value,
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<BuildSlotBroker>()));
        services.AddSingleton<IHostStatsSource>(sp => sp.GetRequiredService<HostStatsStore>());
        services.AddSingleton(sp => new HostStatsSamplerService(
            sp.GetRequiredService<IHostStatsProbe>(),
            sp.GetRequiredService<HostStatsStore>(),
            sp.GetRequiredService<IProcessCpuProbe>(),
            () => HostStatsSamplerService.ReadTargets(sp),
            HostStatsSamplerService.WorkingSet,
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IOptions<HostStatsSettings>>(),
            sp.GetRequiredService<ILogger<HostStatsSamplerService>>()));
        if (startSampler)
            services.AddHostedService(sp => sp.GetRequiredService<HostStatsSamplerService>());
        return services;
    }

    public static IEndpointRouteBuilder MapHostStatsRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGet("/host-stats", (HostStatsStore store, IOptions<HostStatsSettings> options, TimeProvider time) =>
        {
            if (!options.Value.Enabled)
                return Results.NotFound();
            return Results.Ok(store.Snapshot(time.GetUtcNow()));
        });

        app.MapGet("/host-stats/series", (
            string? metric,
            string? window,
            HostStatsStore store,
            IOptions<HostStatsSettings> options,
            TimeProvider time) =>
        {
            if (!options.Value.Enabled)
                return Results.NotFound();
            if (!HostStatsStore.KnownQuery(metric, window))
            {
                return Results.Problem(
                    title: InvalidQueryCode,
                    detail: "metric must be cpu, load, memory or tasks and window must be 1m, 5m, 15m or 30m",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Ok(new RunnerHostSeriesDto(
                metric!,
                window!,
                store.IntervalSeconds,
                store.Series(metric!, window!, time.GetUtcNow())));
        });
        return app;
    }

    /// <summary>Appends <c>hostStatsV1</c> only when sampling is enabled. Existing entries stay in order.</summary>
    public static IReadOnlyList<string> CapabilityFeatures(IReadOnlyList<string> features, HostStatsSettings settings)
    {
        if (!settings.Enabled)
            return features;
        return [.. features, RunnerCapabilityFeatures.HostStatsV1];
    }
}
