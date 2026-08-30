using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Supervision;

/// <summary>
/// CARD-0245 S1: on startup and a short period, read the observer document and raise
/// Critical attention. Detection only — this hosted service never restarts AppHost.
/// </summary>
public sealed class AppHostWatchdogStateHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AppHostWatchdogStateSettings _settings;
    private readonly ILogger<AppHostWatchdogStateHostedService> _logger;

    public AppHostWatchdogStateHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<SupervisionSettings> settings,
        ILogger<AppHostWatchdogStateHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value.AppHostWatchdogState;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("AppHost watchdog-state reader disabled by configuration");
            return;
        }

        await TickOnceAsync(stoppingToken);

        var period = TimeSpan.FromSeconds(Math.Max(5, _settings.PollSeconds));
        using var timer = new PeriodicTimer(period);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await TickOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task TickOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var reader = scope.ServiceProvider.GetRequiredService<AppHostWatchdogStateAttentionService>();
            var raised = await reader.TickAsync(ct);
            if (raised > 0)
            {
                _logger.LogWarning(
                    "Raised {Count} AppHostWatchdogDisabled incident(s) (detection only)",
                    raised);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "AppHost watchdog-state reader failed; continues");
        }
    }
}
