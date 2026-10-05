using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public enum AgentTaskParkState { Requested, Published, ReleasePending, Parked, Held, ResumePending, Resumed }
public enum AgentTaskParkSyncState { NotRequired, Pending, Ready, Held }

/// <summary>
/// One blocked attempt/event's durable custody record. Historical coordinates have no cascading
/// foreign keys. A state alone is never publication or process-exit evidence; the corresponding
/// immutable receipt and the existing RunnerSeatRelease ledger own those facts.
/// </summary>
public sealed class AgentTaskPark
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public int Attempt { get; set; }
    public Guid BlockEventId { get; set; }
    public Guid TaskConcurrencyToken { get; set; }
    public Guid? AgentId { get; set; }
    public Guid? SessionId { get; set; }
    public string? RunnerId { get; set; }
    public Guid? RunnerStoreId { get; set; }
    public DateTime? AcceptedStartedAt { get; set; }
    public WorkspaceMode Workspace { get; set; }
    public Guid? WorktreeId { get; set; }
    public string? WorktreePath { get; set; }
    public string? RemoteWorktreePath { get; set; }
    public string? FullRef { get; set; }
    public string? BaselineSha { get; set; }
    public string? RepositoryIdentity { get; set; }
    public string? EndpointFingerprint { get; set; }
    public string? SourceSha { get; set; }
    public string? VerifiedRemoteSha { get; set; }
    public Guid? PublicationReceiptId { get; set; }
    public string? PublicationReceiptDigest { get; set; }
    public Guid? RunnerSeatReleaseId { get; set; }
    public string? ReportReference { get; set; }
    public string? ReportDigest { get; set; }
    public long? TranscriptSequence { get; set; }
    public DateTime BlockedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public AgentTaskParkState State { get; set; }
    public AgentTaskParkState? HeldFromState { get; set; }
    public string ReasonCode { get; set; } = "park_requested";
    public long Revision { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? ReleasePendingAt { get; set; }
    public DateTime? ParkedAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    // Sync debt is independent of physical custody and retains its exact source on restart.
    public AgentTaskParkSyncState SyncState { get; set; }
    public string? SyncSourceSha { get; set; }
    public string? SyncReasonCode { get; set; }
    public int SyncAttempts { get; set; }
    public DateTime? SyncNextAttemptAt { get; set; }
    public DateTime? SourceReadyAt { get; set; }
    // References the existing accepted-answer journal; never a second copy of the answer.
    public Guid? ResumeInputEventId { get; set; }
    public int? ResumeAttempt { get; set; }
    public Guid? ResumeSessionId { get; set; }
    public DateTime? ResumePendingAt { get; set; }
    public DateTime? ResumedAt { get; set; }
}
