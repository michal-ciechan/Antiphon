namespace Antiphon.Server.Domain.Entities;

/// <summary>Original candidate observation and its outcome, retained outside the candidate root.</summary>
public sealed class HostCleanupCandidate
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public int Ordinal { get; set; }
    public string CanonicalPath { get; set; } = "";
    public string StorageId { get; set; } = "";
    public string? FileId { get; set; }
    public string? OwnerGeneration { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? SessionId { get; set; }
    public string Family { get; set; } = "";
    public bool Worktree { get; set; }
    public string Disposition { get; set; } = "";
    public string ReasonCode { get; set; } = "";
    public string ContentClass { get; set; } = "";
    public string ExistingOwner { get; set; } = "";
    public string? OwnerRefusalCode { get; set; }
    public string? Branch { get; set; }
    public string? SourceSha { get; set; }
    public string? PushedSha { get; set; }
    public string? TargetSha { get; set; }
    public DateTime? NewestWriteAt { get; set; }
    public bool ScanComplete { get; set; }
    public long? LogicalBytes { get; set; }
    public long? AllocatedBytes { get; set; }
    public long? ReservedBytes { get; set; }
    public string? Outcome { get; set; }
    public long ReclaimedBytes { get; set; }
    public HostCleanupRun Run { get; set; } = null!;
}
