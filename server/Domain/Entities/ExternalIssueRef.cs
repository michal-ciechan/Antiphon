using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class ExternalIssueRef
{
    public Guid Id { get; set; }
    public Guid CardId { get; set; }
    public TrackerKind TrackerKind { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string ExternalKey { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string RawPayloadJson { get; set; } = "{}";
    public DateTime LastSyncedAt { get; set; }

    /// <summary>Which side created the link. Default ExternalImport for pre-existing rows.</summary>
    public ExternalIssueOrigin Origin { get; set; } = ExternalIssueOrigin.ExternalImport;

    /// <summary>Last observed external open/closed state ("open"/"closed"). Null = first sync.</summary>
    public string? LastKnownExternalState { get; set; }

    /// <summary>
    /// Content-edit OUT cursor. Migration backfills existing rows to the card's RevisionCount
    /// so first sync does not echo historical edits as comments.
    /// </summary>
    public int LastRevisionSynced { get; set; }

    /// <summary>Coarse dirty check for export-origin title/body/label pushes.</summary>
    public DateTime? LastOutboundSyncedAt { get; set; }

    /// <summary>Tracker login of the issue author. Refreshed every sync pass.</summary>
    public string? Author { get; set; }

    /// <summary>
    /// Whether <see cref="Author"/> is in the board's <c>tracker.operator_logins</c>.
    /// Null = not judged (the board has no operator list). Stored so readers never parse YAML.
    /// </summary>
    public bool? AuthorIsOperator { get; set; }

    public Card Card { get; set; } = null!;
}
