using System.Globalization;
using System.Text.Json;
using Antiphon.Server.Infrastructure.Data;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>CARD-1987 W-6..W-8. Synthetic snapshots only: no database.</summary>
[Category("Unit")]
public class DbRateSamplerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task C1987_window_rollups_are_exact()
    {
        var window = new DbRateWindow();
        var texts = new Dictionary<long, string>
        {
            [1] = "-- rt.witness.a\nSELECT 1",
            [2] = "-- rt.witness.b\nSELECT 2",
        };
        long c1 = 0, r1 = 0, c2 = 0, r2 = 0;
        double m1 = 0, m2 = 0;
        window.Observe(Start, Snap(0, "", Row(1, c1, m1, r1), Row(2, c2, m2, r2)), texts);
        for (var k = 1; k <= 60; k++)
        {
            c1 += 120; m1 += 600; r1 += 60;
            c2 += 60; m2 += 6000; r2 += 120;
            window.Observe(Start.AddMinutes(k), Snap(0, "", Row(1, c1, m1, r1), Row(2, c2, m2, r2)));
        }

        var now = Start.AddMinutes(60);
        var report = window.Report(60, now);
        report.Series.Count.ShouldBe(60);
        report.Series[0].Qps.ShouldBe(3);
        report.Series[0].DbMsPerSecond.ShouldBe(110);
        report.Series[0].RowsPerSecond.ShouldBe(3);
        foreach (var roll in report.Rollups)
        {
            roll.Qps.ShouldBe(3);
            roll.DbMsPerSecond.ShouldBe(110);
            roll.RowsPerSecond.ShouldBe(3);
        }
        report.Rollups.Select(r => r.Minutes).ShouldBe(new[] { 1, 5, 15, 60 });
        var byCalls = report.Top.ByCalls[0];
        byCalls.Label.ShouldBe("rt.witness.a");
        byCalls.Qps.ShouldBe(2);
        byCalls.DbMsPerSecond.ShouldBe(10);
        byCalls.RowsPerSecond.ShouldBe(1);
        var byTime = report.Top.ByTime[0];
        byTime.Label.ShouldBe("rt.witness.b");
        byTime.DbMsPerSecond.ShouldBe(100);
        byTime.Qps.ShouldBe(1);
        report.ByTag.Single(o => o.Operation == "rt.witness.a").Calls.ShouldBe(7200);
        report.DbCommands.TotalPerSecond.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C1987_reset_gaps_do_not_go_negative()
    {
        var reset = new DbRateWindow();
        reset.Observe(Start, Snap(3, "", Row(1, 100, 0, 0)));
        reset.Observe(Start.AddMinutes(1), Snap(3, "2026-11-18 00:01:00+00", Row(1, 40, 0, 0)));
        var resetReport = reset.Report(60, Start.AddMinutes(1));
        resetReport.Gaps.Count.ShouldBe(1);
        resetReport.Gaps[0].Reason.ShouldBe(DbRateWindow.GapReset);
        resetReport.Series.ShouldBeEmpty();
        resetReport.Top.ByCalls.ShouldBeEmpty();
        resetReport.Rollups.ShouldAllBe(r => r.Qps == 0 && r.DbMsPerSecond >= 0);

        var dealloc = new DbRateWindow();
        dealloc.Observe(Start, Snap(3, "", Row(1, 10, 0, 0)));
        dealloc.Observe(Start.AddMinutes(1), Snap(4, "", Row(1, 80, 0, 0)));
        var deallocReport = dealloc.Report(60, Start.AddMinutes(1));
        deallocReport.Gaps.Single().Reason.ShouldBe(DbRateWindow.GapDealloc);
        deallocReport.Series.ShouldBeEmpty();
        deallocReport.Rollups.ShouldAllBe(r => r.Qps == 0);

        var missing = new DbRateWindow();
        missing.Observe(Start, Snap(3, "", Row(1, 10, 0, 0), Row(2, 10, 0, 0)));
        missing.Observe(Start.AddMinutes(1), Snap(3, "", Row(1, 20, 0, 0)));
        var missingReport = missing.Report(60, Start.AddMinutes(1));
        missingReport.Gaps.Single().Reason.ShouldBe(DbRateWindow.GapReset);
        missingReport.Series.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C1987_bounds_ring_drops_the_oldest_minute()
    {
        var window = new DbRateWindow();
        long calls = 0;
        window.Observe(Start, Snap(0, "", Row(1, calls, 0, 0)));
        for (var k = 1; k <= 1441; k++)
        {
            calls += k * 60L;
            window.Observe(Start.AddMinutes(k), Snap(0, "", Row(1, calls, 0, 0)));
        }
        window.MinuteCount.ShouldBe(1440);
        var report = window.Report(1440, Start.AddMinutes(1441));
        report.Series.Count.ShouldBe(1440);
        report.Series[0].Qps.ShouldBe(2);
        report.Series[^1].Qps.ShouldBe(1441);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C1987_bounds_labels_redact_and_cap()
    {
        const string secret = "c1987secretvalue";
        var texts = new Dictionary<long, string>();
        texts[1] = "ALTER ROLE bob PASSWORD '" + secret + "'";
        for (long id = 2; id <= 2000; id++)
            texts[id] = "-- rt.n" + id.ToString(CultureInfo.InvariantCulture) + "\nSELECT " + id.ToString(CultureInfo.InvariantCulture);
        texts[2001] = "-- rt.leak\nSELECT token FROM accounts";
        var rows = new PgssRow[texts.Count];
        var index = 0;
        foreach (var id in texts.Keys.OrderBy(id => id))
            rows[index++] = Row(id, 0, 0, 0);
        var window = new DbRateWindow();
        window.Observe(Start, new PgssSnapshot(0, "", rows), texts);
        var after = new PgssRow[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            var calls = rows[i].QueryId == 2001 ? 100000 : 1;
            after[i] = Row(rows[i].QueryId, calls, calls, calls);
        }
        window.Observe(Start.AddMinutes(1), new PgssSnapshot(0, "", after), texts);
        window.LabelCount.ShouldBe(2000);
        window.CachedLabel(1).ShouldBe(PgStatLabel.Redacted);
        window.CachedLabel(2001).ShouldBeNull();
        var report = window.Report(60, Start.AddMinutes(1));
        report.Top.ByCalls[0].Label.ShouldBe(PgStatLabel.Redacted);
        var json = JsonSerializer.Serialize(report);
        json.Contains(secret, StringComparison.Ordinal).ShouldBeFalse();
        json.Contains("rt.leak", StringComparison.Ordinal).ShouldBeFalse();
        json.Contains("token", StringComparison.Ordinal).ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C1987_window_process_rates_use_the_same_minutes()
    {
        var window = new DbRateWindow();
        var empty = new Dictionary<string, long>();
        var sites = new Dictionary<string, long> { ["AgentSessionRuntime.RecordActivityAsync"] = 30 };
        window.Observe(Start, Snap(0, "", Row(1, 0, 0, 0)), null,
            new ProcessSample(0, 0, 0, 0, 0, 0, 0, 0, 0, empty));
        window.Observe(Start.AddMinutes(1), Snap(0, "", Row(1, 0, 0, 0)), null,
            new ProcessSample(120, 60, 60, 0, 0, 0, 0, 10, 0, sites));
        var report = window.Report(60, Start.AddMinutes(1));
        report.DbCommands.TotalPerSecond.ShouldBe(2);
        report.DbCommands.UntaggedPerSecond.ShouldBe(1);
        report.Transactions.BeginPerSecond.ShouldBe(1);
        report.CallSites.UnknownShare.ShouldBe(0.25);
        var site = report.CallSites.Top.Single();
        site.Site.ShouldBe("AgentSessionRuntime.RecordActivityAsync");
        site.Samples.ShouldBe(30);
        site.Share.ShouldBe(0.75);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C1987_embedded_sql_matches_the_checkout_file()
    {
        var embedded = PgStatStatementsSql.Load().Replace("\r\n", "\n", StringComparison.Ordinal);
        var onDisk = File.ReadAllText(Path.Combine(RepoRoot(), "server", "Infrastructure", "Data", "PgStatStatementsSnapshot.sql"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        onDisk.ShouldBe(embedded);
        embedded.Contains("pg_stat_statements(false)", StringComparison.Ordinal).ShouldBeTrue();
        embedded.Contains("/*IDS*/", StringComparison.Ordinal).ShouldBeTrue();
        embedded.Contains(PgStatStatementsSql.SplitMarker, StringComparison.Ordinal).ShouldBeTrue();
        var (counters, text) = PgStatStatementsSql.Split(embedded);
        counters.Contains("pg_stat_statements(false)", StringComparison.Ordinal).ShouldBeTrue();
        PgStatStatementsSql.WithIds(text, [-5, 9]).Contains("-5,9", StringComparison.Ordinal).ShouldBeTrue();
        await Task.CompletedTask;
    }

    private static PgssSnapshot Snap(long dealloc, string reset, params PgssRow[] rows) => new(dealloc, reset, rows);
    private static PgssRow Row(long id, long calls, double ms, long rows) => new(id, calls, ms, rows);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
