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
/// Poll-based backstop that reconciles DB session/agent status against the session runner — the
/// source of truth for "is there actually a process". The event pump keeps things honest in real
/// time, but events can be missed (runner restart, dropped SSE stream, a process the runner lost
/// track of); without this sweep those misses became permanent phantoms: sessions Running forever
/// on dead PIDs and agents badged Working in the UI with no process behind them.
///
/// Three passes per scan:
///  1. Sessions the DB thinks are live (Starting/Running/Stopping) but the runner either does not
///     know at all or reports as Exited → closed (Stopped/Failed) with an explanatory reason.
///  2. Working agents whose persistent session is no longer live in the DB → flipped to Failed
///     (interactive agents only — card-owned lifecycles belong to the orchestrator).
///  3. The MIRROR of pass 1 (CARD-0056): sessions the runner is still serving while the DB has
///     written them off. That direction was invisible forever — the query only ever asked about
///     DB-live rows — so a healthy session marked Failed by a launch-verification false positive
///     ran on unclaimed and billable for three days while the supervisor started a replacement.
///     It resolves by RE-ADOPTION on positive evidence, never by inferring a kill from "unclaimed":
///     the session that produced this card was the operator's own live conversation, and a pass
///     that killed what nothing claimed would have killed it mid-sentence.
/// </summary>
public sealed class SessionReconciliationService
{
    private static readonly SessionStatus[] LiveStatuses =
        [SessionStatus.Starting, SessionStatus.Running, SessionStatus.Stopping];

    private readonly AppDbContext _db;
    private readonly ISessionRunnerClient _runnerClient;
    private readonly IEventBus _eventBus;
    private readonly IAlertService _alerts;
    private readonly RunnerReachabilityState _reachability;
    private readonly SessionReAdoptionState _reAdoptions;
    private readonly SessionReconciliationSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SessionReconciliationService> _logger;

    public SessionReconciliationService(
        AppDbContext db,
        ISessionRunnerClient runnerClient,
        IEventBus eventBus,
        IAlertService alerts,
        RunnerReachabilityState reachability,
        SessionReAdoptionState reAdoptions,
        IOptions<SessionReconciliationSettings> settings,
        TimeProvider timeProvider,
        ILogger<SessionReconciliationService> logger)
    {
        _db = db;
        _runnerClient = runnerClient;
        _eventBus = eventBus;
        _alerts = alerts;
        _reachability = reachability;
        _reAdoptions = reAdoptions;
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

        // ONE fetch per sweep, shared by both session passes. It is unconditional now: pass 3 asks
        // about sessions the DB does not think are live, so the old "skip the fetch when no DB row
        // is live" shortcut would have hidden exactly the case CARD-0056 is about.
        var runnerSessions = await TryListRunnerSessionsAsync(ct);
        if (runnerSessions is not null)
        {
            corrections += await ReconcileSessionsAsync(runnerSessions, now, ct);
            corrections += await ReconcileRunnerAliveSessionsAsync(runnerSessions, now, ct);
        }

        corrections += await ReconcileAgentsAsync(now, ct);

        return corrections;
    }

    /// <summary>
    /// The runner's view of the world, or null when it cannot be had — which is not evidence of
    /// anything and must never be treated as "nothing is running". A runner restart is the ordinary
    /// cause; the next sweep after it comes back settles state.
    /// </summary>
    private async Task<IReadOnlyList<SessionRunnerSessionDto>?> TryListRunnerSessionsAsync(CancellationToken ct)
    {
        try
        {
            var sessions = await _runnerClient.ListAsync(ct);
            // Edge-triggered recovery alert only (steady-state reachability is not news).
            if (_reachability.MarkReachable())
            {
                await _alerts.RaiseAsync(
                    new AlertRaise(
                        AlertSeverity.Info, "runner", "Session runner reachable again",
                        DedupKey: "runner:reachability"),
                    ct);
            }

            return sessions;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Runner unreachable — could be a restart in progress. Don't guess; the next sweep
            // after it comes back will see its (empty) session list and close what's gone.
            _logger.LogDebug(ex, "Session reconciliation skipped: session runner unreachable");
            if (_reachability.MarkUnreachable())
            {
                await _alerts.RaiseAsync(
                    new AlertRaise(
                        AlertSeverity.Error, "runner", "Session runner unreachable",
                        Detail: ex.Message, DedupKey: "runner:reachability"),
                    ct);
            }

            return null;
        }
    }

    private async Task<int> ReconcileSessionsAsync(
        IReadOnlyList<SessionRunnerSessionDto> runnerSessions, DateTime now, CancellationToken ct)
    {
        var liveSessions = await _db.AgentSessions
            .Where(s => LiveStatuses.Contains(s.Status))
            .ToListAsync(ct);
        if (liveSessions.Count == 0)
            return 0;

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
                // Same mapping as AgentSessionRuntime.CloseSessionOnExitAsync: a CPU-spin watchdog
                // kill is a clean, resumable stop despite the kill's non-zero exit code.
                session.Status = runnerSession.ExitCode == 0 || runnerSession.ExitReason == AgentExitReason.CpuSpinKilled
                    ? SessionStatus.Stopped
                    : SessionStatus.Failed;
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
        await _alerts.RaiseAsync(
            new AlertRaise(
                AlertSeverity.Warning, "reconciler", "Reconciliation closed phantom session(s)",
                Detail: $"{closedSessionIds.Count} DB-live session(s) had no real process behind them and were closed.",
                DedupKey: "reconciler:sessions"),
            ct);
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

    /// <summary>
    /// Pass 3 (CARD-0056) — the mirror of pass 1: the runner is still serving a session the DB has
    /// written off. Each arm is chosen by what the DB row says, and only one of them can end a
    /// process:
    ///
    /// <list type="bullet">
    /// <item><b>No row at all</b> — alert, never kill. Nothing here knows why the row is gone
    /// (cascade delete, a manual reap), and a session nobody can name is still somebody's work.</item>
    /// <item><b>Failed</b> — RE-ADOPT, on positive evidence. This is the default because the case
    /// that created the pass was a healthy session wrongly declared dead.</item>
    /// <item><b>Stopped</b> — the ONLY auto-kill arm: an operator already expressed stop intent and
    /// the kill evidently did not take, so retry it.</item>
    /// <item><b>Starting/Running/Stopping</b> — pass 1's business; the DB and the runner agree the
    /// session is live.</item>
    /// </list>
    /// </summary>
    private async Task<int> ReconcileRunnerAliveSessionsAsync(
        IReadOnlyList<SessionRunnerSessionDto> runnerSessions, DateTime now, CancellationToken ct)
    {
        var running = runnerSessions
            .Where(s => string.Equals(s.Status, "Running", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (running.Count == 0)
            return 0;

        var ids = running.Select(s => s.SessionId).ToList();
        var rows = await _db.AgentSessions
            .Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

        var corrections = 0;
        var orphans = new List<Guid>();
        foreach (var runnerSession in running)
        {
            if (!rows.TryGetValue(runnerSession.SessionId, out var row))
            {
                orphans.Add(runnerSession.SessionId);
                continue;
            }

            corrections += row.Status switch
            {
                SessionStatus.Failed => await TryReAdoptAsync(row, runnerSession, now, ct),
                SessionStatus.Stopped => await RetryFailedKillAsync(row, runnerSession, ct),
                _ => 0,
            };
        }

        if (orphans.Count > 0)
        {
            _logger.LogWarning(
                "Session runner is serving {Count} session(s) with no DB row at all: {SessionIds}",
                orphans.Count, string.Join(", ", orphans));
            await _alerts.RaiseAsync(
                new AlertRaise(
                    AlertSeverity.Warning, "reconciler", "Runner session(s) with no database row",
                    Detail: $"{orphans.Count} running session(s) are unknown to the database: "
                        + $"{string.Join(", ", orphans)}. They are left alone — nothing here knows what "
                        + "they are, and a session nobody can name is still somebody's work.",
                    DedupKey: "reconciler:orphans"),
                ct);
        }

        return corrections;
    }

    /// <summary>
    /// Writes a Failed row back to Running when the runner proves the process is alive.
    ///
    /// <para>Evidence is PRESENCE of health, not absence of bad news: the runner must report a real
    /// process (a Pid or the detached pty-host's HostPid) AND a per-session probe must answer, which
    /// proves the pty-host's pipe is alive and serving rather than merely listed. An idle session's
    /// sequence does not advance, so advancement is deliberately NOT required — requiring it would
    /// declare every quiet session dead, which is the shape of false positive that started all
    /// this. No evidence ⇒ change nothing and alert: unresponsive-but-running is a state for a
    /// human, not something to resolve by guessing.</para>
    ///
    /// <para>The agent pointer is restored only if it still names this session. Otherwise the
    /// session stays unclaimed but Running and VISIBLE, and the operator decides — the constraint
    /// that outranks everything here is that unclaimed must never imply kill.</para>
    /// </summary>
    private async Task<int> TryReAdoptAsync(
        AgentSession row, SessionRunnerSessionDto runnerSession, DateTime now, CancellationToken ct)
    {
        var agent = await FindOwningAgentAsync(row.Id, ct);

        if (!_settings.ReAdoptEnabled)
        {
            await AlertAndIncidentAsync(
                AlertSeverity.Warning, agent, row.Id,
                $"Session {row.Id} reads Failed but the session runner is still running it "
                + $"(pid {Describe(runnerSession)}). Re-adoption is disabled, so nothing was changed.",
                "ReAdoptDisabled", ct);
            return 0;
        }

        if (runnerSession.Pid is null && runnerSession.HostPid is null)
        {
            await AlertAndIncidentAsync(
                AlertSeverity.Error, agent, row.Id,
                $"Session {row.Id} reads Failed and the session runner reports it Running, but names no "
                + "process behind it. Nothing was changed: re-adoption needs positive evidence of a live "
                + "process, and this is a state for a human.",
                "ReAdoptNoProcess", ct);
            return 0;
        }

        try
        {
            // The probe: reading this session's buffer proves the detached pty-host's pipe is alive
            // and serving, which "the runner listed it" alone does not.
            await _runnerClient.GetBufferAsync(row.Id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Session {SessionId} reads Failed and the runner reports it Running, but the buffer probe "
                + "failed; leaving it alone", row.Id);
            await AlertAndIncidentAsync(
                AlertSeverity.Error, agent, row.Id,
                $"Session {row.Id} reads Failed and the session runner reports it Running, but its buffer "
                + $"probe failed ({ex.Message}). Nothing was changed — unresponsive-but-running is a state "
                + "for a human.",
                "ReAdoptProbeFailed", ct);
            return 0;
        }

        var (allowed, count) = _reAdoptions.TryRegisterReAdoption(row.Id, _settings.MaxReAdoptionsPerSession);
        if (!allowed)
        {
            _logger.LogError(
                "Session {SessionId} has now flapped between Failed and runner-Running {Count} times this "
                + "uptime; re-adoption stops here", row.Id, count);
            await AlertAndIncidentAsync(
                AlertSeverity.Critical, agent, row.Id,
                $"Session {row.Id} has flapped between Failed and runner-Running {count} times since this "
                + $"server started (cap {_settings.MaxReAdoptionsPerSession}). Re-adoption has stopped: "
                + "something keeps failing a session the runner keeps serving, and a loop cannot fix that.",
                "ReAdoptCapReached", ct);
            return 0;
        }

        var wasFailedBecause = row.FailureReason;
        row.Status = SessionStatus.Running;
        row.EndedAt = null;
        row.ExitCode = null;
        row.FailureReason = null;
        row.LastSeenAt = now;

        var claimed = agent is not null;
        if (agent is { Status: AgentStatus.Failed })
        {
            agent.Status = AgentStatus.Running;
            agent.UpdatedAt = now;
        }

        var detail =
            $"Session {row.Id} read Failed while the session runner was still running it "
            + $"(pid {Describe(runnerSession)}); the buffer probe answered, so the row is back to Running "
            + $"(re-adoption {count} of {_settings.MaxReAdoptionsPerSession}). "
            + (claimed
                ? "Its agent still points at it and has been restored."
                : "No agent claims it, so it stays unclaimed but visible — stopping it is the operator's "
                  + "call, never this sweep's.")
            + (string.IsNullOrWhiteSpace(wasFailedBecause)
                ? string.Empty
                : $" It had been failed with: {wasFailedBecause}");

        _logger.LogWarning(
            "Reconciliation re-adopted session {SessionId} ({Claimed}): it was Failed but the runner is "
            + "still serving it. Previous failure reason: {FailureReason}",
            row.Id, claimed ? "agent restored" : "unclaimed", wasFailedBecause ?? "(none)");
        await AlertAndIncidentAsync(AlertSeverity.Warning, agent, row.Id, detail, "SessionReAdopted", ct);

        await _db.SaveChangesAsync(ct);
        await _eventBus.PublishToGroupAsync(
            AgentSessionGroups.Session(row.Id),
            "SessionStarted",
            new { sessionId = row.Id, cardId = row.CardId },
            ct);
        if (agent is not null)
            await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);

        return 1;
    }

    /// <summary>
    /// The one arm that may end a process: the row says Stopped, so an operator already asked for
    /// this session to go, and the kill evidently did not take. Retrying that kill enacts a decision
    /// that was already made — it never infers one.
    /// </summary>
    private async Task<int> RetryFailedKillAsync(
        AgentSession row, SessionRunnerSessionDto runnerSession, CancellationToken ct)
    {
        try
        {
            await _runnerClient.KillAsync(row.Id, ct);
            _logger.LogWarning(
                "Reconciliation re-issued the kill for session {SessionId}: the DB says Stopped but the "
                + "runner was still running it (pid {Pid})", row.Id, Describe(runnerSession));
            return 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Re-issuing the kill for stopped session {SessionId} failed", row.Id);
            await _alerts.RaiseAsync(
                new AlertRaise(
                    AlertSeverity.Error, "reconciler", "A stopped session is still running",
                    Detail: $"Session {row.Id} is Stopped in the database but the session runner is still "
                        + $"running it, and re-issuing the kill failed: {ex.Message}",
                    DedupKey: $"reconciler:kill:{row.Id}",
                    SessionId: row.Id),
                ct);
            return 0;
        }
    }

    private Task<Agent?> FindOwningAgentAsync(Guid sessionId, CancellationToken ct) =>
        _db.Agents.FirstOrDefaultAsync(a => a.PersistentSessionId == sessionId.ToString("D"), ct);

    /// <summary>
    /// Raises the alert, and hangs an incident off the owning agent when there is one. The incident
    /// row is written the same way <see cref="AgentSupervisorService.RecordIncidentAsync"/> writes
    /// it — same 1:1 alert, same dedup key — rather than calling that service, which would drag the
    /// whole launch graph (AgentControlService → AgentSessionService → worktrees) into a sweep that
    /// must stay cheap and independent of it. An unclaimed session gets the alert alone: incidents
    /// need an agent to belong to, and that is precisely the session this pass must not lose sight
    /// of, so the alert is not optional.
    /// </summary>
    private async Task AlertAndIncidentAsync(
        AlertSeverity severity, Agent? agent, Guid sessionId, string detail, string failureReason,
        CancellationToken ct)
    {
        if (agent is not null)
        {
            _db.AgentIncidents.Add(new AgentIncident
            {
                Id = Guid.NewGuid(),
                AgentId = agent.Id,
                SessionId = sessionId,
                Kind = AgentIncidentKind.SessionReAdopted,
                Severity = severity,
                Message = detail,
                FailureReason = failureReason,
                CreatedAt = _timeProvider.GetUtcNow().UtcDateTime,
            });
            await _db.SaveChangesAsync(ct);
        }

        await _alerts.RaiseAsync(
            new AlertRaise(
                severity,
                Source: agent is null ? "reconciler" : "supervisor",
                Title: agent is null
                    ? "Runner session the database had written off"
                    : $"{AgentIncidentKind.SessionReAdopted}: agent supervision",
                Detail: detail,
                DedupKey: agent is null
                    ? $"reconciler:readopt:{sessionId}"
                    : $"supervisor:{AgentIncidentKind.SessionReAdopted}:{agent.Id}",
                AgentId: agent?.Id,
                SessionId: sessionId),
            ct);
    }

    private static string Describe(SessionRunnerSessionDto runnerSession) =>
        $"{runnerSession.Pid?.ToString() ?? "?"}, host {runnerSession.HostPid?.ToString() ?? "?"}";

    private async Task<int> ReconcileAgentsAsync(DateTime now, CancellationToken ct)
    {
        var agentGrace = TimeSpan.FromMilliseconds(Math.Max(0, _settings.AgentGraceMs));
        var workingAgents = await _db.Agents
            .Where(a => a.Status == AgentStatus.Running)
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
        await _alerts.RaiseAsync(
            new AlertRaise(
                AlertSeverity.Warning, "reconciler", "Reconciliation failed phantom Working agent(s)",
                Detail: $"{changedAgentIds.Count} Working agent(s) had no live session and were flipped to Failed.",
                DedupKey: "reconciler:agents"),
            ct);
        foreach (var agentId in changedAgentIds)
            await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agentId), ct);

        return changedAgentIds.Count;
    }
}
