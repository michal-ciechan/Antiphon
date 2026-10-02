namespace Antiphon.Server.Application.Interfaces;

/// <summary>Storage-scoped compare-and-swap. No TTL grants custody or clears a live operation.</summary>
public interface IHostMaintenanceStore
{
    Task<HostMaintenanceState> ReadOrCreateAsync(string storageId, string hostId, CancellationToken ct);
    Task<bool> TryReplaceAsync(HostMaintenanceState expected, HostMaintenanceState replacement, CancellationToken ct);
}

public sealed record HostMaintenanceWorkerIdentity(string StoreId, string BootId, int Pid, long StartToken);
public sealed record HostMaintenanceState(string StorageId, string HostId, long Epoch, Guid Revision,
    bool CustodyReconciled, Guid? IntentId, string? Kind, DateTime? RequestedAt,
    Guid? OperationId, HostMaintenanceWorkerIdentity? Worker, DateTime UpdatedAt);
public sealed record HostMaintenanceDecision(bool Accepted, string Reason, HostMaintenanceState State);
public enum HostMaintenanceCustody { Unknown, Live, Stopped }

/// <summary>
/// Stopped requires a complete namespace/process inventory, including the exact prior worker
/// store, boot, PID and start token. Missing connection or an elapsed timeout returns Unknown.
/// </summary>
public interface IHostMaintenanceCustodyProbe
{
    Task<HostMaintenanceCustody> ReadAsync(HostMaintenanceState state, CancellationToken ct);
}
