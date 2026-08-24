using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0166 S3: stored discussion thread on a card (distinct from session-inject /comments).
/// </summary>
public sealed class CardCommentService
{
    public const int MaxBodyLength = 16_000;
    public const int MaxAuthorLength = 200;

    private readonly AppDbContext _db;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;

    public CardCommentService(AppDbContext db, IEventBus eventBus, TimeProvider timeProvider)
    {
        _db = db;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<CardDiscussionCommentDto>> ListAsync(Guid cardId, CancellationToken ct)
    {
        await EnsureCardExistsAsync(cardId, ct);

        var rows = await _db.CardComments
            .AsNoTracking()
            .Where(c => c.CardId == cardId)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<CardDiscussionCommentDto> CreateAsync(
        Guid cardId,
        CreateCardDiscussionRequest request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Body))
            throw new ValidationException(nameof(CreateCardDiscussionRequest.Body), "Comment body is required.");

        var body = request.Body.Trim();
        if (body.Length > MaxBodyLength)
        {
            throw new ValidationException(
                nameof(CreateCardDiscussionRequest.Body),
                $"Comment body must be at most {MaxBodyLength} characters.");
        }

        var author = string.IsNullOrWhiteSpace(request.Author) ? null : request.Author.Trim();
        if (author is { Length: > MaxAuthorLength })
        {
            throw new ValidationException(
                nameof(CreateCardDiscussionRequest.Author),
                $"Author must be at most {MaxAuthorLength} characters.");
        }

        var card = await EnsureCardExistsAsync(cardId, ct);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var comment = new CardComment
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            Body = body,
            Author = author,
            Origin = CardCommentOrigin.Antiphon,
            CreatedAt = now,
            Card = card
        };

        _db.CardComments.Add(comment);
        await _db.SaveChangesAsync(ct);
        await _eventBus.PublishToAllAsync("BoardChanged", new { boardId = card.BoardId }, ct);
        return ToDto(comment);
    }

    private async Task<Card> EnsureCardExistsAsync(Guid cardId, CancellationToken ct)
    {
        return await _db.Cards.FirstOrDefaultAsync(c => c.Id == cardId, ct)
            ?? throw new NotFoundException(nameof(Card), cardId);
    }

    private static CardDiscussionCommentDto ToDto(CardComment comment) =>
        new(
            comment.Id,
            comment.CardId,
            comment.Body,
            comment.Author,
            comment.Origin,
            comment.ExternalCommentId,
            comment.ExternalUrl,
            comment.CreatedAt,
            comment.SyncedAt);
}
