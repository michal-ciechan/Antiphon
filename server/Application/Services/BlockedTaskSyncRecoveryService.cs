using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Recovers accepted publication debt, including while new parking is disabled. Claims and
/// results commit separately from Git. This service cannot settle, approve, release or launch.
/// </summary>
public sealed class BlockedTaskSyncRecoveryService(AppDbContext db, RemoteWorkspaceService workspace,
    TaskParkPublicationService publication, IEventBus events, TimeProvider clock,
    ILogger<BlockedTaskSyncRecoveryService> logger)
{
    internal Func<string, CancellationToken, Task>? BoundaryAsync { get; set; }

    public async Task<int> SweepAsync(CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
            || db.ChangeTracker.HasChanges()) return 0;
        var now = clock.GetUtcNow().UtcDateTime;
        var ids = await db.AgentTaskParks.AsNoTracking()
            .Where(p => p.SyncState == AgentTaskParkSyncState.Pending && p.SyncNextAttemptAt <= now)
            .OrderBy(p => p.SyncNextAttemptAt).ThenBy(p => p.Id).Select(p => p.Id).Take(32).ToListAsync(ct);
        var attempted = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var claim = await ClaimAsync(id, now, ct);
                if (claim is null) continue;
                attempted++;
                if (BoundaryAsync is not null) await BoundaryAsync("claimed", ct);
                var result = await workspace.SyncParkedAsync(claim.Task, claim.Park, ct);
                if (BoundaryAsync is not null) await BoundaryAsync("synced", ct);
                if (!await SaveAsync(claim, result, ct)) continue;
                if (BoundaryAsync is not null) await BoundaryAsync("saved", ct);
                // Missed invalidation is recoverable from the persisted projection on fresh GET.
                await events.PublishToGroupAsync("dashboard", "AgentTaskChanged",
                    new { taskId = claim.Task.Id, rootId = claim.Task.RootTaskId }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // The claim already paced this item. Never let a poison row starve its neighbor,
                // or persist Git stderr/endpoint text in an actionable reason code.
                logger.LogWarning("Park sync {ParkId} remains pending ({Type})", id, ex.GetType().Name);
            }
        }
        return attempted;
    }

    private async Task<Claim?> ClaimAsync(Guid id, DateTime due, CancellationToken ct)
    {
        var snapshot = await db.AgentTaskParks.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (snapshot is null) return null;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockTaskAsync(snapshot.TaskId, ct);
        var park = await db.AgentTaskParks.AsNoTracking().SingleAsync(p => p.Id == id, ct);
        if (park.SyncState != AgentTaskParkSyncState.Pending || park.SyncNextAttemptAt is null
            || park.SyncNextAttemptAt > due) return null;
        var task = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == park.TaskId, ct);
        // Reuse the stored, digest-bound proof. This is a DB-only check: a released runner
        // need not still be online for its desktop debt to recover.
        var proof = await publication.ReadEvidenceAsync(id, ct);
        if (task is null || proof is null || proof.SourceSha != park.SyncSourceSha)
        {
            await db.AgentTaskParks.Where(p => p.Id == id && p.Revision == park.Revision)
                .ExecuteUpdateAsync(u => u.SetProperty(p => p.SyncState, AgentTaskParkSyncState.Held)
                    .SetProperty(p => p.SyncReasonCode, "park_sync_episode_changed")
                    .SetProperty(p => p.SyncNextAttemptAt, (DateTime?)null)
                    .SetProperty(p => p.UpdatedAt, due).SetProperty(p => p.Revision, p => p.Revision + 1), ct);
            await tx.CommitAsync(ct);
            return null;
        }
        var attempts = park.SyncAttempts + 1;
        var next = due.AddMinutes(attempts switch { 1 => 1, 2 => 2, 3 => 4, _ => 5 });
        var changed = await db.AgentTaskParks.Where(p => p.Id == id && p.Revision == park.Revision)
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.SyncAttempts, attempts)
                .SetProperty(p => p.SyncNextAttemptAt, next).SetProperty(p => p.SyncReasonCode, "park_sync_pending")
                .SetProperty(p => p.UpdatedAt, due).SetProperty(p => p.Revision, p => p.Revision + 1), ct);
        if (changed != 1) return null;
        await tx.CommitAsync(ct);
        park.Revision++; park.SyncAttempts = attempts; park.SyncNextAttemptAt = next;
        return new(task, park);
    }

    private async Task<bool> SaveAsync(Claim claim, RemoteSettlementSyncResult result, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockTaskAsync(claim.Task.Id, ct);
        var task = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == claim.Task.Id, ct);
        if (task is null || task.Status != AgentTaskStatus.Blocked || task.Attempt != claim.Task.Attempt
            || task.ConcurrencyToken != claim.Task.ConcurrencyToken
            || task.ProgressBaselineJson != claim.Task.ProgressBaselineJson
            || await publication.ReadEvidenceAsync(claim.Park.Id, ct) is null) return false;
        var ready = result.Confirmed && result.DesktopAfterSha == claim.Park.SyncSourceSha
            && result.RemoteSha == claim.Park.SyncSourceSha && result.FullRef == claim.Park.FullRef;
        var held = result.State is RemoteSettlementSyncState.Refused or RemoteSettlementSyncState.NotApplicable
            || result.Reason == RemoteSettlementSyncReasons.BaselineUnavailable
            || result.Reason == RemoteSettlementSyncReasons.BranchNotPushed;
        var state = ready ? AgentTaskParkSyncState.Ready : held ? AgentTaskParkSyncState.Held : AgentTaskParkSyncState.Pending;
        var reason = ready ? "park_source_ready" : result.Reason ?? "park_sync_unavailable";
        var now = clock.GetUtcNow().UtcDateTime;
        var changed = await db.AgentTaskParks.Where(p => p.Id == claim.Park.Id && p.Revision == claim.Park.Revision
                && p.SyncState == AgentTaskParkSyncState.Pending && p.SyncSourceSha == claim.Park.SyncSourceSha)
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.SyncState, state)
                .SetProperty(p => p.SyncReasonCode, reason)
                .SetProperty(p => p.SyncNextAttemptAt, state == AgentTaskParkSyncState.Pending ? claim.Park.SyncNextAttemptAt : null)
                .SetProperty(p => p.SourceReadyAt, ready ? now : (DateTime?)null)
                .SetProperty(p => p.UpdatedAt, now).SetProperty(p => p.Revision, p => p.Revision + 1), ct);
        await tx.CommitAsync(ct);
        return changed == 1;
    }

    private Task<int> LockTaskAsync(Guid taskId, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync(
        $"""SELECT "Id" FROM "AgentTasks" WHERE "Id" = {taskId} FOR UPDATE""", ct);

    private sealed record Claim(AgentTask Task, AgentTaskPark Park);
}
