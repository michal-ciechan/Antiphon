namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-0298: Class B OS zombie census (report-only). Thresholds match
/// <c>scripts/reap-zombie-agents.ps1</c>. There is no execute switch on this settings object.
/// </summary>
public sealed class ZombieCensusSettings
{
    public bool Enabled { get; set; } = true;

    public string RecurringJobId { get; set; } = "antiphon:zombie-census";

    /// <summary>Five-field daily cron. Default 09:30.</summary>
    public string Cron { get; set; } = "30 9 * * *";

    /// <summary>IANA timezone id. Default Europe/London.</summary>
    public string TimeZoneId { get; set; } = "Europe/London";

    /// <summary>Age/done floor in minutes (script <c>-MinDoneMinutes</c>).</summary>
    public int MinDoneMinutes { get; set; } = 120;

    /// <summary>Quiet-activity floor in hours for the OS <c>EndedButAlive</c> label (script <c>-QuietHours</c>).</summary>
    public int QuietHours { get; set; } = 6;

    /// <summary>Pid-reuse tolerance in seconds (script <c>-PidReuseToleranceSec</c>).</summary>
    public int PidReuseToleranceSeconds { get; set; } = 5;

    /// <summary>
    /// Session-runner log root used for pty-host manifests and ansi mtimes. Windows backslash form.
    /// </summary>
    public string SessionLogPath { get; set; } = @"C:\logs\antiphon\session-runner";
}
