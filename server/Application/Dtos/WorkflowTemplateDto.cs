namespace Antiphon.Server.Application.Dtos;

public record WorkflowTemplateDto(
    Guid Id,
    string Name,
    string Description,
    string YamlDefinition,
    bool IsBuiltIn,
    Guid? TemplateGroupId,
    string? TemplateGroupName,
    bool SelectableStages,
    DateTime CreatedAt,
    DateTime UpdatedAt);
