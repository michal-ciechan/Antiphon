using System.Text;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review.Decisions;
using Antiphon.Server.Application.Review.Workflows;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Antiphon.Server.Application.Review.Execution;

public sealed record BaseCheck(string ReviewedBase, string MergeBase, string Tip, int BehindBy, bool KeepUpToDate);

public sealed record BaseDriftOutput(
    string ComparedBase,
    string Tip,
    IReadOnlyList<string> BaseFiles,
    IReadOnlyList<string> Overlap,
    double? Probability,
    string Verdict,
    string? Steer);

public sealed record BranchUpdateOutput(string Origin, string FromHead, string? NewHead, string Result, string? Message);

/// <summary>Branch maintenance (design 4.12, 4.13): base-moved, base-drift and update-branch.</summary>
public static class ReviewBranchSteps
{
    public const string BaseDriftQuestion = "base_change_affects_pr";

    /// <summary>
    /// Base moved (design 4.12, D-37 option B), no model call: with <c>keepUpToDate</c> the branch's
    /// merge base is compared with the base tip, so <c>moved</c> (the base moved since the reviewed
    /// base) and <c>behind</c> (it did not, but the branch lacks base commits) are told apart;
    /// without it the reviewed base is compared with the tip.
    /// </summary>
    public static async Task<NodeResult> CheckBaseMovedAsync(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution execution, CancellationToken ct)
    {
        var keep = walk.Policy.KeepUpToDate;
        var reviewed = walk.Iteration.BaseSha ?? walk.Run.Subject.BaseSha;
        var state = await walk.Host.GetBranchStateAsync(walk.Binding.Target, walk.Run.Subject.BaseRef, walk.HeadSha, ct);
        walk.Run.BaseTipSha = state.BaseTipSha;
        var tip = state.BaseTipSha;
        string outcome;
        if (keep)
            outcome = Same(state.MergeBaseSha, tip) ? "unchanged" : !Same(reviewed, tip) ? "moved" : "behind";
        else
            outcome = Same(reviewed, tip) ? "unchanged" : "moved";
        var summary = outcome switch
        {
            "unchanged" => $"The base tip {ReviewIntake.Short(tip)} is the {(keep ? "merge base" : "reviewed base")}: nothing to update",
            "moved" => $"The base moved {ReviewIntake.Short(reviewed)} -> {ReviewIntake.Short(tip)} since the review ({state.BehindBy} commits behind)",
            _ => $"The base has not moved since the review, but the branch is {state.BehindBy} commits behind {ReviewIntake.Short(tip)}",
        };
        return NodeResult.Ok(outcome, summary, new BaseCheck(reviewed, state.MergeBaseSha, tip, state.BehindBy, keep));
    }

    /// <summary>
    /// Base drift (design 4.13): a code-only prefilter (do the base commits touch the PR's files or a
    /// contract glob?) and, only on overlap, one yes/no decision; <c>affected</c> at or above
    /// <c>baseDrift.affectedThreshold</c>. An affected verdict withdraws the approval (D-38) and
    /// carries a rebase steer into the re-review.
    /// </summary>
    public static async Task<NodeResult> BaseDriftAsync(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution execution, CancellationToken ct)
    {
        var policy = walk.Policy;
        var reviewed = walk.Iteration.BaseSha ?? walk.Run.Subject.BaseSha;
        var state = await walk.Host.GetBranchStateAsync(walk.Binding.Target, walk.Run.Subject.BaseRef, walk.HeadSha, ct);
        var compared = policy.KeepUpToDate && state.MergeBaseSha.Length > 0 ? state.MergeBaseSha : reviewed;
        walk.Run.BaseTipSha = state.BaseTipSha;
        var tip = state.BaseTipSha;
        if (Same(compared, tip))
            return NodeResult.Ok("unaffected", "The base has no commits the branch lacks", new BaseDriftOutput(compared, tip, [], [], null, "unaffected", null));

        var comparison = await walk.Host.CompareAsync(walk.Binding.Target, compared, tip, ct);
        var baseFiles = comparison.Files.Select(f => f.Path).Distinct(StringComparer.Ordinal).ToList();
        var prFiles = (await walk.Host.ListChangedFilesAsync(walk.Binding.Target, walk.WholeRange, ct)).Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        var contracts = new Matcher(StringComparison.OrdinalIgnoreCase);
        contracts.AddIncludePatterns(policy.EffectiveContractGlobs);
        var overlap = baseFiles.Where(f => prFiles.Contains(f) || contracts.Match(f).HasMatches).ToList();
        var range = $"{ReviewIntake.Short(compared)}..{ReviewIntake.Short(tip)}";
        if (overlap.Count == 0)
        {
            return NodeResult.Ok("unaffected", $"Base {range} touches {baseFiles.Count} files, none of the PR's or a contract path: unaffected, no model call",
                new BaseDriftOutput(compared, tip, baseFiles, [], null, "unaffected", null));
        }

        var stateText = new StringBuilder()
            .Append("PR: ").AppendLine(walk.Run.Subject.Title)
            .AppendLine("Files the PR changes:").AppendLine(string.Join('\n', prFiles.Order(StringComparer.Ordinal)))
            .Append("Base commits ").Append(range).AppendLine(" change these files that overlap the PR or a contract path:")
            .AppendLine(string.Join('\n', overlap))
            .AppendLine("All files the base commits change:").AppendLine(string.Join('\n', baseFiles))
            .ToString();
        var decided = await walk.Services.GetRequiredService<DecisionChain>().DecideAsync(
            node.StringParam("chain") ?? ReviewCategorisation.DefaultChain,
            new DecisionRequest(stateText, [DecisionQuestion.Noul(BaseDriftQuestion, "Could these base changes affect this PR?")]), ct);
        var usage = NodeUsage.From(decided);
        if (!decided.Answers.TryGetValue(BaseDriftQuestion, out var answer) || answer.Refused)
            return NodeResult.Fail($"Every entry refused '{BaseDriftQuestion}'.", usage);
        var p = ReviewIntakeSteps.YesProbability(answer);
        var affected = p >= policy.BaseDriftAffectedThreshold;
        string? steer = null;
        if (affected)
        {
            steer = $"Base moved {range}: review as if rebased onto the new base; check interactions with these files: {string.Join(", ", overlap)}.";
            await walk.SetStatusAsync(ReviewWalk.ApprovalStatusContext, CodeHostStatusState.Pending,
                $"Base moved {range}; re-reviewing before approval", ct);
            walk.Run.Status = ReviewRunStatus.Running;
        }

        return NodeResult.Ok(affected ? "affected" : "unaffected",
            $"Base {range}: {overlap.Count} overlapping files; p(affects)={p:0.00} vs {policy.BaseDriftAffectedThreshold:0.00}: {(affected ? "affected" : "unaffected")}",
            new BaseDriftOutput(compared, tip, baseFiles, overlap, p, affected ? "affected" : "unaffected", steer), usage);
    }

    /// <summary>
    /// Update branch (design 4.12): after <c>base-drift</c> (outcome <c>approved</c>) or an author
    /// opt-in from <c>await-fixes</c> (outcome <c>open</c>). With <c>keepUpToDate</c> the base is
    /// merged in through the code host; when the host reports the new head at once, the run accepts
    /// it as a base-only update and an approval carries forward to it. Without <c>keepUpToDate</c>
    /// it records <c>not-needed</c>; the clean-merge check comes later.
    /// </summary>
    public static async Task<NodeResult> UpdateBranchAsync(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution execution, CancellationToken ct)
    {
        var before = walk.Before(execution);
        var origin = before?.NodeId is { } previous && walk.Workflow.Node(previous)?.Kind == ReviewNodeKind.Wait ? "open" : "approved";
        var fromHead = walk.HeadSha;
        if (!walk.Policy.KeepUpToDate)
            return NodeResult.Ok(origin, "keepUpToDate is off: not-needed (the clean-merge check comes later)",
                new BranchUpdateOutput(origin, fromHead, null, "not-needed", null));
        if (!walk.Host.Capabilities.CanUpdateBranch || walk.Run.Subject.PrNumber is null)
            return NodeResult.Ok(origin, "The code host cannot update branches; the branch is left as it is",
                new BranchUpdateOutput(origin, fromHead, null, "unsupported", null));

        var update = await walk.Host.UpdateBranchAsync(walk.Binding.Target, fromHead, ct);
        var summary = new StringBuilder($"Asked the code host to bring {ReviewIntake.Short(fromHead)} up to date with the base"
            + (string.IsNullOrWhiteSpace(update.Message) ? "" : $" ({update.Message.Trim().TrimEnd('.')})"));
        if (update.NewHeadSha is { Length: > 0 } newHead && !Same(newHead, fromHead))
        {
            walk.Run.Subject.HeadSha = newHead;
            summary.Append($"; new head {ReviewIntake.Short(newHead)} (base-only)");
            if (origin == "approved")
            {
                await walk.SetStatusAsync(ReviewWalk.ApprovalStatusContext, CodeHostStatusState.Success,
                    "Approval carried forward (base-only update)", ct, newHead);
                summary.Append("; approval carried forward");
            }
        }
        else if (update.NewHeadSha is null)
        {
            summary.Append("; the update is asynchronous, so its push is seen on the next Refresh");
        }

        return NodeResult.Ok(origin, summary.ToString(), new BranchUpdateOutput(origin, fromHead, update.NewHeadSha, "updated", update.Message));
    }

    private static bool Same(string? a, string? b) => !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
