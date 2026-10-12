using System.Globalization;

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
    /// <c>TryRemoveAsync</c> on <c>Eligible</c> rows. CardDone destruction also
    /// requires <see cref="CardDoneAfterUtc"/>.
    /// </summary>
    public bool Execute { get; set; }

    /// <summary>
    /// CARD-1033 D-2. UTC instant. A Done revision is eligible only when its immutable
    /// revision time is strictly later. Null, equality, and a missing value stay inventory-only.
    /// Never default this from process start, migration time, or the epoch.
    /// </summary>
    public string? CardDoneAfterUtc { get; set; }

    /// <summary>
    /// Settled tasks younger than this stay <c>Settling</c> (a land may be queued or held).
    /// </summary>
    public int MinSettledMinutes { get; set; } = 120;

    /// <summary>Shared action budget across both cleanup lanes. Default 25.</summary>
    public int MaxActionsPerRun { get; set; } = 25;

    /// <summary>Maximum candidate rows returned in one run-result page.</summary>
    public int RunResultPageSize { get; set; } = 50;

    /// <summary>
    /// CARD-0664 D-1: a workspace-use <c>Launch</c> reservation younger than this blocks retirement
    /// whatever its owner says (the admit-before-status window). Default 15; clamped to at least 1.
    /// </summary>
    public int LaunchGraceMinutes { get; set; } = 15;

    public const long MicrosecondTicks = 10;

    /// <summary>Format a UTC instant the startup parser accepts. Unspecified values are UTC instants, not local.</summary>
    public static string FormatCardDoneAfterUtc(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Local
            ? value.ToUniversalTime()
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Null and missing mean no cohort (inventory only). Any other text must be an explicit UTC timestamp.
    /// </summary>
    public static bool TryReadCardDoneAfterUtc(string? text, out DateTime? cutoffUtc, out string? failure)
    {
        cutoffUtc = null;
        failure = null;
        if (text is null) return true;
        if (string.IsNullOrWhiteSpace(text))
        {
            failure = "WorktreeResidue:CardDoneAfterUtc is malformed.";
            return false;
        }

        var trimmed = text.Trim();
        var explicitUtc = trimmed.EndsWith('Z')
            || trimmed.EndsWith("+00:00", StringComparison.Ordinal)
            || trimmed.EndsWith("+0000", StringComparison.Ordinal);
        if (!DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            failure = "WorktreeResidue:CardDoneAfterUtc is malformed.";
            return false;
        }

        if (!explicitUtc || parsed.Offset != TimeSpan.Zero)
        {
            failure = "WorktreeResidue:CardDoneAfterUtc must be UTC.";
            return false;
        }

        cutoffUtc = parsed.UtcDateTime;
        return true;
    }
}
