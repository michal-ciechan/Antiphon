namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-0726: runner-outage grace and the child-journal backstop. Defaults match the
/// orchestrator's previous loop (180 s grace, 15 min sweep, 5 min journal age).
/// </summary>
public sealed class AlarmSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>How long a runner may be ineligible before the outage is raised. Must be positive.</summary>
    public int RunnerGraceSeconds { get; set; } = 180;

    /// <summary>
    /// CARD-1666 A-2: how long a draining runner that is offline stays excluded from the outage
    /// alarm. Past it (or with no drain start) the alarm raises again. Retired runners have no bound.
    /// Must be positive and at most <see cref="RunnerDrainMaxMinutesLimit"/> (CARD-1671).
    /// </summary>
    public int RunnerDrainMaxMinutes { get; set; } = 30;

    /// <summary>CARD-1671: seven days, the largest <see cref="RunnerDrainMaxMinutes"/>.</summary>
    public const int RunnerDrainMaxMinutesLimit = 7 * 24 * 60;

    /// <summary>Backstop sweep period. Must be positive.</summary>
    public int SweepMinutes { get; set; } = 15;

    /// <summary>A journal record younger than this is an operation in flight. Must be positive.</summary>
    public int JournalStaleMinutes { get; set; } = 5;

    /// <summary>Off: no repository is inspected.</summary>
    public bool JournalEnabled { get; set; } = true;
}
