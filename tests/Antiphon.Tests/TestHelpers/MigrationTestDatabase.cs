using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Historical migration subjects start before their Up, without downgrading newer durable data.</summary>
internal static class MigrationTestDatabase
{
    /// <summary>
    /// Predecessor of HostLoadTelemetryS1. Later host-load Downs throw, so a rewind that only
    /// needs reversible history starts here and walks forward.
    /// </summary>
    public const string LastReversibleMigration = "20261106032116_AddRefOnlyParkWorkspaceSnapshot";

    public static Task<IsolatedTestSchema> CreateReversibleTipAsync(CancellationToken ct = default) =>
        CreateAsync(LastReversibleMigration, ct);

    public static string[] Names()
    {
        using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions("Host=localhost;Database=metadata_only"));
        return db.Database.GetMigrations().ToArray();
    }

    public static async Task<IsolatedTestSchema> CreateAsync(string migration, CancellationToken ct)
    {
        await TestDbFixture.Lifecycle.EnsureReadyAsync();
        var database = $"test_{Guid.NewGuid():N}";
        var connection = new NpgsqlConnectionStringBuilder(TestDbFixture.MaintenanceConnectionString) { Database = database };
        await using (var maintenance = new NpgsqlConnection(TestDbFixture.MaintenanceConnectionString))
        {
            using var phase = TestTiming.BeginPhase(TestPhaseKind.DbCreate, "historical migration database");
            await maintenance.OpenAsync(ct);
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database} TEMPLATE template0", maintenance) { CommandTimeout = 30 };
            await create.ExecuteNonQueryAsync(ct);
        }
        var schema = new IsolatedTestSchema(database, connection.ConnectionString);
        try
        {
            using var phase = TestTiming.BeginPhase(TestPhaseKind.DbMigrate, "historical migration baseline " + migration);
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            await db.GetService<IMigrator>().MigrateAsync(migration, ct);
            return schema;
        }
        catch { await schema.DisposeAsync(); throw; }
    }
}
