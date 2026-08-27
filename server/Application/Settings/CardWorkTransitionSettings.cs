namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-0040: how often cards are moved from delegated-task evidence, and when a card in In
/// Progress with nobody on it starts asking for attention. Bound from the <c>CardTransitions</c>
/// configuration section.
/// </summary>
public sealed class CardWorkTransitionSettings
{
    /// <summary>
    /// Off means the feature does not exist: no sweep runs, no card moves itself, and nothing else
    /// changes. The stale attention row (<c>AttentionKind.CardStalled</c>) is a read-time
    /// projection and is deliberately NOT gated on this — detection is not automation.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How often the sweep runs. 60 s rather than event-driven: every input is a durable row, so a
    /// sweep is exact rather than approximate, one code path covers the backfill and every server
    /// outage with no replay logic, and a second writer beside the settle site is the shape that
    /// produced CARD-0056's flap counter. The board is not a real-time surface.
    /// </summary>
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>
    /// How long a card may sit In Progress with no open bound task and no live session before the
    /// attention feed calls it stale. Seven days is the number the card itself uses; anything
    /// shorter turns every weekend into a row.
    /// </summary>
    public int StaleAfterDays { get; set; } = 7;
}
