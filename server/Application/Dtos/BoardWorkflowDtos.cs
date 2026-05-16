namespace Antiphon.Server.Application.Dtos;

public sealed record BoardWorkflowDto(
    Guid BoardId,
    Guid? DefinitionId,
    int Version,
    string Name,
    string Content,
    string? FilePath,
    DateTime? UpdatedAt);

public sealed record UpdateBoardWorkflowRequest(string Content);
