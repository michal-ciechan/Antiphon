using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public sealed record WorktreeHandoffDispositionDto(
    PipelineHandoffKind? NextStage,
    WorktreeHandoffDispositionKind Kind,
    Guid? ConsumedByTaskId,
    Guid? EvidenceId,
    string Reason);

public sealed record ReleaseWorktreeRetirementRequest(
    Guid ExpectedTaskRevision,
    string SourceSha,
    string? ReportDigest,
    bool NoFurtherWorkspaceUse,
    string Reason,
    IReadOnlyList<WorktreeHandoffDispositionDto>? HandoffDispositions = null,
    bool MissingReportReviewed = false);

public sealed record WorktreeRetirementDto(
    Guid Id,
    Guid TaskId,
    int TaskAttempt,
    WorktreeRetirementState State,
    Guid ReleasedTaskRevision,
    string SourceSha,
    string SourceFullRef,
    string WorktreePath,
    DateTime ReleasedAt,
    DateTime? ClaimedAt,
    DateTime? CommandStartedAt,
    bool? DirectoryRemoved,
    bool? RegistrationRemoved,
    bool? BranchRemoved,
    string? LastReason);

public sealed record WorktreeResiduePreviewRequest(
    Guid? ProjectId = null,
    Guid? BoardId = null,
    string? Unscoped = null);

public sealed record WorktreeResidueRunDto(
    Guid Id,
    DateTime StartedAt,
    DateTime? FinishedAt,
    bool Execute,
    bool Preview,
    int ActionBudget,
    int ActionsAccepted,
    int Candidates,
    int Held,
    int Deferred,
    int Queued,
    int Refused,
    int Partial,
    int Removed,
    IReadOnlyList<WorktreeResidueCandidateDto> Rows,
    int Page,
    int PageSize,
    int TotalRows);

public sealed record WorktreeResidueCandidateDto(
    Guid Id,
    string Lane,
    string Outcome,
    string ReasonCode,
    Guid? TaskId,
    Guid? RetirementId,
    Guid? LandingOperationId,
    Guid? LandRequestId,
    string? Path,
    string? Branch,
    bool? DirectoryRemoved,
    bool? RegistrationRemoved,
    bool? BranchRemoved);

public sealed record WorkspaceReservationKey(
    string CanonicalPath,
    string SourceFullRef,
    string CommonDirectory);

public sealed record WorkspaceReservationSnapshot(
    Guid Id,
    int Generation,
    WorkspaceReservationKind Kind,
    Guid? TaskId,
    Guid? SessionId,
    Guid? RetirementId,
    bool Active);

public sealed record WorkspaceReservationCommand(
    WorkspaceReservationKey Key,
    WorkspaceReservationKind Kind,
    Guid? TaskId = null,
    Guid? SessionId = null,
    Guid? RetirementId = null,
    int? ExpectedGeneration = null);

public sealed record WorkspaceReservationCommitResult(bool Accepted, WorkspaceReservationSnapshot? Snapshot, string? Reason);
