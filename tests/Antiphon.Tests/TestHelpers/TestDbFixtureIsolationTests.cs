using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
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
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(isolated.ConnectionString));
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();

        var started = DateTime.UtcNow;
        await db.Database.MigrateAsync();
        (DateTime.UtcNow - started).ShouldBeLessThan(
            TimeSpan.FromSeconds(2),
            "a cloned database is already current; MigrateAsync must not replay migrations");
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
