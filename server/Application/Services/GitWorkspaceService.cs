using System.Diagnostics;
using System.Text;
using Antiphon.Server.Application.Dtos;

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

    /// <summary>Resolve a ref/short sha to the full commit sha, or null when it doesn't exist.</summary>
    public async Task<string?> ResolveCommitAsync(string workingDirectory, string gitRef, CancellationToken ct)
    {
        var (code, stdout, _) = await RunAsync(
            workingDirectory, ct, "rev-parse", "--verify", "--quiet", $"{gitRef}^{{commit}}");
        var sha = stdout.Trim();
        return code == 0 && sha.Length > 0 ? sha : null;
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

    /// <summary>
    /// Which repo the directory belongs to and what is checked out there. RepoRoot is the MAIN
    /// checkout's root: a linked worktree's --git-common-dir lives under the primary repo, so
    /// resolving its parent maps any worktree (or subdirectory) back to the repo it came from.
    /// Non-repos degrade to IsGitRepository=false, never an error.
    /// </summary>
    public async Task<WorkspaceGitInfoDto> GetWorkspaceInfoAsync(string workingDirectory, CancellationToken ct)
    {
        var (code, stdout, _) = await RunAsync(
            workingDirectory, ct, "rev-parse", "--path-format=absolute", "--show-toplevel", "--git-common-dir");
        string[] lines = code == 0
            ? stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
        if (lines.Length < 2)
            return new WorkspaceGitInfoDto(workingDirectory, false, null, null, false);

        var toplevel = Path.GetFullPath(lines[0]);
        var commonDir = Path.GetFullPath(lines[1]);

        // Bare/odd layouts (common dir not named ".git") fall back to the local toplevel.
        var repoRoot = string.Equals(Path.GetFileName(commonDir), ".git", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(commonDir) ?? toplevel
            : toplevel;
        var isWorktree = !string.Equals(toplevel, repoRoot, StringComparison.OrdinalIgnoreCase);

        // `branch --show-current` is empty on detached HEAD and survives an unborn branch,
        // unlike `rev-parse --abbrev-ref HEAD` which hard-fails before the first commit.
        var (branchCode, branchOut, _) = await RunAsync(workingDirectory, ct, "branch", "--show-current");
        var branch = branchCode == 0 ? branchOut.Trim() : "";

        return new WorkspaceGitInfoDto(
            workingDirectory, true, repoRoot, branch.Length > 0 ? branch : null, isWorktree);
    }

    /// <summary>All worktrees of the repo containing the directory — main checkout first.</summary>
    public async Task<IReadOnlyList<WorktreeEntryDto>> ListWorktreesAsync(
        string workingDirectory, CancellationToken ct)
    {
        var (code, stdout, stderr) = await RunAsync(workingDirectory, ct, "worktree", "list", "--porcelain");
        if (code != 0)
        {
            _logger.LogDebug("git worktree list failed in {Dir}: {Err}", workingDirectory, stderr);
            return [];
        }
        return ParseWorktreeList(stdout);
    }

    /// <summary>
    /// Parses `git worktree list --porcelain`: blocks of `worktree &lt;path&gt;` / `HEAD` /
    /// `branch refs/heads/x`|`detached` / `locked` / `bare`, blank-line separated. The first
    /// block is the main checkout; a bare main repo is skipped — it has no browsable tree.
    /// </summary>
    internal static IReadOnlyList<WorktreeEntryDto> ParseWorktreeList(string porcelain)
    {
        var entries = new List<WorktreeEntryDto>();
        string? path = null;
        string? branch = null;
        bool detached = false, locked = false, bare = false;
        var blockIndex = 0;

        void Flush()
        {
            if (path is not null && !bare)
                entries.Add(new WorktreeEntryDto(
                    Path.GetFullPath(path), branch, IsMain: blockIndex == 0, locked, detached));
            if (path is not null)
                blockIndex++;
            path = branch = null;
            detached = locked = bare = false;
        }

        foreach (var raw in porcelain.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                Flush();
                path = line["worktree ".Length..];
            }
            else if (line.StartsWith("branch ", StringComparison.Ordinal))
            {
                branch = line["branch ".Length..];
                const string heads = "refs/heads/";
                if (branch.StartsWith(heads, StringComparison.Ordinal))
                    branch = branch[heads.Length..];
            }
            else if (line == "detached") detached = true;
            else if (line == "bare") bare = true;
            else if (line == "locked" || line.StartsWith("locked ", StringComparison.Ordinal)) locked = true;
        }
        Flush();
        return entries;
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
