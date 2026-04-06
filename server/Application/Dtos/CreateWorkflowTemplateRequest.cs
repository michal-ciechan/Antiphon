namespace Antiphon.Server.Application.Dtos;

public record CreateWorkflowTemplateRequest(
    string Name,
    string Description,
    string YamlDefinition,
    Guid? TemplateGroupId);
