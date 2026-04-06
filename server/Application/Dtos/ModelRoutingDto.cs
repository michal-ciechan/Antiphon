namespace Antiphon.Server.Application.Dtos;

public record ModelRoutingDto(
    Guid Id,
    string StageName,
    string ModelName,
    Guid ProviderId,
    Guid? WorkflowTemplateId,
    DateTime CreatedAt);

public record CreateModelRoutingRequest(
    string StageName,
    string ModelName,
    Guid ProviderId);

public record UpdateModelRoutingRequest(
    string StageName,
    string ModelName,
    Guid ProviderId);
