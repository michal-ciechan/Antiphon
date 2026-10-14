using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// One directed relationship between two cards (CARD-0100). For <see cref="CardRelationshipKind.BlockedBy"/>,
/// <see cref="CardId"/> is the blocked card and <see cref="RelatedCardId"/> the blocker; "blocks" is the
/// inverse read of the same row. Whether the blocker is resolved is computed at read time
/// (<c>CardBlockers</c>).
/// </summary>
public class CardRelationship
{
    public Guid Id { get; set; }

    /// <summary>The card that owns the row (the blocked card).</summary>
    public Guid CardId { get; set; }

    /// <summary>The other card (the blocker).</summary>
    public Guid RelatedCardId { get; set; }

    public CardRelationshipKind Kind { get; set; }

    public string? Reason { get; set; }

    /// <summary>Self-reported author. Free text; same stance as <see cref="CardRevision.EditedBy"/>.</summary>
    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public Card Card { get; set; } = null!;
    public Card RelatedCard { get; set; } = null!;
}
