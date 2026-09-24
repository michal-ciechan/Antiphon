using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0666. A caller start ref (<c>delegate.ps1 -StartRef</c>) is made available at task CREATE,
/// before the row exists: a full commit SHA pushed from another checkout (the server2 runner, a
/// sibling task) is fetched from origin here, once, bounded. Dispatch never fetches; it only checks
/// the commit is local, so no network call ever runs in the dispatch claim or under a task row lock.
/// <para>
/// The fetch runs under the repository mutation lease (purpose <c>start-ref-fetch</c>) and goes
/// through <see cref="ILandingGit.RunAsync"/>, whose owned-child path journals <c>fetch</c> like every
/// other repository-mutating git child. A dispatch in the same repository meanwhile is held (not
/// blocked). A lease another operation holds past the one network deadline refuses the create as
/// busy; the fetch never runs unfenced.
/// </para>
/// Every refusal names the ref, the repository and which of these it was, each with its own code.
/// </summary>
public sealed class StartRefAvailability(
    ILandingGit git, IRepositoryMutationLease leases, ILogger<StartRefAvailability> logger, TimeSpan? fetchTimeout = null)
{
    public const string NotFullShaCode = "worktree_start_ref_not_full_sha";
    public const string NotCommitCode = "worktree_start_ref_not_commit";
    public const string NoOriginCode = "worktree_start_ref_no_origin";
    public const string FetchTimeoutCode = "worktree_start_ref_fetch_timeout";
    public const string FetchFailedCode = "worktree_start_ref_fetch_failed";
    public const string NotOnOriginCode = "worktree_start_ref_not_on_origin";
    public const string RepositoryBusyCode = "worktree_start_ref_repository_busy";

    /// <summary>
    /// Sized for an interactive create call, not a bulk transfer: one commit's objects. One deadline
    /// for all network work at create: the fetch and, after a failed fetch, the origin probe share it.
    /// </summary>
    public static readonly TimeSpan DefaultFetchTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan LocalGitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LeasePollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly Regex FullShaPattern = new("^([0-9a-fA-F]{40}|[0-9a-fA-F]{64})$", RegexOptions.Compiled);
    // A ref name nothing creates: ls-remote with it lists nothing but still has to reach origin.
    private const string ProbePattern = "refs/antiphon/start-ref-probe";
    private const string Field = nameof(CreateAgentTaskRequest.WorktreeBaseRequestedRef);

    private readonly TimeSpan _fetchTimeout = fetchTimeout is { } t && t > TimeSpan.Zero ? t : DefaultFetchTimeout;

    public async Task EnsureAvailableAsync(string repoPath, string startRef, CancellationToken ct)
    {
        if (await ResolvesAsync(repoPath, startRef + "^{commit}", ct))
            return;
        if (await ResolvesAsync(repoPath, startRef + "^{object}", ct))
            Refuse(NotCommitCode, $"Start ref '{startRef}' names an object in {repoPath} that is not a commit.");
        if (!FullShaPattern.IsMatch(startRef))
            Refuse(NotFullShaCode,
                $"Start ref '{startRef}' does not resolve to a commit in {repoPath}. Only a full 40- or 64-hex "
                + "commit SHA is fetched from origin; a short SHA or a branch or tag name must already exist "
                + "locally. Pass the full SHA, or fetch the ref first.");

        var remote = await RunBoundedAsync(repoPath, ["remote", "get-url", "origin"], LocalGitTimeout, ct);
        if (remote.TimedOut)
            Unavailable(FetchTimeoutCode,
                $"Start ref '{startRef}' is not in {repoPath}, and reading its origin remote did not finish within "
                + $"{LocalGitTimeout.TotalSeconds:0}s. The task was not created; retry.");
        if (!remote.Result!.Succeeded)
            Refuse(NoOriginCode,
                $"Start ref '{startRef}' is not in {repoPath}, and the repository has no 'origin' remote to fetch it from.");

        // One deadline covers all network work: the lease wait, the fetch, and the probe share it.
        var network = System.Diagnostics.Stopwatch.StartNew();
        (LandingGitResult? Result, bool TimedOut) fetch;
        await using (await AcquireLeaseAsync(repoPath, startRef, network, ct))
        {
            fetch = await RunBoundedAsync(repoPath, ["fetch", "--no-tags", "--quiet", "origin", startRef],
                _fetchTimeout - network.Elapsed, ct);
            if (fetch.TimedOut)
                Unavailable(FetchTimeoutCode,
                    $"Start ref '{startRef}' is not in {repoPath}, and fetching it from origin did not finish within "
                    + $"{_fetchTimeout.TotalSeconds:0}s. The task was not created; retry, or fetch the commit first.");
            if (fetch.Result!.Succeeded && await ResolvesAsync(repoPath, startRef + "^{commit}", ct))
            {
                logger.LogInformation("Fetched start ref {StartRef} from origin into {RepoPath} at create", startRef, repoPath);
                return;
            }
        }

        // Git's own wording is locale-dependent and may carry an endpoint, so the verdict comes from
        // whether origin answers at all: it answering while the fetch failed means it lacks the commit.
        var probe = await RunBoundedAsync(repoPath, ["ls-remote", "origin", ProbePattern], _fetchTimeout - network.Elapsed, ct);
        if (probe.TimedOut)
            Unavailable(FetchTimeoutCode,
                $"Start ref '{startRef}' is not in {repoPath}; fetching it from origin failed "
                + $"({fetch.Result.Diagnostic}) and origin did not answer within the {_fetchTimeout.TotalSeconds:0}s budget it shares with the fetch. "
                + "The task was not created; retry, or fetch the commit first.");
        if (!probe.Result!.Succeeded)
            Unavailable(FetchFailedCode,
                $"Start ref '{startRef}' is not in {repoPath}, and origin could not be reached or refused "
                + $"authentication (fetch {fetch.Result.Diagnostic}, ls-remote {probe.Result.Diagnostic}). "
                + "The task was not created; check origin's URL and credentials, then retry.");
        Refuse(NotOnOriginCode,
            $"Start ref '{startRef}' is not in {repoPath}, and origin does not have it either (origin answered, "
            + $"but fetching that commit failed: {fetch.Result.Diagnostic}). Push the commit, or check the SHA.");
    }

    /// <summary>
    /// The fetch writes refs and objects, so it runs under the repository mutation lease like every
    /// other repository mutation. A busy lease is polled, never waited on past the network deadline.
    /// </summary>
    private async Task<RepositoryLease> AcquireLeaseAsync(
        string repoPath, string startRef, System.Diagnostics.Stopwatch network, CancellationToken ct)
    {
        var owner = new RepositoryLeaseOwnerTag(null, RepositoryLeasePurposes.StartRefFetch); // No task row exists yet.
        while (true)
        {
            if (await leases.TryAcquireAsync(repoPath, owner, ct) is { } lease)
                return lease;
            var left = _fetchTimeout - network.Elapsed;
            if (left <= TimeSpan.Zero)
                break;
            await Task.Delay(left < LeasePollInterval ? left : LeasePollInterval, ct);
        }

        var fence = await leases.DescribeUnavailableAsync(repoPath, ct);
        throw new ServiceUnavailableException(
            $"Start ref '{startRef}' is not in {repoPath}, and another operation held the repository mutation lease "
            + $"for the whole {_fetchTimeout.TotalSeconds:0}s budget, so it was not fetched"
            + (fence is null ? "" : $" ({fence})") + ". The task was not created; retry.", RepositoryBusyCode);
    }

    private async Task<bool> ResolvesAsync(string repoPath, string revision, CancellationToken ct)
    {
        var result = await RunBoundedAsync(repoPath, ["rev-parse", "--verify", "--quiet", revision], LocalGitTimeout, ct);
        return !result.TimedOut && result.Result!.Succeeded;
    }

    private async Task<(LandingGitResult? Result, bool TimedOut)> RunBoundedAsync(
        string repoPath, IReadOnlyList<string> arguments, TimeSpan budget, CancellationToken ct)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(budget > TimeSpan.Zero ? budget : TimeSpan.Zero); // A spent deadline runs nothing.
        try
        {
            bounded.Token.ThrowIfCancellationRequested();
            return (await git.RunAsync(repoPath, arguments, bounded.Token), false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException && !ct.IsCancellationRequested)
        {
            // The owned-child path kills the process tree and clears its journal before this surfaces.
            logger.LogWarning("git {Arguments} in {RepoPath} exceeded {Seconds}s at create",
                string.Join(" ", arguments), repoPath, budget.TotalSeconds);
            return (null, true);
        }
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Refuse(string code, string message) =>
        throw new ValidationException(Field, message, code, message);

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Unavailable(string code, string message) =>
        throw new ServiceUnavailableException(message, code);
}
