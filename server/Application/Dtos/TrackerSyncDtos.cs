namespace Antiphon.Server.Application.Dtos;

public sealed record TrackerSyncRunResult(
    IReadOnlyList<TrackerSyncBoardResult> Boards,
    bool ConcurrentRunSkipped = false)
{
    /// <summary>
    /// CARD-0171: one entry per board that had changes, when the trigger asked for a
    /// notification (<c>?notify=true</c>). Empty when the caller did not opt in.
    /// </summary>
    public IReadOnlyList<TrackerSyncNotificationResult> Notifications { get; init; } = [];
}

public sealed record TrackerSyncBoardResult(
    Guid BoardId,
    string BoardName,
    int IssuesPulled,
    int CommentsIn,
    int CommentsOut,
    int LabelsChanged,
    int StateChanges,
    int Creates,
    IReadOnlyList<string> Skips,
    string? Error = null)
{
    /// <summary>
    /// CARD-0171: terminal cards moved back to the first active column because GitHub reopened
    /// the issue (<c>ApplyExternalReopens</c>). Counted nowhere before this card, which is why a
    /// run that only reopened a card looked like a no-op.
    /// </summary>
    public int ExternalReopens { get; init; }

    /// <summary>
    /// CARD-0171: what this run actually changed, itemised so a summary can name the cards.
    /// <c>Changes.Count &gt; 0</c> is the notification gate — equivalent to
    /// commentsIn + commentsOut + labelsChanged + stateChanges + creates + externalReopens +
    /// content pushes. <see cref="IssuesPulled"/> and <see cref="Skips"/> are never changes.
    /// </summary>
    public IReadOnlyList<TrackerSyncChange> Changes { get; init; } = [];
}

/// <summary>CARD-0171: the kinds of change a bidirectional run can make, in message order.</summary>
public enum TrackerSyncChangeKind
{
    CommentIn,
    CommentOut,
    LabelsChanged,
    ClosedOnGitHub,
    ReopenedOnGitHub,
    ReopenedFromGitHub,
    Created,
    ContentPushed
}

/// <summary>
/// CARD-0171: one itemised change from a bidirectional run. <paramref name="CardIdentifier"/> is
/// the card's human identifier (CARD-0171); <paramref name="ExternalKey"/> the tracker's own key
/// (<c>#14</c>). CARD-0346 adds an optional label delta; both lists stay null unless this item
/// is a <see cref="TrackerSyncChangeKind.LabelsChanged"/> whose GitHub write succeeded.
/// </summary>
public sealed record TrackerSyncChange(
    TrackerSyncChangeKind Kind,
    string CardIdentifier,
    string ExternalKey,
    string? Url)
{
    /// <summary>CARD-0346: labels added on this issue, sorted and de-duplicated.</summary>
    public IReadOnlyList<string>? Added { get; init; }

    /// <summary>CARD-0346: labels removed on this issue, sorted and de-duplicated.</summary>
    public IReadOnlyList<string>? Removed { get; init; }
}

/// <summary>
/// CARD-0171: the outcome of announcing one board's changes. <paramref name="Sent"/> false always
/// carries a <paramref name="Reason"/>: <c>notify_channel_unset</c>, <c>channel_not_found</c>,
/// <c>channel_ambiguous</c>, <c>channel_disabled</c> or <c>send_failed</c>. Never fails the sync —
/// the writes have already committed by the time this runs.
/// </summary>
public sealed record TrackerSyncNotificationResult(
    Guid BoardId,
    bool Sent,
    Guid? ChannelId,
    string? Reason);
