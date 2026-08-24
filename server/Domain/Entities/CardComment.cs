using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class CardComment
{
    public Guid Id { get; set; }
    public Guid CardId { get; set; }
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Self-reported author (agent name, "operator") or the GitHub login on imports.
    /// Free text; the server has no principals (same stance as CardRevision.EditedBy).
    /// </summary>
    public string? Author { get; set; }

    public CardCommentOrigin Origin { get; set; }

    /// <summary>
    /// Tracker comment id (GitHub numeric id, stringified). Unique-filtered where non-null.
    /// Non-null on imports at insert; stamped onto an Antiphon-origin row when its outbound
    /// echo is recognized by marker.
    /// </summary>
    public string? ExternalCommentId { get; set; }

    public string? ExternalUrl { get; set; }

    /// <summary>Imports: GitHub created_at. Antiphon rows: server now.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Outbound claim stamp — set BEFORE the POST, cleared if it throws (CARD-0067 shape).
    /// Null on External rows.
    /// </summary>
    public DateTime? SyncedAt { get; set; }

    public Card Card { get; set; } = null!;
}
