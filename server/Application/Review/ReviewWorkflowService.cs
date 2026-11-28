using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Review.Workflows;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Review;

/// <summary>The default workflow and graph validation over the contract's <c>WorkflowGraphDto</c>.</summary>
public sealed class ReviewWorkflowService
{
    private readonly Lazy<WorkflowDefinition> _default = new(ReviewWorkflowYaml.LoadDefault);
    private readonly Lazy<string> _defaultHash;

    private readonly IOptions<ReviewDecisionSettings> _decisions;
    private readonly IOptions<ReviewCompletionSettings> _completions;
    private readonly IOptions<ReviewPolicySettings> _policy;

    public ReviewWorkflowService(
        IOptions<ReviewDecisionSettings> decisions,
        IOptions<ReviewCompletionSettings> completions,
        IOptions<ReviewPolicySettings> policy)
    {
        _decisions = decisions;
        _completions = completions;
        _policy = policy;
        _defaultHash = new Lazy<string>(() => _default.Value.Hash());
    }

    public WorkflowDefinition Default => _default.Value;
    public string DefaultHash => _defaultHash.Value;

    public WorkflowGraphDto GetDefault() => ToDto(Default, revisionId: null);

    /// <summary>How a revision's graph is stored (<c>ReviewWorkflowRevisions.GraphJson</c>).</summary>
    public static readonly JsonSerializerOptions GraphJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>A stored revision's graph as a definition, for the executor's walk.</summary>
    public static WorkflowDefinition FromStoredGraph(string graphJson) =>
        FromDto(JsonSerializer.Deserialize<WorkflowGraphDto>(graphJson, GraphJsonOptions)
            ?? throw new InvalidOperationException("The stored workflow revision holds no graph."));

    internal async Task<WorkflowDefinition> LoadPinnedAsync(AppDbContext db, Guid? revisionId, CancellationToken ct)
    {
        if (revisionId is { } id && await db.ReviewWorkflowRevisions.AsNoTracking()
            .Where(revision => revision.Id == id).Select(revision => revision.GraphJson).SingleOrDefaultAsync(ct) is { } json)
            return FromStoredGraph(json);
        return Default;
    }

    private static readonly JsonSerializerOptions CandidateJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new CandidateNodeKindConverter() },
    };

    private const int MaxNodes = 256;
    private const int MaxEdges = 1024;
    private const int MaxSections = 256;
    private const int MaxJsonValues = 16_384;
    private const int MaxTextLength = 4096;
    // The params object is three levels below the graph root.
    private const int MaxCandidateDepth = 3 + 16;

    public static WorkflowDefinition ReadCandidate(JsonElement body)
    {
        try
        {
            if (body.ValueKind != JsonValueKind.Object)
                throw InvalidGraph();

            var remaining = MaxJsonValues;
            ValidateJson(body, 0, ref remaining);
            var graph = body.Deserialize<WorkflowGraphDto>(CandidateJsonOptions);
            if (graph is not null && graph.Nodes?.Any(node => node is null) != true
                && graph.Edges?.Any(edge => edge is null) != true
                && graph.Sections?.Any(section => section is null || section.NodeIds?.Any(id => id is null) == true) != true)
                return FromDto(graph);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            // Binding and parameter conversion both belong to the candidate's input boundary.
        }
        throw InvalidGraph();
    }

    private static BadRequestException InvalidGraph() =>
        new("Expected a workflow graph with node, edge and section objects and unique JSON keys.");

    private static void LimitCollection(JsonElement collection, string name, int limit)
    {
        if (collection.ValueKind == JsonValueKind.Array && collection.GetArrayLength() > limit)
            throw GraphLimit($"A workflow may contain at most {limit} {name}.");
    }

    private static ValidationException GraphLimit(string detail) => new("graph", detail, "workflow_graph_limit");

    private static void ValidateJson(JsonElement value, int depth, ref int remaining)
    {
        if (--remaining < 0)
            throw GraphLimit($"A workflow may contain at most {MaxJsonValues} JSON values.");
        if (depth > MaxCandidateDepth)
            throw new ValidationException("graph", "Workflow parameters may nest up to 16 levels.", "workflow_parameter_limit");

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                // The graph and graph-item DTOs bind case-insensitively. Parameter maps preserve case.
                var keys = new HashSet<string>(depth is 0 or 2 ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    var name = property.Name;
                    LimitText(name);
                    if (!keys.Add(name))
                        throw InvalidGraph();
                    if (depth == 0)
                    {
                        switch (name.ToLowerInvariant())
                        {
                            case "nodes": LimitCollection(property.Value, "nodes", MaxNodes); break;
                            case "edges": LimitCollection(property.Value, "edges", MaxEdges); break;
                            case "sections": LimitCollection(property.Value, "sections", MaxSections); break;
                        }
                    }
                    ValidateJson(property.Value, depth + 1, ref remaining);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                    ValidateJson(item, depth + 1, ref remaining);
                break;
            case JsonValueKind.String:
                LimitText(value.GetString()!);
                break;
        }
    }

    private static void LimitText(string value)
    {
        if (value.Length > MaxTextLength)
            throw GraphLimit($"Workflow text may contain at most {MaxTextLength} characters per value.");
    }

    public WorkflowValidationDto Validate(WorkflowDefinition definition)
    {
        var decisions = _decisions.Value.Chains;
        var completions = _completions.Value.Chains;
        var completionNames = completions.Keys.ToHashSet(completions.Comparer);
        // VerifyFindings can skip when its built-in completion chain has no provider.
        completionNames.Add(ReviewCompletionSettings.DefaultChain);
        var chains = new ReviewWorkflowChainNames(decisions.Keys.ToHashSet(decisions.Comparer), completionNames);
        var validation = ReviewWorkflowValidator.Validate(definition,
            name => ReviewWorkflowSteps.Find(name)?.Contract, chains,
            node => ReviewWorkflowPolicy.OnFailure(_policy.Value, node).Value == "continue");

        var edgeIndices = new Dictionary<WorkflowEdge, int>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < definition.Edges.Count; index++)
            edgeIndices.Add(definition.Edges[index], index);

        return new WorkflowValidationDto(validation.Valid, validation.Diagnostics
            .Select(d => new WorkflowDiagnosticDto(d.Code, d.Severity.ToString(), d.Message, d.NodeIds,
                d.Edge is { } e ? new WorkflowEdgeRefDto(e.From, e.To, edgeIndices[e]) : null))
            .ToList());
    }

    private sealed class CandidateNodeKindConverter : JsonConverter<ReviewNodeKind>
    {
        private const ReviewNodeKind Unrecognized = (ReviewNodeKind)(-1);

        public override ReviewNodeKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String && reader.GetString() is { } text
                && Enum.GetNames<ReviewNodeKind>().Contains(text, StringComparer.OrdinalIgnoreCase)
                && Enum.TryParse<ReviewNodeKind>(text, true, out var kind))
                return kind;
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
                return (ReviewNodeKind)number;
            reader.Skip();
            return Unrecognized;
        }

        public override void Write(Utf8JsonWriter writer, ReviewNodeKind value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
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
        (graph.Nodes ?? []).Select(n => new WorkflowNode(n.Id ?? "", n.Kind, n.Run, n.Description,
            (n.Params ?? new Dictionary<string, object?>()).ToDictionary(p => p.Key, p => ReviewJsonValues.ToClr(p.Value), StringComparer.Ordinal))).ToList(),
        (graph.Edges ?? []).Select(e => new WorkflowEdge(e.From ?? "", e.To ?? "", string.IsNullOrWhiteSpace(e.Label) ? null : e.Label, e.MaxIterations,
            string.IsNullOrWhiteSpace(e.Overflow) ? null : e.Overflow, e.Description)).ToList());
}
