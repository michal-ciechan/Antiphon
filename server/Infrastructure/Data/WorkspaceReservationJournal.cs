using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OwnerFacts = Antiphon.Server.Application.Services.WorkspaceReservationLiveness.OwnerFacts;

namespace Antiphon.Server.Infrastructure.Data;

public sealed class WorkspaceReservationJournal(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    IOptions<WorktreeResidueSettings>? settings = null) : IWorkspaceReservationJournal
{
    /// <summary>CARD-0664 D-9: <c>WorktreeResidue:LaunchGraceMinutes</c>, clamped to at least one minute.</summary>
    private TimeSpan LaunchGrace => TimeSpan.FromMinutes(Math.Max(1, settings?.Value.LaunchGraceMinutes ?? 15));

    public async Task<IReadOnlyList<WorkspaceReservationSnapshot>> ReadActiveAsync(WorkspaceReservationKey key, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.WorkspaceUseReservations.AsNoTracking()
            .Where(r => r.Active)
            .ToListAsync(ct);
        return rows.Where(r => Same(r, key)).Select(ToSnapshot).ToList();
    }

    public async Task<IReadOnlyList<WorkspaceReservationSnapshot>> ReadActiveAsync(
        WorkspaceReservationKey key, bool liveOwnersOnly, CancellationToken ct)
    {
        if (!liveOwnersOnly) return await ReadActiveAsync(key, ct);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = (await db.WorkspaceUseReservations.AsNoTracking()
            .Where(r => r.Active)
            .ToListAsync(ct)).Where(r => Same(r, key)).ToList();
        var orphaned = await FindOrphanedLaunchRowsAsync(db, rows, LaunchGrace, ct);
        return rows.Where(r => !orphaned.Contains(r)).Select(ToSnapshot).ToList();
    }

    public async Task<WorkspaceReservationCommitResult> TryAdmitConsumerAsync(WorkspaceReservationCommand command, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var blocking = await db.WorkspaceUseReservations
            .Where(r => r.Active && (r.Kind == WorkspaceReservationKind.Retirement || r.Kind == WorkspaceReservationKind.HistoricalFence))
            .ToListAsync(ct);
        if (blocking.Any(r => Same(r, command.Key)))
        {
            await tx.RollbackAsync(ct);
            return new(false, null, "workspace_reserved");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var key = command.Key.Normalized();
        var row = new WorkspaceUseReservation
        {
            Id = Guid.NewGuid(),
            Generation = 1,
            CanonicalPath = key.CanonicalPath,
            SourceFullRef = key.SourceFullRef,
            CommonDirectory = key.CommonDirectory,
            TaskId = command.TaskId,
            SessionId = command.SessionId,
            RetirementId = command.RetirementId,
            Kind = command.Kind,
            CreatedAt = now,
            Active = true,
        };
        db.WorkspaceUseReservations.Add(row);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(true, ToSnapshot(row), null);
    }

    public async Task<WorkspaceReservationCommitResult> TryClaimRetirementAsync(WorkspaceReservationCommand command, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.WorkspaceUseReservations
            .Where(r => r.Active)
            .ToListAsync(ct);
        var overlap = existing.Where(r => Same(r, command.Key)).ToList();
        // CARD-0664 D-1: an orphaned Launch row (owner ended or missing, past grace) no longer
        // blocks, and the claim releases it on sight in its own transaction.
        var orphaned = await FindOrphanedLaunchRowsAsync(db, overlap, LaunchGrace, ct);
        if (orphaned.Count > 0)
        {
            Release(orphaned);
            await db.SaveChangesAsync(ct);
            overlap = overlap.Except(orphaned).ToList();
        }

        if (overlap.Any(r => r.Kind != WorkspaceReservationKind.Retirement || r.RetirementId != command.RetirementId))
        {
            await tx.CommitAsync(ct);
            return new(false, null, "workspace_in_use");
        }

        var mine = overlap.FirstOrDefault(r => r.Kind == WorkspaceReservationKind.Retirement && r.RetirementId == command.RetirementId);
        if (mine is not null)
        {
            await tx.CommitAsync(ct);
            return new(true, ToSnapshot(mine), null);
        }

        var key = command.Key.Normalized();
        var row = new WorkspaceUseReservation
        {
            Id = Guid.NewGuid(),
            Generation = 1,
            CanonicalPath = key.CanonicalPath,
            SourceFullRef = key.SourceFullRef,
            CommonDirectory = key.CommonDirectory,
            TaskId = command.TaskId,
            RetirementId = command.RetirementId,
            Kind = WorkspaceReservationKind.Retirement,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            Active = true,
        };
        db.WorkspaceUseReservations.Add(row);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(true, ToSnapshot(row), null);
    }

    public async Task InvalidateUnclaimedReleaseAsync(Guid taskId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.TaskWorktreeRetirements
            .Where(r => r.TaskId == taskId && r.Active && r.State == WorktreeRetirementState.Released && r.ClaimedAt == null)
            .ToListAsync(ct);
        foreach (var row in rows)
        {
            row.Active = false;
            row.State = WorktreeRetirementState.Revoked;
            row.UpdatedAt = clock.GetUtcNow().UtcDateTime;
            row.ConcurrencyToken = Guid.NewGuid();
        }

        if (rows.Count > 0) await db.SaveChangesAsync(ct);
    }

    public async Task ReleaseConsumerAsync(Guid reservationId, int generation, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.WorkspaceUseReservations.SingleOrDefaultAsync(r => r.Id == reservationId, ct);
        if (row is null || row.Generation != generation) return;
        row.Active = false;
        row.ReleasedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Owner-end release: no grace, but still the owner-liveness rule, so a row admitted by a
    /// racing requeue (task live again) or naming a second, still-live owner stays active.
    /// </summary>
    public Task ReleaseTaskConsumersAsync(Guid taskId, CancellationToken ct) =>
        ReleaseEndedOwnerRowsAsync(r => r.TaskId == taskId, ct);

    public Task ReleaseSessionConsumersAsync(Guid sessionId, CancellationToken ct) =>
        ReleaseEndedOwnerRowsAsync(r => r.SessionId == sessionId, ct);

    public async Task<int> ReleaseOrphanedConsumersAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.WorkspaceUseReservations
            .Where(r => r.Active && r.Kind == WorkspaceReservationKind.Launch)
            .ToListAsync(ct);
        var orphaned = await FindOrphanedLaunchRowsAsync(db, rows, LaunchGrace, ct);
        if (orphaned.Count == 0) return 0;
        Release(orphaned);
        await db.SaveChangesAsync(ct);
        return orphaned.Count;
    }

    private async Task ReleaseEndedOwnerRowsAsync(
        System.Linq.Expressions.Expression<Func<WorkspaceUseReservation, bool>> owner, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.WorkspaceUseReservations
            .Where(r => r.Active && r.Kind == WorkspaceReservationKind.Launch)
            .Where(owner)
            .ToListAsync(ct);
        var ended = await FindOrphanedLaunchRowsAsync(db, rows, TimeSpan.Zero, ct);
        if (ended.Count == 0) return;
        Release(ended);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The <c>Launch</c> rows among <paramref name="rows"/> that D-1 says no longer block.</summary>
    private async Task<List<WorkspaceUseReservation>> FindOrphanedLaunchRowsAsync(
        AppDbContext db, IReadOnlyCollection<WorkspaceUseReservation> rows, TimeSpan grace, CancellationToken ct)
    {
        var launches = rows.Where(r => r.Kind == WorkspaceReservationKind.Launch).ToList();
        if (launches.Count == 0) return [];
        var taskIds = launches.Where(r => r.TaskId is not null).Select(r => r.TaskId!.Value).Distinct().ToList();
        var sessionIds = launches.Where(r => r.SessionId is not null).Select(r => r.SessionId!.Value).Distinct().ToList();
        var tasks = taskIds.Count == 0
            ? []
            : await db.AgentTasks.AsNoTracking()
                .Where(t => taskIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Status, t.LandRequestedAt })
                .ToDictionaryAsync(t => t.Id, ct);
        var sessions = sessionIds.Count == 0
            ? []
            : await db.AgentSessions.AsNoTracking()
                .Where(s => sessionIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Status })
                .ToDictionaryAsync(s => s.Id, s => s.Status, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        return launches.Where(r =>
        {
            var task = r.TaskId is Guid tid && tasks.TryGetValue(tid, out var t) ? t : null;
            SessionStatus? session = r.SessionId is Guid sid && sessions.TryGetValue(sid, out var s) ? s : null;
            var facts = new OwnerFacts(
                task is not null, task?.Status, task?.LandRequestedAt is not null,
                session is not null, session);
            return !WorkspaceReservationLiveness.Blocks(r.Kind, r.RetirementId, null, r.CreatedAt, now, grace, facts);
        }).ToList();
    }

    private void Release(IEnumerable<WorkspaceUseReservation> rows)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var row in rows)
        {
            row.Active = false;
            row.ReleasedAt = now;
        }
    }

    private static WorkspaceReservationSnapshot ToSnapshot(WorkspaceUseReservation row) =>
        new(row.Id, row.Generation, row.Kind, row.TaskId, row.SessionId, row.RetirementId, row.Active);

    private static bool Same(WorkspaceUseReservation row, WorkspaceReservationKey key) =>
        WorkspaceReservationKey.Same(row.CanonicalPath, row.SourceFullRef, row.CommonDirectory, key);
}
