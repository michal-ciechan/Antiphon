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
