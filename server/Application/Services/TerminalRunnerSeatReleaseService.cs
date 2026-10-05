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
/// Dormant S3a foundation. Only committed attempts can register debt. Reservation is separate
/// from physical release; S3d owns sending and reconciling a durable action. No production hook
/// calls this coordinator until the later integration slice.
/// </summary>
public sealed class TerminalRunnerSeatReleaseService(
    AppDbContext db, TerminalRunnerSeatReleasePolicy policy, SessionMessageQueueService queue,
    SessionStateStore states, ISessionRunnerDirectory runners, TimeProvider clock,
    IOptions<TerminalRunnerSeatReleaseOptions> options)
{
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
            var settlementEventId = await db.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == task.Id && e.At == task.CompletedAt
                    && (e.Type == AgentTaskEventType.Completed || e.Type == AgentTaskEventType.Failed
                        || e.Type == AgentTaskEventType.Canceled || e.Type == AgentTaskEventType.Blocked))
                .OrderBy(e => e.Id).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
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
                if (descriptor is not { Available: true, Stale: false })
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
                && t.AgentId == task.AgentId && t.ReportEvidence == task.ReportEvidence))
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.State, RunnerSeatReleaseState.Reserved)
                .SetProperty(r => r.Revision, r => r.Revision + 1).SetProperty(r => r.ActionId, actionId)
                .SetProperty(r => r.ReasonCode, nameof(TerminalRunnerSeatDecision.Reserved))
                .SetProperty(r => r.ObservationToken, observed.Token)
                .SetProperty(r => r.BindingIdentity, observed.Transcript.BindingIdentity)
                .SetProperty(r => r.FileRevision, observed.Transcript.FileRevision)
                .SetProperty(r => r.TranscriptRevision, observed.Transcript.TranscriptRevision)
                .SetProperty(r => r.FirstStableObservedAt, now)
                .SetProperty(r => r.LastObservedAt, now).SetProperty(r => r.UpdatedAt, now), ct);
    }
}
