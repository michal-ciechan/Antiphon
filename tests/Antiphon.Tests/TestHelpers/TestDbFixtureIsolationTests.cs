using System.Collections.Concurrent;
using System.Data.Common;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0110 S2: isolated stores are cloned from a migrate-once template, not a per-call
/// migration replay against a SearchPath schema.
/// </summary>
[Category("Integration")]
public sealed class TestDbFixtureIsolationTests
{
    [Test]
    public async Task Clone_is_a_separate_database_not_a_search_path_schema()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var builder = new NpgsqlConnectionStringBuilder(isolated.ConnectionString);

        (builder.Database?.StartsWith("test_", StringComparison.Ordinal) == true).ShouldBeTrue();
        builder.Database.ShouldNotBe(TestDbFixture.SharedDatabaseName);
        string.IsNullOrWhiteSpace(builder.SearchPath).ShouldBeTrue(
            "CARD-0110 S2 isolation is Database=, not SearchPath=");
    }

    [Test]
    public async Task Clone_is_fully_migrated_and_empty()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(isolated.ConnectionString));

        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        (await db.Database.GetAppliedMigrationsAsync()).ShouldNotBeEmpty();
        (await db.Agents.AnyAsync()).ShouldBeFalse();
        (await db.AgentTuiProfiles.AnyAsync()).ShouldBeFalse();
        (await db.Boards.AnyAsync()).ShouldBeFalse();
    }

    [Test]
    public async Task Two_clones_do_not_share_rows()
    {
        await using var first = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var second = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var firstDb = new AppDbContext(TestDbFixture.CreateDbContextOptions(first.ConnectionString));
        await using var secondDb = new AppDbContext(TestDbFixture.CreateDbContextOptions(second.ConnectionString));

        firstDb.Agents.Add(NewAgent("clone-first"));
        await firstDb.SaveChangesAsync();

        (await firstDb.Agents.CountAsync()).ShouldBe(1);
        (await secondDb.Agents.AnyAsync()).ShouldBeFalse();
    }

    [Test]
    public async Task Dispose_drops_the_cloned_database()
    {
        string databaseName;
        await using (var isolated = await TestDbFixture.CreateIsolatedSchemaAsync())
        {
            databaseName = new NpgsqlConnectionStringBuilder(isolated.ConnectionString).Database!;
            await using var live = new NpgsqlConnection(isolated.ConnectionString);
            await live.OpenAsync();
        }

        await using var maintenance = new NpgsqlConnection(TestDbFixture.MaintenanceConnectionString);
        await maintenance.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM pg_database WHERE datname = @n)",
            maintenance);
        command.Parameters.AddWithValue("n", databaseName);
        var exists = (bool)(await command.ExecuteScalarAsync())!;
        exists.ShouldBeFalse($"cloned database '{databaseName}' must be dropped on dispose");
    }

    [Test]
    public async Task Concurrent_clones_each_get_an_empty_migrated_database()
    {
        var clones = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => TestDbFixture.CreateIsolatedSchemaAsync()));
        try
        {
            var names = clones
                .Select(clone => new NpgsqlConnectionStringBuilder(clone.ConnectionString).Database)
                .ToList();
            names.Distinct().Count().ShouldBe(4);

            foreach (var clone in clones)
            {
                await using var db = new AppDbContext(
                    TestDbFixture.CreateDbContextOptions(clone.ConnectionString));
                (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
                (await db.Agents.AnyAsync()).ShouldBeFalse();
            }
        }
        finally
        {
            foreach (var clone in clones)
                await clone.DisposeAsync();
        }
    }

    [Test]
    public async Task MigrateAsync_on_a_clone_is_a_version_check()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var recorder = new RecordingCommandInterceptor();
        await using var db = new AppDbContext(
            TestDbFixture.CreateDbContextOptions(isolated.ConnectionString, recorder));
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        var ops = new TestDbOperations();
        var before = await ops.ReadAppliedMigrationsAsync(isolated.ConnectionString);
        recorder.Clear();

        // CARD-1420: the property is "applies no migration", asserted below. The timeout is only a
        // hang guard, not a speed bound: on a schema-cache hit no bootstrap migrate ran, so this
        // process's first MigrateAsync pays a cold EF model differ (1-4 s of CPU on server2), which
        // is not a migration.
        await db.Database.MigrateAsync().WaitAsync(TimeSpan.FromSeconds(10));

        var commands = recorder.Commands.ToArray();
        commands.ShouldNotBeEmpty("the interceptor observed the version check");
        (await ops.ReadAppliedMigrationsAsync(isolated.ConnectionString)).ShouldBe(
            before,
            "a cloned database is already current; MigrateAsync must not record a migration");
        commands.SelectMany(Statements).Where(IsSchemaOrDataChange).ShouldBeEmpty(
            "MigrateAsync on a current clone may only read and lock the history: "
            + string.Join(" || ", commands));
    }

    private static IEnumerable<string> Statements(string commandText) =>
        commandText.Split(';').Select(statement => statement.Trim()).Where(statement => statement.Length > 0);

    /// <summary>
    /// DDL or DML in a version check means a migration ran. EF's idempotent history-table ensure is
    /// the one exception: on a current store it changes nothing.
    /// </summary>
    private static bool IsSchemaOrDataChange(string statement)
    {
        if (statement.StartsWith("CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\"", StringComparison.OrdinalIgnoreCase))
            return false;
        string[] changes = ["CREATE", "ALTER", "DROP", "INSERT", "UPDATE", "DELETE", "TRUNCATE", "COMMENT", "GRANT", "REVOKE", "DO"];
        var head = statement.Split(new[] { ' ', '\n', '\r', '\t' }, 2)[0];
        return changes.Contains(head, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class RecordingCommandInterceptor : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Commands { get; private set; } = new();

        public void Clear() => Commands = new();

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Commands.Enqueue(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Commands.Enqueue(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Commands.Enqueue(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private static Agent NewAgent(string name)
    {
        var now = DateTime.UtcNow;
        return new Agent
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = $"clone-{Guid.NewGuid():N}",
            Kind = AgentKind.Raw,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
