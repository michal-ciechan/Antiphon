namespace Antiphon.Server.Application.Dtos;

public sealed record CardCommentRequest(
    string Message,
    string? FilePath = null,
    int? Line = null,
    string? Side = null,
    int? EndLine = null);

public sealed record CardCommentResult(
    Guid CardId,
    Guid SessionId,
    string FormattedMessage);

public sealed record CardPullRequestResult(
    Guid CardId,
    int PrNumber,
    string Owner,
    string Repo,
    string Branch,
    string BaseBranch,
    string? PrUrl,
    string? PrState,
    bool Created);
