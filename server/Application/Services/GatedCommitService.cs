using Antiphon.Server.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

public enum GatedCommitOutcome
{
    NothingToCommit = 0,
    Committed = 1,
    IgnoreRulesChanged = 2,
    IgnoredPathStaged = 3,
    CommitFailed = 4,
    RepositoryBusy = 5,
}

public sealed record GatedCommitRefusal(string Path, string Rule);

public sealed record GatedCommitResult(
    GatedCommitOutcome Outcome,
    string? Sha,
    IReadOnlyList<string> Files,
    IReadOnlyList<GatedCommitRefusal> Refusals,
    string? Stderr = null);

/// <summary>
/// CARD-0527 D-5. Commits a pathspec (or the whole tree) under the repository mutation lease,
/// refusing when ignore rules changed or an ignored path would be staged. Never pushes.
/// </summary>
public sealed class GatedCommitService
{
    private readonly GitWorkspaceService _git;
    private readonly IRepositoryMutationLease _leases;
    private readonly ILogger<GatedCommitService> _logger;

    public GatedCommitService(
        GitWorkspaceService git,
        IRepositoryMutationLease leases,
        ILogger<GatedCommitService> logger)
    {
        _git = git;
        _leases = leases;
        _logger = logger;
    }

    public async Task<GatedCommitResult> CommitAsync(
        string repo,
        IReadOnlyList<string>? pathspec,
        string message,
        IReadOnlyList<(string Key, string Value)> trailers,
        CancellationToken ct)
    {
        await using var lease = await _leases.TryAcquireAsync(repo, ct);
        if (lease is null)
            return new GatedCommitResult(GatedCommitOutcome.RepositoryBusy, null, [], []);
        return await CommitHeldAsync(repo, pathspec, message, trailers, ct);
    }

    public Task<GatedCommitResult> CommitAsync(
        string repo,
        IReadOnlyList<string>? pathspec,
        string message,
        IReadOnlyList<(string Key, string Value)> trailers,
        RepositoryLease held,
        CancellationToken ct)
    {
        _ = held;
        return CommitHeldAsync(repo, pathspec, message, trailers, ct);
    }

    private async Task<GatedCommitResult> CommitHeldAsync(
        string repo,
        IReadOnlyList<string>? pathspec,
        string message,
        IReadOnlyList<(string Key, string Value)> trailers,
        CancellationToken ct)
    {
        var status = await _git.TryGetChangesAsync(repo, ct);
        if (!status.Succeeded)
        {
            return new GatedCommitResult(
                GatedCommitOutcome.CommitFailed, null, [], [], status.ExitCode.ToString());
        }

        if (status.Items.Count == 0)
            return new GatedCommitResult(GatedCommitOutcome.NothingToCommit, null, [], []);

        var ignoreRuleHits = status.Items
            .Where(c => string.Equals(System.IO.Path.GetFileName(c.Path.Replace('\\', '/')), ".gitignore", StringComparison.Ordinal))
            .Select(c => new GatedCommitRefusal(c.Path.Replace('\\', '/'), "gitignore"))
            .ToArray();
        if (ignoreRuleHits.Length > 0)
        {
            return new GatedCommitResult(
                GatedCommitOutcome.IgnoreRulesChanged, null, [], ignoreRuleHits);
        }

        var dirty = status.Items
            .Select(c => c.Path.Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var candidates = pathspec is null
            ? dirty
            : dirty.Where(p => pathspec.Any(s => string.Equals(s.Replace('\\', '/'), p, StringComparison.Ordinal)))
                .ToArray();
        if (candidates.Length == 0)
            return new GatedCommitResult(GatedCommitOutcome.NothingToCommit, null, [], []);

        var ignored = await _git.CheckIgnoredAsync(repo, candidates, ct);
        if (ignored.Count > 0)
        {
            return new GatedCommitResult(
                GatedCommitOutcome.IgnoredPathStaged,
                null,
                [],
                ignored.Select(m => new GatedCommitRefusal(m.Path, m.Rule)).ToArray());
        }

        var staged = await _git.StageAsync(repo, pathspec is null ? null : candidates, ct);
        if (staged.Code != 0)
        {
            return new GatedCommitResult(
                GatedCommitOutcome.CommitFailed, null, [], [], staged.Stderr);
        }

        var stagedPaths = await _git.StagedPathsAsync(repo, ct);
        if (pathspec is null)
        {
            var lateIgnored = await _git.CheckIgnoredAsync(repo, stagedPaths, ct);
            if (lateIgnored.Count > 0)
            {
                await _git.UnstageAsync(repo, stagedPaths, ct);
                return new GatedCommitResult(
                    GatedCommitOutcome.IgnoredPathStaged,
                    null,
                    [],
                    lateIgnored.Select(m => new GatedCommitRefusal(m.Path, m.Rule)).ToArray());
            }
        }
        else
        {
            var extra = stagedPaths
                .Where(p => !candidates.Contains(p, StringComparer.Ordinal))
                .ToArray();
            // --only leaves foreign staged paths in the index; they must not be in THIS commit.
            // The staged-set for the commit is the intersection with candidates.
            var ours = stagedPaths.Where(p => candidates.Contains(p, StringComparer.Ordinal)).ToArray();
            if (ours.Length == 0)
            {
                return new GatedCommitResult(GatedCommitOutcome.NothingToCommit, null, [], []);
            }

            _ = extra;
        }

        var commit = await _git.CommitOnlyAsync(
            repo, pathspec is null ? null : candidates, message, trailers, ct);
        if (commit.Code != 0)
        {
            _logger.LogInformation("Gated commit failed in {Repo}: {Err}", repo, commit.Stderr);
            return new GatedCommitResult(
                GatedCommitOutcome.CommitFailed, null, [], [], commit.Stderr);
        }

        var sha = await _git.HeadShaAsync(repo, ct);
        var files = sha is null ? Array.Empty<string>() : await _git.DiffTreePathsAsync(repo, sha, ct);
        return new GatedCommitResult(GatedCommitOutcome.Committed, sha, files, []);
    }
}
