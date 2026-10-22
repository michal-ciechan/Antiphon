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
    /// revision time is strictly later. Null, empty, whitespace, equality, and a missing value stay inventory-only.
    /// Never default this from process start, migration time, or the epoch.
    /// </summary>
    public string? CardDoneAfterUtc { get; set; }

    /// <summary>
    /// Settled tasks younger than this stay <c>Settling</c> (a land may be queued or held).
    /// </summary>
    public int MinSettledMinutes { get; set; } = 120;

    /// <summary>Shared action budget across both cleanup lanes. Default 25.</summary>
    public int MaxActionsPerRun { get; set; } = 25;

    /// <summary>
    /// CARD-1446 D-1. An Active, unpublished landing in a resumable phase whose task is terminal,
    /// with no pending request and no update for this many days, is marked Refused by the daily
    /// job. 0 disables the reconciler. Default 7.
    /// </summary>
    public int AbandonedLandingDays { get; set; } = 7;

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
    /// Null, empty, and whitespace mean no cohort (inventory only). The JSON provider binds a
    /// JSON null to an empty string, so the shipped value is absence. Any other text must be a
    /// complete UTC timestamp: calendar date, time of day to the second, and Z or a zero offset.
    /// </summary>
    public static bool TryReadCardDoneAfterUtc(string? text, out DateTime? cutoffUtc, out string? failure)
    {
        cutoffUtc = null;
        failure = null;
        if (string.IsNullOrWhiteSpace(text)) return true;

        var trimmed = text.Trim();
        if (DateTime.TryParseExact(trimmed, CompleteUtcFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var utc))
        {
            cutoffUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return true;
        }

        if (DateTime.TryParseExact(trimmed, UnzonedFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            || DateTimeOffset.TryParseExact(trimmed, OffsetFormats, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
        {
            failure = "WorktreeResidue:CardDoneAfterUtc must be UTC.";
            return false;
        }

        failure = "WorktreeResidue:CardDoneAfterUtc is malformed.";
        return false;
    }

    // Exact formats only. DateTime.Parse / TryParse fill a missing date from today and a missing time from midnight.
    private static readonly string[] CompleteUtcFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mm:ss'+00:00'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'+00:00'",
        "yyyy-MM-dd'T'HH:mm:ss'+0000'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'+0000'",
    ];

    private static readonly string[] UnzonedFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
    ];

    private static readonly string[] OffsetFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
        "yyyy-MM-dd'T'HH:mm:ss zzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF zzz",
    ];
}
