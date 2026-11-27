using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// <see cref="IGitRepository"/> answered with git commands through an <see cref="ILandingGit"/> runner: what any runner
/// hands out by default, and what a faster implementation falls back to when it cannot answer.
/// </summary>
public class GitCommandRepository(ILandingGit git, string path) : IGitRepository
{
    private static readonly string[] OperationStates = ["MERGE_HEAD", "rebase-merge", "rebase-apply"];

    public string Path { get; } = path;

    /// <summary>
    /// <c>rev-list --min-parents=2</c> for a merge beyond the target (Unknown), then <c>git cherry</c>: Contained when it
    /// marks every commit as already applied on the target, NotContained when any is not.
    /// </summary>
    public virtual async Task<DelegationWorktreeService.CommitContainment> ContainsUnmergedAsync(string target,
        string source, CancellationToken ct)
    {
        var merges = await git.RunAsync(Path, ["rev-list", "--max-count=1", "--min-parents=2", $"{target}..{source}"], ct);
        if (!merges.Succeeded || !string.IsNullOrWhiteSpace(merges.Output))
            return DelegationWorktreeService.CommitContainment.Unknown;
        var cherry = await git.RunAsync(Path, ["cherry", target, source], ct);
        if (!cherry.Succeeded) return DelegationWorktreeService.CommitContainment.Unknown;
        var lines = cherry.Output.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Any(static line => !line.StartsWith('+') && !line.StartsWith('-')))
            return DelegationWorktreeService.CommitContainment.Unknown;
        return lines.Any(static line => line.StartsWith('+'))
            ? DelegationWorktreeService.CommitContainment.NotContained
            : DelegationWorktreeService.CommitContainment.Contained;
    }

    /// <summary><c>rev-parse --git-path</c> of MERGE_HEAD, rebase-merge and rebase-apply, each checked on disk.</summary>
    public virtual async Task<bool> OperationInProgressAsync(CancellationToken ct)
    {
        foreach (var state in OperationStates)
        {
            var location = await git.RunAsync(Path, ["rev-parse", "--git-path", state], ct);
            if (!location.Succeeded) throw new IOException("git_checkout_operation_probe_failed");
            // git prints the path relative to the checkout for a main worktree.
            var gitPath = System.IO.Path.Combine(Path, location.Output.Trim());
            if (File.Exists(gitPath) || Directory.Exists(gitPath)) return true;
        }
        return false;
    }
}
