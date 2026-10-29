using System.Diagnostics;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Antiphon.Server.Application.Services;

public sealed partial class CapacityRecoveryService
{
    private int DeliveryAttemptCap =>
        Math.Max(1, _supervision.DeliveryVerification.MaxDeliveryAttempts);

    /// <summary>
    /// Retire a canceled or parked queue selection without ending the wait, and supersede a
    /// standing wait whose accepted generation was replaced. Bounded per path. Busy locks defer.
    /// </summary>
    private async Task ReconcileObsoleteConsumersAsync(CancellationToken ct)
    {
        await ReconcileObsoleteQueueSelectionsAsync(ct);
        await ReconcileObsoleteStandingConsumersAsync(ct);
    }

    private async Task ReconcileObsoleteQueueSelectionsAsync(CancellationToken ct)
    {
        var started = Stopwatch.StartNew();
        var batch = Math.Clamp(Settings.ReconciliationBatchSize, 1, 1000);
        var cap = DeliveryAttemptCap;
        List<QueueCleanupCandidate> candidates;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            candidates = await DiscoverObsoleteQueueSelectionsAsync(db, batch, cap, ct);
        }

        var detached = 0;
        var deferred = 0;
        var busy = 0;
        long gateHoldMs = 0;
        if (candidates.Count == 0)
        {
            LogQueueCleanup(0, 0, 0, 0, started.ElapsedMilliseconds, 0);
            return;
        }

        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<SessionMessageQueueService>();
            foreach (var candidate in candidates)
            {
                ct.ThrowIfCancellationRequested();
                if (_landBoundary is not null)
                    await _landBoundary.ReachedAsync(
                        "capacity-obsolete-after-discovery", candidate.WaitId, candidate.MessageId, ct);

                var gate = queue.GetLock(candidate.SessionId);
                if (!await gate.WaitAsync(0, ct))
                {
                    busy++;
                    continue;
                }

                var held = Stopwatch.StartNew();
                try
                {
                    var outcome = await DetachObsoleteSelectionAsync(candidate, cap, ct);
                    if (outcome == CleanupOutcome.Detached)
                        detached++;
                    else if (outcome == CleanupOutcome.Busy)
                        busy++;
                    else
                        deferred++;
                }
                finally
                {
                    gateHoldMs += held.ElapsedMilliseconds;
                    gate.Release();
                }
            }
        }

        LogQueueCleanup(candidates.Count, detached, deferred, busy, started.ElapsedMilliseconds, gateHoldMs);
    }

    private async Task<CleanupOutcome> DetachObsoleteSelectionAsync(
        QueueCleanupCandidate candidate, int cap, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (!await TryLockMessageNoWaitAsync(db, candidate.MessageId, ct))
            {
                await tx.RollbackAsync(ct);
                return CleanupOutcome.Busy;
            }

            var message = await db.SessionQueuedMessages.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == candidate.MessageId, ct);
            if (!MessageIsObsolete(message, candidate.WaitId, candidate.ActionKey, candidate.SessionId, cap))
            {
                await tx.RollbackAsync(ct);
                return CleanupOutcome.Deferred;
            }

            var reason = message!.Status == QueuedMessageStatus.Canceled
                ? "selected-message-canceled"
                : "selected-message-parked";
            var cleared = await db.SessionQueuedMessages
                .Where(m => m.Id == message.Id
                    && m.CapacityWaitId == candidate.WaitId
                    && m.CapacityRecoveryActionKey == candidate.ActionKey
                    && m.AgentSessionId == candidate.SessionId
                    && (m.Origin == QueuedMessageOrigin.Channel || m.Origin == QueuedMessageOrigin.Scheduled)
                    && (m.Status == QueuedMessageStatus.Canceled
                        || (m.Status == QueuedMessageStatus.Pending && m.DeliveryAttempts >= cap)))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.CapacityWaitId, (Guid?)null)
                    .SetProperty(m => m.CapacityWaitVersion, (int?)null)
                    .SetProperty(m => m.CapacityRecoveryActionKey, (string?)null), ct);
            if (cleared != 1)
            {
                await tx.RollbackAsync(ct);
                return CleanupOutcome.Deferred;
            }

            if (!await TryTakeProviderLockAsync(db, candidate.Kind, ct))
            {
                await tx.RollbackAsync(ct);
                return CleanupOutcome.Busy;
            }

            var wait = await db.Set<CapacityRecoveryWait>().AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == candidate.WaitId, ct);
            if (wait is null || IsFinished(wait) || wait.Version != candidate.Version
                || wait.ActionKey != candidate.ActionKey || wait.SelectedMessageId != candidate.MessageId
                || HasIndependentConsumer(wait) || await HasRedeemableTaskAsync(db, wait, ct))
            {
                await tx.RollbackAsync(ct);
                return CleanupOutcome.Deferred;
            }

            if (_landBoundary is not null)
                await _landBoundary.ReachedAsync(
                    "capacity-obsolete-before-finish", wait.Id, candidate.MessageId, ct);

            wait = await db.Set<CapacityRecoveryWait>().AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == candidate.WaitId, ct);
            if (wait is null || IsFinished(wait) || wait.Version != candidate.Version
                || wait.ActionKey != candidate.ActionKey || wait.SelectedMessageId != candidate.MessageId
                || HasIndependentConsumer(wait))
            {
                await tx.RollbackAsync(ct);
                return CleanupOutcome.Deferred;
            }

            var now = UtcNow();
            var nextOrdinal = wait.ActionOrdinal + 1;
            var nextKey = CapacityRecoveryPolicy.ActionKey(wait.Id, nextOrdinal);
            var moveToReady = wait.State is CapacityRecoveryWaitState.ActionPending
                    or CapacityRecoveryWaitState.Admitted
                    or CapacityRecoveryWaitState.Ready
                || (wait.State == CapacityRecoveryWaitState.Deferred && wait.OutcomeReason == "grant-revoked");
            var nextState = moveToReady ? CapacityRecoveryWaitState.Ready : wait.State;
            var eligible = CapacityRecoveryPolicy.EligibleAt(
                wait.LatestClearObservedAt ?? wait.BlockedAt, wait.Id, nextOrdinal, Settings.JitterSeconds);
            var due = moveToReady
                ? wait.DueAt is { } oldDue && oldDue > eligible ? oldDue : eligible
                : wait.DueAt;
            var updated = await db.Set<CapacityRecoveryWait>()
                .Where(w => w.Id == wait.Id
                    && w.Version == candidate.Version
                    && w.ActionKey == candidate.ActionKey
                    && w.SelectedMessageId == candidate.MessageId
                    && w.State == wait.State)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.SelectedMessageId, (Guid?)null)
                    .SetProperty(w => w.NeedsRevalidationGrant, false)
                    .SetProperty(w => w.ActionOrdinal, nextOrdinal)
                    .SetProperty(w => w.ActionKey, nextKey)
                    .SetProperty(w => w.Version, wait.Version + 1)
                    .SetProperty(w => w.UpdatedAt, now)
                    .SetProperty(w => w.State, nextState)
                    .SetProperty(w => w.Outcome, nextState.ToString())
                    .SetProperty(w => w.OutcomeReason, reason)
                    .SetProperty(w => w.DueAt, due), ct);
            if (updated != 1)
            {
                await tx.RollbackAsync(ct);
                return CleanupOutcome.Deferred;
            }

            var provider = await db.Set<CapacityRecoveryProviderState>()
                .FirstOrDefaultAsync(s => s.Kind == candidate.Kind, ct);
            if (provider is { GrantedWaitId: { } granted, GrantedActionKey: { } grantedKey }
                && granted == wait.Id && grantedKey == candidate.ActionKey)
                ClearGrant(provider, now);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            _logger.LogInformation(
                "Capacity obsolete selection detached wait {WaitId} message {MessageId} action {ActionKey} reason {Reason}",
                wait.Id, candidate.MessageId, candidate.ActionKey, reason);
            return CleanupOutcome.Detached;
        }
        catch (Exception ex) when (IsLockNotAvailable(ex))
        {
            await tx.RollbackAsync(ct);
            return CleanupOutcome.Busy;
        }
    }

    private async Task ReconcileObsoleteStandingConsumersAsync(CancellationToken ct)
    {
        var started = Stopwatch.StartNew();
        var batch = Math.Clamp(Settings.ReconciliationBatchSize, 1, 1000);
        List<StandingCleanupCandidate> candidates;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            candidates = await DiscoverReplacedStandingConsumersAsync(db, batch, ct);
        }

        var superseded = 0;
        var deferred = 0;
        var busy = 0;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (_landBoundary is not null)
                await _landBoundary.ReachedAsync(
                    "capacity-obsolete-after-discovery", candidate.WaitId, Guid.Empty, ct);
            var outcome = await SupersedeStandingConsumerAsync(candidate, ct);
            if (outcome == CleanupOutcome.Detached)
                superseded++;
            else if (outcome == CleanupOutcome.Busy)
                busy++;
            else
                deferred++;
        }

        _logger.LogInformation(
            "Capacity obsolete standing scanned {Scanned} superseded {Superseded} deferred {Deferred} busy {Busy} elapsed {ElapsedMs}",
            candidates.Count, superseded, deferred, busy, started.ElapsedMilliseconds);
    }

    private async Task<CleanupOutcome> SupersedeStandingConsumerAsync(
        StandingCleanupCandidate candidate, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (!await TryLockRowNoWaitAsync(db, "AgentSessions", candidate.SessionId, ct))
            {
                await tx.RollbackAsync(ct);
                return CleanupOutcome.Busy;
            }

            if (!await TryLockRowNoWaitAsync(db, "Agents", candidate.AgentId, ct))
            {
                await tx.RollbackAsync(ct);
                return CleanupOutcome.Busy;
            }

            var agent = await db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == candidate.AgentId, ct);
            var session = await db.AgentSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == candidate.SessionId, ct);
            if (agent is null || session is null || agent.IsPoolDelegate
                || !string.Equals(agent.PersistentSessionId, session.Id.ToString("D"), StringComparison.Ordinal))
            {
                await tx.RollbackAsync(ct);
                return CleanupOutcome.Deferred;
            }

            var ownership = await new StandingSessionOwnership(db).ResolveAsync(session, ct);
            if (ownership.Owner != agent.Id)
            {
                await tx.RollbackAsync(ct);
                return CleanupOutcome.Deferred;
            }

            var wait = await db.Set<CapacityRecoveryWait>().AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == candidate.WaitId, ct);
            if (!StandingReplacementHolds(wait, candidate, session))
            {
                await tx.RollbackAsync(ct);
                return CleanupOutcome.Deferred;
            }

            if (!await TryTakeProviderLockAsync(db, candidate.Kind, ct))
            {
                await tx.RollbackAsync(ct);
                return CleanupOutcome.Busy;
            }

            wait = await db.Set<CapacityRecoveryWait>().AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == candidate.WaitId, ct);
            if (wait is null || !StandingReplacementHolds(wait, candidate, session))
            {
                await tx.RollbackAsync(ct);
                return CleanupOutcome.Deferred;
            }

            if (_landBoundary is not null)
                await _landBoundary.ReachedAsync(
                    "capacity-obsolete-before-finish", wait.Id, Guid.Empty, ct);

            wait = await db.Set<CapacityRecoveryWait>().AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == candidate.WaitId, ct);
            if (wait is null || !StandingReplacementHolds(wait, candidate, session))
            {
                await tx.RollbackAsync(ct);
                return CleanupOutcome.Deferred;
            }

            var now = UtcNow();
            var updated = await db.Set<CapacityRecoveryWait>()
                .Where(w => w.Id == wait.Id && w.Version == candidate.Version && w.ActionKey == candidate.ActionKey
                    && w.State == wait.State)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.State, CapacityRecoveryWaitState.Superseded)
                    .SetProperty(w => w.Outcome, nameof(CapacityRecoveryWaitState.Superseded))
                    .SetProperty(w => w.OutcomeReason, "standing-consumer-replaced")
                    .SetProperty(w => w.NeedsRevalidationGrant, false)
                    .SetProperty(w => w.Version, wait.Version + 1)
                    .SetProperty(w => w.UpdatedAt, now), ct);
            if (updated != 1)
            {
                await tx.RollbackAsync(ct);
                return CleanupOutcome.Deferred;
            }

            var provider = await db.Set<CapacityRecoveryProviderState>()
                .FirstOrDefaultAsync(s => s.Kind == candidate.Kind, ct);
            if (provider is { GrantedWaitId: { } granted, GrantedActionKey: { } grantedKey }
                && granted == wait.Id && grantedKey == candidate.ActionKey)
                ClearGrant(provider, now);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            _logger.LogInformation(
                "Capacity obsolete standing superseded wait {WaitId} agent {AgentId} session {SessionId}",
                wait.Id, candidate.AgentId, candidate.SessionId);
            return CleanupOutcome.Detached;
        }
        catch (Exception ex) when (IsLockNotAvailable(ex))
        {
            await tx.RollbackAsync(ct);
            return CleanupOutcome.Busy;
        }
    }

    private static async Task<List<QueueCleanupCandidate>> DiscoverObsoleteQueueSelectionsAsync(
        AppDbContext db, int batch, int cap, CancellationToken ct)
    {
        var rows = await (
            from w in Unfinished(db.CapacityRecoveryWaits)
            where (w.ConsumerKind == CapacityWaitConsumerKind.LiveSession
                    || w.ConsumerKind == CapacityWaitConsumerKind.PendingQueue)
                && w.SelectedMessageId != null
                && w.LaunchSessionId == null
                && w.LaunchReceipt == null
                && w.DispatchAttemptId == null
                && w.ConfirmedPromptSequence == null
                && w.State != CapacityRecoveryWaitState.PromptConfirmed
                && (w.TaskId == null || !db.AgentTasks.Any(t => t.Id == w.TaskId
                    && (t.Status == AgentTaskStatus.Queued
                        || (t.Status == AgentTaskStatus.Working && t.CapacityWaitRetained))))
            join m in db.SessionQueuedMessages on w.SelectedMessageId equals m.Id
            where (m.Origin == QueuedMessageOrigin.Channel || m.Origin == QueuedMessageOrigin.Scheduled)
                && m.CapacityWaitId == w.Id
                && m.CapacityRecoveryActionKey == w.ActionKey
                && (w.SessionId == null || m.AgentSessionId == w.SessionId)
                && (m.Status == QueuedMessageStatus.Canceled
                    || (m.Status == QueuedMessageStatus.Pending && m.DeliveryAttempts >= cap))
            orderby w.BlockedAt, w.Id
            select new
            {
                w.Id,
                w.Version,
                w.ActionKey,
                w.ExecutionKind,
                MessageId = m.Id,
                MessageSessionId = m.AgentSessionId,
                WaitSessionId = w.SessionId,
                w.BlockedAt,
                w.ConsumerKey,
            })
            .Take(batch)
            .ToListAsync(ct);

        var matched = new List<QueueCleanupCandidate>(rows.Count);
        foreach (var row in rows)
        {
            if (!SessionMatchesWait(row.WaitSessionId, row.ConsumerKey, row.MessageSessionId))
                continue;
            matched.Add(new QueueCleanupCandidate(
                row.Id, row.Version, row.ActionKey, row.ExecutionKind, row.MessageId,
                row.MessageSessionId, row.BlockedAt, row.ConsumerKey));
        }

        return matched;
    }

    private static async Task<List<StandingCleanupCandidate>> DiscoverReplacedStandingConsumersAsync(
        AppDbContext db, int batch, CancellationToken ct)
    {
        var waits = await Unfinished(db.CapacityRecoveryWaits)
            .Where(w => w.ConsumerKind == CapacityWaitConsumerKind.StandingStart
                && w.LaunchSessionId == null
                && w.LaunchReceipt == null
                && w.DispatchAttemptId == null
                && w.SelectedMessageId == null
                && w.ConfirmedPromptSequence == null)
            .Select(w => new
            {
                w.Id,
                w.Version,
                w.ActionKey,
                w.ExecutionKind,
                w.AgentId,
                w.ConsumerKey,
                w.BlockedAt,
                w.SessionId,
                w.SessionStartedAt,
            })
            .ToListAsync(ct);
        if (waits.Count == 0)
            return [];

        var agentIds = new HashSet<Guid>();
        foreach (var wait in waits)
        {
            if (wait.AgentId is { } id)
                agentIds.Add(id);
            else if (TryParseAgentKey(wait.ConsumerKey, out var parsed))
                agentIds.Add(parsed);
        }

        var agents = await db.Agents.AsNoTracking()
            .Where(a => agentIds.Contains(a.Id) && !a.IsPoolDelegate && a.PersistentSessionId != null)
            .Select(a => new { a.Id, a.PersistentSessionId })
            .ToListAsync(ct);
        var pointers = new Dictionary<Guid, Guid>();
        foreach (var agent in agents)
        {
            if (Guid.TryParse(agent.PersistentSessionId, out var sessionId))
                pointers[agent.Id] = sessionId;
        }

        var sessions = await db.AgentSessions.AsNoTracking()
            .Where(s => pointers.Values.Contains(s.Id))
            .Select(s => new { s.Id, s.StartedAt, s.CapacityRecoveryActionKey })
            .ToDictionaryAsync(s => s.Id, ct);

        var found = new List<StandingCleanupCandidate>();
        foreach (var wait in waits.OrderBy(w => w.BlockedAt).ThenBy(w => w.Id))
        {
            var agentId = wait.AgentId ?? (TryParseAgentKey(wait.ConsumerKey, out var parsed) ? parsed : Guid.Empty);
            if (agentId == Guid.Empty || !pointers.TryGetValue(agentId, out var sessionId))
                continue;
            if (!sessions.TryGetValue(sessionId, out var session))
                continue;
            if (!IsStandingReplacement(
                    wait.SessionId, wait.SessionStartedAt, wait.BlockedAt, wait.Id, session.Id,
                    session.StartedAt, session.CapacityRecoveryActionKey))
                continue;
            found.Add(new StandingCleanupCandidate(
                wait.Id, wait.Version, wait.ActionKey, wait.ExecutionKind, agentId, sessionId,
                wait.BlockedAt, wait.SessionId, wait.SessionStartedAt));
            if (found.Count == batch)
                break;
        }

        return found;
    }

    private IQueryable<CapacityRecoveryWait> WhereQueueDemandAllowsGrant(
        IQueryable<CapacityRecoveryWait> waits, AppDbContext db, DateTime now)
    {
        var cap = DeliveryAttemptCap;
        return waits.Where(w =>
            (w.ConsumerKind != CapacityWaitConsumerKind.LiveSession
                && w.ConsumerKind != CapacityWaitConsumerKind.PendingQueue)
            || w.SessionId == null
            || w.LaunchSessionId != null
            || w.LaunchReceipt != null
            || w.DispatchAttemptId != null
            || w.ConfirmedPromptSequence != null
            || w.State == CapacityRecoveryWaitState.PromptConfirmed
            || (w.TaskId != null && db.AgentTasks.Any(t => t.Id == w.TaskId
                && (t.Status == AgentTaskStatus.Queued
                    || (t.Status == AgentTaskStatus.Working && t.CapacityWaitRetained))))
            || (!(w.SelectedMessageId != null && db.SessionQueuedMessages.Any(m =>
                    m.Id == w.SelectedMessageId
                    && m.AgentSessionId == w.SessionId
                    && (m.Origin == QueuedMessageOrigin.Channel || m.Origin == QueuedMessageOrigin.Scheduled)
                    && m.CapacityWaitId == w.Id
                    && m.CapacityRecoveryActionKey == w.ActionKey
                    && (m.Status == QueuedMessageStatus.Canceled
                        || (m.Status == QueuedMessageStatus.Pending && m.DeliveryAttempts >= cap))))
                && (db.SessionQueuedMessages.Any(m =>
                        m.AgentSessionId == w.SessionId
                        && m.Status == QueuedMessageStatus.Pending
                        && (m.Origin == QueuedMessageOrigin.Channel || m.Origin == QueuedMessageOrigin.Scheduled)
                        && m.DeliveryAttempts < cap
                        && (m.HoldUntil == null || m.HoldUntil <= now)
                        && m.MaintenanceKind != RemoteControlMaintenanceKind.LegacyUnclassified
                        && (m.CapacityRecoveryActionKey == null
                            || m.CapacityRecoveryActionKey == w.ActionKey
                            || m.CapacityWaitId == w.Id)
                        && (m.DeferredFromRunAttemptId == null
                            || m.MaintenanceAcceptedStartedAt == null
                            || db.AgentSessions.Any(s => s.Id == w.SessionId
                                && s.StartedAt == m.MaintenanceAcceptedStartedAt)))
                    || (w.SelectedMessageId != null
                        && (!db.SessionQueuedMessages.Any(m => m.Id == w.SelectedMessageId)
                            || db.SessionQueuedMessages.Any(m => m.Id == w.SelectedMessageId
                                && (m.Status == QueuedMessageStatus.Sent
                                    || (m.Origin != QueuedMessageOrigin.Channel
                                        && m.Origin != QueuedMessageOrigin.Scheduled))))))));
    }

    private async Task<bool> ObsoleteSelectionAttachedAsync(
        AppDbContext db, CapacityRecoveryWait wait, CancellationToken ct)
    {
        if (!IsQueueConsumer(wait) || wait.SelectedMessageId is not { } selected || HasIndependentConsumer(wait))
            return false;
        if (await HasRedeemableTaskAsync(db, wait, ct))
            return false;
        var sessionId = ResolveQueueSessionId(wait);
        if (sessionId is null)
            return false;
        var cap = DeliveryAttemptCap;
        return await db.SessionQueuedMessages.AsNoTracking().AnyAsync(m =>
            m.Id == selected
            && m.AgentSessionId == sessionId
            && (m.Origin == QueuedMessageOrigin.Channel || m.Origin == QueuedMessageOrigin.Scheduled)
            && m.CapacityWaitId == wait.Id
            && m.CapacityRecoveryActionKey == wait.ActionKey
            && (m.Status == QueuedMessageStatus.Canceled
                || (m.Status == QueuedMessageStatus.Pending && m.DeliveryAttempts >= cap)), ct);
    }

    private async Task<bool> DemandIsProvenAbsentAsync(
        AppDbContext db, CapacityRecoveryWait wait, DateTime now, CancellationToken ct)
    {
        if (!CapacityRecoveryPolicy.IsGrantCandidate(wait) || !IsQueueConsumer(wait) || HasIndependentConsumer(wait))
            return false;
        if (await HasRedeemableTaskAsync(db, wait, ct))
            return false;
        var sessionId = ResolveQueueSessionId(wait);
        if (sessionId is null)
            return false;
        if (await SelectionIsConservativeAsync(db, wait, ct))
            return false;
        return !await HasDeliverableDemandAsync(db, wait, sessionId.Value, now, ct);
    }

    private async Task<bool> ApplyDemandLossAsync(
        AppDbContext db, CapacityRecoveryWait wait, DateTime now, CancellationToken ct)
    {
        var updated = await db.Set<CapacityRecoveryWait>()
            .Where(w => w.Id == wait.Id
                && w.Version == wait.Version
                && w.ActionKey == wait.ActionKey
                && (w.State == CapacityRecoveryWaitState.Ready
                    || w.State == CapacityRecoveryWaitState.ActionPending
                    || (w.State == CapacityRecoveryWaitState.Admitted && w.NeedsRevalidationGrant)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.State, CapacityRecoveryWaitState.Ready)
                .SetProperty(w => w.Outcome, nameof(CapacityRecoveryWaitState.Ready))
                .SetProperty(w => w.OutcomeReason, "queue-demand-lost")
                .SetProperty(w => w.Version, wait.Version + 1)
                .SetProperty(w => w.UpdatedAt, now), ct);
        if (updated != 1)
            return false;
        _logger.LogInformation(
            "Capacity queue demand lost wait {WaitId} action {ActionKey}",
            wait.Id, wait.ActionKey);
        return true;
    }

    private async Task<bool> HasDeliverableDemandAsync(
        AppDbContext db, CapacityRecoveryWait wait, Guid sessionId, DateTime now, CancellationToken ct)
    {
        var cap = DeliveryAttemptCap;
        var sessionStarted = await db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => s.StartedAt)
            .FirstOrDefaultAsync(ct);
        var rows = await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == sessionId
                && m.Status == QueuedMessageStatus.Pending
                && (m.Origin == QueuedMessageOrigin.Channel || m.Origin == QueuedMessageOrigin.Scheduled)
                && m.DeliveryAttempts < cap
                && (m.HoldUntil == null || m.HoldUntil <= now)
                && m.MaintenanceKind != RemoteControlMaintenanceKind.LegacyUnclassified
                && (m.CapacityRecoveryActionKey == null
                    || m.CapacityRecoveryActionKey == wait.ActionKey
                    || m.CapacityWaitId == wait.Id))
            .Select(m => new { m.DeferredFromRunAttemptId, m.MaintenanceAcceptedStartedAt })
            .ToListAsync(ct);
        return rows.Any(m => m.DeferredFromRunAttemptId == null
            || m.MaintenanceAcceptedStartedAt is null
            || SessionGeneration.Equal(m.MaintenanceAcceptedStartedAt, sessionStarted));
    }

    private static async Task<bool> SelectionIsConservativeAsync(
        AppDbContext db, CapacityRecoveryWait wait, CancellationToken ct)
    {
        if (wait.SelectedMessageId is not { } selected)
            return false;
        var row = await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.Id == selected)
            .Select(m => new { m.Status, m.Origin })
            .FirstOrDefaultAsync(ct);
        if (row is null)
            return true;
        return row.Status == QueuedMessageStatus.Sent
            || (row.Origin != QueuedMessageOrigin.Channel && row.Origin != QueuedMessageOrigin.Scheduled);
    }

    private static async Task<bool> HasRedeemableTaskAsync(
        AppDbContext db, CapacityRecoveryWait wait, CancellationToken ct)
    {
        if (wait.TaskId is not { } taskId)
            return false;
        return await db.AgentTasks.AsNoTracking().AnyAsync(t => t.Id == taskId
            && (t.Status == AgentTaskStatus.Queued
                || (t.Status == AgentTaskStatus.Working && t.CapacityWaitRetained)), ct);
    }

    private static bool MessageIsObsolete(
        SessionQueuedMessage? message, Guid waitId, string actionKey, Guid sessionId, int cap) =>
        message is not null
        && message.AgentSessionId == sessionId
        && message.CapacityWaitId == waitId
        && message.CapacityRecoveryActionKey == actionKey
        && message.Origin is QueuedMessageOrigin.Channel or QueuedMessageOrigin.Scheduled
        && (message.Status == QueuedMessageStatus.Canceled
            || (message.Status == QueuedMessageStatus.Pending && message.DeliveryAttempts >= cap));

    private static bool HasIndependentConsumer(CapacityRecoveryWait wait) =>
        wait.LaunchSessionId is not null
        || wait.LaunchReceipt is not null
        || wait.DispatchAttemptId is not null
        || wait.ConfirmedPromptSequence is not null
        || wait.State == CapacityRecoveryWaitState.PromptConfirmed;

    private static bool IsQueueConsumer(CapacityRecoveryWait wait) =>
        wait.ConsumerKind is CapacityWaitConsumerKind.LiveSession or CapacityWaitConsumerKind.PendingQueue;

    private static Guid? ResolveQueueSessionId(CapacityRecoveryWait wait)
    {
        if (wait.SessionId is { } sessionId)
            return sessionId;
        return TryParseSessionKey(wait.ConsumerKey, out var parsed) ? parsed : null;
    }

    private static bool SessionMatchesWait(Guid? waitSessionId, string consumerKey, Guid messageSessionId)
    {
        if (waitSessionId is { } sessionId)
            return sessionId == messageSessionId;
        return TryParseSessionKey(consumerKey, out var parsed) && parsed == messageSessionId;
    }

    private static bool StandingReplacementHolds(
        CapacityRecoveryWait? wait, StandingCleanupCandidate candidate, AgentSession session)
    {
        if (wait is null || IsFinished(wait) || wait.Version != candidate.Version || wait.ActionKey != candidate.ActionKey)
            return false;
        if (wait.ConsumerKind != CapacityWaitConsumerKind.StandingStart || HasIndependentConsumer(wait))
            return false;
        return IsStandingReplacement(
            wait.SessionId, wait.SessionStartedAt, wait.BlockedAt, wait.Id, session.Id, session.StartedAt,
            session.CapacityRecoveryActionKey);
    }

    private static bool IsStandingReplacement(
        Guid? waitSessionId, DateTime? waitStartedAt, DateTime blockedAt, Guid waitId, Guid sessionId,
        DateTime sessionStartedAt, string? sessionActionKey)
    {
        if (sessionStartedAt <= blockedAt)
            return false;
        if (waitSessionId is not null && waitStartedAt is not null)
            return sessionId != waitSessionId || sessionStartedAt != waitStartedAt;
        var prefix = $"{waitId:N}:";
        return sessionActionKey is null || !sessionActionKey.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static bool TryParseAgentKey(string consumerKey, out Guid agentId)
    {
        agentId = Guid.Empty;
        const string prefix = "agent:";
        return consumerKey.StartsWith(prefix, StringComparison.Ordinal)
            && Guid.TryParse(consumerKey[prefix.Length..], out agentId);
    }

    private static bool TryParseSessionKey(string consumerKey, out Guid sessionId)
    {
        sessionId = Guid.Empty;
        const string prefix = "session:";
        return consumerKey.StartsWith(prefix, StringComparison.Ordinal)
            && Guid.TryParse(consumerKey[prefix.Length..], out sessionId);
    }

    private static async Task<bool> TryTakeProviderLockAsync(AppDbContext db, AgentKind kind, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT pg_try_advisory_xact_lock(hashtext(@key))";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.Value = $"antiphon.capacity.kind.{kind}";
        command.Parameters.Add(parameter);
        var acquired = await command.ExecuteScalarAsync(ct);
        return acquired is bool held && held;
    }

    private static Task<bool> TryLockMessageNoWaitAsync(AppDbContext db, Guid messageId, CancellationToken ct) =>
        TryLockLiteralNoWaitAsync(
            db,
            $"""SELECT "Id" FROM "SessionQueuedMessages" WHERE "Id" = {messageId} FOR UPDATE NOWAIT""",
            ct);

    private static async Task<bool> TryLockRowNoWaitAsync(
        AppDbContext db, string table, Guid id, CancellationToken ct) => table switch
    {
        "AgentSessions" => await TryLockLiteralNoWaitAsync(
            db, $"""SELECT "Id" FROM "AgentSessions" WHERE "Id" = {id} FOR UPDATE NOWAIT""", ct),
        "Agents" => await TryLockLiteralNoWaitAsync(
            db, $"""SELECT "Id" FROM "Agents" WHERE "Id" = {id} FOR UPDATE NOWAIT""", ct),
        _ => throw new InvalidOperationException($"Refusing to lock table {table}."),
    };

    private static async Task<bool> TryLockLiteralNoWaitAsync(
        AppDbContext db, FormattableString sql, CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(sql, ct);
            return true;
        }
        catch (Exception ex) when (IsLockNotAvailable(ex))
        {
            return false;
        }
    }

    private static bool IsLockNotAvailable(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is PostgresException pg && pg.SqlState == "55P03")
                return true;
        }

        return false;
    }

    private void LogQueueCleanup(int scanned, int detached, int deferred, int busy, long elapsedMs, long gateHoldMs) =>
        _logger.LogInformation(
            "Capacity obsolete queue scanned {Scanned} detached {Detached} deferred {Deferred} busy {Busy} elapsed {ElapsedMs} gateHold {GateHoldMs}",
            scanned, detached, deferred, busy, elapsedMs, gateHoldMs);

    private readonly record struct QueueCleanupCandidate(
        Guid WaitId,
        int Version,
        string ActionKey,
        AgentKind Kind,
        Guid MessageId,
        Guid SessionId,
        DateTime BlockedAt,
        string ConsumerKey);

    private readonly record struct StandingCleanupCandidate(
        Guid WaitId,
        int Version,
        string ActionKey,
        AgentKind Kind,
        Guid AgentId,
        Guid SessionId,
        DateTime BlockedAt,
        Guid? WaitSessionId,
        DateTime? WaitStartedAt);

    private enum CleanupOutcome
    {
        Detached,
        Deferred,
        Busy,
    }
}
