namespace Antiphon.Server.Domain.Entities;

/// <summary>A protected host plan/receipt. Worktree inventory never grants removal authority.</summary>
public sealed class HostCleanupRun
{
    public Guid Id { get; set; }
    public Guid BoardId { get; set; }
    public string HostId { get; set; } = "";
    public string StorageId { get; set; } = "";
    public string RunnerStoreId { get; set; } = "";
    public string ProcessBootId { get; set; } = "";
    public string SourceSha { get; set; } = "";
    public string ConfigDigest { get; set; } = "";
    public string PlanDigest { get; set; } = "";
    public string? ReceiptDigest { get; set; }
    public DateOnly LocalDate { get; set; }
    public bool Daily { get; set; }
    public bool Execute { get; set; }
    public bool Complete { get; set; }
    public string Status { get; set; } = "planned";
    public DateTime PlannedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public DateTime? SampledAt { get; set; }
    public bool SampleComplete { get; set; }
    public long? NamespaceAllocatedBytes { get; set; }
    public long? DiskCapacityBytes { get; set; }
    public long? FreeBytesBefore { get; set; }
    public long? FreeBytesAfter { get; set; }
    public int AttemptLimit { get; set; }
    public long ByteLimit { get; set; }
    public int Attempts { get; set; }
    public long ReservedBytes { get; set; }
    public long ReclaimedBytes { get; set; }
    public long EligibleWorktreeBytes { get; set; }
    public string? NextCursor { get; set; }
    public bool InvalidationPending { get; set; }
    public List<HostCleanupCandidate> Candidates { get; set; } = [];
}
