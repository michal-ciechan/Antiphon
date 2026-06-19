namespace Antiphon.Messaging;

/// <summary>
/// Declares what a given channel actually supports — the "full vs common features" matrix
/// consumers use to decide how to format a reply (e.g. whether @-mentions or markdown are honored).
/// </summary>
public sealed record ChannelCapabilities
{
    public required string Channel { get; init; }
    public bool Mentions { get; init; }
    public bool Attachments { get; init; }
    public bool Edit { get; init; }
    public bool Delete { get; init; }
    public bool Reactions { get; init; }
    public bool Threads { get; init; }
    public bool TypingIndicator { get; init; }

    /// <summary>Markup flavor for text, e.g. "MarkdownV2", "HTML", or null for plain text only.</summary>
    public string? MarkdownFlavor { get; init; }

    /// <summary>Maximum characters per outbound text message.</summary>
    public int MaxTextLength { get; init; }

    public IReadOnlyList<AttachmentKind> AttachmentKinds { get; init; } = [];
}
