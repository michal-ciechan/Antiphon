using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Review.Workflows;

internal sealed record ReviewPublishConfiguration(string Collate, string Verify, string? Categorise, string? Select)
{
    public const string Step = "Publish";
    public const string InputsField = "inputs";
    public const string TemplateField = "summaryTemplate";
    public const string CollateStep = "CollateFindings";
    public const string VerifyStep = "VerifyFindings";
    public const int MaxInputs = 8;
    public const string KindError = "Publish requires a Code node.";

    private enum Role { Collate, Verify, Categorise, Select }

    public static bool HasInvalidKind(WorkflowNode node) => node.Run == Step && node.Kind != ReviewNodeKind.Code;

    public static bool TryRead(WorkflowDefinition workflow, WorkflowNode consumer, out ReviewPublishConfiguration? configuration)
    {
        configuration = null;
        if (!consumer.Params.TryGetValue(InputsField, out var raw))
            return true;
        if (raw is not IEnumerable<object?> values)
            return false;

        var ids = values.Take(MaxInputs + 1).ToList();
        if (ids.Count > MaxInputs)
            return false;

        var children = workflow.Nodes.Where(n => n.Kind == ReviewNodeKind.FanOut)
            .Select(n => n.StringParam("node")).ToHashSet(StringComparer.Ordinal);
        var sources = new Dictionary<Role, string>();
        foreach (var value in ids)
        {
            if (value is not string id || string.IsNullOrWhiteSpace(id) || id == consumer.Id || children.Contains(id)
                || ProducerRole(workflow.Node(id)) is not { } role || !sources.TryAdd(role, id))
                return false;
        }

        if (!sources.TryGetValue(Role.Collate, out var collate) || !sources.TryGetValue(Role.Verify, out var verify))
            return false;

        configuration = new(collate, verify, sources.GetValueOrDefault(Role.Categorise), sources.GetValueOrDefault(Role.Select));
        return true;
    }

    public static void Validate(WorkflowDefinition workflow, WorkflowNode node, List<WorkflowDiagnostic> diagnostics)
    {
        if (node.Run != Step)
            return;
        if (HasInvalidKind(node))
        {
            diagnostics.Add(Error("invalid-publish-kind", "kind", KindError));
            return;
        }

        if (!TryRead(workflow, node, out var configuration))
            diagnostics.Add(Error("invalid-input", InputsField, "Declare one CollateFindings and one VerifyFindings node, and at most one Categorise and one SelectChecks node."));
        else if (configuration is not null && !configuration.RequiredInputsPrecedePublish(workflow, node.Id))
            diagnostics.Add(Error("unavailable-input", InputsField, "The declared collate and verify must precede Publish on every path in the iteration, and Verify must use that collate."));

        if (node.Params.TryGetValue(TemplateField, out var raw)
            && !ReviewSummaryTemplate.TryParse(raw, configuration, out _))
            diagnostics.Add(Error("invalid-summary-template", TemplateField,
                "Use a bounded summary template with known, bound slots and exactly one standalone defaultBody or findings slot in visible top-level Markdown text outside HTML containers. Preceding HTML must use recognized, balanced tags."));

        WorkflowDiagnostic Error(string code, string field, string message) =>
            new(code, WorkflowDiagnosticSeverity.Error, message, [node.Id]) { Field = field };
    }

    private static Role? ProducerRole(WorkflowNode? node) => (node?.Kind, node?.Run) switch
    {
        (ReviewNodeKind.Join, CollateStep) => Role.Collate,
        (ReviewNodeKind.Completion, VerifyStep) => Role.Verify,
        (ReviewNodeKind.Decision, "Categorise") => Role.Categorise,
        (ReviewNodeKind.Decision, "SelectChecks") => Role.Select,
        _ => null,
    };

    private bool RequiredInputsPrecedePublish(WorkflowDefinition workflow, string publish)
    {
        var entry = workflow.Nodes.FirstOrDefault(n => n.Run == "Intake")?.Id ?? workflow.Nodes[0].Id;
        var routes = workflow.Edges.ToLookup(e => e.From, StringComparer.Ordinal);
        var reachable = Reachable(entry);
        return reachable.Contains(publish)
            && !Reachable(entry, Verify).Contains(publish)
            && !Reachable(entry, Collate).Overlaps([Verify, publish])
            && !workflow.Nodes.Any(n => n.Run == CollateStep && n.Id != Collate
                && reachable.Contains(n.Id) && Reachable(n.Id, Collate).Contains(Verify));

        HashSet<string> Reachable(string start, string? excluded = null)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>();
            pending.Push(start);
            while (pending.TryPop(out var current))
            {
                if (current == excluded || !seen.Add(current))
                    continue;
                foreach (var edge in routes[current])
                {
                    if (edge.To != entry)
                        pending.Push(edge.To);
                    if (edge.Overflow is { } overflow && overflow != entry)
                        pending.Push(overflow);
                }
            }

            return seen;
        }
    }
}
