using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Per-(kind, model-alias) pause (CARD-0022). Active = <see cref="ClearedAt"/> is null.
/// CARD-0022 writes <see cref="ModelAvailabilitySource.AutoDetected"/>; CARD-0309 writes
/// <see cref="ModelAvailabilitySource.Manual"/> onto the same row. One active row per key.
/// </summary>
public class ModelAvailabilityHold
{
    public Guid Id { get; set; }

    public AgentKind Kind { get; set; }

    /// <summary>
    /// Canonical family alias (<c>fable</c>, <c>opus</c>, <c>sonnet</c>, <c>haiku</c>,
    /// <c>grok-4.6</c>, <c>gpt-6-astra</c>, …) or <c>*</c> for a kind-wide hold (CARD-0309).
    /// Never a stub <c>&lt;synthetic&gt;</c> model id.
    /// </summary>
    public string ModelAlias { get; set; } = string.Empty;

    public ModelAvailabilitySource Source { get; set; }

    /// <summary>
    /// UTC. Null = until cleared (an open-ended Manual hold). AutoDetected holds are timed
    /// (CARD-0335); a legacy AutoDetected null is materialized to HitAt + fallback on sweep or
    /// lazy read.
    /// </summary>
    public DateTime? DisabledUntil { get; set; }

    public DateTime HitAt { get; set; }

    public DateTime? ClearedAt { get; set; }

    /// <summary>Stub text, capped.</summary>
    public string? RawText { get; set; }

    /// <summary>AutoDetected only — the session whose stub produced this hold.</summary>
    public Guid? SourceSessionId { get; set; }

    /// <summary>When the stub sat on a delegate.</summary>
    public Guid? SourceTaskId { get; set; }

    /// <summary>
    /// Short operator sentence, e.g. <c>session-limit resets 18:10 Europe/London</c> or
    /// <c>Fable 5 per-model cap (no reset stated)</c>.
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}
