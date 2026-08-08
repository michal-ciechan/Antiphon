namespace Antiphon.Server.Application.Dtos;

/// <summary>The agent Files review surface: workspace + merged file list (git ∪ agent activity).</summary>
public sealed record AgentFilesDto(
    Guid AgentId,
    string WorkspaceRoot,
    bool IsGitRepository,
    IReadOnlyList<AgentFileDto> Files,
    FilesBaselineDto Baseline);

/// <summary>
/// What the git half of the listing was diffed against. Kind: "head" (working tree vs HEAD —
/// the default when nothing else resolves), "checkpoint" (the latest work-completed checkpoint),
/// or "commit" (an explicitly requested sha). TimestampFallback marks a checkpoint whose commit
/// was not in history (or never captured), resolved to the newest commit before its timestamp.
/// </summary>
public sealed record FilesBaselineDto(
    string Kind,
    string? CommitSha,
    string? CheckpointReason,
    DateTime? CheckpointAt,
    bool TimestampFallback);

public sealed record WorkspaceCommitDto(
    string Sha,
    string ShortSha,
    string Author,
    DateTime Date,
    string Subject,
    bool IsCheckpoint);

public sealed record WorkspaceCommitsDto(
    IReadOnlyList<WorkspaceCommitDto> Commits);

/// <summary>Full workspace file listing for the "show all files" toggle (paths only, capped).</summary>
public sealed record WorkspaceTreeDto(
    IReadOnlyList<string> Paths,
    bool Truncated);

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
    string? Level, // "viewed" | "reviewed" | null/"none" to clear
    string? Since = null); // baseline the client is viewing — prefix marks apply to that listing

/// <summary>
/// Manual checkpoint request. CommitSha null = current HEAD; otherwise any ref/short sha in the
/// workspace's history ("work up to this historic commit is signed off").
/// </summary>
public sealed record SetReviewCheckpointRequest(
    string? CommitSha = null,
    string? Reason = null);

/// <summary>One stored section mark; the client compares ContentHash to the live hash for staleness.</summary>
public sealed record SectionReviewDto(
    string Key,
    string ContentHash,
    DateTime UpdatedAt);

/// <summary>Batch section mark/clear for one file. ContentHash null clears that section's mark.</summary>
public sealed record MarkSectionReviewsRequest(
    string Path,
    IReadOnlyList<SectionMarkRequest> Sections);

public sealed record SectionMarkRequest(
    string Key,
    string? ContentHash);

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
