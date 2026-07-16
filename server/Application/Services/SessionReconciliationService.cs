using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Poll-based backstop that reconciles DB session/agent status against the session runner — the
/// source of truth for "is there actually a process". The event pump keeps things honest in real
/// time, but events can be missed (runner restart, dropped SSE stream, a process the runner lost
/// track of); without this sweep those misses became permanent phantoms: sessions Running forever
/// on dead PIDs and agents badged Working in the UI with no process behind them.
///
/// Two passes per scan:
///  1. Sessions the DB thinks are live (Starting/Running/Stopping) but the runner either does not
///     know at all or reports as Exited → closed (Stopped/Failed) with an explanatory reason.
///  2. Working agents whose persistent session is no longer live in the DB → flipped to Failed
///     (interactive agents only — card-owned lifecycles belong to the orchestrator).
/// </summary>
public sealed class SessionReconciliationService
{
    private static readonly SessionStatus[] LiveStatuses =
        [SessionStatus.Starting, SessionStatus.Running, SessionStatus.Stopping];

    private readonly AppDbContext _db;
    private readonly ISessionRunnerClient _runnerClient;
    private readonly IEventBus _eventBus;
    private readonly SessionReconciliationSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SessionReconciliationService> _logger;

    public SessionReconciliationService(
        AppDbContext db,
        ISessionRunnerClient runnerClient,
        IEventBus eventBus,
        IOptions<SessionReconciliationSettings> settings,
        TimeProvider timeProvider,
        ILogger<SessionReconciliationService> logger)
    {
        _db = db;
        _runnerClient = runnerClient;
        _eventBus = eventBus;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Runs one reconciliation sweep. Returns the number of rows it had to correct.</summary>
    public async Task<int> ScanAsync(CancellationToken ct)
    {
        if (!_settings.Enabled)
            return 0;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var corrections = 0;

        corrections += await ReconcileSessionsAsync(now, ct);
        corrections += await ReconcileAgentsAsync(now, ct);

        return corrections;
    }

    private async Task<int> ReconcileSessionsAsync(DateTime now, CancellationToken ct)
    {
        var liveSessions = await _db.AgentSessions
            .Where(s => LiveStatuses.Contains(s.Status))
            .ToListAsync(ct);
        if (liveSessions.Count == 0)
            return 0;

        IReadOnlyList<SessionRunnerSessionDto> runnerSessions;
        try
        {
            runnerSessions = await _runnerClient.ListAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Runner unreachable — could be a restart in progress. Don't guess; the next sweep
            // after it comes back will see its (empty) session list and close what's gone.
            _logger.LogDebug(ex, "Session reconciliation skipped: session runner unreachable");
            return 0;
        }

        var runnerById = runnerSessions.ToDictionary(s => s.SessionId);
        var startingGrace = TimeSpan.FromMilliseconds(Math.Max(0, _settings.StartingGraceMs));
        var closedSessionIds = new List<Guid>();

        foreach (var session in liveSessions)
        {
            // A Starting session may legitimately not have reached the runner yet.
            if (session.Status == SessionStatus.Starting && now - session.StartedAt < startingGrace)
                continue;

            if (!runnerById.TryGetValue(session.Id, out var runnerSession))
            {
                session.Status = SessionStatus.Failed;
                session.FailureReason =
                    "Session runner does not know this session (launch failed or the runner restarted).";
                session.EndedAt ??= now;
                session.LastSeenAt = now;
                closedSessionIds.Add(session.Id);
                _logger.LogWarning(
                    "Reconciliation closed session {SessionId}: unknown to the session runner", session.Id);
            }
            else if (string.Equals(runnerSession.Status, "Exited", StringComparison.OrdinalIgnoreCase))
            {
                session.Status = runnerSession.ExitCode == 0 ? SessionStatus.Stopped : SessionStatus.Failed;
                session.ExitCode = runnerSession.ExitCode;
                if (session.Status == SessionStatus.Failed)
                {
                    session.FailureReason =
                        $"Runner reported an exit that was never observed ({runnerSession.ExitReason}, "
                        + $"code {runnerSession.ExitCode?.ToString() ?? "unknown"}).";
                }
                session.EndedAt ??= now;
                session.LastSeenAt = now;
                closedSessionIds.Add(session.Id);
                _logger.LogWarning(
                    "Reconciliation closed session {SessionId}: runner reported unobserved exit ({ExitReason})",
                    session.Id, runnerSession.ExitReason);
            }
        }

        if (closedSessionIds.Count == 0)
            return 0;

        await _db.SaveChangesAsync(ct);
        foreach (var sessionId in closedSessionIds)
        {
            await _eventBus.PublishToGroupAsync(
                AgentSessionGroups.Session(sessionId),
                "SessionExited",
                new { sessionId, status = "Exited", exitCode = (int?)null, exitReason = "Reconciled" },
                ct);
        }

        return closedSessionIds.Count;
    }

    private async Task<int> ReconcileAgentsAsync(DateTime now, CancellationToken ct)
    {
        var agentGrace = TimeSpan.FromMilliseconds(Math.Max(0, _settings.AgentGraceMs));
        var workingAgents = await _db.Agents
            .Where(a => a.Status == AgentStatus.Working)
            .ToListAsync(ct);

        var changedAgentIds = new List<Guid>();
        foreach (var agent in workingAgents)
        {
            // Card-owned agents transition via the orchestrator; interfering here would race it.
            if (agent.CurrentCardId is not null)
                continue;
            // Give normal flows (launch queue lag, session hand-over) time to settle.
            if (now - agent.UpdatedAt < agentGrace)
                continue;

            var hasLiveSession = Guid.TryParse(agent.PersistentSessionId, out var sessionId)
                && await _db.AgentSessions.AnyAsync(
                    s => s.Id == sessionId && LiveStatuses.Contains(s.Status), ct);
            if (hasLiveSession)
                continue;

            agent.Status = AgentStatus.Failed;
            agent.UpdatedAt = now;
            changedAgentIds.Add(agent.Id);
            _logger.LogWarning(
                "Reconciliation flipped agent {AgentId} ({AgentName}) from Working to Failed: no live session",
                agent.Id, agent.Name);
        }

        if (changedAgentIds.Count == 0)
            return 0;

        await _db.SaveChangesAsync(ct);
        foreach (var agentId in changedAgentIds)
            await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agentId), ct);

        return changedAgentIds.Count;
    }
}
