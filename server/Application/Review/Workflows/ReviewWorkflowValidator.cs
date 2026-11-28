using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Review.Execution;
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
    WorkflowEdge? Edge = null);

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
    private const int MaxDiagnostics = 100;

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
        public const string InvalidNodeId = "invalid-node-id";
        public const string InvalidKind = "invalid-kind";
        public const string UnknownStep = "unknown-step";
        public const string KindMismatch = "kind-mismatch";
        public const string ChildOnly = "child-only";
        public const string InvalidParameter = "invalid-parameter";
        public const string InvalidInput = "invalid-input";
        public const string UnsupportedInput = "unsupported-input";
        public const string UnavailableInput = "unavailable-input";
        public const string UnknownChain = "unknown-chain";
        public const string UnsupportedOutput = "unsupported-output";
        public const string AmbiguousRoute = "ambiguous-route";
        public const string MissingEntry = "missing-entry";
        public const string MissingSuccessor = "missing-successor";
        public const string MissingFailureRoute = "missing-failure-route";
    }

    public static WorkflowValidation Validate(WorkflowDefinition workflow) =>
        Validate(workflow, name => ReviewWorkflowSteps.Find(name)?.Contract);

    internal static WorkflowValidation Validate(
        WorkflowDefinition workflow,
        Func<string?, ReviewStepContract?> findStep,
        ReviewWorkflowChainNames? chains = null,
        Func<WorkflowNode, bool>? continuesOnFailure = null)
    {
        var diagnostics = new DiagnosticList();
        var graph = new ValidationGraph(workflow);
        ValidateStructure(graph, diagnostics);
        var childIds = workflow.Nodes.Where(node => node.Kind == ReviewNodeKind.FanOut)
            .Select(node => node.StringParam("node")).OfType<string>().ToHashSet(StringComparer.Ordinal);
        if (graph.Entry is not { } entry || childIds.Contains(entry.Id))
            diagnostics.Add(Error(Codes.MissingEntry, "A workflow needs an entry node that can run on its own.", []));

        foreach (var node in workflow.Nodes)
        {
            if (!ValidNodeId(node.Id))
                diagnostics.Add(Error(Codes.InvalidNodeId, "Use a letter followed by letters, digits, dashes or underscores for a node ID.", [node.Id]));

            if (!Enum.IsDefined(node.Kind))
            {
                diagnostics.Add(Error(Codes.InvalidKind, $"Node '{node.Id}' has an unknown kind.", [node.Id]));
                continue;
            }

            var contract = ReviewWorkflowSteps.Control(node.Kind)?.Contract ?? findStep(node.Run);
            switch (node.Kind)
            {
                case ReviewNodeKind.Agent when childIds.Contains(node.Id) && node.Run is null:
                    break;
                case ReviewNodeKind.Gate or ReviewNodeKind.Wait or ReviewNodeKind.FanOut:
                    if (node.Run is not null)
                        diagnostics.Add(Error(Codes.KindMismatch, $"Node '{node.Id}' is handled by its kind and cannot name a step.", [node.Id]));
                    ValidateControlNode(graph, node, diagnostics);
                    break;
                default:
                    if (contract is null)
                        diagnostics.Add(Error(Codes.UnknownStep, $"Node '{node.Id}' has no registered step '{node.Run}'.", [node.Id]));
                    else if (node.Kind != contract.Kind)
                        diagnostics.Add(Error(Codes.KindMismatch, $"Step '{node.Run}' requires kind {contract.Kind}.", [node.Id]));
                    break;
            }

            ValidateParameters(graph, node, contract, chains, continuesOnFailure, diagnostics);
            if (!childIds.Contains(node.Id) && contract?.Kind == node.Kind)
                ValidateRequiredRoutes(graph, node, contract, continuesOnFailure, diagnostics);
        }

        var routes = new HashSet<(string NodeId, string? Label)>();
        foreach (var edge in workflow.Edges)
        {
            if (childIds.Contains(edge.To) || childIds.Contains(edge.From) || (edge.Overflow is { } overflow && childIds.Contains(overflow)))
                diagnostics.Add(Error(Codes.ChildOnly, "Fan-out child nodes must run through their parent fan-out.", [edge.From, edge.To], edge));

            IReadOnlyList<string?> labels = edge.Labels.Count == 0 ? new string?[] { null } : edge.Labels;
            foreach (var label in labels.Distinct(StringComparer.Ordinal))
            {
                if (!routes.Add((edge.From, label)))
                    diagnostics.Add(Error(Codes.AmbiguousRoute, $"Node '{edge.From}' has more than one route for '{label ?? "the default outcome"}'.",
                        [edge.From, edge.To], edge));
            }
        }
        return diagnostics.Result();
    }

    // Structural checks are also exercised in isolation on the small cycle and gate fixtures.
    internal static WorkflowValidation ValidateStructure(WorkflowDefinition workflow)
    {
        var diagnostics = new DiagnosticList();
        ValidateStructure(new ValidationGraph(workflow), diagnostics);
        return diagnostics.Result();
    }

    private static void ValidateStructure(ValidationGraph graph, DiagnosticList diagnostics)
    {
        var workflow = graph.Definition;
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
            if (node.Kind == ReviewNodeKind.Wait || (node.Kind == ReviewNodeKind.Gate && GateBreaksCycles(node)))
                bounded.Add(node.Id);
        }

        ValidateEdges(graph, diagnostics);
        ValidateSections(workflow, ids, diagnostics);
        ValidateCycles(graph, ids, bounded, diagnostics);
    }

    private static bool ValidNodeId(string? id) => !string.IsNullOrEmpty(id) && char.IsAsciiLetter(id[0])
        && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static void ValidateControlNode(ValidationGraph graph, WorkflowNode node, DiagnosticList diagnostics)
    {
        var invalid = node.Kind switch
        {
            ReviewNodeKind.Gate => node.Decisions.Any(decision => ReviewGateRule.Find(decision) is null)
                || (node.Params.ContainsKey("optional") && !bool.TryParse(node.StringParam("optional"), out _))
                || (node.Params.ContainsKey("gateKind") && (!Enum.TryParse<ReviewGateKind>(node.StringParam("gateKind"), true, out var kind)
                    || !Enum.IsDefined(kind))),
            ReviewNodeKind.Wait => ReviewDecideSteps.WaitOutcomes(node) is var events && (events.Count == 0
                || events.Any(evt => evt is not ("push" or "merge" or "close" or "base-moved" or "recheck" or "update-requested"))),
            ReviewNodeKind.FanOut => (node.StringParam("over") is { } over && over != "groups")
                || (graph.Node(node.StringParam("node") ?? "") is { } child && (child.Kind != ReviewNodeKind.Agent || child.Run is not null)),
            _ => false,
        };
        if (invalid)
            diagnostics.Add(Error(Codes.InvalidParameter, $"Node '{node.Id}' has unsupported {node.Kind} parameters.", [node.Id]));
    }

    private static void ValidateParameters(
        ValidationGraph graph,
        WorkflowNode node,
        ReviewStepContract? contract,
        ReviewWorkflowChainNames? chains,
        Func<WorkflowNode, bool>? continuesOnFailure,
        DiagnosticList diagnostics)
    {
        if (node.Params.TryGetValue("chain", out var authoredChain) && authoredChain is not (null or string))
            diagnostics.Add(Error(Codes.InvalidParameter, $"Node '{node.Id}' needs a text chain name.", [node.Id]));
        else if (contract?.Chain is { } chain && chains is not null && !chains.Contains(chain.Family, chain.Resolve(node).Value))
            diagnostics.Add(Error(Codes.UnknownChain, $"Node '{node.Id}' needs a registered {chain.Family.ToString().ToLowerInvariant()} chain.", [node.Id]));
        else if (contract?.Chain is null && authoredChain is not null)
            diagnostics.Add(Error(Codes.InvalidParameter, $"Step '{node.Run}' does not use a model chain.", [node.Id]));

        var fallback = node.StringParam("fallback");
        if (node.Params.ContainsKey("fallback") && (node.Params["fallback"] is not string || string.IsNullOrWhiteSpace(fallback)))
            diagnostics.Add(Error(Codes.InvalidParameter, $"Node '{node.Id}' needs a nonempty fallback label.", [node.Id]));

        if (contract is { ControlOutcomes: null })
        {
            foreach (var output in node.ListParam("outputs").Distinct(StringComparer.Ordinal))
            {
                if (!contract.Outputs.Contains(output, StringComparer.Ordinal) && output != fallback)
                    diagnostics.Add(Error(Codes.UnsupportedOutput, $"Step '{node.Run}' cannot emit '{output}'.", [node.Id]));
            }
            foreach (var edge in graph.Outgoing(node.Id))
            {
                foreach (var label in edge.Labels.Where(label => !contract.Outputs.Contains(label, StringComparer.Ordinal) && label != fallback))
                    diagnostics.Add(Error(Codes.UnsupportedOutput, $"Step '{node.Run}' cannot take an edge for '{label}'.", [node.Id], edge));
            }
        }

        if (!node.Params.TryGetValue("inputs", out var raw))
            return;
        if (raw is not IEnumerable<object?> inputs || inputs.Any(input => input is not string id || !ValidNodeId(id)))
        {
            diagnostics.Add(Error(Codes.InvalidInput, $"Node '{node.Id}' inputs must be an array of node IDs.", [node.Id]));
            return;
        }
        if (contract?.AcceptsInputs != true)
        {
            diagnostics.Add(Error(Codes.UnsupportedInput, $"Node '{node.Id}' does not support declared inputs.", [node.Id]));
            return;
        }
        foreach (var id in inputs.Cast<string>().Distinct(StringComparer.Ordinal))
        {
            var producer = graph.Node(id);
            if (producer is null || !ProducerAvailable(graph, producer, node, continuesOnFailure))
                diagnostics.Add(Error(Codes.UnavailableInput, $"Input '{id}' is not available on every path to '{node.Id}'.", [node.Id]));
        }
    }

    private static void ValidateRequiredRoutes(
        ValidationGraph graph,
        WorkflowNode node,
        ReviewStepContract contract,
        Func<WorkflowNode, bool>? continuesOnFailure,
        DiagnosticList diagnostics)
    {
        var edges = graph.Outgoing(node.Id);
        bool HasRoute(string? outcome) => ReviewWorkflowRouting.NextEdge(edges, outcome) is not null
            || ReviewWorkflowRouting.TerminalStatus(outcome) is not null;

        foreach (var outcome in contract.Outcomes(node).Distinct(StringComparer.Ordinal))
        {
            if (!HasRoute(outcome))
                diagnostics.Add(Error(Codes.MissingSuccessor,
                    $"Node '{node.Id}' needs a route for '{outcome ?? "the unlabelled outcome"}'.", [node.Id]));
        }

        if (ContinuesOnFailure(node, continuesOnFailure) && !HasRoute(ReviewWorkflowRouting.FailureOutcome(node)))
            diagnostics.Add(Error(Codes.MissingFailureRoute,
                $"Node '{node.Id}' needs a route after a continued failure.", [node.Id]));

        foreach (var step in contract.RequiredSteps ?? [])
        {
            if (!graph.HasPredecessor(node.Id, step, byStep: true))
                diagnostics.Add(Error(Codes.UnavailableInput, $"Step '{step}' must precede '{node.Id}' on every path.", [node.Id]));
        }

        if (node.Run == ReviewWorkflowSteps.CollateFindings)
        {
            var fanOut = ReviewReviewSteps.CollatedFanOutId(node);
            if (graph.Node(fanOut)?.Kind != ReviewNodeKind.FanOut)
                diagnostics.Add(Error(Codes.InvalidParameter, $"Join '{node.Id}' must gather a fan-out.", [node.Id]));
            else if (!graph.HasPredecessor(node.Id, fanOut, byStep: false))
                diagnostics.Add(Error(Codes.UnavailableInput, $"Fan-out '{fanOut}' must precede '{node.Id}' on every path.", [node.Id]));
        }
    }

    private static bool ContinuesOnFailure(WorkflowNode node, Func<WorkflowNode, bool>? resolve) =>
        resolve?.Invoke(node) ?? node.StringParam("onFailure") == "continue";

    private static bool ProducerAvailable(
        ValidationGraph graph, WorkflowNode producer, WorkflowNode consumer, Func<WorkflowNode, bool>? continuesOnFailure)
    {
        if (producer.Id == consumer.Id || graph.Entry?.Id is not { } entry)
            return false;

        var failedExit = ContinuesOnFailure(producer, continuesOnFailure)
            ? ReviewWorkflowRouting.NextEdge(graph.Outgoing(producer.Id), ReviewWorkflowRouting.FailureOutcome(producer))
            : null;
        var visited = new HashSet<(string Id, bool Produced)>();
        var pending = new Stack<(string Id, bool Produced)>();
        pending.Push((entry, false));
        var reached = false;
        while (pending.TryPop(out var state))
        {
            if (!visited.Add(state))
                continue;
            if (state.Id == consumer.Id)
            {
                if (!state.Produced)
                    return false;
                reached = true;
                continue;
            }
            foreach (var edge in graph.Outgoing(state.Id))
            {
                Push(edge, state.Produced || state.Id == producer.Id);
                if (ReferenceEquals(edge, failedExit))
                    Push(edge, state.Produced);
            }
        }
        return reached;

        void Push(WorkflowEdge edge, bool produced)
        {
            pending.Push((edge.To, edge.To != entry && produced));
            if (edge.Overflow is { } overflow)
                pending.Push((overflow, overflow != entry && produced));
        }
    }

    private static void ValidateNode(WorkflowNode node, HashSet<string> ids, DiagnosticList diagnostics)
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

    private static void ValidateEdges(ValidationGraph graph, DiagnosticList diagnostics)
    {
        foreach (var edge in graph.Definition.Edges)
        {
            var source = graph.Node(edge.From);
            if (source is not null && edge.Labels.Count > 0)
            {
                var allowed = source.Kind switch
                {
                    ReviewNodeKind.Gate => ReviewDecideSteps.GateOutcomes(source),
                    ReviewNodeKind.Wait => ReviewDecideSteps.WaitOutcomes(source),
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
                else if (graph.Node(edge.Overflow) is not { } overflow)
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

    private static void ValidateSections(WorkflowDefinition workflow, HashSet<string> ids, DiagnosticList diagnostics)
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
    /// Design 3.2: remove gates and waits that break cycles, and iteration-limited edges.
    /// Find strongly connected components of the remaining graph using an explicit DFS stack.
    /// </summary>
    private static void ValidateCycles(ValidationGraph graph, HashSet<string> ids, HashSet<string> bounded, DiagnosticList diagnostics)
    {
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var low = new Dictionary<string, int>(StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var componentStack = new Stack<string>();
        var search = new Stack<(string Id, int NextEdge)>();

        foreach (var root in ids)
        {
            if (bounded.Contains(root) || indices.ContainsKey(root))
                continue;

            Discover(root);
            while (search.TryPop(out var frame))
            {
                var outgoing = graph.Outgoing(frame.Id);
                if (frame.NextEdge < outgoing.Count)
                {
                    search.Push((frame.Id, frame.NextEdge + 1));
                    var edge = outgoing[frame.NextEdge];
                    if (edge.MaxIterations is not null || !ids.Contains(edge.To) || bounded.Contains(edge.To))
                        continue;

                    if (!indices.ContainsKey(edge.To))
                        Discover(edge.To);
                    else if (onStack.Contains(edge.To))
                        low[frame.Id] = Math.Min(low[frame.Id], indices[edge.To]);
                    continue;
                }

                if (search.TryPeek(out var parent))
                    low[parent.Id] = Math.Min(low[parent.Id], low[frame.Id]);
                if (low[frame.Id] != indices[frame.Id])
                    continue;

                var component = new List<string>();
                string id;
                do
                {
                    id = componentStack.Pop();
                    onStack.Remove(id);
                    component.Add(id);
                } while (id != frame.Id);

                component.Reverse();
                var members = component.ToHashSet(StringComparer.Ordinal);
                var closing = component.SelectMany(graph.Outgoing)
                    .LastOrDefault(edge => edge.MaxIterations is null && members.Contains(edge.To));
                if (closing is not null)
                {
                    diagnostics.Add(Error(Codes.UnboundedCycle,
                        $"The cycle through {string.Join(" -> ", component)} has no gate or wait node and no maxIterations; insert a gate or add an iteration limit on {closing.From} -> {closing.To}.",
                        component, closing));
                }
            }
        }

        void Discover(string id)
        {
            indices[id] = low[id] = indices.Count;
            onStack.Add(id);
            componentStack.Push(id);
            search.Push((id, 0));
        }
    }

    private sealed class ValidationGraph(WorkflowDefinition definition)
    {
        private readonly Dictionary<string, WorkflowNode> _nodes = definition.Nodes
            .DistinctBy(node => node.Id, StringComparer.Ordinal).ToDictionary(node => node.Id, StringComparer.Ordinal);
        private readonly Dictionary<string, WorkflowEdge[]> _outgoing = definition.Edges
            .GroupBy(edge => edge.From, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        private readonly Dictionary<(string Producer, bool ByStep), HashSet<string>> _reachableWithout = [];
        private HashSet<string>? _reachable;

        public WorkflowDefinition Definition { get; } = definition;
        public WorkflowNode? Entry { get; } = ReviewWorkflowRouting.Entry(definition);
        public WorkflowNode? Node(string id) => _nodes.GetValueOrDefault(id);
        public IReadOnlyList<WorkflowEdge> Outgoing(string id) => _outgoing.GetValueOrDefault(id) ?? [];

        public bool HasPredecessor(string consumer, string producer, bool byStep)
        {
            _reachable ??= Reachable(_ => false);
            if (!_reachable.Contains(consumer))
                return false;

            if (!_reachableWithout.TryGetValue((producer, byStep), out var bypass))
            {
                bypass = Reachable(id => byStep ? Node(id)?.Run == producer : id == producer);
                _reachableWithout.Add((producer, byStep), bypass);
            }

            return !bypass.Contains(consumer);
        }

        private HashSet<string> Reachable(Func<string, bool> stopAfter)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>();
            if (Entry is { } entry)
                pending.Push(entry.Id);
            while (pending.TryPop(out var id))
            {
                if (!visited.Add(id) || stopAfter(id))
                    continue;
                foreach (var edge in Outgoing(id))
                {
                    pending.Push(edge.To);
                    if (edge.Overflow is { } overflow)
                        pending.Push(overflow);
                }
            }
            return visited;
        }
    }

    private sealed class DiagnosticList
    {
        private readonly List<WorkflowDiagnostic> _items = [];

        public void Add(WorkflowDiagnostic diagnostic)
        {
            if (_items.Count >= MaxDiagnostics)
                throw new ValidationException("graph", $"A workflow may report at most {MaxDiagnostics} diagnostics.", "workflow_diagnostic_limit");
            _items.Add(diagnostic);
        }

        public WorkflowValidation Result() => new(_items);
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
        new(code, WorkflowDiagnosticSeverity.Error, message, nodeIds, edge);

    private static WorkflowDiagnostic Warning(string code, string message, IReadOnlyList<string> nodeIds) =>
        new(code, WorkflowDiagnosticSeverity.Warning, message, nodeIds);
}
