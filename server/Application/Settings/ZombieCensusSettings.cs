namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-0298: Class B OS zombie census (report-only). Thresholds match the <c>reap-zombie-agents</c> command
/// (<c>scripts/lib/Antiphon.Nightly/ZombieAgentReaper.cs</c>). There is no execute switch on this settings object.
/// </summary>
public sealed class ZombieCensusSettings
{
    public bool Enabled { get; set; } = true;

    public string RecurringJobId { get; set; } = "antiphon:zombie-census";

    /// <summary>Five-field daily cron. Default 09:30.</summary>
    public string Cron { get; set; } = "30 9 * * *";

    /// <summary>IANA timezone id. Default Europe/London.</summary>
    public string TimeZoneId { get; set; } = "Europe/London";

    /// <summary>Age/done floor in minutes (command <c>--min-done-minutes</c>).</summary>
    public int MinDoneMinutes { get; set; } = 120;

    /// <summary>Quiet-activity floor in hours for the OS <c>EndedButAlive</c> label (command <c>--quiet-hours</c>).</summary>
    public int QuietHours { get; set; } = 6;

    /// <summary>Pid-reuse tolerance in seconds (command <c>--pid-reuse-tolerance-sec</c>).</summary>
    public int PidReuseToleranceSeconds { get; set; } = 5;

    /// <summary>
    /// Session-runner log root used for pty-host manifests and ansi mtimes. Windows backslash form.
    /// </summary>
    public string SessionLogPath { get; set; } = @"C:\logs\antiphon\session-runner";
}
