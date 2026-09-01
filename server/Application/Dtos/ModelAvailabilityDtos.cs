using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// GET /api/model-availability (CARD-0022 S4). PUT/DELETE (CARD-0309) return
/// <see cref="ModelAvailabilityHoldDto"/> on the same shape.
/// </summary>
public sealed record ModelAvailabilityDto(
    IReadOnlyList<ModelAvailabilityHoldDto> Holds,
    IReadOnlyList<string> Available);

/// <summary>
/// PUT /api/model-availability/{kind}/{alias} (CARD-0309). Omitted or null
/// <see cref="DisabledUntil"/> is open-ended (until DELETE). A past timestamp is 422.
/// </summary>
public sealed record PutModelAvailabilityRequest(
    DateTimeOffset? DisabledUntil = null,
    string? Reason = null);

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
