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
/// Always-on agent supervision (spec: 2026-07-20-always-on-agents-and-alerting.md). Each tick
/// ensures every <c>AlwaysOn</c> agent that is not user-suspended has a live session: starting it
/// at boot, restarting it after crashes on a backoff ladder that never gives up (doubling to a
/// 30-day cap), recording every decision as an <see cref="AgentIncident"/> with the attempt
/// number, chosen delay, and absolute next-retry time, and firing tier-escalation incidents
/// exactly once when the ladder crosses hourly (Warning) and daily (Critical).
/// </summary>
public sealed class AgentSupervisorService
{
    private static readonly SessionStatus[] LiveSessionStatuses =
        [SessionStatus.Starting, SessionStatus.Running, SessionStatus.Stopping];

    private static readonly TimeSpan HourlyTier = TimeSpan.FromHours(1);
    private static readonly TimeSpan DailyTier = TimeSpan.FromDays(1);

    private readonly AppDbContext _db;
    private readonly AgentControlService _control;
    private readonly ISessionRunnerClient _runnerClient;
    private readonly IEventBus _eventBus;
    private readonly SupervisionSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AgentSupervisorService> _logger;

    public AgentSupervisorService(
        AppDbContext db,
        AgentControlService control,
        ISessionRunnerClient runnerClient,
        IEventBus eventBus,
        IOptions<SupervisionSettings> settings,
        TimeProvider timeProvider,
        ILogger<AgentSupervisorService> logger)
    {
        _db = db;
        _control = control;
        _runnerClient = runnerClient;
        _eventBus = eventBus;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Runs one supervision sweep. Returns the number of actions taken (schedules + attempts).</summary>
    public async Task<int> TickAsync(CancellationToken ct)
    {
        if (!_settings.Enabled)
            return 0;

        // Never restart-storm through a runner outage/restart window: while the runner is
        // unreachable the truth about sessions is unknowable — skip and let the reconciler
        // settle state first. (Non-transport failures fall through: fakes in tests may throw
        // NotSupported, which must not disable supervision.)
        try
        {
            await _runnerClient.ListAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogDebug(ex, "Supervision tick skipped: session runner unreachable");
            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogTrace(ex, "Supervision runner probe failed non-transport; continuing");
        }

        var agents = await _db.Agents.Where(a => a.AlwaysOn).ToListAsync(ct);
        if (agents.Count == 0)
            return 0;

        var actions = 0;
        foreach (var agent in agents)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                actions += await SuperviseAsync(agent, ct) ? 1 : 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Supervision failed for agent {AgentId} ({AgentName})", agent.Id, agent.Name);
            }
        }

        return actions;
    }

    private async Task<bool> SuperviseAsync(Agent agent, CancellationToken ct)
    {
        var now = UtcNow();
        var state = await GetOrCreateStateAsync(agent.Id, ct);
        if (state.Suspended)
            return false;

        var liveSession = await FindPersistentSessionAsync(agent, LiveSessionStatuses, ct);
        if (liveSession is not null)
        {
            // Healthy long enough? Reset the ladder so the next incident starts from 5s again.
            if ((state.ConsecutiveFailures > 0 || state.NextRestartAt is not null || state.LastEscalationTier > 0)
                && liveSession.Status == SessionStatus.Running
                && now - liveSession.StartedAt >= TimeSpan.FromMinutes(_settings.HealthyUptimeResetMinutes))
            {
                var failures = state.ConsecutiveFailures;
                state.ConsecutiveFailures = 0;
                state.NextRestartAt = null;
                state.LastEscalationTier = 0;
                state.LastHealthyAt = now;
                state.UpdatedAt = now;
                await RecordIncidentAsync(
                    agent.Id,
                    liveSession.Id,
                    AgentIncidentKind.Recovered,
                    AlertSeverity.Info,
                    $"Recovered: running healthily for {_settings.HealthyUptimeResetMinutes} min after {failures} failure(s); backoff reset.",
                    ct: ct);
                _logger.LogInformation(
                    "Agent {AgentName} recovered after {Failures} failure(s); supervision backoff reset",
                    agent.Name, failures);
                await _db.SaveChangesAsync(ct);
                await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);
            }
            else if (state.LastHealthyAt is null || liveSession.Status == SessionStatus.Running)
            {
                state.LastHealthyAt = now;
                state.UpdatedAt = now;
                await _db.SaveChangesAsync(ct);
            }

            return false;
        }

        // Not running. Schedule a restart if none is pending.
        if (state.NextRestartAt is null)
        {
            var dead = await FindPersistentSessionAsync(agent, statuses: null, ct);

            // A previous supervised start that evidently died before reaching healthy uptime
            // counts as a failure — this is what grows the ladder for fast crash-loops.
            if (state.LastAttemptAt is not null)
                state.ConsecutiveFailures++;

            var attempt = state.ConsecutiveFailures + 1;
            var delay = Backoff(state.ConsecutiveFailures);
            state.NextRestartAt = now + delay;
            state.UpdatedAt = now;

            if (dead is not null && dead.Status == SessionStatus.Failed)
            {
                await RecordIncidentAsync(
                    agent.Id, dead.Id, AgentIncidentKind.Crash, AlertSeverity.Warning,
                    $"Session died (exit {dead.ExitCode?.ToString() ?? "unknown"}: {dead.FailureReason ?? "no reason recorded"}).",
                    dead.ExitCode, dead.FailureReason, ct);
            }

            await RecordIncidentAsync(
                agent.Id, dead?.Id, AgentIncidentKind.RestartScheduled, AlertSeverity.Warning,
                $"Restart attempt {attempt} scheduled for {state.NextRestartAt:u} (backing off {Describe(delay)}).",
                dead?.ExitCode, dead?.FailureReason, ct);
            await EscalateIfTierCrossedAsync(agent, state, delay, ct);

            _logger.LogWarning(
                "Agent {AgentName}: not running; restart attempt {Attempt} scheduled for {NextRestartAt:u} (backoff {Delay})",
                agent.Name, attempt, state.NextRestartAt, Describe(delay));

            await _db.SaveChangesAsync(ct);
            await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);
            return true;
        }

        if (now < state.NextRestartAt)
            return false;

        // Due: attempt the restart.
        var attemptNumber = state.ConsecutiveFailures + 1;
        state.LastAttemptAt = now;
        state.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        var fresh = state.ConsecutiveFailures >= _settings.FreshAfterResumeFailures;
        try
        {
            _logger.LogInformation(
                "Agent {AgentName}: supervised restart attempt {Attempt} ({Mode})",
                agent.Name, attemptNumber, fresh ? "fresh conversation" : "resume");
            await _control.StartAsync(agent.Id, new StartAgentRequest(Fresh: fresh), ct);

            // Success ⇒ stop scheduling; the failure counter only resets after sustained health.
            // (StartAsync clears supervision state itself for manual semantics; re-load ours.)
            var refreshed = await GetOrCreateStateAsync(agent.Id, ct);
            refreshed.NextRestartAt = null;
            refreshed.UpdatedAt = UtcNow();
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var refreshed = await GetOrCreateStateAsync(agent.Id, ct);
            refreshed.ConsecutiveFailures++;
            var delay = Backoff(refreshed.ConsecutiveFailures);
            refreshed.NextRestartAt = UtcNow() + delay;
            refreshed.LastAttemptAt = now;
            refreshed.UpdatedAt = UtcNow();

            await RecordIncidentAsync(
                agent.Id, null, AgentIncidentKind.StartFailure, AlertSeverity.Error,
                $"Start attempt {attemptNumber} failed: {ex.Message} — next retry {refreshed.NextRestartAt:u} (backing off {Describe(delay)}).",
                ct: ct);
            await EscalateIfTierCrossedAsync(agent, refreshed, delay, ct);

            _logger.LogWarning(ex,
                "Agent {AgentName}: start attempt {Attempt} failed; next retry {NextRestartAt:u} (backoff {Delay})",
                agent.Name, attemptNumber, refreshed.NextRestartAt, Describe(delay));

            await _db.SaveChangesAsync(ct);
            await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);
            return true;
        }
    }

    /// <summary>min(base · 2ⁿ, cap) — the never-give-up ladder.</summary>
    public TimeSpan Backoff(int consecutiveFailures) =>
        TimeSpan.FromSeconds(Math.Min(
            _settings.BackoffBaseSeconds * Math.Pow(2, Math.Min(consecutiveFailures, 40)),
            _settings.BackoffMaxSeconds));

    private async Task EscalateIfTierCrossedAsync(
        Agent agent, AgentSupervisionState state, TimeSpan delay, CancellationToken ct)
    {
        var tier = delay >= DailyTier ? 2 : delay >= HourlyTier ? 1 : 0;
        if (tier <= state.LastEscalationTier)
            return;

        state.LastEscalationTier = tier;
        var severity = tier == 2 ? AlertSeverity.Critical : AlertSeverity.Warning;
        var cadence = tier == 2 ? "daily-or-slower" : "hourly-or-slower";
        await RecordIncidentAsync(
            agent.Id, null, AgentIncidentKind.BackoffEscalated, severity,
            $"Backoff escalated: {state.ConsecutiveFailures} consecutive failures; now retrying on a {cadence} cadence (current delay {Describe(delay)}).",
            ct: ct);
        _logger.LogError(
            "Agent {AgentName}: supervision backoff escalated to {Cadence} after {Failures} consecutive failures",
            agent.Name, cadence, state.ConsecutiveFailures);
    }

    public async Task<AgentSupervisionState> GetOrCreateStateAsync(Guid agentId, CancellationToken ct)
    {
        var state = await _db.AgentSupervisionStates.FirstOrDefaultAsync(s => s.AgentId == agentId, ct);
        if (state is null)
        {
            state = new AgentSupervisionState { AgentId = agentId, UpdatedAt = UtcNow() };
            _db.AgentSupervisionStates.Add(state);
        }

        return state;
    }

    public async Task RecordIncidentAsync(
        Guid agentId,
        Guid? sessionId,
        AgentIncidentKind kind,
        AlertSeverity severity,
        string message,
        int? exitCode = null,
        string? failureReason = null,
        CancellationToken ct = default)
    {
        _db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            AgentId = agentId,
            SessionId = sessionId,
            Kind = kind,
            Severity = severity,
            Message = message,
            ExitCode = exitCode,
            FailureReason = failureReason,
            CreatedAt = UtcNow(),
        });
        await Task.CompletedTask;
    }

    /// <summary>Nightly-ish hygiene: incidents past retention or beyond the per-agent cap.</summary>
    public async Task<int> PruneIncidentsAsync(CancellationToken ct)
    {
        var cutoff = UtcNow().AddDays(-_settings.IncidentRetentionDays);
        var removed = await _db.AgentIncidents.Where(i => i.CreatedAt < cutoff).ExecuteDeleteAsync(ct);

        var overCap = await _db.AgentIncidents
            .GroupBy(i => i.AgentId)
            .Where(g => g.Count() > _settings.IncidentCapPerAgent)
            .Select(g => g.Key)
            .ToListAsync(ct);
        foreach (var agentId in overCap)
        {
            var keepIds = _db.AgentIncidents
                .Where(i => i.AgentId == agentId)
                .OrderByDescending(i => i.CreatedAt)
                .Take(_settings.IncidentCapPerAgent)
                .Select(i => i.Id);
            removed += await _db.AgentIncidents
                .Where(i => i.AgentId == agentId && !keepIds.Contains(i.Id))
                .ExecuteDeleteAsync(ct);
        }

        return removed;
    }

    private async Task<AgentSession?> FindPersistentSessionAsync(
        Agent agent, SessionStatus[]? statuses, CancellationToken ct)
    {
        if (!Guid.TryParse(agent.PersistentSessionId, out var sessionId))
            return null;

        var query = _db.AgentSessions.Where(s => s.Id == sessionId);
        if (statuses is not null)
            query = query.Where(s => statuses.Contains(s.Status));
        return await query.FirstOrDefaultAsync(ct);
    }

    private static string Describe(TimeSpan delay) =>
        delay.TotalDays >= 1 ? $"{delay.TotalDays:0.#}d"
        : delay.TotalHours >= 1 ? $"{delay.TotalHours:0.#}h"
        : delay.TotalMinutes >= 1 ? $"{delay.TotalMinutes:0.#}m"
        : $"{delay.TotalSeconds:0.#}s";

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
