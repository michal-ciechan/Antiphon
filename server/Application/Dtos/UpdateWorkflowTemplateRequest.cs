namespace Antiphon.Server.Application.Dtos;

public record UpdateWorkflowTemplateRequest(
    string Name,
    string Description,
    string YamlDefinition,
    Guid? TemplateGroupId);
