using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// A standing routing instruction for the NEXT task created against a card+role (CARD-0305).
/// Active = <see cref="ClearedAt"/> is null. Two grains in one table: a per-card pin
/// (<see cref="CardId"/> set) and a stage-wide pin for a role (<see cref="CardId"/> null). One
/// active row per grain, so <see cref="Provenance"/> is overwrite protection rather than a second
/// key.
///
/// <para>Distinct from <see cref="ModelAvailabilityHold"/> (CARD-0022 / CARD-0309), which answers
/// "is this kind+alias USABLE at all" for the whole fleet. This answers "for this card+role, which
/// kind/tier SHOULD run". A pin naming a held alias consumes the hold's 409; it never writes one.</para>
///
/// <para>CARD-0322: the kind/level constraint is an ordered candidate list in
/// <see cref="CandidatesJson"/>. <see cref="AgentKind"/> / <see cref="ModelLevel"/> are the head
/// candidate, kept as unmapped properties so a one-candidate pin reads the same as before the
/// list column existed.</para>
/// </summary>
public class RoutingPin
{
    public static readonly JsonSerializerOptions CandidatesJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    private string? _candidatesJson;
    private IReadOnlyList<RoutingCandidate>? _cached;

    public Guid Id { get; set; }

    /// <summary>Null = stage-wide (this role, every card). Set = this card's stage only.</summary>
    public Guid? CardId { get; set; }

    public Card? Card { get; set; }

    /// <summary>Never a specialist role — those rows are furniture, not a card stage.</summary>
    public AgentTaskRole Role { get; set; }

    public RoutingPinProvenance Provenance { get; set; }

    public RoutingPinStrength Strength { get; set; }

    /// <summary>
    /// Ordered partial (kind, level) pairs, enum member names on disk:
    /// <c>[{"agentKind":"ClaudeCode","modelLevel":"Frontier"}, …]</c>. Null/empty = no kind/level
    /// constraint (a forbid-only or agent-only pin).
    /// </summary>
    public string? CandidatesJson
    {
        get => _candidatesJson;
        set
        {
            _candidatesJson = value;
            _cached = null;
        }
    }

    /// <summary>A STANDING agent to run it on; never a pool delegate (that is a follow-up's job).</summary>
    public Guid? AgentId { get; set; }

    /// <summary>
    /// Comma-separated canonical aliases this stage may not use, e.g. <c>fable</c>. Empty/null =
    /// none. Only consulted when the stage-wide pin is the chosen grain, or when a card pin the
    /// operator did not write (Auto) resolved the alias — a Human card pin is a deliberate
    /// exception to the stage rule (CARD-0301).
    /// </summary>
    public string? ForbiddenAliases { get; set; }

    /// <summary>
    /// UTC. The dispatcher skips a task pinned here until this instant. Create still returns 200
    /// Queued — the pin is WHY the work exists. The opposite of a fleet hold's 409, on purpose.
    /// </summary>
    public DateTime? NotBefore { get; set; }

    /// <summary>UTC. Past this, the pin lazily self-clears. Expiry, not a hold.</summary>
    public DateTime? NotAfter { get; set; }

    /// <summary>Short operator sentence, capped. "operator: CARD-0301 stays on fable until Thursday".</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Which delegate task wrote it, when the caller sent a task token. Audit only.</summary>
    public Guid? SourceTaskId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? ClearedAt { get; set; }

    /// <summary>Parsed <see cref="CandidatesJson"/>, cached for the life of this instance.</summary>
    public IReadOnlyList<RoutingCandidate> Candidates
    {
        get
        {
            _cached ??= RoutingCandidate.Parse(_candidatesJson);
            return _cached;
        }
    }

    public RoutingCandidate? Head => Candidates.Count > 0 ? Candidates[0] : null;

    /// <summary>
    /// Head candidate's kind. Unmapped: storage is <see cref="CandidatesJson"/>. Kept so existing
    /// readers and object-initializers that name the single pair still compile.
    /// </summary>
    public AgentKind? AgentKind
    {
        get => Head?.AgentKind;
        set => ReplaceHead(value, Head?.ModelLevel);
    }

    /// <summary>Head candidate's level. Unmapped; see <see cref="AgentKind"/>.</summary>
    public AgentModelLevel? ModelLevel
    {
        get => Head?.ModelLevel;
        set => ReplaceHead(Head?.AgentKind, value);
    }

    public void SetCandidates(IReadOnlyList<RoutingCandidate> candidates)
    {
        CandidatesJson = candidates.Count == 0 ? null : RoutingCandidate.Serialize(candidates);
    }

    private void ReplaceHead(AgentKind? kind, AgentModelLevel? level)
    {
        var rest = Candidates.Skip(1).ToList();
        if (kind is null && level is null)
        {
            SetCandidates(rest);
            return;
        }

        var next = new List<RoutingCandidate>(1 + rest.Count) { new(kind, level) };
        next.AddRange(rest);
        SetCandidates(next);
    }
}

/// <summary>
/// One (possibly partial) pair on a routing pin. Either field may be null: <c>-Kind Grok</c> only
/// means "Grok, level from the request or the role policy". Both null is invalid on write.
/// </summary>
public sealed record RoutingCandidate(AgentKind? AgentKind, AgentModelLevel? ModelLevel)
{
    public string Describe()
    {
        if (AgentKind is { } kind && ModelLevel is { } level)
            return $"{kind}/{level}";
        if (AgentKind is { } kindOnly)
            return kindOnly.ToString();
        if (ModelLevel is { } levelOnly)
            return $"*/{levelOnly}";
        return "*";
    }

    public static IReadOnlyList<RoutingCandidate> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<RoutingCandidate>>(json, RoutingPin.CandidatesJsonOptions)
                ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string Serialize(IReadOnlyList<RoutingCandidate> candidates) =>
        JsonSerializer.Serialize(candidates, RoutingPin.CandidatesJsonOptions);
}
