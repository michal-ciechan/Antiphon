using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review.Decisions;
using Antiphon.Server.Application.Review.Execution;
using Antiphon.Server.Application.Review.Workflows;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Review;

/// <summary>
/// The workflow executor (design 3.4): a hosted service, not Hangfire, that walks a run's workflow
/// snapshot node by node from intake to merge, and publishes <c>ReviewRunChanged</c> on every
/// transition. Each node is a step keyed by its yaml <c>run</c> name; gates, waits and fan-outs are
/// handled by kind.
/// <para>
/// The walk follows the latest top-level execution of the run's current iteration (by
/// <see cref="ReviewNodeExecution.Sequence"/>): a Pending or Running one is run, a Succeeded or
/// Skipped one is followed along the edge labelled with its outcome (else its unlabelled edge), and
/// a Waiting gate or wait has parked the run. A gate decision (the decision API) or an event
/// (Refresh: push, base-moved, merge, close) completes the parked execution with the decision or
/// event as its outcome and queues the run again. An edge back to the entry node starts a new
/// iteration; an edge with <c>maxIterations</c> opens its overflow gate past the limit (D-2).
/// </para>
/// <para>
/// Lease: a run is claimed by one conditional UPDATE that moves it from Queued to Running, or takes
/// over a Running run not updated for <c>Review:Executor:LeaseSeconds</c>, so two servers on one
/// database never walk the same run. Every transition renews it. A run whose iterations never pinned
/// a head through a code host (the demo data) is never claimed.
/// </para>
/// <para>
/// A failure is loud: the node is marked Failed with the reason as its summary and the run becomes
/// Failed. Nothing is defaulted. Refresh retries a failed run. Each pass also syncs pending finding
/// responses to the code host (<see cref="ReviewFindingSync"/>).
/// </para>
/// </summary>
public sealed class ReviewWorkflowExecutor(
    IServiceScopeFactory scopes,
    ReviewExecutorSignal signal,
    IOptions<ReviewExecutorSettings> options,
    TimeProvider time,
    ILogger<ReviewWorkflowExecutor> logger) : BackgroundService
{
    private const int MaxRunsPerPass = 20;
    private const int MaxStepsPerWalk = 64;

    private delegate Task<NodeResult> Step(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution execution, CancellationToken ct);

    /// <summary>The code and decision steps by yaml <c>run</c> name (design 3.3).</summary>
    private static readonly IReadOnlyDictionary<string, Step> Steps = new Dictionary<string, Step>(StringComparer.Ordinal)
    {
        ["Intake"] = ReviewIntakeSteps.IntakeAsync,
        ["Delta"] = ReviewIntakeSteps.DeltaAsync,
        ["Categorise"] = ReviewIntakeSteps.CategoriseAsync,
        ["SelectChecks"] = ReviewSelectionSteps.SelectChecksAsync,
        ["GroupChecks"] = ReviewSelectionSteps.GroupChecksAsync,
        ["BuildPacks"] = ReviewSelectionSteps.BuildPacksAsync,
        ["CollateFindings"] = ReviewReviewSteps.CollateAsync,
        ["VerifyFindings"] = ReviewReviewSteps.VerifyAsync,
        ["Publish"] = ReviewReviewSteps.PublishAsync,
        ["RouteAfterRound"] = ReviewDecideSteps.RouteAsync,
        ["NeedsHumanApproval"] = ReviewDecideSteps.NeedsHumanApprovalAsync,
        ["BotApprove"] = ReviewDecideSteps.BotApproveAsync,
        ["RecordBaseDrift"] = ReviewDecideSteps.RecordBaseDriftAsync,
        ["CheckBaseMoved"] = ReviewBranchSteps.CheckBaseMovedAsync,
        ["BaseDriftCheck"] = ReviewBranchSteps.BaseDriftAsync,
        ["UpdateBranch"] = ReviewBranchSteps.UpdateBranchAsync,
        ["Merge"] = ReviewBranchSteps.MergeAsync,
    };

    /// <summary>The steps this executor implements, for diagnostics and tests.</summary>
    public static IReadOnlyCollection<string> StepNames => Steps.Keys.ToList();

    private TimeSpan Lease => TimeSpan.FromSeconds(Math.Max(30, options.Value.LeaseSeconds));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("The review workflow executor is disabled (Review:Executor:Enabled).");
            return;
        }

        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "A review workflow executor pass failed; the next poll retries.");
            }

            try
            {
                await signal.WaitAsync(TimeSpan.FromSeconds(Math.Max(1, options.Value.PollSeconds)), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One pass: claims and walks every run with work, oldest update first, then syncs responses. Returns the runs walked.</summary>
    public async Task<int> RunPendingAsync(CancellationToken ct)
    {
        List<Guid> candidates;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var staleBefore = Now() - Lease;
            candidates = await Claimable(db.ReviewRuns.AsNoTracking(), staleBefore)
                .Where(r => r.Iterations.Any(i => i.HeadSha != null))
                .OrderBy(r => r.UpdatedAt)
                .Select(r => r.Id)
                .Take(MaxRunsPerPass)
                .ToListAsync(ct);
        }

        var walked = 0;
        foreach (var runId in candidates)
        {
            ct.ThrowIfCancellationRequested();
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = Now();
            var claimed = await Claimable(db.ReviewRuns, now - Lease)
                .Where(r => r.Id == runId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReviewRunStatus.Running).SetProperty(r => r.UpdatedAt, now), ct);
            if (claimed != 1)
                continue;
            try
            {
                await WalkAsync(scope.ServiceProvider, runId, ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                logger.LogDebug("Review run {RunId} changed during execution. Stopping this walk.", runId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Review run {RunId}: the executor failed outside a node", runId);
                await FailRunAsync(runId, $"Executor error: {ex.Message}", ct);
            }

            walked++;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ReviewFindingSync>().SyncAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Review finding sync failed; the next pass retries.");
        }

        return walked;
    }

    /// <summary>
    /// An error outside a node's own handling (a broken graph, a database failure): the running node
    /// and the run are marked Failed from a fresh scope, so the lease does not retry it forever.
    /// </summary>
    private async Task FailRunAsync(Guid runId, string reason, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = Now();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var updated = await db.ReviewRuns.Where(r => r.Id == runId
                    && (r.Status == ReviewRunStatus.Running || r.Status == ReviewRunStatus.Approved))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReviewRunStatus.Failed)
                    .SetProperty(r => r.WaitingOn, (ReviewWaitingOn?)null).SetProperty(r => r.UpdatedAt, now), ct);
            if (updated == 0)
                return;
            await db.ReviewNodeExecutions.Where(n => n.RunId == runId && n.Status == ReviewNodeStatus.Running)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, ReviewNodeStatus.Failed)
                    .SetProperty(n => n.Summary, reason).SetProperty(n => n.CompletedAt, now), ct);
            await transaction.CommitAsync(ct);
            await scope.ServiceProvider.GetRequiredService<IEventBus>()
                .PublishToAllAsync(ReviewService.RunChangedEvent, new ReviewRunChangedEvent(runId), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Review run {RunId} could not be marked failed", runId);
        }
    }

    /// <summary>Runs that are queued, or running under a lapsed lease. Parked and finished runs wait for an event.</summary>
    private static IQueryable<ReviewRun> Claimable(IQueryable<ReviewRun> runs, DateTime staleBefore) =>
        runs.Where(r => r.Status == ReviewRunStatus.Queued || (r.Status == ReviewRunStatus.Running && r.UpdatedAt < staleBefore));

    private async Task WalkAsync(IServiceProvider services, Guid runId, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var run = await db.ReviewRuns.Include(r => r.Subject).Include(r => r.Iterations).Include(r => r.Nodes)
            .AsSplitQuery().SingleAsync(r => r.Id == runId, ct);
        var iteration = run.Iterations.MaxBy(i => i.Iteration)
            ?? throw new InvalidOperationException($"Review run {runId} has no iteration.");
        var workflow = await WorkflowAsync(services, db, run, ct);
        var walk = new ReviewWalk(services, db, run, iteration, workflow, time);

        for (var step = 0; step < MaxStepsPerWalk; step++)
        {
            if (await SupersededAsync(walk, ct))
                return;

            var frontier = walk.Frontier();
            if (frontier is null)
            {
                var entry = workflow.Node(walk.EntryNodeId)!;
                walk.NewExecution(entry.Id, entry.Kind);
                continue;
            }

            var node = workflow.Node(frontier.NodeId)
                ?? throw new InvalidOperationException($"Workflow '{workflow.Name}' has no node '{frontier.NodeId}'.");
            switch (frontier.Status)
            {
                case ReviewNodeStatus.Pending:
                case ReviewNodeStatus.Running:
                case ReviewNodeStatus.Waiting when node.Kind == ReviewNodeKind.FanOut:
                    if (!await StepAsync(walk, node, frontier, ct))
                        return;
                    break;
                case ReviewNodeStatus.Waiting:
                    await ParkAgainAsync(walk, node, ct);
                    return;
                case ReviewNodeStatus.Succeeded:
                case ReviewNodeStatus.Skipped:
                case ReviewNodeStatus.FailedContinued:
                    // A merge gate's approve is "merge it": the PR was approved before the gate opened.
                    if (node.Kind == ReviewNodeKind.Gate && frontier.Outcome == "approve" && ReviewDecideSteps.GateKindOf(node) != ReviewGateKind.MergeGate)
                        await ReviewDecideSteps.ApplyGateApprovalAsync(walk, frontier, ct);
                    if (!await FollowAsync(walk, node, frontier, ct))
                        return;
                    break;
                default:
                    walk.Run.Status = ReviewRunStatus.Failed;
                    walk.Run.WaitingOn = null;
                    walk.Run.UpdatedAt = Now();
                    await SaveAndPublishAsync(walk, ct);
                    return;
            }
        }

        throw new InvalidOperationException($"Review run {runId} did not park within {MaxStepsPerWalk} steps.");
    }

    /// <summary>Runs one node. False when the walk stops: the node failed, parked the run, or is polling.</summary>
    private async Task<bool> StepAsync(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution execution, CancellationToken ct)
    {
        var now = Now();
        execution.Kind = node.Kind;
        if (execution.Status != ReviewNodeStatus.Waiting)
        {
            execution.StartedAt = now;
            execution.CompletedAt = null;
            execution.Outcome = null;
            execution.Summary = null;
        }

        execution.Status = ReviewNodeStatus.Running;
        walk.Run.Status = ReviewRunStatus.Running;
        walk.Run.Stage = StageOf(node, walk.Run.Stage);
        walk.Run.WaitingOn = ReviewWaitingOn.Bot;
        walk.Run.UpdatedAt = now;
        await SaveAndPublishAsync(walk, ct);

        NodeResult result;
        walk.Warnings.Clear();
        try
        {
            result = await RunNodeAsync(walk, node, execution, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await ReleaseAsync(walk, execution);
            throw;
        }
        catch (Exception ex)
        {
            if (ex is not (CodeHostException or DecisionChainException or ReviewAgentRunnerException))
                logger.LogError(ex, "Review run {RunId} node {NodeId} threw", walk.Run.Id, node.Id);
            result = NodeResult.Fail(ex is CodeHostException ? $"Code host: {ex.Message}" : ex.Message);
        }

        if (await SupersededAsync(walk, ct))
            return false;

        // onFailure: continue (Review:Policy:OnFailure or the workflow) turns a failure into a fallback:
        // the run goes on along the node's fallback output, else its unlabelled edge.
        if (result.Kind == NodeResultKind.Failed && walk.ContinuesOnFailure(node))
            result = new NodeResult(NodeResultKind.Succeeded, node.StringParam("fallback"), "Continued without this step.", result.Output, result.Usage)
            {
                Degraded = result.Summary,
            };

        now = Now();
        execution.Outcome = result.Outcome;
        execution.Summary = result.Degraded is { } degraded
            ? $"Failed, continued (onFailure: continue): {degraded}\n{result.Summary}"
            : result.Summary;
        if (walk.Warnings.Count > 0)
            execution.Summary = $"{execution.Summary}\n{string.Join("\n", walk.Warnings)}";
        if (result.Output is not null)
            execution.OutputJson = ReviewWalk.ToJson(result.Output);
        if (result.Usage is { } usage)
        {
            execution.Provider = usage.Provider;
            execution.Model = usage.Model;
            execution.Transport = usage.Transport;
            execution.InputTokens = usage.InputTokens;
            execution.OutputTokens = usage.OutputTokens;
            execution.CostUsd = usage.CostUsd;
            execution.CostBasis = usage.CostBasis;
            walk.Run.CostUsd += usage.CostUsd;
        }

        walk.Run.UpdatedAt = now;
        var proceed = false;
        switch (result.Kind)
        {
            case NodeResultKind.Succeeded:
            case NodeResultKind.Skipped:
                execution.Status = result.Degraded is not null ? ReviewNodeStatus.FailedContinued
                    : result.Kind == NodeResultKind.Succeeded ? ReviewNodeStatus.Succeeded
                    : ReviewNodeStatus.Skipped;
                execution.CompletedAt = now;
                if (walk.Run.Status is not (ReviewRunStatus.Running or ReviewRunStatus.Approved))
                    walk.Run.Status = ReviewRunStatus.Running;
                proceed = true;
                break;
            case NodeResultKind.Waiting:
                execution.Status = ReviewNodeStatus.Waiting;
                Park(walk.Run, result.Park!);
                break;
            case NodeResultKind.Polling:
                execution.Status = ReviewNodeStatus.Waiting;
                walk.Run.Status = ReviewRunStatus.Queued;
                walk.Run.WaitingOn = ReviewWaitingOn.Bot;
                break;
            default:
                execution.Status = ReviewNodeStatus.Failed;
                execution.CompletedAt = now;
                walk.Run.Status = ReviewRunStatus.Failed;
                walk.Run.WaitingOn = null;
                logger.LogWarning("Review run {RunId} failed at {NodeId}: {Reason}", walk.Run.Id, node.Id, result.Summary);
                await ReportFailureAsync(walk, node, result.Summary, ct);
                break;
        }

        await SaveAndPublishAsync(walk, ct);
        return proceed;
    }

    /// <summary>
    /// A failed run says so on the PR, so its review status does not stay pending. Best effort: the
    /// run has already failed, so a refused status changes nothing.
    /// </summary>
    private async Task ReportFailureAsync(ReviewWalk walk, WorkflowNode node, string reason, CancellationToken ct)
    {
        try
        {
            var text = $"Failed at {node.Id}: {reason}".ReplaceLineEndings(" ");
            await walk.SetStatusAsync(ReviewWalk.ReviewStatusContext, CodeHostStatusState.Error, text.Length <= 140 ? text : text[..137] + "...", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogInformation("Review run {RunId}: the failure status was not posted: {Reason}", walk.Run.Id, ex.Message);
        }
    }

    private static Task<NodeResult> RunNodeAsync(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution execution, CancellationToken ct) =>
        node.Kind switch
        {
            ReviewNodeKind.Gate => ReviewDecideSteps.GateAsync(walk, node, execution, ct),
            ReviewNodeKind.Wait => ReviewDecideSteps.WaitAsync(walk, node, execution, ct),
            ReviewNodeKind.FanOut => ReviewReviewSteps.FanOutAsync(walk, node, execution, ct),
            _ when node.Run is { } name && Steps.TryGetValue(name, out var step) => step(walk, node, execution, ct),
            _ => Task.FromResult(NodeResult.Fail($"The executor has no step for '{node.Run ?? node.Id}' ({node.Kind}).")),
        };

    /// <summary>
    /// Follows the edge for the execution's outcome. A back edge into the entry node starts the next
    /// iteration; over an edge's <c>maxIterations</c> the overflow gate opens instead. False when the
    /// walk ends here (no edge: the run fails, or a merge or close event finishes it).
    /// </summary>
    private async Task<bool> FollowAsync(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution execution, CancellationToken ct)
    {
        var edges = walk.Workflow.Edges.Where(e => e.From == node.Id).ToList();
        var outcome = execution.Outcome;
        var edge = (outcome is null ? null : edges.FirstOrDefault(e => e.Labels.Contains(outcome, StringComparer.Ordinal)))
            ?? edges.FirstOrDefault(e => e.Labels.Count == 0);
        if (edge is null)
        {
            if (outcome is "merge" or "close")
            {
                Finish(walk.Run, outcome == "merge" ? ReviewRunStatus.Merged : ReviewRunStatus.Closed, Now());
                await SaveAndPublishAsync(walk, ct);
                return false;
            }

            execution.Status = ReviewNodeStatus.Failed;
            execution.Summary = $"{execution.Summary}\nNo edge leaves '{node.Id}' for outcome '{outcome ?? "(none)"}'.".Trim();
            walk.Run.Status = ReviewRunStatus.Failed;
            walk.Run.WaitingOn = null;
            walk.Run.UpdatedAt = Now();
            await SaveAndPublishAsync(walk, ct);
            return false;
        }

        var target = edge.To;
        string? overflowReason = null;
        if (edge.MaxIterations is { } max)
        {
            var traversals = walk.Run.Nodes.Count(n => n.NodeId == node.Id && n.ItemKey == null && n.Status == ReviewNodeStatus.Succeeded
                && (edge.Labels.Count == 0 || (n.Outcome is not null && edge.Labels.Contains(n.Outcome))));
            if (traversals > max)
            {
                target = edge.Overflow ?? throw new InvalidOperationException($"Edge {edge.From} -> {edge.To} has no overflow gate.");
                overflowReason = $"{edge.From} -> {edge.To} passed its limit of {max} iterations (D-2)";
            }
        }

        if (target == walk.EntryNodeId)
        {
            StartIteration(walk, node, outcome);
        }
        else
        {
            var next = walk.Workflow.Node(target) ?? throw new InvalidOperationException($"Workflow '{walk.Workflow.Name}' has no node '{target}'.");
            var created = walk.NewExecution(next.Id, next.Kind);
            if (overflowReason is not null)
                created.OutputJson = ReviewWalk.ToJson(new GateOutput(Guid.Empty, [overflowReason]));
        }

        walk.Run.UpdatedAt = Now();
        await SaveAndPublishAsync(walk, ct);
        return true;
    }

    /// <summary>
    /// A back edge into intake: the next iteration (design 3.2). A push or another round is the next
    /// round; a recheck or an affected base drift re-reviews in the same round.
    /// </summary>
    private void StartIteration(ReviewWalk walk, WorkflowNode from, string? outcome)
    {
        var run = walk.Run;
        var trigger = (from.Id, outcome) switch
        {
            (_, "push") => "push",
            (_, "recheck") => "recheck",
            (_, "another-round") => "another-round",
            ("base-drift", "affected") => "base-moved",
            _ => outcome ?? "manual",
        };
        if (trigger is "push" or "another-round")
            run.Round += 1;
        if (trigger == "another-round")
            run.BotOnlyRounds = 0;
        var number = Math.Max(run.Nodes.Select(n => n.Iteration).DefaultIfEmpty(0).Max(), run.Iterations.Select(i => i.Iteration).DefaultIfEmpty(0).Max()) + 1;
        var iteration = new ReviewIteration { Id = Guid.NewGuid(), RunId = run.Id, Iteration = number, Round = run.Round, Trigger = trigger, StartedAt = Now() };
        walk.Db.ReviewIterations.Add(iteration);
        if (!run.Iterations.Contains(iteration))
            run.Iterations.Add(iteration);
        walk.MoveTo(iteration);
        var entry = walk.Workflow.Node(walk.EntryNodeId)!;
        walk.NewExecution(entry.Id, entry.Kind);
        run.Status = ReviewRunStatus.Running;
        run.Stage = ReviewStage.Intake;
        run.WaitingOn = ReviewWaitingOn.Bot;
    }

    /// <summary>A queued run whose frontier is still a parked gate or wait (no event yet): parks it again.</summary>
    private async Task ParkAgainAsync(ReviewWalk walk, WorkflowNode node, CancellationToken ct)
    {
        Park(walk.Run, node.Kind == ReviewNodeKind.Gate
            ? new ParkState(ReviewRunStatus.AtGate, ReviewStage.HumanGate, ReviewWaitingOn.Humans)
            : node.ListParam("on").Contains("merge")
                ? new ParkState(ReviewRunStatus.AwaitingMerge, ReviewStage.BranchMaintenance, ReviewWaitingOn.Author)
                : new ParkState(ReviewRunStatus.AwaitingFixes, ReviewStage.AwaitingFixes, ReviewWaitingOn.Author));
        walk.Run.UpdatedAt = Now();
        await SaveAndPublishAsync(walk, ct);
    }

    private static void Park(ReviewRun run, ParkState park)
    {
        run.Status = park.Status;
        run.Stage = park.Stage;
        run.WaitingOn = park.WaitingOn;
    }

    /// <summary>The run ended on the code host: Merged or Closed, stage Done.</summary>
    public static void Finish(ReviewRun run, ReviewRunStatus status, DateTime now)
    {
        run.Status = status;
        run.Stage = ReviewStage.Done;
        run.WaitingOn = null;
        run.UpdatedAt = now;
    }

    /// <summary>
    /// True when the run moved on under this walk: a newer iteration (a Refresh saw a push) or a
    /// status this executor did not set. The running node is marked Skipped and the walk ends.
    /// </summary>
    private async Task<bool> SupersededAsync(ReviewWalk walk, CancellationToken ct)
    {
        var current = await walk.Db.ReviewRuns.AsNoTracking().Where(r => r.Id == walk.Run.Id)
            .Select(r => new { r.Status, Latest = r.Iterations.Max(i => i.Iteration) })
            .SingleAsync(ct);
        if (current.Status == ReviewRunStatus.Closed)
            return true;
        if (current.Latest <= walk.Iteration.Iteration && current.Status is ReviewRunStatus.Running or ReviewRunStatus.Approved)
            return false;

        var running = walk.Run.Nodes.Where(n => n.Iteration == walk.Iteration.Iteration && n.Status == ReviewNodeStatus.Running).ToList();
        if (running.Count > 0)
        {
            foreach (var execution in running)
            {
                execution.Status = ReviewNodeStatus.Skipped;
                execution.CompletedAt = Now();
                execution.Summary = current.Latest > walk.Iteration.Iteration
                    ? $"superseded by iteration {current.Latest}"
                    : $"stopped: the run is {current.Status}";
            }

            // Only the executions are saved: the run row and the rest now belong to whoever moved it on.
            foreach (var entry in walk.Db.ChangeTracker.Entries().Where(e => e.Entity is not ReviewNodeExecution))
            {
                if (entry.State == EntityState.Modified)
                    entry.State = EntityState.Unchanged;
                else if (entry.State == EntityState.Added)
                    entry.State = EntityState.Detached;
            }

            await using (var transaction = await walk.Db.Database.BeginTransactionAsync(ct))
            {
                await ReviewRunWrites.LockAsync(walk.Db, walk.Run.Id, ct);
                if (await walk.Db.ReviewRuns.AnyAsync(r => r.Id == walk.Run.Id && r.Status == ReviewRunStatus.Closed, ct))
                    return true;
                await walk.Db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            await PublishAsync(walk, ct);
        }

        return true;
    }

    /// <summary>On shutdown, hands the node and run back (Pending, Queued) so the next start resumes at once.</summary>
    private async Task ReleaseAsync(ReviewWalk walk, ReviewNodeExecution execution)
    {
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            execution.Status = ReviewNodeStatus.Pending;
            execution.StartedAt = null;
            walk.Run.Status = ReviewRunStatus.Queued;
            await ReviewRunWrites.SaveAsync(walk.Db, walk.Run.Id, budget.Token);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Review run {RunId} was not released on shutdown; its lease will lapse", walk.Run.Id);
        }
    }

    private static async Task<WorkflowDefinition> WorkflowAsync(IServiceProvider services, AppDbContext db, ReviewRun run, CancellationToken ct)
    {
        if (run.WorkflowRevisionId is { } id
            && await db.ReviewWorkflowRevisions.AsNoTracking().Where(w => w.Id == id).Select(w => w.GraphJson).SingleOrDefaultAsync(ct) is { } json)
            return ReviewWorkflowService.FromStoredGraph(json);
        return services.GetRequiredService<ReviewWorkflowService>().Default;
    }

    private static ReviewStage StageOf(WorkflowNode node, ReviewStage current) => (node.Kind, node.Run) switch
    {
        (_, "Intake" or "Delta") => ReviewStage.Intake,
        (_, "Categorise") => ReviewStage.Categorise,
        (_, "SelectChecks" or "GroupChecks" or "BuildPacks") => ReviewStage.Select,
        (ReviewNodeKind.FanOut or ReviewNodeKind.Agent, _) => ReviewStage.Review,
        (_, "CollateFindings") => ReviewStage.Collate,
        (_, "VerifyFindings") => ReviewStage.Verify,
        (_, "Publish" or "RouteAfterRound") => ReviewStage.Publish,
        (_, "NeedsHumanApproval" or "BotApprove") => ReviewStage.Approval,
        (ReviewNodeKind.Gate, _) => ReviewStage.HumanGate,
        (_, "RecordBaseDrift") => ReviewStage.AwaitingFixes,
        (_, "CheckBaseMoved" or "BaseDriftCheck" or "UpdateBranch" or "Merge") => ReviewStage.BranchMaintenance,
        _ => current,
    };

    private static async Task SaveAndPublishAsync(ReviewWalk walk, CancellationToken ct)
    {
        await ReviewRunWrites.SaveAsync(walk.Db, walk.Run.Id, ct);
        await PublishAsync(walk, ct);
    }

    private static Task PublishAsync(ReviewWalk walk, CancellationToken ct) =>
        walk.Services.GetRequiredService<IEventBus>()
            .PublishToAllAsync(ReviewService.RunChangedEvent, new ReviewRunChangedEvent(walk.Run.Id), ct);

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
