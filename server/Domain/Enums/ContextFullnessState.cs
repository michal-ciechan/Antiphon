namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// Why <c>ContextFullness</c> is a number or null (CARD-0178). Distinct from the
/// occupancy calculation itself — four reasons used to collapse to one null.
/// </summary>
public enum ContextFullnessState
{
    /// <summary>A usage-bearing row won and no later compact/clear invalidated it.</summary>
    Known = 0,

    /// <summary>No usage-bearing row exists yet (pre-first-turn, or empty transcript).</summary>
    NoUsageYet = 1,

    /// <summary>A CompactBoundary landed after the newest usage-bearing row.</summary>
    Compacted = 2,

    /// <summary>A <c>/clear</c> local command landed after the newest usage-bearing row.</summary>
    Cleared = 3,

    /// <summary>
    /// Provider contract is Degraded + SelfReported (Grok's suppressed fullness, CARD-0153 S5).
    /// The client renders no badge.
    /// </summary>
    Suppressed = 4,
}
