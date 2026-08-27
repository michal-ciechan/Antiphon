namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-0091: controls the periodic cleanup of machine-origin parked messages whose session has
/// no open task. Bound from the <c>ParkedMessages</c> configuration section.
/// </summary>
public sealed class ParkedMessageSweepSettings
{
    /// <summary>Off means the sweep does no reads or writes.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the durable-row sweep runs.</summary>
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>Race-hygiene window after the last delivery start or row creation.</summary>
    public int MinParkedMinutes { get; set; } = 10;

    /// <summary>Logs candidates but leaves their queue rows untouched.</summary>
    public bool DryRun { get; set; }
}
