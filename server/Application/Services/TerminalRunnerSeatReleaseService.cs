using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
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
        if (session?.RunnerId is not string runnerId || session.RunnerStoreId != observation.ExpectedRunnerStoreId
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
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "RunnerSeatReleases" ("Id", "RunnerId", "RunnerStoreId", "SessionId",
                    "AcceptedStartedAt", "TaskId", "Attempt", "AgentId", "SettlementRevision", "SettledAt",
                    "State", "Revision", "ReasonCode", "CreatedAt", "UpdatedAt")
                VALUES ({id}, {runnerId}, {observation.ExpectedRunnerStoreId}, {sessionId},
                    {observation.ExpectedAcceptedStartedAt}, {task.Id}, {task.Attempt}, {task.AgentId},
                    {task.ConcurrencyToken}, {task.CompletedAt}, {0}, {0L}, {"Observing"}, {now}, {now})
                ON CONFLICT ("RunnerId", "RunnerStoreId", "SessionId", "AcceptedStartedAt") DO NOTHING
                """, ct);
            release = await db.RunnerSeatReleases.AsNoTracking().SingleAsync(r =>
                r.RunnerId == runnerId && r.RunnerStoreId == observation.ExpectedRunnerStoreId
                && r.SessionId == sessionId && r.AcceptedStartedAt == observation.ExpectedAcceptedStartedAt, ct);
        }
        // S3a red checkpoint: durable registration is present; authorization/reservation follows.
        return new(release.Id, TerminalRunnerSeatDecision.Waiting);
    }

    internal Task<int> TryReserveAsync(RunnerSeatRelease expected, AgentTask task,
        TerminalSeatObservation observed, CancellationToken ct) => Task.FromResult(0);
}
