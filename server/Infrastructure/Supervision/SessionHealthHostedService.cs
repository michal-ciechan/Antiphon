using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Supervision;

/// <summary>Drives <see cref="SessionHealthService"/> (RC watch + liveness probes) on its own cadence.</summary>
public sealed class SessionHealthHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SupervisionSettings _settings;
    private readonly ILogger<SessionHealthHostedService> _logger;

    public SessionHealthHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<SupervisionSettings> settings,
        ILogger<SessionHealthHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled || (!_settings.RcWatch.Enabled && !_settings.LivenessProbe.Enabled))
        {
            _logger.LogInformation("Session health watch disabled by configuration");
            return;
        }

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(Math.Max(10, _settings.RcWatch.ProbeIntervalSeconds)));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
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
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
