using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
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

    private readonly IServiceScopeFactory? _scopeFactory;
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
        IOptions<DelegationSettings>? delegation = null,
        IServiceScopeFactory? scopeFactory = null)
    {
        _scopeFactory = scopeFactory;
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
        catch (Exception ex) when (!ct.IsCancellationRequested && new RestartFailurePolicy().Classify(ex) == RestartFailureKind.Infrastructure)
        {
            _logger.LogDebug(ex, "Supervision tick skipped: session runner unreachable");
            return 0;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogTrace(ex, "Supervision runner probe failed non-transport; continuing");
        }

        var agents = await _db.Agents.Where(a => a.AlwaysOn)
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).ToListAsync(ct);
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
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Rollback leaves the failed entities Modified. Detach them so the next
                // AlwaysOn agent's SaveChanges does not retry this agent's bookkeeping.
                _db.ChangeTracker.Clear();
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
        var current = await FindPersistentSessionAsync(agent, statuses: null, ct);
        var escalation = new StandingRestartEscalationPolicy(_settings, _delegation);
        if (state.Suspended || state.LivenessLatchedAt is not null)
            return await CompleteAsync(false);
        if (state.ContinuityHeldAt is not null
            && !escalation.CanReserve(agent, state, current, current?.AgentKind ?? agent.Kind))
        {
            var staged = await TryStageFreshHoldAlertAsync(state, ct);
            var held = await CompleteAsync(false);
            if (staged is not null)
                await RaiseFreshHoldAlertAsync(staged, ct);
            return held;
        }
        if (Guid.TryParse(agent.PersistentSessionId, out var ownedId) && _launchQueue.Owns(ownedId))
            return await CompleteAsync(false);

        var liveSession = await FindPersistentSessionAsync(agent, LiveSessionStatuses, ct);
        if (liveSession is not null)
        {
            // Healthy long enough? Reset the ladder so the next incident starts from 5s again.
            if ((state.RestartBackoffFailures > 0 || state.ConsecutiveFailures > 0 || state.ContinuityResumeFailures > 0
                    || state.AutomaticResumeFailures > 0 || state.LastAutomaticResumeSessionId is not null
                    || state.LastAutomaticResumeStartedAt is not null || state.AutomaticFreshConsumed
                    || state.AutomaticFreshAlertedHoldAt is not null || state.NextRestartAt is not null || state.LastEscalationTier > 0)
                && liveSession.Status == SessionStatus.Running
                && liveSession.InteractiveLaunchCompletedAt is { } completedAt
                && now - completedAt >= TimeSpan.FromMinutes(_settings.HealthyUptimeResetMinutes))
            {
                var failures = state.ConsecutiveFailures;
                state.ConsecutiveFailures = 0;
                state.RestartBackoffFailures = 0;
                state.ContinuityResumeFailures = 0;
                new RestartFailurePolicy().ResetAutomaticEpisode(state);
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
            // Capacity waits also consume the admitted terminal generation before choosing
            // recovery. The shared generation stamp keeps ordinary observation idempotent.
            if (current?.RestartFailureKind == RestartFailureKind.ContinuityUnavailable)
            {
                await new StandingContinuityState(_db, _timeProvider).HoldAsync(agent.Id, current.Id,
                    StandingContinuityReason.NativeSessionMissing, ct);
                return await CompleteAsync(false);
            }
            if (current is not null) new RestartFailurePolicy().Observe(state, current);
            if (await TryHoldRepeatedResumeFailureAsync(agent, state, current?.Id,
                current?.Status, current?.RestartFailureKind, ct))
                return await CompleteAsync(false);
            await CompleteAsync(false);
            await transaction.DisposeAsync();
            try
            {
                return await TryHandleCapacityWaitAsync(agent, state, ct) ?? false;
            }
            catch (ConflictException ex) when (ex.Code is HerdrSupervisionStateService.HeldCode
                or StandingContinuityState.HeldCode)
            {
                // Same observation as the ordinary restart path: a hold that lands
                // during the handoff is not a crash and must not climb the ladder.
                await _herdrSupervision.ObserveAsync(agent.Id, false, false, ct);
                return false;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                return await RecordStartFailureInFreshScopeAsync(agent.Id, ex, now,
                    Math.Min(state.RestartBackoffFailures, int.MaxValue - 1) + 1,
                    current?.Id, current?.StartedAt, ct);
            }
        }

        // Not running. Schedule a restart if none is pending.
        if (state.NextRestartAt is null)
        {
            var dead = await FindPersistentSessionAsync(agent, statuses: null, ct);

            // A previous supervised start that evidently died before reaching healthy uptime
            // counts as a failure — this is what grows the ladder for fast crash-loops.
            if (dead?.RestartFailureKind == RestartFailureKind.ContinuityUnavailable)
            {
                await new StandingContinuityState(_db, _timeProvider).HoldAsync(agent.Id, dead.Id,
                    StandingContinuityReason.NativeSessionMissing, ct);
                return await CompleteAsync(false);
            }
            if (dead is not null)
                new RestartFailurePolicy().Observe(state, dead);

            if (dead is not null && dead.Status == SessionStatus.Failed)
            {
                await RecordIncidentWithExitAsync(
                    agent.Id, dead.Id, AgentIncidentKind.Crash, AlertSeverity.Warning,
                    $"Session died ({SessionExitPolicy.Describe(dead.ExitCode, dead.ExitSignal, dead.TerminationSource,
                        SessionExitEvidence.ReadIntent(dead.ExitStopIntentJson))}: {dead.FailureReason ?? "no reason recorded"}).",
                    dead.ExitCode, dead.FailureReason, ct: ct, exitSignal: dead.ExitSignal);
            }

            if (await TryHoldRepeatedResumeFailureAsync(
                    agent, state, dead?.Id, dead?.Status, dead?.RestartFailureKind, ct))
            {
                await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);
                return await CompleteAsync(false);
            }

            var attempt = Math.Min(state.RestartBackoffFailures, int.MaxValue - 1) + 1;
            var delay = Backoff(state.RestartBackoffFailures);
            state.NextRestartAt = now + delay;
            state.UpdatedAt = now;

            await RecordIncidentWithExitAsync(
                agent.Id, dead?.Id, AgentIncidentKind.RestartScheduled, AlertSeverity.Warning,
                $"Restart attempt {attempt} scheduled for {state.NextRestartAt:u} (backing off {Describe(delay)}).",
                dead?.ExitCode, dead?.FailureReason, ct: ct, exitSignal: dead?.ExitSignal);
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
        var attemptNumber = Math.Min(state.RestartBackoffFailures, int.MaxValue - 1) + 1;
        var beforeAttempt = await FindPersistentSessionAsync(agent, statuses: null, ct);
        var beforeId = beforeAttempt?.Id;
        var beforeStartedAt = beforeAttempt?.StartedAt;
        state.LastAttemptAt = now;
        state.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        // Admission rechecks the bounded exception under the queue/session/agent locks.
        await transaction.CommitAsync(ct);
        await transaction.DisposeAsync();
        // A manual Start may have consumed another outcome while this tick was handing off.
        state = await _herdrSupervision.ObserveAsync(agent.Id, false, false, ct);
        if (state.HerdrFailureHeldAt is not null)
            return false;
        try
        {
            if (await CheckCompactionAdmission.BlocksAutomaticRestartAsync(_db, agent.Id, ct))
                return false;
            _logger.LogInformation(
                "Agent {AgentName}: supervised restart attempt {Attempt}",
                agent.Name, attemptNumber);
            // IgnoreSubscriptionQuota: a supervisor cannot pick another provider, and stopping
            // AlwaysOn restarts on a quota reading is the silent-stop the CARD-0136 gate forbids.
            if (!await StartSupervisedRestartAsync(agent, state, beforeAttempt, capacityActionKey: null, ct))
                return false;

            // Success ⇒ stop scheduling; the failure counter only resets after sustained health.
            // (StartAsync clears supervision state itself for manual semantics; re-load ours.)
            var refreshed = await GetOrCreateStateAsync(agent.Id, ct);
            refreshed.NextRestartAt = null;
            refreshed.UpdatedAt = UtcNow();
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (ConflictException ex) when (ex.Code is HerdrSupervisionStateService.HeldCode or StandingContinuityState.HeldCode)
        {
            await _herdrSupervision.ObserveAsync(agent.Id, false, false, ct);
            return false;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return await RecordStartFailureInFreshScopeAsync(agent.Id, ex, now,
                attemptNumber, beforeId, beforeStartedAt, ct);
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

    private async Task<bool> RecordStartFailureInFreshScopeAsync(Guid agentId, Exception ex, DateTime now,
        int attemptNumber, Guid? beforeId, DateTime? beforeStartedAt, CancellationToken ct)
    {
        // A failed Start may leave its context/transaction unusable. Both handoffs use
        // persisted state; if storage also refuses bookkeeping, the hosted tick retries.
        if (_scopeFactory is not null)
        {
            await using var failureScope = _scopeFactory.CreateAsyncScope();
            return await failureScope.ServiceProvider.GetRequiredService<AgentSupervisorService>()
                .RecordStartFailureAsync(agentId, ex, now, attemptNumber, beforeId, beforeStartedAt, ct);
        }
        _db.ChangeTracker.Clear();
        return await RecordStartFailureAsync(agentId, ex, now, attemptNumber, beforeId, beforeStartedAt, ct);
    }

    private async Task<bool> RecordStartFailureAsync(Guid agentId, Exception ex, DateTime now, int attemptNumber,
        Guid? beforeId, DateTime? beforeStartedAt, CancellationToken ct)
    {
        var agent = await _db.Agents.SingleAsync(a => a.Id == agentId, ct);
        var refreshed = await GetOrCreateStateAsync(agent.Id, ct);
        var current = await FindPersistentSessionAsync(agent, statuses: null, ct);
        if (refreshed.Suspended || refreshed.ContinuityHeldAt is not null
            && !new StandingRestartEscalationPolicy(_settings, _delegation)
                .CanReserve(agent, refreshed, current, current?.AgentKind ?? agent.Kind))
            return false;
        if (Guid.TryParse(agent.PersistentSessionId, out var owned) && _launchQueue.Owns(owned)) return true;
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
                // The wait stays WaitingForHold until the grant. A backoff deadline
                // here would refuse that grant (CARD-0412).
                refreshed.LastAttemptAt = now;
                refreshed.UpdatedAt = UtcNow();
                await _db.SaveChangesAsync(ct);
                return true;
            }

            new RestartFailurePolicy().Charge(refreshed, RestartFailureKind.Unknown);
            var heldDelay = Backoff(refreshed.RestartBackoffFailures);
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

            if (!await TryHoldRepeatedResumeFailureAsync(
                    agent, refreshed, null, SessionStatus.Failed, RestartFailureKind.Unknown, ct))
                await EscalateIfTierCrossedAsync(agent, refreshed, heldDelay, ct);
        }
        else
        {
            var kind = new RestartFailurePolicy().Classify(ex);
            var incarnation = await FindPersistentSessionAsync(agent, statuses: null, ct);
            if (incarnation is not null && (incarnation.Id != beforeId || incarnation.StartedAt != beforeStartedAt))
                new RestartFailurePolicy().ConsumeGeneration(refreshed, incarnation, kind);
            else
                new RestartFailurePolicy().Charge(refreshed, kind);
            var delay = Backoff(refreshed.RestartBackoffFailures);
            refreshed.NextRestartAt = UtcNow() + delay;
            refreshed.LastAttemptAt = now;
            refreshed.UpdatedAt = UtcNow();
            await RecordIncidentAsync(
                agent.Id, null, AgentIncidentKind.StartFailure, AlertSeverity.Error,
                $"Start attempt {attemptNumber} failed: {ex.Message} — next retry {refreshed.NextRestartAt:u} (backing off {Describe(delay)}).",
                ct: ct);
            if (!await TryHoldRepeatedResumeFailureAsync(
                    agent, refreshed, incarnation?.Id ?? beforeId, incarnation?.Status, incarnation?.RestartFailureKind ?? kind, ct))
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
        if (await CheckCompactionAdmission.BlocksAutomaticRestartAsync(_db, agent.Id, ct))
            return false;
        return await StartSupervisedRestartAsync(agent, state,
            await FindPersistentSessionAsync(agent, statuses: null, ct), wait.ActionKey, ct);
    }

    private async Task<bool> StartSupervisedRestartAsync(
        Agent agent, AgentSupervisionState state, AgentSession? current, string? capacityActionKey, CancellationToken ct)
    {
        var policy = new StandingRestartEscalationPolicy(_settings, _delegation);
        if (!policy.CanReserve(agent, state, current, current?.AgentKind ?? agent.Kind))
        {
            await _control.StartAsync(agent.Id, new StartAgentRequest(Fresh: false,
                IgnoreSubscriptionQuota: !StandingSpecialistSeatPolicy.IsAlternate(agent),
                CapacityRecovery: capacityActionKey is not null), ct, automatic: true);
            return true;
        }

        try
        {
            await _control.StartSupervisedFreshAsync(agent.Id, current!.Id, current.StartedAt, capacityActionKey, ct);
            await TryRaiseFreshStartedAlertAsync(agent.Id, ct);
            return true;
        }
        catch (ConflictException ex) when (!ct.IsCancellationRequested && ex.Code is
            "standing_resume_delivery_pending" or "standing_resume_work_in_flight"
            or "standing_resume_card_work_pending" or "standing_resume_not_owned"
            or "standing_resume_owner_unproven" or "standing_resume_ineligible"
            or "standing_resume_unsupported" or "standing_resume_incompatible")
        {
            // Only durable refusals stop both handoffs. Infrastructure/model failures
            // propagate to retry/capacity bookkeeping, even under a repeated-resume hold.
            // Read persisted state in a fresh scope: reservation rollback can leave tracked
            // allowance/pointer changes behind, and a concurrent human Start may have won.
            bool held;
            if (_scopeFactory is not null)
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                held = await scope.ServiceProvider.GetRequiredService<AgentSupervisorService>()
                    .HoldRefusedFreshAsync(agent.Id, current!.Id, current.StartedAt, ct);
            }
            else
            {
                _db.ChangeTracker.Clear();
                held = await HoldRefusedFreshAsync(agent.Id, current!.Id, current.StartedAt, ct);
            }
            if (!held) throw;
            _logger.LogWarning(ex, "Agent {AgentName}: automatic fresh reservation refused; continuity held", agent.Name);
            return false;
        }
    }

    private async Task<bool> HoldRefusedFreshAsync(
        Guid agentId, Guid expectedSessionId, DateTime expectedStartedAt, CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        var state = await _herdrSupervision.ObserveAsync(agentId, false, false, ct);
        var agent = await _db.Agents.SingleAsync(a => a.Id == agentId, ct);
        var current = await FindPersistentSessionAsync(agent, statuses: null, ct);
        if (current?.Id != expectedSessionId || current.StartedAt != expectedStartedAt
            || !new StandingRestartEscalationPolicy(_settings, _delegation)
                .CanReserve(agent, state, current, current.AgentKind))
            return false;

        // Keep the unused allowance, but distinguish refusal from the repeated-resume hold
        // which permits one reservation. Only committed acceptance clears this decision.
        await new StandingContinuityState(_db, _timeProvider).HoldAsync(agentId, current.Id,
            StandingContinuityReason.AutomaticFreshRefused,
            "Automatic fresh reservation was refused before commit; inspect pending input and work before explicit recovery.", ct);
        var heldState = await _db.AgentSupervisionStates.SingleAsync(s => s.AgentId == agentId, ct);
        var staged = await TryStageFreshHoldAlertAsync(heldState, ct);
        await transaction.CommitAsync(ct);
        await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agentId), ct);
        if (staged is not null)
            await RaiseFreshHoldAlertAsync(staged, ct);
        return true;
    }

    private sealed record FreshHoldAlert(Guid AgentId, Guid? SessionId, string Detail, DateTime HeldAt);

    /// <summary>
    /// Stages one refused/exhausted fresh incident and the hold marker inside the open
    /// supervision transaction. A failed write rolls back only that savepoint.
    /// </summary>
    private async Task<FreshHoldAlert?> TryStageFreshHoldAlertAsync(AgentSupervisionState state, CancellationToken ct)
    {
        if (state.ContinuityHeldAt is not { } heldAt || state.AutomaticFreshAlertedHoldAt == heldAt)
            return null;
        var refused = state.ContinuityReason == StandingContinuityReason.AutomaticFreshRefused;
        var exhausted = state.AutomaticFreshConsumed
            && state.ContinuityReason == StandingContinuityReason.RepeatedResumeFailure;
        if (!refused && !exhausted)
            return null;
        var transaction = _db.Database.CurrentTransaction;
        if (transaction is null || !transaction.SupportsSavepoints)
        {
            _logger.LogWarning("Agent {AgentId}: fresh-hold alert skipped; no savepoint in the supervision transaction", state.AgentId);
            return null;
        }

        try
        {
            await transaction.CreateSavepointAsync("c510_fresh_alert", ct);
            var queues = "none";
            if (state.ContinuitySessionId is Guid sessionId)
            {
                var ids = await _db.SessionQueuedMessages.AsNoTracking()
                    .Where(m => m.AgentSessionId == sessionId
                        && m.Status == QueuedMessageStatus.Pending
                        && m.RulesRefreshKey == null)
                    .OrderBy(m => m.Sequence).ThenBy(m => m.Id)
                    .Select(m => m.Id)
                    .ToListAsync(ct);
                if (ids.Count > 0)
                    queues = string.Join(",", ids.Select(id => id.ToString("D")));
            }

            var session = state.ContinuitySessionId?.ToString("D") ?? "none";
            var reason = state.ContinuityReason?.ToString() ?? "none";
            var message = ColumnText.Clip(
                $"StandingAutomaticFreshRefused agent {state.AgentId:D} session {session} queues {queues} reason {reason}; prior conversation retained.",
                AgentIncident.MessageMaxLength);
            _db.AgentIncidents.Add(new AgentIncident
            {
                Id = Guid.NewGuid(),
                AgentId = state.AgentId,
                SessionId = state.ContinuitySessionId,
                Kind = AgentIncidentKind.StandingAutomaticFreshRefused,
                Severity = AlertSeverity.Error,
                Message = message,
                CreatedAt = UtcNow(),
            });
            state.AutomaticFreshAlertedHoldAt = heldAt;
            state.UpdatedAt = UtcNow();
            await _db.SaveChangesAsync(ct);
            return new FreshHoldAlert(state.AgentId, state.ContinuitySessionId, message, heldAt);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent {AgentId}: fresh-hold alert incident was not stored; the hold remains", state.AgentId);
            await ForgetFreshHoldAlertAttemptAsync(transaction, state, ct);
            return null;
        }
    }

    private async Task ForgetFreshHoldAlertAttemptAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        AgentSupervisionState state, CancellationToken ct)
    {
        try
        {
            await transaction.RollbackToSavepointAsync("c510_fresh_alert", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Agent {AgentId}: fresh-hold alert savepoint rollback failed", state.AgentId);
        }

        foreach (var entry in _db.ChangeTracker.Entries<AgentIncident>()
            .Where(entry => entry.State == EntityState.Added).ToList())
            entry.State = EntityState.Detached;
        if (_db.Entry(state).State == EntityState.Detached)
            return;
        try
        {
            await _db.Entry(state).ReloadAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Agent {AgentId}: fresh-hold state reload failed", state.AgentId);
        }
    }

    private async Task RaiseFreshHoldAlertAsync(FreshHoldAlert alert, CancellationToken ct)
    {
        try
        {
            await _alerts.RaiseAsync(
                new AlertRaise(
                    AlertSeverity.Error,
                    Source: "supervisor",
                    Title: "StandingAutomaticFreshRefused: agent supervision",
                    Detail: alert.Detail,
                    DedupKey: $"supervisor:StandingAutomaticFreshRefused:{alert.AgentId:N}:{alert.HeldAt:O}",
                    AgentId: alert.AgentId,
                    SessionId: alert.SessionId),
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent {AgentId}: fresh-hold alert routing failed", alert.AgentId);
        }
    }

    private async Task TryRaiseFreshStartedAlertAsync(Guid agentId, CancellationToken ct)
    {
        try
        {
            var incident = await _db.AgentIncidents.AsNoTracking()
                .Where(i => i.AgentId == agentId && i.Kind == AgentIncidentKind.StandingAutomaticFreshStarted)
                .OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id)
                .FirstOrDefaultAsync(ct);
            if (incident is null)
                return;
            // Alerts stay for the retention window. Match this reservation's incident only,
            // so a later episode still notifies and a replay of this one does not.
            var dedupKey = $"supervisor:StandingAutomaticFreshStarted:{agentId:N}:{incident.Id:N}";
            var already = await _db.Alerts.AsNoTracking().AnyAsync(
                a => a.AgentId == agentId && a.DedupKey == dedupKey, ct);
            if (already)
                return;
            await _alerts.RaiseAsync(
                new AlertRaise(
                    AlertSeverity.Warning,
                    Source: "supervisor",
                    Title: "StandingAutomaticFreshStarted: agent supervision",
                    Detail: incident.Message,
                    DedupKey: dedupKey,
                    AgentId: agentId,
                    SessionId: incident.SessionId),
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent {AgentId}: fresh-start alert routing failed", agentId);
        }
    }

    private async Task<bool> TryHoldRepeatedResumeFailureAsync(
        Agent agent,
        AgentSupervisionState state,
        Guid? sessionId,
        SessionStatus? lastStatus,
        RestartFailureKind? lastKind,
        CancellationToken ct)
    {
        var threshold = _settings.ResumeFailureHoldAttempts;
        if (threshold <= 0 || state.ContinuityResumeFailures < threshold || state.ContinuityHeldAt is not null)
            return false;

        var detail =
            $"{state.ContinuityResumeFailures} consecutive supervised resumes ended before healthy uptime; last outcome {lastStatus}/{lastKind}; see the Crash incidents on this agent.";
        await new StandingContinuityState(_db, _timeProvider).HoldAsync(
            agent.Id, sessionId, StandingContinuityReason.RepeatedResumeFailure, detail, ct);
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
            $"Backoff escalated: {state.RestartBackoffFailures} consecutive failures; now retrying on a {cadence} cadence (current delay {Describe(delay)}).",
            ct: ct);
        _logger.LogError(
            "Agent {AgentName}: supervision backoff escalated to {Cadence} after {Failures} consecutive failures",
            agent.Name, cadence, state.RestartBackoffFailures);
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

    public Task RecordIncidentAsync(
        Guid? agentId, Guid? sessionId, AgentIncidentKind kind, AlertSeverity severity, string message,
        int? exitCode = null, string? failureReason = null, bool raiseAlert = true, CancellationToken ct = default) =>
        RecordIncidentWithExitAsync(agentId, sessionId, kind, severity, message, exitCode, failureReason,
            raiseAlert, exitSignal: null, ct: ct);

    private async Task RecordIncidentWithExitAsync(
        Guid? agentId,
        Guid? sessionId,
        AgentIncidentKind kind,
        AlertSeverity severity,
        string message,
        int? exitCode = null,
        string? failureReason = null,
        bool raiseAlert = true,
        int? exitSignal = null,
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
            ExitSignal = exitSignal,
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

    /// <summary>
    /// Nightly-ish hygiene: incidents past retention or beyond the per-agent cap.
    ///
    /// <para>CARD-1156 D-5: the receipts of a CURRENT unresolved standing boot episode are the boot
    /// sweep's dedup evidence, so they are exempt from both deletes until a positive fact resolves
    /// the episode (<see cref="StandingBootAttentionProjection.CurrentEpisodeKeysAsync"/>). When those
    /// keys cannot be read, every <c>standingBoot:v1;</c> receipt is retained this pass: uncertainty
    /// never deletes dedup evidence. Nothing here touches restart or counter logic.</para>
    /// </summary>
    public async Task<int> PruneIncidentsAsync(CancellationToken ct)
    {
        IReadOnlyList<string> protectedKeys = [];
        var retainAllStanding = false;
        try
        {
            protectedKeys = await StandingBootAttentionProjection.CurrentEpisodeKeysAsync(_db, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            retainAllStanding = true;
            _logger.LogWarning(
                ex, "Could not read the current standing boot episodes; every {Prefix} receipt is retained this prune",
                StandingBootWatchPolicy.KeyPrefix);
        }

        var prunable = retainAllStanding
            ? _db.AgentIncidents.Where(i => i.FailureReason == null
                || !i.FailureReason.StartsWith(StandingBootWatchPolicy.KeyPrefix))
            : _db.AgentIncidents.Where(i => i.FailureReason == null || !protectedKeys.Contains(i.FailureReason));

        var cutoff = UtcNow().AddDays(-_settings.IncidentRetentionDays);
        var removed = await prunable.Where(i => i.CreatedAt < cutoff).ExecuteDeleteAsync(ct);

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
            removed += await prunable
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
