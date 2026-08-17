using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Supervision;

/// <summary>
/// Drives <see cref="AgentSupervisorService"/> on a fixed tick (same shape as the reconciliation
/// and orchestrator hosted services), plus a slow incident-retention pass every 6 hours and the
/// channel-reply correlation sweep every minute.
/// </summary>
public sealed class AgentSupervisorHostedService : BackgroundService
{
    private static readonly TimeSpan PrunePeriod = TimeSpan.FromHours(6);

    /// <summary>
    /// CARD-0067: how often the global channel-reply sweep runs. The per-session sweep only fires
    /// when that session ends another turn, and a session that answered a chat into a void may never
    /// end one — so a correlation abandoned unanswered must be reported on a clock nobody's turn owns.
    /// </summary>
    private static readonly TimeSpan ChannelReplySweepPeriod = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ChannelReplyDispatcher _channelReplies;
    private readonly SupervisionSettings _settings;
    private readonly ILogger<AgentSupervisorHostedService> _logger;
    private DateTime _lastPruneUtc = DateTime.MinValue;
    private DateTime _lastChannelSweepUtc = DateTime.MinValue;

    public AgentSupervisorHostedService(
        IServiceScopeFactory scopeFactory,
        ChannelReplyDispatcher channelReplies,
        IOptions<SupervisionSettings> settings,
        ILogger<AgentSupervisorHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _channelReplies = channelReplies;
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

                    if (DateTime.UtcNow - _lastChannelSweepUtc >= ChannelReplySweepPeriod)
                    {
                        _lastChannelSweepUtc = DateTime.UtcNow;
                        var abandoned = await _channelReplies.SweepStaleCorrelationsAsync(stoppingToken);
                        if (abandoned > 0)
                        {
                            _logger.LogWarning(
                                "Abandoned {Count} channel reply correlation(s) past their TTL; each is a "
                                + "ChannelReplyLost incident", abandoned);
                        }
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
