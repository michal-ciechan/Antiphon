namespace Antiphon.Server.Application.Dtos;

public sealed record TrackerSyncRunResult(
    IReadOnlyList<TrackerSyncBoardResult> Boards,
    bool ConcurrentRunSkipped = false);

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
    string? Error = null);
