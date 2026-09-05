namespace Antiphon.Server.Application.Dtos;

public sealed record IssueDelegationCapabilityRequest(
    string Name,
    IReadOnlyList<string> Roots,
    Guid? BoardId = null,
    Guid? ProjectId = null);

/// <summary>
/// Returned by issue and rotate only. GET list/detail use <see cref="DelegationCapabilityDto"/>,
/// which has no token-shaped property.
/// </summary>
public sealed record DelegationCapabilityIssuedDto(
    Guid Id,
    string Name,
    IReadOnlyList<string> Roots,
    Guid? BoardId,
    Guid? ProjectId,
    string Token,
    string StorePath,
    DateTime CreatedAt,
    DateTime? LastUsedAt,
    DateTime? RotatedAt,
    DateTime? RevokedAt);

/// <summary>
/// Metadata a GET may show. Deliberately has no Token / RawToken / Secret / Bearer property.
/// </summary>
public sealed record DelegationCapabilityDto(
    Guid Id,
    string Name,
    IReadOnlyList<string> Roots,
    Guid? BoardId,
    Guid? ProjectId,
    DateTime CreatedAt,
    DateTime? LastUsedAt,
    DateTime? RotatedAt,
    DateTime? RevokedAt);
