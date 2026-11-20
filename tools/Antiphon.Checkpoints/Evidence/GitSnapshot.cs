using System.Diagnostics;
using System.Text;
using LibGit2Sharp;

namespace Antiphon.Checkpoints;

/// <summary>
/// The human-readable git.txt a run keeps beside its evidence (HEAD, branch, status, recent log, and the diff stat
/// against origin/master), and the branch a run registers under. Read in process with libgit2 (no git child) for
/// a checkout libgit2 opens at its root; git answers anything else (a subdirectory, a layout libgit2 does not open,
/// libgit2 not loadable). The diagnostics are git's in substance, not byte for byte (status codes and the diff stat
/// are libgit2's view); the branch is exactly what `git rev-parse --abbrev-ref HEAD` prints.
/// </summary>
public static class GitSnapshot
{
    public static string Capture(string worktree)
    {
        if (InProcess(worktree, CaptureWith) is { } captured)
            return captured;
        var text = new StringBuilder();
        text.AppendLine("HEAD=" + Run(worktree, "rev-parse", "HEAD"));
        text.AppendLine("branch=" + Run(worktree, "rev-parse", "--abbrev-ref", "HEAD"));
        text.AppendLine("status:");
        text.AppendLine(Run(worktree, "status", "--porcelain"));
        text.AppendLine("log:");
        text.AppendLine(Run(worktree, "log", "-5", "--oneline"));
        var mergeBase = Run(worktree, "merge-base", "origin/master", "HEAD");
        if (!mergeBase.StartsWith("git-failed", StringComparison.Ordinal) && mergeBase.Length > 0)
        {
            text.AppendLine("diffstat:");
            text.AppendLine(Run(worktree, "diff", "--stat", mergeBase.Trim() + "..HEAD"));
        }

        return text.ToString();
    }

    /// <summary>`git rev-parse --abbrev-ref HEAD`: the branch name, or HEAD when detached.</summary>
    public static string Branch(string worktree) =>
        InProcess(worktree, repo => repo.Info.IsHeadUnborn ? null : repo.Info.IsHeadDetached ? "HEAD" : repo.Head.FriendlyName)
        ?? Run(worktree, "rev-parse", "--abbrev-ref", "HEAD");

    /// <summary>`git rev-parse HEAD`.</summary>
    public static string Head(string worktree) =>
        InProcess(worktree, repo => repo.Head.Tip?.Sha) ?? Run(worktree, "rev-parse", "HEAD");

    private static string? InProcess(string worktree, Func<Repository, string?> read)
    {
        if (string.IsNullOrWhiteSpace(worktree) || !ToolLibGit2.Available) return null;
        try
        {
            using var repo = new Repository(worktree);
            // Only a checkout opened at its own root, as git run there would see it.
            if (repo.Info.IsBare || repo.Info.WorkingDirectory is null
                || !string.Equals(Path.GetFullPath(repo.Info.WorkingDirectory).TrimEnd('\\', '/'),
                    Path.GetFullPath(worktree).TrimEnd('\\', '/'),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                || Antiphon.GitInProcess.InProcessGitReads.Unusual(repo))
                return null;
            return read(repo);
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException
                                       || ToolLibGit2.IsNativeUnavailable(ex))
        {
            return null;
        }
    }

    private static string? CaptureWith(Repository repo)
    {
        if (repo.Info.IsHeadUnborn) return null; // git's own messages for an empty repository
        var head = repo.Head.Tip;
        var text = new StringBuilder();
        text.AppendLine("HEAD=" + head.Sha);
        text.AppendLine("branch=" + (repo.Info.IsHeadDetached ? "HEAD" : repo.Head.FriendlyName));
        text.AppendLine("status:");
        var status = new StringBuilder();
        foreach (var entry in repo.RetrieveStatus(new StatusOptions { IncludeUntracked = true, RecurseUntrackedDirs = false, IncludeIgnored = false }))
        {
            if (entry.State is FileStatus.Unaltered or FileStatus.Ignored) continue;
            if (status.Length > 0) status.Append('\n');
            status.Append(Code(entry.State)).Append(' ').Append(entry.FilePath.Replace('\\', '/'));
        }
        text.AppendLine(status.ToString());
        text.AppendLine("log:");
        text.AppendLine(string.Join('\n', repo.Commits.Take(5).Select(commit => commit.Sha[..7] + " " + commit.MessageShort)));
        var upstream = repo.Branches["origin/master"]?.Tip;
        var mergeBase = upstream is null ? null : repo.ObjectDatabase.FindMergeBase(upstream, head);
        if (mergeBase is not null)
        {
            text.AppendLine("diffstat:");
            var stats = repo.Diff.Compare<PatchStats>(mergeBase.Tree, head.Tree);
            var lines = stats.Select(file => " " + file.Path.Replace('\\', '/') + " | " + (file.LinesAdded + file.LinesDeleted)).ToList();
            lines.Add($" {lines.Count} files changed, {stats.TotalLinesAdded} insertions(+), {stats.TotalLinesDeleted} deletions(-)");
            text.AppendLine(string.Join('\n', lines));
        }

        return text.ToString();
    }

    // Porcelain v1's two columns (index, work tree) as libgit2 sees them.
    private static string Code(FileStatus state)
    {
        if (state.HasFlag(FileStatus.Conflicted)) return "UU";
        if (state == FileStatus.NewInWorkdir) return "??";
        var index = state.HasFlag(FileStatus.NewInIndex) ? 'A'
            : state.HasFlag(FileStatus.ModifiedInIndex) ? 'M'
            : state.HasFlag(FileStatus.DeletedFromIndex) ? 'D'
            : state.HasFlag(FileStatus.RenamedInIndex) ? 'R'
            : state.HasFlag(FileStatus.TypeChangeInIndex) ? 'T' : ' ';
        var work = state.HasFlag(FileStatus.ModifiedInWorkdir) ? 'M'
            : state.HasFlag(FileStatus.DeletedFromWorkdir) ? 'D'
            : state.HasFlag(FileStatus.RenamedInWorkdir) ? 'R'
            : state.HasFlag(FileStatus.TypeChangeInWorkdir) ? 'T' : ' ';
        return new string([index, work]);
    }

    public static string Run(string worktree, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = worktree,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);
            using var process = Process.Start(psi);
            if (process is null)
                return "git-failed";
            var stdout = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(30_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return "git-failed timeout";
            }

            return process.ExitCode == 0 ? stdout.Trim() : "git-failed " + process.ExitCode;
        }
        catch (Exception ex)
        {
            return "git-failed " + ex.Message;
        }
    }
}
