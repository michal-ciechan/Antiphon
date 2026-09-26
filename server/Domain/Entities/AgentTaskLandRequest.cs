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

    /// <summary>
    /// CARD-0641: the holder a Held caller note was last minted for. Null means no Held note
    /// exists for this request; <c>unknown</c> means one exists without identified ownership;
    /// <c>task:&lt;guid-N&gt;</c> is a known task anchor. Diagnostic Held events are not gated by it.
    /// </summary>
    public string? HoldNotificationOwnerKey { get; set; }
    public DateTime? WarningAt { get; set; }
    public DateTime? ErrorAt { get; set; }
    public string? ReconciliationError { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();

    public int SchemaVersion { get; set; } = 1;
    public string? ExpectedSourceSha { get; set; }
    public Guid? ReviewEvidenceId { get; set; }
    public LandRecoveryMode RecoveryMode { get; set; }
    public AgentTaskStatus? RecoveryOwnerStatus { get; set; }
    public Guid? RecoverySourceTaskId { get; set; }
    public string? RecoverySourceFullRef { get; set; }
    public string? RecoverySourceFingerprint { get; set; }
    public Guid? SupersedesRequestId { get; set; }
    public string? RecoveryStartBaseSha { get; set; }
    public string? RecoveryLocalBeforeSha { get; set; }
    public string? RecoveryOwnerRemoteBeforeSha { get; set; }
    public string? RecoveryOwnerRemoteAfterSha { get; set; }
    public string? RecoveryRelationship { get; set; }
    public bool? RecoveryPatchesContained { get; set; }
    public string? RecoveryUncontainedPatches { get; set; }
    public DateTime? RecoveryAdoptedAt { get; set; }
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

    public string? TerminalFailureCode { get; set; }
    public Guid? FailureDiagnosticId { get; set; }
    public string? FailureExceptionType { get; set; }
    public string? SourceDiagnosticCommand { get; set; }
    public int? SourceDiagnosticExitCode { get; set; }
    public string? SourceDiagnosticCode { get; set; }
    public string? SourceDiagnosticExceptionType { get; set; }

    public bool CleanupOnly { get; set; }
    public Guid? RequiredLandingOperationId { get; set; }
    public Guid? SweepRunId { get; set; }
    public LandRequestOrigin Origin { get; set; }
}
