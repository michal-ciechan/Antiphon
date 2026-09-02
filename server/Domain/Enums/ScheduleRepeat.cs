namespace Antiphon.Server.Domain.Enums;

/// <summary>Typed recurrence shapes. No cron (CARD-0057 D2).</summary>
public enum ScheduleRepeat
{
    /// <summary>A single UTC instant. Re-arms to null after the claim.</summary>
    Once = 0,

    /// <summary>Every N minutes, drift-free from an UTC anchor.</summary>
    Interval = 1,

    /// <summary>HH:mm on a day-of-week mask in a named zone.</summary>
    Daily = 2,
}
