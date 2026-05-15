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

    public Card Card { get; set; } = null!;
}
