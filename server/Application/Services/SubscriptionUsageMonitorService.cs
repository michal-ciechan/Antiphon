using System.Collections.Concurrent;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0143: idle poll of a provider's TUI usage panel. Singleton so the in-memory
/// per-session last-poll stamp and consecutive-failure counter survive the hosted
/// service's per-tick scope. Ships with <c>Enabled = false</c>; the sweep no-ops
/// unless that is flipped.
/// </summary>
public sealed class SubscriptionUsageMonitorService
{
    private readonly ConcurrentDictionary<Guid, DateTime> _lastPoll = new();
    private readonly ConcurrentDictionary<Guid, int> _failures = new();
    private readonly ConcurrentDictionary<Guid, byte> _incidentRaised = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SessionMessageQueueService _queue;
    private readonly AgentSessionRuntime _runtime;
    private readonly SubscriptionUsageMonitoringSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<SubscriptionUsageMonitorService> _logger;

    public SubscriptionUsageMonitorService(
        IServiceScopeFactory scopeFactory,
        SessionMessageQueueService queue,
        AgentSessionRuntime runtime,
        IOptions<SubscriptionUsageMonitoringSettings> settings,
        TimeProvider time,
        ILogger<SubscriptionUsageMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _runtime = runtime;
        _settings = settings.Value;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Kinds whose poll contract has an established command and whose state is Supported,
    /// or Degraded when <paramref name="includeDegraded"/> is true. Closed list so the
    /// sweep query can <c>IN</c>-filter at the database.
    /// </summary>
    internal static AgentKind[] EligibleKinds(bool includeDegraded) =>
        Enum.GetValues<AgentKind>().Where(k => IsEligible(k, includeDegraded)).ToArray();

    internal static bool IsEligible(AgentKind kind, bool includeDegraded)
    {
        if (!Enum.IsDefined(kind))
            return false;
        var poll = ProviderContractCatalog.For(kind).SubscriptionUsagePoll;
        if (string.IsNullOrEmpty(poll.Command))
            return false;
        return poll.State == AgentTuiCapabilityState.Supported
            || (includeDegraded && poll.State == AgentTuiCapabilityState.Degraded);
    }

    internal static IQueryable<AgentSession> WhereEligible(
        IQueryable<AgentSession> sessions, bool includeDegraded)
    {
        var eligible = EligibleKinds(includeDegraded);
        return sessions.Where(s => s.Status == SessionStatus.Running && eligible.Contains(s.AgentKind));
    }

    public async Task<int> SweepAsync(CancellationToken ct)
    {
        if (!_settings.Enabled)
            return 0;

        List<(Guid Id, AgentKind Kind)> sessions;
        Dictionary<Guid, Agent> owners;
        Dictionary<Guid, DateTime> newestSample;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await WhereEligible(db.AgentSessions.AsNoTracking(), _settings.IncludeDegradedProviders)
                .Select(s => new { s.Id, s.AgentKind })
                .ToListAsync(ct);
            sessions = rows.Select(s => (s.Id, s.AgentKind)).ToList();
            if (sessions.Count == 0)
                return 0;

            var idTexts = sessions.Select(s => s.Id.ToString("D")).ToList();
            var claimed = await db.Agents.AsNoTracking()
                .Where(a => a.PersistentSessionId != null && idTexts.Contains(a.PersistentSessionId))
                .ToListAsync(ct);
            owners = new Dictionary<Guid, Agent>();
            foreach (var agent in claimed)
            {
                if (Guid.TryParse(agent.PersistentSessionId, out var sid) && !owners.ContainsKey(sid))
                    owners[sid] = agent;
            }

            var sessionIds = sessions.Select(s => s.Id).ToList();
            newestSample = await db.SubscriptionUsageSamples.AsNoTracking()
                .Where(s => sessionIds.Contains(s.AgentSessionId))
                .GroupBy(s => s.AgentSessionId)
                .Select(g => new { SessionId = g.Key, At = g.Max(x => x.ObservedAt) })
                .ToDictionaryAsync(x => x.SessionId, x => x.At, ct);
        }

        var live = _runtime.ListLiveSessions();
        var now = UtcNow();
        var cooldown = TimeSpan.FromMinutes(Math.Max(1, _settings.MinPollIntervalMinutes));
        var candidates = new List<(Guid Id, AgentKind Kind)>();
        foreach (var (sessionId, kind) in sessions)
        {
            if (!live.Contains(sessionId))
            {
                _logger.LogDebug("Subscription usage sweep skipped session {SessionId}: not live", sessionId);
                continue;
            }

            if (RecentlyPolled(sessionId, now, cooldown, newestSample))
            {
                _logger.LogDebug("Subscription usage sweep skipped session {SessionId}: cooldown", sessionId);
                continue;
            }

            candidates.Add((sessionId, kind));
        }

        var cap = Math.Max(1, _settings.MaxSessionsPerSweep);
        var dropped = Math.Max(0, candidates.Count - cap);
        if (dropped > 0)
        {
            _logger.LogInformation(
                "Subscription usage sweep dropping {Dropped} session(s) this pass (cap {Cap})",
                dropped, cap);
            candidates = candidates.Take(cap).ToList();
        }

        var polled = 0;
        foreach (var (sessionId, kind) in candidates)
        {
            ct.ThrowIfCancellationRequested();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.PerSessionTimeoutSeconds)));
            try
            {
                if (await PollSessionAsync(sessionId, kind, owners.GetValueOrDefault(sessionId), timeoutCts.Token))
                    polled++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Subscription usage poll timed out for session {SessionId}", sessionId);
                await NoteFailureAsync(sessionId, owners.GetValueOrDefault(sessionId), "timed out", ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Subscription usage sweep failed for session {SessionId}", sessionId);
                await NoteFailureAsync(sessionId, owners.GetValueOrDefault(sessionId), ex.Message, ct);
            }
        }

        return polled;
    }

    private async Task<bool> PollSessionAsync(
        Guid sessionId, AgentKind kind, Agent? owner, CancellationToken ct)
    {
        await _runtime.CatchUpTranscriptAsync(sessionId, ct);

        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.AgentSessions.AsNoTracking()
                .AnyAsync(s => s.Id == sessionId && s.Status == SessionStatus.Running, ct))
            {
                _logger.LogDebug("Subscription usage sweep skipped session {SessionId}: not Running", sessionId);
                return false;
            }

            if (await SessionMessageQueueService.IsWorkingAsync(db, sessionId, ct))
            {
                _logger.LogDebug("Subscription usage sweep skipped session {SessionId}: working", sessionId);
                return false;
            }

            if (await db.SessionQueuedMessages.AsNoTracking()
                .AnyAsync(m => m.AgentSessionId == sessionId && m.Status == QueuedMessageStatus.Pending, ct))
            {
                _logger.LogDebug("Subscription usage sweep skipped session {SessionId}: pending messages", sessionId);
                return false;
            }
        }

        var contract = ProviderContractCatalog.For(kind).SubscriptionUsagePoll;
        if (string.IsNullOrEmpty(contract.Command))
            return false;

        var poll = new LocalCommandPoll(
            kind,
            contract.Command,
            contract.Navigation,
            contract.OpensOverlay,
            _settings.OverlaySettleMs,
            _settings.PanelTimeoutSeconds);

        var result = await _queue.TryPollLocalCommandAsync(sessionId, poll, ct);
        switch (result)
        {
            case LocalCommandPollResult.Skipped(var reason):
                _logger.LogDebug(
                    "Subscription usage poll skipped session {SessionId}: {Reason}", sessionId, reason);
                return false;

            case LocalCommandPollResult.NotAccepted:
                _logger.LogDebug(
                    "Subscription usage poll withheld Enter for session {SessionId}: no composer evidence",
                    sessionId);
                await NoteFailureAsync(sessionId, owner, "no composer evidence", ct);
                return false;

            case LocalCommandPollResult.PanelNotRendered:
                _logger.LogDebug(
                    "Subscription usage poll got no panel for session {SessionId}", sessionId);
                await NoteFailureAsync(sessionId, owner, "panel did not render", ct);
                return false;

            case LocalCommandPollResult.Sent(var buffer):
                await StoreSampleAsync(sessionId, kind, owner, contract.Command, buffer, ct);
                return true;

            default:
                return false;
        }
    }

    private async Task StoreSampleAsync(
        Guid sessionId, AgentKind kind, Agent? owner, string command, string buffer, CancellationToken ct)
    {
        var now = UtcNow();
        Stamp(sessionId, now);

        var parsed = SubscriptionUsageParser.Parse(kind, buffer, _time);
        var sample = new SubscriptionUsageSample
        {
            Id = Guid.NewGuid(),
            Provider = kind,
            SubscriptionKey = KeyFor(owner, kind),
            PlanLabel = parsed.PlanLabel,
            RemainingPercent = parsed.RemainingPercent,
            ResetsAt = parsed.ResetsAt,
            ResetsAtRaw = parsed.ResetsAtRaw,
            ObservedAt = now,
            AgentId = owner?.Id,
            AgentSessionId = sessionId,
            SourceCommand = command,
            ParseStatus = parsed.Status,
            RawExcerpt = parsed.RawExcerpt,
        };

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.SubscriptionUsageSamples.Add(sample);
        await db.SaveChangesAsync(ct);

        if (parsed.Status == SubscriptionUsageParseStatus.Parsed)
            NoteSuccess(sessionId);
        else
            await NoteFailureAsync(sessionId, owner, "unparsed panel", ct);
    }

    private bool RecentlyPolled(
        Guid sessionId, DateTime now, TimeSpan cooldown, Dictionary<Guid, DateTime> newestSample)
    {
        if (_lastPoll.TryGetValue(sessionId, out var stamped) && now - stamped < cooldown)
            return true;
        if (newestSample.TryGetValue(sessionId, out var stored) && now - stored < cooldown)
            return true;
        return false;
    }

    private void Stamp(Guid sessionId, DateTime now) => _lastPoll[sessionId] = now;

    private void NoteSuccess(Guid sessionId)
    {
        _failures[sessionId] = 0;
        _incidentRaised.TryRemove(sessionId, out _);
    }

    private async Task NoteFailureAsync(Guid sessionId, Agent? owner, string reason, CancellationToken ct)
    {
        var count = _failures.AddOrUpdate(sessionId, 1, (_, n) => n + 1);
        var threshold = Math.Max(1, _settings.ConsecutiveFailuresBeforeIncident);
        if (count < threshold)
            return;
        if (!_incidentRaised.TryAdd(sessionId, 0))
            return;

        var message =
            $"Subscription usage poll on session {sessionId:D} failed {count} consecutive time(s) ({reason}).";
        await using var scope = _scopeFactory.CreateAsyncScope();
        if (owner is not null)
        {
            var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
            await supervisor.RecordIncidentAsync(
                owner.Id,
                sessionId,
                AgentIncidentKind.SubscriptionUsagePollDegraded,
                AlertSeverity.Warning,
                message,
                failureReason: reason,
                ct: ct);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.SaveChangesAsync(ct);
            return;
        }

        _logger.LogWarning(
            "SUBSCRIPTION USAGE POLL DEGRADED on unclaimed session {SessionId}: {Message}",
            sessionId, message);
        var alerts = scope.ServiceProvider.GetService<IAlertService>();
        if (alerts is null)
            return;
        await alerts.RaiseAsync(
            new AlertRaise(
                AlertSeverity.Warning,
                Source: "supervisor",
                Title: $"{AgentIncidentKind.SubscriptionUsagePollDegraded}: usage poll",
                Detail: message,
                DedupKey: $"supervisor:{AgentIncidentKind.SubscriptionUsagePollDegraded}:{sessionId:D}",
                AgentId: null,
                SessionId: sessionId),
            ct);
    }

    internal static string KeyFor(Agent? owner, AgentKind kind) =>
        SubscriptionUsageKey.For(owner, kind);

    private DateTime UtcNow() => _time.GetUtcNow().UtcDateTime;
}
