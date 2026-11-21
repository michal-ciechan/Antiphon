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
