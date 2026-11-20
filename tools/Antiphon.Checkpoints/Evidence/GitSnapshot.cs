using System.Diagnostics;
using System.Text;
using LibGit2Sharp;

namespace Antiphon.Checkpoints;

/// <summary>
/// The human-readable git.txt a run keeps beside its evidence (HEAD, branch, status, recent log, and the diff stat
/// against origin/master), and the branch a run registers under. HEAD and branch are read in process with libgit2 for
/// a checkout libgit2 opens at its root, exactly as git prints them, and so is the merge base (libgit2's choice
/// where criss-cross history has several best bases, which may differ from git's); status, log and diff stat stay
/// git children, so their text is git's own (they follow the user's and system config, which libgit2 here does not
/// read). git answers everything for anything else: a subdirectory, an unusual layout, an unborn or broken HEAD, a
/// branch name git would print another way, libgit2 not loadable, any error.
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
        text.AppendLine(Run(worktree, "--no-optional-locks", "status", "--porcelain"));
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
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
        {
            // Any failure (a missing object, an odd path, libgit2 not loadable): git answers, as before.
            return null;
        }
    }

    // git prints refs/heads/<name> shortened as far as it stays unambiguous (shorten_unambiguous_ref): "heads/<name>"
    // when the bare name ($GIT_DIR/<name>, such as a branch called ORIG_HEAD) or a ref of that short name elsewhere
    // also resolves. Such a name is git's to print.
    private static string? BranchOf(Repository repo)
    {
        if (repo.Info.IsHeadDetached) return "HEAD";
        var name = repo.Head.FriendlyName;
        foreach (var other in new[] { "refs/" + name, "refs/tags/" + name, "refs/remotes/" + name, "refs/remotes/" + name + "/HEAD" })
            if (repo.Refs[other] is not null) return null;
        // The bare name can only be a file in the git dir (packed-refs holds refs/ names alone); LibGit2Sharp refuses
        // to look a bare lower-case name up at all.
        var common = Antiphon.GitInProcess.InProcessGitReads.CommonDirectory(repo.Info.Path);
        if (File.Exists(Path.Combine(repo.Info.Path, name)) || common is null || File.Exists(Path.Combine(common, name)))
            return null;
        return name;
    }

    private static string? CaptureWith(Repository repo, string worktree)
    {
        var head = repo.Head.Tip;
        if (BranchOf(repo) is not { } branch) return null;
        var upstream = repo.Branches["origin/master"]?.Tip;
        var mergeBase = upstream is null ? null : repo.ObjectDatabase.FindMergeBase(upstream, head);
        var text = new StringBuilder();
        text.AppendLine("HEAD=" + head.Sha);
        text.AppendLine("branch=" + branch);
        text.AppendLine("status:");
        // --no-optional-locks: a diagnostic read never takes index.lock from a worktree an agent may be committing in.
        text.AppendLine(Run(worktree, "--no-optional-locks", "status", "--porcelain"));
        text.AppendLine("log:");
        // The commit read above, not the symbolic HEAD: a commit landing meanwhile cannot split the snapshot.
        text.AppendLine(Run(worktree, "log", "-5", "--oneline", head.Sha));
        if (mergeBase is not null)
        {
            text.AppendLine("diffstat:");
            text.AppendLine(Run(worktree, "diff", "--stat", mergeBase.Sha + ".." + head.Sha));
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
