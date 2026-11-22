using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review.Decisions;
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
/// The minimal workflow executor (design 3.4): a hosted service, not Hangfire, that walks a run's
/// workflow snapshot from intake, one node execution at a time, and publishes <c>ReviewRunChanged</c>
/// on every transition. It runs intake (for an another-round iteration still waiting for it), delta
/// and categorise, then stops before <see cref="StopBeforeNode"/>.
/// <para>
/// Lease: a run is claimed by one conditional UPDATE that moves it from Queued to Running, or takes
/// over a Running run not updated for <c>Review:Executor:LeaseSeconds</c>, so two servers on one
/// database never walk the same run. Every transition renews it. A run whose iterations never pinned
/// a head through a code host (the demo data) is never claimed.
/// </para>
/// <para>
/// A failure is loud: the node is marked Failed with the reason as its summary and the run becomes
/// Failed. Nothing is defaulted. Refresh retries a failed run.
/// </para>
/// </summary>
public sealed class ReviewWorkflowExecutor(
    IServiceScopeFactory scopes,
    ReviewExecutorSignal signal,
    IOptions<ReviewExecutorSettings> options,
    TimeProvider time,
    ILogger<ReviewWorkflowExecutor> logger) : BackgroundService
{
    /// <summary>
    /// Where the walk stops in this slice. The run is left at stage Select, status Running, with a
    /// Pending execution of this node that says so; a later executor resumes from it. Move this as
    /// the next nodes are implemented.
    /// </summary>
    public const string StopBeforeNode = "select";

    public const string StopSummary = "executor stops here in this slice";

    private const int MaxRunsPerPass = 20;
    private const int MaxStepsPerWalk = 32;

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

    /// <summary>One pass: claims and walks every run with work, oldest update first. Returns the runs walked.</summary>
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
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Review run {RunId}: the executor failed outside a node", runId);
                await FailRunAsync(runId, $"Executor error: {ex.Message}", ct);
            }

            walked++;
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
            await db.ReviewNodeExecutions.Where(n => n.RunId == runId && n.Status == ReviewNodeStatus.Running)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, ReviewNodeStatus.Failed)
                    .SetProperty(n => n.Summary, reason).SetProperty(n => n.CompletedAt, now), ct);
            await db.ReviewRuns.Where(r => r.Id == runId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReviewRunStatus.Failed)
                    .SetProperty(r => r.WaitingOn, (ReviewWaitingOn?)null).SetProperty(r => r.UpdatedAt, now), ct);
            await scope.ServiceProvider.GetRequiredService<IEventBus>()
                .PublishToAllAsync(ReviewService.RunChangedEvent, new ReviewRunChangedEvent(runId), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Review run {RunId} could not be marked failed", runId);
        }
    }

    /// <summary>Runs at intake or categorise that are queued, or running under a lapsed lease.</summary>
    private static IQueryable<ReviewRun> Claimable(IQueryable<ReviewRun> runs, DateTime staleBefore) =>
        runs.Where(r => (r.Stage == ReviewStage.Intake || r.Stage == ReviewStage.Categorise)
            && (r.Status == ReviewRunStatus.Queued || (r.Status == ReviewRunStatus.Running && r.UpdatedAt < staleBefore)));

    private async Task WalkAsync(IServiceProvider services, Guid runId, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var run = await db.ReviewRuns.Include(r => r.Subject).Include(r => r.Iterations).Include(r => r.Nodes)
            .AsSplitQuery().SingleAsync(r => r.Id == runId, ct);
        var iteration = run.Iterations.MaxBy(i => i.Iteration)
            ?? throw new InvalidOperationException($"Review run {runId} has no iteration.");
        var workflow = await WorkflowAsync(services, db, run, ct);
        var walk = new Walk(services, db, run, iteration, workflow);

        var nodeId = workflow.Nodes.FirstOrDefault(n => n.Run == "Intake")?.Id ?? workflow.Nodes[0].Id;
        for (var step = 0; step < MaxStepsPerWalk; step++)
        {
            if (await SupersededAsync(walk, ct))
                return;
            if (nodeId == StopBeforeNode)
            {
                await ParkAsync(walk, ct);
                return;
            }

            var node = workflow.Node(nodeId)
                ?? throw new InvalidOperationException($"Workflow '{workflow.Name}' has no node '{nodeId}'.");
            if (walk.Execution(nodeId) is not { Status: ReviewNodeStatus.Succeeded } && !await StepAsync(walk, node, ct))
                return;
            nodeId = Next(workflow, nodeId);
        }

        throw new InvalidOperationException($"Review run {runId} did not reach '{StopBeforeNode}' within {MaxStepsPerWalk} steps.");
    }

    /// <summary>Runs one node. False when it failed (the run is then Failed) or the iteration was superseded.</summary>
    private async Task<bool> StepAsync(Walk walk, WorkflowNode node, CancellationToken ct)
    {
        var execution = walk.Execution(node.Id);
        if (execution is null)
        {
            execution = new ReviewNodeExecution
            {
                Id = Guid.NewGuid(),
                RunId = walk.Run.Id,
                NodeId = node.Id,
                Iteration = walk.Iteration.Iteration,
                Kind = node.Kind,
            };
            // Added through the context: a client-set Guid key found only through the navigation is
            // treated as an existing row (see ReviewService.Apply).
            walk.Db.ReviewNodeExecutions.Add(execution);
        }

        var now = Now();
        execution.Kind = node.Kind;
        execution.Status = ReviewNodeStatus.Running;
        execution.StartedAt = now;
        execution.CompletedAt = null;
        execution.Outcome = null;
        execution.Summary = null;
        walk.Run.Status = ReviewRunStatus.Running;
        walk.Run.Stage = StageOf(node, walk.Run.Stage);
        walk.Run.WaitingOn = ReviewWaitingOn.Bot;
        walk.Run.UpdatedAt = now;
        await SaveAndPublishAsync(walk, ct);

        NodeResult result;
        try
        {
            result = node.Run switch
            {
                "Intake" => await IntakeAsync(walk, execution, ct),
                "Delta" => await DeltaAsync(walk, ct),
                "Categorise" => await CategoriseAsync(walk, node, ct),
                _ => NodeResult.Fail($"The executor has no step for '{node.Run ?? node.Id}' in this slice."),
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await ReleaseAsync(walk, execution);
            throw;
        }
        catch (Exception ex)
        {
            if (ex is not (CodeHostException or DecisionChainException))
                logger.LogError(ex, "Review run {RunId} node {NodeId} threw", walk.Run.Id, node.Id);
            result = NodeResult.Fail(ex is CodeHostException ? $"Code host: {ex.Message}" : ex.Message);
        }

        if (await SupersededAsync(walk, ct))
            return false;

        now = Now();
        execution.Status = result.FailureReason is null ? ReviewNodeStatus.Succeeded : ReviewNodeStatus.Failed;
        execution.Outcome = result.Outcome;
        execution.Summary = result.FailureReason ?? result.Summary;
        execution.CompletedAt = now;
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
        if (result.FailureReason is { } reason)
        {
            walk.Run.Status = ReviewRunStatus.Failed;
            walk.Run.WaitingOn = null;
            logger.LogWarning("Review run {RunId} failed at {NodeId}: {Reason}", walk.Run.Id, node.Id, reason);
        }

        await SaveAndPublishAsync(walk, ct);
        return result.FailureReason is null;
    }

    /// <summary>An another-round iteration's intake: re-reads the PR and pins its SHAs.</summary>
    private static async Task<NodeResult> IntakeAsync(Walk walk, ReviewNodeExecution execution, CancellationToken ct)
    {
        var snapshot = await walk.Services.GetRequiredService<ReviewIntake>().ReadAsync(walk.Run.Subject, ct);
        if (walk.Run.Subject.Host == ReviewHost.GitHub && snapshot.PullRequest.Title.Length > 0)
            walk.Run.Subject.Title = snapshot.PullRequest.Title;
        ReviewIntake.Apply(walk.Run.Subject, walk.Iteration, execution, snapshot, execution.StartedAt ?? DateTime.UtcNow);
        return new NodeResult(null, execution.Summary ?? "");
    }

    private static async Task<NodeResult> DeltaAsync(Walk walk, CancellationToken ct)
    {
        var scope = await walk.ScopeAsync(ct);
        return new NodeResult(scope.Range.IsIncremental ? "incremental" : "full", ReviewIntake.Summary(scope.Files, scope.Description, scope.Note));
    }

    /// <summary>
    /// One batched decision call (design 4.3). Later iterations only raise the blast radius and add
    /// categories: a small follow-up push does not shrink what the PR as a whole can break.
    /// </summary>
    private static async Task<NodeResult> CategoriseAsync(Walk walk, WorkflowNode node, CancellationToken ct)
    {
        var scope = await walk.ScopeAsync(ct);
        var host = scope.Binding.Host;
        var pr = await host.GetPullRequestAsync(scope.Binding.Target, ct);
        string? diff = null;
        string? diffNote = null;
        try
        {
            diff = await host.GetDiffAsync(scope.Binding.Target, scope.Range, ct);
        }
        catch (CodeHostException ex) when (ex.Kind == CodeHostFailureKind.TooLarge)
        {
            diffNote = "The code host will not render this diff (too large); judge from the file list.";
        }

        var state = ReviewCategorisation.BuildState(walk.Run.Subject.Title, pr.Description, scope.Description, scope.Files, diff, diffNote);
        var chain = node.StringParam("chain") is { Length: > 0 } named ? named : ReviewCategorisation.DefaultChain;
        var decided = await walk.Services.GetRequiredService<DecisionChain>()
            .DecideAsync(chain, new DecisionRequest(state, ReviewCategorisation.Questions), ct);
        var usage = NodeUsage.From(decided);
        var outcome = ReviewCategorisation.Read(decided.Answers, out var unanswered);
        if (outcome is null)
            return NodeResult.Fail($"Chain '{chain}' gave no answer for {string.Join(", ", unanswered)} (refused by every entry).", usage);

        var run = walk.Run;
        run.BlastReach = Raise(run.BlastReach, outcome.Reach);
        run.BlastExternalContract = Raise(run.BlastExternalContract, outcome.ExternalContract);
        run.BlastData = Raise(run.BlastData, outcome.Data);
        run.BlastRollbackAtRelease = Raise(run.BlastRollbackAtRelease, outcome.RollbackAtRelease);
        run.BlastReversalAfterMonths = Raise(run.BlastReversalAfterMonths, outcome.ReversalAfterMonths);
        run.BlastExposure = Raise(run.BlastExposure, outcome.Exposure);
        run.Categories = run.Categories.Union(outcome.Categories, StringComparer.Ordinal).ToList();
        var others = decided.Calls.Where(c => c.Outcome == "ok" && c.Provider != usage.Provider).Select(c => c.Provider).Distinct().ToList();
        var summary = ReviewCategorisation.Describe(outcome) + (others.Count > 0 ? $"; escalated to {string.Join(", ", others)}" : "");
        return new NodeResult(outcome.ChangeKind, summary, usage);
    }

    private static int Raise(int? current, int value) => current is { } c ? Math.Max(c, value) : value;

    /// <summary>The stop point: a Pending execution of <see cref="StopBeforeNode"/>, stage Select, status Running.</summary>
    private async Task ParkAsync(Walk walk, CancellationToken ct)
    {
        var execution = walk.Execution(StopBeforeNode);
        if (execution is null)
        {
            execution = new ReviewNodeExecution
            {
                Id = Guid.NewGuid(),
                RunId = walk.Run.Id,
                NodeId = StopBeforeNode,
                Iteration = walk.Iteration.Iteration,
                Kind = walk.Workflow.Node(StopBeforeNode)?.Kind ?? ReviewNodeKind.Decision,
            };
            walk.Db.ReviewNodeExecutions.Add(execution);
        }

        execution.Status = ReviewNodeStatus.Pending;
        execution.Summary = StopSummary;
        walk.Run.Status = ReviewRunStatus.Running;
        walk.Run.Stage = ReviewStage.Select;
        walk.Run.WaitingOn = ReviewWaitingOn.Bot;
        walk.Run.UpdatedAt = Now();
        await SaveAndPublishAsync(walk, ct);
    }

    /// <summary>
    /// True when the run moved on under this walk: a newer iteration (a Refresh saw a push) or a
    /// status this executor did not set. The running node is marked Skipped and the walk ends.
    /// </summary>
    private async Task<bool> SupersededAsync(Walk walk, CancellationToken ct)
    {
        var current = await walk.Db.ReviewRuns.AsNoTracking().Where(r => r.Id == walk.Run.Id)
            .Select(r => new { r.Status, Latest = r.Iterations.Max(i => i.Iteration) })
            .SingleAsync(ct);
        if (current.Latest <= walk.Iteration.Iteration && current.Status == ReviewRunStatus.Running)
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

            // Only the executions are saved: the run row now belongs to whoever moved it on.
            foreach (var entry in walk.Db.ChangeTracker.Entries<ReviewRun>())
                entry.State = EntityState.Unchanged;
            await walk.Db.SaveChangesAsync(ct);
            await PublishAsync(walk, ct);
        }

        return true;
    }

    /// <summary>On shutdown, hands the node and run back (Pending, Queued) so the next start resumes at once.</summary>
    private async Task ReleaseAsync(Walk walk, ReviewNodeExecution execution)
    {
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            execution.Status = ReviewNodeStatus.Pending;
            execution.StartedAt = null;
            walk.Run.Status = ReviewRunStatus.Queued;
            await walk.Db.SaveChangesAsync(budget.Token);
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

    /// <summary>The node after <paramref name="nodeId"/> on its unlabelled edge (the straight path of design 3.3).</summary>
    private static string Next(WorkflowDefinition workflow, string nodeId) =>
        workflow.Edges.FirstOrDefault(e => e.From == nodeId && e.Labels.Count == 0)?.To
        ?? throw new InvalidOperationException($"Node '{nodeId}' of workflow '{workflow.Name}' has no unlabelled outgoing edge.");

    private static ReviewStage StageOf(WorkflowNode node, ReviewStage current) => node.Run switch
    {
        "Intake" or "Delta" => ReviewStage.Intake,
        "Categorise" => ReviewStage.Categorise,
        "SelectChecks" or "GroupChecks" or "BuildPacks" => ReviewStage.Select,
        _ => current,
    };

    private static async Task SaveAndPublishAsync(Walk walk, CancellationToken ct)
    {
        await walk.Db.SaveChangesAsync(ct);
        await PublishAsync(walk, ct);
    }

    private static Task PublishAsync(Walk walk, CancellationToken ct) =>
        walk.Services.GetRequiredService<IEventBus>()
            .PublishToAllAsync(ReviewService.RunChangedEvent, new ReviewRunChangedEvent(walk.Run.Id), ct);

    private DateTime Now() => time.GetUtcNow().UtcDateTime;

    /// <summary>A node's result: its outcome label and summary, or the reason it failed; and any model usage.</summary>
    private sealed record NodeResult(string? Outcome, string Summary, NodeUsage? Usage = null, string? FailureReason = null)
    {
        public static NodeResult Fail(string reason, NodeUsage? usage = null) => new(null, "", usage, reason);
    }

    /// <summary>
    /// What a decision node records: the first entry that answered (provider, resolved model,
    /// transport, cost basis), and the tokens and cost of every call the chain made.
    /// </summary>
    private sealed record NodeUsage(string? Provider, string? Model, ReviewTransport? Transport, long InputTokens, long OutputTokens,
        decimal CostUsd, string CostBasis)
    {
        public static NodeUsage From(DecisionChainResult result)
        {
            var first = result.Calls.FirstOrDefault(c => c.Outcome == "ok");
            ReviewTransport? transport = first is not null && Enum.TryParse<ReviewTransport>(first.Transport, ignoreCase: true, out var parsed)
                ? parsed
                : null;
            return new NodeUsage(first?.Provider, first?.ResolvedModel, transport, result.Calls.Sum(c => c.InputTokens),
                result.Calls.Sum(c => c.OutputTokens), result.CostUsd, first?.CostBasis ?? "actual");
        }
    }

    /// <summary>The change a walk reviews, from delta: the binding, the range, its files and how it was chosen.</summary>
    private sealed record DeltaScope(CodeHostBinding Binding, CodeHostRange Range, IReadOnlyList<CodeHostChangedFile> Files, string Description,
        string? Note);

    /// <summary>One run's walk: its scope, context and tracked rows.</summary>
    private sealed class Walk(IServiceProvider services, AppDbContext db, ReviewRun run, ReviewIteration iteration, WorkflowDefinition workflow)
    {
        private DeltaScope? _scope;

        public IServiceProvider Services { get; } = services;
        public AppDbContext Db { get; } = db;
        public ReviewRun Run { get; } = run;
        public ReviewIteration Iteration { get; } = iteration;
        public WorkflowDefinition Workflow { get; } = workflow;

        public ReviewNodeExecution? Execution(string nodeId) =>
            Run.Nodes.FirstOrDefault(n => n.NodeId == nodeId && n.Iteration == Iteration.Iteration && n.ItemKey == null);

        /// <summary>
        /// Delta (design 4.2): the whole change on the first iteration, else the change since the last
        /// iteration's pinned head. An unreachable earlier head (force push) or an unchanged head falls
        /// back to the whole change and says so. Interdiffs by patch id, re-anchoring and the drift
        /// check come later. Computed once per walk, again after a restart.
        /// </summary>
        public async Task<DeltaScope> ScopeAsync(CancellationToken ct)
        {
            if (_scope is not null)
                return _scope;
            var binding = Services.GetRequiredService<CodeHostRouter>().Resolve(Run.Subject);
            var baseSha = Iteration.BaseSha ?? Run.Subject.BaseSha;
            var headSha = Iteration.HeadSha ?? Run.Subject.HeadSha;
            var since = Run.Iterations.Where(i => i.Iteration < Iteration.Iteration && i.HeadSha != null).MaxBy(i => i.Iteration)?.HeadSha;
            string? note = null;
            if (since is not null && !string.Equals(since, headSha, StringComparison.OrdinalIgnoreCase))
            {
                var incremental = new CodeHostRange(baseSha, headSha, since);
                try
                {
                    var changed = await binding.Host.ListChangedFilesAsync(binding.Target, incremental, ct);
                    return _scope = new DeltaScope(binding, incremental, changed, $"changes since {ReviewIntake.Short(since)}", null);
                }
                catch (CodeHostException ex) when (ex.Kind is CodeHostFailureKind.NotFound or CodeHostFailureKind.InvalidRequest)
                {
                    note = $"The last reviewed head {ReviewIntake.Short(since)} is not reachable; reviewing the whole change.";
                }
            }
            else if (since is not null)
            {
                note = "The head has not moved since the last iteration; reviewing the whole change.";
            }

            var whole = new CodeHostRange(baseSha, headSha);
            var files = await binding.Host.ListChangedFilesAsync(binding.Target, whole, ct);
            return _scope = new DeltaScope(binding, whole, files, "full review", note);
        }
    }
}
