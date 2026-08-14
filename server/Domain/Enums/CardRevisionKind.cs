namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// What a <c>CardRevision</c> row records. One table with a discriminator, deliberately, so a
/// card's history is a single ordered sequence: the modal, the API and the revision numbering all
/// want one interleaved list, and a separate transitions table would have to be merged back
/// together at every read site.
/// </summary>
public enum CardRevisionKind
{
    /// <summary>Text was superseded: the row carries the OLD title/description/priority/labels.</summary>
    ContentEdit = 0,

    /// <summary>The card changed column: the row carries from/to column and status. No text.</summary>
    Move = 1,

    /// <summary>The card was archived (never deleted); the row carries the archive reason.</summary>
    Archive = 2,

    /// <summary>An archive was undone; the row carries why.</summary>
    Unarchive = 3
}
