using System.Text;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Antiphon.Server.Infrastructure.Supervision;

/// <summary>
/// One <c>pg_stat_statements</c> sample a minute, in memory only. A failed tick records a gap and
/// does not retry. The exception text is not logged: it can carry a connection string.
/// </summary>
public sealed class DbRateSampler : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly DbRateSettings _settings;
    private readonly DbRateWindow _window;
    private readonly DbCommandMetrics _commands;
    private readonly DbCallSiteMetrics _sites;
    private readonly ILogger<DbRateSampler> _logger;
    private readonly string _countersSql;
    private readonly string _textSql;

    public DbRateSampler(
        IServiceScopeFactory scopes,
        IOptions<DbRateSettings> settings,
        DbRateWindow window,
        DbCommandMetrics commands,
        DbCallSiteMetrics sites,
        ILogger<DbRateSampler> logger)
    {
        _scopes = scopes;
        _settings = settings.Value;
        _window = window;
        _commands = commands;
        _sites = sites;
        _logger = logger;
        var (counters, text) = PgStatStatementsSql.Split(PgStatStatementsSql.Load());
        _countersSql = "-- rt.diag.pgss\n" + counters;
        _textSql = "-- rt.diag.pgss\n" + text;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled) return;
        try
        {
            await SampleAsync(stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_settings.IntervalSecondsOrDefault));
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await SampleAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task SampleAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.SetCommandTimeout(5);
            var rows = await db.Database.SqlQueryRaw<PgssCounterRow>(_countersSql).ToListAsync(ct);
            if (rows.Count == 0)
            {
                Gap(DbRateWindow.GapExtension);
                return;
            }
            var snapshot = PgStatStatementsSql.ToSnapshot(rows);
            var missing = new List<long>();
            foreach (var row in snapshot.Rows)
                if (!_window.KnowsLabel(row.QueryId)) missing.Add(row.QueryId);
            IReadOnlyDictionary<long, string>? texts = null;
            if (missing.Count > 0)
            {
                try { texts = await ReadTextsAsync(db, missing, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { _logger.LogInformation("Database rate text read skipped ({Reason})", Reason(ex)); }
            }
            _window.Observe(DateTimeOffset.UtcNow, snapshot, texts, Process());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Gap(Reason(ex)); }
    }

    private async Task<Dictionary<long, string>> ReadTextsAsync(AppDbContext db, List<long> ids, CancellationToken ct)
    {
        var sql = PgStatStatementsSql.WithIds(_textSql, ids);
        var rows = await db.Database.SqlQueryRaw<PgssTextRow>(sql).ToListAsync(ct);
        var texts = new Dictionary<long, string>(rows.Count);
        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row.QueryId) || string.IsNullOrEmpty(row.QueryB64)) continue;
            if (!long.TryParse(row.QueryId, out var id)) continue;
            try
            {
                texts[id] = Encoding.UTF8.GetString(Convert.FromBase64String(row.QueryB64));
            }
            catch (FormatException) { }
        }
        return texts;
    }

    private ProcessSample Process()
    {
        var commands = _commands.Read();
        var sites = _sites.Read();
        return new ProcessSample(commands.Total, commands.Untagged,
            sites.Begin, sites.Commit, sites.Rollback, sites.Savepoint, sites.Release,
            sites.Unknown, sites.Overflow, sites.Sites);
    }

    private void Gap(string reason)
    {
        _window.RecordGap(DateTimeOffset.UtcNow, reason);
        _logger.LogInformation("Database rate sample gap ({Reason})", reason);
    }

    private static string Reason(Exception ex)
    {
        for (var cursor = ex; cursor is not null; cursor = cursor.InnerException)
            if (cursor is PostgresException pg && pg.SqlState is "42883" or "42P01")
                return DbRateWindow.GapExtension;
        return DbRateWindow.GapUnavailable;
    }
}
