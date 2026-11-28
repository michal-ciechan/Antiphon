using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Migrations;
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

    /// <summary>Seeds task graphs at the reversible tip with that migration's AgentTask columns.</summary>
    public static AppDbContext CreateReversibleTipSeedContext(IsolatedTestSchema schema) =>
        CreateReversibleTipSeedContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    public static AppDbContext CreateReversibleTipSeedContext(DbContextOptions<AppDbContext> options) =>
        new ReversibleTipSeedDbContext(options);

    private sealed class ReversibleTipSeedDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            var task = builder.Entity<AgentTask>();
            var legacy = new AddRefOnlyParkWorkspaceSnapshot().TargetModel.FindEntityType(typeof(AgentTask).FullName!)!;
            foreach (var property in task.Metadata.GetProperties().Where(p => legacy.FindProperty(p.Name) is null).ToArray())
            {
                // Explicit relationships and indexes can retain a shadow property after Ignore.
                foreach (var foreignKey in property.GetContainingForeignKeys().ToArray())
                    task.Metadata.RemoveForeignKey(foreignKey);
                foreach (var index in property.GetContainingIndexes().ToArray())
                    task.Metadata.RemoveIndex(index);
                task.Ignore(property.Name);
            }
        }
    }

    /// <summary>The review engine migration. Its Down throws, so a rewind detaches an empty copy instead.</summary>
    public const string ReviewEngineMigration = "20261122173647_AddReviewEngine";

    /// <summary>The message <c>AddReviewEngine.Down</c> throws, which the detach repeats for stored rows.</summary>
    public const string ReviewEngineDowngradeRefusal = "Review engine tables cannot be discarded by a downgrade.";

    /// <summary>Every table <c>AddReviewEngine</c> creates, dependents before the tables they reference.</summary>
    public static readonly string[] ReviewEngineTables =
    [
        "ReviewContextPacks", "ReviewFindingEvents", "ReviewGatePasses", "ReviewIterations", "ReviewNodeExecutions",
        "ReviewPackLedgerEntries", "ReviewFindings", "ReviewGates", "ReviewRuns", "ReviewSubjects",
        "ReviewWorkflowRevisions",
    ];

    private static readonly string ReviewEngineRowsSql =
        "SELECT (" + string.Join(" OR ", ReviewEngineTables.Select(t => $"EXISTS (SELECT 1 FROM \"{t}\")"))
        + ")::int AS \"Value\"";

    private static readonly string ReviewEngineDetachSql =
        string.Concat(ReviewEngineTables.Select(t => $"DROP TABLE IF EXISTS \"{t}\";\n"))
        + """
          DROP FUNCTION IF EXISTS "FillReviewRunGitHubIdentity"();
          DROP FUNCTION IF EXISTS "NormalizeReviewGitHubRepo"(text);
          """
        + $"\nDELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" IN ('{ReviewEngineMigration}', '{OpenReviewRunMigration}');";

    public const string OpenReviewRunMigration = "20261127142459_EnforceOneOpenReviewRunPerPullRequest";

    public static string[] Names()
    {
        using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions("Host=localhost;Database=metadata_only"));
        return db.Database.GetMigrations().ToArray();
    }

    /// <summary>
    /// Drops additive objects whose Down methods throw, and their history rows, so a cloned tip
    /// can return to <see cref="LastReversibleMigration"/> before an older reversible migration runs.
    /// Empty caller report receipts are detached first, then empty review engine tables, then an
    /// empty requested-model alias, then an
    /// empty land-verification addition.
    /// A stored value is refused with the migration's own message and is not dropped.
    /// <see cref="RequestedModelAliasSchema"/> also adds the alias column for fixture commands
    /// that name it before its migration.
    /// </summary>
    public static async Task DetachHostLoadAsync(AppDbContext db, CancellationToken ct = default)
    {
        await DetachCallerReportReceiptAsync(db, ct);
        await DetachReviewEngineAsync(db, ct);
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

    /// <summary>The caller report receipt migration. Its Down throws, so a rewind detaches an empty copy instead.</summary>
    public const string CallerReportReceiptMigration = "20261125151517_AddCallerReportSettlementReceipt";

    /// <summary>The message <c>AddCallerReportSettlementReceipt.Down</c> throws, which the detach repeats for stored receipts.</summary>
    public const string CallerReportReceiptDowngradeRefusal = "A caller's settlement receipt cannot be discarded by a downgrade.";

    /// <summary>
    /// <c>AddCallerReportSettlementReceipt.Down</c> always throws. A tip clone that never recorded a
    /// receipt drops the three empty columns and the history row here, so its schema is exactly the
    /// predecessor's. A stored receipt is refused with the migration's own message and is not dropped.
    /// </summary>
    public static async Task DetachCallerReportReceiptAsync(AppDbContext db, CancellationToken ct = default)
    {
        var present = await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM information_schema.columns
            WHERE table_schema = current_schema() AND table_name = 'AgentTasks'
              AND column_name = 'ReceivedReportCompletedAt'
            """).SingleAsync(ct);
        if (present == 0)
            return;

        var recorded = await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM "AgentTasks"
            WHERE "ReceivedReportCompletedAt" IS NOT NULL
               OR "ReceivedReportSessionId" IS NOT NULL
               OR "ReceivedReportPromptSequence" IS NOT NULL
            """).SingleAsync(ct);
        if (recorded > 0)
            throw new NotSupportedException(CallerReportReceiptDowngradeRefusal);

        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "AgentTasks" DROP COLUMN IF EXISTS "ReceivedReportPromptSequence";
            ALTER TABLE "AgentTasks" DROP COLUMN IF EXISTS "ReceivedReportSessionId";
            ALTER TABLE "AgentTasks" DROP COLUMN IF EXISTS "ReceivedReportCompletedAt";
            DELETE FROM "__EFMigrationsHistory"
            WHERE "MigrationId" = '20261125151517_AddCallerReportSettlementReceipt';
            """, ct);
    }

    /// <summary>
    /// <c>AddReviewEngine.Down</c> always throws. A tip clone whose review engine tables are all
    /// empty reverses the review identity migration before dropping the review tables and history.
    /// A stored review row in any of them is
    /// refused with that same message; this helper does not call Down and does not discard the rows.
    /// </summary>
    public static async Task DetachReviewEngineAsync(AppDbContext db, CancellationToken ct = default)
    {
        var present = await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM information_schema.tables
            WHERE table_schema = current_schema() AND table_name = 'ReviewRuns'
            """).SingleAsync(ct);
        if (present == 0)
            return;

        var recorded = await db.Database.SqlQueryRaw<int>(ReviewEngineRowsSql).SingleAsync(ct);
        if (recorded > 0)
            throw new NotSupportedException(ReviewEngineDowngradeRefusal);

        // This fixture can clone a tip with unrelated later migrations. Apply only the identity
        // migration's Down SQL, using its own operations to remove the dependent AgentTasks FK.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if ((await db.Database.GetAppliedMigrationsAsync(ct)).Contains(OpenReviewRunMigration))
        {
            var migrations = db.GetService<IMigrationsAssembly>();
            var identity = migrations.CreateMigration(migrations.Migrations[OpenReviewRunMigration], db.Database.ProviderName!);
            var generator = db.GetService<IMigrationsSqlGenerator>();
            foreach (var command in generator.Generate(identity.DownOperations))
                await db.Database.ExecuteSqlRawAsync(command.CommandText, ct);
        }
        await db.Database.ExecuteSqlRawAsync(ReviewEngineDetachSql, ct);
        await transaction.CommitAsync(ct);
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
