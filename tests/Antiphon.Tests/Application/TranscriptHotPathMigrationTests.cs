using System.Diagnostics;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[NotInParallel("TranscriptHotPathMigrations")]
public class TranscriptHotPathMigrationTests
{
    [Test]
    public async Task Generated_sql_and_model_preserve_unique_sequence_and_suppress_index_transactions()
    {
        await using var f = await TranscriptHotPathFixture.CreateAsync();
        await using var db = f.CreateDb();
        var (previous, current, migration) = MigrationInfo(db);
        var indexes = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(TranscriptEntry))!.GetIndexes().ToArray();
        indexes.Single(i => i.GetDatabaseName() == "IX_TranscriptEntries_AgentSessionId_Sequence").IsUnique.ShouldBeTrue();
        indexes.Single(i => i.GetDatabaseName() == "IX_TranscriptEntries_IsApiError").ShouldNotBeNull();
        var names = NewIndexNames();
        foreach (var name in names)
        {
            var index = indexes.Single(i => i.GetDatabaseName() == name);
            index.IsUnique.ShouldBeFalse();
            index.IsCreatedConcurrently().ShouldBe(true);
        }
        var uuid = indexes.Single(i => i.GetDatabaseName() == names[0]);
        uuid.GetIncludeProperties().ShouldBe(["Kind"]);
        uuid.GetFilter().ShouldBe("\"Uuid\" IS NOT NULL");
        var operations = migration.UpOperations.OfType<CreateIndexOperation>().ToArray();
        migration.UpOperations.Count.ShouldBe(3);
        operations.Select(i => i.Name).ShouldBe(names);
        operations.ShouldAllBe(i => !i.IsUnique);
        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, migration.TargetModel);
        commands.Count.ShouldBe(3);
        commands.ShouldAllBe(c => c.TransactionSuppressed && c.CommandText.StartsWith("CREATE INDEX CONCURRENTLY"));
        var script = db.GetService<IMigrator>().GenerateScript(previous, current);
        var inTransaction = false;
        foreach (var line in script.Split('\n').Select(l => l.Trim()))
        {
            if (line is "START TRANSACTION;" or "BEGIN;") inTransaction = true;
            if (line.StartsWith("CREATE INDEX")) inTransaction.ShouldBeFalse();
            if (line == "COMMIT;") inTransaction = false;
        }
        f.Record("migration-sql", new { up = script,
            down = db.GetService<IMigrator>().GenerateScript(current, previous),
            commands = commands.Select(c => new { c.CommandText, c.TransactionSuppressed }) });
    }

    [Test]
    public async Task Populated_prechange_upgrade_preserves_all_rows_and_uuid_kinds()
    {
        await using var f = await TranscriptHotPathFixture.CreateAsync();
        await using var db = f.CreateDb();
        var (previous, current, _) = MigrationInfo(db);
        db.Database.SetCommandTimeout(300);
        await db.GetService<IMigrator>().MigrateAsync(previous);
        await f.SeedLargeAsync();
        await AddIdentityRowsAsync(db, f.SessionIds[0]);
        var before = await FingerprintAsync(f.ConnectionString);
        var elapsed = Stopwatch.StartNew();
        await db.GetService<IMigrator>().MigrateAsync(current);
        elapsed.Stop();
        (await FingerprintAsync(f.ConnectionString)).ShouldBe(before);
        await AssertIdentityRowsAsync(db, f.SessionIds[0]);
        (await db.Database.GetAppliedMigrationsAsync()).ShouldContain(current);
        var catalog = await ReadCatalogAsync(f.ConnectionString);
        catalog.Count.ShouldBe(3);
        catalog.ShouldAllBe(i => i.Valid && i.Ready && !i.Unique && i.Bytes > 0);
        f.Record("migration-rehearsal", new { rows = 377003, elapsedMs = elapsed.ElapsedMilliseconds, catalog });
    }

    [Test]
    public async Task Down_up_roundtrip_preserves_data_and_old_query_compatibility()
    {
        await using var f = await TranscriptHotPathFixture.CreateAsync();
        await using var db = f.CreateDb();
        var (previous, current, _) = MigrationInfo(db);
        await AddIdentityRowsAsync(db, f.SessionIds[0]);
        var before = await FingerprintAsync(f.ConnectionString);
        // The pre-change binary still reads the same idle verdict with the additive indexes.
        (await TranscriptWorkingStateOracle.ReadAsync(db, [f.SessionIds[0]], default))[f.SessionIds[0]].ShouldBeFalse();
        await db.GetService<IMigrator>().MigrateAsync(previous);
        (await ReadCatalogAsync(f.ConnectionString)).ShouldBeEmpty();
        (await FingerprintAsync(f.ConnectionString)).ShouldBe(before);
        (await TranscriptWorkingStateOracle.ReadAsync(db, [f.SessionIds[0]], default))[f.SessionIds[0]].ShouldBeFalse();
        await db.GetService<IMigrator>().MigrateAsync(current);
        (await ReadCatalogAsync(f.ConnectionString)).Count.ShouldBe(3);
        (await FingerprintAsync(f.ConnectionString)).ShouldBe(before);
        await AssertIdentityRowsAsync(db, f.SessionIds[0]);
        (await TranscriptWorkingStateOracle.ReadAsync(db, [f.SessionIds[0]], default))[f.SessionIds[0]].ShouldBeFalse();
    }

    [Test]
    public async Task Concurrent_index_build_waits_for_old_writer_without_blocking_another_insert()
    {
        await using var f = await TranscriptHotPathFixture.CreateAsync();
        await using var db = f.CreateDb();
        var (previous, current, _) = MigrationInfo(db);
        await db.GetService<IMigrator>().MigrateAsync(previous);
        await using var writer = new NpgsqlConnection(f.ConnectionString);
        await writer.OpenAsync();
        await using var transaction = await writer.BeginTransactionAsync();
        await InsertAsync(writer, f.SessionIds[0], 1);
        var migrationConnection = new NpgsqlConnectionStringBuilder(f.ConnectionString)
        {
            ApplicationName = "c698-concurrent-migration", CommandTimeout = 60
        }.ConnectionString;
        await using var migrationDb = new AppDbContext(TestDbFixture.CreateDbContextOptions(migrationConnection));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var migrating = migrationDb.GetService<IMigrator>().MigrateAsync(current, timeout.Token);
        try
        {
            await using var observer = new NpgsqlConnection(f.ConnectionString);
            await observer.OpenAsync();
            var clock = Stopwatch.StartNew();
            var waiting = false;
            while (clock.Elapsed < TimeSpan.FromSeconds(20) && !migrating.IsCompleted)
            {
                await using var probe = new NpgsqlCommand("""
                    SELECT EXISTS (SELECT 1 FROM pg_stat_activity
                        WHERE datname = current_database() AND application_name = 'c698-concurrent-migration'
                          AND wait_event_type = 'Lock' AND query LIKE 'CREATE%INDEX%')
                    """, observer);
                waiting = (bool)(await probe.ExecuteScalarAsync())!;
                if (waiting) break;
                await Task.Delay(50);
            }
            waiting.ShouldBeTrue("index build must actually be waiting on the controlled old writer");
            await using var second = new NpgsqlConnection(f.ConnectionString);
            await second.OpenAsync();
            // A plain CREATE INDEX queued behind writer one would block this INSERT as well.
            await using (var budget = new NpgsqlCommand("SET statement_timeout = '2s'", second))
                await budget.ExecuteNonQueryAsync();
            await InsertAsync(second, f.SessionIds[1], 1);
            migrating.IsCompleted.ShouldBeFalse("the first writer remains open while the second commits");
        }
        finally
        {
            await transaction.RollbackAsync();
            await migrating;
        }
        var catalog = await ReadCatalogAsync(f.ConnectionString);
        catalog.Count.ShouldBe(3);
        catalog.ShouldAllBe(i => i.Valid && i.Ready);
        (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == f.SessionIds[1])).ShouldBe(1);
    }

    private static (string Previous, string Current, Migration Migration) MigrationInfo(AppDbContext db)
    {
        var assembly = db.GetService<IMigrationsAssembly>();
        var ids = assembly.Migrations.Keys.ToArray();
        var index = Array.FindIndex(ids, id => id.EndsWith("_AddTranscriptHotPathIndexes", StringComparison.Ordinal));
        index.ShouldBeGreaterThan(0);
        return (ids[index - 1], ids[index], assembly.CreateMigration(assembly.Migrations[ids[index]], db.Database.ProviderName!));
    }

    private static string[] NewIndexNames() =>
    [
        "IX_TranscriptEntries_AgentSessionId_Uuid",
        "IX_TranscriptEntries_End_AgentSessionId_Sequence",
        "IX_TranscriptEntries_End_AgentSessionId_Timestamp"
    ];

    private static async Task InsertAsync(NpgsqlConnection connection, Guid sessionId, long sequence)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO "TranscriptEntries" ("Id", "AgentSessionId", "Sequence", "Kind", "CreatedAt")
            VALUES (gen_random_uuid(), @id, @seq, 'AssistantText', now())
            """, connection);
        command.Parameters.AddWithValue("id", sessionId);
        command.Parameters.AddWithValue("seq", sequence);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AddIdentityRowsAsync(AppDbContext db, Guid sessionId)
    {
        db.TranscriptEntries.AddRange(
            new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = 200001,
                Uuid = "identity-pair", Kind = "AssistantText", Text = "preserve me", CreatedAt = DateTime.UtcNow },
            new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = 200002,
                Uuid = "identity-pair", Kind = "TurnEnd", CreatedAt = DateTime.UtcNow },
            new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = 200003,
                Uuid = null, Kind = "QueueEnqueue", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    private static async Task AssertIdentityRowsAsync(AppDbContext db, Guid sessionId)
    {
        db.ChangeTracker.Clear();
        var rows = await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId && t.Sequence >= 200001)
            .OrderBy(t => t.Sequence).ToListAsync();
        rows.Count.ShouldBe(3);
        rows.Select(t => t.Uuid).ShouldBe(["identity-pair", "identity-pair", null]);
        rows.Select(t => t.Kind).ShouldBe(["AssistantText", "TurnEnd", "QueueEnqueue"]);
        rows[0].Text.ShouldBe("preserve me");
    }

    private static async Task<string> FingerprintAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        // Hash all persisted columns in stable identity order, without materializing the history.
        await using var command = new NpgsqlCommand("""
            SELECT md5(string_agg(md5(row_to_json(t)::text), '' ORDER BY "Id")) FROM "TranscriptEntries" AS t
            """, connection) { CommandTimeout = 120 };
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private sealed record IndexInfo(string Name, bool Valid, bool Ready, bool Unique, long Bytes, string Definition);

    private static async Task<List<IndexInfo>> ReadCatalogAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT c.relname, i.indisvalid, i.indisready, i.indisunique, pg_relation_size(c.oid), pg_get_indexdef(c.oid)
            FROM pg_index AS i JOIN pg_class AS c ON c.oid = i.indexrelid
            WHERE i.indrelid = '"TranscriptEntries"'::regclass AND c.relname = ANY (@names) ORDER BY c.relname
            """, connection);
        command.Parameters.AddWithValue("names", NewIndexNames());
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<IndexInfo>();
        while (await reader.ReadAsync()) result.Add(new(reader.GetString(0), reader.GetBoolean(1),
            reader.GetBoolean(2), reader.GetBoolean(3), reader.GetInt64(4), reader.GetString(5)));
        return result;
    }
}
