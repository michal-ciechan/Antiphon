using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review.Checks;
using Antiphon.Server.Application.Review.Decisions;
using Antiphon.Server.Application.Review.Workflows;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Review.Execution;

public enum NodeResultKind
{
    Succeeded,
    Failed,

    /// <summary>A gate or wait parked the run; an event or decision completes it.</summary>
    Waiting,

    /// <summary>The node did nothing by design (verify with no chain) and the walk goes on.</summary>
    Skipped,

    /// <summary>A fan-out child is still running elsewhere: the run is queued and polled on the next pass.</summary>
    Polling,
}

/// <summary>How a parked run reads while it waits (design 4.11 and api.md statuses).</summary>
public sealed record ParkState(ReviewRunStatus Status, ReviewStage Stage, ReviewWaitingOn WaitingOn);

/// <summary>A node's result: its outcome (the edge label), summary, outputs and any model usage.</summary>
public sealed record NodeResult(NodeResultKind Kind, string? Outcome, string Summary, object? Output = null, NodeUsage? Usage = null,
    ParkState? Park = null)
{
    /// <summary>
    /// Set when the step failed but its failure policy let it fall back (select includes every
    /// candidate, verify passes findings through): the execution shows failed-continued with this reason.
    /// </summary>
    public string? Degraded { get; init; }

    /// <summary>This result as the fallback of a step that failed with <paramref name="reason"/> and may continue.</summary>
    public NodeResult AsDegraded(string reason) => this with { Kind = NodeResultKind.Succeeded, Degraded = reason };

    public static NodeResult Ok(string? outcome, string summary, object? output = null, NodeUsage? usage = null) =>
        new(NodeResultKind.Succeeded, outcome, summary, output, usage);

    public static NodeResult Fail(string reason, NodeUsage? usage = null, object? output = null) =>
        new(NodeResultKind.Failed, null, reason, output, usage);

    public static NodeResult Wait(string summary, ParkState park, object? output = null) => new(NodeResultKind.Waiting, null, summary, output, Park: park);

    public static NodeResult Skip(string summary, object? output = null) => new(NodeResultKind.Skipped, null, summary, output);

    public static NodeResult Poll(string summary) => new(NodeResultKind.Polling, null, summary);
}

/// <summary>
/// What a model-backed node records: the first entry that answered (provider, resolved model,
/// transport, cost basis), and the tokens and cost of every call it made.
/// </summary>
public sealed record NodeUsage(string? Provider, string? Model, ReviewTransport? Transport, long InputTokens, long OutputTokens,
    decimal CostUsd, string CostBasis)
{
    public static NodeUsage From(IReadOnlyList<DecisionCall> calls, decimal cost)
    {
        var first = calls.FirstOrDefault(c => c.Outcome == "ok");
        ReviewTransport? transport = first is not null && Enum.TryParse<ReviewTransport>(first.Transport, ignoreCase: true, out var parsed)
            ? parsed
            : null;
        return new NodeUsage(first?.Provider, first?.ResolvedModel, transport, calls.Sum(c => c.InputTokens), calls.Sum(c => c.OutputTokens),
            cost, first?.CostBasis ?? "actual");
    }

    public static NodeUsage From(DecisionChainResult result) => From(result.Calls, result.CostUsd);

    public static NodeUsage? Add(NodeUsage? a, NodeUsage? b) => (a, b) switch
    {
        (null, _) => b,
        (_, null) => a,
        _ => a with
        {
            Provider = a.Provider ?? b.Provider,
            Model = a.Model ?? b.Model,
            Transport = a.Transport ?? b.Transport,
            InputTokens = a.InputTokens + b.InputTokens,
            OutputTokens = a.OutputTokens + b.OutputTokens,
            CostUsd = a.CostUsd + b.CostUsd,
        },
    };
}

/// <summary>The change a walk reviews, from delta: the binding, the range, its files and how it was chosen.</summary>
public sealed record DeltaScope(CodeHostBinding Binding, CodeHostRange Range, IReadOnlyList<CodeHostChangedFile> Files, string Description,
    string? Note);

/// <summary>
/// One run's walk: its tracked rows, the iteration being walked, and per-walk caches (the delta
/// scope, the diffs, the check catalogue). Node steps read and write through it.
/// </summary>
public sealed class ReviewWalk(IServiceProvider services, AppDbContext db, ReviewRun run, ReviewIteration iteration, WorkflowDefinition workflow,
    TimeProvider time)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public const string EntryRun = "Intake";
    public const string BotLogin = "antiphon[bot]";

    private DeltaScope? _scope;
    private CodeHostBinding? _binding;
    private ReviewCheckSet? _checks;
    private readonly Dictionary<string, string?> _diffs = new(StringComparer.Ordinal);

    public IServiceProvider Services { get; } = services;
    public AppDbContext Db { get; } = db;
    public ReviewRun Run { get; } = run;
    public ReviewIteration Iteration { get; private set; } = iteration;
    public WorkflowDefinition Workflow { get; } = workflow;
    public ReviewPolicySettings Policy => Services.GetRequiredService<IOptions<ReviewPolicySettings>>().Value;

    public DateTime Now() => time.GetUtcNow().UtcDateTime;

    /// <summary>Problems this node noted without failing (a refused commit status); the executor adds them to its summary.</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>
    /// Whether a failure of <paramref name="node"/> lets the run go on: <c>Review:Policy:OnFailure:&lt;node&gt;</c>
    /// when set, else the workflow's <c>onFailure</c> param; the default is stop.
    /// </summary>
    public bool ContinuesOnFailure(WorkflowNode node) => OnFailure(Policy, node) == "continue";

    public static string OnFailure(ReviewPolicySettings policy, WorkflowNode node) =>
        ReviewWorkflowPolicy.OnFailure(policy, node).Value;

    public string EntryNodeId => Workflow.Nodes.FirstOrDefault(n => n.Run == EntryRun)?.Id ?? Workflow.Nodes[0].Id;

    public CodeHostBinding Binding => _binding ??= Services.GetRequiredService<CodeHostRouter>().Resolve(Run.Subject);

    public ICodeHost Host => Binding.Host;

    /// <summary>The head the run accepts now: the pinned head, moved forward by a base-only branch update.</summary>
    public string HeadSha => Run.Subject.HeadSha.Length > 0 ? Run.Subject.HeadSha : Iteration.HeadSha ?? "";

    public void MoveTo(ReviewIteration next)
    {
        Iteration = next;
        _scope = null;
        _diffs.Clear();
    }

    /// <summary>The latest top-level execution of the iteration: where the walk stands.</summary>
    public ReviewNodeExecution? Frontier() => Run.Nodes
        .Where(n => n.Iteration == Iteration.Iteration && n.ItemKey == null)
        .OrderByDescending(n => n.Sequence)
        .ThenByDescending(n => n.StartedAt ?? DateTime.MaxValue)
        .FirstOrDefault();

    /// <summary>The latest execution of <paramref name="nodeId"/> in the iteration (top-level unless <paramref name="itemKey"/> is given).</summary>
    public ReviewNodeExecution? Latest(string nodeId, string? itemKey = null, int? iteration = null) => Run.Nodes
        .Where(n => n.NodeId == nodeId && n.Iteration == (iteration ?? Iteration.Iteration) && n.ItemKey == itemKey)
        .MaxBy(n => n.Sequence);

    /// <summary>The latest top-level execution in the iteration of any node whose yaml <c>run</c> is <paramref name="run"/>.</summary>
    public ReviewNodeExecution? LatestOf(string run) => Workflow.Nodes.Where(n => n.Run == run)
        .Select(n => Latest(n.Id)).OfType<ReviewNodeExecution>().MaxBy(n => n.Sequence);

    /// <summary>The node ids whose yaml <c>run</c> is <paramref name="run"/>.</summary>
    public IReadOnlyList<string> NodesOf(string run) => Workflow.Nodes.Where(n => n.Run == run).Select(n => n.Id).ToList();

    /// <summary>The latest top-level execution in any iteration of a node whose yaml <c>run</c> is <paramref name="run"/>.</summary>
    public ReviewNodeExecution? LatestAnywhereOf(string run, Func<ReviewNodeExecution, bool>? where = null)
    {
        var ids = NodesOf(run);
        return Run.Nodes.Where(n => ids.Contains(n.NodeId) && n.ItemKey == null && (where is null || where(n)))
            .OrderBy(n => n.Iteration).ThenBy(n => n.Sequence)
            .LastOrDefault();
    }

    /// <summary>The top-level execution created just before <paramref name="execution"/> in its iteration.</summary>
    public ReviewNodeExecution? Before(ReviewNodeExecution execution) => Run.Nodes
        .Where(n => n.Iteration == execution.Iteration && n.ItemKey == null && n.Sequence < execution.Sequence)
        .MaxBy(n => n.Sequence);

    public int NextSequence() => Run.Nodes.Select(n => n.Sequence).DefaultIfEmpty(0).Max() + 1;

    /// <summary>A new Pending execution, added through the context (see <c>ReviewService.Apply</c> on client-set keys).</summary>
    public ReviewNodeExecution NewExecution(string nodeId, ReviewNodeKind kind, string? itemKey = null)
    {
        var execution = new ReviewNodeExecution
        {
            Id = Guid.NewGuid(),
            RunId = Run.Id,
            NodeId = nodeId,
            Iteration = Iteration.Iteration,
            Sequence = NextSequence(),
            Kind = kind,
            ItemKey = itemKey,
            Status = ReviewNodeStatus.Pending,
        };
        Db.ReviewNodeExecutions.Add(execution);
        if (!Run.Nodes.Contains(execution))
            Run.Nodes.Add(execution);
        return execution;
    }

    public static string ToJson(object value) => JsonSerializer.Serialize(value, Json);

    public static T? Output<T>(ReviewNodeExecution? execution) where T : class =>
        execution?.OutputJson is { Length: > 0 } json ? JsonSerializer.Deserialize<T>(json, Json) : null;

    /// <summary>The iteration's checks, loaded once per walk at the base SHA (design 9, D-8).</summary>
    public async Task<ReviewCheckSet> ChecksAsync(CancellationToken ct) =>
        _checks ??= await Services.GetRequiredService<ReviewCheckCatalogue>().LoadAsync(Binding, Iteration.BaseSha ?? Run.Subject.BaseSha, ct);

    /// <summary>The unified diff over <paramref name="range"/>, or null when the host will not render it (too large).</summary>
    public async Task<string?> DiffAsync(CodeHostRange range, CancellationToken ct)
    {
        var key = $"{range.BaseSha}|{range.HeadSha}|{range.SinceSha}";
        if (_diffs.TryGetValue(key, out var cached))
            return cached;
        try
        {
            return _diffs[key] = await Host.GetDiffAsync(Binding.Target, range, ct);
        }
        catch (CodeHostException ex) when (ex.Kind == CodeHostFailureKind.TooLarge)
        {
            return _diffs[key] = null;
        }
    }

    /// <summary>The whole change this iteration reviews: base to head.</summary>
    public CodeHostRange WholeRange => new(Iteration.BaseSha ?? Run.Subject.BaseSha, Iteration.HeadSha ?? Run.Subject.HeadSha);

    /// <summary>
    /// Sets a commit status on <paramref name="sha"/> (the accepted head by default) when the host
    /// supports statuses (design 4.11); a host without them is skipped.
    /// </summary>
    public async Task<bool> SetStatusAsync(string context, CodeHostStatusState state, string description, CancellationToken ct, string? sha = null)
    {
        if (!Host.Capabilities.SupportsCommitStatuses || Run.Subject.Host != ReviewHost.GitHub)
            return false;
        var root = Services.GetRequiredService<IOptions<ReviewSettings>>().Value.PublicBaseUrl;
        var link = string.IsNullOrWhiteSpace(root) ? null : $"{root.TrimEnd('/')}/reviews/{Run.Id}";
        try
        {
            await Host.SetCommitStatusAsync(Binding.Target, sha ?? HeadSha, new CodeHostCommitStatus(context, state, description, link), ct);
            return true;
        }
        catch (CodeHostException ex) when (!Policy.StatusesRequired)
        {
            // A status is a courtesy to the PR; Review:Policy:StatusesRequired makes a refused one fail the step.
            Warnings.Add($"status {context} not posted: {ex.Message}");
            return false;
        }
    }

    public const string ReviewStatusContext = "antiphon/review";
    public const string ApprovalStatusContext = "antiphon/approval";

    /// <summary>
    /// Delta (design 4.2): the whole change on the first iteration, else the change since the last
    /// iteration's pinned head. An unreachable earlier head (force push) or an unchanged head falls
    /// back to the whole change and says so. Interdiffs by patch id and re-anchoring come later.
    /// Computed once per walk, again after a restart.
    /// </summary>
    public async Task<DeltaScope> ScopeAsync(CancellationToken ct)
    {
        if (_scope is not null)
            return _scope;
        var binding = Binding;
        var baseSha = Iteration.BaseSha ?? Run.Subject.BaseSha;
        var headSha = Iteration.HeadSha ?? Run.Subject.HeadSha;
        var previous = Run.Iterations.Where(i => i.Iteration < Iteration.Iteration && i.HeadSha != null).MaxBy(i => i.Iteration);
        var since = previous?.HeadSha;
        string? note = null;
        // After a rebase or a merge of the base, since...head also spans the base's own commits (live,
        // 2026-11-23: a 5-file PR rebased onto master came back as 58 files). Until interdiffs by patch
        // id exist, a moved base reviews the whole change again.
        if (since is not null && previous?.BaseSha is { Length: > 0 } previousBase && !string.Equals(previousBase, baseSha, StringComparison.OrdinalIgnoreCase))
        {
            note = $"The base moved ({ReviewIntake.Short(previousBase)} -> {ReviewIntake.Short(baseSha)}), so the change since "
                + $"{ReviewIntake.Short(since)} would include the base's own commits; reviewing the whole change.";
        }
        else if (since is not null && !string.Equals(since, headSha, StringComparison.OrdinalIgnoreCase))
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
