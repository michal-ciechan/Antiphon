using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// A code host (review engine design 8.1): GitHub over REST with a personal access token, or a local
/// git repository holding a commit pair. Reads: the PR, its changed files, its unified diff, a
/// comparison of two refs, the branch state and files at a commit. Writes: a review with inline
/// comments, thread replies and resolution, commit statuses, an approval and a branch update. A
/// caller checks <see cref="Capabilities"/> before a write; an adapter without one throws
/// <see cref="CodeHostException"/> with <see cref="CodeHostFailureKind.Unsupported"/>. Teams,
/// webhooks and events come later. <c>CodeHostRouter</c> picks the adapter and builds the
/// <see cref="CodeHostTarget"/> from <c>Review:CodeHosts</c>; callers never pick an adapter themselves.
/// </summary>
public interface ICodeHost
{
    /// <summary>True when this adapter serves subjects on <paramref name="kind"/>.</summary>
    bool Serves(ReviewHost kind);

    CodeHostCapabilities Capabilities { get; }

    /// <summary>
    /// The PR, or on a local host the commit pair, with its base and head resolved to SHAs. Throws
    /// <see cref="CodeHostException"/> for every failure.
    /// </summary>
    Task<CodeHostPullRequest> GetPullRequestAsync(CodeHostTarget target, CancellationToken ct);

    /// <summary>The changed files with line stats over <paramref name="range"/>.</summary>
    Task<IReadOnlyList<CodeHostChangedFile>> ListChangedFilesAsync(CodeHostTarget target, CodeHostRange range, CancellationToken ct);

    /// <summary>The unified diff over <paramref name="range"/>.</summary>
    Task<string> GetDiffAsync(CodeHostTarget target, CodeHostRange range, CancellationToken ct);

    /// <summary>
    /// Compares two refs or commits like <c>from...to</c>: the merge base, how far <paramref name="toRef"/>
    /// is ahead and behind, and the files changed from the merge base to <paramref name="toRef"/>.
    /// </summary>
    Task<CodeHostComparison> CompareAsync(CodeHostTarget target, string fromRef, string toRef, CancellationToken ct);

    /// <summary>The base branch's tip, the head's merge base with it, and how many base commits the head lacks.</summary>
    Task<CodeHostBranchState> GetBranchStateAsync(CodeHostTarget target, string baseRef, string headSha, CancellationToken ct);

    /// <summary>The paths of files under <paramref name="directory"/> at <paramref name="sha"/> (recursive, repo-relative).</summary>
    Task<IReadOnlyList<string>> ListFilesAsync(CodeHostTarget target, string sha, string directory, CancellationToken ct);

    /// <summary>A file's text at <paramref name="sha"/>, or null when it does not exist there.</summary>
    Task<string?> ReadFileAsync(CodeHostTarget target, string sha, string path, CancellationToken ct);

    /// <summary>Posts one review on the PR: a summary body plus inline comments, returning each comment's ids and link.</summary>
    Task<CodeHostPostedReview> PostReviewAsync(CodeHostTarget target, CodeHostReview review, CancellationToken ct);

    /// <summary>Replies in the thread that <paramref name="commentId"/> started.</summary>
    Task<CodeHostThreadReply> ReplyToThreadAsync(CodeHostTarget target, string commentId, string body, CancellationToken ct);

    /// <summary>
    /// Resolves a thread by its host thread id (<see cref="CodeHostPostedComment.ThreadId"/>). When no
    /// thread id was recorded, callers pass the id of the comment that started the thread; GitHub maps
    /// it to the thread itself.
    /// </summary>
    Task ResolveThreadAsync(CodeHostTarget target, string threadId, CancellationToken ct);

    /// <summary>Sets a commit status on <paramref name="sha"/> (design 4.11).</summary>
    Task SetCommitStatusAsync(CodeHostTarget target, string sha, CodeHostCommitStatus status, CancellationToken ct);

    /// <summary>Approves the PR at <paramref name="headSha"/>.</summary>
    Task ApproveAsync(CodeHostTarget target, string headSha, string body, CancellationToken ct);

    /// <summary>
    /// Brings the PR branch up to date with its base. <see cref="CodeHostBranchUpdate.NewHeadSha"/> is the
    /// new head when the host reports it at once, or null when the update is asynchronous (GitHub).
    /// </summary>
    Task<CodeHostBranchUpdate> UpdateBranchAsync(CodeHostTarget target, string expectedHeadSha, CancellationToken ct);
}

/// <summary>
/// Where a subject lives: the configured host id, the repo (<c>owner/name</c>), and either a PR
/// number (GitHub) or a local path with the two refs to compare (local git). It holds no credential:
/// an adapter reads its host's token from settings by <see cref="HostId"/>.
/// </summary>
public sealed record CodeHostTarget(
    string HostId,
    ReviewHost Kind,
    string Repo,
    int? PrNumber = null,
    string? LocalPath = null,
    string? BaseRef = null,
    string? HeadRef = null);

/// <summary>
/// The commits a file list or diff covers. With <see cref="SinceSha"/> null it is the whole change:
/// the PR, or <c>base...head</c> on a local host. With it set, it is the change since that earlier
/// head, <c>since...head</c> (a recheck, design 4.2).
/// </summary>
public sealed record CodeHostRange(string BaseSha, string HeadSha, string? SinceSha = null)
{
    public bool IsIncremental => SinceSha is not null;
}

public enum CodeHostPullRequestState
{
    Open,
    Closed,
    Merged,
}

/// <summary>A PR as intake needs it. On a local host the title and description come from the head commit.</summary>
public sealed record CodeHostPullRequest(
    int? Number,
    string Title,
    string? Description,
    string AuthorLogin,
    string? AuthorDisplayName,
    string BaseRef,
    string BaseSha,
    string HeadRef,
    string HeadSha,
    string? Url,
    CodeHostPullRequestState State,
    bool IsDraft);

public enum CodeHostFileStatus
{
    Added,
    Modified,
    Deleted,
    Renamed,
    Copied,
    Other,
}

public sealed record CodeHostChangedFile(
    string Path,
    CodeHostFileStatus Status,
    int Additions,
    int Deletions,
    string? PreviousPath = null);

/// <summary>
/// Capability flags (design 8.1). The first four are the design's; the rest say which reads and
/// writes the adapter implements, so the executor skips a write a host cannot make instead of failing.
/// </summary>
public sealed record CodeHostCapabilities(
    bool SupportsCheckRuns,
    bool CanResolveThreads,
    bool ApprovalCountsForMerge,
    bool SupportsAutoMerge,
    bool CanPostReviews = false,
    bool CanReplyToThreads = false,
    bool SupportsCommitStatuses = false,
    bool CanApprove = false,
    bool CanUpdateBranch = false,
    bool CanReadFiles = false);

/// <summary><c>from...to</c>: the resolved ends, their merge base, the ahead and behind counts, and the changed files.</summary>
public sealed record CodeHostComparison(
    string FromSha,
    string ToSha,
    string MergeBaseSha,
    int AheadBy,
    int BehindBy,
    IReadOnlyList<CodeHostChangedFile> Files);

/// <summary>The base branch tip, the head's merge base with it, and how many base commits the head lacks.</summary>
public sealed record CodeHostBranchState(string BaseTipSha, string MergeBaseSha, int BehindBy, int AheadBy);

/// <summary>An inline comment on the new side of the diff, at <see cref="Line"/> of <see cref="Path"/>.</summary>
public sealed record CodeHostReviewComment(string Path, int Line, string Body);

/// <summary>One review: a summary body and inline comments, at <see cref="CommitSha"/>.</summary>
public sealed record CodeHostReview(string CommitSha, string Body, IReadOnlyList<CodeHostReviewComment> Comments);

/// <summary>
/// A posted inline comment. <see cref="CommentId"/> takes replies; <see cref="ThreadId"/> resolves the
/// thread and is null when the host needs another call to find it (GitHub REST).
/// </summary>
public sealed record CodeHostPostedComment(string Path, int Line, string CommentId, string? ThreadId, string? Url);

public sealed record CodeHostPostedReview(string ReviewId, string? Url, IReadOnlyList<CodeHostPostedComment> Comments);

public sealed record CodeHostThreadReply(string CommentId, string? Url);

public enum CodeHostStatusState
{
    Pending,
    Success,
    Failure,
    Error,
}

/// <summary>A commit status: <c>antiphon/review</c> or <c>antiphon/approval</c> (design 4.11).</summary>
public sealed record CodeHostCommitStatus(string Context, CodeHostStatusState State, string Description, string? TargetUrl);

/// <summary>A branch update: whether it was made, the new head when known at once, and the host's message.</summary>
public sealed record CodeHostBranchUpdate(bool Updated, string? NewHeadSha, string? Message);

/// <summary>What went wrong with a code host call; the caller maps it to an HTTP error or a failed node.</summary>
public enum CodeHostFailureKind
{
    /// <summary>No host, adapter, path or setting for this subject.</summary>
    NotConfigured,

    /// <summary>The request itself is malformed: a bad repo name or ref.</summary>
    InvalidRequest,

    /// <summary>The repo, PR, commit or range does not exist (or the token cannot see it).</summary>
    NotFound,

    /// <summary>401 or 403: the token is missing, wrong or lacks a permission.</summary>
    Rejected,

    /// <summary>429, or 403 with the rate limit spent.</summary>
    RateLimited,

    /// <summary>A 5xx, a timeout, a transport failure or a failed git process.</summary>
    Unavailable,

    /// <summary>The host will not render this diff (GitHub 406 for a very large PR).</summary>
    TooLarge,

    /// <summary>The adapter does not implement this operation (see <see cref="CodeHostCapabilities"/>).</summary>
    Unsupported,
}

public sealed class CodeHostException(CodeHostFailureKind kind, string message, int? statusCode = null)
    : Exception(message)
{
    public CodeHostFailureKind Kind { get; } = kind;
    public int? StatusCode { get; } = statusCode;
}
