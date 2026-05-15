namespace Antiphon.Server.Application.Dtos;

public sealed record WorktreeInfo(
    string CardId,
    string RepoPath,
    string Path,
    string Branch,
    string BaseRef,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastTouchedAt);
