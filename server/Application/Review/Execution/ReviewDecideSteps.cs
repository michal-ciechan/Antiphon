using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review.Workflows;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Review.Execution;

public sealed record RouteOutput(int Blocking, IReadOnlyList<string> BlockingKeys, int BotOnlyRounds, int Limit);

public sealed record ApprovalVerdict(bool Required, IReadOnlyList<string> Reasons, IReadOnlyList<string> NotChecked);

/// <summary>An approval: the SHA it covers and the base it was reviewed against (design 4.10).</summary>
public sealed record ApprovalRecord(string ApprovedSha, string? ReviewedBase, string By, IReadOnlyList<string> NotChecked);

public sealed record GateOutput(Guid GateId, IReadOnlyList<string> Reasons);

/// <summary>An optional gate passed without a person: <c>Review:Policy:GatesOff</c> lists <see cref="Repo"/> for it.</summary>
public sealed record GateSkipped(string NodeId, string Repo);

public sealed record WaitOutput(IReadOnlyList<string> Awaits);

public sealed record DriftRecord(string? From, string To);

/// <summary>The decide section (design 4.10, section 5): route, the approval question and approvals, gates and waits.</summary>
public static class ReviewDecideSteps
{
    /// <summary>Iteration triggers that start a round; a recheck or base re-review is the same round.</summary>
    public static readonly IReadOnlyList<string> RoundTriggers = ["opened", "manual", "push", "another-round"];
    private const string ApproveOutcome = ReviewGateRule.ApproveOutcome;

    /// <summary>
    /// Route (design 3.3, 5): counts this round as bot-only (a human response or decision resets the
    /// counter), then <c>findings</c> when an open finding still clears this round's bar, or
    /// <c>bot-round-limit</c> once the bot-only limit is reached; otherwise <c>clean</c>.
    /// </summary>
    public static async Task<NodeResult> RouteAsync(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution execution, CancellationToken ct)
    {
        var run = walk.Run;
        var policy = walk.Policy;
        var counted = walk.Run.Nodes.Any(n => n.NodeId == node.Id && n.Iteration == walk.Iteration.Iteration && n.Id != execution.Id
            && n.Status == ReviewNodeStatus.Succeeded);
        if (!counted && RoundTriggers.Contains(walk.Iteration.Trigger))
            run.BotOnlyRounds += 1;

        var findings = await walk.Db.ReviewFindings.Where(f => f.RunId == run.Id).ToListAsync(ct);
        var blocking = findings.Where(f => ReviewRoundsPolicy.IsBlocking(f, run.Round, policy)).OrderBy(f => f.CreatedAt).ToList();
        var outcome = ReviewRoundsPolicy.Route(blocking.Count, run.BotOnlyRounds, policy);
        var summary = $"Round {run.Round}: {blocking.Count} open findings above the bar"
            + (blocking.Count > 0 ? $" ({string.Join(", ", blocking.Select(f => $"{f.Key} {f.Severity.ToString().ToLowerInvariant()}"))})" : "")
            + $"; bot-only rounds {run.BotOnlyRounds}/{policy.HumanGateAfterBotOnlyRounds}";
        return NodeResult.Ok(outcome, summary,
            new RouteOutput(blocking.Count, blocking.Select(f => f.Key).ToList(), run.BotOnlyRounds, policy.HumanGateAfterBotOnlyRounds));
    }

    /// <summary>
    /// Needs human approval (design 4.10): yes when any configured rule fires, and a yes is sticky for
    /// the rest of the PR. The author-is-new rule needs author history from the code host and is not
    /// evaluated yet; it is listed as not checked.
    /// </summary>
    public static async Task<NodeResult> NeedsHumanApprovalAsync(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution execution, CancellationToken ct)
    {
        var run = walk.Run;
        var policy = walk.Policy;
        var reasons = new List<string>();
        if (run.NeedsHumanApproval == true)
            reasons.Add("an earlier round needed a human approval (sticky)");
        foreach (var category in run.Categories.Where(c => policy.EffectiveHumanRequiredCategories.Contains(c, StringComparer.OrdinalIgnoreCase)))
            reasons.Add($"risk category {category}");
        if (run.BlastExternalContract is { } contract && contract >= policy.HumanRequiredExternalContractAtLeast)
            reasons.Add($"blast radius: external contract {contract} >= {policy.HumanRequiredExternalContractAtLeast}");
        if (run.BlastReversalAfterMonths is { } reversal && reversal >= policy.HumanRequiredReversalAtLeast)
            reasons.Add($"blast radius: reversal after months {reversal} >= {policy.HumanRequiredReversalAtLeast}");

        var selected = await ReviewSelectionSteps.SelectedChecksAsync(walk, ct);
        foreach (var check in selected.Where(c => c.RequiresHuman))
            reasons.Add($"check {check.Id} requires a human");

        var files = await walk.Host.ListChangedFilesAsync(walk.Binding.Target, walk.WholeRange, ct);
        var size = ReviewIntake.SizeBucket(files.Sum(f => f.Additions + f.Deletions));
        if (policy.EffectiveLargeSizes.Contains(size, StringComparer.OrdinalIgnoreCase))
            reasons.Add($"the PR is large (size {size})");

        var conceded = await walk.Db.ReviewFindings.CountAsync(f => f.RunId == run.Id && f.Status == ReviewFindingStatus.Dismissed
            && f.Events.Any(e => e.Action == ReviewFindingAction.Dispute), ct);
        if (conceded >= policy.HumanRequiredConcededDisputesAtLeast)
            reasons.Add($"{conceded} disputes conceded");

        if (policy.HumanRequiredWhenUnverifiedHighSeverity)
        {
            var unverified = ReviewWalk.Output<CollateOutput>(walk.LatestOf("CollateFindings"))?.UnverifiedChecks ?? [];
            foreach (var check in selected.Where(c => c.Severity >= ReviewSeverity.Major && unverified.Contains(c.Id, StringComparer.OrdinalIgnoreCase)))
                reasons.Add($"high-severity check {check.Id} was marked unverified");
        }

        var required = reasons.Count > 0;
        run.NeedsHumanApproval = required || run.NeedsHumanApproval == true;
        var verdict = new ApprovalVerdict(required, reasons, ["author is new to the repo"]);
        return NodeResult.Ok(required ? "yes" : "no", required ? $"A human must approve: {string.Join("; ", reasons)}" : "No rule needs a human", verdict);
    }

    /// <summary>
    /// Bot approval (design 4.10): only with no open blocker (D-21), no open finding above the bar,
    /// every assigned check accounted for (the reviewer parse enforces it) and the head still the
    /// reviewed SHA. Posts the approval and <c>antiphon/approval</c> success. CI status and open human
    /// threads are not read from the code host yet; they are recorded as not checked.
    /// </summary>
    public static async Task<NodeResult> BotApproveAsync(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution execution, CancellationToken ct)
    {
        var run = walk.Run;
        var findings = await walk.Db.ReviewFindings.Where(f => f.RunId == run.Id).ToListAsync(ct);
        if (findings.FirstOrDefault(f => f.Severity == ReviewSeverity.Blocker && ReviewRoundsPolicy.OpenStatuses.Contains(f.Status)) is { } blocker)
            return NodeResult.Fail($"Bot approval refused: blocker {blocker.Key} is open (D-21).");
        if (findings.FirstOrDefault(f => ReviewRoundsPolicy.IsBlocking(f, run.Round, walk.Policy)) is { } open)
            return NodeResult.Fail($"Bot approval refused: finding {open.Key} is open above the bar.");

        var pr = await walk.Host.GetPullRequestAsync(walk.Binding.Target, ct);
        if (!string.Equals(pr.HeadSha, walk.HeadSha, StringComparison.OrdinalIgnoreCase))
            return NodeResult.Fail($"Bot approval refused: the head moved from the reviewed {ReviewIntake.Short(walk.HeadSha)} to {ReviewIntake.Short(pr.HeadSha)}; Refresh starts a new iteration.");

        var notChecked = new List<string> { "ci-green", "no-open-human-threads" };
        var approved = false;
        if (walk.Host.Capabilities.CanApprove && run.Subject.PrNumber is not null)
        {
            await walk.Host.ApproveAsync(walk.Binding.Target, walk.HeadSha,
                $"Approved by Antiphon: round {run.Round} is clean and no rule needs a human.", ct);
            approved = true;
        }

        await walk.SetStatusAsync(ReviewWalk.ApprovalStatusContext, CodeHostStatusState.Success, "Approved by Antiphon", ct);
        run.Status = ReviewRunStatus.Approved;
        return NodeResult.Ok(null,
            $"Approved {ReviewIntake.Short(walk.HeadSha)}{(approved ? "" : " (the code host takes no approvals)")}; not checked: {string.Join(", ", notChecked)}",
            new ApprovalRecord(walk.HeadSha, walk.Iteration.BaseSha, ReviewWalk.BotLogin, notChecked));
    }

    /// <summary>
    /// A gate (design 3.1, 11.3): opens a queue item with the reasons a human is needed and parks the
    /// run. The decision API completes the execution with the decision as its outcome and wakes the executor.
    /// </summary>
    public static async Task<NodeResult> GateAsync(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution execution, CancellationToken ct)
    {
        var repo = walk.Run.Subject.Repo;
        if (ReviewWorkflowPolicy.GatesOff(walk.Policy, node, repo).Value)
            return NodeResult.Ok(ApproveOutcome, $"Gate off for {repo} (Review:Policy:GatesOff:{node.Id}): passed without a person", new GateSkipped(node.Id, repo));

        var kind = GateKindOf(node);
        if (CanSkipMergeGate(kind) && (!walk.Host.Capabilities.CanMerge || walk.Run.Subject.PrNumber is null))
            return NodeResult.Ok(ApproveOutcome, "Nothing for Antiphon to merge here (no PR, or the code host cannot merge): passed, the merge is left to the author",
                new GateSkipped(node.Id, repo));

        var gate = await walk.Db.ReviewGates.FirstOrDefaultAsync(g => g.RunId == walk.Run.Id && g.NodeId == node.Id
            && g.Iteration == walk.Iteration.Iteration && g.Status != ReviewQueueItemStatus.Done, ct);
        IReadOnlyList<string> reasons = kind == ReviewGateKind.MergeGate
            ? ["approved: a person confirms the merge here, or merges on the code host"]
            : GateReasons(walk, execution);
        if (gate is null)
        {
            gate = new ReviewGate
            {
                Id = Guid.NewGuid(),
                RunId = walk.Run.Id,
                Kind = kind,
                NodeId = node.Id,
                Iteration = walk.Iteration.Iteration,
                Status = ReviewQueueItemStatus.Unassigned,
                TeamKey = walk.Run.Subject.OwnerTeams.FirstOrDefault(),
                OpenedAt = walk.Now(),
                Reasons = reasons.ToList(),
            };
            walk.Db.ReviewGates.Add(gate);
        }

        var team = gate.TeamKey is { } t ? $" ({t})" : "";
        // A merge gate opens after the approval, so antiphon/approval stays the success it is.
        if (kind != ReviewGateKind.MergeGate)
            await walk.SetStatusAsync(ReviewWalk.ApprovalStatusContext, CodeHostStatusState.Pending,
                kind == ReviewGateKind.ApprovalGate ? $"Waiting on human approval{team}" : $"Waiting on a human review{team}", ct);
        return NodeResult.Wait($"Waiting on a human decision{team}: {string.Join("; ", reasons)}",
            new ParkState(ReviewRunStatus.AtGate, ReviewStage.HumanGate, ReviewWaitingOn.Humans), new GateOutput(gate.Id, reasons));
    }

    /// <summary>The gate's kind: its <c>gateKind</c> param, else an approval gate when its id says approve.</summary>
    public static ReviewGateKind GateKindOf(WorkflowNode node) =>
        node.StringParam("gateKind") is { } named && Enum.TryParse<ReviewGateKind>(named, ignoreCase: true, out var parsed)
            ? parsed
            : node.Id.Contains("approve", StringComparison.OrdinalIgnoreCase) ? ReviewGateKind.ApprovalGate : ReviewGateKind.ReviewGate;

    /// <summary>A gate the workflow marks <c>optional: true</c>, which <c>Review:Policy:GatesOff</c> may switch off per repo.</summary>
    public static bool IsOptional(WorkflowNode node) =>
        string.Equals(node.StringParam("optional"), "true", StringComparison.OrdinalIgnoreCase);

    private static bool CanSkipMergeGate(ReviewGateKind kind) => kind == ReviewGateKind.MergeGate;

    internal static IReadOnlyList<string> GateOutcomes(WorkflowNode node) =>
        IsOptional(node) || CanSkipMergeGate(GateKindOf(node)) ? [.. node.Decisions, ApproveOutcome] : node.Decisions;

    internal static IReadOnlyList<string> GateEdgeOutcomes(WorkflowNode node) => GateOutcomes(node)
        .Where(outcome => ReviewGateRule.Find(outcome)?.Transition == ReviewGateTransition.FollowEdge).ToArray();

    internal static IReadOnlyList<string> WaitOutcomes(WorkflowNode node) => node.ListParam("on");

    /// <summary>
    /// A wait (design 3.1): parks the run, with no seat held, on the events in <c>on</c>. Refresh turns
    /// code-host state into those events and completes the execution with the event as its outcome.
    /// </summary>
    public static Task<NodeResult> WaitAsync(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution execution, CancellationToken ct)
    {
        var awaits = WaitOutcomes(node);
        var park = awaits.Contains("merge")
            ? new ParkState(ReviewRunStatus.AwaitingMerge, ReviewStage.BranchMaintenance, ReviewWaitingOn.Author)
            : new ParkState(ReviewRunStatus.AwaitingFixes, ReviewStage.AwaitingFixes, ReviewWaitingOn.Author);
        return Task.FromResult(NodeResult.Wait($"Waiting for {string.Join(", ", awaits)}", park, new WaitOutput(awaits)));
    }

    /// <summary>
    /// Record drift (D-19): the base moved while fixes are awaited. Only the move is recorded: no
    /// model call, no re-review and no branch update.
    /// </summary>
    public static async Task<NodeResult> RecordBaseDriftAsync(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution execution, CancellationToken ct)
    {
        var state = await walk.Host.GetBranchStateAsync(walk.Binding.Target, walk.Run.Subject.BaseRef, walk.HeadSha, ct);
        var from = walk.Run.BaseTipSha;
        walk.Run.BaseTipSha = state.BaseTipSha;
        return NodeResult.Ok(null,
            $"The base moved {ReviewIntake.Short(from)} -> {ReviewIntake.Short(state.BaseTipSha)} while fixes are awaited; recorded only (D-19)",
            new DriftRecord(from, state.BaseTipSha));
    }

    /// <summary>The executor's side effect of a gate's approve decision: <c>antiphon/approval</c> success at the head, once.</summary>
    public static async Task ApplyGateApprovalAsync(ReviewWalk walk, ReviewNodeExecution execution, CancellationToken ct)
    {
        if (ReviewWalk.Output<ApprovalRecord>(execution) is { ApprovedSha.Length: > 0 })
            return;
        var gate = await walk.Db.ReviewGates.AsNoTracking()
            .Where(g => g.RunId == walk.Run.Id && g.NodeId == execution.NodeId && g.Iteration == execution.Iteration && g.Decision == ReviewGateDecision.Approve)
            .OrderByDescending(g => g.DecidedAt).FirstOrDefaultAsync(ct);
        var by = gate?.DecidedBy ?? "a human";
        await walk.SetStatusAsync(ReviewWalk.ApprovalStatusContext, CodeHostStatusState.Success, $"Approved by {by}", ct);
        walk.Run.Status = ReviewRunStatus.Approved;
        execution.OutputJson = ReviewWalk.ToJson(new ApprovalRecord(walk.HeadSha, walk.Iteration.BaseSha, by, []));
    }

    private static IReadOnlyList<string> GateReasons(ReviewWalk walk, ReviewNodeExecution execution)
    {
        var reasons = new List<string>(ReviewWalk.Output<GateOutput>(execution)?.Reasons ?? []);
        var before = walk.Before(execution);
        if (before is not null && walk.Workflow.Node(before.NodeId)?.Run == "NeedsHumanApproval" && ReviewWalk.Output<ApprovalVerdict>(before) is { } verdict)
            reasons.AddRange(verdict.Reasons);
        else if (before?.Outcome == ReviewRoundsPolicy.RouteBotRoundLimit)
            reasons.Add($"{walk.Run.BotOnlyRounds} bot-only rounds without a human (limit {walk.Policy.HumanGateAfterBotOnlyRounds})");
        if (reasons.Count == 0)
            reasons.Add("the workflow routes here");
        return reasons.Distinct().ToList();
    }
}
