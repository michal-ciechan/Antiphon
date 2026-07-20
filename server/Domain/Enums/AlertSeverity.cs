namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// Shared severity scale for agent incidents and (later) the alert pipeline.
/// Order matters: routing thresholds compare with ≥.
/// </summary>
public enum AlertSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3,
}
