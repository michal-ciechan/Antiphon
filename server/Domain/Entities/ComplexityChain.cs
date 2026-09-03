using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Ordered (kind, level) fallback list for one (Role?, Complexity) cell (CARD-0090, CARD-0332).
/// Active = <see cref="ClearedAt"/> is null. One active row per (Role?, Complexity); a role
/// cell outranks the any-role row as a whole; config fills a complexity with neither.
/// Provenance is overwrite protection, same rule as <see cref="RoutingPin"/>: Auto never
/// replaces Human.
/// </summary>
public class ComplexityChain
{
    public static readonly JsonSerializerOptions CandidatesJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    public Guid Id { get; set; }

    public TaskComplexity Complexity { get; set; }

    /// <summary>
    /// Null = any role (CARD-0090's row). A non-null value is one cell of the matrix.
    /// </summary>
    public AgentTaskRole? Role { get; set; }

    /// <summary>
    /// Ordered complete pairs, enum member names on disk:
    /// <c>[{"agentKind":"ClaudeCode","modelLevel":"Frontier"}, …]</c>. 1..8 entries.
    /// </summary>
    public string CandidatesJson { get; set; } = "[]";

    public RoutingPinProvenance Provenance { get; set; }

    public string Reason { get; set; } = string.Empty;

    /// <summary>UTC. Past this, the row lazily self-clears (a temporary chain until Friday).</summary>
    public DateTime? NotAfter { get; set; }

    public Guid? SourceTaskId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? ClearedAt { get; set; }

    public IReadOnlyList<ComplexityCandidatePair> ParseCandidates() =>
        ParseCandidates(CandidatesJson);

    public static IReadOnlyList<ComplexityCandidatePair> ParseCandidates(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<ComplexityCandidatePair>>(json, CandidatesJsonOptions)
                ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string SerializeCandidates(IReadOnlyList<ComplexityCandidatePair> candidates) =>
        JsonSerializer.Serialize(candidates, CandidatesJsonOptions);
}

/// <summary>One complete (kind, level) pair on a complexity chain. Both fields required.</summary>
public sealed record ComplexityCandidatePair(AgentKind AgentKind, AgentModelLevel ModelLevel);
