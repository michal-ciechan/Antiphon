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
        public DateTime? LastRoundTripUtc;
        public long? RoundTripBaselineSequence;
        public DateTime? RoundTripDeadlineUtc;
    }

    public ConcurrentDictionary<Guid, Entry> Sessions { get; } = new();
}

/// <summary>
/// Session health for always-on agents (spec slice 3): the RC bridge watch (re-arm in place,
/// then restart-when-idle) and the round-trip liveness probe (6-hourly healthcheck prompt).
/// Everything is idle-gated: a session showing fresh output is never touched. Wedge detection
/// moved to delivery time (SessionMessageQueueService + ComposerDeliveryEvidence).
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
        if (!_settings.RcWatch.Enabled && !_settings.LivenessProbe.Enabled)
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
                // Seed the round-trip clock at first sight: the store is in-memory, so without
                // this every server restart made the 6-hourly probe "due" immediately and idle
                // agents got pinged after each deploy (observed 2026-07-23 as pong-spam).
                LastRoundTripUtc = now,
            });

            // Idle tracking: any output-sequence movement stamps activity and resets streaks —
            // busy sessions are never probed or repaired. Real output is also liveness evidence,
            // so it re-arms the round-trip clock: only a session that has been COMPLETELY silent
            // for the whole interval earns a synthetic healthcheck turn.
            if (live.LastSequence != entry.LastSequence)
            {
                entry.LastSequence = live.LastSequence;
                entry.LastSequenceChangeUtc = now;
                entry.ConsecutiveZeroConnProbes = 0;
                entry.LastRoundTripUtc = now;
            }

            var idleFor = now - entry.LastSequenceChangeUtc;
            var isIdle = idleFor >= TimeSpan.FromMinutes(_settings.RcWatch.IdleQuietMinutes);

            try
            {
                if (_settings.RcWatch.Enabled && agent.RemoteControlEnabled && isIdle)
                    actions += await WatchRcAsync(agent, session, childPid, entry, now, ct) ? 1 : 0;

                if (_settings.LivenessProbe.Enabled && isIdle)
                    actions += await RunLivenessProbesAsync(agent, session, live, entry, now, ct) ? 1 : 0;
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

    private async Task<bool> RunLivenessProbesAsync(
        Agent agent, AgentSession session, SessionRunnerSessionDto live,
        SessionHealthStateStore.Entry entry, DateTime now, CancellationToken ct)
    {
        // Round-trip verdict pending?
        if (entry.RoundTripDeadlineUtc is { } deadline)
        {
            if (live.LastSequence > entry.RoundTripBaselineSequence)
            {
                entry.RoundTripDeadlineUtc = null;
                entry.RoundTripBaselineSequence = null;
                return false; // output moved: alive
            }

            if (now >= deadline)
            {
                entry.RoundTripDeadlineUtc = null;
                entry.RoundTripBaselineSequence = null;
                await FailLivenessAsync(agent, session,
                    $"Round-trip healthcheck produced no output within {_settings.LivenessProbe.RoundTripTimeoutMinutes} min.",
                    ct);
                return true;
            }

            return false;
        }

        // NOTE: there is deliberately no synthetic "type a char and watch the screen" probe here.
        // The old TUI echo probe false-positive-killed healthy idle sessions (2026-07-20); wedge
        // detection now happens at DELIVERY time via composer content verification in
        // SessionMessageQueueService (ComposerDeliveryEvidence), where there is real expected
        // content to check for instead of "any delta".

        // Round-trip probe: queue a tiny prompt; the reply must move the output sequence.
        if (_settings.LivenessProbe.RoundTripIntervalHours > 0
            && Due(entry.LastRoundTripUtc, TimeSpan.FromHours(_settings.LivenessProbe.RoundTripIntervalHours), now))
        {
            entry.LastRoundTripUtc = now;
            entry.RoundTripBaselineSequence = live.LastSequence;
            entry.RoundTripDeadlineUtc = now.AddMinutes(_settings.LivenessProbe.RoundTripTimeoutMinutes);
            await _actions.EnqueueWhenIdleAsync(
                session.Id, "healthcheck: reply with exactly `pong` and nothing else", ct);
            _logger.LogInformation("Agent {AgentName}: round-trip liveness probe enqueued", agent.Name);
            return true;
        }

        return false;
    }

    private async Task FailLivenessAsync(Agent agent, AgentSession session, string reason, CancellationToken ct)
    {
        await RecordAsync(agent.Id, session.Id, AgentIncidentKind.LivenessProbeFailed, AlertSeverity.Error,
            $"{reason} Restarting the session while idle.", ct);
        _logger.LogWarning(
            "Agent {AgentName}: liveness probe failed ({Reason}); restarting session {SessionId}",
            agent.Name, reason, session.Id);
        await _actions.KillSessionAsync(session.Id, ct);
        _store.Sessions.TryRemove(session.Id, out _);
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

    private static bool Due(DateTime? last, TimeSpan interval, DateTime now) =>
        last is null || now - last >= interval;

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
