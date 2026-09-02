namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// Outcome of syncing one board's cards into <c>docs/cards/&lt;slug&gt;/</c> (CARD-0004).
/// </summary>
public sealed record CardFileSyncBoardResult(
    Guid BoardId,
    string BoardName,
    string? Directory,
    int Written,
    int Deleted,
    int Unchanged,
    string? CommitSha,
    string? WriteSkipReason,
    string? CommitSkipReason,
    string? Error,
    bool DryRun);
