using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public record WorkflowDetailDto(
    Guid Id,
    string Name,
    string Description,
    WorkflowStatus Status,
    string? CurrentStageName,
    Guid TemplateId,
    string TemplateName,
    Guid ProjectId,
    string ProjectName,
    int StageCount,
    int CompletedStageCount,
    IReadOnlyList<WorkflowStatus> AvailableTransitions,
    IReadOnlyList<StageDto> Stages,
    string? FeatureName,
    DateTime CreatedAt,
    DateTime UpdatedAt);
