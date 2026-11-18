namespace Antiphon.Server.Infrastructure.Data;

/// <summary>CARD-1987 red witness. The window records nothing, so every rate stays zero.</summary>
public sealed class DbRateWindow
{
    public const int RingMinutes = 1440;
    public const int MaxLabels = 2000;
    public const int MaxStatementsPerMinute = 15;
    public const int MaxOperationsPerMinute = 32;
    public const int MaxSitesPerMinute = 16;
    public const string GapUnavailable = "unavailable";
    public const string GapExtension = "extension-missing";
    public const string GapReset = "stats-reset";
    public const string GapDealloc = "dealloc";

    public DbRateWindow(int retentionMinutes = RingMinutes)
    {
        _ = retentionMinutes;
    }

    public int LabelCount => 0;

    public int MinuteCount => 0;

    internal bool KnowsLabel(long id)
    {
        _ = id;
        return false;
    }

    internal string? CachedLabel(long id)
    {
        _ = id;
        return null;
    }

    public void RecordGap(DateTimeOffset at, string reason)
    {
        _ = at;
        _ = reason;
    }

    public void Observe(
        DateTimeOffset at,
        PgssSnapshot snapshot,
        IReadOnlyDictionary<long, string>? texts = null,
        ProcessSample? process = null)
    {
        _ = at;
        _ = snapshot;
        _ = texts;
        _ = process;
    }

    public DbRateReport Report(int minutes, DateTimeOffset now)
    {
        _ = minutes;
        _ = now;
        return new DbRateReport(
            [
                new Rollup(1, 0, 0, 0),
                new Rollup(5, 0, 0, 0),
                new Rollup(15, 0, 0, 0),
                new Rollup(60, 0, 0, 0),
            ],
            [],
            new TopStatements([], []),
            [],
            [],
            new CommandRate(0, 0),
            new CallSiteReport(0, []),
            new TransactionRate(0, 0, 0, 0, 0));
    }
}
