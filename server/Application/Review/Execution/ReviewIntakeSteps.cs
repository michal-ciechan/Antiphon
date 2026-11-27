using System.Text;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review.Decisions;
using Antiphon.Server.Application.Review.Workflows;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Server.Application.Review.Execution;

/// <summary>The intake section's code and decision nodes: intake, delta and categorise (design 4.1 to 4.3).</summary>
public static class ReviewIntakeSteps
{
    /// <summary>A yes/no <c>still_present</c> answer below this marks the finding fixed (design 4.2 step 3).</summary>
    public const double StillPresentThreshold = 0.5;

    /// <summary>An another-round or executor-started iteration's intake: re-reads the PR and pins its SHAs.</summary>
    public static async Task<NodeResult> IntakeAsync(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution execution, CancellationToken ct)
    {
        var snapshot = await walk.Services.GetRequiredService<ReviewIntake>().ReadAsync(walk.Run, ct);
        if (walk.Run.Subject.Host == ReviewHost.GitHub && snapshot.PullRequest.Title.Length > 0)
            walk.Run.Subject.Title = snapshot.PullRequest.Title;
        ReviewIntake.Apply(walk.Run.Subject, walk.Iteration, execution, snapshot, execution.StartedAt ?? walk.Now());
        walk.Run.BaseTipSha = snapshot.PullRequest.BaseSha;
        return NodeResult.Ok(null, execution.Summary ?? "");
    }

    /// <summary>
    /// Delta (design 4.2): picks the scope, posts the pickup status, and on a recheck asks
    /// <c>still_present</c> once per open finding whose file changed (one batched call). A finding whose
    /// file was deleted is outdated; one no longer present is fixed, with a bot event that syncs a reply.
    /// </summary>
    public static async Task<NodeResult> DeltaAsync(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution execution, CancellationToken ct)
    {
        await walk.SetStatusAsync(ReviewWalk.ReviewStatusContext, CodeHostStatusState.Pending,
            $"Picked up by Antiphon (round {walk.Run.Round}, iteration {walk.Iteration.Iteration})", ct);
        var scope = await walk.ScopeAsync(ct);
        var summary = new StringBuilder(ReviewIntake.Summary(scope.Files, scope.Description, scope.Note));
        NodeUsage? usage = null;
        var output = new Dictionary<string, object?> { ["range"] = scope.Range, ["description"] = scope.Description };

        if (walk.LatestAnywhereOf("RecordBaseDrift", n => n.Status == ReviewNodeStatus.Succeeded) is { } drift
            && drift.Iteration == PreviousIteration(walk))
        {
            summary.Append("\nA base drift was recorded while fixes were awaited; the drift re-check in delta comes later (design 4.2 step 1).");
        }

        if (scope.Range.IsIncremental)
        {
            var changed = scope.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
            var open = await OpenFindingsAsync(walk, ct);
            var touched = open.Where(f => changed.ContainsKey(f.FilePath)).ToList();
            var outdated = touched.Where(f => changed[f.FilePath].Status == CodeHostFileStatus.Deleted).ToList();
            foreach (var finding in outdated)
            {
                finding.Status = ReviewFindingStatus.Outdated;
                finding.UpdatedAt = walk.Now();
            }

            var asked = touched.Except(outdated).ToList();
            var fixedKeys = new List<string>();
            if (asked.Count > 0)
            {
                var diff = await walk.DiffAsync(scope.Range, ct);
                var questions = asked.Select(f => DecisionQuestion.Noul(StillPresentName(f.Key),
                    $"Is finding {f.Key} ({f.Title}, at {f.FilePath}: `{f.Anchor.Trim()}`) still present after the change below?")).ToList();
                var state = StillPresentState(asked, diff);
                var decided = await walk.Services.GetRequiredService<DecisionChain>()
                    .DecideAsync(node.StringParam("chain") ?? ReviewCategorisation.DefaultChain, new DecisionRequest(state, questions), ct);
                usage = NodeUsage.From(decided);
                foreach (var finding in asked)
                {
                    if (!decided.Answers.TryGetValue(StillPresentName(finding.Key), out var answer) || answer.Refused
                        || YesProbability(answer) >= StillPresentThreshold)
                        continue;
                    finding.Status = ReviewFindingStatus.Fixed;
                    finding.UpdatedAt = walk.Now();
                    AddBotEvent(walk, finding, ReviewFindingAction.Fixed,
                        $"No longer present at {ReviewIntake.Short(walk.Iteration.HeadSha)} (still_present p={YesProbability(answer):0.00}).");
                    fixedKeys.Add(finding.Key);
                }
            }

            summary.Append($"\nOpen findings: {open.Count}; rechecked {asked.Count}, fixed {fixedKeys.Count}, outdated {outdated.Count}.");
            output["rechecked"] = asked.Select(f => f.Key).ToList();
            output["fixed"] = fixedKeys;
            output["outdated"] = outdated.Select(f => f.Key).ToList();
        }

        return NodeResult.Ok(scope.Range.IsIncremental ? "incremental" : "full", summary.ToString(), output, usage);
    }

    /// <summary>One batched decision call (design 4.3); later iterations only raise the blast radius and add categories.</summary>
    public static async Task<NodeResult> CategoriseAsync(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution execution, CancellationToken ct)
    {
        var scope = await walk.ScopeAsync(ct);
        var pr = await walk.Host.GetPullRequestAsync(walk.Binding.Target, ct);
        var diff = await walk.DiffAsync(scope.Range, ct);
        var diffNote = diff is null ? "The code host will not render this diff (too large); judge from the file list." : null;
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
        await walk.SetStatusAsync(ReviewWalk.ReviewStatusContext, CodeHostStatusState.Pending, $"Categorised: {outcome.ChangeKind}", ct);
        return NodeResult.Ok(outcome.ChangeKind, summary, outcome, usage);
    }

    public static string StillPresentName(string key) => $"still_present_{key.ToLowerInvariant()}";

    public static double YesProbability(DecisionAnswer answer) =>
        answer.Probabilities is { } p && p.TryGetValue("yes", out var yes) ? yes : answer.Value == "yes" ? answer.Confidence ?? 1 : 0;

    /// <summary>A bot-authored finding event; it syncs to the thread like a human response.</summary>
    public static void AddBotEvent(ReviewWalk walk, ReviewFinding finding, ReviewFindingAction action, string text)
    {
        var evt = new ReviewFindingEvent
        {
            Id = Guid.NewGuid(),
            FindingId = finding.Id,
            Action = action,
            ActorLogin = ReviewWalk.BotLogin,
            ActorDisplayName = "Antiphon",
            Text = text,
            SyncState = ReviewSyncState.Pending,
            CreatedAt = walk.Now(),
        };
        walk.Db.ReviewFindingEvents.Add(evt);
        finding.SyncState = ReviewSyncState.Pending;
    }

    /// <summary>The run's open findings, tracked.</summary>
    public static async Task<List<ReviewFinding>> OpenFindingsAsync(ReviewWalk walk, CancellationToken ct)
    {
        var open = ReviewRoundsPolicy.OpenStatuses.ToList();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            walk.Db.ReviewFindings.Where(f => f.RunId == walk.Run.Id && open.Contains(f.Status)).OrderBy(f => f.CreatedAt), ct);
    }

    private static int? PreviousIteration(ReviewWalk walk) =>
        walk.Run.Iterations.Where(i => i.Iteration < walk.Iteration.Iteration).Select(i => (int?)i.Iteration).Max();

    private static string StillPresentState(IReadOnlyList<ReviewFinding> findings, string? diff)
    {
        var state = new StringBuilder("Open review findings, each anchored on one line of code:\n");
        foreach (var finding in findings)
        {
            state.Append("- ").Append(finding.Key).Append(" [").Append(finding.Severity).Append("] ").Append(finding.FilePath)
                .Append(": ").AppendLine(finding.Title)
                .Append("  anchor: ").AppendLine(finding.Anchor.Trim());
        }

        state.AppendLine().AppendLine("The change since the findings were raised:");
        state.AppendLine(diff is null ? "(the code host will not render this diff)"
            : diff.Length > ReviewCategorisation.MaxDiffChars ? diff[..ReviewCategorisation.MaxDiffChars] : diff);
        return state.ToString();
    }

    private static int Raise(int? current, int value) => current is { } c ? Math.Max(c, value) : value;
}
