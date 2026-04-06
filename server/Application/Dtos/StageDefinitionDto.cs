namespace Antiphon.Server.Application.Dtos;

public record StageDefinitionDto(
    string Name,
    string ExecutorType,
    string? ModelName,
    bool GateRequired,
    string? SystemPrompt);
