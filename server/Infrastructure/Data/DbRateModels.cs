namespace Antiphon.Server.Infrastructure.Data;

/// <summary>One <c>pg_stat_statements</c> counter row. Text never rides along.</summary>
public sealed record PgssRow(long QueryId, long Calls, double TotalExecMs, long Rows);

/// <summary>One counters-only snapshot. <paramref name="StatsReset"/> is the raw timestamp text, empty when null.</summary>
public sealed record PgssSnapshot(long Dealloc, string StatsReset, IReadOnlyList<PgssRow> Rows);

/// <summary>Cumulative in-process counters taken at the same instant as a snapshot.</summary>
public readonly record struct ProcessSample(
    long CommandTotal,
    long CommandUntagged,
    long Begin,
    long Commit,
    long Rollback,
    long Savepoint,
    long Release,
    long Unknown,
    long Overflow,
    IReadOnlyDictionary<string, long> Sites);

public sealed record Rollup(int Minutes, double Qps, double DbMsPerSecond, double RowsPerSecond);

public sealed record MinuteRate(DateTimeOffset At, double Qps, double DbMsPerSecond, double RowsPerSecond);

public sealed record StatementRate(
    string Label, long Calls, double Qps, double DbMsPerSecond, double RowsPerSecond, double MeanMs);

public sealed record TopStatements(IReadOnlyList<StatementRate> ByCalls, IReadOnlyList<StatementRate> ByTime);

public sealed record OperationRate(
    string Operation, double Qps, double DbMsPerSecond, double RowsPerSecond, long Calls, double MeanMs);

public sealed record RateGap(DateTimeOffset At, string Reason);

public sealed record CommandRate(double TotalPerSecond, double UntaggedPerSecond);

public sealed record CallSiteShare(string Site, long Samples, double Share);

public sealed record CallSiteReport(double UnknownShare, IReadOnlyList<CallSiteShare> Top);

public sealed record TransactionRate(
    double BeginPerSecond,
    double CommitPerSecond,
    double RollbackPerSecond,
    double SavepointPerSecond,
    double ReleasePerSecond);

public sealed record DbRateReport(
    IReadOnlyList<Rollup> Rollups,
    IReadOnlyList<MinuteRate> Series,
    TopStatements Top,
    IReadOnlyList<OperationRate> ByTag,
    IReadOnlyList<RateGap> Gaps,
    CommandRate DbCommands,
    CallSiteReport CallSites,
    TransactionRate Transactions);
