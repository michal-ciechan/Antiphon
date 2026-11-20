using System.Diagnostics;
using System.Text;
using LibGit2Sharp;

namespace Antiphon.Checkpoints;

/// <summary>
/// The human-readable git.txt a run keeps beside its evidence (HEAD, branch, status, recent log, and the diff stat
/// against origin/master), and the branch a run registers under. HEAD, branch, log, merge base and diff stat are read
/// in process with libgit2 for a checkout libgit2 opens at its root; the status stays one `git status --porcelain`
/// child (libgit2 here does not read the user's or system config, which can change what status shows, such as
/// core.autocrlf or an ignore file). git answers everything for anything else: a subdirectory, an unusual layout, an
/// unborn or broken HEAD, an ambiguous branch name, libgit2 not loadable, any error. Log and diff stat are git's in
/// substance, not byte for byte; the branch is exactly what `git rev-parse --abbrev-ref HEAD` prints.
/// </summary>
public static class GitSnapshot
{
    public static string Capture(string worktree)
    {
        if (CaptureInProcess(worktree) is { } captured)
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
        InProcess(worktree, BranchOf) ?? Run(worktree, "rev-parse", "--abbrev-ref", "HEAD");

    /// <summary>`git rev-parse HEAD`.</summary>
    public static string Head(string worktree) =>
        InProcess(worktree, repo => repo.Head.Tip?.Sha) ?? Run(worktree, "rev-parse", "HEAD");

    /// <summary>The snapshot read in process, or null when git must answer (see the class summary).</summary>
    internal static string? CaptureInProcess(string worktree) => InProcess(worktree, repo => CaptureWith(repo, worktree));

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
                || Antiphon.GitInProcess.InProcessGitReads.Unusual(repo)
                || repo.Info.IsHeadUnborn || repo.Head.Tip is null)
                return null;
            return read(repo);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            // Any failure (a missing object, an odd path, libgit2 not loadable): git answers, as before.
            return null;
        }
    }

    // git prints refs/heads/<name> shortened as far as it stays unambiguous: "heads/<name>" when a ref of that
    // short name exists elsewhere. Such a name is git's to print.
    private static string? BranchOf(Repository repo)
    {
        if (repo.Info.IsHeadDetached) return "HEAD";
        var name = repo.Head.FriendlyName;
        foreach (var other in new[] { "refs/" + name, "refs/tags/" + name, "refs/remotes/" + name, "refs/remotes/" + name + "/HEAD" })
            if (repo.Refs[other] is not null) return null;
        return name;
    }

    private static string? CaptureWith(Repository repo, string worktree)
    {
        var head = repo.Head.Tip;
        if (BranchOf(repo) is not { } branch) return null;
        var text = new StringBuilder();
        text.AppendLine("HEAD=" + head.Sha);
        text.AppendLine("branch=" + branch);
        text.AppendLine("status:");
        text.AppendLine(Run(worktree, "status", "--porcelain"));
        text.AppendLine("log:");
        text.AppendLine(string.Join('\n', repo.Commits.Take(5).Select(commit =>
            repo.ObjectDatabase.ShortenObjectId(commit) + " " + commit.MessageShort)));
        var upstream = repo.Branches["origin/master"]?.Tip;
        var mergeBase = upstream is null ? null : repo.ObjectDatabase.FindMergeBase(upstream, head);
        if (mergeBase is not null)
        {
            text.AppendLine("diffstat:");
            var patch = repo.Diff.Compare<Patch>(mergeBase.Tree, head.Tree,
                new CompareOptions { Similarity = SimilarityOptions.Renames });
            var lines = patch.Select(file => " " + file.Path.Replace('\\', '/') + " | "
                + (file.IsBinaryComparison ? "Bin" : (file.LinesAdded + file.LinesDeleted).ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList();
            if (lines.Count > 0)
                lines.Add(" " + lines.Count + (lines.Count == 1 ? " file changed" : " files changed")
                    + (patch.LinesAdded > 0 ? ", " + patch.LinesAdded + (patch.LinesAdded == 1 ? " insertion(+)" : " insertions(+)") : "")
                    + (patch.LinesDeleted > 0 ? ", " + patch.LinesDeleted + (patch.LinesDeleted == 1 ? " deletion(-)" : " deletions(-)") : ""));
            text.AppendLine(string.Join('\n', lines));
        }

        return text.ToString();
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
