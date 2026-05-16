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

    public WatchdogService(
        AppDbContext db,
        AgentSessionRuntime runtime,
        WatchdogMatcher matcher,
        WatchdogCooldownStore cooldowns,
        IEventBus eventBus,
        IOptions<WatchdogSettings> settings,
        TimeProvider timeProvider,
        ILogger<WatchdogService> logger)
    {
        _db = db;
        _runtime = runtime;
        _matcher = matcher;
        _cooldowns = cooldowns;
        _eventBus = eventBus;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<int> ScanAsync(CancellationToken ct)
    {
        if (!_settings.Enabled)
            return 0;

        var liveSessionIds = _runtime.ListLiveSessions();
        if (liveSessionIds.Count == 0)
            return 0;

        var activeSessionIds = await _db.AgentSessions
            .AsNoTracking()
            .Where(s => liveSessionIds.Contains(s.Id) && ActiveStatuses.Contains(s.Status))
            .Select(s => s.Id)
            .ToListAsync(ct);

        var responded = 0;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var cooldown = TimeSpan.FromMilliseconds(Math.Max(0, _settings.CooldownMs));
        foreach (var sessionId in activeSessionIds)
        {
            if (!_runtime.TryGetLiveSnapshot(sessionId, out var snapshot))
                continue;

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
