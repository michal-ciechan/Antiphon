using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Supervision;

/// <summary>
/// Drives <see cref="SessionHealthService"/> (RC watch + liveness probes) and the stranded-queue
/// watchdog (<see cref="SessionMessageQueueService.FlushStrandedQueuesAsync"/>) on its own cadence.
/// </summary>
public sealed class SessionHealthHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SessionMessageQueueService _queue;
    private readonly SupervisionSettings _settings;
    private readonly ILogger<SessionHealthHostedService> _logger;

    public SessionHealthHostedService(
        IServiceScopeFactory scopeFactory,
        SessionMessageQueueService queue,
        IOptions<SupervisionSettings> settings,
        ILogger<SessionHealthHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Session health watch disabled by configuration");
            return;
        }

        if (!_settings.RcWatch.Enabled && !_settings.RcModalWatch.Enabled)
        {
            _logger.LogInformation("RC connection watch and modal watch are both disabled");
            return;
        }

        var interval = Math.Max(10, Math.Min(
            _settings.RcWatch.Enabled ? _settings.RcWatch.ProbeIntervalSeconds : int.MaxValue,
            _settings.RcModalWatch.Enabled ? _settings.RcModalWatch.ProbeIntervalSeconds : int.MaxValue));
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (_settings.RcWatch.Enabled)
                {
                    try
                    {
                        await using var scope = _scopeFactory.CreateAsyncScope();
                        await scope.ServiceProvider.GetRequiredService<SessionHealthService>().TickAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Session health tick failed");
                    }
                }

                if (_settings.RcModalWatch.Enabled)
                {
                    try
                    {
                        await using var scope = _scopeFactory.CreateAsyncScope();
                        await scope.ServiceProvider.GetRequiredService<RemoteControlModalWatchService>().TickAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Remote-control modal watch tick failed");
                    }
                }

                try
                {
                    await _queue.FlushStrandedQueuesAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Stranded-queue watchdog pass failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
