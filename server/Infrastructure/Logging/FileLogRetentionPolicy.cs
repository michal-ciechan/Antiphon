using System.Globalization;

namespace Antiphon.Server.Infrastructure.Logging;

/// <summary>
/// How much of the rolling file log is kept (CARD-0043).
///
/// Retention is TIME-based. <c>retainedFileCountLimit</c> counts FILES, not days: at the write rate
/// this server had before the noisy sources were turned down, a 100 MB file filled in 21 minutes to
/// 3.5 hours depending on load, so the "14" that read as a fortnight was between 5 and 45 HOURS of
/// history — and the 2026-08-11 delegation investigation found the previous evening already rolled
/// away. The count cap stays as a pure DISK backstop (<see cref="MaxDiskBytes"/>), sized so it only
/// bites when someone deliberately re-arms a turned-down source via
/// <c>Serilog:MinimumLevel:Override</c> and the volume goes back up.
///
/// Serilog applies both caps, so effective retention is the SMALLER of the two: the time limit alone
/// cannot guarantee 5 days of history — what guarantees it is
/// <c>write rate x 5 days &lt;= <see cref="MaxDiskBytes"/></c>. <see cref="FitsDaysAtRate"/> is that
/// arithmetic, and is what the tests assert against measured MB/hour rather than an assumption.
///
/// All three values are configurable so a debugging session that raises a source back to Information
/// can raise its disk budget to match, without a rebuild.
/// </summary>
public sealed record FileLogRetentionPolicy(
    long FileSizeLimitBytes,
    int RetainedFileCountLimit,
    TimeSpan RetainedFileTimeLimit)
{
    /// <summary>Configuration section these values are read from (shared with <c>Serilog:LogPath</c>).</summary>
    public const string Section = "Serilog";

    public const string FileSizeLimitMbKey = Section + ":FileSizeLimitMb";
    public const string RetainedFileCountLimitKey = Section + ":RetainedFileCountLimit";
    public const string RetainedFileTimeLimitDaysKey = Section + ":RetainedFileTimeLimitDays";

    /// <summary>
    /// Shipped defaults: 100 MB files, 5 days of history, and a 14-file (1.4 GB) disk backstop —
    /// the same disk budget the old count-only policy had.
    /// </summary>
    public static FileLogRetentionPolicy Default { get; } = new(
        FileSizeLimitBytes: 100L * 1024 * 1024,
        RetainedFileCountLimit: 14,
        RetainedFileTimeLimit: TimeSpan.FromDays(5));

    /// <summary>Worst-case bytes on disk: every retained file at its size cap.</summary>
    public long MaxDiskBytes => FileSizeLimitBytes * RetainedFileCountLimit;

    /// <summary>
    /// Does <paramref name="days"/> of history at <paramref name="megabytesPerHour"/> fit inside the
    /// disk backstop? Non-positive rates trivially fit (nothing is being written).
    /// </summary>
    public bool FitsDaysAtRate(double days, double megabytesPerHour) =>
        megabytesPerHour <= 0
        || megabytesPerHour * 24 * days * 1024 * 1024 <= MaxDiskBytes;

    /// <summary>
    /// The sustained rate at which the count cap starts evicting before the time limit does — i.e.
    /// the rate above which <paramref name="days"/> of history no longer fits on disk.
    /// </summary>
    public double MaxMegabytesPerHourFor(double days) =>
        MaxDiskBytes / 1024d / 1024d / (24 * days);

    /// <summary>
    /// Reads the policy from configuration, falling back to <paramref name="defaults"/> for anything
    /// missing, unparseable or non-positive — a typo must never leave the log unbounded.
    /// </summary>
    public static FileLogRetentionPolicy FromConfiguration(
        IConfiguration configuration, FileLogRetentionPolicy? defaults = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var fallback = defaults ?? Default;

        var sizeMb = ReadPositiveDouble(configuration, FileSizeLimitMbKey);
        var count = ReadPositiveDouble(configuration, RetainedFileCountLimitKey);
        var days = ReadPositiveDouble(configuration, RetainedFileTimeLimitDaysKey);

        return new FileLogRetentionPolicy(
            FileSizeLimitBytes: sizeMb is { } mb ? (long)(mb * 1024 * 1024) : fallback.FileSizeLimitBytes,
            RetainedFileCountLimit: count is { } c ? (int)c : fallback.RetainedFileCountLimit,
            RetainedFileTimeLimit: days is { } d ? TimeSpan.FromDays(d) : fallback.RetainedFileTimeLimit);
    }

    private static double? ReadPositiveDouble(IConfiguration configuration, string key)
    {
        var raw = configuration[key];
        if (string.IsNullOrWhiteSpace(raw)
            || !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || value <= 0
            || double.IsInfinity(value))
        {
            return null;
        }

        return value;
    }
}
