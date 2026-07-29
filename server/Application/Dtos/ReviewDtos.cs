namespace Antiphon.Server.Application.Dtos;

/// <summary>The agent Files review surface: workspace + merged file list (git ∪ agent activity).</summary>
public sealed record AgentFilesDto(
    Guid AgentId,
    string WorkspaceRoot,
    bool IsGitRepository,
    IReadOnlyList<AgentFileDto> Files);

public sealed record AgentFileDto(
    string Path,
    string GitStatus,
    bool External,
    int AgentEdits,
    DateTime? LastAgentEditAt,
    string? ContentHash,
    string? ReviewLevel,
    bool ReviewStale,
    long? SizeBytes,
    bool IsMarkdown);

public sealed record AgentFileContentDto(
    string Path,
    string Rev,
    string? Text,
    bool Truncated,
    bool Missing,
    bool IsBinary = false);

public sealed record MarkFilesReviewRequest(
    IReadOnlyList<string>? Paths,
    string? Prefix,
    string? Level); // "viewed" | "reviewed" | null/"none" to clear

public sealed record ReviewThreadDto(
    Guid Id,
    Guid AgentId,
    string Path,
    int Line,
    string? Snippet,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<ReviewCommentDto> Comments);

public sealed record ReviewCommentDto(
    Guid Id,
    string Author,
    string Body,
    DateTime CreatedAt);

public sealed record CreateReviewThreadRequest(
    string Path,
    int Line,
    string? Snippet,
    string Body,
    bool Dispatch);

public sealed record AddReviewCommentRequest(
    string Body,
    bool Dispatch);
