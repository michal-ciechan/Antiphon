using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.ValueObjects;

namespace Antiphon.Server.Application.Dtos;

public sealed record PipelineDefinitionRevisionDto(
    Guid Id, int RevisionNumber, string ContentHash,
    IReadOnlyList<PipelineStageSpec> Stages, string? ChangeNote, DateTime CreatedAt);

public sealed record PipelineDefinitionDto(
    Guid Id, string Name, string Description, PipelineDefinitionSource Source,
    Guid? ActiveRevisionId, PipelineDefinitionRevisionDto? ActiveRevision,
    DateTime? ArchivedAt, string? ArchivedReason, string? ArchivedBy,
    DateTime CreatedAt, DateTime UpdatedAt);

public sealed record PipelineResolutionDto(
    Guid DefinitionId, string Name, Guid RevisionId,
    int RevisionNumber, string Hash, string Source);

public sealed record CreatePipelineDefinitionRequest(
    string Name, string Description, IReadOnlyList<PipelineStageSpec> Stages);

public sealed record AddPipelineDefinitionRevisionRequest(
    IReadOnlyList<PipelineStageSpec> Stages, string? ChangeNote);

public sealed record ClonePipelineDefinitionRequest(string Name);
public sealed record PipelineDefinitionPointerRequest(Guid? PipelineDefinitionId);
public sealed record PipelineDefinitionArchiveRequest(string? Reason = null, string? Actor = null);
