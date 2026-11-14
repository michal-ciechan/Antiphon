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

[Category("Integration")]
[Category("Slow")]
[NotInParallel]
public sealed class SessionTranscriptSummaryMigrationTests
{
    [Test]
    public void Summary_model_is_metadata_only_and_invalid_by_default()
    {
        using var db = Metadata();
        var entity = db.Model.FindEntityType("Antiphon.Server.Domain.Entities.SessionTranscriptSummary");
        entity.ShouldNotBeNull("S2 adds durable metadata without transcript payloads");
        entity.GetProperties().Select(p => p.Name).Order().ShouldBe(new[] {
            "AgentSessionId", "SchemaVersion", "Valid", "NextBackfillAttemptAt", "Count", "NewestEffectiveTimestamp",
            "LastSequence", "LastEntryId", "LastKind", "LastTimestamp", "LastCreatedAt", "EndSequence", "EndTimestamp",
            "HasActivity", "HasNullTimestampActivity", "ActivityTimestamp", "LastTurnEndSequence", "LastTurnTitleSequence", "LastUserPromptSequence"
        }.Order());
        entity.FindPrimaryKey()!.Properties.Select(p => p.Name).ShouldBe(["AgentSessionId"]);
        entity.GetForeignKeys().Single().DeleteBehavior.ShouldBe(DeleteBehavior.Cascade);
        entity.FindProperty("Valid")!.GetDefaultValue().ShouldBe(false);
        entity.GetIndexes().ShouldContain(i => i.GetFilter() == "NOT \"Valid\" OR \"SchemaVersion\" <> 1");
    }

    [Test]
    public void Up_is_additive_bounded_and_Down_refuses_data_loss()
    {
        using var db = Metadata();
        var current = Current(db);
        string.CompareOrdinal(current, "20261110213738_Card1294ShadowCohort").ShouldBeGreaterThan(0);
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations[current], db.Database.ProviderName!);
        migration.UpOperations.ShouldAllBe(op => op.GetType() == typeof(CreateTableOperation) || op.GetType() == typeof(CreateIndexOperation) || op.GetType() == typeof(SqlOperation));
        migration.UpOperations.OfType<CreateTableOperation>().Select(op => op.Name).ShouldBe(["SessionTranscriptSummaries"]);
        migration.UpOperations.OfType<CreateIndexOperation>().ShouldAllBe(op => op.Table == "SessionTranscriptSummaries");
        var sql = string.Join("\n", migration.UpOperations.OfType<SqlOperation>().Select(op => op.Sql));
        sql.ShouldContain("SET LOCAL lock_timeout = '5s'");
        sql.ShouldNotContain("UPDATE \"TranscriptEntries\"");
        sql.ShouldNotContain("DELETE FROM \"TranscriptEntries\"");
        sql.ShouldNotContain("INSERT INTO \"TranscriptEntries\"");
        sql.ShouldNotContain("\r");
        Should.Throw<NotSupportedException>(() => _ = migration.DownOperations);
    }

    [Test]
    public async Task Validity_requires_every_invalidation_trigger()
    {
        using var metadata = Metadata();
        var current = Current(metadata);
        var names = metadata.Database.GetMigrations().ToArray();
        var predecessor = names[Array.IndexOf(names, current) - 1];
        // A fresh predecessor is the subject: never rewind a durable tip clone.
        await using var schema = await MigrationTestDatabase.CreateAsync(predecessor, default);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await using (var blocker = new NpgsqlConnection(schema.ConnectionString))
        {
            await blocker.OpenAsync();
            await using var transaction = await blocker.BeginTransactionAsync();
            await using var command = new NpgsqlCommand("LOCK TABLE \"TranscriptEntries\" IN ROW EXCLUSIVE MODE", blocker, transaction);
            await command.ExecuteNonQueryAsync();
            var error = await Should.ThrowAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(current));
            error.SqlState.ShouldBe(PostgresErrorCodes.LockNotAvailable);
            (await db.Database.GetAppliedMigrationsAsync()).ShouldNotContain(current);
            (await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('\"SessionTranscriptSummaries\"') IS NULL AS \"Value\"").SingleAsync()).ShouldBeTrue();
            await transaction.RollbackAsync();
        }
        await db.GetService<IMigrator>().MigrateAsync(current);
        await db.GetService<IMigrator>().MigrateAsync(current); // complete install is idempotent
        (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_trigger WHERE tgrelid='\"TranscriptEntries\"'::regclass AND tgname LIKE 'c1915_%' AND NOT tgisinternal").SingleAsync()).ShouldBe(4);
        var id = Guid.NewGuid();
        db.AgentSessions.Add(new AgentSession { Id = id, DefinitionName = "c1915-migration", Cwd = "/synthetic", Status = SessionStatus.Stopped, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"TranscriptEntries\" (\"Id\",\"AgentSessionId\",\"Sequence\",\"Kind\",\"CreatedAt\") VALUES ({Guid.NewGuid()},{id},1,'AssistantText',now())");
        (await ValidAsync(db, id)).ShouldBeFalse("missing summaries are invalid placeholders, never invented exact counts");
        foreach (var mutation in new[] {
            "UPDATE \"TranscriptEntries\" SET \"Text\"='changed without count/sequence change' WHERE \"AgentSessionId\"=@id",
            "DELETE FROM \"TranscriptEntries\" WHERE \"AgentSessionId\"=@id",
            "TRUNCATE TABLE \"TranscriptEntries\" CASCADE"
        })
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"SessionTranscriptSummaries\" SET \"Valid\"=true WHERE \"AgentSessionId\"={id}");
            await db.Database.ExecuteSqlRawAsync(mutation, new NpgsqlParameter("id", id));
            (await ValidAsync(db, id)).ShouldBeFalse(mutation);
        }
        await Should.ThrowAsync<NotSupportedException>(() => db.GetService<IMigrator>().MigrateAsync(predecessor));
        (await db.Database.GetAppliedMigrationsAsync()).ShouldContain(current);
        await db.AgentSessions.Where(s => s.Id == id).ExecuteDeleteAsync();
        (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM \"SessionTranscriptSummaries\"").SingleAsync()).ShouldBe(0, "session cascade must not recreate a summary");
    }

    private static Task<bool> ValidAsync(AppDbContext db, Guid id) => db.Database.SqlQueryRaw<bool>(
        "SELECT \"Valid\" AS \"Value\" FROM \"SessionTranscriptSummaries\" WHERE \"AgentSessionId\"=@id", new NpgsqlParameter("id", id)).SingleAsync();
    private static AppDbContext Metadata() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=metadata_only").Options);
    private static string Current(AppDbContext db)
    {
        var current = db.Database.GetMigrations().SingleOrDefault(n => n.EndsWith("_AddSessionTranscriptSummaries", StringComparison.Ordinal));
        current.ShouldNotBeNull("the additive summary migration must be discoverable");
        return current;
    }
}
