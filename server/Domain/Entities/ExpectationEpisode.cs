using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// One unresolved condition. A second open row for the same directive, kind and subject is refused.
/// Reason text and elapsed time update this row; they do not open another episode.
/// </summary>
public class ExpectationEpisode
{
    public Guid Id { get; set; }
    public string DirectiveId { get; set; } = string.Empty;
    public ExpectationEpisodeKind Kind { get; set; }
    public string SubjectKey { get; set; } = string.Empty;
    public DateTime FirstObservedAt { get; set; }
    public DateTime LastObservedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string Evidence { get; set; } = string.Empty;
    public string ConfigDigest { get; set; } = string.Empty;
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
