using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class WatchdogService
{
    private static readonly SessionStatus[] ActiveStatuses = [SessionStatus.Starting, SessionStatus.Running];

    private readonly AppDbContext _db;
    private readonly AgentSessionRuntime _runtime;
    private readonly WatchdogMatcher _matcher;
    private readonly WatchdogCooldownStore _cooldowns;
    private readonly IEventBus _eventBus;
    private readonly WatchdogSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WatchdogService> _logger;
    private readonly AgentTaskDispatcher? _dispatcher;

    public WatchdogService(
        AppDbContext db,
        AgentSessionRuntime runtime,
        WatchdogMatcher matcher,
        WatchdogCooldownStore cooldowns,
        IEventBus eventBus,
        IOptions<WatchdogSettings> settings,
        TimeProvider timeProvider,
        ILogger<WatchdogService> logger,
        AgentTaskDispatcher? dispatcher = null)
    {
        _db = db;
        _runtime = runtime;
        _matcher = matcher;
        _cooldowns = cooldowns;
        _eventBus = eventBus;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _dispatcher = dispatcher;
    }

    public async Task<int> ScanAsync(CancellationToken ct)
    {
        if (!_settings.Enabled)
            return 0;

        var liveSessionIds = _runtime.ListLiveSessions();
        if (liveSessionIds.Count == 0)
            return 0;

        var activeSessions = await _db.AgentSessions
            .AsNoTracking()
            .Where(s => liveSessionIds.Contains(s.Id) && ActiveStatuses.Contains(s.Status))
            .Select(s => new { s.Id, s.AgentKind })
            .ToListAsync(ct);

        var responded = 0;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var cooldown = TimeSpan.FromMilliseconds(Math.Max(0, _settings.CooldownMs));
        foreach (var session in activeSessions)
        {
            var sessionId = session.Id;
            if (!_runtime.TryGetLiveSnapshot(sessionId, out var snapshot))
                continue;

            if (string.IsNullOrWhiteSpace(snapshot.RenderedScreen)
                && _matcher.IsUnsafeRmApprovalCandidate(snapshot.Buffer))
                continue;

            // A numeric modal is never eligible for a generic yes/Enter rule found in earlier
            // screen text. Only the complete active Claude warning and an empty answer can act.
            if (_matcher.IsUnsafeRmApprovalCandidate(snapshot.RenderedScreen))
            {
                if (session.AgentKind != AgentKind.ClaudeCode
                    || _runtime.HasPendingTerminalInput(sessionId))
                    continue;

                if (!_matcher.IsActiveUnsafeRmApproval(snapshot.RenderedScreen))
                {
                    _cooldowns.HoldUnsafeAnswer(sessionId);
                    continue;
                }
                if (!_cooldowns.ConfirmUnsafeAnswerCleared(sessionId))
                    continue;

                _cooldowns.ClearActiveExcept(sessionId, WatchdogMatcher.UnsafeRmRefusalRule);
                if (!_cooldowns.TryRecord(sessionId, WatchdogMatcher.UnsafeRmRefusalRule, now, cooldown))
                    continue;

                var boundTaskIds = await _db.AgentTasks.AsNoTracking()
                    .Where(t => t.AgentSessionId == sessionId
                        && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working))
                    .Select(t => t.Id).Take(2).ToListAsync(ct);
                try
                {
                    // The measured Claude modal consumes one ASCII '2'. Enter would be a second
                    // input into the interrupted composer and must never be sent here.
                    await _runtime.SendModalKeyAsync(sessionId, "2", ct);
                    responded++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Watchdog could not select No in session {SessionId}", sessionId);
                    continue;
                }

                // The native interruption disarms the local-tool deadline. Once No was sent,
                // fail the still-bound task through the ordinary non-killing caller-note path.
                if (boundTaskIds.Count == 1 && _dispatcher is not null)
                    await _dispatcher.FailAutoRefusedUnsafeDeleteAsync(boundTaskIds[0], sessionId, ct);

                try
                {
                    await _eventBus.PublishToGroupAsync(AgentSessionGroups.Session(sessionId),
                        "WatchdogAutoResponded",
                        new { sessionId, ruleName = WatchdogMatcher.UnsafeRmRefusalRule }, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Watchdog refusal audit failed for session {SessionId}", sessionId);
                }
                continue;
            }

            var screen = string.IsNullOrWhiteSpace(snapshot.RenderedScreen)
                ? snapshot.Buffer
                : snapshot.RenderedScreen;
            var match = _matcher.Match(screen, _settings.Rules);
            if (match is null)
            {
                _cooldowns.ClearActive(sessionId);
                continue;
            }

            _cooldowns.ClearActiveExcept(sessionId, match.RuleName);
            if (!_cooldowns.TryRecord(sessionId, match.RuleName, now, cooldown))
                continue;

            try
            {
                await _runtime.SendInputAsync(sessionId, match.Response, ct);
                await _eventBus.PublishToGroupAsync(
                    AgentSessionGroups.Session(sessionId),
                    "WatchdogAutoResponded",
                    new { sessionId, ruleName = match.RuleName },
                    ct);
                responded++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Watchdog failed to respond to session {SessionId}", sessionId);
            }
        }

        return responded;
    }
}
