using System.Reflection;
using System.Runtime.CompilerServices;
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
    // Existing-table changes already checked against their predecessor writers. The host
    // load membership migration supplies a default, and the land-verification fields are
    // added before any fixture writes through the current AgentTaskLandings model.
    private static readonly HashSet<(string Migration, SeedColumn Column)> ReviewedAdditions =
    [
        ("20261107034430_HostLoadTelemetryS1", new("AgentTaskEvents", "LoadAttempt", true, false)),
        ("20261107034430_HostLoadTelemetryS1", new("AgentTaskEvents", "LoadDispatchedAt", true, false)),
        ("20261107034430_HostLoadTelemetryS1", new("AgentTaskEvents", "LoadQueuedAt", true, false)),
        ("20261107034430_HostLoadTelemetryS1", new("AgentTaskEvents", "LoadRunnerId", true, false)),
        ("20261107034430_HostLoadTelemetryS1", new("AgentTaskEvents", "LoadSessionId", true, false)),
        ("20261107034430_HostLoadTelemetryS1", new("AgentTaskEvents", "LoadStatus", true, false)),
        ("20261107040142_HostLoadSnapshotMembership", new("HostLoadHourlyRollups", "CreatedTransactionId", false, true)),
        ("20261107040142_HostLoadSnapshotMembership", new("HostLoadSamples", "CreatedTransactionId", false, true)),
        ("20261119045705_AddLandVerificationFailure", new("AgentTaskLandings", "VerificationFailingProjects", true, false)),
        ("20261119045705_AddLandVerificationFailure", new("AgentTaskLandings", "VerificationFailureOutput", true, false)),
        ("20261119045705_AddLandVerificationFailure", new("AgentTaskLandings", "VerificationFailureStep", true, false)),
        ("20261120091515_AddRequestedModelAlias", new("AgentTasks", "RequestedModelAlias", true, false)),
        ("20261125151517_AddCallerReportSettlementReceipt", new("AgentTasks", "ReceivedReportCompletedAt", true, false)),
        ("20261125151517_AddCallerReportSettlementReceipt", new("AgentTasks", "ReceivedReportPromptSequence", true, false)),
        ("20261125151517_AddCallerReportSettlementReceipt", new("AgentTasks", "ReceivedReportSessionId", true, false)),
        // The historical PR fixture writes through predecessor SQL. Reversible-tip task seeds
        // project AgentTask onto their older model, including its relationships and indexes.
        (MigrationTestDatabase.OpenReviewRunMigration, new("ReviewRuns", "GitHubPrNumber", true, false)),
        (MigrationTestDatabase.OpenReviewRunMigration, new("ReviewRuns", "GitHubRepo", true, false)),
        (MigrationTestDatabase.OpenReviewRunMigration, new("ReviewRuns", "GitHubAuthority", true, false)),
        (MigrationTestDatabase.OpenReviewRunMigration, new("ReviewRuns", "GitHubRepositoryId", true, false)),
        (MigrationTestDatabase.OpenReviewRunMigration, new("ReviewRuns", "IsDemo", false, true)),
        (MigrationTestDatabase.OpenReviewRunMigration, new("AgentTasks", "ReviewerRunId", true, false)),
        (MigrationTestDatabase.OpenReviewRunMigration, new("AgentTasks", "ReviewerNodeId", true, false)),
        (MigrationTestDatabase.OpenReviewRunMigration, new("AgentTasks", "ReviewerAttempt", true, false)),
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

        var candidates = DiscoverHistoricalMigrationTests();
        TestContext.Current!.OutputWriter.WriteLine(
            $"Historical migration candidates ({candidates.Length} methods):{Environment.NewLine}"
            + string.Join(Environment.NewLine, candidates));
        unreviewed.ShouldBeEmpty("Check the discovered historical migration candidates for writers of each listed column.");

        // Simulate a later handwritten Up against the same disposable PostgreSQL database.
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "OutputDistillations"
            ADD COLUMN "HistoricalSeedRequiredProbe" integer NOT NULL DEFAULT 0
            """, ct);
        UnreviewedAdditions("synthetic migration", before, await ReadColumnsAsync(db, ct))
            .ShouldBe(["synthetic migration: OutputDistillations.HistoricalSeedRequiredProbe (required)"]);

        candidates.ShouldContain(
            $"{typeof(PipelineSpeedIntegrationTests).FullName}.{nameof(PipelineSpeedIntegrationTests.Task_worlds_seed_their_owners_at_the_selected_migration)}");
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

    private const BindingFlags DeclaredMethods = BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static string[] DiscoverHistoricalMigrationTests()
    {
        var witness = typeof(HistoricalMigrationSeedCoverageTests);
        var tests = witness.Assembly.GetTypes()
            .Where(type => type != witness)
            .SelectMany(type => type.GetMethods(DeclaredMethods))
            .Where(method => method.IsDefined(typeof(TestAttribute)))
            .ToArray();

        // Scan each test or fixture body once, then follow incoming calls back to the tests.
        var pending = new Queue<MethodBase>(tests);
        var queued = tests.Select(test => test.MetadataToken).ToHashSet();
        var callers = new Dictionary<int, List<int>>();
        var historical = new HashSet<int>();
        while (pending.TryDequeue(out var method))
        {
            foreach (var called in ReferencedMethods(method))
            {
                if (IsHistoricalMigrationCall(called))
                {
                    historical.Add(method.MetadataToken);
                    continue;
                }

                if (called.Module != witness.Module)
                    continue;

                var token = called.MetadataToken;
                if (!callers.TryGetValue(token, out var incoming))
                    callers[token] = incoming = [];
                incoming.Add(method.MetadataToken);
                if (queued.Add(token))
                    pending.Enqueue(called);
            }
        }

        var historicalCalls = new Queue<int>(historical);
        while (historicalCalls.TryDequeue(out var token))
        {
            if (!callers.TryGetValue(token, out var incoming))
                continue;
            foreach (var caller in incoming)
                if (historical.Add(caller))
                    historicalCalls.Enqueue(caller);
        }

        return tests.Where(test => historical.Contains(test.MetadataToken))
            .Select(test => $"{test.DeclaringType!.FullName}.{test.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<MethodBase> ReferencedMethods(MethodBase method)
    {
        if (method.GetCustomAttribute<StateMachineAttribute>() is { } stateMachine)
            foreach (var stateMethod in stateMachine.StateMachineType.GetMethods(DeclaredMethods))
                yield return stateMethod;

        if (method.GetMethodBody() is { } body)
            foreach (var called in ProcessSpawnLimitTests.ReferencedMembers(method, body).OfType<MethodBase>())
                yield return called;
    }

    private static bool IsHistoricalMigrationCall(MethodBase method)
    {
        // The historical database helper also exposes a metadata-only migration-name query.
        if (method.DeclaringType == typeof(MigrationTestDatabase))
            return method.Name != nameof(MigrationTestDatabase.Names);

        return (method.DeclaringType == typeof(IMigrator)
                || method.DeclaringType == typeof(RelationalDatabaseFacadeExtensions))
            && method.Name is nameof(IMigrator.Migrate) or nameof(IMigrator.MigrateAsync)
            && method.GetParameters().Any(parameter => parameter.ParameterType == typeof(string));
    }

    private static IEnumerable<string> UnreviewedAdditions(string migration, List<SeedColumn> before, List<SeedColumn> after)
    {
        var existingTables = before.Select(column => column.TableName).ToHashSet(StringComparer.Ordinal);
        var existingColumns = before.Select(column => (column.TableName, column.ColumnName)).ToHashSet();
        return after.Where(column => existingTables.Contains(column.TableName)
                && !existingColumns.Contains((column.TableName, column.ColumnName))
                && !ReviewedAdditions.Contains((migration, column)))
            .Select(column => $"{migration}: {column.TableName}.{column.ColumnName} ({(column.IsNullable ? "nullable" : "required")})");
    }

    private static Task<List<SeedColumn>> ReadColumnsAsync(AppDbContext db, CancellationToken ct) =>
        db.Database.SqlQueryRaw<SeedColumn>("""
            SELECT table_name AS "TableName", column_name AS "ColumnName",
                   is_nullable = 'YES' AS "IsNullable", column_default IS NOT NULL AS "HasDefault"
            FROM information_schema.columns WHERE table_schema = current_schema()
            ORDER BY table_name, column_name
            """).ToListAsync(ct);

    private sealed record SeedColumn(string TableName, string ColumnName, bool IsNullable, bool HasDefault);
}
