using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class TerminalRunnerSeatReleaseOptions
{
    public const string SectionName = "TerminalRunnerSeatRelease";
    public bool AutomaticEnabled { get; set; } = false;
}

public sealed record TerminalRunnerSeatReservation(Guid? ReleaseId, TerminalRunnerSeatDecision Decision);

/// <summary>
/// Only committed attempts can register debt. A durable send intent precedes runner I/O;
/// interrupted sends reconcile authoritative inventory before any further mutation. No production
/// hook calls this coordinator until the later integration slice; automatic release ships disabled.
/// </summary>
public sealed class TerminalRunnerSeatReleaseService(
    AppDbContext db, TerminalRunnerSeatReleasePolicy policy, SessionMessageQueueService queue,
    SessionStateStore states, ISessionRunnerDirectory runners, TimeProvider clock,
    IOptions<TerminalRunnerSeatReleaseOptions> options)
{
    internal Func<string, CancellationToken, Task>? BoundaryAsync { get; set; }

    // This reader is shared by answer admission and retry. A missing/stopped session alone is
    // never authority to skip StopDelegateAsync. Keep every part of the accepted identity.
    internal static async Task<RunnerSeatRelease?> FindAttemptReleaseAsync(
        AppDbContext db, AgentTask task, CancellationToken ct)
    {
        if (task.Workspace != WorkspaceMode.Worktree || string.IsNullOrEmpty(task.RunnerId)
            || task.AgentSessionId is not Guid sessionId) return null;
        var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null || session.RunnerId != task.RunnerId) return null;
        return await db.RunnerSeatReleases.AsNoTracking().SingleOrDefaultAsync(r =>
            r.TaskId == task.Id && r.Attempt == task.Attempt && r.SessionId == sessionId
            && r.AgentId == task.AgentId && r.RunnerId == task.RunnerId
            && r.RunnerStoreId == session.RunnerStoreId && r.AcceptedStartedAt == session.StartedAt
            && r.SettlementRevision == task.ConcurrencyToken && r.SettledAt == task.CompletedAt, ct);
    }

    internal static bool IsConfirmed(RunnerSeatRelease? release) =>
        release is { State: RunnerSeatReleaseState.Confirmed, ConfirmedAt: not null, ActionId: not null }
        && release.OutcomeCode is nameof(TerminalSeatReleaseOutcome.Released)
            or nameof(TerminalSeatReleaseOutcome.AlreadyExited) or nameof(TerminalSeatReleaseOutcome.AlreadyAbsent);

    public async Task<Guid?> RegisterAndReleaseAsync(
        Guid taskId, TerminalSeatObservationRequest observation, CancellationToken ct)
    {
        var reservation = await RegisterAndReserveAsync(taskId, observation, ct);
        if (reservation.ReleaseId is Guid releaseId
            && reservation.Decision is TerminalRunnerSeatDecision.Reserved or TerminalRunnerSeatDecision.AlreadyReserved)
            await AdvanceAsync(releaseId, observation, ct);
        return reservation.ReleaseId;
    }

    public async Task AdvanceAsync(Guid releaseId, TerminalSeatObservationRequest observation, CancellationToken ct)
    {
        if (!options.Value.AutomaticEnabled || db.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null) return;
        var release = await db.RunnerSeatReleases.AsNoTracking().SingleOrDefaultAsync(r => r.Id == releaseId, ct);
        if (release is null || release.State is RunnerSeatReleaseState.Observing or RunnerSeatReleaseState.Confirmed
            || release.ActionId is null || string.IsNullOrWhiteSpace(release.ObservationToken)
            || release.RunnerStoreId != observation.ExpectedRunnerStoreId
            || release.AcceptedStartedAt != observation.ExpectedAcceptedStartedAt) return;
        var gate = queue.GetLock(release.SessionId);
        if (!await gate.WaitAsync(0, ct)) return;
        try
        {
            // No tracked caller snapshot may decide recovery. Another scope/process can have
            // advanced the same durable action while this caller waited for the recipient gate.
            release = await db.RunnerSeatReleases.AsNoTracking().SingleAsync(r => r.Id == releaseId, ct);
            if (release.State == RunnerSeatReleaseState.Confirmed) return;
            if (release.State == RunnerSeatReleaseState.Unresolved)
            {
                await ReconcileAsync(release, observation, ct);
                return;
            }
            if (release.State != RunnerSeatReleaseState.Reserved) return;
            if (BoundaryAsync is not null) await BoundaryAsync("BeforeDispatch", ct);
            if (!await RunnerReadyAsync(release, ct))
            {
                await PendingAsync(release, "Unknown", ct);
                return;
            }

            // Queue gate -> short task/release/session transaction. Commit before touching the
            // wire. Unresolved means "may have sent", including death immediately after commit.
            await using (var tx = await db.Database.BeginTransactionAsync(ct))
            {
                await LockIdentityAsync(release, ct);
                if (await RevalidateAsync(release, ct) is { } refusal)
                {
                    await PendingAsync(release, refusal.ToString(), ct);
                    await tx.CommitAsync(ct);
                    return;
                }
                var changed = await db.RunnerSeatReleases.Where(r => r.Id == release.Id
                    && r.Revision == release.Revision && r.State == RunnerSeatReleaseState.Reserved
                    && r.ActionId == release.ActionId)
                    .ExecuteUpdateAsync(u => u.SetProperty(r => r.State, RunnerSeatReleaseState.Unresolved)
                        .SetProperty(r => r.Revision, r => r.Revision + 1)
                        .SetProperty(r => r.OutcomeCode, "Unresolved")
                        .SetProperty(r => r.UpdatedAt, clock.GetUtcNow().UtcDateTime), ct);
                await tx.CommitAsync(ct);
                if (changed != 1) return;
            }
            release.Revision++;
            release.State = RunnerSeatReleaseState.Unresolved;
            TerminalSeatReleaseResult result;
            try
            {
                result = await runners.Resolve(release.RunnerId).ReleaseTerminalSeatAsync(release.SessionId,
                    new(release.ActionId!.Value, observation, release.ObservationToken!), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // The committed send intent is already the recovery record. No new action ID,
                // force-release fallback or inferred success after a dropped response.
                return;
            }
            if (BoundaryAsync is not null) await BoundaryAsync("BeforeResponse", ct);
            if (result.SessionId != release.SessionId || result.ActionId != release.ActionId
                || (result.AcceptedStartedAt != release.AcceptedStartedAt
                    && !(result.Outcome == TerminalSeatReleaseOutcome.AlreadyAbsent && result.AcceptedStartedAt is null)))
            {
                await PendingAsync(release, "MismatchedReceipt", ct);
                return;
            }
            if (!result.ConfirmsExit)
            {
                await PendingAsync(release, result.Outcome.ToString(), ct);
                return;
            }
            if (await RunnerReadyAsync(release, ct))
                await ConfirmAsync(release, result.Outcome, ct);
        }
        finally { gate.Release(); }
    }

    private async Task ReconcileAsync(RunnerSeatRelease release,
        TerminalSeatObservationRequest observation, CancellationToken ct)
    {
        if (await RevalidateAsync(release, ct) is not null || !await RunnerReadyAsync(release, ct)) return;
        RunnerInventory inventory;
        try { inventory = await runners.GetInventoryAsync(release.RunnerId, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { return; }
        // Available means a complete fresh List RPC, not a heartbeat or an error's empty list.
        // Recheck recovery/store after the RPC too: a disconnect or adoption can race List.
        if (inventory is not RunnerInventory.Available available || !await RunnerReadyAsync(release, ct)) return;
        var seats = available.Sessions.Where(s => s.SessionId == release.SessionId).ToArray();
        if (seats.Length == 0)
        {
            await ConfirmAsync(release, TerminalSeatReleaseOutcome.AlreadyAbsent, ct);
            return;
        }
        if (seats.Length != 1 || seats[0].AcceptedStartedAt != release.AcceptedStartedAt
            || seats[0].Pending is not null) return;
        if (seats[0].Status == "Exited")
        {
            await ConfirmAsync(release, TerminalSeatReleaseOutcome.AlreadyExited, ct);
            return;
        }

        // A remaining seat needs new runner qualification, never a replay of the ambiguous
        // action. Reusing its token would spend old authority after an unknown interval.
        TerminalSeatObservation fresh;
        try { fresh = await runners.Resolve(release.RunnerId).ObserveTerminalSeatAsync(release.SessionId, observation, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { return; }
        if (fresh.Status != TerminalSeatQualificationStatus.Qualified
            || fresh.StableFor < TimeSpan.FromSeconds(120) || string.IsNullOrWhiteSpace(fresh.Token)
            || fresh.Token == release.ObservationToken || fresh.Transcript.Status != TerminalTranscriptReadStatus.Success
            || fresh.Transcript.Verdict != TerminalTranscriptVerdict.Idle) return;
        if (!await RunnerReadyAsync(release, ct)) return;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockIdentityAsync(release, ct);
        if (await RevalidateAsync(release, ct) is null)
        {
            await db.RunnerSeatReleases.Where(r => r.Id == release.Id && r.Revision == release.Revision
                && r.State == RunnerSeatReleaseState.Unresolved && r.ActionId == release.ActionId)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.State, RunnerSeatReleaseState.Reserved)
                    .SetProperty(r => r.Revision, r => r.Revision + 1).SetProperty(r => r.ActionId, Guid.NewGuid())
                    .SetProperty(r => r.ObservationToken, fresh.Token)
                    .SetProperty(r => r.BindingIdentity, fresh.Transcript.BindingIdentity)
                    .SetProperty(r => r.FileRevision, fresh.Transcript.FileRevision)
                    .SetProperty(r => r.TranscriptRevision, fresh.Transcript.TranscriptRevision)
                    .SetProperty(r => r.OutcomeCode, (string?)null)
                    .SetProperty(r => r.UpdatedAt, clock.GetUtcNow().UtcDateTime), ct);
        }
        await tx.CommitAsync(ct);
        // The next advance must revalidate this new reservation again before dispatch.
    }

    private async Task<bool> RunnerReadyAsync(RunnerSeatRelease release, CancellationToken ct)
    {
        try
        {
            if (runners.RemoteInventoryPending(release.RunnerId)
                || runners.GetLiveStoreId(release.RunnerId) != release.RunnerStoreId) return false;
            var descriptor = await runners.DescribeAsync(release.RunnerId, ct);
            return descriptor is { Available: true, DispatchEligible: true, Stale: false }
                && descriptor.RunnerId == release.RunnerId
                && descriptor.Capabilities?.RunnerStoreId == release.RunnerStoreId
                && descriptor.Capabilities.Features?.Contains(RunnerCapabilityFeatures.TerminalSeatReleaseV1) == true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { return false; }
    }

    private async Task LockIdentityAsync(RunnerSeatRelease release, CancellationToken ct)
    {
        // Same ordering will be used by the later released-answer/claim integration. These
        // commands lock committed rows; no SaveChanges on a caller's dirty tracker is involved.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT "Id" FROM "AgentTasks" WHERE "Id" = {release.TaskId} FOR UPDATE
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT "Id" FROM "RunnerSeatReleases" WHERE "Id" = {release.Id} FOR UPDATE
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT "Id" FROM "AgentSessions" WHERE "Id" = {release.SessionId} FOR UPDATE
            """, ct);
    }

    private async Task<TerminalRunnerSeatDecision?> RevalidateAsync(RunnerSeatRelease release, CancellationToken ct)
    {
        var task = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == release.TaskId, ct);
        if (policy.TerminalAttempt(task) is { } terminal) return terminal;
        if (task!.Attempt != release.Attempt || task.ConcurrencyToken != release.SettlementRevision
            || task.CompletedAt != release.SettledAt || task.AgentSessionId != release.SessionId
            || task.AgentId != release.AgentId || release.ReasonCode != $"Reserved:{task.Status}")
            return TerminalRunnerSeatDecision.StaleAttempt;
        if (await SettlementEventAsync(task, ct) != release.SettlementEventId)
            return TerminalRunnerSeatDecision.StaleAttempt;
        var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == release.SessionId, ct);
        if (session is null || session.RunnerId != release.RunnerId || session.RunnerStoreId != release.RunnerStoreId
            || session.StartedAt != release.AcceptedStartedAt) return TerminalRunnerSeatDecision.IdentityUnknown;
        if (session.StandingAgentId is not null || session.CardId is not null)
            return TerminalRunnerSeatDecision.StandingOwner;
        var sessionKey = release.SessionId.ToString("D");
        var agents = await db.Agents.AsNoTracking().Where(a => a.Id == task.AgentId
            || a.PersistentSessionId == sessionKey).ToListAsync(ct);
        if (policy.Custody(task, null) is { } taskCustody) return taskCustody;
        foreach (var agent in agents)
            if (policy.Custody(task, agent) is { } custody) return custody;
        var agentIds = agents.Select(a => a.Id).ToArray();
        var owners = await db.AgentTasks.AsNoTracking().Where(t => t.Id != task.Id
            && (t.AgentSessionId == release.SessionId
                || (t.AgentId != null && (t.AgentId == task.AgentId || agentIds.Contains(t.AgentId.Value)))))
            .ToListAsync(ct);
        if (owners.Any(t => policy.TerminalAttempt(t) is not null)) return TerminalRunnerSeatDecision.Owned;
        if (policy.SettlementAge(task, clock.GetUtcNow().UtcDateTime) is { } age) return age;
        if (await HasPendingDeliveryAsync(release.SessionId, ct)) return TerminalRunnerSeatDecision.PendingDelivery;
        var state = await states.ReadAsync(release.SessionId, ct);
        if (state.Readiness != SessionStateReadiness.Ready || state.AcceptedGeneration != session.StartedAt)
            return TerminalRunnerSeatDecision.Unknown;
        return state.Working ? TerminalRunnerSeatDecision.Working : null;
    }

    private async Task ConfirmAsync(RunnerSeatRelease release, TerminalSeatReleaseOutcome outcome, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockIdentityAsync(release, ct);
        if (await RevalidateAsync(release, ct) is { } refusal)
        {
            await PendingAsync(release, refusal.ToString(), ct);
            await tx.CommitAsync(ct);
            return;
        }
        var now = clock.GetUtcNow().UtcDateTime;
        var changed = await db.RunnerSeatReleases.Where(r => r.Id == release.Id && r.Revision == release.Revision
            && r.ActionId == release.ActionId && r.State == RunnerSeatReleaseState.Unresolved)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.State, RunnerSeatReleaseState.Confirmed)
                .SetProperty(r => r.Revision, r => r.Revision + 1).SetProperty(r => r.OutcomeCode, outcome.ToString())
                .SetProperty(r => r.ConfirmedAt, now).SetProperty(r => r.UpdatedAt, now), ct);
        if (changed == 1)
        {
            await db.AgentSessions.Where(s => s.Id == release.SessionId && s.RunnerId == release.RunnerId
                && s.RunnerStoreId == release.RunnerStoreId && s.StartedAt == release.AcceptedStartedAt
                && (s.Status == SessionStatus.Created || s.Status == SessionStatus.Starting
                    || s.Status == SessionStatus.Running || s.Status == SessionStatus.Stopping))
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Stopped)
                    .SetProperty(s => s.TerminationSource, s => s.TerminationSource == SessionTerminationSource.Unknown
                        ? SessionTerminationSource.SystemRequest : s.TerminationSource)
                    .SetProperty(s => s.EndedAt, now).SetProperty(s => s.LastSeenAt, now), ct);
        }
        await tx.CommitAsync(ct);
    }

    private async Task PendingAsync(RunnerSeatRelease release, string outcome, CancellationToken ct)
    {
        await db.RunnerSeatReleases.Where(r => r.Id == release.Id && r.Revision == release.Revision
            && r.ActionId == release.ActionId && r.State != RunnerSeatReleaseState.Confirmed)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.State, RunnerSeatReleaseState.Unresolved)
                .SetProperty(r => r.Revision, r => r.Revision + 1).SetProperty(r => r.OutcomeCode, outcome)
                .SetProperty(r => r.UpdatedAt, clock.GetUtcNow().UtcDateTime), ct);
    }

    private Task<Guid?> SettlementEventAsync(AgentTask task, CancellationToken ct) =>
        db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == task.Id && e.At == task.CompletedAt
            && (e.Type == AgentTaskEventType.Completed || e.Type == AgentTaskEventType.Failed
                || e.Type == AgentTaskEventType.Canceled || e.Type == AgentTaskEventType.Blocked))
            .OrderBy(e => e.Id).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);

    public async Task<TerminalRunnerSeatReservation> RegisterAndReserveAsync(
        Guid taskId, TerminalSeatObservationRequest observation, CancellationToken ct)
    {
        if (!options.Value.AutomaticEnabled) return new(null, TerminalRunnerSeatDecision.Disabled);
        // Never let a caller's ambient/uncommitted settlement become release authority.
        if (db.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            return new(null, TerminalRunnerSeatDecision.IncompleteAttempt);
        var task = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId, ct);
        if (policy.TerminalAttempt(task) is { } refusal) return new(null, refusal);
        if (task!.AgentSessionId is not Guid sessionId) return new(null, TerminalRunnerSeatDecision.IdentityUnknown);
        var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (observation.ExpectedRunnerStoreId == Guid.Empty || observation.ExpectedAcceptedStartedAt == default
            || session?.RunnerId is not string runnerId || session.RunnerStoreId != observation.ExpectedRunnerStoreId
            || session.StartedAt != observation.ExpectedAcceptedStartedAt)
            return new(null, TerminalRunnerSeatDecision.IdentityUnknown);
        var now = clock.GetUtcNow().UtcDateTime;
        var release = await db.RunnerSeatReleases.AsNoTracking().SingleOrDefaultAsync(r =>
            r.RunnerId == runnerId && r.RunnerStoreId == observation.ExpectedRunnerStoreId
            && r.SessionId == sessionId && r.AcceptedStartedAt == observation.ExpectedAcceptedStartedAt, ct);
        if (release is null)
        {
            // Conflict-safe insert keeps the unique generation identity even across processes.
            var id = Guid.NewGuid();
            var settlementEventId = await SettlementEventAsync(task, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "RunnerSeatReleases" ("Id", "RunnerId", "RunnerStoreId", "SessionId",
                    "AcceptedStartedAt", "TaskId", "Attempt", "AgentId", "SettlementRevision", "SettledAt",
                    "SettlementEventId", "State", "Revision", "ReasonCode", "CreatedAt", "UpdatedAt")
                VALUES ({id}, {runnerId}, {observation.ExpectedRunnerStoreId}, {sessionId},
                    {observation.ExpectedAcceptedStartedAt}, {task.Id}, {task.Attempt}, {task.AgentId},
                    {task.ConcurrencyToken}, {task.CompletedAt}, {settlementEventId}, {0}, {0L}, {"Observing"}, {now}, {now})
                ON CONFLICT ("RunnerId", "RunnerStoreId", "SessionId", "AcceptedStartedAt") DO NOTHING
                """, ct);
            release = await db.RunnerSeatReleases.AsNoTracking().SingleAsync(r =>
                r.RunnerId == runnerId && r.RunnerStoreId == observation.ExpectedRunnerStoreId
                && r.SessionId == sessionId && r.AcceptedStartedAt == observation.ExpectedAcceptedStartedAt, ct);
        }
        if (release.TaskId != task.Id || release.Attempt != task.Attempt
            || release.SettlementRevision != task.ConcurrencyToken || release.SettledAt != task.CompletedAt)
            return new(release.Id, TerminalRunnerSeatDecision.StaleAttempt);
        if (release.State != RunnerSeatReleaseState.Observing)
            return new(release.Id, TerminalRunnerSeatDecision.AlreadyReserved);

        // The same recipient gate serializes normal queue mutation. Never enter it from a SQL
        // transaction or transcript-state lease, and never call a lock-taking queue operation here.
        var gate = queue.GetLock(sessionId);
        if (!await gate.WaitAsync(0, ct))
            return await HoldAsync(release, TerminalRunnerSeatDecision.PendingDelivery, ct);
        try
        {
            var sessionKey = sessionId.ToString("D");
            var agents = await db.Agents.AsNoTracking().Where(a => a.Id == task.AgentId
                || a.Id == session.StandingAgentId || a.PersistentSessionId == sessionKey).ToListAsync(ct);
            if (policy.Custody(task, null) is { } taskCustody)
                return await HoldAsync(release, taskCustody, ct);
            foreach (var agent in agents)
                if (policy.Custody(task, agent) is { } custody)
                    return await HoldAsync(release, custody, ct);
            var agentIds = agents.Select(a => a.Id).ToArray();
            var owners = await db.AgentTasks.AsNoTracking().Where(t => t.Id != task.Id
                && (t.AgentSessionId == sessionId
                    || (t.AgentId != null && (t.AgentId == task.AgentId || agentIds.Contains(t.AgentId.Value)))))
                .ToListAsync(ct);
            if (owners.Any(t => policy.TerminalAttempt(t) is not null))
                return await HoldAsync(release, TerminalRunnerSeatDecision.Owned, ct);
            if (policy.SettlementAge(task, clock.GetUtcNow().UtcDateTime) is { } age)
                return await HoldAsync(release, age, ct);
            if (await HasPendingDeliveryAsync(sessionId, ct))
                return await HoldAsync(release, TerminalRunnerSeatDecision.PendingDelivery, ct);

            var state = await states.ReadAsync(sessionId, ct);
            if (state.Readiness != SessionStateReadiness.Ready || state.AcceptedGeneration != session.StartedAt)
                return await HoldAsync(release, TerminalRunnerSeatDecision.Unknown, ct);
            if (state.Working)
                return await HoldAsync(release, TerminalRunnerSeatDecision.Working, ct);

            TerminalSeatObservation observed;
            try
            {
                if (runners.GetLiveStoreId(runnerId) != release.RunnerStoreId)
                    return await HoldAsync(release, TerminalRunnerSeatDecision.IdentityUnknown, ct);
                var descriptor = await runners.DescribeAsync(runnerId, ct);
                if (descriptor is not { Available: true, DispatchEligible: true, Stale: false }
                    || runners.RemoteInventoryPending(runnerId))
                    return await HoldAsync(release, TerminalRunnerSeatDecision.Unknown, ct);
                if (descriptor.Capabilities?.Features?.Contains(RunnerCapabilityFeatures.TerminalSeatReleaseV1) != true)
                    return await HoldAsync(release, TerminalRunnerSeatDecision.Unsupported, ct);
                observed = await runners.Resolve(runnerId).ObserveTerminalSeatAsync(sessionId, observation, ct);
            }
            catch (NotSupportedException)
            {
                return await HoldAsync(release, TerminalRunnerSeatDecision.Unsupported, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Unavailable is never an absence/exit certificate. Persist only a bounded code.
                return await HoldAsync(release, TerminalRunnerSeatDecision.Unknown, ct);
            }
            if (observed.Status != TerminalSeatQualificationStatus.Qualified)
                return await HoldAsync(release, observed.Status switch
                {
                    TerminalSeatQualificationStatus.Working => TerminalRunnerSeatDecision.Working,
                    TerminalSeatQualificationStatus.Waiting => TerminalRunnerSeatDecision.Waiting,
                    _ => TerminalRunnerSeatDecision.Unknown
                }, ct);
            // StableFor is measured by the runner; never subtract its timestamps from our clock.
            if (observed.StableFor < TimeSpan.FromSeconds(120)
                || observed.Transcript.Status != TerminalTranscriptReadStatus.Success
                || observed.Transcript.Verdict != TerminalTranscriptVerdict.Idle
                || string.IsNullOrWhiteSpace(observed.Token))
                return await HoldAsync(release, TerminalRunnerSeatDecision.Unknown, ct);
            if (BoundaryAsync is not null) await BoundaryAsync("BeforeReservation", ct);
            var changed = await TryReserveAsync(release, task, observed, ct);
            return new(release.Id, changed == 1 ? TerminalRunnerSeatDecision.Reserved : TerminalRunnerSeatDecision.StaleAttempt);
        }
        finally { gate.Release(); }
    }

    private async Task<bool> HasPendingDeliveryAsync(Guid sessionId, CancellationToken ct) =>
        await db.SessionQueuedMessages.AnyAsync(m => m.AgentSessionId == sessionId
            && (m.Status == QueuedMessageStatus.Pending
                || ((m.Status == QueuedMessageStatus.Sent || m.DeliveryAttempts > 0)
                    && m.DeliveryVerdict != DeliveryVerdict.Delivered && m.DeliveryVerdict != DeliveryVerdict.LateConfirmed)), ct)
        || await db.AgentTaskLandNotifications.AnyAsync(n => n.ParentSessionId == sessionId
            && n.ConfirmedAt == null && n.State != LandNotificationState.NotRequired
            && n.State != LandNotificationState.Canceled && n.State != LandNotificationState.Confirmed, ct)
        || await db.AgentTaskDispatchWarningIntents.AnyAsync(n => n.ParentSessionId == sessionId
            && n.MaterializedAt == null && n.InitialState != LandNotificationState.NotRequired, ct)
        || await db.AgentTasks.AnyAsync(t => t.AgentSessionId == sessionId && t.ReleasedSeatAnswerId != null
            && t.ReleasedSeatAnswerTargetAttempt == t.Attempt, ct);

    private async Task<TerminalRunnerSeatReservation> HoldAsync(
        RunnerSeatRelease expected, TerminalRunnerSeatDecision reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await db.RunnerSeatReleases.Where(r => r.Id == expected.Id && r.Revision == expected.Revision)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.ReasonCode, reason.ToString())
                .SetProperty(r => r.Revision, r => r.Revision + 1)
                .SetProperty(r => r.UpdatedAt, now).SetProperty(r => r.LastObservedAt, now)
                .SetProperty(r => r.FirstStableObservedAt, (DateTime?)null)
                .SetProperty(r => r.ObservationToken, (string?)null), ct);
        return new(expected.Id, reason);
    }

    internal async Task<int> TryReserveAsync(RunnerSeatRelease expected, AgentTask task,
        TerminalSeatObservation observed, CancellationToken ct)
    {
        if (!options.Value.AutomaticEnabled || expected.State != RunnerSeatReleaseState.Observing
            || expected.ActionId is not null || observed.Status != TerminalSeatQualificationStatus.Qualified
            || string.IsNullOrWhiteSpace(observed.Token) || db.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null)
            return 0;
        var now = clock.GetUtcNow().UtcDateTime;
        var actionId = Guid.NewGuid();
        // Revision is the sole ledger compare-and-swap fence. Two readers of the same existing
        // row must report the actual affected count, not success from a tracked SaveChanges.
        return await db.RunnerSeatReleases.Where(r => r.Id == expected.Id && r.Revision == expected.Revision
            && db.AgentTasks.Any(t => t.Id == expected.TaskId && t.Attempt == expected.Attempt
                && t.ConcurrencyToken == expected.SettlementRevision && t.Status == task.Status
                && t.CompletedAt == expected.SettledAt && t.AgentSessionId == expected.SessionId
                && t.AgentId == task.AgentId && t.ReportEvidence == task.ReportEvidence)
            && db.AgentTaskEvents.Where(e => e.AgentTaskId == task.Id && e.At == expected.SettledAt
                && (e.Type == AgentTaskEventType.Completed || e.Type == AgentTaskEventType.Failed
                    || e.Type == AgentTaskEventType.Canceled || e.Type == AgentTaskEventType.Blocked))
                .OrderBy(e => e.Id).Select(e => (Guid?)e.Id).FirstOrDefault() == expected.SettlementEventId)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.State, RunnerSeatReleaseState.Reserved)
                .SetProperty(r => r.Revision, r => r.Revision + 1).SetProperty(r => r.ActionId, actionId)
                .SetProperty(r => r.ReasonCode, $"Reserved:{task.Status}")
                .SetProperty(r => r.ObservationToken, observed.Token)
                .SetProperty(r => r.BindingIdentity, observed.Transcript.BindingIdentity)
                .SetProperty(r => r.FileRevision, observed.Transcript.FileRevision)
                .SetProperty(r => r.TranscriptRevision, observed.Transcript.TranscriptRevision)
                .SetProperty(r => r.FirstStableObservedAt, now)
                .SetProperty(r => r.LastObservedAt, now).SetProperty(r => r.UpdatedAt, now), ct);
    }
}
