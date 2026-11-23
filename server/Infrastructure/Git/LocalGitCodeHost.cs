using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// The review engine's local-git code host (design 8.2): a repository directory from
/// <c>Review:CodeHosts:Repos</c> and two refs. There is no conversation; findings go to Antiphon
/// gates. Reads go through <see cref="GitWorkspaceService"/> (its process gate, timeout and
/// in-process reads). The adapter for GitHub is <c>GitHubCodeHost</c>.
/// </summary>
public sealed class LocalGitCodeHost(GitWorkspaceService git) : ICodeHost
{
    public bool Serves(ReviewHost kind) => kind == ReviewHost.Local;

    /// <summary>Reads only: there is no conversation, no status and no remote to approve on or update (design 8.2).</summary>
    public CodeHostCapabilities Capabilities { get; } = new(
        SupportsCheckRuns: false, CanResolveThreads: false, ApprovalCountsForMerge: false, SupportsAutoMerge: false,
        CanReadFiles: true);

    /// <summary>The commit pair: base and head resolved to SHAs, the title and description from the head commit.</summary>
    public async Task<CodeHostPullRequest> GetPullRequestAsync(CodeHostTarget target, CancellationToken ct)
    {
        var path = RequirePath(target);
        var baseRef = target.BaseRef is { Length: > 0 } b
            ? b
            : throw new CodeHostException(CodeHostFailureKind.InvalidRequest, "A local review needs a base commit (baseSha).");
        var headRef = target.HeadRef is { Length: > 0 } h ? h : "HEAD";
        var baseSha = await ResolveAsync(path, baseRef, ct);
        var headSha = await ResolveAsync(path, headRef, ct);

        var log = await RunAsync(path, ct, "log", "-1", "--format=%s%x00%an%x00%ae%x00%b", headSha);
        var fields = log.Split('\0');
        var title = fields[0].Trim();
        var authorName = fields.Length > 1 ? fields[1].Trim() : "";
        var authorEmail = fields.Length > 2 ? fields[2].Trim() : "";
        var body = fields.Length > 3 ? fields[3].Trim() : "";
        return new CodeHostPullRequest(
            null,
            title,
            body.Length == 0 ? null : body,
            authorEmail,
            authorName.Length == 0 ? null : authorName,
            baseRef,
            baseSha,
            headRef,
            headSha,
            null,
            CodeHostPullRequestState.Open,
            false);
    }

    public async Task<IReadOnlyList<CodeHostChangedFile>> ListChangedFilesAsync(CodeHostTarget target, CodeHostRange range, CancellationToken ct)
    {
        var path = RequirePath(target);
        var spec = Spec(range);
        var statuses = ParseNameStatus(await RunAsync(path, ct, "diff", "--name-status", "-z", "--find-renames", spec, "--"));
        var stats = ParseNumstat(await RunAsync(path, ct, "diff", "--numstat", "-z", "--find-renames", spec, "--"));
        return statuses.Select(s =>
        {
            var (additions, deletions) = stats.TryGetValue(s.Path, out var counted) ? counted : (0, 0);
            return s with { Additions = additions, Deletions = deletions };
        }).ToList();
    }

    public Task<string> GetDiffAsync(CodeHostTarget target, CodeHostRange range, CancellationToken ct) =>
        RunAsync(RequirePath(target), ct, "diff", "--find-renames", Spec(range), "--");

    public async Task<CodeHostComparison> CompareAsync(CodeHostTarget target, string fromRef, string toRef, CancellationToken ct)
    {
        var path = RequirePath(target);
        var from = await ResolveAsync(path, fromRef, ct);
        var to = await ResolveAsync(path, toRef, ct);
        var mergeBase = (await RunAsync(path, ct, "merge-base", from, to)).Trim();
        var ahead = await CountAsync(path, $"{from}..{to}", ct);
        var behind = await CountAsync(path, $"{to}..{from}", ct);
        var files = await ListChangedFilesAsync(target, new CodeHostRange(from, to), ct);
        return new CodeHostComparison(from, to, mergeBase, ahead, behind, files);
    }

    public async Task<CodeHostBranchState> GetBranchStateAsync(CodeHostTarget target, string baseRef, string headSha, CancellationToken ct)
    {
        var path = RequirePath(target);
        var tip = await ResolveAsync(path, baseRef, ct);
        var head = await ResolveAsync(path, headSha, ct);
        var mergeBase = (await RunAsync(path, ct, "merge-base", tip, head)).Trim();
        return new CodeHostBranchState(tip, mergeBase, await CountAsync(path, $"{head}..{tip}", ct), await CountAsync(path, $"{tip}..{head}", ct));
    }

    public async Task<IReadOnlyList<string>> ListFilesAsync(CodeHostTarget target, string sha, string directory, CancellationToken ct)
    {
        var path = RequirePath(target);
        var commit = await ResolveAsync(path, sha, ct);
        var prefix = directory.Trim('/');
        if (!IsSafeRef(prefix))
            throw new CodeHostException(CodeHostFailureKind.InvalidRequest, $"'{directory}' is not a directory path.");
        var output = await RunAsync(path, ct, "ls-tree", "-r", "-z", "--name-only", commit, "--", prefix);
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public async Task<string?> ReadFileAsync(CodeHostTarget target, string sha, string path, CancellationToken ct)
    {
        var repo = RequirePath(target);
        var commit = await ResolveAsync(repo, sha, ct);
        if (!IsSafeRef(path) || path.Contains(':'))
            throw new CodeHostException(CodeHostFailureKind.InvalidRequest, $"'{path}' is not a file path.");
        var (code, stdout, _) = await git.RunAsync(repo, ct, "show", $"{commit}:{path}");
        return code == 0 ? stdout : null;
    }

    public Task<CodeHostPostedReview> PostReviewAsync(CodeHostTarget target, CodeHostReview review, CancellationToken ct) =>
        throw Unsupported("post a review");

    public Task<CodeHostThreadReply> ReplyToThreadAsync(CodeHostTarget target, string commentId, string body, CancellationToken ct) =>
        throw Unsupported("reply to a thread");

    public Task ResolveThreadAsync(CodeHostTarget target, string threadId, CancellationToken ct) => throw Unsupported("resolve a thread");

    public Task SetCommitStatusAsync(CodeHostTarget target, string sha, CodeHostCommitStatus status, CancellationToken ct) =>
        throw Unsupported("set a commit status");

    public Task ApproveAsync(CodeHostTarget target, string headSha, string body, CancellationToken ct) => throw Unsupported("approve");

    public Task<CodeHostBranchUpdate> UpdateBranchAsync(CodeHostTarget target, string expectedHeadSha, CancellationToken ct) =>
        throw Unsupported("update a branch");

    /// <summary>There are no pull requests to poll (capability <c>CanListPullRequests</c> is false).</summary>
    public Task<IReadOnlyList<CodeHostOpenPullRequest>> ListOpenPullRequestsAsync(CodeHostTarget target, CancellationToken ct) =>
        throw Unsupported("list pull requests");

    public Task<IReadOnlyList<CodeHostConversationComment>> ListConversationCommentsAsync(CodeHostTarget target, DateTime since, CancellationToken ct) =>
        throw Unsupported("list conversation comments");

    public Task<CodeHostThreadReply> PostConversationCommentAsync(CodeHostTarget target, string body, CancellationToken ct) =>
        throw Unsupported("post a conversation comment");

    public Task AddCommentReactionAsync(CodeHostTarget target, string commentId, string reaction, CancellationToken ct) =>
        throw Unsupported("react to a comment");

    private static CodeHostException Unsupported(string what) =>
        new(CodeHostFailureKind.Unsupported, $"A local git host cannot {what}; findings stay in Antiphon (design 8.2).");

    private async Task<int> CountAsync(string path, string range, CancellationToken ct) =>
        int.TryParse((await RunAsync(path, ct, "rev-list", "--count", range)).Trim(), out var n) ? n : 0;

    /// <summary><c>from...head</c>: from the merge base, like a PR.</summary>
    private static string Spec(CodeHostRange range)
    {
        var from = range.SinceSha ?? range.BaseSha;
        if (!IsSafeRef(from) || !IsSafeRef(range.HeadSha))
            throw new CodeHostException(CodeHostFailureKind.InvalidRequest, $"'{from}...{range.HeadSha}' is not a commit range.");
        return $"{from}...{range.HeadSha}";
    }

    /// <summary><c>git diff --name-status -z</c>: <c>status\0path\0</c>, or <c>R100\0old\0new\0</c> for a rename or copy.</summary>
    public static List<CodeHostChangedFile> ParseNameStatus(string output)
    {
        var records = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var files = new List<CodeHostChangedFile>();
        for (var i = 0; i + 1 < records.Length;)
        {
            var status = records[i++];
            var path = records[i++];
            string? previous = null;
            if (status[0] is 'R' or 'C' && i < records.Length)
            {
                previous = path;
                path = records[i++];
            }

            files.Add(new CodeHostChangedFile(path, status[0] switch
            {
                'A' => CodeHostFileStatus.Added,
                'M' => CodeHostFileStatus.Modified,
                'D' => CodeHostFileStatus.Deleted,
                'R' => CodeHostFileStatus.Renamed,
                'C' => CodeHostFileStatus.Copied,
                _ => CodeHostFileStatus.Other,
            }, 0, 0, previous));
        }

        return files;
    }

    /// <summary>
    /// <c>git diff --numstat -z</c>: <c>added\tdeleted\tpath\0</c>, or <c>added\tdeleted\t\0old\0new\0</c>
    /// for a rename. A binary file counts <c>-</c>, read as zero. Keyed by the new path.
    /// </summary>
    public static Dictionary<string, (int Additions, int Deletions)> ParseNumstat(string output)
    {
        var records = output.Split('\0');
        var stats = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        for (var i = 0; i < records.Length; i++)
        {
            var parts = records[i].Split('\t', 3);
            if (parts.Length < 3)
                continue;
            var path = parts[2];
            if (path.Length == 0 && i + 2 < records.Length)
            {
                path = records[i + 2];
                i += 2;
            }

            stats[path] = (int.TryParse(parts[0], out var a) ? a : 0, int.TryParse(parts[1], out var d) ? d : 0);
        }

        return stats;
    }

    private async Task<string> ResolveAsync(string path, string gitRef, CancellationToken ct)
    {
        if (!IsSafeRef(gitRef))
            throw new CodeHostException(CodeHostFailureKind.InvalidRequest, $"'{gitRef}' is not a ref or commit.");
        return await git.ResolveCommitAsync(path, gitRef, ct)
            ?? throw new CodeHostException(CodeHostFailureKind.NotFound, $"Commit '{gitRef}' was not found in {path}.");
    }

    private async Task<string> RunAsync(string path, CancellationToken ct, params string[] args)
    {
        var (code, stdout, stderr) = await git.RunAsync(path, ct, args);
        if (code != 0)
        {
            var reason = stderr.Trim();
            throw new CodeHostException(CodeHostFailureKind.Unavailable,
                $"git {args[0]} failed in {path} (exit {code}): {(reason.Length <= 300 ? reason : reason[..300])}");
        }

        return stdout;
    }

    private static string RequirePath(CodeHostTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.LocalPath))
            throw new CodeHostException(CodeHostFailureKind.NotConfigured, $"No local path is configured for {target.Repo} (Review:CodeHosts:Repos).");
        if (!Directory.Exists(target.LocalPath))
            throw new CodeHostException(CodeHostFailureKind.NotConfigured, $"The local path for {target.Repo} does not exist: {target.LocalPath}");
        return target.LocalPath;
    }

    /// <summary>Not an option, and no whitespace or control characters: the ref goes on a git command line.</summary>
    private static bool IsSafeRef(string value) =>
        value.Length is > 0 and <= 255 && value[0] != '-' && !value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c));
}
