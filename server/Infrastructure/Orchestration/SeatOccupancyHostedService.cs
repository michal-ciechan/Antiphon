using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Orchestration;

/// <summary>
/// CARD-1079: publishes one occupancy snapshot per interval. A failed tick is logged and the
/// previous snapshot stays. This service does not stop, kill, release or dispatch anything.
/// </summary>
public sealed class SeatOccupancyHostedService(
    IServiceScopeFactory scopes,
    IOptions<AttentionSettings> settings,
    TimeProvider clock,
    ILogger<SeatOccupancyHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Value.SeatWatchEnabled)
            return;

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(settings.Value.OccupancySampleIntervalSeconds), clock);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var sampler = scope.ServiceProvider.GetRequiredService<SeatOccupancySampler>();
                await sampler.SampleOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Seat occupancy sample failed");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
