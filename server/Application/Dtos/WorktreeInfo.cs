namespace Antiphon.Server.Application.Dtos;

public sealed record WorktreeInfo(
    string CardId,
    string RepoPath,
    string Path,
    string Branch,
    string BaseRef,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastTouchedAt)
{
    /// <summary>
    /// CARD-1448: the saved creation intent is schema 2 and never reached CreationComplete
    /// (an interrupted <c>git worktree add</c>). Such a checkout is never adopted.
    /// </summary>
    public bool CreationIncomplete { get; init; }
}
