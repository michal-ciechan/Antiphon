using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public sealed record CardDiscussionCommentDto(
    Guid Id,
    Guid CardId,
    string Body,
    string? Author,
    CardCommentOrigin Origin,
    string? ExternalCommentId,
    string? ExternalUrl,
    DateTime CreatedAt,
    DateTime? SyncedAt);

public sealed record CreateCardDiscussionRequest(
    string Body,
    string? Author = null);
