using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Orchestration;

/// <summary>
/// Boot reconciliation and periodic backstop for durable land requests (CARD-0331). Claims
/// nothing: a pending row whose id is not in this process's active set is re-enqueued.
/// Never waits on git — that is <see cref="AgentTaskLandHostedService"/>.
/// </summary>
public sealed class AgentTaskLandSweepHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentTaskLandQueue _queue;
    private readonly DelegationSettings _settings;
    private readonly ILogger<AgentTaskLandSweepHostedService> _logger;

    public AgentTaskLandSweepHostedService(
        IServiceScopeFactory scopeFactory,
        AgentTaskLandQueue queue,
        IOptions<DelegationSettings> settings,
        ILogger<AgentTaskLandSweepHostedService> logger)
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
            _logger.LogInformation("Delegation is disabled; the land sweep will not run.");
            return;
        }

        await SweepOnceAsync(stoppingToken);

        var interval = TimeSpan.FromSeconds(Math.Clamp(_settings.LandSweepSeconds, 1, 60));
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SweepOnceAsync(stoppingToken);
        }
    }

    private async Task SweepOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var lands = scope.ServiceProvider.GetRequiredService<AgentTaskLandService>();
            await lands.SweepAsync(stoppingToken);
            if (_queue.PendingCount > 0)
                _logger.LogDebug("Land sweep pending {Pending}", _queue.PendingCount);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Land sweep failed");
        }
    }
}
