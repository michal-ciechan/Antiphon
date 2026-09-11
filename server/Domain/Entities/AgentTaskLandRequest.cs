using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>An accepted land, including time spent waiting before an operation exists.</summary>
public sealed class AgentTaskLandRequest
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public DateTime RequestedAt { get; set; }
    public string? VerifyFilter { get; set; }
    public AgentTaskReplyTo ReplyTo { get; set; }
    public Guid? ParentSessionId { get; set; }
    public LandRequestState State { get; set; }
    public bool IsPending { get; set; } = true;
    public Guid? LandingOperationId { get; set; }
    public Guid? TerminalEventId { get; set; }
    public DateTime? StartedAt { get; set; }
    public int Attempt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime LastEvaluatedAt { get; set; }
    public DateTime LastProgressAt { get; set; }
    public int HighestProgress { get; set; } = -2;
    public string? HoldReasonCode { get; set; }
    public string? HoldDetail { get; set; }
    public Guid? HoldingTaskId { get; set; }
    public AgentTaskStatus? HoldingTaskStatus { get; set; }
    public DateTime? HeldSince { get; set; }
    public int HoldEpisode { get; set; }
    public DateTime? WarningAt { get; set; }
    public DateTime? ErrorAt { get; set; }
    public string? ReconciliationError { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();

    public int SchemaVersion { get; set; } = 1;
    public string? ExpectedSourceSha { get; set; }
    public Guid? ReviewEvidenceId { get; set; }
    public LandApprovalKind ApprovalKind { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? SourceFullRefSnapshot { get; set; }
    public string? RepositoryPathSnapshot { get; set; }
    public string? WorktreePathSnapshot { get; set; }
    public string? TargetFullRefSnapshot { get; set; }

    public LandSourceResolutionState SourceResolutionState { get; set; }
    public string? LocalBeforeSha { get; set; }
    public string? RemoteSourceSha { get; set; }
    public string? RemoteSourceRef { get; set; }
    public string? RemoteSourceFingerprint { get; set; }
    public string? SourceObservationRef { get; set; }
    public DateTime? SourceObservedAt { get; set; }
    public string? ResolvedSourceSha { get; set; }
    public string? SourceCommonDirectory { get; set; }
    public string? SourceWorktreePath { get; set; }
    public string? SourceGitDirectory { get; set; }
    public LandSourceRelationship SourceRelationship { get; set; }
    public string? SourceRefusalReason { get; set; }
    public string? CandidateSourceSha { get; set; }

    public int? SourceAdvanceChildProcessId { get; set; }
    public long? SourceAdvanceChildStartTicks { get; set; }
    public string? SourceAdvanceChildOperation { get; set; }
}
