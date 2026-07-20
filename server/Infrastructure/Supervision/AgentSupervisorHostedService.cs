using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Supervision;

/// <summary>
/// Drives <see cref="AgentSupervisorService"/> on a fixed tick (same shape as the reconciliation
/// and orchestrator hosted services), plus a slow incident-retention pass every 6 hours.
/// </summary>
public sealed class AgentSupervisorHostedService : BackgroundService
{
    private static readonly TimeSpan PrunePeriod = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SupervisionSettings _settings;
    private readonly ILogger<AgentSupervisorHostedService> _logger;
    private DateTime _lastPruneUtc = DateTime.MinValue;

    public AgentSupervisorHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<SupervisionSettings> settings,
        ILogger<AgentSupervisorHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Agent supervision disabled by configuration");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _settings.TickSeconds)));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
                    await supervisor.TickAsync(stoppingToken);

                    if (DateTime.UtcNow - _lastPruneUtc >= PrunePeriod)
                    {
                        _lastPruneUtc = DateTime.UtcNow;
                        var pruned = await supervisor.PruneIncidentsAsync(stoppingToken);
                        if (pruned > 0)
                            _logger.LogInformation("Pruned {Count} agent incident(s) past retention", pruned);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Agent supervision tick failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
