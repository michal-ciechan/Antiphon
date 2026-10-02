using System.Collections.Immutable;

namespace Antiphon.Server.Application.Dtos;

public enum HostCleanupWorktreeDisposition { WouldRemove, Keep, Unknown }

public enum HostCleanupWorktreeKind { Registered, RunnerMirror, Unregistered, Slot }

/// <summary>Read-only observations. A null fact means its source could not prove the answer.</summary>
public sealed record HostCleanupWorktreeFacts(
    string Path,
    string StorageId,
    string? OwnerGeneration,
    Guid? TaskId,
    HostCleanupWorktreeKind Kind,
    long? AllocatedBytes,
    DateTimeOffset? NewestWriteUtc,
    bool? IsDirty,
    bool? HasUntrackedSource,
    bool? IsTaskActive,
    bool? HasLiveSession,
    bool? HasPendingLandOrRecovery,
    bool? IsPushed,
    bool? IsContainedInPushedBranch,
    bool IsCodeOrSourceLanding,
    bool? HasRelease,
    bool? EvidencePreserved,
    ImmutableArray<string> IgnoredPaths,
    bool ScanComplete,
    string ExistingOwner);

/// <summary>Inventory-only classification. No value in this record authorizes a delete.</summary>
public sealed record HostCleanupWorktreeInventoryRow(
    HostCleanupWorktreeFacts Facts,
    HostCleanupWorktreeDisposition Disposition,
    string Reason,
    WorktreeIgnoredContent Content,
    string? OwnerAvailabilityReason = null);

public sealed record HostCleanupWorktreeInventorySummary(
    int WouldRemove,
    long WouldRemoveBytes,
    int Keep,
    int Unknown,
    IReadOnlyDictionary<string, (int Count, long Bytes)> EligibleByOwner);

public sealed record HostCleanupWorktreeInventoryResult(
    IReadOnlyList<HostCleanupWorktreeInventoryRow> Rows, bool Complete, string? IncompleteReason);

public sealed record HostCleanupReportedCandidate(
    string CanonicalPath, string StorageId, string? FileId, string? OwnerGeneration,
    string Family, bool Worktree, string Disposition, string ReasonCode,
    string ContentClass, string ExistingOwner, string? OwnerRefusalCode,
    DateTime? NewestWriteAt, bool ScanComplete, long? LogicalBytes, long? AllocatedBytes,
    long? ReservedBytes, string Outcome, long ReclaimedBytes);

/// <summary>Metadata-only completed receipt. No file contents, environment or raw stderr.</summary>
public sealed record HostCleanupReceiptDto(
    Guid RunId, Guid BoardId, string HostId, string StorageId, string RunnerStoreId,
    string ProcessBootId, string SourceSha, string ConfigDigest, string PlanDigest,
    DateOnly LocalDate, bool Daily, bool Execute, bool Complete, DateTime PlannedAt,
    DateTime FinishedAt, int AttemptLimit, long ByteLimit, int Attempts, long ReservedBytes,
    IReadOnlyList<HostCleanupReportedCandidate> Candidates,
    DateTime? SampledAt = null, bool SampleComplete = false,
    long? NamespaceAllocatedBytes = null, long? DiskCapacityBytes = null,
    long? FreeBytesBefore = null, long? FreeBytesAfter = null);

public sealed record HostCleanupReportPage(
    Guid RunId, Guid BoardId, string HostId, string StorageId, string ReceiptDigest,
    bool Complete, long ReclaimedBytes, long EligibleWorktreeBytes,
    int TotalCandidates, IReadOnlyList<HostCleanupReportedCandidate> Candidates,
    int? NextOffset);
