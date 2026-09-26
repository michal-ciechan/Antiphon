using System.Diagnostics;
using System.Text;

namespace Antiphon.Checkpoints;

public static class GitSnapshot
{
    public static string Capture(string worktree)
    {
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
