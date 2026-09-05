using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// One immutable entry in a card's history. The card SURFACE is correctable; the card RECORD is
/// append-only — every edit supersedes the visible text but archives the prior text here, with a
/// reason, and nothing is ever destroyed (CARD-0019).
/// </summary>
/// <remarks>
/// A <see cref="CardRevisionKind.ContentEdit"/> row stores the values it SUPERSEDED, not the new
/// ones: revision n plus the current card is the complete history with no duplication, and it means
/// the thousands of never-edited cards need no backfilled "revision 0".
///
/// <para><see cref="CardRevisionKind.Move"/> rows carry no text — they exist because a move
/// supersedes no text and so would otherwise leave no trace at all. Every column transition writes
/// one, which is what gives the board state graph its entered-state-at (per-edge counts are a
/// group-by) and what finally gives <c>MoveCardRequest.Reason</c> somewhere to live on a
/// non-terminal move, where it used to be accepted and silently dropped.</para>
///
/// <para><see cref="RevisionNumber"/> is ONE per-card monotonic sequence across all kinds, so the
/// history reads interleaved in the order things actually happened. It is allocated from
/// <c>Card.RevisionCount</c> on the tracked card — no query, so any code path that mutates a card
/// can append a revision, and the card's concurrency token guards the allocation.</para>
/// </remarks>
public class CardRevision
{
    public Guid Id { get; set; }
    public Guid CardId { get; set; }

    /// <summary>1..n per card, monotonic across every kind. Assigned server-side.</summary>
    public int RevisionNumber { get; set; }

    public CardRevisionKind Kind { get; set; }

    // ---- ContentEdit only (null on every other kind) ----

    /// <summary>The SUPERSEDED title.</summary>
    public string? Title { get; set; }

    /// <summary>The SUPERSEDED alias (CARD-0350). Null on a row that did not snapshot one.</summary>
    public string? Alias { get; set; }

    /// <summary>The SUPERSEDED description.</summary>
    public string? Description { get; set; }

    /// <summary>The SUPERSEDED importance.</summary>
    public CardImportance? Importance { get; set; }

    /// <summary>The SUPERSEDED urgency.</summary>
    public CardUrgency? Urgency { get; set; }

    /// <summary>The SUPERSEDED due date.</summary>
    public DateTime? DueAt { get; set; }

    /// <summary>
    /// The SUPERSEDED position (CARD-0098). Set on a <see cref="CardRevisionKind.ContentEdit"/>
    /// when a promotion clears it, and on a <see cref="CardRevisionKind.Reorder"/> row.
    /// </summary>
    public int? Position { get; set; }

    /// <summary>The SUPERSEDED labels, same jsonb shape as <see cref="Card.LabelsJson"/>.</summary>
    public string? LabelsJson { get; set; }

    // ---- Move and Reopen (null on every other kind) ----

    public Guid? FromColumnId { get; set; }
    public Guid? ToColumnId { get; set; }
    public CardStatus? FromStatus { get; set; }
    public CardStatus? ToStatus { get; set; }

    // ---- Reopen only (null on every other kind) ----

    /// <summary>The SUPERSEDED terminal reason, snapshotted before the card is made live again.</summary>
    public string? TerminalReason { get; set; }

    /// <summary>The SUPERSEDED completion timestamp, snapshotted before it is cleared on the card.</summary>
    public DateTime? CompletedAt { get; set; }

    // ---- Every kind ----

    /// <summary>
    /// Why this happened. REQUIRED on a content edit (a correction that does not say why is how
    /// the record silently rots); optional on a move, which often has no reason beyond "the work
    /// moved on".
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Self-reported author — free text (agent name, "operator"). The server has no principals, so
    /// this is NOT an authenticated actor and must never be presented as one.
    /// </summary>
    public string? EditedBy { get; set; }

    /// <summary>When these values were superseded.</summary>
    public DateTime CreatedAt { get; set; }

    public Card Card { get; set; } = null!;
}
