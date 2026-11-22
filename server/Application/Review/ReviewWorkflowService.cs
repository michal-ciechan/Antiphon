using Antiphon.Server.Application.Review.Workflows;

namespace Antiphon.Server.Application.Review;

/// <summary>The default workflow and graph validation over the contract's <c>WorkflowGraphDto</c>.</summary>
public sealed class ReviewWorkflowService
{
    private readonly Lazy<WorkflowDefinition> _default = new(ReviewWorkflowYaml.LoadDefault);
    private readonly Lazy<string> _defaultHash;

    public ReviewWorkflowService() => _defaultHash = new Lazy<string>(() => _default.Value.Hash());

    public WorkflowDefinition Default => _default.Value;
    public string DefaultHash => _defaultHash.Value;

    public WorkflowGraphDto GetDefault() => ToDto(Default, revisionId: null);

    public WorkflowValidationDto Validate(WorkflowGraphDto graph)
    {
        var validation = ReviewWorkflowValidator.Validate(FromDto(graph));
        return new WorkflowValidationDto(validation.Valid, validation.Diagnostics
            .Select(d => new WorkflowDiagnosticDto(d.Code, d.Severity.ToString(), d.Message, d.NodeIds,
                d.Edge is { } e ? new WorkflowEdgeRefDto(e.From, e.To) : null))
            .ToList());
    }

    public static WorkflowGraphDto ToDto(WorkflowDefinition workflow, Guid? revisionId)
    {
        var back = workflow.BackEdges();
        return new WorkflowGraphDto(
            workflow.Name,
            revisionId,
            workflow.Hash(),
            workflow.Sections.Select(s => new WorkflowSectionDto(s.Id, s.Title, s.NodeIds, s.Collapsed)).ToList(),
            workflow.Nodes.Select(n => new WorkflowNodeDto(n.Id, n.Kind, n.Run, n.Description, n.Params)).ToList(),
            workflow.Edges.Select(e => new WorkflowEdgeDto(e.From, e.To, e.Label, e.MaxIterations, back.Contains(e), e.Overflow, e.Description)).ToList());
    }

    public static WorkflowDefinition FromDto(WorkflowGraphDto graph) => new(
        string.IsNullOrWhiteSpace(graph.Name) ? "workflow" : graph.Name,
        1,
        (graph.Sections ?? []).Select(s => new WorkflowSection(s.Id, s.Title, s.NodeIds ?? [], s.Collapsed)).ToList(),
        (graph.Nodes ?? []).Select(n => new WorkflowNode(n.Id, n.Kind, n.Run, n.Description,
            (n.Params ?? new Dictionary<string, object?>()).ToDictionary(p => p.Key, p => ReviewJsonValues.ToClr(p.Value), StringComparer.Ordinal))).ToList(),
        (graph.Edges ?? []).Select(e => new WorkflowEdge(e.From, e.To, string.IsNullOrWhiteSpace(e.Label) ? null : e.Label, e.MaxIterations,
            string.IsNullOrWhiteSpace(e.Overflow) ? null : e.Overflow, e.Description)).ToList());
}
