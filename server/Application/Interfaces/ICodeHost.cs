using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// A code host (review engine design 8.1): GitHub over REST with a personal access token, or a local
/// git repository holding a commit pair. This slice covers intake only: the PR, its changed files and
/// its unified diff. Threads, reviews, statuses, teams and events come later.
/// <c>CodeHostRouter</c> picks the adapter and builds the <see cref="CodeHostTarget"/> from
/// <c>Review:CodeHosts</c>; callers never pick an adapter themselves.
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

/// <summary>Capability flags (design 8.1).</summary>
public sealed record CodeHostCapabilities(
    bool SupportsCheckRuns,
    bool CanResolveThreads,
    bool ApprovalCountsForMerge,
    bool SupportsAutoMerge);

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
}

public sealed class CodeHostException(CodeHostFailureKind kind, string message, int? statusCode = null)
    : Exception(message)
{
    public CodeHostFailureKind Kind { get; } = kind;
    public int? StatusCode { get; } = statusCode;
}
