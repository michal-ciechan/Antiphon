using System.Data;
using System.Reflection;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1619's exit-signal migration keeps the rc id <c>20261030141651</c>.
/// An rc database already has that history row and the four columns; a master
/// database has CARD-1610 applied and neither. EF must reach head both ways.
/// </summary>
[Category("Integration")]
public sealed class Card1619ExitMigrationIdTests
{
    private const string RcExitMigrationId = "20261030141651_AddSessionExitSignalEvidence";
    private const string SupersededExitMigrationId = "20261031180000_AddSessionExitSignalEvidence";
    private const string BeforeEitherChain = "20261028170715_AddAgentSessionCodexTokenCount";
    private const string Card1610Floor = "20261030114738_AddTaskCeilingSupervision";
    private const string Card1610Head = "20261030214946_CloseTaskSupervisionReportWatches";

    private static readonly string[] Card1610Migrations =
    [
        "20261030114738_AddTaskCeilingSupervision",
        "20261030143219_AddTaskSupervisionQueuePurpose",
        "20261030214946_CloseTaskSupervisionReportWatches",
    ];

    [Test]
    public void Migration_list_uses_the_rc_exit_signal_id()
    {
        var options = TestDbFixture.CreateDbContextOptions(
            "Host=127.0.0.1;Database=antiphon_exit_migration_id;Username=unused;Password=unused");
        using var db = new AppDbContext(options);
        var migrations = db.Database.GetMigrations().ToArray();
        var exit = migrations.Where(IsExitSignalMigration).ToArray();
        exit.ShouldBe([RcExitMigrationId]);
        migrations.ShouldNotContain(SupersededExitMigrationId);

        var types = typeof(AppDbContext).Assembly.GetTypes()
            .Where(type => type.Name == "AddSessionExitSignalEvidence" && type.IsSubclassOf(typeof(Migration)))
            .ToArray();
        types.Length.ShouldBe(1);
        types[0].GetCustomAttribute<MigrationAttribute>()!.Id.ShouldBe(RcExitMigrationId);
    }

    [Test]
    [Timeout(300_000)]
    public async Task Rc_ledger_upgrades_to_head_and_applies_card1610(CancellationToken cancellationToken)
    {
        await using var schema = await MigrationTestDatabase.CreateAsync(BeforeEitherChain, cancellationToken);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var migrator = db.GetService<IMigrator>();

        Applied(db).ShouldNotContain(RcExitMigrationId);
        foreach (var id in Card1610Migrations)
            Applied(db).ShouldNotContain(id);
        (await ExitColumnsAsync(db, cancellationToken)).ShouldBeEmpty();

        var exitId = db.Database.GetMigrations().Single(IsExitSignalMigration);
        await ApplyMigrationUpAsync(db, exitId, RcExitMigrationId, cancellationToken);
        Applied(db).ShouldContain(RcExitMigrationId);
        foreach (var id in Card1610Migrations)
            Applied(db).ShouldNotContain(id);
        await AssertExitColumnsAsync(db, cancellationToken);

        await migrator.MigrateAsync(targetMigration: null, cancellationToken);
        Applied(db).ShouldBe(db.Database.GetMigrations().ToArray());
        foreach (var id in Card1610Migrations)
            Applied(db).ShouldContain(id);
        await AssertExitColumnsAsync(db, cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task Master_ledger_applies_rc_exit_migration_out_of_order(CancellationToken cancellationToken)
    {
        await using var schema = await MigrationTestDatabase.CreateAsync(Card1610Floor, cancellationToken);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var migrator = db.GetService<IMigrator>();
        Applied(db).ShouldContain(Card1610Floor);
        Applied(db).ShouldNotContain(RcExitMigrationId);
        Applied(db).ShouldNotContain(SupersededExitMigrationId);

        foreach (var id in db.Database.GetMigrations())
        {
            if (string.CompareOrdinal(id, Card1610Floor) <= 0 || string.CompareOrdinal(id, Card1610Head) > 0)
                continue;
            if (IsExitSignalMigration(id))
                continue;
            await ApplyMigrationUpAsync(db, id, id, cancellationToken);
        }

        Applied(db).ShouldContain(Card1610Head);
        Applied(db).ShouldNotContain(RcExitMigrationId);
        (await ExitColumnsAsync(db, cancellationToken)).ShouldBeEmpty();

        await migrator.MigrateAsync(targetMigration: null, cancellationToken);
        var applied = Applied(db);
        applied.ShouldContain(RcExitMigrationId);
        applied.ShouldNotContain(SupersededExitMigrationId);
        foreach (var id in Card1610Migrations)
            applied.ShouldContain(id);
        applied.ShouldBe(db.Database.GetMigrations().ToArray());
        await AssertExitColumnsAsync(db, cancellationToken);
    }

    private static bool IsExitSignalMigration(string migrationId) =>
        migrationId.EndsWith("_AddSessionExitSignalEvidence", StringComparison.Ordinal);

    private static string[] Applied(AppDbContext db) => db.Database.GetAppliedMigrations().ToArray();

    private static async Task ApplyMigrationUpAsync(
        AppDbContext db, string migrationId, string historyId, CancellationToken cancellationToken)
    {
        var provider = db.Database.ProviderName ?? throw new InvalidOperationException("no database provider");
        var assembly = db.GetService<IMigrationsAssembly>();
        if (!assembly.Migrations.TryGetValue(migrationId, out var migrationType))
            throw new InvalidOperationException($"assembly is missing {migrationId}");
        var migration = assembly.CreateMigration(migrationType, provider);
        var builder = new MigrationBuilder(provider);
        var up = typeof(Migration).GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Migration.Up is missing");
        up.Invoke(migration, [builder]);
        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(
            builder.Operations, model: null, MigrationsSqlGenerationOptions.Default);
        await db.GetService<IMigrationCommandExecutor>().ExecuteNonQueryAsync(
            commands, db.GetService<IRelationalConnection>(), cancellationToken);

        var version = await ProductVersionAsync(db, cancellationToken);
        var insert = db.GetService<IHistoryRepository>().GetInsertScript(new HistoryRow(historyId, version));
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = insert;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string> ProductVersionAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "ProductVersion" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1""";
        var version = (string?)await command.ExecuteScalarAsync(cancellationToken);
        version.ShouldNotBeNullOrWhiteSpace("history rows record the EF product version");
        return version;
    }

    private static async Task<string[]> ExitColumnsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.relname, a.attname, a.attnotnull, format_type(a.atttypid, a.atttypmod)
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public'
              AND a.attnum > 0 AND NOT a.attisdropped
              AND c.relname IN ('RunAttempts', 'AgentSessions', 'AgentIncidents')
              AND a.attname IN ('ExitSignal', 'ExitStopIntentJson')
            ORDER BY c.relname, a.attname
            """;
        var found = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var table = reader.GetString(0);
            var column = reader.GetString(1);
            reader.GetBoolean(2).ShouldBeFalse($"{table}.{column} stays nullable");
            var type = reader.GetString(3);
            if (column == "ExitSignal")
                type.ShouldBe("integer", $"{table}.{column}");
            else
                type.ShouldBe("character varying(4096)", $"{table}.{column}");
            found.Add($"{table}.{column}");
        }
        return found.ToArray();
    }

    private static async Task AssertExitColumnsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var found = await ExitColumnsAsync(db, cancellationToken);
        found.ShouldBe(
        [
            "AgentIncidents.ExitSignal",
            "AgentSessions.ExitSignal",
            "AgentSessions.ExitStopIntentJson",
            "RunAttempts.ExitSignal",
        ]);
    }
}
