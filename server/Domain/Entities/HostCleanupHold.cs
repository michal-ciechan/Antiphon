namespace Antiphon.Server.Domain.Entities;

/// <summary>Expiry requests review; only explicit disposition lifts protection.</summary>
public sealed class HostCleanupHold
{
    public Guid Id { get; set; }
    public Guid BoardId { get; set; }
    public string HostId { get; set; } = "";
    public string StorageId { get; set; } = "";
    public string? CanonicalPath { get; set; }
    public Guid? TaskId { get; set; }
    public string? OwnerGeneration { get; set; }
    public string? UnresolvedTaskPrefix { get; set; }
    public string Reason { get; set; } = "";
    public string Creator { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? DisposedAt { get; set; }
    public string? DispositionReason { get; set; }
    public Guid Revision { get; set; }
}
