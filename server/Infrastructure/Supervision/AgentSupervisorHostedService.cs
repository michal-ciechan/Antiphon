using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Supervision;

/// <summary>
/// Drives <see cref="AgentSupervisorService"/> on a fixed tick (same shape as the reconciliation
/// and orchestrator hosted services), plus a slow incident-retention pass every 6 hours, the
/// channel-reply correlation sweep every minute, the idle auto-compact sweep every minute,
/// the API-error recovery sweep every minute, and the orchestrator-investigation detection
/// sweep every minute (CARD-0247).
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

    /// <summary>
    /// CARD-0082: how often the idle auto-compact sweep runs. An idle session never ends a turn
    /// to hook on, so only a global clock can notice idle ∧ full. Same shape as the CARD-0067
    /// channel-reply pass sitting next to it.
    /// </summary>
    private static readonly TimeSpan ContextCompactionSweepPeriod = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ChannelReplyDispatcher _channelReplies;
    private readonly ContextCompactionService _compaction;
    private readonly ApiErrorRecoveryService _apiErrorRecovery;
    private readonly HerdrStatusCorroborationService _herdrCorroboration;
    private readonly OrchestratorInvestigationSweepService _investigation;
    private readonly SupervisionSettings _settings;
    private readonly ILogger<AgentSupervisorHostedService> _logger;
    private DateTime _lastPruneUtc = DateTime.MinValue;
    private DateTime _lastChannelSweepUtc = DateTime.MinValue;
    private DateTime _lastCompactionSweepUtc = DateTime.MinValue;
    private DateTime _lastApiErrorRecoverySweepUtc = DateTime.MinValue;
    private DateTime _lastHerdrCorroborationSweepUtc = DateTime.MinValue;
    private DateTime _lastInvestigationSweepUtc = DateTime.MinValue;

    public AgentSupervisorHostedService(
        IServiceScopeFactory scopeFactory,
        ChannelReplyDispatcher channelReplies,
        ContextCompactionService compaction,
        ApiErrorRecoveryService apiErrorRecovery,
        HerdrStatusCorroborationService herdrCorroboration,
        OrchestratorInvestigationSweepService investigation,
        IOptions<SupervisionSettings> settings,
        ILogger<AgentSupervisorHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _channelReplies = channelReplies;
        _compaction = compaction;
        _apiErrorRecovery = apiErrorRecovery;
        _herdrCorroboration = herdrCorroboration;
        _investigation = investigation;
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

                    if (DateTime.UtcNow - _lastCompactionSweepUtc >= ContextCompactionSweepPeriod)
                    {
                        _lastCompactionSweepUtc = DateTime.UtcNow;
                        var compacted = await _compaction.SweepAsync(stoppingToken);
                        if (compacted > 0)
                        {
                            _logger.LogInformation(
                                "Enqueued idle auto-compact on {Count} session(s)", compacted);
                        }
                    }

                    var apiErrorPeriod = TimeSpan.FromSeconds(
                        Math.Max(1, _settings.ApiErrorRecovery.SweepPeriodSeconds));
                    if (_settings.ApiErrorRecovery.Enabled
                        && DateTime.UtcNow - _lastApiErrorRecoverySweepUtc >= apiErrorPeriod)
                    {
                        _lastApiErrorRecoverySweepUtc = DateTime.UtcNow;
                        var resumed = await _apiErrorRecovery.SweepAsync(stoppingToken);
                        if (resumed > 0)
                        {
                            _logger.LogInformation(
                                "Enqueued API-error resume on {Count} session(s)", resumed);
                        }
                    }

                    var herdrPeriod = TimeSpan.FromSeconds(
                        Math.Max(1, _settings.HerdrCorroboration.SweepPeriodSeconds));
                    if (_settings.HerdrCorroboration.Enabled
                        && DateTime.UtcNow - _lastHerdrCorroborationSweepUtc >= herdrPeriod)
                    {
                        _lastHerdrCorroborationSweepUtc = DateTime.UtcNow;
                        var disagreements = await _herdrCorroboration.SweepAsync(stoppingToken);
                        if (disagreements > 0)
                        {
                            _logger.LogWarning(
                                "Raised {Count} HerdrStatusDisagreement incident(s) (corroboration only)",
                                disagreements);
                        }
                    }

                    var investigationPeriod = TimeSpan.FromSeconds(
                        Math.Max(1, _settings.OrchestratorInvestigation.SweepPeriodSeconds));
                    if (_settings.OrchestratorInvestigation.Enabled
                        && DateTime.UtcNow - _lastInvestigationSweepUtc >= investigationPeriod)
                    {
                        _lastInvestigationSweepUtc = DateTime.UtcNow;
                        var investigations = await _investigation.SweepAsync(stoppingToken);
                        if (investigations > 0)
                        {
                            _logger.LogInformation(
                                "Raised {Count} OrchestratorInvestigation incident(s) (detection only)",
                                investigations);
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
