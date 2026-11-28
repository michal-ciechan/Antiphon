using Antiphon.Server.Application.Review.Execution;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Review.Workflows;

internal enum ReviewChainFamily
{
    Decision,
    Completion,
}

internal sealed record ReviewWorkflowChainNames(IReadOnlySet<string> Decisions, IReadOnlySet<string> Completions)
{
    public bool Contains(ReviewChainFamily family, string name) =>
        (family == ReviewChainFamily.Decision ? Decisions : Completions).Contains(name);
}

internal sealed record ReviewStepChain(ReviewChainFamily Family, string Default, bool EmptyUsesDefault = false)
{
    public WorkflowPolicyValue<string> Resolve(WorkflowNode node)
    {
        var authored = node.StringParam("chain");
        return authored is null || (EmptyUsesDefault && authored.Length == 0)
            ? new(Default, WorkflowPolicySource.StepDefault)
            : new(authored, WorkflowPolicySource.WorkflowRevision);
    }
}

internal sealed record ReviewStepContract(
    ReviewNodeKind Kind,
    IReadOnlyList<string> Outputs,
    ReviewStepChain? Chain = null,
    bool AcceptsInputs = false);

internal static class ReviewWorkflowSteps
{
    internal delegate Task<NodeResult> Handler(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution execution, CancellationToken ct);

    internal sealed record Registration(ReviewStepContract Contract, Handler Run);

    private static readonly ReviewStepChain Decision = new(ReviewChainFamily.Decision, ReviewCategorisation.DefaultChain);
    private static readonly ReviewStepChain DecisionWithEmptyDefault = Decision with { EmptyUsesDefault = true };

    private static readonly Dictionary<ReviewNodeKind, Registration> ControlSteps = new()
    {
        [ReviewNodeKind.Gate] = new(new(ReviewNodeKind.Gate, []), ReviewDecideSteps.GateAsync),
        [ReviewNodeKind.Wait] = new(new(ReviewNodeKind.Wait, []), ReviewDecideSteps.WaitAsync),
        [ReviewNodeKind.FanOut] = new(new(ReviewNodeKind.FanOut, []), ReviewReviewSteps.FanOutAsync),
    };

    private static readonly Dictionary<string, Registration> Steps = new(StringComparer.Ordinal)
    {
        ["Intake"] = new(new(ReviewNodeKind.Code, []), ReviewIntakeSteps.IntakeAsync),
        ["Delta"] = new(new(ReviewNodeKind.Code, ["incremental", "full"], Decision), ReviewIntakeSteps.DeltaAsync),
        ["Categorise"] = new(new(ReviewNodeKind.Decision, ReviewCategorisation.ChangeKinds, DecisionWithEmptyDefault), ReviewIntakeSteps.CategoriseAsync),
        ["SelectChecks"] = new(new(ReviewNodeKind.Decision, [], DecisionWithEmptyDefault), ReviewSelectionSteps.SelectChecksAsync),
        ["GroupChecks"] = new(new(ReviewNodeKind.Code, []), ReviewSelectionSteps.GroupChecksAsync),
        ["BuildPacks"] = new(new(ReviewNodeKind.Code, []), ReviewSelectionSteps.BuildPacksAsync),
        ["CollateFindings"] = new(new(ReviewNodeKind.Join, []), ReviewReviewSteps.CollateAsync),
        ["VerifyFindings"] = new(new(ReviewNodeKind.Completion, [],
            new(ReviewChainFamily.Completion, ReviewCompletionSettings.DefaultChain)), ReviewReviewSteps.VerifyAsync),
        ["Publish"] = new(new(ReviewNodeKind.Code, []), ReviewReviewSteps.PublishAsync),
        ["RouteAfterRound"] = new(new(ReviewNodeKind.Code, ["bot-round-limit", "findings", "clean"]), ReviewDecideSteps.RouteAsync),
        ["NeedsHumanApproval"] = new(new(ReviewNodeKind.Code, ["yes", "no"]), ReviewDecideSteps.NeedsHumanApprovalAsync),
        ["BotApprove"] = new(new(ReviewNodeKind.Code, []), ReviewDecideSteps.BotApproveAsync),
        ["RecordBaseDrift"] = new(new(ReviewNodeKind.Code, []), ReviewDecideSteps.RecordBaseDriftAsync),
        ["CheckBaseMoved"] = new(new(ReviewNodeKind.Code, ["unchanged", "moved", "behind"]), ReviewBranchSteps.CheckBaseMovedAsync),
        ["BaseDriftCheck"] = new(new(ReviewNodeKind.Decision, ["affected", "unaffected"], Decision), ReviewBranchSteps.BaseDriftAsync),
        ["UpdateBranch"] = new(new(ReviewNodeKind.Code, ["approved", "open"]), ReviewBranchSteps.UpdateBranchAsync),
        ["Merge"] = new(new(ReviewNodeKind.Code, []), ReviewBranchSteps.MergeAsync),
    };

    public static IReadOnlyCollection<string> Names => Steps.Keys;

    public static Registration? Find(string? name) => name is null ? null : Steps.GetValueOrDefault(name);

    public static Registration? Resolve(WorkflowNode node) => ControlSteps.GetValueOrDefault(node.Kind) ?? Find(node.Run);

    public static string ChainName(WorkflowNode node) => Steps[node.Run!].Contract.Chain!.Resolve(node).Value;
}
