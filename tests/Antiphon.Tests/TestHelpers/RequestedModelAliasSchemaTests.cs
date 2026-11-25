using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// <see cref="RequestedModelAliasSchema"/> adds the nullable alias column for historical
/// databases. Once the column is committed, a later command inside an open transaction must
/// not take <c>ACCESS EXCLUSIVE</c> on <c>AgentTasks</c> for the rest of that transaction.
/// Dispatch claims pause inside such a transaction; a second session's read then waits out
/// the 30s command timeout.
/// </summary>
[Category("Integration")]
public sealed class RequestedModelAliasSchemaTests
{
    [Test]
    public async Task Historical_seed_can_write_an_alias_before_the_migration(CancellationToken ct)
    {
        const string predecessor = "20261119045705_AddLandVerificationFailure";
        await using var historical = await MigrationTestDatabase.CreateAsync(predecessor, ct);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(historical.ConnectionString).Options;
        await using var unmodified = new AppDbContext(options);
        var before = (await unmodified.Database.GetAppliedMigrationsAsync(ct)).ToArray();
        before.Last().ShouldBe(predecessor);
        (await unmodified.Database.SqlQueryRaw<bool>("""
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = current_schema() AND table_name = 'AgentTasks'
                  AND column_name = 'RequestedModelAlias') AS "Value"
            """).SingleAsync(ct)).ShouldBeFalse();

        var fixtureOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(historical.ConnectionString)
            .AddInterceptors(RequestedModelAliasSchema.Interceptor)
            .Options;
        // Seed the predecessor schema with its own columns plus the alias. The current entity also
        // maps later additive columns (the caller report receipt), which this schema does not have.
        var id = Guid.NewGuid();
        await using (var fixture = new AppDbContext(fixtureOptions))
        {
            await fixture.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "AgentTasks" ("Id", "RootTaskId", "Depth", "Title", "Goal", "Kind", "Role", "ModelLevel",
                    "Attempt", "MaxAttempts", "Workspace", "WorkingDirectory", "Ephemeral", "Status", "ReplyTo",
                    "ConcurrencyToken", "CreatedAt", "TokensIn", "TokensOut", "CostUsd", "RequestedModelAlias")
                VALUES ({id}, {id}, {0}, {"alias"}, {"preserve"}, {0}, {0}, {0}, {1}, {2}, {1},
                    {"/tmp/requested-model-alias"}, {false}, {0}, {0}, {Guid.NewGuid()}, {DateTime.UtcNow}, {0L}, {0L}, {0m},
                    {"gpt-6.2-astra"})
                """, ct);
        }

        (await unmodified.AgentTasks.Where(task => task.Id == id).Select(task => task.RequestedModelAlias).SingleAsync(ct))
            .ShouldBe("gpt-6.2-astra");
        (await unmodified.Database.GetAppliedMigrationsAsync(ct)).ShouldBe(before);
    }

    [Test]
    [Timeout(120_000)]
    public async Task Open_transaction_does_not_keep_access_exclusive_on_agent_tasks()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(isolated.ConnectionString);
        await using (var warm = new AppDbContext(options))
        {
            _ = await warm.Database.SqlQueryRaw<int>("""
                SELECT count(*)::int AS "Value" FROM "AgentTasks" WHERE "RequestedModelAlias" IS NULL
                """).ToListAsync();
        }

        await using var held = new AppDbContext(options);
        await using var transaction = await held.Database.BeginTransactionAsync();
        _ = await held.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM "AgentTasks" WHERE "RequestedModelAlias" IS NULL
            """).ToListAsync();

        await using var other = new NpgsqlConnection(isolated.ConnectionString);
        await other.OpenAsync();
        await using (var timeout = new NpgsqlCommand("SET statement_timeout = '3s'", other))
            await timeout.ExecuteNonQueryAsync();
        await using var probe = new NpgsqlCommand("SELECT count(*) FROM \"AgentTasks\"", other);
        var count = await probe.ExecuteScalarAsync();
        count.ShouldBe(0L);
        await transaction.RollbackAsync();
    }
}
