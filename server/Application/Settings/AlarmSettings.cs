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

    /// <summary>Backstop sweep period. Must be positive.</summary>
    public int SweepMinutes { get; set; } = 15;

    /// <summary>A journal record younger than this is an operation in flight. Must be positive.</summary>
    public int JournalStaleMinutes { get; set; } = 5;

    /// <summary>Off: no repository is inspected.</summary>
    public bool JournalEnabled { get; set; } = true;
}
