namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// CARD-0658: a one-time dashboard login link. <see cref="LoginPath"/> carries a single-use nonce
/// valid until <see cref="ExpiresAt"/>; it is a credential for that window and is never logged.
/// </summary>
public sealed record OperatorDashboardLoginDto(string LoginPath, DateTimeOffset ExpiresAt);
