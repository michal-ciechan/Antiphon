using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Exceptions;
using Microsoft.Extensions.Logging;
using System.Text.Json;

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
        var tag = OwnerFromTrailers(trailers);
        await using var lease = await _leases.TryAcquireAsync(repo, tag, ct);
        if (lease is null)
            return new GatedCommitResult(GatedCommitOutcome.RepositoryBusy, null, [], []);
        return await CommitHeldAsync(repo, pathspec, message, trailers, ct);
    }

    private static RepositoryLeaseOwnerTag OwnerFromTrailers(IReadOnlyList<(string Key, string Value)> trailers)
    {
        var raw = trailers.Single(t => t.Key == "antiphon-task").Value;
        return new RepositoryLeaseOwnerTag(Guid.Parse(raw), RepositoryLeasePurposes.GatedCommit);
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
        IReadOnlyList<string> expectedPaths = stagedPaths;
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
            // Existing foreign staged contents are allowed, but staging must not alter them.
            var changed = await _git.ChangedIndexPathsAsync(repo, index.Stdout.Trim(), ct);
            if (!changed.Succeeded || changed.Items.Any(p => !candidates.Contains(p, StringComparer.Ordinal)))
                return await RefuseAfterStageAsync("Scoped staging changed paths outside the approved selection: " + changed.Error);
            // Staging can remove a selected change that was reverted in the working tree.
            // The manifest describes the actual staged footprint, not the earlier candidates.
            expectedPaths = stagedPaths.Where(p => candidates.Contains(p, StringComparer.Ordinal)).ToArray();
            if (expectedPaths.Count == 0)
            {
                return new GatedCommitResult(GatedCommitOutcome.NothingToCommit, null, [], []);
            }
        }

        var commit = await _git.CommitOnlyAsync(
            repo, pathspec is null ? null : candidates, message,
            [.. trailers, ("antiphon-operation", operationId.ToString("D")),
                ("antiphon-paths", JsonSerializer.Serialize(expectedPaths))], ct);
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
        var files = await InspectCommittedFilesAsync(repo, sha, requireApprovedPaths: knownCommitted, ct);
        if (!files.Succeeded || files.Items.Count == 0)
            throw new CommitInspectionPendingException(operationId, sha);
        return new GatedCommitResult(GatedCommitOutcome.Committed, sha, files.Items, []);
    }

    /// <summary>All commit receipts, including settlement recovery, validate the same saved selection.</summary>
    public async Task<GitWorkspaceService.GitStrictList<string>> InspectCommittedFilesAsync(
        string repo, string sha, bool requireApprovedPaths, CancellationToken ct)
    {
        var files = await _git.TryDiffTreePathsAsync(repo, sha, ct);
        if (!files.Succeeded || files.Items.Count == 0) return files;
        var approved = await _git.ApprovedCommitPathsAsync(repo, sha, ct);
        if (approved.Code != 0) return new(false, [], approved.Code, approved.Stderr);
        // Older gated commits predate the manifest. New successful mutations require it.
        if (string.IsNullOrWhiteSpace(approved.Stdout) && !requireApprovedPaths) return files;
        try
        {
            var paths = JsonSerializer.Deserialize<string[]>(approved.Stdout.Trim());
            if (paths is { Length: > 0 } && paths.ToHashSet(StringComparer.Ordinal).SetEquals(files.Items))
                return files;
        }
        catch (JsonException) { }
        return new(false, [], -1, "Committed paths do not match the approved selection.");
    }
}
