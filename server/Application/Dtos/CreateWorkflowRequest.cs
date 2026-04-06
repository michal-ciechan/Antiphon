namespace Antiphon.Server.Application.Dtos;

public record CreateWorkflowRequest(
    Guid TemplateId,
    Guid ProjectId,
    string? Name,
    string? InitialContext,
    string? FeatureName,
    List<string>? SelectedStages,
    Dictionary<string, string>? StageModelOverrides);
