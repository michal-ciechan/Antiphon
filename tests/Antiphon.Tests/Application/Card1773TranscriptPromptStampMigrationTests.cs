using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1773. Startup runs this concurrent index with the default command timeout. An interrupted
/// build leaves an INVALID index and no history row; the next startup must replace that leftover.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[NotInParallel("Card1773PromptStampMigration")]
public sealed class Card1773TranscriptPromptStampMigrationTests
{
    private const string Previous = "20261107104114_HostLoadRetentionCatchup";
    private const string Current = "20261108121426_Card1773TranscriptPromptStampIndex";
    private const string IndexName = "IX_TranscriptEntries_AgentSessionId_Kind_Sequence";

    [Test]
    public void Up_renames_an_invalid_index_drops_it_and_creates_if_missing()
    {
        using var db = Metadata();
        var migration = Migration(db);
        migration.UpOperations.ShouldAllBe(op => op is SqlOperation);
        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, migration.TargetModel);
        commands.Count.ShouldBe(3);
        commands.ShouldAllBe(c => c.TransactionSuppressed);
        commands[0].CommandText.ShouldContain(
            $"ALTER INDEX \"{IndexName}\" RENAME TO \"{IndexName}_invalid\"");
        commands[0].CommandText.ShouldContain("NOT indisvalid");
        commands[1].CommandText.ShouldBe($"DROP INDEX CONCURRENTLY IF EXISTS \"{IndexName}_invalid\";");
        commands[2].CommandText.ShouldStartWith($"CREATE INDEX CONCURRENTLY IF NOT EXISTS \"{IndexName}\"");
        commands[2].CommandText.ShouldContain("(\"AgentSessionId\", \"Kind\", \"Sequence\")");
        commands[2].CommandText.ShouldNotContain("WHERE");
        var down = db.GetService<IMigrationsSqlGenerator>().Generate(migration.DownOperations, migration.TargetModel);
        down.Count.ShouldBe(2);
        down.ShouldAllBe(c => c.TransactionSuppressed);
        down[0].CommandText.ShouldBe($"DROP INDEX CONCURRENTLY IF EXISTS \"{IndexName}\";");
        down[1].CommandText.ShouldBe($"DROP INDEX CONCURRENTLY IF EXISTS \"{IndexName}_invalid\";");
    }

    [Test, Timeout(180_000)]
    public async Task Retry_replaces_an_interrupted_invalid_index()
    {
        await using var schema = await PrepareInterruptedAsync(renameLeftover: false);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var before = await ReadIndexAsync(schema.ConnectionString, IndexName);
        before.ShouldNotBeNull();
        before.Valid.ShouldBeFalse();
        (await db.Database.GetAppliedMigrationsAsync()).ShouldNotContain(Current);

        await Should.NotThrowAsync(() => db.GetService<IMigrator>().MigrateAsync(Current));

        var after = await ReadIndexAsync(schema.ConnectionString, IndexName);
        after.ShouldNotBeNull();
        after.Valid.ShouldBeTrue();
        after.Ready.ShouldBeTrue();
        after.Oid.ShouldNotBe(before.Oid);
        after.Definition.ShouldContain("AgentSessionId");
        after.Definition.ShouldContain("Kind");
        after.Definition.ShouldContain("Sequence");
        (await ReadIndexAsync(schema.ConnectionString, IndexName + "_invalid")).ShouldBeNull();
        (await db.Database.GetAppliedMigrationsAsync()).ShouldContain(Current);
    }

    [Test, Timeout(180_000)]
    public async Task Retry_finishes_cleanup_after_invalid_index_rename()
    {
        await using var schema = await PrepareInterruptedAsync(renameLeftover: true);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        (await ReadIndexAsync(schema.ConnectionString, IndexName)).ShouldBeNull();
        var leftover = await ReadIndexAsync(schema.ConnectionString, IndexName + "_invalid");
        leftover.ShouldNotBeNull();
        leftover.Valid.ShouldBeFalse();
        (await db.Database.GetAppliedMigrationsAsync()).ShouldNotContain(Current);

        await Should.NotThrowAsync(() => db.GetService<IMigrator>().MigrateAsync(Current));

        var after = await ReadIndexAsync(schema.ConnectionString, IndexName);
        after.ShouldNotBeNull();
        after.Valid.ShouldBeTrue();
        after.Ready.ShouldBeTrue();
        (await ReadIndexAsync(schema.ConnectionString, IndexName + "_invalid")).ShouldBeNull();
        (await db.Database.GetAppliedMigrationsAsync()).ShouldContain(Current);
    }

    private static async Task<IsolatedTestSchema> PrepareInterruptedAsync(bool renameLeftover)
    {
        var schema = await MigrationTestDatabase.CreateAsync(Previous);
        try
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var sessionId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                DefinitionName = "c1773-stamp",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = Path.GetTempPath(),
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            });
            await db.SaveChangesAsync();
            var sql = CreateIndexSql(db);

            await using var writer = new NpgsqlConnection(schema.ConnectionString);
            await writer.OpenAsync();
            await using var transaction = await writer.BeginTransactionAsync();
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO "TranscriptEntries" ("Id", "AgentSessionId", "Sequence", "Kind", "CreatedAt")
                VALUES (gen_random_uuid(), @id, 1, 'AssistantText', now())
                """, writer, transaction))
            {
                insert.Parameters.AddWithValue("id", sessionId);
                await insert.ExecuteNonQueryAsync();
            }

            await using var builder = new NpgsqlConnection(schema.ConnectionString);
            await builder.OpenAsync();
            await using (var budget = new NpgsqlCommand("SET statement_timeout = '1s'", builder))
                await budget.ExecuteNonQueryAsync();
            await using var command = new NpgsqlCommand(sql, builder);
            // A canceled concurrent build leaves pg_index.indisvalid=false. Do not edit the catalog.
            var error = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            error.SqlState.ShouldBe(PostgresErrorCodes.QueryCanceled);
            await transaction.RollbackAsync();

            if (renameLeftover)
            {
                await db.Database.ExecuteSqlRawAsync(
                    $"ALTER INDEX \"{IndexName}\" RENAME TO \"{IndexName}_invalid\";");
            }

            return schema;
        }
        catch
        {
            await schema.DisposeAsync();
            throw;
        }
    }

    private static string CreateIndexSql(AppDbContext db)
    {
        var migration = Migration(db);
        return db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, migration.TargetModel)
            .Single(c => c.CommandText.Contains("CREATE INDEX CONCURRENTLY", StringComparison.Ordinal)
                         && c.CommandText.Contains($"\"{IndexName}\"", StringComparison.Ordinal))
            .CommandText;
    }

    private static AppDbContext Metadata() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=localhost;Database=metadata_only").Options);

    private static Migration Migration(AppDbContext db)
    {
        var assembly = db.GetService<IMigrationsAssembly>();
        return assembly.CreateMigration(assembly.Migrations[Current], db.Database.ProviderName!);
    }

    private sealed record IndexRow(bool Valid, bool Ready, uint Oid, string Definition);

    private static async Task<IndexRow?> ReadIndexAsync(string connectionString, string name)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT i.indisvalid, i.indisready, c.oid, pg_get_indexdef(c.oid)
            FROM pg_class AS c
            JOIN pg_index AS i ON i.indexrelid = c.oid
            WHERE c.relname = @name AND i.indrelid = '"TranscriptEntries"'::regclass
            """, connection);
        command.Parameters.AddWithValue("name", name);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new IndexRow(reader.GetBoolean(0), reader.GetBoolean(1), reader.GetFieldValue<uint>(2), reader.GetString(3));
    }
}
