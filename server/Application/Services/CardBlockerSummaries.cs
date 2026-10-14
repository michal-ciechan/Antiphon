using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Shared blocker projection for card, card-list and board reads.</summary>
internal static class CardBlockerSummaries
{
    /// <summary>
    /// Loads every requested card's BlockedBy summaries in one query, including resolved rows.
    /// Resolution uses the same predicate as dispatch admission (CardBlockers).
    /// </summary>
    internal static async Task<Dictionary<Guid, List<CardBlockerSummaryDto>>> LoadAsync(
        AppDbContext db, IReadOnlyCollection<Guid> cardIds, CancellationToken ct)
    {
        if (cardIds.Count == 0)
            return [];

        var rows = await db.CardRelationships.AsNoTracking()
            .Where(r => cardIds.Contains(r.CardId) && r.Kind == CardRelationshipKind.BlockedBy)
            .Select(r => new { r.CardId, r.RelatedCard.Identifier, r.RelatedCard.Status, r.RelatedCard.ArchivedAt })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.CardId).ToDictionary(
            g => g.Key,
            g => g.OrderBy(r => r.Identifier, StringComparer.Ordinal)
                .Select(r => new CardBlockerSummaryDto(
                    r.Identifier, r.Status, CardBlockers.IsResolved(r.Status, r.ArchivedAt)))
                .ToList());
    }

    internal static CardDto Attach(CardDto dto, IReadOnlyDictionary<Guid, List<CardBlockerSummaryDto>> blockers) =>
        blockers.TryGetValue(dto.Id, out var rows)
            ? dto with { Blockers = rows, OpenBlockerCount = rows.Count(r => !r.Resolved) }
            : dto;
}
