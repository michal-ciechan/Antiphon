using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Appends to a card's immutable history. Every write goes through here so the per-card revision
/// sequence has exactly one allocator.
/// </summary>
/// <remarks>
/// Deliberately synchronous and database-free: the revision number comes from
/// <see cref="Card.RevisionCount"/> on the TRACKED card and the row is attached through the
/// navigation collection, so a code path that mutates a card can record why without having a
/// <c>DbContext</c>, a <c>CancellationToken</c> or a round-trip to hand. That matters because moves
/// are made from four places, two of them static helpers deep inside other flows
/// (<see cref="CardLifecycleTransitions.TryMoveToReview"/>, the external-tracker sync) — the
/// addendum's assumption that every move passes through <c>CardService.ApplyColumnMove</c> is not
/// true of the code, and a history missing "the session finished, so the card went to Review" is
/// missing the most common transition there is.
///
/// <para>The caller owns <c>SaveChanges</c>. The card's concurrency token guards the allocation:
/// two writers racing for the same revision number both bump the token, so one of them loses on
/// save rather than colliding on the unique (CardId, RevisionNumber) index.</para>
/// </remarks>
internal static class CardRevisionLog
{
    /// <summary>Records a column transition. Reason is optional — many moves have none.</summary>
    public static CardRevision AppendMove(
        Card card,
        Guid fromColumnId,
        CardStatus fromStatus,
        BoardColumn toColumn,
        string? reason,
        string? movedBy,
        DateTime utcNow)
    {
        return Append(card, new CardRevision
        {
            Kind = CardRevisionKind.Move,
            FromColumnId = fromColumnId,
            ToColumnId = toColumn.Id,
            FromStatus = fromStatus,
            ToStatus = toColumn.CardStatus,
            Reason = Trimmed(reason),
            EditedBy = Trimmed(movedBy),
            CreatedAt = utcNow
        });
    }

    /// <summary>
    /// Snapshots the values a content edit is about to supersede. Call BEFORE mutating the card:
    /// storing the old values means revision n plus the current card is the whole history with no
    /// duplication, and never-edited cards need no backfilled revision 0.
    /// </summary>
    public static CardRevision AppendContentEdit(
        Card card, string reason, string? editedBy, DateTime utcNow)
    {
        return Append(card, new CardRevision
        {
            Kind = CardRevisionKind.ContentEdit,
            Title = card.Title,
            Alias = card.Alias,
            Description = card.Description,
            Importance = card.Importance,
            Urgency = card.Urgency,
            DueAt = card.DueAt,
            Position = card.Position,
            LabelsJson = card.LabelsJson,
            Reason = Trimmed(reason),
            EditedBy = Trimmed(editedBy),
            CreatedAt = utcNow
        });
    }

    /// <summary>
    /// Records a reopen. Call BEFORE mutating the card: the row is the move record for that
    /// transition AND the home of the superseded <see cref="Card.TerminalReason"/> /
    /// <see cref="Card.CompletedAt"/>. Reason is required at the call site.
    /// </summary>
    public static CardRevision AppendReopen(
        Card card, BoardColumn toColumn, string reason, string? reopenedBy, DateTime utcNow)
    {
        return Append(card, new CardRevision
        {
            Kind = CardRevisionKind.Reopen,
            FromColumnId = card.BoardColumnId,
            ToColumnId = toColumn.Id,
            FromStatus = card.Status,
            ToStatus = toColumn.CardStatus,
            TerminalReason = card.TerminalReason,
            CompletedAt = card.CompletedAt,
            Reason = Trimmed(reason),
            EditedBy = Trimmed(reopenedBy),
            CreatedAt = utcNow
        });
    }

    /// <summary>Records an archive or unarchive, so neither the act nor its reason is lost.</summary>
    public static CardRevision AppendArchiveChange(
        Card card, CardRevisionKind kind, string reason, string? actedBy, DateTime utcNow)
    {
        return Append(card, new CardRevision
        {
            Kind = kind,
            Reason = Trimmed(reason),
            EditedBy = Trimmed(actedBy),
            CreatedAt = utcNow
        });
    }

    private static CardRevision Append(Card card, CardRevision revision)
    {
        // Id is deliberately left unset. A row reached through a navigation rather than through
        // DbSet.Add() is classified by DetectChanges on whether its key is already set: unset means
        // Added (EF generates the Guid), set means "this is an existing row", and EF emits an UPDATE
        // that matches nothing — a DbUpdateConcurrencyException on every single move, which is
        // exactly what pre-assigning Guid.NewGuid() here produced.
        revision.CardId = card.Id;
        revision.RevisionNumber = ++card.RevisionCount;
        card.Revisions.Add(revision);
        return revision;
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
