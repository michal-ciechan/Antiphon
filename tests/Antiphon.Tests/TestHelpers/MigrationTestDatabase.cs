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

    /// <summary>
    /// Drops additive objects whose Down methods throw, and their history rows, so a cloned tip
    /// can return to <see cref="LastReversibleMigration"/> before an older reversible migration runs.
    /// An empty requested-model alias is detached first, then an empty land-verification addition.
    /// A stored value is refused with the migration's own message and is not dropped.
    /// Historical seeds that insert the current <c>AgentTask</c> shape into a pre-alias schema are
    /// covered by <see cref="RequestedModelAliasSchema"/> on every fixture context, because those
    /// seeds never reach this rewind helper.
    /// </summary>
    public static async Task DetachHostLoadAsync(AppDbContext db, CancellationToken ct = default)
    {
        await DetachRequestedModelAliasAsync(db, ct);
        await DetachLandVerificationFailureAsync(db, ct);
        await db.Database.ExecuteSqlRawAsync(
            """
            DROP TABLE IF EXISTS "CardCompletionShadowEvents";
            DROP TABLE IF EXISTS "HostLoadRetentionStates";
            DROP TABLE IF EXISTS "HostLoadTaskMetricBuckets";
            DROP TABLE IF EXISTS "AgentTaskLoadWindows";
            DROP TABLE IF EXISTS "HostLoadHourlyRollups";
            DROP TABLE IF EXISTS "HostLoadIngestCursors";
            DROP TABLE IF EXISTS "HostLoadObservedEvents";
            DROP TABLE IF EXISTS "HostLoadSampleIdentities";
            DROP TABLE IF EXISTS "HostLoadSamples";
            DROP INDEX IF EXISTS "IX_AgentTaskEvents_LoadObservation";
            DROP INDEX IF EXISTS "IX_TranscriptEntries_AgentSessionId_Kind_Sequence";
            ALTER TABLE "AgentTaskEvents" DROP COLUMN IF EXISTS "LoadAttempt";
            ALTER TABLE "AgentTaskEvents" DROP COLUMN IF EXISTS "LoadDispatchedAt";
            ALTER TABLE "AgentTaskEvents" DROP COLUMN IF EXISTS "LoadQueuedAt";
            ALTER TABLE "AgentTaskEvents" DROP COLUMN IF EXISTS "LoadRunnerId";
            ALTER TABLE "AgentTaskEvents" DROP COLUMN IF EXISTS "LoadSessionId";
            ALTER TABLE "AgentTaskEvents" DROP COLUMN IF EXISTS "LoadStatus";
            DELETE FROM "__EFMigrationsHistory"
            WHERE "MigrationId" IN (
                '20261107034430_HostLoadTelemetryS1',
                '20261107040142_HostLoadSnapshotMembership',
                '20261107104114_HostLoadRetentionCatchup',
                '20261108121426_Card1773TranscriptPromptStampIndex',
                '20261110213738_Card1294ShadowCohort');
            """, ct);
    }

    /// <summary>
    /// <c>AddRequestedModelAlias.Down</c> always throws. A tip clone that never stored an alias
    /// drops the empty column and the history row here. A stored alias is refused with that same
    /// message; this helper does not call Down and does not discard the row.
    /// </summary>
    private static async Task DetachRequestedModelAliasAsync(AppDbContext db, CancellationToken ct)
    {
        var present = await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM information_schema.columns
            WHERE table_schema = current_schema() AND table_name = 'AgentTasks'
              AND column_name = 'RequestedModelAlias'
            """).SingleAsync(ct);
        if (present == 0)
            return;

        var recorded = await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM "AgentTasks"
            WHERE "RequestedModelAlias" IS NOT NULL
            """).SingleAsync(ct);
        if (recorded > 0)
            throw new NotSupportedException(
                "An explicit requested model alias cannot be discarded by a downgrade.");

        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "AgentTasks" DROP COLUMN IF EXISTS "RequestedModelAlias";
            DELETE FROM "__EFMigrationsHistory"
            WHERE "MigrationId" = '20261120091515_AddRequestedModelAlias';
            """, ct);
    }

    /// <summary>
    /// <c>AddLandVerificationFailure.Down</c> always throws. A tip clone that never stored a
    /// failure value drops the empty columns and the history row here. A stored value is refused
    /// with that same message; this helper does not call Down and does not discard the row.
    /// </summary>
    private static async Task DetachLandVerificationFailureAsync(AppDbContext db, CancellationToken ct)
    {
        var present = await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM information_schema.columns
            WHERE table_schema = current_schema() AND table_name = 'AgentTaskLandings'
              AND column_name = 'VerificationFailureStep'
            """).SingleAsync(ct);
        if (present == 0)
            return;

        var recorded = await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM "AgentTaskLandings"
            WHERE "VerificationFailureStep" IS NOT NULL
               OR "VerificationFailureOutput" IS NOT NULL
               OR "VerificationFailingProjects" IS NOT NULL
            """).SingleAsync(ct);
        if (recorded > 0)
            throw new NotSupportedException(
                "Recorded land verification failures cannot be discarded by a downgrade.");

        await db.Database.ExecuteSqlRawAsync(
            """
            DROP INDEX IF EXISTS "IX_AgentTaskLandings_TargetBuildFailure";
            ALTER TABLE "AgentTaskLandings" DROP COLUMN IF EXISTS "VerificationFailingProjects";
            ALTER TABLE "AgentTaskLandings" DROP COLUMN IF EXISTS "VerificationFailureOutput";
            ALTER TABLE "AgentTaskLandings" DROP COLUMN IF EXISTS "VerificationFailureStep";
            DELETE FROM "__EFMigrationsHistory"
            WHERE "MigrationId" = '20261119045705_AddLandVerificationFailure';
            """, ct);
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
