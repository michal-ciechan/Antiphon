namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0824 S1 (<c>PhoneHome:WorkspaceSlots</c>). With <see cref="Enabled"/> false the runner keeps the
/// per-task <c>task-&lt;id&gt;</c> mirror path byte for byte and never creates a slot ledger.
/// </summary>
public sealed class PhoneHomeWorkspaceSlotSettings
{
    public bool Enabled { get; set; }

    /// <summary>Slots above seat capacity for settled tasks that still await release (D-5).</summary>
    public int Headroom { get; set; } = 4;

    /// <summary>Lingering-process deferrals before the directory is quarantined (D-11).</summary>
    public int LingerRetries { get; set; } = 3;

    /// <summary>How long a lease waits for a Releasing slot before it grows an overflow slot (D-13).</summary>
    public int OverflowWaitSeconds { get; set; } = 10;

    public int MaxOverflowSlots { get; set; } = 4;
    public int SlotBytesEstimateGiB { get; set; } = 3;
    public int PoolDiskBudgetGiB { get; set; } = 64;
    public int IdleRetireHours { get; set; } = 72;
    public int MaxQuarantinedDirectories { get; set; } = 4;
    public int MaxRehomesPerSlotPerDay { get; set; } = 3;

    /// <summary>Off, Daily or Weekly (D-16).</summary>
    public string ColdResetSchedule { get; set; } = "Weekly";
    public DayOfWeek ColdResetDayOfWeek { get; set; } = DayOfWeek.Sunday;
    public int ColdResetHourUtc { get; set; } = 2;

    /// <summary>H-5: evidence retained per task before the wipe, and for how long.</summary>
    public long EvidenceCapBytes { get; set; } = 256L * 1024 * 1024;
    public int EvidenceRetentionDays { get; set; } = 7;
}
