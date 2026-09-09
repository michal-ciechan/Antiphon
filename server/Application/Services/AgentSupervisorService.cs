using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
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
/// at boot, restarting it after crashes on a backoff ladder (doubling to a
/// 30-day cap), recording every decision as an <see cref="AgentIncident"/> with the attempt
/// number, chosen delay, and absolute next-retry time, and firing tier-escalation incidents
/// exactly once when the ladder crosses hourly (Warning) and daily (Critical).
/// Repeated qualifying Herdr failures instead hold future starts until explicit operator retry.
/// </summary>
public sealed class AgentSupervisorService : IAgentIncidentRecorder
{
    private static readonly SessionStatus[] LiveSessionStatuses =
        [SessionStatus.Starting, SessionStatus.Running, SessionStatus.Stopping];

    private static readonly TimeSpan HourlyTier = TimeSpan.FromHours(1);
    private static readonly TimeSpan DailyTier = TimeSpan.FromDays(1);

    private readonly AppDbContext _db;
    private readonly AgentControlService _control;
    private readonly ISessionRunnerClient _runnerClient;
    private readonly IEventBus _eventBus;
    private readonly IAlertService _alerts;
    private readonly SupervisionSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AgentSupervisorService> _logger;
    private readonly AgentSessionLaunchQueue _launchQueue;
    private readonly HerdrSupervisionStateService _herdrSupervision;
    private readonly CapacityRecoveryService? _capacityRecovery;
    private readonly DelegationSettings _delegation;

    public AgentSupervisorService(
        AppDbContext db,
        AgentControlService control,
        ISessionRunnerClient runnerClient,
        IEventBus eventBus,
        IAlertService alerts,
        IOptions<SupervisionSettings> settings,
        TimeProvider timeProvider,
        ILogger<AgentSupervisorService> logger,
        AgentSessionLaunchQueue launchQueue,
        HerdrSupervisionStateService? herdrSupervision = null,
        CapacityRecoveryService? capacityRecovery = null,
        IOptions<DelegationSettings>? delegation = null)
    {
        _db = db;
        _control = control;
        _runnerClient = runnerClient;
        _eventBus = eventBus;
        _alerts = alerts;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _launchQueue = launchQueue;
        _herdrSupervision = herdrSupervision ?? new HerdrSupervisionStateService(db, settings, timeProvider, launchQueue, eventBus);
        _capacityRecovery = capacityRecovery;
        _delegation = delegation?.Value ?? new();
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
        if (await StandingSpecialistSeatPolicy.StartRefusalAsync(_db, agent, _delegation, automatic: true, ct) is not null)
            return false;
        // Keep the scheduling decision and evidence consumption under the same agent lock.
        // Commit before Start: composition and runner RPCs never run under this transaction.
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        var now = UtcNow();
        var previouslyHeld = await _db.AgentSupervisionStates.AsNoTracking()
            .Where(s => s.AgentId == agent.Id).Select(s => s.HerdrFailureHeldAt).FirstOrDefaultAsync(ct);
        var state = await _herdrSupervision.ObserveAsync(agent.Id, false, true, ct);
        await _db.Entry(agent).ReloadAsync(ct);
        if (state.HerdrFailureHeldAt is not null)
        {
            // T-5: retain the terminal observation on the tripping tick, without scheduling.
            if (previouslyHeld is null && state.LastHerdrObservedSessionId is { } failedId
                && !await _db.AgentIncidents.AnyAsync(i => i.AgentId == agent.Id
                    && i.Kind == AgentIncidentKind.Crash && i.SessionId == failedId
                    && i.CreatedAt >= state.LastHerdrObservedStartedAt, ct))
            {
                await RecordIncidentAsync(agent.Id, failedId, AgentIncidentKind.Crash, AlertSeverity.Warning,
                    $"Session died ({state.LastHerdrFailureKind}); Herdr retries paused.", ct: ct);
            }
            return await CompleteAsync(false);
        }
        if (state.Suspended)
            return await CompleteAsync(false);
        if (Guid.TryParse(agent.PersistentSessionId, out var ownedId) && _launchQueue.Owns(ownedId))
            return await CompleteAsync(false);

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

            return await CompleteAsync(false);
        }

        // A provider redemption takes the global/provider locks. Release the consumer lock
        // before entering that lane, and before any launch I/O. The redemption and Start each
        // recheck ownership; this observation itself grants no authority.
        if (_capacityRecovery is { IsEnabled: true } && await _db.CapacityRecoveryWaits.AnyAsync(
            w => (w.AgentId == agent.Id || w.ConsumerKey == $"agent:{agent.Id:N}")
                && w.State != CapacityRecoveryWaitState.Progressed && w.State != CapacityRecoveryWaitState.Canceled
                && w.State != CapacityRecoveryWaitState.Superseded && w.State != CapacityRecoveryWaitState.Exhausted, ct))
        {
            await CompleteAsync(false);
            await transaction.DisposeAsync();
            return await TryHandleCapacityWaitAsync(agent, state, ct) ?? false;
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
                    dead.ExitCode, dead.FailureReason, ct: ct);
            }

            await RecordIncidentAsync(
                agent.Id, dead?.Id, AgentIncidentKind.RestartScheduled, AlertSeverity.Warning,
                $"Restart attempt {attempt} scheduled for {state.NextRestartAt:u} (backing off {Describe(delay)}).",
                dead?.ExitCode, dead?.FailureReason, ct: ct);
            await EscalateIfTierCrossedAsync(agent, state, delay, ct);

            _logger.LogWarning(
                "Agent {AgentName}: not running; restart attempt {Attempt} scheduled for {NextRestartAt:u} (backoff {Delay})",
                agent.Name, attempt, state.NextRestartAt, Describe(delay));

            await _db.SaveChangesAsync(ct);
            await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);
            return await CompleteAsync(true);
        }

        if (now < state.NextRestartAt)
            return await CompleteAsync(false);

        // Due: attempt the restart.
        var attemptNumber = state.ConsecutiveFailures + 1;
        state.LastAttemptAt = now;
        state.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        var fresh = state.ConsecutiveFailures >= _settings.FreshAfterResumeFailures;
        await transaction.CommitAsync(ct);
        await transaction.DisposeAsync();
        // A manual Start may have consumed another outcome while this tick was handing off.
        state = await _herdrSupervision.ObserveAsync(agent.Id, false, false, ct);
        if (state.HerdrFailureHeldAt is not null)
            return false;
        try
        {
            _logger.LogInformation(
                "Agent {AgentName}: supervised restart attempt {Attempt} ({Mode})",
                agent.Name, attemptNumber, fresh ? "fresh conversation" : "resume");
            // IgnoreSubscriptionQuota: a supervisor cannot pick another provider, and stopping
            // AlwaysOn restarts on a quota reading is the silent-stop the CARD-0136 gate forbids.
            await _control.StartAsync(
                agent.Id, new StartAgentRequest(Fresh: fresh, IgnoreSubscriptionQuota: !StandingSpecialistSeatPolicy.IsAlternate(agent)), ct, automatic: true);

            // Success ⇒ stop scheduling; the failure counter only resets after sustained health.
            // (StartAsync clears supervision state itself for manual semantics; re-load ours.)
            var refreshed = await GetOrCreateStateAsync(agent.Id, ct);
            refreshed.NextRestartAt = null;
            refreshed.UpdatedAt = UtcNow();
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (ConflictException ex) when (ex.Code == HerdrSupervisionStateService.HeldCode)
        {
            await _herdrSupervision.ObserveAsync(agent.Id, false, false, ct);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var refreshed = await GetOrCreateStateAsync(agent.Id, ct);
            if (ex is ModelDisabledException held)
            {
                if (_capacityRecovery is not null)
                {
                    await _capacityRecovery.EnsureWaitOnAsync(_db, new CapacityWaitRegistration
                    {
                        ConsumerKey = $"agent:{agent.Id:N}",
                        ConsumerKind = CapacityWaitConsumerKind.StandingStart,
                        ExecutionKind = agent.Kind,
                        RequestedKind = agent.Kind,
                        RequestedAlias = held.Hold.ModelAlias,
                        AgentId = agent.Id,
                        HoldId = held.Hold.Id,
                        HoldRevision = held.Hold.Revision,
                        BlockedAt = UtcNow(),
                    }, ct);
                    refreshed.UpdatedAt = UtcNow();
                    await _db.SaveChangesAsync(ct);
                    return true;
                }

                refreshed.ConsecutiveFailures++;
                var heldDelay = Backoff(refreshed.ConsecutiveFailures);
                refreshed.NextRestartAt = UtcNow() + heldDelay;
                refreshed.LastAttemptAt = now;
                refreshed.UpdatedAt = UtcNow();
                var holdKey = held.Hold.Id.ToString("D");
                var already = await _db.AgentIncidents.AsNoTracking().AnyAsync(
                    i => i.AgentId == agent.Id
                        && i.Kind == AgentIncidentKind.StartFailure
                        && i.FailureReason == holdKey, ct);
                if (!already)
                {
                    await RecordIncidentAsync(
                        agent.Id, null, AgentIncidentKind.StartFailure, AlertSeverity.Error,
                        $"held: {held.Hold.ModelAlias} is disabled (per-model cap); no fallback declared — next retry {refreshed.NextRestartAt:u} (backing off {Describe(heldDelay)}).",
                        failureReason: holdKey,
                        ct: ct);
                }

                await EscalateIfTierCrossedAsync(agent, refreshed, heldDelay, ct);
            }
            else
            {
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
            }

            _logger.LogWarning(ex,
                "Agent {AgentName}: start attempt {Attempt} failed; next retry {NextRestartAt:u} (backoff {Delay})",
                agent.Name, attemptNumber, refreshed.NextRestartAt,
                refreshed.NextRestartAt is { } next ? Describe(next - UtcNow()) : "none");

            await _db.SaveChangesAsync(ct);
            await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);
            return true;
        }

        async Task<bool> CompleteAsync(bool result)
        {
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            if (previouslyHeld != state.HerdrFailureHeldAt)
                await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);
            return result;
        }
    }

    /// <summary>
    /// CARD-0412: a capacity-waiting dead session skips the NextRestartAt-null crash ladder
    /// and only starts when the sole granter has issued a matching grant.
    /// </summary>
    private async Task<bool?> TryHandleCapacityWaitAsync(
        Agent agent, AgentSupervisionState state, CancellationToken ct)
    {
        if (_capacityRecovery is null || !_capacityRecovery.IsEnabled)
            return null;

        var wait = await _db.CapacityRecoveryWaits.FirstOrDefaultAsync(
            w => w.AgentId == agent.Id
                && w.State != CapacityRecoveryWaitState.Progressed
                && w.State != CapacityRecoveryWaitState.Canceled
                && w.State != CapacityRecoveryWaitState.Superseded
                && w.State != CapacityRecoveryWaitState.Exhausted,
            ct);
        if (wait is null)
        {
            wait = await _db.CapacityRecoveryWaits.FirstOrDefaultAsync(
                w => w.ConsumerKey == $"agent:{agent.Id:N}"
                    && w.State != CapacityRecoveryWaitState.Progressed
                    && w.State != CapacityRecoveryWaitState.Canceled
                    && w.State != CapacityRecoveryWaitState.Superseded
                    && w.State != CapacityRecoveryWaitState.Exhausted,
                ct);
        }

        if (wait is null)
            return null;

        if (wait.State == CapacityRecoveryWaitState.WaitingForHold)
            return false;

        if (!CapacityRecoveryPolicy.IsGrantCandidate(wait) && wait.State != CapacityRecoveryWaitState.ActionPending)
            return false;

        var provider = await _db.Set<CapacityRecoveryProviderState>()
            .FirstOrDefaultAsync(s => s.Kind == wait.ExecutionKind, ct);
        if (provider is null
            || provider.GrantedWaitId != wait.Id
            || provider.GrantedActionKey != wait.ActionKey)
            return false;

        var redemption = await _capacityRecovery.RedeemAsync(
            wait.Id, wait.ActionKey, wait.ExecutionKind, CapacityRedemptionPath.Start, ct);
        if (!redemption.Ok)
            return false;

        if (Guid.TryParse(agent.PersistentSessionId, out var sessionId))
        {
            var session = await _db.AgentSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
            if (session is not null && session.CapacityRecoveryActionKey == wait.ActionKey)
                return false;
            if (session is not null)
                session.CapacityRecoveryActionKey = wait.ActionKey;
        }

        state.CapacityRecoveryActionKey = wait.ActionKey;
        await _db.SaveChangesAsync(ct);
        await _control.StartAsync(
            agent.Id,
            new StartAgentRequest(Fresh: false, IgnoreSubscriptionQuota: !StandingSpecialistSeatPolicy.IsAlternate(agent), CapacityRecovery: true),
            ct, automatic: true);
        return true;
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
        if (state is not null && _db.Entry(state).State != EntityState.Added)
            await _db.Entry(state).ReloadAsync(ct);
        if (state is null)
        {
            state = new AgentSupervisionState { AgentId = agentId, UpdatedAt = UtcNow() };
            _db.AgentSupervisionStates.Add(state);
        }

        return state;
    }

    public async Task RecordIncidentAsync(
        Guid? agentId,
        Guid? sessionId,
        AgentIncidentKind kind,
        AlertSeverity severity,
        string message,
        int? exitCode = null,
        string? failureReason = null,
        bool raiseAlert = true,
        CancellationToken ct = default)
    {
        _db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            AgentId = agentId,
            SessionId = sessionId,
            Kind = kind,
            Severity = severity,
            // Clipped at the shared writer (CARD-0205): callers compose these from transcript text,
            // process output and exception messages, none of which is bounded, and an incident row
            // that overflows its column takes the alert below down with it — they share this
            // context's SaveChanges.
            Message = ColumnText.Clip(message, AgentIncident.MessageMaxLength),
            ExitCode = exitCode,
            FailureReason = ColumnText.ClipOrNull(failureReason, AgentIncident.FailureReasonMaxLength),
            CreatedAt = UtcNow(),
        });

        // Incidents ARE the supervisor's alerts (1:1 by default): same severity, deduped per
        // agent+kind so the routing throttle can group repeats. raiseAlert=false is for incidents
        // that record normal operation (e.g. a context compaction) — timeline row, no alert.
        if (!raiseAlert)
            return;

        var subject = agentId is Guid id ? id.ToString("D") : sessionId?.ToString("D") ?? "unclaimed";
        await _alerts.RaiseAsync(
            new AlertRaise(
                severity,
                Source: "supervisor",
                Title: $"{kind}: agent supervision",
                Detail: message,
                DedupKey: $"supervisor:{kind}:{subject}",
                AgentId: agentId,
                SessionId: sessionId),
            ct);
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
