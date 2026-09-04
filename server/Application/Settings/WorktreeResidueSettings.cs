namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-0328 S3: daily worktree-residue sweep. <see cref="Execute"/> stays false until one
/// report-only Hangfire run has been read (D5). The dashboard's "Trigger now" is the rollout pass.
/// </summary>
public sealed class WorktreeResidueSettings
{
    public bool Enabled { get; set; } = true;

    public string RecurringJobId { get; set; } = "antiphon:worktree-residue";

    /// <summary>Five-field daily cron. Default 10:00.</summary>
    public string Cron { get; set; } = "0 10 * * *";

    /// <summary>IANA timezone id. Default Europe/London.</summary>
    public string TimeZoneId { get; set; } = "Europe/London";

    /// <summary>
    /// When false the job classifies and logs only. When true it calls
    /// <c>TryRemoveAsync</c> on <c>Eligible</c> rows.
    /// </summary>
    public bool Execute { get; set; }

    /// <summary>
    /// Settled tasks younger than this stay <c>Settling</c> (a land may be queued or held).
    /// </summary>
    public int MinSettledMinutes { get; set; } = 120;
}
