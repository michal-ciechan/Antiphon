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

    public CodeHostCapabilities Capabilities { get; } = new(
        SupportsCheckRuns: false, CanResolveThreads: false, ApprovalCountsForMerge: false, SupportsAutoMerge: false);

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
