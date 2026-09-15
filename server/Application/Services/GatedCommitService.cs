using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Services;

public enum GatedCommitOutcome
{
    Committed, NothingToCommit, IgnoreRulesChanged, IgnoredPathStaged, RepositoryBusy, CommitFailed,
}

public sealed record GatedCommitResult(
    GatedCommitOutcome Outcome, string? Sha = null, IReadOnlyList<string>? Files = null,
    IReadOnlyList<GitWorkspaceService.IgnoredPath>? Refusals = null, string? Stderr = null);

/// <summary>Local commits under the repository lease, with git's own ignore semantics and hooks.</summary>
public sealed class GatedCommitService(GitWorkspaceService git, IRepositoryMutationLease leases, ILandingGit landingGit)
{
    public async Task<GatedCommitResult> CommitAsync(string repo, IReadOnlyList<string>? pathspec,
        string message, IReadOnlyDictionary<string, string> trailers, CancellationToken ct)
    {
        await using var lease = await leases.TryAcquireAsync(repo, ct);
        if (lease is null) return new(GatedCommitOutcome.RepositoryBusy);
        return await CommitAsync(repo, pathspec, message, trailers, lease, ct);
    }

    public async Task<GatedCommitResult> CommitAsync(string repo, IReadOnlyList<string>? pathspec,
        string message, IReadOnlyDictionary<string, string> trailers, RepositoryLease lease, CancellationToken ct)
    {
        if (!leases.Owns(lease, await landingGit.CommonDirectoryAsync(repo, ct)))
            return new(GatedCommitOutcome.RepositoryBusy);
        try
        {
            var status = await git.TryGetChangesAsync(repo, ct);
            if (!status.Succeeded) return new(GatedCommitOutcome.CommitFailed, Stderr: "git status failed");
            if (status.Items.Count == 0) return new(GatedCommitOutcome.NothingToCommit);
            var dirty = status.Items.SelectMany(c => c.OldPath is null ? new[] { c.Path } : new[] { c.Path, c.OldPath })
                .Distinct(StringComparer.Ordinal).ToArray();
            var ignoreFiles = dirty.Where(p => Path.GetFileName(p) == ".gitignore")
                .Select(p => new GitWorkspaceService.IgnoredPath(p, "ignore rules changed")).ToArray();
            if (ignoreFiles.Length > 0) return new(GatedCommitOutcome.IgnoreRulesChanged, Refusals: ignoreFiles);
            var candidates = dirty.Where(p => pathspec is null || pathspec.Contains(p, StringComparer.Ordinal)).ToArray();
            if (candidates.Length == 0) return new(GatedCommitOutcome.NothingToCommit);
            var ignored = await git.CheckIgnoredAsync(repo, candidates, ct);
            if (ignored.Count > 0) return new(GatedCommitOutcome.IgnoredPathStaged, Refusals: ignored);
            var before = await git.StagedPathsAsync(repo, ct);
            var stagedResult = await git.StageAsync(repo, pathspec is null ? null : candidates, ct);
            if (stagedResult.Code != 0) return new(GatedCommitOutcome.CommitFailed, Stderr: stagedResult.Stderr);
            var staged = await git.StagedPathsAsync(repo, ct);
            ignored = pathspec is null ? await git.CheckIgnoredAsync(repo, staged, ct)
                : staged.Except(candidates, StringComparer.Ordinal).Except(before, StringComparer.Ordinal)
                    .Select(p => new GitWorkspaceService.IgnoredPath(p, "path outside task footprint")).ToArray();
            if (ignored.Count > 0)
            {
                var reset = await git.UnstageAsync(repo, pathspec is null ? staged : staged.Except(before, StringComparer.Ordinal).ToArray(), ct);
                return new(GatedCommitOutcome.IgnoredPathStaged, Refusals: ignored,
                    Stderr: reset.Code == 0 ? null : reset.Stderr);
            }
            var committed = await git.CommitOnlyAsync(repo, pathspec is null ? null : candidates, message, trailers, ct);
            if (committed.Code != 0) return new(GatedCommitOutcome.CommitFailed, Stderr: committed.Stderr);
            var sha = await git.GetHeadShaAsync(repo, ct);
            if (sha is null) return new(GatedCommitOutcome.CommitFailed, Stderr: "Committed but HEAD could not be read");
            return new(GatedCommitOutcome.Committed, sha, await git.DiffTreePathsAsync(repo, sha, ct));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new(GatedCommitOutcome.CommitFailed, Stderr: ex.Message); }
    }
}
