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
/// </summary>
public class RoutingPin
{
    public Guid Id { get; set; }

    /// <summary>Null = stage-wide (this role, every card). Set = this card's stage only.</summary>
    public Guid? CardId { get; set; }

    public Card? Card { get; set; }

    /// <summary>Never <see cref="AgentTaskRole.Check"/> — a check row is about a task, not a card.</summary>
    public AgentTaskRole Role { get; set; }

    public RoutingPinProvenance Provenance { get; set; }

    public RoutingPinStrength Strength { get; set; }

    /// <summary>Null = do not constrain which program runs it.</summary>
    public AgentKind? AgentKind { get; set; }

    /// <summary>
    /// Null = take the role policy's tier. The alias the pin means is
    /// <c>ModelLevelAliases.For(kind, level)</c> — reasoning effort is this axis, not a column
    /// of its own.
    /// </summary>
    public AgentModelLevel? ModelLevel { get; set; }

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
}
