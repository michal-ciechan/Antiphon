using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Review;

/// <summary>
/// Code host polling (design 8.5, <c>Review:Polling</c>): there is no public webhook at home, so a
/// pass every interval turns code host state into review work. Per configured repo it reads the open
/// PRs and the PR conversation comments since its cursor (conditional GETs, so an unchanged listing is
/// a free 304), then:
/// <list type="number">
/// <item>acts on comment commands from <c>OWNER</c>, <c>MEMBER</c> or <c>COLLABORATOR</c>:
/// <c>/antiphon review [steer]</c> starts a run when the PR has none (or only a finished one) and
/// otherwise asks for a round with that steer (<c>ReviewService.RequestRoundAsync</c>);
/// <c>/antiphon recheck</c> is the same with no steer. Each gets an <c>eyes</c> reaction and a short
/// reply, both best effort;</item>
/// <item>starts a run for every open PR that matches the repo's filters and has no run;</item>
/// <item>refreshes a run whose PR head differs from the run's, or whose PR is no longer open. An
/// unchanged run is left alone, so a pass publishes nothing when nothing moved.</item>
/// </list>
/// A command is never acted on twice: its comment id is the new iteration's trigger
/// (<c>comment:&lt;id&gt;</c>) or in the completed wait's summary, and a refusal's reply carries the same
/// marker, all of which survive a restart; on start the poller looks back only two intervals. One
/// repo's failure is logged and recorded in its pass result and never stops the others or the loop.
/// The last pass is kept in memory (<see cref="ReviewPollingState"/>).
/// </summary>
public sealed class ReviewPoller(
    IServiceScopeFactory scopes,
    ReviewPollingState state,
    IOptions<ReviewPollingSettings> options,
    TimeProvider time,
    ILogger<ReviewPoller> logger) : BackgroundService
{
    /// <summary>The <c>author_association</c> values whose commands are acted on.</summary>
    public static readonly IReadOnlySet<string> TrustedAssociations =
        new HashSet<string>(["OWNER", "MEMBER", "COLLABORATOR"], StringComparer.OrdinalIgnoreCase);

    /// <summary>The actor of runs the poller starts and refreshes on its own.</summary>
    public const string PollerActor = "antiphon";

    private const int MaxRememberedComments = 5000;

    private readonly DateTime _startedAt = time.GetUtcNow().UtcDateTime;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, DateTime> _cursors = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Queue<string> _seenOrder = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Review polling is off (Review:Polling:Enabled); POST /api/review-polling/poll still runs a pass.");
            return;
        }

        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync("timer", stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "A review polling pass failed; the next interval retries.");
            }

            try
            {
                await Task.Delay(options.Value.Interval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// One pass over every configured repo; waits for a pass already running. A cancelled pass is
    /// recorded with the repos it reached. Returns the pass and keeps it in <see cref="ReviewPollingState"/>.
    /// </summary>
    public async Task<ReviewPollingPassDto> PollOnceAsync(string trigger, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        state.SetRunning(true);
        try
        {
            var settings = options.Value;
            var started = Now();
            var results = new List<ReviewPollingRepoPassDto>();
            foreach (var repo in settings.Repos.Where(r => !string.IsNullOrWhiteSpace(r.Repo)))
            {
                if (ct.IsCancellationRequested)
                {
                    results.Add(new ReviewPollingRepoPassDto(repo.Repo.Trim(), 0, 0, 0, 0, "The pass was cancelled before it reached this repo."));
                    continue;
                }

                results.Add(await PollRepoAsync(repo, settings, ct));
            }

            var pass = new ReviewPollingPassDto(trigger, started, Now(), results.All(r => r.Error is null), results);
            state.Record(pass);
            return pass;
        }
        finally
        {
            state.SetRunning(false);
            _gate.Release();
        }
    }

    private async Task<ReviewPollingRepoPassDto> PollRepoAsync(ReviewPollingRepo repo, ReviewPollingSettings settings, CancellationToken ct)
    {
        var name = repo.Repo.Trim();
        var tally = new Tally(name);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var binding = scope.ServiceProvider.GetRequiredService<CodeHostRouter>().Resolve(repo.Host, name, null, null, null);
            if (!binding.Host.Capabilities.CanListPullRequests)
                throw new CodeHostException(CodeHostFailureKind.Unsupported, $"The {repo.Host} code host cannot list pull requests, so {name} cannot be polled.");

            var open = (await binding.Host.ListOpenPullRequestsAsync(binding.Target, ct)).GroupBy(p => p.Number).ToDictionary(g => g.Key, g => g.First());
            tally.Open = open.Count;
            await HandleCommentsAsync(db, binding, repo, name, open, settings, tally, ct);

            var existing = await LatestRunsAsync(db, repo.Host, name, ct);
            foreach (var pr in open.Values.Where(p => !existing.ContainsKey(p.Number) && Matches(repo, p)).OrderBy(p => p.Number).ToList())
            {
                await ActAsync(tally, $"#{pr.Number}", async () =>
                {
                    await WithServiceAsync(PollerActor, s => s.CreateRunAsync(new CreateReviewRunRequest(repo.Host, name, pr.Number), "opened", null, ct));
                    tally.Started++;
                }, ct);
            }

            // A queued run is the executor's next work, and its intake pins the current head; refreshing
            // it again before then would start a second iteration for the same push. A running run is
            // mid-walk: its own branch update may have just moved the head that the walk has not saved
            // yet (live, 2026-11-23: the bot's base-only rebase was refreshed as a push and started a
            // new round). The next pass sees the settled run.
            var current = await LatestRunsAsync(db, repo.Host, name, ct);
            foreach (var run in current.Values.Where(r => r.Walked
                         && r.Status is not (ReviewRunStatus.Closed or ReviewRunStatus.Merged or ReviewRunStatus.Queued or ReviewRunStatus.Running)))
            {
                var due = !open.TryGetValue(run.PrNumber, out var listed) || !string.Equals(listed.HeadSha, run.HeadSha, StringComparison.OrdinalIgnoreCase);
                if (!due)
                    continue;
                await ActAsync(tally, $"#{run.PrNumber}", async () =>
                {
                    await WithServiceAsync(PollerActor, async s =>
                    {
                        await s.RefreshAsync(run.Id, ct);
                        return true;
                    });
                    tally.Refreshed++;
                }, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            tally.Error = "The pass was cancelled before it finished this repo.";
        }
        catch (Exception ex)
        {
            tally.Error = Describe(ex);
            logger.LogWarning(ex, "Polling {Repo} failed: {Error}", name, tally.Error);
        }

        return tally.ToDto();
    }

    /// <summary>The comment commands since the repo's cursor; see the class summary for the rules.</summary>
    private async Task HandleCommentsAsync(AppDbContext db, CodeHostBinding binding, ReviewPollingRepo repo, string name,
        IReadOnlyDictionary<int, CodeHostOpenPullRequest> open, ReviewPollingSettings settings, Tally tally, CancellationToken ct)
    {
        var floor = _startedAt - 2 * settings.Interval;
        var key = $"{repo.Host}|{name}";
        DateTime since;
        lock (_cursors)
            since = _cursors.TryGetValue(key, out var cursor) ? cursor : floor;
        var comments = await binding.Host.ListConversationCommentsAsync(binding.Target, since, ct);
        var answered = comments.SelectMany(c => ReviewCommand.AnsweredIds(c.Body)).ToHashSet(StringComparer.Ordinal);

        // The cursor is the newest comment seen (the listing's since is inclusive), held back to the
        // oldest command that failed for a transient reason so the next pass retries it.
        var next = comments.Count > 0 ? comments.Max(c => c.CreatedAt) : since;
        DateTime? retryFrom = null;
        foreach (var comment in comments.OrderBy(c => c.CreatedAt))
        {
            var seenKey = $"{key}|{comment.Id}";
            if (comment.CreatedAt < floor || Seen(seenKey) || ReviewCommand.Parse(comment.Body, settings.Prefix) is not { } command)
                continue;
            var marker = ReviewCommand.Marker(comment.Id);
            if (!TrustedAssociations.Contains(comment.AuthorAssociation))
            {
                logger.LogInformation("Ignored {Verb} on {Repo}#{Pr} from {Login} ({Association}).", command.Verb, name, comment.PrNumber,
                    comment.AuthorLogin, comment.AuthorAssociation);
                Remember(seenKey);
                continue;
            }

            if (answered.Contains(comment.Id) || await HandledAsync(db, marker, ct))
            {
                Remember(seenKey);
                continue;
            }

            try
            {
                await ActOnCommandAsync(db, binding, repo, name, open, comment, command, marker, tally, ct);
                tally.Commands++;
                Remember(seenKey);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                tally.Error = $"comment {comment.Id} on #{comment.PrNumber}: {Describe(ex)}";
                logger.LogWarning(ex, "Polling {Repo}: comment {CommentId} on #{Pr} failed; the next pass retries it.", name, comment.Id, comment.PrNumber);
                retryFrom ??= comment.CreatedAt;
            }
        }

        var cursorAt = retryFrom is { } retry && retry < next ? retry : next;
        lock (_cursors)
            _cursors[key] = cursorAt > since ? cursorAt : since;
    }

    /// <summary>
    /// Starts a run or asks for a round, then replies. A refusal (finished, at a gate, busy) is the
    /// reply; a code host or other failure throws so the comment is retried.
    /// </summary>
    private async Task ActOnCommandAsync(AppDbContext db, CodeHostBinding binding, ReviewPollingRepo repo, string name,
        IReadOnlyDictionary<int, CodeHostOpenPullRequest> open, CodeHostConversationComment comment, ReviewCommand command, string marker, Tally tally,
        CancellationToken ct)
    {
        var target = binding.Target with { PrNumber = comment.PrNumber };
        var conversation = binding.Host.Capabilities.CanPostConversation;
        if (conversation)
            await BestEffortAsync(() => binding.Host.AddCommentReactionAsync(target, comment.Id, "eyes", ct), name, comment.Id);

        string reply;
        if (!open.ContainsKey(comment.PrNumber))
        {
            reply = "This PR is not open, so Antiphon will not review it.";
        }
        else
        {
            var latest = (await LatestRunsAsync(db, repo.Host, name, ct)).GetValueOrDefault(comment.PrNumber);
            try
            {
                if (latest is null || latest.Status is ReviewRunStatus.Closed or ReviewRunStatus.Merged)
                {
                    var run = await WithServiceAsync(comment.AuthorLogin, s =>
                        s.CreateRunAsync(new CreateReviewRunRequest(repo.Host, name, comment.PrNumber), marker, command.Steer, ct));
                    tally.Started++;
                    reply = $"Review started, round {run.Round} queued{SteerText(command.Steer)}.";
                }
                else
                {
                    var runId = latest.Id;
                    var run = await WithServiceAsync(comment.AuthorLogin, s => s.RequestRoundAsync(runId, command.Steer, comment.AuthorLogin, marker, ct));
                    reply = $"Round {run.Round} queued{SteerText(command.Steer)}.";
                }
            }
            catch (HttpException ex) when (ex.StatusCode is 404 or 409 or 422)
            {
                reply = $"Not queued: {ex.Message}";
            }
        }

        if (conversation)
            await BestEffortAsync(() => binding.Host.PostConversationCommentAsync(target, $"{reply}\n\n{ReviewCommand.AnswerTag(comment.Id)}", ct), name, comment.Id);
    }

    /// <summary>True when an iteration's trigger is the marker, or a wait completed for it names it.</summary>
    private static async Task<bool> HandledAsync(AppDbContext db, string marker, CancellationToken ct)
    {
        var token = $"({marker})";
        return await db.ReviewIterations.AnyAsync(i => i.Trigger == marker, ct)
            || await db.ReviewNodeExecutions.AnyAsync(n => n.Kind == ReviewNodeKind.Wait && n.Summary != null && n.Summary.Contains(token), ct);
    }

    private sealed record RunHead(Guid Id, int PrNumber, ReviewRunStatus Status, string HeadSha, DateTime StartedAt, bool Walked);

    /// <summary>The newest run of each PR in the repo.</summary>
    private static async Task<Dictionary<int, RunHead>> LatestRunsAsync(AppDbContext db, ReviewHost host, string repo, CancellationToken ct)
    {
        var lower = repo.ToLowerInvariant();
        var rows = await db.ReviewRuns.AsNoTracking()
            .Where(r => r.Subject.Host == host && r.Subject.Repo.ToLower() == lower && r.Subject.PrNumber != null)
            .Select(r => new RunHead(r.Id, r.Subject.PrNumber!.Value, r.Status, r.Subject.HeadSha, r.StartedAt, r.Iterations.Any(i => i.HeadSha != null)))
            .ToListAsync(ct);
        return rows.GroupBy(r => r.PrNumber).ToDictionary(g => g.Key, g => g.MaxBy(r => r.StartedAt)!);
    }

    /// <summary>The repo's filters for an automatic run: drafts, base branches, skip labels and the required label.</summary>
    public static bool Matches(ReviewPollingRepo repo, CodeHostOpenPullRequest pr)
    {
        if (pr.IsDraft && !repo.IncludeDrafts)
            return false;
        if (repo.BaseBranches.Count > 0 && !repo.BaseBranches.Contains(pr.BaseRef, StringComparer.OrdinalIgnoreCase))
            return false;
        if (pr.Labels.Any(l => repo.EffectiveSkipLabels.Contains(l, StringComparer.OrdinalIgnoreCase)))
            return false;
        return string.IsNullOrWhiteSpace(repo.RequireLabel) || pr.Labels.Contains(repo.RequireLabel.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A review service in its own scope, acting as <paramref name="actor"/>: a background scope has no
    /// request to resolve the viewer from, and a failed save never leaks into the next action.
    /// </summary>
    private async Task<T> WithServiceAsync<T>(string actor, Func<ReviewService, Task<T>> action)
    {
        await using var scope = scopes.CreateAsyncScope();
        var service = ActivatorUtilities.CreateInstance<ReviewService>(scope.ServiceProvider, new ReviewViewer(actor, null, []));
        return await action(service);
    }

    /// <summary>One PR's action; its failure is recorded and the pass goes on to the next PR.</summary>
    private async Task ActAsync(Tally tally, string what, Func<Task> action, CancellationToken ct)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            tally.Error = $"{what}: {Describe(ex)}";
            logger.LogWarning(ex, "Polling {Repo} {What} failed: {Error}", tally.Repo, what, tally.Error);
        }
    }

    private async Task BestEffortAsync(Func<Task> write, string repo, string commentId)
    {
        try
        {
            await write();
        }
        catch (CodeHostException ex)
        {
            logger.LogInformation("Polling {Repo}: a reply or reaction for comment {CommentId} was not posted: {Error}", repo, commentId, ex.Message);
        }
    }

    private bool Seen(string key)
    {
        lock (_seen)
            return _seen.Contains(key);
    }

    private void Remember(string key)
    {
        lock (_seen)
        {
            if (!_seen.Add(key))
                return;
            _seenOrder.Enqueue(key);
            while (_seenOrder.Count > MaxRememberedComments)
                _seen.Remove(_seenOrder.Dequeue());
        }
    }

    private static string SteerText(string? steer) =>
        steer is null ? "" : $", steer: {(steer.Length <= 200 ? steer : steer[..199] + "…").ReplaceLineEndings(" ")}";

    private static string Describe(Exception ex) => ex switch
    {
        CodeHostException host => $"{host.Kind}: {host.Message}",
        HttpException http => http.Code is { } code ? $"{code}: {http.Message}" : http.Message,
        _ => ex.Message,
    };

    private DateTime Now() => time.GetUtcNow().UtcDateTime;

    private sealed class Tally(string repo)
    {
        public string Repo { get; } = repo;
        public int Open { get; set; }
        public int Started { get; set; }
        public int Refreshed { get; set; }
        public int Commands { get; set; }
        public string? Error { get; set; }

        public ReviewPollingRepoPassDto ToDto() => new(Repo, Open, Started, Refreshed, Commands, Error);
    }
}

/// <summary>The last polling pass and whether one is running, in memory (design 8.5).</summary>
public sealed class ReviewPollingState
{
    private ReviewPollingPassDto? _last;
    private int _running;

    public ReviewPollingPassDto? LastPass => Volatile.Read(ref _last);

    public bool Running => Volatile.Read(ref _running) == 1;

    public void Record(ReviewPollingPassDto pass) => Volatile.Write(ref _last, pass);

    public void SetRunning(bool running) => Volatile.Write(ref _running, running ? 1 : 0);

    public ReviewPollingDto ToDto(ReviewPollingSettings settings) => new(
        settings.Enabled,
        (int)settings.Interval.TotalSeconds,
        settings.Prefix,
        settings.Repos.Where(r => !string.IsNullOrWhiteSpace(r.Repo))
            .Select(r => new ReviewPollingRepoDto(r.Repo.Trim(), r.Host, r.BaseBranches, r.IncludeDrafts, r.EffectiveSkipLabels, r.RequireLabel))
            .ToList(),
        Running,
        LastPass);
}

/// <summary>
/// A PR comment command: <c>&lt;prefix&gt; review [steer]</c> or <c>&lt;prefix&gt; recheck</c>, at the start of
/// the comment (a quoted command does not count). The steer is the rest of the comment.
/// </summary>
public sealed record ReviewCommand(string Verb, string? Steer)
{
    public const string Review = "review";
    public const string Recheck = "recheck";

    private const string AnswerPrefix = "<!-- antiphon:comment:";

    public static ReviewCommand? Parse(string body, string prefix)
    {
        var text = body.TrimStart();
        if (prefix.Length == 0 || !text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;
        var rest = text[prefix.Length..];
        if (rest.Length == 0 || !char.IsWhiteSpace(rest[0]))
            return null;
        rest = rest.TrimStart();
        var end = 0;
        while (end < rest.Length && !char.IsWhiteSpace(rest[end]))
            end++;
        var verb = rest[..end].ToLowerInvariant();
        var steer = rest[end..].Trim();
        if (steer.Length > ReviewService.MaxSteerPromptLength)
            steer = steer[..ReviewService.MaxSteerPromptLength];
        return verb switch
        {
            Review => new ReviewCommand(Review, steer.Length == 0 ? null : steer),
            Recheck => new ReviewCommand(Recheck, null),
            _ => null,
        };
    }

    /// <summary><c>comment:&lt;id&gt;</c>: the trigger, or the wait summary token, that records the command.</summary>
    public static string Marker(string commentId) => $"comment:{commentId}";

    /// <summary>The hidden tag a reply carries, so a refused command is not answered again after a restart.</summary>
    public static string AnswerTag(string commentId) => $"{AnswerPrefix}{commentId} -->";

    /// <summary>The comment ids a reply's hidden tags answer.</summary>
    public static IEnumerable<string> AnsweredIds(string body)
    {
        var at = 0;
        while ((at = body.IndexOf(AnswerPrefix, at, StringComparison.Ordinal)) >= 0)
        {
            var start = at + AnswerPrefix.Length;
            var end = body.IndexOf(" -->", start, StringComparison.Ordinal);
            if (end < 0)
                yield break;
            yield return body[start..end];
            at = end;
        }
    }
}
