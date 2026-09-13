using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public sealed record CapturePinnedInstructionRequest(
    Guid RequestId,
    int ExpectedRevision,
    string Text,
    string? SourceNamespace = null,
    string? SourceKey = null,
    string? SourceRef = null,
    Guid? ReplacesPinId = null,
    Guid? RepinsPinId = null,
    PinInstructionSource? Source = null,
    PinClaudeImportMode? PinClaudeImportMode = null);

public sealed record RevokePinnedInstructionRequest(
    Guid RequestId,
    int ExpectedRevision);

public sealed record ReconcilePinnedInstructionsRequest(
    int ExpectedRevision);

public sealed record PinnedInstructionDto(
    Guid Id,
    string Text,
    PinInstructionSource Source,
    string? SourceNamespace,
    string? SourceKey,
    string? SourceRef,
    DateTime CreatedAt,
    Guid? CreatedByUserId,
    Guid? CreatedBySessionId,
    DateTime? RevokedAt,
    Guid? RevokedByUserId,
    Guid? RevokedBySessionId,
    Guid? SupersedesPinId);

public sealed record PinProjectionDto(
    Guid Id,
    string CanonicalHost,
    string CanonicalCwd,
    string TargetRelativePath,
    string TargetAbsolutePath,
    int LocationGeneration,
    int DesiredRevision,
    int? ProjectedRevision,
    PinProjectionStatus Status,
    string? Error,
    PinImportStatus ImportStatus,
    PinClaudeImportMode ImportMode);

public sealed record PinReconciliationDto(
    int DesiredRevision,
    string DesiredHash,
    PinProjectionStatus Status,
    string? Error);

public sealed record PinnedInstructionMutationResult(
    PinnedInstructionSetDto Set,
    bool CreatedNewRow);

public sealed record PinnedInstructionSetDto(
    Guid AgentId,
    int Revision,
    string? ContentHash,
    DateTime? FirstUsedAt,
    bool RuntimeSupported,
    AgentKind Kind,
    PinClaudeImportMode PinClaudeImportMode,
    IReadOnlyList<PinnedInstructionDto> Pins,
    IReadOnlyList<PinProjectionDto> Projections,
    PinReconciliationDto? Reconciliation,
    string? OwnSessionReadTarget,
    int PendingCleanupCount);

public sealed record AgentPinnedInstructionsChangedDto(
    Guid AgentId,
    int Revision,
    string Hash);
