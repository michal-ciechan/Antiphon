using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Hands out a board's next <c>CARD-nnnn</c> identifiers: one past the HIGHEST suffix already
/// taken, and N consecutive distinct numbers for a batch that saves in one round trip.
/// </summary>
/// <remarks>
/// This used to live in <see cref="CardService"/> as <c>NextIdentifierAsync</c>, which re-queried
/// per card and so could only ever hand out one number per <c>SaveChanges</c> — fine for the
/// single-card create route, useless for a tracker sync importing eleven issues at once
/// (CARD-0175). The parse is unchanged: the numeric suffix after the LAST <c>-</c>, over ALL of
/// the board's cards INCLUDING archived ones, +1.
///
/// <para>The sequence only ever moves FORWARD. It used to be <c>count + 1</c>, which reused an
/// identifier after a delete (CARD-0005): remove CARD-0007 from a seven-card board and the next
/// create hands out CARD-0007 again, silently pointing every existing reference — commit messages,
/// docs, other cards' terminal reasons — at a different card. Identifiers are cited outside the
/// database, so the sequence has to move forward even when rows leave. Suffixes that do not parse
/// (a board synced from a foreign tracker before CARD-0175, or a <c>#12</c> import) are ignored
/// rather than blocking allocation.</para>
///
/// <para>A HARD delete of the current highest card still frees its number, because the only record
/// that it was ever taken is the row itself. That is avoidable rather than inevitable:
/// <c>CardService.ArchiveAsync</c> is what "delete" means for a card, and an archived row is still
/// counted here — which is exactly why archived cards are filtered at the read site and NOT by a
/// global EF query filter.</para>
///
/// <para>There is no table and no sequence object behind this: the per-board unique index
/// <c>IX_Cards_BoardId_Identifier</c> is the arbiter, exactly as it already is for two concurrent
/// manual creates. A lost race is a <c>DbUpdateException</c> the caller reports with the
/// constraint's own name and retries on its next pass.</para>
/// </remarks>
public sealed class CardIdentifierAllocator
{
    private int _highest;

    private CardIdentifierAllocator(int highest) => _highest = highest;

    /// <summary>Reads the board's identifiers once and computes the starting point.</summary>
    public static async Task<CardIdentifierAllocator> ForBoardAsync(
        AppDbContext db,
        Guid boardId,
        CancellationToken ct)
    {
        var identifiers = await db.Cards
            .Where(c => c.BoardId == boardId)
            .Select(c => c.Identifier)
            .ToListAsync(ct);
        return new CardIdentifierAllocator(HighestOf(identifiers));
    }

    /// <summary>Test/seam entry point: the same allocator over identifiers already in hand.</summary>
    public static CardIdentifierAllocator ForIdentifiers(IEnumerable<string?> identifiers) =>
        new(HighestOf(identifiers));

    /// <summary>The next free identifier. Consumes it — two calls never return the same value.</summary>
    public string Next() => Format(++_highest);

    /// <summary>The highest number handed out so far, for a caller that wants to log the range.</summary>
    public int Highest => _highest;

    public static string Format(int number) => $"CARD-{number:0000}";

    private static int HighestOf(IEnumerable<string?> identifiers)
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
}
