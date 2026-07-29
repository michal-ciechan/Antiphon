namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Per-agent "I have looked at this file" bookkeeping for the Files review surface — the GitHub
/// PR "viewed" checkbox, hash-anchored: the mark records the content hash it applied to, so any
/// later change makes the mark stale and the file surfaces as unviewed again.
/// </summary>
public class FileReviewState
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }

    /// <summary>Workspace-relative path, forward slashes (the same normalized form the files API serves).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>SHA-256 (first 16 hex chars) of the content this mark applied to.</summary>
    public string ContentHash { get; set; } = string.Empty;

    public FileReviewLevel Level { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Agent Agent { get; set; } = null!;
}

public enum FileReviewLevel
{
    /// <summary>Seen it — the lightweight skim mark.</summary>
    Viewed = 1,

    /// <summary>Properly reviewed — the stronger sign-off.</summary>
    Reviewed = 2,
}
