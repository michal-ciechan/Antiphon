namespace Antiphon.Server.Application.Dtos;

public record CreateWorkflowRequest(
    Guid TemplateId,
    Guid ProjectId,
    string? Name,
    string? InitialContext);
