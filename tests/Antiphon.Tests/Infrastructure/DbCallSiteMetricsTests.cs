using Antiphon.Server.Infrastructure.Data;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>CARD-1987 W-9. The interceptor is driven directly; no database.</summary>
[Category("Unit")]
public class DbCallSiteMetricsTests
{
    [Test]
    public async Task C1987_sampled_command_names_its_method()
    {
        var metrics = new DbCallSiteMetrics(commandInterval: 1, transactionInterval: 1);
        using var command = new NpgsqlCommand("SELECT 1");
        for (var i = 0; i < 128; i++)
            metrics.ReaderExecuting(command, null!, default);
        var reading = metrics.Read();
        reading.Unknown.ShouldBe(0);
        reading.Sites.Count.ShouldBe(1);
        var site = reading.Sites.Single();
        site.Key.ShouldBe("DbCallSiteMetricsTests.C1987_sampled_command_names_its_method");
        site.Value.ShouldBe(128);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C1987_transaction_completions_are_counted()
    {
        var metrics = new DbCallSiteMetrics(commandInterval: 1, transactionInterval: 1);
        var tx = metrics.Transactions;
        tx.TransactionStarted(null!, null!, null!);
        tx.TransactionCommitted(null!, null!);
        tx.TransactionRolledBack(null!, null!);
        tx.CreatedSavepoint(null!, null!);
        tx.ReleasedSavepoint(null!, null!);
        tx.RolledBackToSavepoint(null!, null!);
        var reading = metrics.Read();
        reading.Begin.ShouldBe(1);
        reading.Commit.ShouldBe(1);
        reading.Rollback.ShouldBe(2);
        reading.Savepoint.ShouldBe(1);
        reading.Release.ShouldBe(1);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C1987_async_demangles_issue_async()
    {
        var metrics = new DbCallSiteMetrics(commandInterval: 1, transactionInterval: 1);
        await IssueAsync(metrics);
        var reading = metrics.Read();
        reading.Unknown.ShouldBe(0);
        reading.Sites.ContainsKey("DbCallSiteMetricsTests.IssueAsync").ShouldBeTrue(string.Join(",", reading.Sites.Keys));
        reading.Sites["DbCallSiteMetricsTests.IssueAsync"].ShouldBe(1);
    }

    [Test]
    public async Task C1987_site_cap_overflows_after_256()
    {
        var metrics = new DbCallSiteMetrics(commandInterval: 1, transactionInterval: 1);
        for (var i = 0; i < 300; i++)
            metrics.ObserveSite("Site" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var reading = metrics.Read();
        reading.Sites.Count.ShouldBe(256);
        reading.Overflow.ShouldBe(44);
        reading.Unknown.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C1987_missing_frame_counts_unknown()
    {
        var metrics = new DbCallSiteMetrics(commandInterval: 1, transactionInterval: 1);
        metrics.ObserveSite(null);
        var reading = metrics.Read();
        reading.Unknown.ShouldBe(1);
        reading.Sites.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    private static async Task IssueAsync(DbCallSiteMetrics metrics)
    {
        await Task.Yield();
        using var command = new NpgsqlCommand("SELECT 1");
        await metrics.ReaderExecutingAsync(command, null!, default);
    }
}
