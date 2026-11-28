using Antiphon.Server.Application.Review.Execution;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Review.Workflows;

internal static class ReviewWorkflowPolicy
{
    public static WorkflowPolicyValue<string> OnFailure(ReviewPolicySettings policy, WorkflowNode node)
    {
        if (policy.OnFailure.TryGetValue(node.Id, out var configured) && !string.IsNullOrWhiteSpace(configured))
            return new(FailureAction(configured), WorkflowPolicySource.InstanceOverride);
        if (node.StringParam("onFailure") is { } authored)
            return new(FailureAction(authored), WorkflowPolicySource.WorkflowRevision);
        return new("stop", WorkflowPolicySource.StepDefault);
    }

    public static WorkflowPolicyValue<bool> GatesOff(ReviewPolicySettings policy, WorkflowNode node, string repo) =>
        ReviewDecideSteps.IsOptional(node) && policy.GateOff(node.Id, repo)
            ? new(true, WorkflowPolicySource.RepoRule)
            : new(false, WorkflowPolicySource.StepDefault);

    public static WorkflowNodePolicyDto ForNode(ReviewPolicySettings policy, WorkflowNode node, string repo)
    {
        var step = ReviewWorkflowSteps.Resolve(node)?.Contract;
        return new(
            node.Id,
            step is null ? null : OnFailure(policy, node),
            node.Kind == ReviewNodeKind.Gate ? GatesOff(policy, node, repo) : null,
            step?.Chain?.Resolve(node));
    }

    private static string FailureAction(string value) =>
        value.Trim().Equals("continue", StringComparison.OrdinalIgnoreCase) ? "continue" : "stop";
}
