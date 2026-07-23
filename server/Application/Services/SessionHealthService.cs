using System.Collections.Concurrent;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Per-session health bookkeeping that must survive across ticks (the service itself is scoped).
/// In-memory on purpose: probe streaks are ephemeral observations, not durable state.
/// </summary>
public sealed class SessionHealthStateStore
{
    public sealed class Entry
    {
        public long LastSequence;
        public DateTime LastSequenceChangeUtc;
        public int ConsecutiveZeroConnProbes;
        public int ReArmAttempts;
        public DateTime? ReArmSettleUntilUtc;
        public bool DegradedAlerted;
    }

    public ConcurrentDictionary<Guid, Entry> Sessions { get; } = new();
}

/// <summary>
/// Session health for always-on agents (spec slice 3): the RC bridge watch (re-arm in place,
/// then restart-when-idle). Everything is idle-gated: a session showing fresh output is never
/// touched. Wedge/deadness detection is DELIVERY-TIME ONLY (SessionMessageQueueService +
/// ComposerDeliveryEvidence): a session is only probed when a real message we sent doesn't
/// behave as expected. The periodic round-trip "pong" probe was removed 2026-07-23 — it spent
/// model turns on healthy idle sessions and its restart-reset in-memory clock made it spammy.
/// </summary>
public sealed class SessionHealthService
{
    private static readonly SessionStatus[] LiveSessionStatuses =
        [SessionStatus.Starting, SessionStatus.Running, SessionStatus.Stopping];

    private readonly AppDbContext _db;
    private readonly ISessionRunnerClient _runner;
    private readonly IRcBridgeProbe _probe;
    private readonly ISessionHealthActions _actions;
    private readonly AgentSupervisorService _supervisor;
    private readonly SessionHealthStateStore _store;
    private readonly SupervisionSettings _settings;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SessionHealthService> _logger;

    public SessionHealthService(
        AppDbContext db,
        ISessionRunnerClient runner,
        IRcBridgeProbe probe,
        ISessionHealthActions actions,
        AgentSupervisorService supervisor,
        SessionHealthStateStore store,
        IOptions<SupervisionSettings> settings,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<SessionHealthService> logger)
    {
        _db = db;
        _runner = runner;
        _probe = probe;
        _actions = actions;
        _supervisor = supervisor;
        _store = store;
        _settings = settings.Value;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<int> TickAsync(CancellationToken ct)
    {
        if (!_settings.RcWatch.Enabled)
            return 0;

        IReadOnlyList<SessionRunnerSessionDto> runnerSessions;
        try
        {
            runnerSessions = await _runner.ListAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Session health tick skipped: runner unavailable");
            return 0;
        }

        var candidates = await LoadCandidatesAsync(ct);
        if (candidates.Count == 0)
            return 0;

        var bySessionId = runnerSessions.ToDictionary(s => s.SessionId);
        var now = UtcNow();
        var actions = 0;

        foreach (var (agent, session) in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (!bySessionId.TryGetValue(session.Id, out var live) || live.Pid is not int childPid)
                continue;

            var entry = _store.Sessions.GetOrAdd(session.Id, _ => new SessionHealthStateStore.Entry
            {
                LastSequence = live.LastSequence,
                LastSequenceChangeUtc = now,
            });

            // Idle tracking: any output-sequence movement stamps activity and resets streaks —
            // busy sessions are never probed or repaired.
            if (live.LastSequence != entry.LastSequence)
            {
                entry.LastSequence = live.LastSequence;
                entry.LastSequenceChangeUtc = now;
                entry.ConsecutiveZeroConnProbes = 0;
            }

            var idleFor = now - entry.LastSequenceChangeUtc;
            var isIdle = idleFor >= TimeSpan.FromMinutes(_settings.RcWatch.IdleQuietMinutes);

            try
            {
                if (_settings.RcWatch.Enabled && agent.RemoteControlEnabled && isIdle)
                    actions += await WatchRcAsync(agent, session, childPid, entry, now, ct) ? 1 : 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Session health pass failed for agent {AgentName} session {SessionId}", agent.Name, session.Id);
            }
        }

        // Forget sessions that are gone.
        var liveIds = candidates.Select(c => c.Session.Id).ToHashSet();
        foreach (var stale in _store.Sessions.Keys.Where(k => !liveIds.Contains(k)).ToList())
            _store.Sessions.TryRemove(stale, out _);

        return actions;
    }

    private async Task<bool> WatchRcAsync(
        Agent agent, AgentSession session, int childPid,
        SessionHealthStateStore.Entry entry, DateTime now, CancellationToken ct)
    {
        if (entry.ReArmSettleUntilUtc is { } settle && now < settle)
            return false;

        var probe = _probe.Probe(childPid);
        if (probe.BridgeConnections > 0)
        {
            if (entry.ConsecutiveZeroConnProbes > 0 || entry.ReArmAttempts > 0 || entry.DegradedAlerted)
                _logger.LogInformation(
                    "Agent {AgentName}: RC bridge healthy again ({Connections} connection(s))",
                    agent.Name, probe.BridgeConnections);
            entry.ConsecutiveZeroConnProbes = 0;
            entry.ReArmAttempts = 0;
            entry.DegradedAlerted = false;
            return false;
        }

        entry.ConsecutiveZeroConnProbes++;
        _logger.LogDebug(
            "Agent {AgentName}: RC bridge probe zero connections ({Streak}/{Threshold}, armed={Armed})",
            agent.Name, entry.ConsecutiveZeroConnProbes,
            _settings.RcWatch.ConsecutiveFailedProbesBeforeAction, probe.Armed);

        if (entry.ConsecutiveZeroConnProbes < _settings.RcWatch.ConsecutiveFailedProbesBeforeAction)
            return false;

        if (entry.ReArmAttempts < _settings.RcWatch.ReArmAttemptsBeforeRestart)
        {
            entry.ReArmAttempts++;
            entry.ConsecutiveZeroConnProbes = 0;
            entry.ReArmSettleUntilUtc = now.AddMinutes(_settings.RcWatch.ReArmSettleMinutes);
            await _actions.EnqueueWhenIdleAsync(session.Id, "/remote-control", ct);
            await RecordAsync(agent.Id, session.Id, AgentIncidentKind.RcReArmed, AlertSeverity.Warning,
                $"Remote-control bridge dead for {_settings.RcWatch.ConsecutiveFailedProbesBeforeAction} probes; "
                + $"re-armed /remote-control in place (attempt {entry.ReArmAttempts}/{_settings.RcWatch.ReArmAttemptsBeforeRestart}).",
                ct);
            _logger.LogWarning(
                "Agent {AgentName}: RC bridge dead; re-arming /remote-control (attempt {Attempt})",
                agent.Name, entry.ReArmAttempts);
            return true;
        }

        if (!entry.DegradedAlerted)
        {
            entry.DegradedAlerted = true;
            entry.ConsecutiveZeroConnProbes = 0;
            await RecordAsync(agent.Id, session.Id, AgentIncidentKind.RcRestart, AlertSeverity.Warning,
                "Re-arms failed to restore the remote-control bridge; restarting the session while idle "
                + "(conversation resumes; a fresh Claude process re-establishes the bridge).",
                ct);
            _logger.LogWarning(
                "Agent {AgentName}: restarting idle session {SessionId} to restore remote control",
                agent.Name, session.Id);
            await _actions.KillSessionAsync(session.Id, ct);
            _store.Sessions.TryRemove(session.Id, out _);
            return true;
        }

        // Restart cycle already spent and still dead: record once, hold until healthy resets us.
        await RecordAsync(agent.Id, session.Id, AgentIncidentKind.RcDegraded, AlertSeverity.Error,
            "Remote control remains degraded after re-arms and a session restart; holding "
            + "(likely a claude.ai-side outage). Will keep probing.",
            ct);
        entry.ConsecutiveZeroConnProbes = int.MinValue / 2; // hold: don't re-alert every probe
        return true;
    }

    private async Task RecordAsync(
        Guid agentId, Guid? sessionId, AgentIncidentKind kind, AlertSeverity severity,
        string message, CancellationToken ct)
    {
        await _supervisor.RecordIncidentAsync(agentId, sessionId, kind, severity, message, ct: ct);
        await _db.SaveChangesAsync(ct);
        await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agentId), ct);
    }

    private async Task<List<(Agent Agent, AgentSession Session)>> LoadCandidatesAsync(CancellationToken ct)
    {
        // Health watch covers always-on agents' live interactive ClaudeCode sessions.
        var agents = await _db.Agents.AsNoTracking().Where(a => a.AlwaysOn).ToListAsync(ct);
        var result = new List<(Agent, AgentSession)>();
        foreach (var agent in agents)
        {
            if (!Guid.TryParse(agent.PersistentSessionId, out var sessionId))
                continue;

            var session = await _db.AgentSessions.AsNoTracking()
                .FirstOrDefaultAsync(
                    s => s.Id == sessionId
                        && s.CardId == null
                        && s.AgentKind == AgentKind.ClaudeCode
                        && LiveSessionStatuses.Contains(s.Status),
                    ct);
            if (session is not null)
                result.Add((agent, session));
        }

        return result;
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
