using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Exceptions;
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
        var taskId = Guid.Parse(trailers.Single(t => t.Key == "antiphon-task").Value);
        var operationId = Guid.NewGuid();
        var status = await _git.TryGetChangesAsync(repo, ct);
        if (!status.Succeeded)
        {
            return new GatedCommitResult(
                GatedCommitOutcome.CommitFailed, null, [], [], status.ExitCode.ToString());
        }

        if (status.Items.Count == 0)
            return new GatedCommitResult(GatedCommitOutcome.NothingToCommit, null, [], []);

        var dirty = status.Items
            .SelectMany(c => c.OldPath is null ? new[] { c.Path } : new[] { c.Path, c.OldPath })
            .Select(p => p.Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var ignoreRuleHits = dirty
            .Where(p => string.Equals(System.IO.Path.GetFileName(p), ".gitignore", StringComparison.Ordinal))
            .Select(p => new GatedCommitRefusal(p, "gitignore"))
            .ToArray();
        if (ignoreRuleHits.Length > 0)
        {
            return new GatedCommitResult(
                GatedCommitOutcome.IgnoreRulesChanged, null, [], ignoreRuleHits);
        }

        // A rename is one change: selecting either endpoint must include the other endpoint.
        var scopePaths = pathspec?.Select(p => p.Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
        var selected = scopePaths is null ? dirty : status.Items
            .Where(c => scopePaths.Contains(c.Path.Replace('\\', '/'))
                || c.OldPath is not null && scopePaths.Contains(c.OldPath.Replace('\\', '/')))
            .SelectMany(c => c.OldPath is null ? new[] { c.Path } : new[] { c.Path, c.OldPath })
            .Select(p => p.Replace('\\', '/')).Distinct(StringComparer.Ordinal).ToArray();
        var candidates = pathspec is null
            ? dirty
            : selected;
        if (candidates.Length == 0)
            return new GatedCommitResult(GatedCommitOutcome.NothingToCommit, null, [], []);

        var ignored = await _git.CheckIgnoredAsync(repo, candidates, ct);
        if (!ignored.Succeeded)
            return new(GatedCommitOutcome.CommitFailed, null, [], [], ignored.Error);
        if (ignored.Items.Count > 0)
        {
            return new GatedCommitResult(
                GatedCommitOutcome.IgnoredPathStaged,
                null,
                [],
                ignored.Items.Select(m => new GatedCommitRefusal(m.Path, m.Rule)).ToArray());
        }

        // Keep the exact staged contents, including foreign partial staging, for a late refusal.
        var index = await _git.CaptureIndexAsync(repo, ct);
        if (index.Code != 0)
            return new(GatedCommitOutcome.CommitFailed, null, [], [], index.Stderr);
        // Porcelain rename records already have the old endpoint removed from the index.
        // Stage the current paths; commit --only below still includes both HEAD endpoints.
        var stageCandidates = candidates.Where(p => status.Items.Any(c => c.Path.Replace('\\', '/') == p)).ToArray();
        var staged = await _git.StageAsync(repo, pathspec is null ? null : stageCandidates, ct);
        if (staged.Code != 0)
        {
            return new GatedCommitResult(
                GatedCommitOutcome.CommitFailed, null, [], [], staged.Stderr);
        }

        var inspection = await _git.StagedPathsAsync(repo, ct);
        if (!inspection.Succeeded)
            return await RefuseAfterStageAsync(inspection.Error);
        var stagedPaths = inspection.Items;
        if (pathspec is null)
        {
            var lateIgnored = await _git.CheckIgnoredAsync(repo, stagedPaths, ct);
            if (!lateIgnored.Succeeded)
                return await RefuseAfterStageAsync(lateIgnored.Error);
            if (lateIgnored.Items.Count > 0)
            {
                var restored = await _git.RestoreIndexAsync(repo, index.Stdout.Trim(), ct);
                if (restored.Code != 0)
                    return new(GatedCommitOutcome.CommitFailed, null, [], [], "Index restore failed: " + restored.Stderr);
                return new GatedCommitResult(
                    GatedCommitOutcome.IgnoredPathStaged,
                    null,
                    [],
                    lateIgnored.Items.Select(m => new GatedCommitRefusal(m.Path, m.Rule)).ToArray());
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
            repo, pathspec is null ? null : candidates, message,
            [.. trailers, ("antiphon-operation", operationId.ToString("D"))], ct);
        if (commit.Code != 0)
        {
            _logger.LogInformation("Gated commit failed in {Repo}: {Err}", repo, commit.Stderr);
            return new GatedCommitResult(
                GatedCommitOutcome.CommitFailed, null, [], [], commit.Stderr);
        }

        // A successful mutation must never turn into a refusal or an invented empty footprint.
        // Use the operation trailer, since another commit (including a hook) can advance HEAD.
        return await InspectCommitAsync(repo, taskId, operationId, knownCommitted: true, ct);

        async Task<GatedCommitResult> RefuseAfterStageAsync(string? error)
        {
            var restored = await _git.RestoreIndexAsync(repo, index.Stdout.Trim(), ct);
            return new(GatedCommitOutcome.CommitFailed, null, [], [], error
                + (restored.Code == 0 ? "" : "; index restore failed: " + restored.Stderr));
        }
    }

    /// <summary>Read the exact completed operation without staging or committing again.</summary>
    public Task<GatedCommitResult> RecoverAsync(
        string repo, Guid taskId, Guid operationId, CancellationToken ct) =>
        InspectCommitAsync(repo, taskId, operationId, knownCommitted: false, ct);

    private async Task<GatedCommitResult> InspectCommitAsync(
        string repo, Guid taskId, Guid operationId, bool knownCommitted, CancellationToken ct)
    {
        var commits = await _git.FindCommitOperationAsync(repo, taskId, operationId, ct);
        if (!commits.Succeeded || commits.Items.Count != 1)
        {
            if (knownCommitted) throw new CommitInspectionPendingException(operationId);
            if (commits.Succeeded && commits.Items.Count == 0)
                throw new NotFoundException("Commit operation", operationId);
            throw new ServiceUnavailableException("Commit recovery inspection is unavailable or ambiguous.",
                "commit_recovery_unavailable");
        }
        var sha = commits.Items[0];
        var files = await _git.TryDiffTreePathsAsync(repo, sha, ct);
        if (!files.Succeeded || files.Items.Count == 0)
            throw new CommitInspectionPendingException(operationId, sha);
        return new GatedCommitResult(GatedCommitOutcome.Committed, sha, files.Items, []);
    }
}
