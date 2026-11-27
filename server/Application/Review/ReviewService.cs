using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Antiphon.Server.Application.Review;

/// <summary>
/// Review runs, findings, gates and the human queue (docs/features/012-review-engine/api.md). Every
/// mutation publishes <c>ReviewRunChanged</c> and records the <see cref="ReviewViewer"/> as the actor.
/// Responses are stored with sync state Pending; the executor's next pass posts them to the code
/// host (<see cref="ReviewFindingSync"/>). Refresh turns code-host state into workflow events, and a
/// gate decision on an executor-walked run hands it back to the executor.
/// </summary>
public sealed class ReviewService(
    AppDbContext db,
    IEventBus events,
    ReviewViewer viewer,
    IOptions<ReviewSettings> settings,
    TimeProvider time,
    ReviewWorkflowService workflows,
    ReviewIntake intake,
    ReviewExecutorSignal executor,
    CodeHostRouter router,
    IOptions<ReviewPolicySettings> policy)
{
    public const string RunChangedEvent = "ReviewRunChanged";
    private const string CloseAuditNode = "review-run-close";
    private const int MaxCloseReasonLength = 4000;

    /// <summary>Working minutes after which a queue item warns, and is breached (design 11.3).</summary>
    public const int SlaWarnWorkingMinutes = 240;

    public const int SlaBreachWorkingMinutes = 480;

    private static readonly ReviewFindingStatus[] OpenStatuses =
        [ReviewFindingStatus.Open, ReviewFindingStatus.WillFix, ReviewFindingStatus.Disputed];

    private static readonly ReviewRunStatus[] ActiveStatuses =
        [ReviewRunStatus.Queued, ReviewRunStatus.Running, ReviewRunStatus.AwaitingFixes, ReviewRunStatus.AtGate];

    private static readonly ReviewFindingStatus[] AcceptedStatuses =
        [ReviewFindingStatus.Accepted, ReviewFindingStatus.WillFix, ReviewFindingStatus.Fixed];

    private static readonly JsonSerializerOptions GraphJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public ReviewViewerDto GetViewer() => viewer.ToDto();

    // ---- Runs ----

    public async Task<ReviewListDto<ReviewRunSummaryDto>> ListRunsAsync(
        ReviewRunStatus? status, string? repo, ReviewStage? stage, string? q, CancellationToken ct)
    {
        var query = db.ReviewRuns.AsNoTracking().Include(r => r.Subject).AsQueryable();
        if (status is { } s)
            query = query.Where(r => r.Status == s);
        if (stage is { } st)
            query = query.Where(r => r.Stage == st);
        if (!string.IsNullOrWhiteSpace(repo))
        {
            var canonicalRepo = repo.Trim().ToLowerInvariant();
            query = query.Where(r => r.GitHubRepo == canonicalRepo || (r.GitHubRepo == null && r.Subject.Repo == repo));
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = "%" + q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            var number = int.TryParse(q.Trim().TrimStart('#'), out var n) ? n : (int?)null;
            query = query.Where(r => EF.Functions.ILike(r.Subject.Title, pattern) || EF.Functions.ILike(r.Subject.Repo, pattern)
                || EF.Functions.ILike(r.Subject.AuthorLogin, pattern) || (number != null && r.Subject.PrNumber == number));
        }

        var runs = await query.OrderByDescending(r => r.UpdatedAt).ToListAsync(ct);
        var summaries = await SummariesAsync(runs, ct);
        return new ReviewListDto<ReviewRunSummaryDto>(summaries, summaries.Count);
    }

    public async Task<ReviewListDto<ReviewRunSummaryDto>> GetSummariesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var runs = await db.ReviewRuns.AsNoTracking().Include(r => r.Subject)
            .Where(r => ids.Contains(r.Id)).OrderByDescending(r => r.UpdatedAt).ToListAsync(ct);
        var summaries = await SummariesAsync(runs, ct);
        return new ReviewListDto<ReviewRunSummaryDto>(summaries, summaries.Count);
    }

    public Task<ReviewRunDetailDto> GetRunAsync(Guid id, CancellationToken ct) =>
        GetRunAsync(id, existing: null, ct);

    private async Task<ReviewRunDetailDto> GetRunAsync(Guid id, bool? existing, CancellationToken ct)
    {
        var run = await db.ReviewRuns.AsNoTracking()
            .Include(r => r.Subject)
            .Include(r => r.Iterations)
            .Include(r => r.Nodes)
            .Include(r => r.Gates).ThenInclude(g => g.Passes)
            .AsSplitQuery()
            .SingleOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("Review run", id);
        var counts = await FindingCountsAsync([id], ct);
        var costs = await CostsAsync([id], ct);
        return ToDetail(run, counts.GetValueOrDefault(id), costs.GetValueOrDefault(id), existing);
    }

    /// <summary>
    /// Manual intake (design 4.1): resolves the subject through its code host, pins the base and head
    /// SHAs, records the intake node with the changed files and size bucket, and queues the run for
    /// the workflow executor. A local review takes <c>baseSha</c> and <c>headSha</c> as
    /// refs or commits (head defaults to <c>HEAD</c>).
    /// </summary>
    public Task<ReviewRunDetailDto> CreateRunAsync(CreateReviewRunRequest request, CancellationToken ct) =>
        CreateRunAsync(request, "manual", null, ct);

    /// <summary>
    /// Intake as above, with the first iteration's <paramref name="trigger"/> (<c>manual</c>,
    /// <c>opened</c> when polling found the PR, or <c>comment:&lt;id&gt;</c> for a PR comment command,
    /// which also dedupes the command) and an optional steer prompt for round 1.
    /// </summary>
    public async Task<ReviewRunDetailDto> CreateRunAsync(CreateReviewRunRequest request, string trigger, string? steerPrompt, CancellationToken ct)
    {
        var steer = Steer(steerPrompt);
        if (string.IsNullOrWhiteSpace(request.Repo))
            throw new ValidationException("repo", "A repo (owner/name) is required.");
        if (request.PrNumber is <= 0)
            throw new ValidationException("prNumber", "A PR number is positive.");
        if (request.Host == ReviewHost.Local && string.IsNullOrWhiteSpace(request.BaseSha))
            throw new ValidationException("baseSha", "A local review needs a base commit or ref.");

        var repo = request.Repo.Trim();
        var prNumber = request.PrNumber;
        var github = request.Host == ReviewHost.GitHub;
        if (github)
        {
            (repo, prNumber) = GitHubReviewIdentity.Parse(repo, prNumber);
            if (await FindOpenRunAsync(repo, prNumber, ct) is { } existingRun)
                return await GetRunAsync(existingRun, existing: true, ct);
        }

        var snapshot = await ReadIntakeAsync(
            () => intake.ReadAsync(request.Host, repo, prNumber, request.BaseSha?.Trim(), request.HeadSha?.Trim(), ct));
        var pr = snapshot.PullRequest;
        var now = Now();
        var local = request.Host == ReviewHost.Local;
        var subject = new ReviewSubject
        {
            Id = Guid.NewGuid(),
            Host = request.Host,
            Repo = repo,
            PrNumber = prNumber,
            Title = !string.IsNullOrWhiteSpace(request.Title) ? request.Title.Trim()
                : pr.Title.Length > 0 ? pr.Title
                : prNumber is { } number ? $"{repo} #{number}" : $"{repo} local review",
            AuthorLogin = local ? viewer.Login : pr.AuthorLogin,
            AuthorDisplayName = local ? viewer.DisplayName : pr.AuthorDisplayName,
            OpenedByLogin = local ? viewer.Login : pr.AuthorLogin,
            CreatedAt = now,
        };
        var run = new ReviewRun
        {
            Id = Guid.NewGuid(),
            SubjectId = subject.Id,
            Subject = subject,
            Status = ReviewRunStatus.Queued,
            Stage = ReviewStage.Intake,
            Round = 1,
            WaitingOn = ReviewWaitingOn.Bot,
            WorkflowName = workflows.Default.Name,
            WorkflowHash = workflows.DefaultHash,
            WorkflowRevisionId = await EnsureDefaultRevisionAsync(ct),
            SteerPrompt = steer,
            StartedAt = now,
            UpdatedAt = now,
        };
        var iteration = new ReviewIteration { Id = Guid.NewGuid(), RunId = run.Id, Iteration = 1, Round = 1, Trigger = trigger, StartedAt = now };
        var intakeNode = PendingIntake(run.Id, 1, 1);
        ReviewIntake.Apply(subject, iteration, intakeNode, snapshot, now);
        run.BaseTipSha = pr.BaseSha;
        run.Iterations.Add(iteration);
        run.Nodes.Add(intakeNode);
        while (true)
        {
            db.ReviewRuns.Add(run);
            try
            {
                await db.SaveChangesAsync(ct);
                break;
            }
            catch (DbUpdateException ex) when (github && IsOpenRunConflict(ex))
            {
                // SaveChanges rolled back the whole new graph, including its subject. If the
                // winner closed before this read, it is now legitimate to insert a new run.
                db.ChangeTracker.Clear();
                if (await FindOpenRunAsync(repo, prNumber, ct) is { } winner)
                    return await GetRunAsync(winner, existing: true, ct);
            }
        }
        await PublishAsync(run.Id, ct);
        executor.Wake();
        return await GetRunAsync(run.Id, existing: false, ct);
    }

    private Task<Guid?> FindOpenRunAsync(string repo, int? number, CancellationToken ct) =>
        db.ReviewRuns.Where(r => r.GitHubRepo == repo && r.GitHubPrNumber == number && r.Status != ReviewRunStatus.Closed)
            .Select(r => (Guid?)r.Id).SingleOrDefaultAsync(ct);

    internal static bool IsOpenRunConflict(DbUpdateException error) =>
        error.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: AppDbContext.OpenReviewRunIndex };

    /// <summary>Close the run and its outstanding work, with a compare-and-swap on the displayed version.</summary>
    public async Task<ReviewRunDetailDto> CloseAsync(Guid id, CloseReviewRunRequest request, CancellationToken ct)
    {
        var run = await db.ReviewRuns.AsNoTracking().Include(r => r.Subject).SingleOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("Review run", id);
        if (!viewer.MayDecideOn(run.GitHubRepo ?? run.Subject.Repo))
            throw new ForbiddenException($"You need write access to {run.Subject.Repo} to close its review runs.", "review_run_close_forbidden");
        if (run.Status == ReviewRunStatus.Closed)
            return await GetRunAsync(id, ct);

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > MaxCloseReasonLength)
            throw new ValidationException("reason", $"A close reason is required and may contain up to {MaxCloseReasonLength} characters.");
        if (request.ConcurrencyToken == 0)
            throw new ValidationException("concurrencyToken", "A review run concurrency token is required.");
        if (run.ConcurrencyToken != request.ConcurrencyToken)
            throw RunChanged(id);

        var now = Now();
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var outstanding = await db.ReviewNodeExecutions.Where(n => n.RunId == id
                && (n.Status == ReviewNodeStatus.Pending || n.Status == ReviewNodeStatus.Running || n.Status == ReviewNodeStatus.Waiting))
                .ToListAsync(ct);
            foreach (var node in outstanding)
            {
                node.Status = ReviewNodeStatus.Skipped;
                node.CompletedAt = now;
                node.Summary = string.IsNullOrEmpty(node.Summary) ? reason : $"{node.Summary}\nClosed: {reason}";
            }

            var gates = await db.ReviewGates.Where(g => g.RunId == id && g.Status != ReviewQueueItemStatus.Done).ToListAsync(ct);
            foreach (var gate in gates)
            {
                gate.Status = ReviewQueueItemStatus.Done;
                gate.DecidedAt = now;
                gate.Reasons = [.. gate.Reasons, $"closed: {reason}"];
            }

            // Iteration zero is run history, outside the executable review iterations.
            db.ReviewNodeExecutions.Add(new ReviewNodeExecution
            {
                Id = Guid.NewGuid(),
                RunId = id,
                NodeId = CloseAuditNode,
                Kind = ReviewNodeKind.Completion,
                Status = ReviewNodeStatus.Succeeded,
                Outcome = "close",
                CompletedAt = now,
                Summary = reason,
                OutputJson = JsonSerializer.Serialize(new ReviewRunClosureDto(viewer.Login, reason, now), GraphJson),
            });
            await db.SaveChangesAsync(ct);
            var updated = await db.ReviewRuns
                .Where(r => r.Id == id && r.ConcurrencyToken == request.ConcurrencyToken && r.Status != ReviewRunStatus.Closed)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReviewRunStatus.Closed)
                    .SetProperty(r => r.Stage, ReviewStage.Done).SetProperty(r => r.WaitingOn, (ReviewWaitingOn?)null)
                    .SetProperty(r => r.UpdatedAt, now), ct);
            if (updated == 0)
            {
                await transaction.RollbackAsync(ct);
                if (await db.ReviewRuns.AnyAsync(r => r.Id == id && r.Status == ReviewRunStatus.Closed, ct))
                    return await GetRunAsync(id, ct);
                throw RunChanged(id);
            }

            await transaction.CommitAsync(ct);
        }

        await PublishAsync(id, ct);
        return await GetRunAsync(id, ct);
    }

    private static ConflictException RunChanged(Guid id) =>
        new($"Review run {id} changed. Reload it and retry.", "review_run_changed");

    private async Task SaveRunAsync(Guid id, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex) when (ex.Entries.Any(entry => entry.Entity is ReviewRun))
        {
            throw RunChanged(id);
        }
    }

    /// <summary>
    /// The manual Refresh (design 8.5) for one run: re-reads the PR through its code host. A moved
    /// head starts a new iteration (trigger <c>push</c>, the next round); a failed run whose head has
    /// not moved is retried (trigger <c>recheck</c>, same round). An another-round iteration still
    /// waiting for intake is handed to the executor. A run never resolved through a code host (the
    /// demo data) or already finished is only re-announced.
    /// </summary>
    public async Task RefreshAsync(Guid id, CancellationToken ct)
    {
        var run = await db.ReviewRuns.Include(r => r.Subject).Include(r => r.Iterations).Include(r => r.Nodes)
            .AsSplitQuery().SingleOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("Review run", id);
        var current = run.Iterations.MaxBy(i => i.Iteration);
        if (current is null || run.Status is ReviewRunStatus.Closed or ReviewRunStatus.Merged || run.Iterations.All(i => i.HeadSha is null))
        {
            await PublishAsync(id, ct);
            return;
        }

        if (run.Nodes.Any(n => n.NodeId == ReviewIntake.NodeId && n.Iteration == current.Iteration && n.Status == ReviewNodeStatus.Pending))
        {
            executor.Wake();
            await PublishAsync(id, ct);
            return;
        }

        var snapshot = await ReadIntakeAsync(() => intake.ReadAsync(run.Subject, ct));
        var pr = snapshot.PullRequest;
        var now = Now();
        if (run.Subject.Host == ReviewHost.GitHub && pr.Title.Length > 0)
            run.Subject.Title = pr.Title;
        run.UpdatedAt = now;

        // Merged or closed on the code host: the run ends wherever it stands (design 8.5).
        if (pr.State is CodeHostPullRequestState.Merged or CodeHostPullRequestState.Closed)
        {
            var merged = pr.State == CodeHostPullRequestState.Merged;
            if (Parked(run, current) is { } parked)
                Complete(parked, merged ? "merge" : "close", merged ? "merged on the code host" : "closed on the code host", now);
            await SupersedeGatesAsync(run, merged ? "the PR was merged" : "the PR was closed", now, ct);
            ReviewWorkflowExecutor.Finish(run, merged ? ReviewRunStatus.Merged : ReviewRunStatus.Closed, now);
            await SaveRunAsync(id, ct);
            await PublishAsync(id, ct);
            return;
        }

        // The head the run accepts: the pinned head, moved forward by a base-only branch update.
        var previousHead = run.Subject.HeadSha.Length > 0 ? run.Subject.HeadSha : current.HeadSha ?? "";
        var moved = !string.Equals(pr.HeadSha, previousHead, StringComparison.OrdinalIgnoreCase);

        // A parked wait takes the event it listens for: a push, or a base move (design 3.1, 8.5).
        if (Parked(run, current) is { Kind: ReviewNodeKind.Wait } wait)
        {
            var awaits = workflows.Default.Node(wait.NodeId)?.ListParam("on") ?? [];
            string? evt = null;
            string? detail = null;
            if (moved && awaits.Contains("push"))
            {
                evt = "push";
                detail = $"head moved {ReviewIntake.Short(previousHead)} -> {ReviewIntake.Short(pr.HeadSha)}";
            }
            else if (!moved && awaits.Contains("base-moved"))
            {
                var state = await ReadIntakeAsync(() =>
                {
                    var binding = router.Resolve(run.Subject);
                    return binding.Host.GetBranchStateAsync(binding.Target, run.Subject.BaseRef, previousHead, ct);
                });
                if (!string.Equals(state.BaseTipSha, run.BaseTipSha, StringComparison.OrdinalIgnoreCase))
                {
                    evt = "base-moved";
                    detail = $"base moved {ReviewIntake.Short(run.BaseTipSha)} -> {ReviewIntake.Short(state.BaseTipSha)}";
                }
            }

            if (evt is not null)
            {
                Complete(wait, evt, detail!, now);
                run.Status = ReviewRunStatus.Queued;
                run.WaitingOn = ReviewWaitingOn.Bot;
                await SaveRunAsync(id, ct);
                await PublishAsync(id, ct);
                executor.Wake();
                return;
            }

            if (!moved)
            {
                await SaveRunAsync(id, ct);
                await PublishAsync(id, ct);
                return;
            }
        }

        var trigger = moved ? "push" : run.Status == ReviewRunStatus.Failed ? "recheck" : null;
        if (trigger is not null)
        {
            if (moved)
            {
                run.Round += 1;
                await SupersedeGatesAsync(run, $"a push moved the head to {ReviewIntake.Short(pr.HeadSha)}", now, ct);
            }

            var number = Math.Max(run.Nodes.Select(n => n.Iteration).DefaultIfEmpty(0).Max(), current.Iteration) + 1;
            var iteration = new ReviewIteration { Id = Guid.NewGuid(), RunId = run.Id, Iteration = number, Round = run.Round, Trigger = trigger, StartedAt = now };
            var intakeNode = PendingIntake(run.Id, number, NextSequence(run));
            ReviewIntake.Apply(run.Subject, iteration, intakeNode, snapshot, now,
                moved ? $"head moved {ReviewIntake.Short(previousHead)} -> {ReviewIntake.Short(pr.HeadSha)}" : "recheck after a failure");
            run.BaseTipSha = pr.BaseSha;
            // Added through the context: see Apply() on client-set Guid keys.
            db.ReviewIterations.Add(iteration);
            db.ReviewNodeExecutions.Add(intakeNode);
            run.Status = ReviewRunStatus.Queued;
            run.Stage = ReviewStage.Intake;
            run.WaitingOn = ReviewWaitingOn.Bot;
        }

        await SaveRunAsync(id, ct);
        await PublishAsync(id, ct);
        if (trigger is not null)
            executor.Wake();
    }

    /// <summary>The current iteration's parked execution: its latest top-level one, when it is Waiting.</summary>
    private static ReviewNodeExecution? Parked(ReviewRun run, ReviewIteration current) =>
        run.Nodes.Where(n => n.Iteration == current.Iteration && n.ItemKey == null).MaxBy(n => n.Sequence) is { Status: ReviewNodeStatus.Waiting } parked
            ? parked
            : null;

    private static void Complete(ReviewNodeExecution execution, string outcome, string summary, DateTime now)
    {
        execution.Status = ReviewNodeStatus.Succeeded;
        execution.Outcome = outcome;
        execution.CompletedAt = now;
        execution.Summary = $"{outcome}: {summary}";
    }

    /// <summary>Open gates of a run that moved on without them are closed with no decision, and their parked node skipped.</summary>
    private async Task SupersedeGatesAsync(ReviewRun run, string why, DateTime now, CancellationToken ct)
    {
        var open = await db.ReviewGates.Where(g => g.RunId == run.Id && g.Status != ReviewQueueItemStatus.Done).ToListAsync(ct);
        foreach (var gate in open)
        {
            gate.Status = ReviewQueueItemStatus.Done;
            gate.DecidedAt = now;
            if (!gate.Reasons.Contains($"superseded: {why}"))
                gate.Reasons = [.. gate.Reasons, $"superseded: {why}"];
            foreach (var parked in run.Nodes.Where(n => n.NodeId == gate.NodeId && n.Iteration == gate.Iteration && n.Status == ReviewNodeStatus.Waiting))
            {
                parked.Status = ReviewNodeStatus.Skipped;
                parked.CompletedAt = now;
                parked.Summary = $"superseded: {why}";
            }
        }
    }

    private static int NextSequence(ReviewRun run) => run.Nodes.Select(n => n.Sequence).DefaultIfEmpty(0).Max() + 1;

    /// <summary>The longest steer prompt a round request takes.</summary>
    public const int MaxSteerPromptLength = 4000;

    public Task<ReviewRunDetailDto> RequestRoundAsync(Guid runId, string? steerPrompt, string actor, CancellationToken ct) =>
        RequestRoundAsync(runId, steerPrompt, actor, null, ct);

    /// <summary>
    /// A manual review round (<c>POST /api/review-runs/{id}/rounds</c>, or a <c>/antiphon review</c>
    /// comment), same round, carrying the steer prompt. A run parked at a wait that listens for
    /// <c>recheck</c> (<c>await-fixes</c>) completes it with <c>recheck</c>. A run at a gate is refused:
    /// the gate's another-round decision is the way there. A finished, demo or busy run is refused too.
    /// Otherwise (approved, awaiting merge or failed) a new iteration starts like Refresh's recheck,
    /// superseding the parked wait and withdrawing the approval status. <paramref name="marker"/>
    /// (<c>comment:&lt;id&gt;</c>) is the new iteration's trigger, or goes in the completed wait's
    /// summary, so the poller never acts on the same comment twice.
    /// </summary>
    public async Task<ReviewRunDetailDto> RequestRoundAsync(Guid runId, string? steerPrompt, string actor, string? marker, CancellationToken ct)
    {
        var steer = Steer(steerPrompt);
        var run = await db.ReviewRuns.Include(r => r.Subject).Include(r => r.Iterations).Include(r => r.Nodes)
            .AsSplitQuery().SingleOrDefaultAsync(r => r.Id == runId, ct)
            ?? throw new NotFoundException("Review run", runId);
        var who = string.IsNullOrWhiteSpace(actor) ? viewer.Login : actor.Trim();
        if (run.Status is ReviewRunStatus.Closed or ReviewRunStatus.Merged)
            throw new ConflictException($"Review run {run.Id} is {run.Status}; it takes no more rounds.", "review_run_finished");
        var current = run.Iterations.MaxBy(i => i.Iteration);
        if (current is null || run.Iterations.All(i => i.HeadSha is null))
            throw new ConflictException($"Review run {run.Id} was never read from a code host (demo data); it takes no rounds.", "review_run_not_walked");

        var parked = Parked(run, current);
        if (run.Status == ReviewRunStatus.AtGate || parked is { Kind: ReviewNodeKind.Gate })
        {
            var gateNode = parked?.Kind == ReviewNodeKind.Gate ? parked.NodeId : "human";
            throw new ConflictException(
                $"Review run {run.Id} is waiting at its {gateNode} gate. Ask for another round with a steer prompt from the gate instead.",
                "review_run_at_gate");
        }

        var now = Now();
        var source = marker is null ? $"requested by {who}" : $"requested by {who} ({marker})";
        IReadOnlyList<string> awaits = parked is { Kind: ReviewNodeKind.Wait }
            ? workflows.Default.Node(parked.NodeId)?.ListParam("on") ?? []
            : [];
        if (parked is not null && awaits.Contains("recheck"))
        {
            // The executor follows the wait's recheck edge into a new iteration of the same round.
            Complete(parked, "recheck", source, now);
            run.SteerPrompt = steer;
            run.BotOnlyRounds = 0;
            run.Status = ReviewRunStatus.Queued;
            run.WaitingOn = ReviewWaitingOn.Bot;
            run.UpdatedAt = now;
            await SaveRunAsync(runId, ct);
            await PublishAsync(run.Id, ct);
            executor.Wake();
            return await GetRunAsync(run.Id, ct);
        }

        if (run.Status is ReviewRunStatus.Queued or ReviewRunStatus.Running)
            throw new ConflictException($"Review run {run.Id} is already reviewing round {run.Round}; ask again once it waits for fixes or a merge.",
                "review_run_busy");

        // Approved, awaiting merge or failed: a new iteration, as Refresh starts one for a recheck.
        var approved = run.Status is ReviewRunStatus.Approved or ReviewRunStatus.AwaitingMerge;
        var snapshot = await ReadIntakeAsync(() => intake.ReadAsync(run.Subject, ct));
        var pr = snapshot.PullRequest;
        if (pr.State is not CodeHostPullRequestState.Open)
            throw new ConflictException($"The PR is {pr.State} on the code host; Refresh finishes the run.", "review_run_finished");
        if (run.Subject.Host == ReviewHost.GitHub && pr.Title.Length > 0)
            run.Subject.Title = pr.Title;
        var previousHead = run.Subject.HeadSha.Length > 0 ? run.Subject.HeadSha : current.HeadSha ?? "";
        var moved = !string.Equals(pr.HeadSha, previousHead, StringComparison.OrdinalIgnoreCase);
        if (parked is not null)
        {
            parked.Status = ReviewNodeStatus.Skipped;
            parked.CompletedAt = now;
            parked.Summary = $"superseded: a review round was {source}";
        }

        await SupersedeGatesAsync(run, $"a review round was {source}", now, ct);
        if (moved)
            run.Round += 1;
        var trigger = marker ?? "recheck";
        var number = Math.Max(run.Nodes.Select(n => n.Iteration).DefaultIfEmpty(0).Max(), current.Iteration) + 1;
        var iteration = new ReviewIteration { Id = Guid.NewGuid(), RunId = run.Id, Iteration = number, Round = run.Round, Trigger = trigger, StartedAt = now };
        var intakeNode = PendingIntake(run.Id, number, NextSequence(run));
        ReviewIntake.Apply(run.Subject, iteration, intakeNode, snapshot, now,
            $"{trigger} {source}" + (moved ? $"; head moved {ReviewIntake.Short(previousHead)} -> {ReviewIntake.Short(pr.HeadSha)}" : ""));
        run.BaseTipSha = pr.BaseSha;
        // Added through the context: see Apply() on client-set Guid keys.
        db.ReviewIterations.Add(iteration);
        db.ReviewNodeExecutions.Add(intakeNode);
        run.SteerPrompt = steer;
        run.BotOnlyRounds = 0;
        run.Status = ReviewRunStatus.Queued;
        run.Stage = ReviewStage.Intake;
        run.WaitingOn = ReviewWaitingOn.Bot;
        run.UpdatedAt = now;
        await SaveRunAsync(runId, ct);
        if (approved)
            await WithdrawApprovalAsync(run, who, ct);
        await PublishAsync(run.Id, ct);
        executor.Wake();
        return await GetRunAsync(run.Id, ct);
    }

    /// <summary>
    /// A re-review revokes the approval, as a push does: <c>antiphon/approval</c> goes back to pending
    /// at the head. Best effort; the new round sets it again. A posted approving review stays on the
    /// code host (there is no dismissal yet).
    /// </summary>
    private async Task WithdrawApprovalAsync(ReviewRun run, string who, CancellationToken ct)
    {
        if (run.Subject.Host != ReviewHost.GitHub || run.Subject.HeadSha.Length == 0)
            return;
        try
        {
            var binding = router.Resolve(run.Subject);
            if (!binding.Host.Capabilities.SupportsCommitStatuses)
                return;
            var root = settings.Value.PublicBaseUrl;
            var link = string.IsNullOrWhiteSpace(root) ? null : $"{root.TrimEnd('/')}/reviews/{run.Id}";
            await binding.Host.SetCommitStatusAsync(binding.Target, run.Subject.HeadSha,
                new CodeHostCommitStatus(Execution.ReviewWalk.ApprovalStatusContext, CodeHostStatusState.Pending, $"Re-review requested by {who}", link), ct);
        }
        catch (CodeHostException)
        {
            // Best effort: the new round's statuses replace it.
        }
    }

    /// <summary>A steer prompt trimmed, or null when blank; longer than <see cref="MaxSteerPromptLength"/> is refused.</summary>
    private static string? Steer(string? steerPrompt)
    {
        if (string.IsNullOrWhiteSpace(steerPrompt))
            return null;
        var steer = steerPrompt.Trim();
        return steer.Length <= MaxSteerPromptLength
            ? steer
            : throw new ValidationException("steerPrompt", $"A steer prompt is at most {MaxSteerPromptLength} characters.");
    }

    /// <summary>A code host failure during a request becomes the API's error (404, 422 or 503).</summary>
    private static async Task<T> ReadIntakeAsync<T>(Func<Task<T>> read)
    {
        try
        {
            return await read();
        }
        catch (CodeHostException ex)
        {
            throw CodeHostRouter.ToHttp(ex);
        }
    }

    // ---- Workflow revisions ----

    /// <summary>The stored revision of the default workflow, created on first use (design 3.3).</summary>
    public async Task<Guid> EnsureDefaultRevisionAsync(CancellationToken ct)
    {
        var hash = workflows.DefaultHash;
        var existing = await db.ReviewWorkflowRevisions.Where(w => w.Hash == hash).Select(w => (Guid?)w.Id).SingleOrDefaultAsync(ct);
        if (existing is { } id)
            return id;
        var revision = new ReviewWorkflowRevision
        {
            Id = Guid.NewGuid(),
            Name = workflows.Default.Name,
            Hash = hash,
            GraphJson = JsonSerializer.Serialize(ReviewWorkflowService.ToDto(workflows.Default, revisionId: null), GraphJson),
            CreatedAt = Now(),
        };
        db.ReviewWorkflowRevisions.Add(revision);
        try
        {
            await db.SaveChangesAsync(ct);
            return revision.Id;
        }
        catch (DbUpdateException)
        {
            // A concurrent first use stored the same hash; use that row.
            db.Entry(revision).State = EntityState.Detached;
            return await db.ReviewWorkflowRevisions.Where(w => w.Hash == hash).Select(w => w.Id).SingleAsync(ct);
        }
    }

    public async Task<WorkflowGraphDto> GetWorkflowRevisionAsync(Guid id, CancellationToken ct)
    {
        var revision = await db.ReviewWorkflowRevisions.AsNoTracking().SingleOrDefaultAsync(w => w.Id == id, ct)
            ?? throw new NotFoundException("Review workflow revision", id);
        var graph = JsonSerializer.Deserialize<WorkflowGraphDto>(revision.GraphJson, GraphJson)
            ?? throw new InvalidOperationException($"Workflow revision {id} holds no graph.");
        return graph with { RevisionId = revision.Id, Hash = revision.Hash };
    }

    // ---- Findings ----

    public async Task<ReviewListDto<ReviewFindingDto>> ListFindingsAsync(Guid runId, ReviewFindingStatus? status, CancellationToken ct)
    {
        if (!await db.ReviewRuns.AnyAsync(r => r.Id == runId, ct))
            throw new NotFoundException("Review run", runId);
        var query = db.ReviewFindings.AsNoTracking().Include(f => f.Events).Where(f => f.RunId == runId);
        if (status is { } s)
            query = query.Where(f => f.Status == s);
        var findings = await query.ToListAsync(ct);
        var items = findings.OrderBy(f => KeyOrder(f.Key)).ThenBy(f => f.Key, StringComparer.Ordinal).Select(ToDto).ToList();
        return new ReviewListDto<ReviewFindingDto>(items, items.Count);
    }

    public async Task<ReviewFindingDto> RespondAsync(Guid findingId, FindingResponseRequest request, CancellationToken ct)
    {
        var finding = await db.ReviewFindings.Include(f => f.Events).SingleOrDefaultAsync(f => f.Id == findingId, ct)
            ?? throw new NotFoundException("Review finding", findingId);
        var run = await db.ReviewRuns.SingleAsync(r => r.Id == finding.RunId, ct);
        EnsureRespondable(run);
        Apply(finding, request);
        run.BotOnlyRounds = 0;
        run.UpdatedAt = Now();
        await SaveRunAsync(run.Id, ct);
        await PublishAsync(run.Id, ct);
        executor.Wake();
        return ToDto(finding);
    }

    /// <summary>One submitted review: every response applies, or none does.</summary>
    public async Task<ReviewItemsDto<ReviewFindingDto>> RespondBatchAsync(Guid runId, BatchFindingResponsesRequest request, CancellationToken ct)
    {
        var run = await db.ReviewRuns.SingleOrDefaultAsync(r => r.Id == runId, ct)
            ?? throw new NotFoundException("Review run", runId);
        EnsureRespondable(run);
        if (request.Responses is not { Count: > 0 })
            throw new ValidationException("responses", "A submitted review needs at least one response.");

        var ids = request.Responses.Select(r => r.FindingId).Distinct().ToList();
        var findings = await db.ReviewFindings.Include(f => f.Events).Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
        foreach (var response in request.Responses)
        {
            if (!findings.TryGetValue(response.FindingId, out var finding))
                throw new NotFoundException("Review finding", response.FindingId);
            if (finding.RunId != runId)
                throw new ValidationException("responses", $"Finding {response.FindingId} belongs to another run.");
            Apply(finding, response.ToRequest());
        }

        // A submitted review is human involvement: the bot-only counter starts again (design 5).
        run.BotOnlyRounds = 0;
        run.UpdatedAt = Now();
        await SaveRunAsync(runId, ct);
        await PublishAsync(runId, ct);
        executor.Wake();
        return new ReviewItemsDto<ReviewFindingDto>(ids.Select(id => ToDto(findings[id])).ToList());
    }

    private void Apply(ReviewFinding finding, FindingResponseRequest request)
    {
        var blocking = finding.Severity == ReviewSeverity.Blocker && OpenStatuses.Contains(finding.Status);
        switch (request.Action)
        {
            case ReviewFindingAction.WillFix:
                finding.Status = ReviewFindingStatus.WillFix;
                break;
            case ReviewFindingAction.Fixed:
                finding.Status = ReviewFindingStatus.Fixed;
                break;
            case ReviewFindingAction.Dispute:
                finding.Status = ReviewFindingStatus.Disputed;
                break;
            case ReviewFindingAction.Defer:
                if (blocking)
                    throw new ConflictException($"Finding {finding.Key} is an open blocker; it cannot be deferred (D-21).", "review_blocker_open");
                finding.Status = ReviewFindingStatus.Deferred;
                break;
            case ReviewFindingAction.Dismiss:
                if (request.ReasonCategory is null)
                    throw new ValidationException("reasonCategory", "A dismissal needs a reason category.");
                if (blocking)
                    throw new ConflictException($"Finding {finding.Key} is a blocker; clear it with NotABlocker (design 11.6).", "review_blocker_open");
                // Path, Repo and Team scopes record the proposal intent on the event only; the learn
                // PR or team proposal of design 11.7 comes later. The instance itself is dismissed.
                finding.Status = ReviewFindingStatus.Dismissed;
                break;
            case ReviewFindingAction.NotABlocker:
                if (finding.Severity != ReviewSeverity.Blocker)
                    throw new ConflictException($"Finding {finding.Key} is not a blocker.", "review_not_a_blocker");
                if (request.ReasonCategory is not (ReviewReasonCategory.FalsePositive or ReviewReasonCategory.ByDesign or ReviewReasonCategory.AcceptedRisk))
                    throw new ValidationException("reasonCategory", "Clearing a blocker needs FalsePositive, ByDesign or AcceptedRisk (design 11.6).");
                // TODO(review-engine trust): a clear counts alone only at or above clearBlocker trust;
                // below it the clear is pending-confirmation and the blocker stays open (design 11.6, 11.9).
                finding.Status = ReviewFindingStatus.Dismissed;
                break;
            case ReviewFindingAction.Question:
            case ReviewFindingAction.Reply:
                if (string.IsNullOrWhiteSpace(request.Text))
                    throw new ValidationException("text", $"A {request.Action} needs text.");
                break;
        }

        var now = Now();
        // Added through the context, not only the navigation: a client-set Guid key on an entity
        // discovered by DetectChanges is treated as existing (UPDATE of 0 rows), not as new.
        var evt = new ReviewFindingEvent
        {
            Id = Guid.NewGuid(),
            FindingId = finding.Id,
            Action = request.Action,
            ActorLogin = viewer.Login,
            ActorDisplayName = viewer.DisplayName,
            ReasonCategory = request.ReasonCategory,
            Scope = request.Action is ReviewFindingAction.Dismiss ? request.Scope ?? ReviewDismissScope.Instance : request.Scope,
            Text = string.IsNullOrWhiteSpace(request.Text) ? null : request.Text.Trim(),
            SyncState = ReviewSyncState.Pending,
            CreatedAt = now,
        };
        db.Add(evt);
        finding.Events.Add(evt);
        finding.SyncState = ReviewSyncState.Pending;
        finding.UpdatedAt = now;
    }

    private static void EnsureRespondable(ReviewRun run)
    {
        if (run.Status is ReviewRunStatus.Closed or ReviewRunStatus.Merged)
            throw new ConflictException($"Review run {run.Id} is {run.Status}; it takes no more responses.", "review_run_finished");
    }

    // ---- Gates and the queue ----

    /// <summary>
    /// Mine: assigned to the viewer (or <paramref name="login"/>), plus unassigned gates of the viewer's
    /// teams. Teams: every open gate of the viewer's teams. All: every open gate (design 11.3).
    /// </summary>
    public async Task<ReviewListDto<QueueItemDto>> GetQueueAsync(string? scope, string? login, CancellationToken ct)
    {
        var kind = string.IsNullOrWhiteSpace(scope) ? "All" : scope.Trim();
        var me = (string.IsNullOrWhiteSpace(login) ? viewer.Login : login.Trim()).ToLower();
        var teams = viewer.Teams.ToList();
        var query = db.ReviewGates.AsNoTracking().Include(g => g.Passes).Where(g => g.Status != ReviewQueueItemStatus.Done);
        switch (kind.ToLowerInvariant())
        {
            case "mine":
                query = query.Where(g => (g.AssigneeLogin != null && g.AssigneeLogin.ToLower() == me)
                    || (g.AssigneeLogin == null && g.TeamKey != null && teams.Contains(g.TeamKey)));
                break;
            case "teams":
                // TODO(review-engine teams): memberships come from org sync (design 12); with none
                // configured for the viewer, every team-owned gate is listed.
                query = teams.Count > 0
                    ? query.Where(g => g.TeamKey != null && teams.Contains(g.TeamKey))
                    : query.Where(g => g.TeamKey != null);
                break;
            case "all":
                break;
            default:
                throw new ValidationException("scope", "scope must be Mine, Teams or All.");
        }

        var gates = await query.OrderBy(g => g.OpenedAt).ToListAsync(ct);
        var runIds = gates.Select(g => g.RunId).Distinct().ToList();
        var runs = await db.ReviewRuns.AsNoTracking().Include(r => r.Subject).Where(r => runIds.Contains(r.Id)).ToListAsync(ct);
        var authors = runs.ToDictionary(r => r.Id, r => GateAuthor(r.Subject.Repo, r.Subject.AuthorLogin));
        var summaries = (await SummariesAsync(runs, ct)).ToDictionary(s => s.Id);
        var now = Now();
        var items = gates.Where(g => summaries.ContainsKey(g.RunId)).Select(g =>
        {
            var age = WorkingMinutes(g.OpenedAt, now);
            var sla = age >= SlaBreachWorkingMinutes ? "Breached" : age >= SlaWarnWorkingMinutes ? "Warn" : "Ok";
            return new QueueItemDto(ToDto(g, authors[g.RunId]), summaries[g.RunId], age, SlaBreachWorkingMinutes, sla);
        }).ToList();
        return new ReviewListDto<QueueItemDto>(items, items.Count);
    }

    /// <summary>
    /// Assigns a gate. With no login, picks at random from the subject's user code owners, excluding the
    /// author and anyone who passed (design 11.4; team members and load weighting come later).
    /// </summary>
    public async Task<ReviewGateDto> AssignAsync(Guid gateId, string? login, CancellationToken ct)
    {
        var gate = await OpenGateAsync(gateId, ct);
        var subject = await db.ReviewRuns.Where(r => r.Id == gate.RunId).Select(r => r.Subject).SingleAsync(ct);
        string assignee;
        if (!string.IsNullOrWhiteSpace(login))
        {
            assignee = login.Trim();
        }
        else
        {
            var excluded = new HashSet<string>(gate.Passes.Select(p => p.Login), StringComparer.OrdinalIgnoreCase) { subject.AuthorLogin };
            var candidates = subject.OwnerUsers
                .Select(o => o.TrimStart('@'))
                .Where(o => o.Length > 0 && !excluded.Contains(o))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (candidates.Count == 0)
                throw new ConflictException("No eligible reviewer: every code owner is the author or has passed.", "review_no_eligible_reviewer");
            assignee = candidates[Random.Shared.Next(candidates.Count)];
        }

        gate.AssigneeLogin = assignee;
        gate.AssigneeDisplayName = settings.Value.Viewers.TryGetValue(assignee, out var known) ? known.DisplayName : null;
        gate.Status = ReviewQueueItemStatus.Assigned;
        await TouchAndSaveAsync(gate.RunId, ct);
        return ToDto(gate, GateAuthor(subject.Repo, subject.AuthorLogin));
    }

    /// <summary>"Not me": the viewer returns the gate to the team queue with a reason and is skipped by the next pick.</summary>
    public async Task<ReviewGateDto> PassAsync(Guid gateId, string? reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ValidationException("reason", "Passing a review needs a reason.");
        var gate = await OpenGateAsync(gateId, ct);
        var subjectOf = await db.ReviewRuns.Where(r => r.Id == gate.RunId).Select(r => new { r.Subject.Repo, r.Subject.AuthorLogin }).SingleAsync(ct);
        var author = GateAuthor(subjectOf.Repo, subjectOf.AuthorLogin);
        var pass = new ReviewGatePass { Id = Guid.NewGuid(), GateId = gate.Id, Login = viewer.Login, Reason = reason.Trim(), At = Now() };
        db.Add(pass); // explicit Added: see Apply() on client-set Guid keys
        gate.Passes.Add(pass);
        gate.AssigneeLogin = null;
        gate.AssigneeDisplayName = null;
        gate.Status = ReviewQueueItemStatus.Unassigned;
        await TouchAndSaveAsync(gate.RunId, ct);
        return ToDto(gate, author);
    }

    /// <summary>
    /// A human gate decision (design 3.3 gates): Approve approves the run; AnotherRound starts the next
    /// round, queued, carrying the steer prompt; Close closes the review. The author cannot decide.
    /// </summary>
    public async Task<ReviewRunDetailDto> DecideAsync(Guid gateId, GateDecisionRequest request, CancellationToken ct)
    {
        var gate = await OpenGateAsync(gateId, ct);
        var run = await db.ReviewRuns.Include(r => r.Subject).Include(r => r.Nodes).Include(r => r.Iterations)
            .AsSplitQuery().SingleAsync(r => r.Id == gate.RunId, ct);
        if (run.Status is ReviewRunStatus.Closed or ReviewRunStatus.Merged)
            throw new ConflictException($"Review run {run.Id} is {run.Status}.", "review_run_finished");
        if (CannotDecideReason(gate, GateAuthor(run.Subject.Repo, run.Subject.AuthorLogin)) is { } reason)
            throw new ForbiddenException(reason, "review_gate_not_decider");
        if (!AllowedDecisions(gate).Contains(request.Decision))
            throw new ConflictException($"Gate {gate.Id} does not allow {request.Decision}.", "review_gate_decision_not_allowed");

        var now = Now();
        var steer = string.IsNullOrWhiteSpace(request.SteerPrompt) ? null : request.SteerPrompt.Trim();
        gate.Status = ReviewQueueItemStatus.Done;
        gate.Decision = request.Decision;
        gate.DecidedAt = now;
        gate.DecidedBy = viewer.Login;
        gate.SteerPrompt = steer;
        if (gate.NodeId is not null)
        {
            foreach (var waiting in run.Nodes.Where(n => n.NodeId == gate.NodeId && n.Iteration == gate.Iteration
                         && n.Status is ReviewNodeStatus.Waiting or ReviewNodeStatus.Running))
            {
                waiting.Status = ReviewNodeStatus.Succeeded;
                waiting.Outcome = DecisionLabel(request.Decision);
                waiting.CompletedAt = now;
                waiting.Summary = $"{request.Decision} by {viewer.Login}";
            }
        }

        // A gate decision is human involvement: the bot-only counter starts again (design 5).
        run.BotOnlyRounds = 0;
        var walked = run.Iterations.Any(i => i.HeadSha != null);
        switch (request.Decision)
        {
            case ReviewGateDecision.Approve when walked:
                // The executor follows the gate's approve edge: the approval status, then base-moved.
                run.Status = ReviewRunStatus.Queued;
                run.Stage = ReviewStage.Approval;
                run.WaitingOn = ReviewWaitingOn.Bot;
                break;
            case ReviewGateDecision.Approve:
                run.Status = ReviewRunStatus.Approved;
                run.Stage = ReviewStage.Approval;
                run.WaitingOn = null;
                break;
            case ReviewGateDecision.AnotherRound:
                run.Round += 1;
                run.BotOnlyRounds = 0;
                run.Status = ReviewRunStatus.Queued;
                run.Stage = ReviewStage.Intake;
                run.WaitingOn = ReviewWaitingOn.Bot;
                run.SteerPrompt = steer;
                var iteration = Math.Max(run.Nodes.Select(n => n.Iteration).DefaultIfEmpty(0).Max(),
                    run.Iterations.Select(i => i.Iteration).DefaultIfEmpty(0).Max()) + 1;
                db.ReviewIterations.Add(new ReviewIteration
                {
                    Id = Guid.NewGuid(),
                    RunId = run.Id,
                    Iteration = iteration,
                    Round = run.Round,
                    Trigger = "another-round",
                    StartedAt = now,
                });
                db.ReviewNodeExecutions.Add(PendingIntake(run.Id, iteration, NextSequence(run)));
                break;
            case ReviewGateDecision.Close:
                run.Status = ReviewRunStatus.Closed;
                run.Stage = ReviewStage.Done;
                run.WaitingOn = null;
                break;
        }

        run.UpdatedAt = now;
        await SaveRunAsync(run.Id, ct);
        await PublishAsync(run.Id, ct);
        if (request.Decision == ReviewGateDecision.AnotherRound || (walked && request.Decision == ReviewGateDecision.Approve))
            executor.Wake();
        return await GetRunAsync(run.Id, ct);
    }

    private async Task<ReviewGate> OpenGateAsync(Guid gateId, CancellationToken ct)
    {
        var gate = await db.ReviewGates.Include(g => g.Passes).SingleOrDefaultAsync(g => g.Id == gateId, ct)
            ?? throw new NotFoundException("Review gate", gateId);
        if (gate.Status == ReviewQueueItemStatus.Done)
            throw new ConflictException($"Review gate {gateId} is already decided.", "review_gate_decided");
        return gate;
    }

    private async Task TouchAndSaveAsync(Guid runId, CancellationToken ct)
    {
        var run = await db.ReviewRuns.SingleAsync(r => r.Id == runId, ct);
        if (run.Status == ReviewRunStatus.Closed)
            throw RunChanged(runId);
        run.UpdatedAt = Now();
        await SaveRunAsync(runId, ct);
        await PublishAsync(runId, ct);
    }

    /// <summary>The gate node's decisions in the default workflow; review and approval gates fall back to all three.</summary>
    private IReadOnlyList<ReviewGateDecision> AllowedDecisions(ReviewGate gate)
    {
        var declared = gate.NodeId is null ? [] : workflows.Default.Node(gate.NodeId)?.Decisions ?? [];
        var mapped = declared.Select(d => d switch
        {
            "approve" => ReviewGateDecision.Approve,
            "another-round" => ReviewGateDecision.AnotherRound,
            "close" => ReviewGateDecision.Close,
            _ => (ReviewGateDecision?)null,
        }).OfType<ReviewGateDecision>().ToList();
        if (mapped.Count > 0)
            return mapped;
        return gate.Kind is ReviewGateKind.ReviewGate or ReviewGateKind.ApprovalGate
            ? [ReviewGateDecision.Approve, ReviewGateDecision.AnotherRound, ReviewGateDecision.Close]
            : [];
    }

    /// <summary>Why the viewer may not decide this gate, or null (design 11.10: not the author).</summary>
    /// <summary>
    /// A gate's repo and the author it refuses: none where the policy lets the author decide
    /// (<see cref="ReviewPolicySettings.AuthorMayDecideRepos"/>).
    /// </summary>
    public readonly record struct GateSubject(string Repo, string RefusedAuthor);

    private GateSubject GateAuthor(string repo, string authorLogin) => new(repo, policy.Value.AuthorMayDecide(repo) ? "" : authorLogin);

    private string? CannotDecideReason(ReviewGate gate, GateSubject subject)
    {
        if (gate.Status == ReviewQueueItemStatus.Done)
            return "Already decided";
        if (subject.RefusedAuthor.Length > 0 && string.Equals(viewer.Login, subject.RefusedAuthor, StringComparison.OrdinalIgnoreCase))
            return "You are the author";
        if (!viewer.MayDecideOn(subject.Repo))
            return $"You need {settings.Value.Auth.RequiredRepoPermission.Trim().ToLowerInvariant()} access to {subject.Repo} on GitHub to decide its gates " +
                "(checked when you signed in: sign out and in again after your access changes)";
        // TODO(review-engine trust): deciders must also be at or above the gate's trust threshold (design 11.9, 11.10).
        return AllowedDecisions(gate).Count == 0 ? "This gate's decisions are not supported yet" : null;
    }

    private static string DecisionLabel(ReviewGateDecision decision) => decision switch
    {
        ReviewGateDecision.AnotherRound => "another-round",
        _ => decision.ToString().ToLowerInvariant(),
    };

    // ---- KPIs ----

    public async Task<ReviewKpisDto> GetKpisAsync(CancellationToken ct)
    {
        var inReview = await db.ReviewRuns.CountAsync(r => ActiveStatuses.Contains(r.Status), ct);
        var waitingOnHumans = await db.ReviewRuns.CountAsync(r => r.Status == ReviewRunStatus.AtGate || r.WaitingOn == ReviewWaitingOn.Humans, ct);
        var averageRounds = await db.ReviewRuns.AnyAsync(ct) ? await db.ReviewRuns.AverageAsync(r => (double)r.Round, ct) : 0;
        var today = new DateTime(Now().Date.Ticks, DateTimeKind.Utc);
        var costToday = await db.ReviewNodeExecutions.Where(n => (n.CompletedAt ?? n.StartedAt) >= today).SumAsync(n => n.CostUsd, ct);
        var totalCost = await db.ReviewRuns.SumAsync(r => r.CostUsd, ct);
        var accepted = await db.ReviewFindings.CountAsync(f => AcceptedStatuses.Contains(f.Status), ct);
        var usage = await db.ReviewNodeExecutions.AsNoTracking()
            .Where(n => n.Model != null)
            .GroupBy(n => new { n.Model, n.Provider, n.Transport })
            .Select(g => new { g.Key.Model, g.Key.Provider, g.Key.Transport, Calls = g.Count(), Cost = g.Sum(n => n.CostUsd) })
            .ToListAsync(ct);
        return new ReviewKpisDto(
            inReview,
            waitingOnHumans,
            Math.Round(averageRounds, 2),
            costToday,
            accepted == 0 ? null : Math.Round(totalCost / accepted, 4),
            usage.OrderByDescending(u => u.Calls)
                .Select(u => new ModelUsageDto(u.Model!, u.Provider ?? "", u.Transport ?? ReviewTransport.Http, u.Calls, u.Cost))
                .ToList());
    }

    // ---- Mapping ----

    private readonly record struct FindingCounts(int Open, int Blockers);

    private async Task<Dictionary<Guid, FindingCounts>> FindingCountsAsync(IReadOnlyCollection<Guid> runIds, CancellationToken ct)
    {
        var rows = await db.ReviewFindings.AsNoTracking()
            .Where(f => runIds.Contains(f.RunId) && OpenStatuses.Contains(f.Status))
            .GroupBy(f => f.RunId)
            .Select(g => new { RunId = g.Key, Open = g.Count(), Blockers = g.Count(f => f.Severity == ReviewSeverity.Blocker) })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.RunId, r => new FindingCounts(r.Open, r.Blockers));
    }

    /// <summary>Actual (API calls) and estimated (seat API-equivalent, D-20) cost per run, from its node executions.</summary>
    private readonly record struct RunCosts(decimal Actual, decimal Estimated);

    private async Task<Dictionary<Guid, RunCosts>> CostsAsync(IReadOnlyCollection<Guid> runIds, CancellationToken ct)
    {
        var rows = await db.ReviewNodeExecutions.AsNoTracking()
            .Where(n => runIds.Contains(n.RunId) && n.CostUsd != 0)
            .GroupBy(n => new { n.RunId, Estimated = n.CostBasis == "api-equivalent" })
            .Select(g => new { g.Key.RunId, g.Key.Estimated, Cost = g.Sum(n => n.CostUsd) })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.RunId).ToDictionary(g => g.Key, g => new RunCosts(
            g.Where(r => !r.Estimated).Sum(r => r.Cost), g.Where(r => r.Estimated).Sum(r => r.Cost)));
    }

    private async Task<List<ReviewRunSummaryDto>> SummariesAsync(List<ReviewRun> runs, CancellationToken ct)
    {
        var ids = runs.Select(r => r.Id).ToList();
        var counts = await FindingCountsAsync(ids, ct);
        var costs = await CostsAsync(ids, ct);
        var models = (await db.ReviewNodeExecutions.AsNoTracking()
                .Where(n => ids.Contains(n.RunId) && n.Model != null)
                .Select(n => new { n.RunId, n.Model })
                .Distinct()
                .ToListAsync(ct))
            .GroupBy(m => m.RunId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(m => m.Model!).Order(StringComparer.Ordinal).ToList());
        return runs.Select(r => ToSummary(r, counts.GetValueOrDefault(r.Id), costs.GetValueOrDefault(r.Id), models.GetValueOrDefault(r.Id) ?? []))
            .ToList();
    }

    private static ReviewRunSummaryDto ToSummary(ReviewRun run, FindingCounts counts, RunCosts costs, IReadOnlyList<string> models) => new()
    {
        Id = run.Id,
        Subject = ToDto(run.Subject),
        Status = run.Status,
        Stage = run.Stage,
        Round = run.Round,
        BotOnlyRounds = run.BotOnlyRounds,
        NeedsHumanApproval = run.NeedsHumanApproval,
        OpenFindings = counts.Open,
        OpenBlockers = counts.Blockers,
        Models = models,
        CostUsd = costs.Actual + costs.Estimated,
        CostActualUsd = costs.Actual,
        CostEstimatedUsd = costs.Estimated,
        BlastRadius = run.BlastReach is { } reach
            ? new BlastRadiusDto(reach, run.BlastExternalContract ?? 1, run.BlastData ?? 1, run.BlastRollbackAtRelease ?? 1,
                run.BlastReversalAfterMonths ?? 1, run.BlastExposure ?? 1)
            : null,
        StartedAt = run.StartedAt,
        UpdatedAt = run.UpdatedAt,
        WaitingOn = run.WaitingOn,
    };

    private ReviewRunDetailDto ToDetail(ReviewRun run, FindingCounts counts, RunCosts costs, bool? existing)
    {
        var models = run.Nodes.Where(n => n.Model != null).Select(n => n.Model!).Distinct().Order(StringComparer.Ordinal).ToList();
        var summary = ToSummary(run, counts, costs, models);
        return new ReviewRunDetailDto
        {
            Existing = existing,
            ConcurrencyToken = run.ConcurrencyToken,
            Closure = run.Nodes.SingleOrDefault(n => n.Iteration == 0 && n.NodeId == CloseAuditNode)?.OutputJson is { } closure
                ? JsonSerializer.Deserialize<ReviewRunClosureDto>(closure, GraphJson)
                : null,
            Id = summary.Id,
            Subject = summary.Subject,
            Status = summary.Status,
            Stage = summary.Stage,
            Round = summary.Round,
            BotOnlyRounds = summary.BotOnlyRounds,
            NeedsHumanApproval = summary.NeedsHumanApproval,
            OpenFindings = summary.OpenFindings,
            OpenBlockers = summary.OpenBlockers,
            Models = summary.Models,
            CostUsd = summary.CostUsd,
            CostActualUsd = summary.CostActualUsd,
            CostEstimatedUsd = summary.CostEstimatedUsd,
            BlastRadius = summary.BlastRadius,
            StartedAt = summary.StartedAt,
            UpdatedAt = summary.UpdatedAt,
            WaitingOn = summary.WaitingOn,
            Categories = run.Categories,
            WorkflowRevisionId = run.WorkflowRevisionId,
            Iterations = run.Iterations.OrderBy(i => i.Iteration)
                .Select(i => new ReviewIterationDto(i.Iteration, i.Round, i.Trigger, i.StartedAt, i.BaseSha, i.HeadSha)).ToList(),
            Nodes = run.Nodes.Where(n => n.Iteration > 0).OrderBy(n => n.Iteration).ThenBy(n => n.StartedAt ?? DateTime.MaxValue).ThenBy(n => n.NodeId, StringComparer.Ordinal)
                .ThenBy(n => n.ItemKey, StringComparer.Ordinal)
                .Select(ToDto).ToList(),
            Gates = run.Gates.OrderBy(g => g.OpenedAt).Select(g => ToDto(g, GateAuthor(run.Subject.Repo, run.Subject.AuthorLogin))).ToList(),
        };
    }

    public static ReviewSubjectDto ToDto(ReviewSubject s) => new(
        s.Id, s.Host, s.Repo, s.PrNumber, s.Title, s.Url, s.AuthorLogin, s.AuthorDisplayName, s.AuthorResolvedBy,
        s.OpenedByLogin.Length > 0 ? s.OpenedByLogin : s.AuthorLogin,
        s.BaseRef, s.HeadRef, s.BaseSha, s.HeadSha,
        s.OwnerTeams.Select(t => new ReviewOwnerDto("Team", t)).Concat(s.OwnerUsers.Select(u => new ReviewOwnerDto("User", u))).ToList());

    public static NodeExecutionDto ToDto(ReviewNodeExecution n) => new(
        n.Id, n.NodeId, n.Iteration, n.Kind, n.Status, n.ItemKey, n.Outcome, n.Provider, n.Model, n.Transport, n.InputTokens,
        n.OutputTokens, n.CostUsd, n.StartedAt, n.CompletedAt, n.Summary);

    /// <summary>A gate as the viewer sees it: which decisions it allows and whether the viewer may decide.</summary>
    public ReviewGateDto ToDto(ReviewGate g, GateSubject subject)
    {
        var cannot = CannotDecideReason(g, subject);
        return new ReviewGateDto(
            g.Id, g.RunId, g.Kind, g.Status, g.TeamKey, g.AssigneeLogin, g.AssigneeDisplayName, g.OpenedAt, g.DecidedAt, g.DecidedBy,
            g.Decision, g.SteerPrompt, g.Reasons, AllowedDecisions(g), cannot is null, cannot,
            g.Passes.OrderBy(p => p.At).Select(p => new GatePassDto(p.Login, p.Reason, p.At)).ToList());
    }

    public static ReviewFindingDto ToDto(ReviewFinding f) => new(
        f.Id, f.RunId, f.Key, f.CheckId, f.CheckWhy, f.Severity, f.Flags, f.BypassesRoundBar, f.Confidence, f.FilePath,
        f.LineStart, f.LineEnd, f.Anchor, f.Title, f.Body, f.Suggestion, f.Hunk, f.Status, f.RoundRaised, f.Posted, f.SyncState,
        f.ExternalThreadUrl,
        f.Events.OrderBy(e => e.CreatedAt).Select(e => new FindingEventDto(e.Id, e.Action, e.ActorLogin, e.ActorDisplayName,
            e.ReasonCategory, e.Scope, e.Text, e.SyncState, e.CreatedAt)).ToList());

    private static ReviewNodeExecution PendingIntake(Guid runId, int iteration, int sequence) => new()
    {
        Id = Guid.NewGuid(),
        RunId = runId,
        NodeId = "intake",
        Iteration = iteration,
        Sequence = sequence,
        Kind = ReviewNodeKind.Code,
        Status = ReviewNodeStatus.Pending,
    };

    private static int KeyOrder(string key) => key.Length > 1 && int.TryParse(key.AsSpan(1), out var n) ? n : int.MaxValue;

    /// <summary>Working minutes, Monday to Friday 09:00-17:00 UTC, until work calendars exist (design 11.3).</summary>
    public static int WorkingMinutes(DateTime from, DateTime to)
    {
        if (to <= from)
            return 0;
        var minutes = 0.0;
        for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            var start = day.AddHours(9);
            var end = day.AddHours(17);
            var overlapStart = from > start ? from : start;
            var overlapEnd = to < end ? to : end;
            if (overlapEnd > overlapStart)
                minutes += (overlapEnd - overlapStart).TotalMinutes;
        }

        return (int)minutes;
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;

    private Task PublishAsync(Guid runId, CancellationToken ct) =>
        events.PublishToAllAsync(RunChangedEvent, new ReviewRunChangedEvent(runId), ct);
}
