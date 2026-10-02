using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Antiphon.Server.Infrastructure.Data;

public sealed class HostMaintenanceStore(AppDbContext db, TimeProvider clock) : IHostMaintenanceStore
{
    public async Task<HostMaintenanceState> ReadOrCreateAsync(string storageId, string hostId, CancellationToken ct)
    {
        var row = await db.HostMaintenanceActivities.AsNoTracking().SingleOrDefaultAsync(
            entry => entry.StorageId == storageId, ct);
        if (row is not null) return Map(row);
        row = new() { StorageId = storageId, HostId = hostId, Revision = Guid.NewGuid(),
            UpdatedAt = clock.GetUtcNow().UtcDateTime };
        db.HostMaintenanceActivities.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            db.Entry(row).State = EntityState.Detached;
            return Map(await db.HostMaintenanceActivities.AsNoTracking().SingleAsync(
                entry => entry.StorageId == storageId, ct));
        }
        db.Entry(row).State = EntityState.Detached;
        return Map(row);
    }

    public async Task<bool> TryReplaceAsync(HostMaintenanceState expected, HostMaintenanceState replacement,
        CancellationToken ct)
    {
        if (replacement.StorageId != expected.StorageId || replacement.HostId != expected.HostId ||
            replacement.Revision == expected.Revision || replacement.Epoch < expected.Epoch)
            throw new ArgumentException("Maintenance replacement must preserve identity and advance revision.");
        var worker = replacement.Worker;
        return await db.HostMaintenanceActivities.Where(row => row.StorageId == expected.StorageId &&
            row.Revision == expected.Revision && row.Epoch == expected.Epoch)
            .ExecuteUpdateAsync(update => update
                .SetProperty(row => row.Revision, replacement.Revision)
                .SetProperty(row => row.Epoch, replacement.Epoch)
                .SetProperty(row => row.CustodyReconciled, replacement.CustodyReconciled)
                .SetProperty(row => row.MaintenanceIntentId, replacement.IntentId)
                .SetProperty(row => row.MaintenanceKind, replacement.Kind)
                .SetProperty(row => row.MaintenanceRequestedAt, replacement.RequestedAt)
                .SetProperty(row => row.CleanupOperationId, replacement.OperationId)
                .SetProperty(row => row.WorkerStoreId, worker == null ? null : worker.StoreId)
                .SetProperty(row => row.WorkerBootId, worker == null ? null : worker.BootId)
                .SetProperty(row => row.WorkerPid, worker == null ? (int?)null : worker.Pid)
                .SetProperty(row => row.WorkerStartToken, worker == null ? (long?)null : worker.StartToken)
                .SetProperty(row => row.UpdatedAt, replacement.UpdatedAt), ct) == 1;
    }

    private static HostMaintenanceState Map(HostMaintenanceActivity row) => new(row.StorageId, row.HostId,
        row.Epoch, row.Revision, row.CustodyReconciled, row.MaintenanceIntentId, row.MaintenanceKind,
        row.MaintenanceRequestedAt, row.CleanupOperationId,
        row.WorkerStoreId is { } store && row.WorkerBootId is { } boot && row.WorkerPid is { } pid &&
        row.WorkerStartToken is { } start ? new(store, boot, pid, start) : null, row.UpdatedAt);
}
