using System.Diagnostics;
using System.Text;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Thin async wrapper over the git CLI for an agent's workspace — status, HEAD content, and
/// unified diffs. Shells out (no LibGit2) so behaviour matches whatever git the machine has;
/// every call is best-effort: a non-repo workspace or a git failure degrades to "no git info",
/// never an error surfaced to the files UI.
/// </summary>
public sealed class GitWorkspaceService
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(15);
    private readonly ILogger<GitWorkspaceService> _logger;

    public GitWorkspaceService(ILogger<GitWorkspaceService> logger) => _logger = logger;

    public sealed record GitChange(string Path, GitFileStatus Status, string? OldPath);

    public async Task<bool> IsRepositoryAsync(string workingDirectory, CancellationToken ct)
    {
        var (code, stdout, _) = await RunAsync(workingDirectory, ct, "rev-parse", "--is-inside-work-tree");
        return code == 0 && stdout.Trim() == "true";
    }

    /// <summary>Working tree + index changes vs HEAD (porcelain v1 -z), untracked included.</summary>
    public async Task<IReadOnlyList<GitChange>> GetChangesAsync(string workingDirectory, CancellationToken ct)
    {
        var (code, stdout, stderr) = await RunAsync(
            workingDirectory, ct, "status", "--porcelain", "-z", "--untracked-files=all");
        if (code != 0)
        {
            _logger.LogDebug("git status failed in {Dir}: {Err}", workingDirectory, stderr);
            return [];
        }

        // Git reports paths relative to the REPO ROOT; when the workspace is a SUBDIRECTORY of
        // the repo (live miss 2026-07-29: agents/family inside the ClaudeBot repo — every file
        // rendered empty because "sites/x.md" resolved against the workspace), re-relativize
        // paths under the workspace and return paths elsewhere in the repo as absolute.
        var (prefixCode, prefixOut, _) = await RunAsync(workingDirectory, ct, "rev-parse", "--show-prefix");
        var prefix = prefixCode == 0 ? prefixOut.Trim() : "";
        var (topCode, topOut, _) = await RunAsync(workingDirectory, ct, "rev-parse", "--show-toplevel");
        var toplevel = topCode == 0 ? topOut.Trim() : workingDirectory.Replace('\\', '/');

        var changes = new List<GitChange>();
        var records = stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < records.Length; i++)
        {
            var record = records[i];
            if (record.Length < 4)
                continue;
            var index = record[0];
            var work = record[1];
            var path = record[3..];

            string? oldPath = null;
            if (index is 'R' or 'C' && i + 1 < records.Length)
            {
                // Renames/copies emit the ORIGINAL path as the next NUL-separated record.
                oldPath = records[++i];
            }

            changes.Add(new GitChange(Rebase(path), Classify(index, work), oldPath is null ? null : Rebase(oldPath)));
        }
        return changes;

        string Rebase(string repoRelative) =>
            prefix.Length == 0 ? repoRelative
            : repoRelative.StartsWith(prefix, StringComparison.Ordinal) ? repoRelative[prefix.Length..]
            : $"{toplevel}/{repoRelative}";
    }

    /// <summary>
    /// Working tree changes vs an arbitrary base commit — committed AND uncommitted differences
    /// combined (git diff &lt;base&gt;), plus untracked files from status. This is the "changes since
    /// commit X" view; with base=HEAD it degenerates to <see cref="GetChangesAsync"/> semantics.
    /// </summary>
    public async Task<IReadOnlyList<GitChange>> GetChangesSinceAsync(
        string workingDirectory, string baseCommit, CancellationToken ct)
    {
        var (code, stdout, stderr) = await RunAsync(
            workingDirectory, ct, "diff", "--name-status", "-z", "--find-renames", baseCommit);
        if (code != 0)
        {
            _logger.LogDebug("git diff {Base} failed in {Dir}: {Err}", baseCommit, workingDirectory, stderr);
            return [];
        }

        var (prefixCode, prefixOut, _) = await RunAsync(workingDirectory, ct, "rev-parse", "--show-prefix");
        var prefix = prefixCode == 0 ? prefixOut.Trim() : "";
        var (topCode, topOut, _) = await RunAsync(workingDirectory, ct, "rev-parse", "--show-toplevel");
        var toplevel = topCode == 0 ? topOut.Trim() : workingDirectory.Replace('\\', '/');

        string Rebase(string repoRelative) =>
            prefix.Length == 0 ? repoRelative
            : repoRelative.StartsWith(prefix, StringComparison.Ordinal) ? repoRelative[prefix.Length..]
            : $"{toplevel}/{repoRelative}";

        // -z --name-status records: STATUS \0 path \0 (renames/copies: STATUS \0 old \0 new \0).
        var changes = new List<GitChange>();
        var records = stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var i = 0;
        while (i + 1 < records.Length)
        {
            var status = records[i++];
            var path = records[i++];
            string? oldPath = null;
            if ((status[0] == 'R' || status[0] == 'C') && i < records.Length)
            {
                oldPath = path;
                path = records[i++];
            }
            changes.Add(new GitChange(
                Rebase(path),
                status[0] switch
                {
                    'A' => GitFileStatus.Added,
                    'D' => GitFileStatus.Deleted,
                    'R' or 'C' => GitFileStatus.Renamed,
                    _ => GitFileStatus.Modified,
                },
                oldPath is null ? null : Rebase(oldPath)));
        }

        // git diff never lists untracked files — union them in from status.
        var known = new HashSet<string>(changes.Select(c => c.Path), StringComparer.OrdinalIgnoreCase);
        foreach (var change in await GetChangesAsync(workingDirectory, ct))
        {
            if (change.Status == GitFileStatus.Untracked && !known.Contains(change.Path))
                changes.Add(change);
        }
        return changes;
    }

    /// <summary>The file's content at HEAD, or null when it doesn't exist there (new file / no repo).</summary>
    public Task<string?> GetHeadContentAsync(string workingDirectory, string relativePath, CancellationToken ct)
        => GetContentAtAsync(workingDirectory, relativePath, "HEAD", ct);

    /// <summary>The file's content at an arbitrary commit, or null when it doesn't exist there.</summary>
    public async Task<string?> GetContentAtAsync(
        string workingDirectory, string relativePath, string gitRef, CancellationToken ct)
    {
        // ref:./path is CWD-relative; a bare ref:path is repo-ROOT-relative and breaks for
        // workspaces that are subdirectories of the repo.
        var (code, stdout, _) = await RunAsync(workingDirectory, ct, "show", $"{gitRef}:./{relativePath}");
        return code == 0 ? stdout : null;
    }

    /// <summary>Unified diff of the file vs a base commit (default HEAD); null on failure/no repo.</summary>
    public async Task<string?> GetDiffAsync(
        string workingDirectory, string relativePath, CancellationToken ct, string baseRef = "HEAD")
    {
        var (code, stdout, _) = await RunAsync(workingDirectory, ct, "diff", baseRef, "--", relativePath);
        return code == 0 ? stdout : null;
    }

    /// <summary>Current HEAD commit sha, or null (no repo / unborn branch).</summary>
    public async Task<string?> GetHeadShaAsync(string workingDirectory, CancellationToken ct)
    {
        var (code, stdout, _) = await RunAsync(workingDirectory, ct, "rev-parse", "HEAD");
        return code == 0 ? stdout.Trim() : null;
    }

    /// <summary>True when the commit exists and is an ancestor of (or equal to) HEAD.</summary>
    public async Task<bool> IsInHistoryAsync(string workingDirectory, string sha, CancellationToken ct)
    {
        var (code, _, _) = await RunAsync(workingDirectory, ct, "merge-base", "--is-ancestor", sha, "HEAD");
        return code == 0;
    }

    /// <summary>The newest commit on HEAD's history at or before the given time, or null.</summary>
    public async Task<string?> GetLastCommitBeforeAsync(
        string workingDirectory, DateTime utcTimestamp, CancellationToken ct)
    {
        var (code, stdout, _) = await RunAsync(
            workingDirectory, ct, "rev-list", "-1", $"--before={utcTimestamp:yyyy-MM-ddTHH:mm:ssZ}", "HEAD");
        var sha = stdout.Trim();
        return code == 0 && sha.Length > 0 ? sha : null;
    }

    public sealed record GitCommit(string Sha, string ShortSha, string Author, DateTime Date, string Subject);

    /// <summary>Recent commits on HEAD, newest first.</summary>
    public async Task<IReadOnlyList<GitCommit>> GetRecentCommitsAsync(
        string workingDirectory, int limit, CancellationToken ct)
    {
        var (code, stdout, _) = await RunAsync(
            workingDirectory, ct, "log", $"-{limit}", "--format=%H%x00%h%x00%an%x00%aI%x00%s%x01");
        if (code != 0)
            return [];

        var commits = new List<GitCommit>();
        foreach (var record in stdout.Split('\x01', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.TrimStart('\n', '\r').Split('\0');
            if (fields.Length < 5)
                continue;
            if (!DateTimeOffset.TryParse(fields[3], out var date))
                continue;
            commits.Add(new GitCommit(fields[0], fields[1], fields[2], date.UtcDateTime, fields[4]));
        }
        return commits;
    }

    /// <summary>
    /// Every file git knows about or would add (tracked + untracked-but-not-ignored), workspace
    /// relative. The "show all files" listing — .gitignore keeps node_modules/bin out for free.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListFilesAsync(string workingDirectory, CancellationToken ct)
    {
        var (code, stdout, _) = await RunAsync(
            workingDirectory, ct, "ls-files", "-z", "--cached", "--others", "--exclude-standard");
        if (code != 0)
            return [];
        return stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static GitFileStatus Classify(char index, char work)
    {
        if (index == '?' || work == '?') return GitFileStatus.Untracked;
        if (index == 'A' || work == 'A') return GitFileStatus.Added;
        if (index == 'D' || work == 'D') return GitFileStatus.Deleted;
        if (index == 'R' || work == 'R') return GitFileStatus.Renamed;
        return GitFileStatus.Modified;
    }

    private async Task<(int Code, string Stdout, string Stderr)> RunAsync(
        string workingDirectory, CancellationToken ct, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);

            using var process = Process.Start(psi);
            if (process is null)
                return (-1, "", "git failed to start");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(GitTimeout);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            return (process.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("git {Args} timed out in {Dir}", string.Join(' ', args), workingDirectory);
            return (-1, "", "timeout");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "git {Args} failed in {Dir}", string.Join(' ', args), workingDirectory);
            return (-1, "", ex.Message);
        }
    }
}

public enum GitFileStatus
{
    None = 0,
    Modified = 1,
    Added = 2,
    Deleted = 3,
    Renamed = 4,
    Untracked = 5,
}
