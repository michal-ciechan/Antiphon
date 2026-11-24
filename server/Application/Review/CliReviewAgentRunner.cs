using System.Collections.Concurrent;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Review;

/// <summary>
/// The reviewer agents (design 4.7) as one-shot <c>claude -p</c> calls on this server
/// (<c>Review:Agents:Transport</c> = <c>cli</c>), for an instance with no session runner. A first call
/// starts the reviewer in the background under a <c>cli:&lt;guid&gt;</c> handle and returns
/// <see cref="ReviewAgentStatus.Pending"/> at once; a poll with the handle stays Pending until it finishes,
/// then completes with the answer and its usage, or throws <see cref="ReviewAgentRunnerException"/> with
/// the reason. The run checks the head out as a detached worktree of the <c>Review:Agents:Repos</c> row's
/// <c>LocalPath</c> (a local-git subject's own path when there is no row, a GitHub repo's bare mirror
/// under <c>Review:Agents:MirrorRoot</c> when that is set), or runs in an empty folder with the pack
/// alone and says so. <c>claude</c> gets Read, Grep and Glob only, the group tier's model
/// (<c>Review:Agents:Cli:Models</c>), the brief with the pack always inline, and
/// <c>Review:Agents:TimeoutMinutes</c>. At most <c>Review:Agents:Cli:MaxParallel</c> run at once. Runs
/// live in memory: a handle from before a restart is reported lost, and Refresh reruns the review.
/// </summary>
public sealed class CliReviewAgentRunner : IReviewAgentRunner, IDisposable
{
    public const string HandlePrefix = "cli:";
    public const string Provider = "claude-cli";

    /// <summary>The reviewer's tools: read the checkout, nothing else.</summary>
    public static readonly IReadOnlyList<string> ReviewerTools = ["Read", "Grep", "Glob"];

    /// <summary>A finished run is kept this long for its poll, then forgotten.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(6);

    private static readonly TimeSpan CleanupBudget = TimeSpan.FromMinutes(1);

    private readonly IClaudeCli _cli;
    private readonly IReviewCheckout _checkout;
    private readonly IOptions<ReviewAgentSettings> _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<CliReviewAgentRunner> _logger;
    private readonly SemaphoreSlim _slots;
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<string, Run> _runs = new(StringComparer.Ordinal);
    private readonly string _root;

    public CliReviewAgentRunner(IClaudeCli cli, IReviewCheckout checkout, IOptions<ReviewAgentSettings> settings, TimeProvider time,
        ILogger<CliReviewAgentRunner> logger, string? workRoot = null)
    {
        _cli = cli;
        _checkout = checkout;
        _settings = settings;
        _time = time;
        _logger = logger;
        _slots = new SemaphoreSlim(Math.Max(1, settings.Value.Cli.MaxParallel));
        _root = string.IsNullOrWhiteSpace(workRoot) ? Path.Combine(Path.GetTempPath(), "antiphon-review-cli") : workRoot;
    }

    public Task<ReviewAgentResult> RunAsync(ReviewAgentRequest request, CancellationToken ct)
    {
        Forget();
        if (request.Handle is { Length: > 0 } handle)
            return Task.FromResult(Poll(request, handle));

        var id = Guid.NewGuid().ToString("N");
        var run = new Run();
        var key = HandlePrefix + id;
        _runs[key] = run;
        run.Work = Task.Run(() => ExecuteAsync(request, id, run, _stopping.Token), CancellationToken.None);
        _logger.LogInformation("Review run {RunId} reviewer {Group}: started claude -p ({Model}) as {Handle} at {HeadSha}",
            request.RunId, request.GroupKey, _settings.Value.Cli.ModelFor(request.Tier), key, request.HeadSha);
        return Task.FromResult(ReviewAgentResult.Pending(key));
    }

    /// <summary>The run behind <paramref name="handle"/>, for tests that wait on it; null when unknown.</summary>
    public Task? Completion(string handle) => _runs.TryGetValue(handle, out var run) ? run.Work : null;

    /// <summary>Stops every running reviewer (their process trees are killed). The source is not disposed: runs still read its token.</summary>
    public void Dispose() => _stopping.Cancel();

    private ReviewAgentResult Poll(ReviewAgentRequest request, string handle)
    {
        if (!_runs.TryGetValue(handle, out var run))
        {
            throw new ReviewAgentRunnerException(
                $"Reviewer run {handle} for group {request.GroupKey} is not known to this server: it was lost (most likely in a server restart, " +
                "or the reviewer transport changed). Refresh the run to rerun the review.");
        }

        lock (run)
        {
            if (run.FinishedAt is null)
                return ReviewAgentResult.Pending(handle);
            if (run.Error is { } error)
                throw new ReviewAgentRunnerException(error);
            return ReviewAgentResult.Completed(run.Text!, run.Usage);
        }
    }

    private async Task ExecuteAsync(ReviewAgentRequest request, string id, Run run, CancellationToken stopping)
    {
        var options = _settings.Value;
        var model = options.Cli.ModelFor(request.Tier);
        var timeout = TimeSpan.FromMinutes(Math.Max(1, options.TimeoutMinutes));
        var acquired = false;
        try
        {
            await _slots.WaitAsync(stopping);
            acquired = true;
            var directory = Path.Combine(_root, id);
            var repoPath = RepoPath(request, options);
            var remote = request.Host == ReviewHost.GitHub
                ? new ReviewCheckoutRemote(request.Repo, CreateMirror: repoPath is not null && repoPath == MirrorPath(options.MirrorRoot, request.Repo))
                : null;
            if (repoPath is not null)
                await _checkout.AddAsync(repoPath, remote, request.Host == ReviewHost.GitHub ? request.PrNumber : null, request.HeadSha, directory, stopping);
            else
                Directory.CreateDirectory(directory);

            try
            {
                var prompt = DelegationReviewAgentRunner.Brief(request, options, inlineLimit: int.MaxValue, hasCheckout: repoPath is not null);
                var answer = await _cli.RunAsync(new ClaudeCliRequest(prompt, model, ReviewerTools, directory, timeout), stopping);
                Finish(run, answer.Text, new ReviewAgentUsage(Provider, answer.Model, ReviewTransport.OneShot, answer.InputTokens, answer.OutputTokens,
                    answer.CostUsd, "api-equivalent"), null);
                _logger.LogInformation("Review run {RunId} reviewer {Group}: claude -p {Model} answered ({InputTokens} in, {OutputTokens} out, ${Cost})",
                    request.RunId, request.GroupKey, answer.Model, answer.InputTokens, answer.OutputTokens, answer.CostUsd);
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(CleanupBudget);
                if (repoPath is not null)
                    await _checkout.RemoveAsync(repoPath, directory, cleanup.Token);
                TryDelete(directory);
            }
        }
        catch (ClaudeCliException ex) when (ex.TimedOut)
        {
            Fail(run, request, $"Reviewer {request.GroupKey} (claude -p {model}) did not finish within {timeout.TotalMinutes:0} minutes " +
                "(Review:Agents:TimeoutMinutes); it was stopped.");
        }
        catch (ClaudeCliException ex)
        {
            Fail(run, request, $"Reviewer {request.GroupKey} (claude -p {model}) failed: {ex.Message}");
        }
        catch (ReviewAgentRunnerException ex)
        {
            Fail(run, request, ex.Message);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            Fail(run, request, $"Reviewer {request.GroupKey} was stopped because the server is shutting down; Refresh the run to rerun it.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Review run {RunId} reviewer {Group}: the one-shot reviewer could not run", request.RunId, request.GroupKey);
            Fail(run, request, $"Reviewer {request.GroupKey} (claude -p {model}) could not run: {ex.Message}");
        }
        finally
        {
            if (acquired)
                _slots.Release();
        }
    }

    /// <summary>
    /// The checkout to cut the worktree from: the repo row's <c>LocalPath</c>, else a local-git subject's own
    /// path, else a GitHub repo's mirror under <c>MirrorRoot</c>, else none.
    /// </summary>
    public static string? RepoPath(ReviewAgentRequest request, ReviewAgentSettings options) =>
        options.FindRepo(request.Repo)?.LocalPath is { Length: > 0 } configured ? configured
        : request.Host == ReviewHost.Local && !string.IsNullOrWhiteSpace(request.LocalPath) ? request.LocalPath
        : request.Host == ReviewHost.GitHub ? MirrorPath(options.MirrorRoot, request.Repo)
        : null;

    /// <summary>
    /// <c>{root}/{owner}/{name}.git</c>, lower-cased (GitHub names are case-insensitive); null without a root
    /// or for a repo that is not a plain <c>owner/name</c> of letters, digits, <c>-</c>, <c>_</c> and <c>.</c>.
    /// </summary>
    public static string? MirrorPath(string? root, string repo)
    {
        if (string.IsNullOrWhiteSpace(root))
            return null;
        var parts = repo.Trim().ToLowerInvariant().Split('/');
        if (parts.Length != 2 || parts.Any(p => p.Length == 0 || p is "." or ".." || !p.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')))
            return null;
        return Path.Combine(root.Trim(), parts[0], parts[1] + ".git");
    }

    private void Finish(Run run, string? text, ReviewAgentUsage? usage, string? error)
    {
        lock (run)
        {
            if (error is null && string.IsNullOrWhiteSpace(text))
                error = "The reviewer answered nothing.";
            run.Text = text;
            run.Usage = usage;
            run.Error = error;
            run.FinishedAt = _time.GetUtcNow();
        }
    }

    private void Fail(Run run, ReviewAgentRequest request, string error)
    {
        _logger.LogWarning("Review run {RunId} reviewer {Group}: {Error}", request.RunId, request.GroupKey, error);
        Finish(run, null, null, error);
    }

    /// <summary>Drops finished runs older than <see cref="Retention"/>.</summary>
    private void Forget()
    {
        var cutoff = _time.GetUtcNow() - Retention;
        foreach (var (key, run) in _runs)
        {
            if (run.FinishedAt is { } finished && finished < cutoff)
                _runs.TryRemove(key, out _);
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class Run
    {
        public Task? Work { get; set; }
        public string? Text { get; set; }
        public ReviewAgentUsage? Usage { get; set; }
        public string? Error { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
    }
}
