using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// GET /api/model-availability (CARD-0022 S4). CARD-0309 later adds PUT/DELETE on the same shape.
/// </summary>
public sealed record ModelAvailabilityDto(
    IReadOnlyList<ModelAvailabilityHoldDto> Holds,
    IReadOnlyList<string> Available);

public sealed record ModelAvailabilityHoldDto(
    Guid Id,
    string Kind,
    string ModelAlias,
    ModelAvailabilitySource Source,
    DateTime? DisabledUntil,
    DateTime HitAt,
    string Reason,
    string? RawText,
    Guid? SourceSessionId,
    Guid? SourceTaskId);
