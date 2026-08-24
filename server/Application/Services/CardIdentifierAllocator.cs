using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The next identifier for a board: one past the HIGHEST suffix already handed out.
/// </summary>
/// <remarks>
/// This used to be <c>count + 1</c>, which reused an identifier after a delete (CARD-0005):
/// remove CARD-0007 from a seven-card board and the next create hands out CARD-0007 again,
/// silently pointing every existing reference — commit messages, docs, other cards' terminal
/// reasons — at a different card. Identifiers are cited outside the database, so the sequence
/// has to move forward even when rows leave. Suffixes that do not parse (a board synced from a
/// foreign tracker, or a GitHub <c>#12</c>) are ignored rather than blocking allocation.
///
/// <para>A HARD delete of the current highest card still frees its number, because the only
/// record that it was ever taken is the row itself. That is now avoidable rather than
/// inevitable: archive is what "delete" means for a card, and an archived row is still counted
/// here — which is exactly why archived cards are filtered at the read site and NOT by a global
/// EF query filter.</para>
///
/// <para>One instance per board per save. <see cref="Next"/> is how a batch of N imports gets N
/// distinct numbers in one <c>SaveChanges</c>; a lost race against a manual create is a unique
/// index failure the caller reports with the constraint name and retries on the next tick.</para>
/// </remarks>
public sealed class CardIdentifierAllocator
{
    private int _highest;

    internal CardIdentifierAllocator(int highest) => _highest = highest;

    public static async Task<CardIdentifierAllocator> ForBoardAsync(
        AppDbContext db,
        Guid boardId,
        CancellationToken ct)
    {
        var identifiers = await db.Cards
            .Where(c => c.BoardId == boardId)
            .Select(c => c.Identifier)
            .ToListAsync(ct);
        return FromIdentifiers(identifiers);
    }

    public static CardIdentifierAllocator FromIdentifiers(IEnumerable<string> identifiers) =>
        new(HighestFrom(identifiers));

    public static int HighestFrom(IEnumerable<string> identifiers)
    {
        var highest = 0;
        foreach (var identifier in identifiers)
        {
            if (string.IsNullOrEmpty(identifier))
                continue;
            var suffix = identifier.AsSpan(identifier.LastIndexOf('-') + 1);
            if (int.TryParse(suffix, out var value) && value > highest)
                highest = value;
        }

        return highest;
    }

    public string Next()
    {
        _highest++;
        return $"CARD-{_highest:0000}";
    }
}
