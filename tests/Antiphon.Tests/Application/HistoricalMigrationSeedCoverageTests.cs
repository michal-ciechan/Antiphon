using System.Reflection;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class HistoricalMigrationSeedCoverageTests
{
    // AppDbContext tables written by the historical fixtures in the group 89 investigation.
    private static readonly HashSet<string> SeededTables = new(StringComparer.Ordinal)
    {
        "AgentIncidents", "Agents", "AgentSessions", "AgentSupervisionStates", "AgentTaskEvents",
        "AgentTaskLandNotifications", "AgentTaskLandRequests", "AgentTaskParks", "AgentTasks", "AgentTaskSyncDebts",
        "BoardColumns", "Boards", "CardRevisions", "Cards", "CardWorkflowRuns", "CardWorkflowStages",
        "CardWorktreeCleanupEndpoints", "CardWorktreeCleanups", "CardWorktreeCleanupTargets",
        "ChannelOutboundDeliveries", "ChatChannels", "HostBudgets", "HostLoadSamples",
        "ModelAvailabilityHolds", "OutputDistillations", "PipelineDefinitionRevisions", "PipelineDefinitions",
        "Projects", "ReviewComments", "ReviewThreads", "ReviewWorkflowRevisions", "RoutingPins", "RunnerRoutingSettings",
        "SessionQueuedMessages", "SessionRuntimeActions", "SessionTranscriptSummaries", "SessionTranscriptSummaryMaintenance",
        "StageOutcomes", "TaskWorktreeMirrorBindings", "TranscriptEntries", "WorkflowTemplates", "Worktrees",
        "WorktreeResidueCandidateCursors", "WorktreeResidueRunCandidates", "WorktreeResidueRuns",
    };

    // These nullable fields are excluded by the bound AgentTask context or by explicit SQL.
    // The HostLoadSamples old-writer test verifies that its required field is filled by PostgreSQL.
    private static readonly HashSet<(string Migration, SeedColumn Column)> ReviewedAdditions =
    [
        ("20261107034430_HostLoadTelemetryS1", new("AgentTaskEvents", "LoadAttempt", true, false)),
        ("20261107034430_HostLoadTelemetryS1", new("AgentTaskEvents", "LoadDispatchedAt", true, false)),
        ("20261107034430_HostLoadTelemetryS1", new("AgentTaskEvents", "LoadQueuedAt", true, false)),
        ("20261107034430_HostLoadTelemetryS1", new("AgentTaskEvents", "LoadRunnerId", true, false)),
        ("20261107034430_HostLoadTelemetryS1", new("AgentTaskEvents", "LoadSessionId", true, false)),
        ("20261107034430_HostLoadTelemetryS1", new("AgentTaskEvents", "LoadStatus", true, false)),
        ("20261107040142_HostLoadSnapshotMembership", new("HostLoadSamples", "CreatedTransactionId", false, true)),
        ("20261120091515_AddRequestedModelAlias", new("AgentTasks", "RequestedModelAlias", true, false)),
        ("20261125151517_AddCallerReportSettlementReceipt", new("AgentTasks", "ReceivedReportCompletedAt", true, false)),
        ("20261125151517_AddCallerReportSettlementReceipt", new("AgentTasks", "ReceivedReportPromptSequence", true, false)),
        ("20261125151517_AddCallerReportSettlementReceipt", new("AgentTasks", "ReceivedReportSessionId", true, false)),
    ];

    [Test]
    public async Task Post_tip_columns_on_historical_seed_tables_have_coverage(CancellationToken ct)
    {
        await using var schema = await MigrationTestDatabase.CreateReversibleTipAsync(ct);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var migrator = db.GetService<IMigrator>();
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToArray();
        pending.ShouldNotBeEmpty();

        var before = await ReadColumnsAsync(db, ct);
        var unreviewed = new List<string>();
        foreach (var migration in pending)
        {
            await migrator.MigrateAsync(migration, ct);
            var after = await ReadColumnsAsync(db, ct);
            unreviewed.AddRange(UnreviewedAdditions(migration, before, after));
            before = after;
        }

        unreviewed.ShouldBeEmpty("Check the historical writers for each listed column and cover their seed compatibility.");

        // Simulate a later handwritten Up against the same disposable PostgreSQL database.
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "OutputDistillations"
            ADD COLUMN "HistoricalSeedRequiredProbe" integer NOT NULL DEFAULT 0
            """, ct);
        UnreviewedAdditions("synthetic migration", before, await ReadColumnsAsync(db, ct))
            .ShouldBe(["synthetic migration: OutputDistillations.HistoricalSeedRequiredProbe (required)"]);

        var candidates = DiscoverHistoricalMigrationTests();
        var capacityRecovery = typeof(CapacityRecoveryCompatibilityTests).FullName;
        var group90 = new[]
        {
            $"{capacityRecovery}.{nameof(CapacityRecoveryCompatibilityTests.Card0412_V22_migrate_forward_preserves_legacy_holds)}",
            $"{capacityRecovery}.{nameof(CapacityRecoveryCompatibilityTests.Input_body_upgrade_preserves_a_preexisting_event_with_null_body)}",
        };
        group90.Except(candidates, StringComparer.Ordinal)
            .ShouldBeEmpty("Find both historical writers even when the class lacks the MigrationTests suffix.");
        candidates.ShouldNotContain(
            $"{capacityRecovery}.{nameof(CapacityRecoveryCompatibilityTests.Card0412_V23_legacy_available_for_stranded_current_session)}");
    }

    private static string[] DiscoverHistoricalMigrationTests() => typeof(HistoricalMigrationSeedCoverageTests).Assembly
        .GetTypes()
        .Where(type => type.Name.EndsWith("MigrationTests", StringComparison.Ordinal))
        .SelectMany(type => type.GetMethods().Where(method => method.IsDefined(typeof(TestAttribute)))
            .Select(method => $"{type.FullName}.{method.Name}"))
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static IEnumerable<string> UnreviewedAdditions(string migration, List<SeedColumn> before, List<SeedColumn> after)
    {
        var existingTables = before.Select(column => column.TableName).ToHashSet(StringComparer.Ordinal);
        var existingColumns = before.Select(column => (column.TableName, column.ColumnName)).ToHashSet();
        return after.Where(column => existingTables.Contains(column.TableName)
                && !existingColumns.Contains((column.TableName, column.ColumnName))
                && !ReviewedAdditions.Contains((migration, column)))
            .Select(column => $"{migration}: {column.TableName}.{column.ColumnName} ({(column.IsNullable ? "nullable" : "required")})");
    }

    private static async Task<List<SeedColumn>> ReadColumnsAsync(AppDbContext db, CancellationToken ct)
    {
        var columns = await db.Database.SqlQueryRaw<SeedColumn>("""
            SELECT table_name AS "TableName", column_name AS "ColumnName",
                   is_nullable = 'YES' AS "IsNullable", column_default IS NOT NULL AS "HasDefault"
            FROM information_schema.columns WHERE table_schema = current_schema()
            ORDER BY table_name, column_name
            """).ToListAsync(ct);
        return columns.Where(column => SeededTables.Contains(column.TableName)).ToList();
    }

    private sealed record SeedColumn(string TableName, string ColumnName, bool IsNullable, bool HasDefault);
}
