using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Review.Workflows;

/// <summary>A node. <see cref="Params"/> holds every key other than kind, run and description.</summary>
public sealed record WorkflowNode(
    string Id,
    ReviewNodeKind Kind,
    string? Run,
    string? Description,
    IReadOnlyDictionary<string, object?> Params)
{
    public string? StringParam(string name) => Params.TryGetValue(name, out var v) ? v?.ToString() : null;

    public IReadOnlyList<string> ListParam(string name)
    {
        if (!Params.TryGetValue(name, out var v) || v is null)
            return [];
        if (v is string single)
            return [single];
        return v is IEnumerable<object?> items ? items.Select(i => i?.ToString() ?? "").ToList() : [v.ToString() ?? ""];
    }

    /// <summary>The decisions a gate allows (<c>decisions:</c>).</summary>
    public IReadOnlyList<string> Decisions => ListParam("decisions");
}

/// <summary>
/// An edge. <see cref="Label"/> is the source output(s) that take it, e.g. <c>findings</c> or
/// <c>push|recheck</c>. A limited back edge carries <see cref="MaxIterations"/> and the gate that
/// opens over the limit (<see cref="Overflow"/>, D-2).
/// </summary>
public sealed record WorkflowEdge(
    string From,
    string To,
    string? Label,
    int? MaxIterations,
    string? Overflow,
    string? Description)
{
    public IReadOnlyList<string> Labels =>
        Label is null ? [] : Label.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>A named, presentation-only group of nodes (design 3.3, D-27).</summary>
public sealed record WorkflowSection(string Id, string Title, IReadOnlyList<string> NodeIds, bool Collapsed);

/// <summary>A resolved workflow definition (design 3.3).</summary>
public sealed record WorkflowDefinition(
    string Name,
    int Version,
    IReadOnlyList<WorkflowSection> Sections,
    IReadOnlyList<WorkflowNode> Nodes,
    IReadOnlyList<WorkflowEdge> Edges)
{
    public WorkflowNode? Node(string id) => Nodes.FirstOrDefault(n => n.Id == id);

    /// <summary>
    /// SHA-256 of the definition's canonical JSON, lower-case hex: the revision identity. Section
    /// layout and descriptions are included, so any edit is a new revision (design 3.3).
    /// </summary>
    public string Hash()
    {
        var canonical = new
        {
            name = Name,
            version = Version,
            sections = Sections.OrderBy(s => s.Id, StringComparer.Ordinal)
                .Select(s => new { s.Id, s.Title, s.NodeIds, s.Collapsed }),
            nodes = Nodes.OrderBy(n => n.Id, StringComparer.Ordinal).Select(n => new
            {
                n.Id,
                kind = n.Kind.ToString(),
                n.Run,
                n.Description,
                @params = n.Params.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value),
            }),
            edges = Edges.Select(e => new { e.From, e.To, e.Label, e.MaxIterations, e.Overflow, e.Description }),
        };
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Back edges: every edge with <see cref="WorkflowEdge.MaxIterations"/>, plus every edge that closes
    /// a cycle in a depth-first walk from the entry nodes in declaration order.
    /// </summary>
    public IReadOnlySet<WorkflowEdge> BackEdges()
    {
        var result = new HashSet<WorkflowEdge>(Edges.Where(e => e.MaxIterations is not null));
        var outgoing = Edges.GroupBy(e => e.From).ToDictionary(g => g.Key, g => g.ToList());
        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 1 on stack, 2 done
        var targets = Edges.Select(e => e.To).ToHashSet(StringComparer.Ordinal);
        var roots = Nodes.Where(n => !targets.Contains(n.Id)).Select(n => n.Id).Concat(Nodes.Select(n => n.Id));

        void Visit(string id)
        {
            state[id] = 1;
            foreach (var edge in outgoing.GetValueOrDefault(id) ?? [])
            {
                var next = state.GetValueOrDefault(edge.To);
                if (next == 1)
                    result.Add(edge);
                else if (next == 0)
                    Visit(edge.To);
            }

            state[id] = 2;
        }

        foreach (var root in roots)
        {
            if (state.GetValueOrDefault(root) == 0)
                Visit(root);
        }

        return result;
    }

    /// <summary>Node kinds (design 3.1) are written in lower kebab case in YAML (<c>fan-out</c>).</summary>
    public static string KindToYaml(ReviewNodeKind kind) => kind switch
    {
        ReviewNodeKind.FanOut => "fan-out",
        _ => kind.ToString().ToLowerInvariant(),
    };

    public static bool TryParseKind(string? text, out ReviewNodeKind kind)
    {
        kind = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var folded = text.Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);
        return Enum.TryParse(folded, ignoreCase: true, out kind) && Enum.IsDefined(kind);
    }
}
