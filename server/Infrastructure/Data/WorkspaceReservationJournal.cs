using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Infrastructure.Data;

public sealed class WorkspaceReservationJournal(IServiceScopeFactory scopes, TimeProvider clock) : IWorkspaceReservationJournal
{
    public async Task<IReadOnlyList<WorkspaceReservationSnapshot>> ReadActiveAsync(WorkspaceReservationKey key, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.WorkspaceUseReservations.AsNoTracking()
            .Where(r => r.Active)
            .ToListAsync(ct);
        return rows.Where(r => Same(r, key)).Select(ToSnapshot).ToList();
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
        var row = new WorkspaceUseReservation
        {
            Id = Guid.NewGuid(),
            Generation = 1,
            CanonicalPath = command.Key.CanonicalPath,
            SourceFullRef = command.Key.SourceFullRef,
            CommonDirectory = command.Key.CommonDirectory,
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
        if (overlap.Any(r => r.Kind != WorkspaceReservationKind.Retirement || r.RetirementId != command.RetirementId))
        {
            await tx.RollbackAsync(ct);
            return new(false, null, "workspace_in_use");
        }

        var mine = overlap.FirstOrDefault(r => r.Kind == WorkspaceReservationKind.Retirement && r.RetirementId == command.RetirementId);
        if (mine is not null)
        {
            await tx.CommitAsync(ct);
            return new(true, ToSnapshot(mine), null);
        }

        var row = new WorkspaceUseReservation
        {
            Id = Guid.NewGuid(),
            Generation = 1,
            CanonicalPath = command.Key.CanonicalPath,
            SourceFullRef = command.Key.SourceFullRef,
            CommonDirectory = command.Key.CommonDirectory,
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

    private static WorkspaceReservationSnapshot ToSnapshot(WorkspaceUseReservation row) =>
        new(row.Id, row.Generation, row.Kind, row.TaskId, row.SessionId, row.RetirementId, row.Active);

    private static bool Same(WorkspaceUseReservation row, WorkspaceReservationKey key) =>
        PathsEqual(row.CanonicalPath, key.CanonicalPath)
        && string.Equals(row.SourceFullRef, key.SourceFullRef, StringComparison.Ordinal)
        && PathsEqual(row.CommonDirectory, key.CommonDirectory);

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(left, right, StringComparison.Ordinal);
        }
    }
}
