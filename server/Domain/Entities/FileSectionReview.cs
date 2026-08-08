namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Per-agent, per-file, per-SECTION "reviewed" bookkeeping for rendered markdown (feature 009) —
/// the section-granular sibling of <see cref="FileReviewState"/>. Hash-anchored the same way: the
/// mark records the hash of the section content the reader saw, so any later change to that
/// section makes the mark stale and the section surfaces (expanded, badged) again.
///
/// The CLIENT owns markdown structure: it splits sections (heading-delimited), derives the slug
/// key, and computes the hash. The server stores opaque strings — it never parses markdown, so
/// the two sides can never disagree about section boundaries.
/// </summary>
public class FileSectionReview
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }

    /// <summary>Workspace-relative path, forward slashes (same normalized form the files API serves).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Client-derived stable section identity: slugified heading text plus an occurrence suffix
    /// for duplicates ("setup", "setup-2"); the pre-heading preamble is "__intro".
    /// </summary>
    public string SectionKey { get; set; } = string.Empty;

    /// <summary>Client-computed hash of the section's direct content when the mark was made.</summary>
    public string ContentHash { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; }

    public Agent Agent { get; set; } = null!;
}
