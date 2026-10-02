using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Durable, try-only admission for one bounded cleanup operation. Entry-point integration
/// remains required: maintenance must observe a drained intent before repository or process work.
/// Inventory never calls this service. This service cannot delete, kill or acquire a repository lease.
/// </summary>
public sealed class HostMaintenanceService(IHostMaintenanceStore store, IHostMaintenanceCustodyProbe probe,
    TimeProvider clock)
{
    public Task<HostMaintenanceState> ReadAsync(string storageId, string hostId, CancellationToken ct) =>
        store.ReadOrCreateAsync(storageId, hostId, ct);
    public async Task<HostMaintenanceDecision> ReconcileAsync(string storageId, string hostId, CancellationToken ct) =>
        new(false, "not_implemented", await ReadAsync(storageId, hostId, ct));
    public async Task<HostMaintenanceDecision> RequestAsync(string storageId, string hostId, Guid intentId,
        string kind, CancellationToken ct) => new(false, "not_implemented", await ReadAsync(storageId, hostId, ct));
    public async Task<HostMaintenanceDecision> TryBeginCleanupAsync(string storageId, string hostId, long epoch,
        Guid operationId, HostMaintenanceWorkerIdentity worker, CancellationToken ct) =>
        new(false, "not_implemented", await ReadAsync(storageId, hostId, ct));
    public async Task<HostMaintenanceDecision> CheckCleanupAsync(string storageId, string hostId, long epoch,
        Guid operationId, CancellationToken ct) => new(false, "not_implemented", await ReadAsync(storageId, hostId, ct));
    public async Task<HostMaintenanceDecision> CompleteCleanupAsync(string storageId, string hostId,
        Guid operationId, bool outcomeKnown, CancellationToken ct) =>
        new(false, "not_implemented", await ReadAsync(storageId, hostId, ct));
    public async Task<HostMaintenanceDecision> FinishAsync(string storageId, string hostId, Guid intentId,
        bool outcomeKnown, CancellationToken ct) => new(false, "not_implemented", await ReadAsync(storageId, hostId, ct));
}
