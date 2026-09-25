using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public sealed record RunnerKindDefaultDto(AgentKind AgentKind, string RunnerId, string? InheritedRunnerId, string Source);

public sealed record RunnerDefaultsDto(
    long Revision,
    string? GlobalRunnerId,
    IReadOnlyList<RunnerKindDefaultDto> KindDefaults,
    DateTime UpdatedAt,
    string? LastReason,
    string? LastProvenance,
    Guid? LastCallerTaskId,
    IReadOnlyList<string> SupportedKinds,
    IReadOnlyList<string> UnresolvedReferences);

public sealed record PutRunnerKindDefault(AgentKind AgentKind, string RunnerId);

public sealed record PutRunnerDefaultsRequest(
    long ExpectedRevision,
    string? GlobalRunnerId,
    IReadOnlyList<PutRunnerKindDefault> KindDefaults,
    string Reason,
    string Provenance);

public sealed record RunnerDefaultsRevisionDto(
    long Revision,
    long? PreviousRevision,
    string? GlobalRunnerId,
    IReadOnlyList<RunnerKindDefaultDto> KindDefaults,
    DateTime CreatedAt,
    string Reason,
    string Provenance,
    Guid? CallerTaskId);

public sealed record RunnerDefaultsRevisionPageDto(
    IReadOnlyList<RunnerDefaultsRevisionDto> Revisions,
    long? NextBeforeRevision);
