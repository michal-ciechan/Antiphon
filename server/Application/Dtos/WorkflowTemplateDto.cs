namespace Antiphon.Server.Application.Dtos;

public record WorkflowTemplateDto(
    Guid Id,
    string Name,
    string Description,
    string YamlDefinition,
    bool IsBuiltIn,
    DateTime CreatedAt,
    DateTime UpdatedAt);
