using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Review.Workflows;

public enum WorkflowDiagnosticSeverity
{
    Error,
    Warning,
}

/// <summary>One validation diagnostic, shaped like the contract's <c>WorkflowValidationDto</c> entries.</summary>
public sealed record WorkflowDiagnostic(
    string Code,
    WorkflowDiagnosticSeverity Severity,
    string Message,
    IReadOnlyList<string> NodeIds,
    (string From, string To)? Edge = null)
{
    public string? Field { get; init; }
}

public sealed record WorkflowValidation(IReadOnlyList<WorkflowDiagnostic> Diagnostics)
{
    /// <summary>Warnings (a missing description) do not make a graph invalid (design 3.3).</summary>
    public bool Valid => Diagnostics.All(d => d.Severity != WorkflowDiagnosticSeverity.Error);
}

/// <summary>
/// Graph rules for saving a workflow revision (design 3.2, 3.3): known node references, gate
/// <c>autoRetries</c> bounds, iteration-limited back edges that overflow to a gate, declared
/// outputs, and the cycle rule.
/// </summary>
public static class ReviewWorkflowValidator
{
    public const int MaxAutoRetries = 3;

    public static class Codes
    {
        public const string UnknownNode = "unknown-node";
        public const string DuplicateNode = "duplicate-node";
        public const string GateAutoRetries = "gate-auto-retries";
        public const string GateAutoDecision = "gate-auto-decision";
        public const string GateDecisions = "gate-decisions";
        public const string UnknownOutput = "unknown-output";
        public const string MaxIterations = "max-iterations";
        public const string MissingOverflow = "missing-overflow";
        public const string OverflowNotGate = "overflow-not-gate";
        public const string UnboundedCycle = "unbounded-cycle";
        public const string SectionOverlap = "section-overlap";
        public const string FanOutPolicy = "fan-out-policy";
        public const string MissingDescription = "missing-description";
        public const string OnFailure = "on-failure";
    }

    public static WorkflowValidation Validate(WorkflowDefinition workflow)
    {
        var diagnostics = new List<WorkflowDiagnostic>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in workflow.Nodes)
        {
            if (!ids.Add(node.Id))
                diagnostics.Add(Error(Codes.DuplicateNode, $"Node '{node.Id}' is declared twice.", [node.Id]));
        }

        foreach (var edge in workflow.Edges)
        {
            foreach (var end in new[] { edge.From, edge.To }.Distinct())
            {
                if (!ids.Contains(end))
                    diagnostics.Add(Error(Codes.UnknownNode, $"Edge {edge.From} -> {edge.To} names unknown node '{end}'.", [end], edge));
            }
        }

        var bounded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in workflow.Nodes)
        {
            ValidateNode(node, ids, diagnostics);
            ReviewPublishConfiguration.Validate(workflow, node, diagnostics);
            if (node.Kind == ReviewNodeKind.Wait || (node.Kind == ReviewNodeKind.Gate && GateBreaksCycles(node)))
                bounded.Add(node.Id);
        }

        ValidateEdges(workflow, diagnostics);
        ValidateSections(workflow, ids, diagnostics);
        ValidateCycles(workflow, ids, bounded, diagnostics);
        return new WorkflowValidation(diagnostics);
    }

    private static void ValidateNode(WorkflowNode node, HashSet<string> ids, List<WorkflowDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(node.Description))
            diagnostics.Add(Warning(Codes.MissingDescription, $"Node '{node.Id}' has no description.", [node.Id]));

        // onFailure: continue | stop, with an optional fallback output (the edge a failed-continued step takes).
        var onFailure = node.StringParam("onFailure");
        if (onFailure is not null && onFailure is not ("continue" or "stop"))
            diagnostics.Add(Error(Codes.OnFailure, $"Node '{node.Id}' onFailure '{onFailure}' must be continue or stop.", [node.Id]));
        else if (onFailure == "continue" && node.Kind is ReviewNodeKind.Gate or ReviewNodeKind.Wait or ReviewNodeKind.FanOut)
            diagnostics.Add(Error(Codes.OnFailure, $"Node '{node.Id}' is a {node.Kind}: only a step that does work can continue past a failure.", [node.Id]));
        if (node.StringParam("fallback") is { } fallback && node.ListParam("outputs") is { Count: > 0 } outputs && !outputs.Contains(fallback, StringComparer.Ordinal))
            diagnostics.Add(Error(Codes.OnFailure, $"Node '{node.Id}' fallback '{fallback}' is not one of its outputs.", [node.Id]));

        switch (node.Kind)
        {
            case ReviewNodeKind.Gate:
                if (node.Decisions.Count == 0)
                    diagnostics.Add(Error(Codes.GateDecisions, $"Gate '{node.Id}' lists no decisions.", [node.Id]));
                var hasRetries = node.Params.TryGetValue("autoRetries", out var retries);
                var hasDecision = node.Params.TryGetValue("autoDecision", out var autoDecision);
                if (hasRetries && !IsWholeNumberInRange(retries, 0, MaxAutoRetries))
                {
                    diagnostics.Add(Error(Codes.GateAutoRetries,
                        $"Gate '{node.Id}' autoRetries must be a whole number from 0 to {MaxAutoRetries}; '{retries}' could skip the human forever.", [node.Id]));
                }
                else if (hasDecision && !hasRetries)
                {
                    diagnostics.Add(Error(Codes.GateAutoRetries,
                        $"Gate '{node.Id}' has an autoDecision but no autoRetries; a missing bound could skip the human forever.", [node.Id]));
                }

                if (hasDecision && !node.Decisions.Contains(autoDecision?.ToString() ?? "", StringComparer.Ordinal))
                    diagnostics.Add(Error(Codes.GateAutoDecision, $"Gate '{node.Id}' autoDecision '{autoDecision}' is not one of its decisions.", [node.Id]));
                break;

            case ReviewNodeKind.FanOut:
                var child = node.StringParam("node");
                if (child is null || !ids.Contains(child))
                    diagnostics.Add(Error(Codes.UnknownNode, $"Fan-out '{node.Id}' runs unknown node '{child}'.", [node.Id]));
                var policy = node.StringParam("policy") ?? "all";
                if (policy is not ("all" or "best-effort") && !IsQuorum(policy))
                    diagnostics.Add(Error(Codes.FanOutPolicy, $"Fan-out '{node.Id}' policy '{policy}' must be all, quorum:n or best-effort.", [node.Id]));
                break;

            case ReviewNodeKind.Join:
                var from = node.StringParam("from");
                if (from is not null && !ids.Contains(from))
                    diagnostics.Add(Error(Codes.UnknownNode, $"Join '{node.Id}' gathers unknown node '{from}'.", [node.Id]));
                break;
        }
    }

    private static void ValidateEdges(WorkflowDefinition workflow, List<WorkflowDiagnostic> diagnostics)
    {
        foreach (var edge in workflow.Edges)
        {
            var source = workflow.Node(edge.From);
            if (source is not null && edge.Labels.Count > 0)
            {
                var allowed = source.Kind switch
                {
                    ReviewNodeKind.Gate => source.Decisions,
                    ReviewNodeKind.Wait => source.ListParam("on"),
                    _ => source.ListParam("outputs"),
                };
                if (allowed.Count > 0)
                {
                    foreach (var label in edge.Labels.Where(l => !allowed.Contains(l, StringComparer.Ordinal)))
                    {
                        diagnostics.Add(Error(Codes.UnknownOutput,
                            $"Edge {edge.From}[{label}] -> {edge.To}: '{edge.From}' has no output '{label}'.", [edge.From], edge));
                    }
                }
            }

            if (edge.MaxIterations is { } max)
            {
                if (max < 1)
                    diagnostics.Add(Error(Codes.MaxIterations, $"Edge {edge.From} -> {edge.To} maxIterations must be at least 1.", [edge.From, edge.To], edge));
                if (edge.Overflow is null)
                {
                    diagnostics.Add(Error(Codes.MissingOverflow,
                        $"Edge {edge.From} -> {edge.To} has maxIterations but no overflow gate to open over the limit (D-2).", [edge.From, edge.To], edge));
                }
                else if (workflow.Node(edge.Overflow) is not { } overflow)
                {
                    diagnostics.Add(Error(Codes.UnknownNode, $"Edge {edge.From} -> {edge.To} overflows to unknown node '{edge.Overflow}'.", [edge.Overflow], edge));
                }
                else if (overflow.Kind != ReviewNodeKind.Gate)
                {
                    diagnostics.Add(Error(Codes.OverflowNotGate,
                        $"Edge {edge.From} -> {edge.To} overflows to '{edge.Overflow}', which is a {WorkflowDefinition.KindToYaml(overflow.Kind)} node, not a gate (D-2).", [edge.Overflow], edge));
                }
            }
            else if (edge.Overflow is not null)
            {
                diagnostics.Add(Error(Codes.MaxIterations, $"Edge {edge.From} -> {edge.To} names an overflow gate but no maxIterations.", [edge.From, edge.To], edge));
            }
        }
    }

    private static void ValidateSections(WorkflowDefinition workflow, HashSet<string> ids, List<WorkflowDiagnostic> diagnostics)
    {
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var section in workflow.Sections)
        {
            foreach (var nodeId in section.NodeIds)
            {
                if (!ids.Contains(nodeId))
                    diagnostics.Add(Error(Codes.UnknownNode, $"Section '{section.Id}' lists unknown node '{nodeId}'.", [nodeId]));
                else if (owner.TryGetValue(nodeId, out var other) && other != section.Id)
                    diagnostics.Add(Error(Codes.SectionOverlap, $"Node '{nodeId}' is in sections '{other}' and '{section.Id}'; a node is in at most one.", [nodeId]));
                else
                    owner[nodeId] = section.Id;
            }
        }
    }

    /// <summary>
    /// Design 3.2: every cycle must contain a gate or wait node, or an edge with maxIterations. Remove
    /// those nodes and edges; any strongly connected component left with a cycle breaks the rule.
    /// </summary>
    private static void ValidateCycles(WorkflowDefinition workflow, HashSet<string> ids, HashSet<string> bounded, List<WorkflowDiagnostic> diagnostics)
    {
        var edges = workflow.Edges
            .Where(e => e.MaxIterations is null && ids.Contains(e.From) && ids.Contains(e.To)
                && !bounded.Contains(e.From) && !bounded.Contains(e.To))
            .ToList();
        var outgoing = edges.GroupBy(e => e.From).ToDictionary(g => g.Key, g => g.Select(e => e.To).Distinct().ToList(), StringComparer.Ordinal);

        foreach (var component in StronglyConnected(ids.Where(id => !bounded.Contains(id)).ToList(), outgoing))
        {
            var members = component.ToHashSet(StringComparer.Ordinal);
            var inside = edges.Where(e => members.Contains(e.From) && members.Contains(e.To)).ToList();
            if (inside.Count == 0)
                continue;
            var closing = inside.Last();
            diagnostics.Add(Error(Codes.UnboundedCycle,
                $"The cycle through {string.Join(" -> ", component)} has no gate or wait node and no maxIterations; insert a gate or add an iteration limit on {closing.From} -> {closing.To}.",
                component, closing));
        }
    }

    private static List<List<string>> StronglyConnected(List<string> nodes, Dictionary<string, List<string>> outgoing)
    {
        var index = 0;
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var low = new Dictionary<string, int>(StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var result = new List<List<string>>();
        var allowed = nodes.ToHashSet(StringComparer.Ordinal);

        void Connect(string v)
        {
            indices[v] = low[v] = index++;
            stack.Push(v);
            onStack.Add(v);
            foreach (var w in outgoing.GetValueOrDefault(v) ?? [])
            {
                if (!allowed.Contains(w))
                    continue;
                if (!indices.ContainsKey(w))
                {
                    Connect(w);
                    low[v] = Math.Min(low[v], low[w]);
                }
                else if (onStack.Contains(w))
                {
                    low[v] = Math.Min(low[v], indices[w]);
                }
            }

            if (low[v] != indices[v])
                return;
            var component = new List<string>();
            string popped;
            do
            {
                popped = stack.Pop();
                onStack.Remove(popped);
                component.Add(popped);
            } while (popped != v);
            component.Reverse();
            result.Add(component);
        }

        foreach (var node in nodes)
        {
            if (!indices.ContainsKey(node))
                Connect(node);
        }

        return result;
    }

    /// <summary>A gate breaks a cycle only while autoRetries is absent or a whole number from 0 to 3 (design 3.2).</summary>
    private static bool GateBreaksCycles(WorkflowNode gate)
    {
        var hasRetries = gate.Params.TryGetValue("autoRetries", out var retries);
        if (!hasRetries)
            return !gate.Params.ContainsKey("autoDecision");
        return IsWholeNumberInRange(retries, 0, MaxAutoRetries);
    }

    private static bool IsWholeNumberInRange(object? value, int min, int max) => value switch
    {
        long l => l >= min && l <= max,
        int i => i >= min && i <= max,
        double d => d == Math.Floor(d) && d >= min && d <= max,
        decimal m => m == decimal.Floor(m) && m >= min && m <= max,
        _ => false,
    };

    private static bool IsQuorum(string policy) =>
        policy.StartsWith("quorum:", StringComparison.Ordinal) && int.TryParse(policy["quorum:".Length..], out var n) && n > 0;

    private static WorkflowDiagnostic Error(string code, string message, IReadOnlyList<string> nodeIds, WorkflowEdge? edge = null) =>
        new(code, WorkflowDiagnosticSeverity.Error, message, nodeIds, edge is null ? null : (edge.From, edge.To));

    private static WorkflowDiagnostic Warning(string code, string message, IReadOnlyList<string> nodeIds) =>
        new(code, WorkflowDiagnosticSeverity.Warning, message, nodeIds);
}
