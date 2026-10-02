namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// One storage namespace's cross-process maintenance epoch. Uncertain custody defaults to
/// deletion disabled; elapsed time is not a worker-death observation.
/// </summary>
public sealed class HostMaintenanceActivity
{
    public string StorageId { get; set; } = "";
    public string HostId { get; set; } = "";
    public long Epoch { get; set; }
    public bool CustodyReconciled { get; set; }
    public Guid? MaintenanceIntentId { get; set; }
    public string? MaintenanceKind { get; set; }
    public DateTime? MaintenanceRequestedAt { get; set; }
    public Guid? CleanupOperationId { get; set; }
    public string? WorkerStoreId { get; set; }
    public string? WorkerBootId { get; set; }
    public int? WorkerPid { get; set; }
    public long? WorkerStartToken { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid Revision { get; set; }
}
