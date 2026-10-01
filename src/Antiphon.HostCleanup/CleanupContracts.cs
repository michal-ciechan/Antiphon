namespace Antiphon.HostCleanup;

public enum CleanupDisposition { Eligible, Keep, Unknown }

public enum CleanupFamily
{
    Checkpoint, Probe, LandVerify, TaskScratch, WorkScratch, BuildOutput,
    PrivateCache, TestSandbox
}

public enum OwnerLiveness { Dead, Alive, Reused, Unknown }

public sealed record CleanupRoot(string Path, string StorageId, CleanupFamily Family);

public sealed record CleanupIdentity(string StorageId, string FileId, string OwnerGeneration);

public sealed record CleanupOwner(
    string HostBootId,
    string PidNamespace,
    int Pid,
    DateTimeOffset ProcessStart,
    OwnerLiveness Liveness,
    bool Released,
    bool EvidencePreserved,
    bool SlotBound,
    bool NestedCustodyComplete,
    OwnerLiveness? NestedExecutorLiveness = null,
    bool HasNestedExecutor = false);

public sealed record CleanupEntry(
    string Path,
    bool IsDirectory,
    bool IsLink,
    bool IsMountBoundary,
    bool IsReadable,
    DateTimeOffset? LastWriteUtc,
    long? LogicalBytes,
    long? AllocatedBytes);

public sealed record CleanupCandidateFacts(
    string Path,
    CleanupIdentity? Identity,
    DateTimeOffset? CreatedUtc,
    CleanupOwner? Owner,
    IReadOnlyList<CleanupEntry> Entries,
    bool Complete,
    bool IsWorktree,
    bool IsRetainedEvidence,
    bool HasValidMarker,
    bool IsRegistered,
    string? LocalBootId,
    string? LocalPidNamespace,
    int? TraversedCount = null,
    bool PrivateCachePairOwned = false);

public sealed record CleanupDecision(
    string Path,
    CleanupDisposition Disposition,
    string Reason,
    CleanupIdentity? Identity,
    DateTimeOffset? NewestWriteUtc,
    long? ReservedBytes)
{
    public static CleanupDecision Keep(CleanupCandidateFacts facts, string reason) =>
        new(facts.Path, CleanupDisposition.Keep, reason, facts.Identity, null, null);

    public static CleanupDecision Unknown(CleanupCandidateFacts facts, string reason) =>
        new(facts.Path, CleanupDisposition.Unknown, reason, facts.Identity, null, null);
}

public sealed record CleanupPlan(
    Guid RunId,
    string StorageId,
    DateTimeOffset PlannedUtc,
    IReadOnlyList<CleanupDecision> Decisions,
    string? NextCursor = null);

public enum CleanupOutcomeKind { Removed, Kept, Partial, AlreadyAbsent, Duplicate, Deferred }

public sealed record CleanupOutcome(
    CleanupDecision Decision, CleanupOutcomeKind Kind, string Reason,
    long ReclaimedBytes, Guid ReceiptId);

public sealed record CleanupReceipt(Guid RunId, IReadOnlyList<CleanupOutcome> Outcomes);
