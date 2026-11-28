using Antiphon.Server.Application.Review.Execution;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Review.Workflows;

internal static class ReviewWorkflowRouting
{
    public static WorkflowNode? Entry(WorkflowDefinition workflow) =>
        workflow.Nodes.FirstOrDefault(node => node.Run == ReviewWalk.EntryRun) ?? workflow.Nodes.FirstOrDefault();

    public static WorkflowEdge? NextEdge(IEnumerable<WorkflowEdge> edges, string? outcome) =>
        (outcome is null ? null : edges.FirstOrDefault(edge => edge.Labels.Contains(outcome, StringComparer.Ordinal)))
            ?? edges.FirstOrDefault(edge => edge.Labels.Count == 0);

    public static string? FailureOutcome(WorkflowNode node) => node.StringParam("fallback");

    public static ReviewRunStatus? TerminalStatus(string? outcome) => outcome switch
    {
        "merge" => ReviewRunStatus.Merged,
        "close" => ReviewRunStatus.Closed,
        _ => null,
    };
}
