using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration"), Category("Slow"), ParallelLimiter<ProcessSpawnLimit>]
[Timeout(60_000)] // CARD-1514: provisional; real additive migration/refused Down, measured in CP-M1.
public sealed class C1514HostLoadMigrationTests
{
    private const string Previous = "20261106032116_AddRefOnlyParkWorkspaceSnapshot";
    private const string Telemetry = "20261107034430_HostLoadTelemetryS1";
    private const string Membership = "20261107040142_HostLoadSnapshotMembership";
    private const string Catchup = "20261107104114_HostLoadRetentionCatchup";
    private IsolatedTestSchema? _schema;

    [Before(Class)]
    public static Task WarmDatabase() => TestDbFixture.Lifecycle.EnsureReadyAsync();

    [Before(Test)]
    public async Task PrepareHistoricalBaseline()
    {
        if (!TestContext.Current!.Metadata.TestDetails.TestName.StartsWith(nameof(UpgradeAndRefusedDowngradePreserveDurableRows), StringComparison.Ordinal)) return;
        // A new database is necessary because Up/Down are the subject. Share the
        // assembly container, but never bypass a newer migration's Down guard.
        using var setup = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        _schema = await MigrationTestDatabase.CreateAsync(Previous, setup.Token);
    }

    [After(Test)]
    public async Task ReleaseHistoricalBaseline()
    {
        if (_schema is not null) await _schema.DisposeAsync();
    }

    [Test]
    public void ChainIsLinearAndSnapshotMatchesModel()
    {
        using var db = Metadata();
        var chain = db.Database.GetMigrations().ToArray();
        var previous = Array.IndexOf(chain, Previous);
        previous.ShouldBeGreaterThanOrEqualTo(0);
        chain.Skip(previous).Take(3).ShouldBe([Previous, Telemetry, Membership], "linear-after-current-master");
        chain[previous + 3].ShouldBe(Catchup, "retention-catchup-follows-membership");
        (chain.Length - previous).ShouldBe(4, "no-migration-after-retention-catchup");
        db.Database.HasPendingModelChanges().ShouldBeFalse("generated-snapshot-matches-runtime-model");
    }

    [Test]
    public void UpgradeOperationsAreAdditiveAndPreserveReplayKeys()
    {
        var first = Migration(Telemetry).UpOperations;
        first.ShouldAllBe(op => op is AddColumnOperation || op is CreateTableOperation || op is CreateIndexOperation);
        first.OfType<CreateTableOperation>().Select(t => t.Name).ShouldBe([
            "AgentTaskLoadWindows", "HostLoadHourlyRollups", "HostLoadIngestCursors", "HostLoadObservedEvents",
            "HostLoadSampleIdentities", "HostLoadSamples", "HostLoadTaskMetricBuckets"]);
        var eventColumns = first.OfType<AddColumnOperation>().ToArray();
        eventColumns.Select(c => c.Name).ShouldBe(["LoadAttempt", "LoadDispatchedAt", "LoadQueuedAt", "LoadRunnerId", "LoadSessionId", "LoadStatus"]);
        eventColumns.ShouldAllBe(c => c.Table == "AgentTaskEvents" && c.IsNullable);
        first.OfType<CreateIndexOperation>().ShouldContain(i => i.Table == "HostLoadSamples" && i.IsUnique
            && i.Columns.SequenceEqual(new[] { "RunnerStoreId", "BootId", "Sequence" }));
        var membership = Migration(Membership).UpOperations;
        membership.Count.ShouldBe(2);
        membership.ShouldAllBe(op => op is AddColumnOperation);
        var columns = membership.Cast<AddColumnOperation>().ToArray();
        columns.Select(c => c.Table).ShouldBe(["HostLoadSamples", "HostLoadHourlyRollups"]);
        columns.ShouldAllBe(c => c.Name == "CreatedTransactionId" && !c.IsNullable && c.ColumnType == "bigint"
            && c.DefaultValueSql == "pg_current_xact_id()::text::bigint");
        var catchup = Migration(Catchup).UpOperations;
        catchup.ShouldAllBe(op => op is CreateTableOperation || op is CreateIndexOperation);
        catchup.OfType<CreateTableOperation>().Select(t => t.Name).ShouldBe(["HostLoadRetentionStates"]);
        catchup.OfType<CreateIndexOperation>().Select(i => i.Name).Order().ShouldBe([
            "IX_HostLoadHourlyRollups_Age", "IX_HostLoadSamples_DerivedAge"]);
    }

    [Test, Arguments(Telemetry), Arguments(Membership), Arguments(Catchup)]
    public void BothDownOperationsRefuseDestructiveDowngrade(string migration)
    {
        var error = Should.Throw<NotSupportedException>(() => _ = Migration(migration).DownOperations);
        error.Message.ShouldContain("cannot be discarded by a downgrade");
    }

    [Test, Arguments(Telemetry, Previous), Arguments(Membership, Telemetry), Arguments(Catchup, Membership)]
    public async Task UpgradeAndRefusedDowngradePreserveDurableRows(string target, string previous, CancellationToken ct)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(_schema!.ConnectionString));
        var migrator = db.GetService<IMigrator>();
        var id = Guid.NewGuid();
        var now = new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AgentTaskSyncDebts" ("Id", "TaskId", "Attempt", "SettlementEventId", "State", "ReasonCode",
                "Attempts", "Revision", "CreatedAt", "UpdatedAt")
            VALUES ({id}, {id}, {1}, {Guid.NewGuid()}, {0}, {"legacy-telemetry-migration"}, {0}, {0L}, {now}, {now})
            """, ct);
        using (TestTiming.BeginPhase(TestPhaseKind.DbMigrate, "host-load additive upgrade " + target))
            await migrator.MigrateAsync(target, ct);
        const string payload = "{\"source\":\"owned-durable-migration-receipt\",\"cpuPercent\":40}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "HostLoadSamples" ("RunnerStoreId", "BootId", "Sequence", "RunnerId", "SampledAt", "IngestedAt", "IsPrimary", "PayloadJson")
            VALUES ({id}, {id}, {101L}, {"owned-migration-runner"}, {now}, {now}, {true}, {payload})
            """, ct);
        var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
        applied.ShouldContain(target);
        var error = await Should.ThrowAsync<NotSupportedException>(() => migrator.MigrateAsync(previous, ct));
        error.Message.ShouldContain("cannot be discarded by a downgrade");
        (await db.Database.GetAppliedMigrationsAsync(ct)).ShouldBe(applied, "refused-down-preserves-migration-history");
        (await db.Database.SqlQueryRaw<string>("SELECT \"PayloadJson\" AS \"Value\" FROM \"HostLoadSamples\" WHERE \"RunnerStoreId\" = {0}", id).SingleAsync(ct))
            .ShouldBe(payload, "refused-down-preserves-exact-durable-bytes");
        (await db.Database.SqlQueryRaw<string>("SELECT \"ReasonCode\" AS \"Value\" FROM \"AgentTaskSyncDebts\" WHERE \"Id\" = {0}", id).SingleAsync(ct))
            .ShouldBe("legacy-telemetry-migration", "additive-upgrade-preserves-legacy-row");
        if (target is Membership or Catchup)
            (await db.Database.SqlQueryRaw<long>("SELECT \"CreatedTransactionId\" AS \"Value\" FROM \"HostLoadSamples\" WHERE \"RunnerStoreId\" = {0}", id).SingleAsync(ct))
                .ShouldBeGreaterThan(0, "old-writer-gets-membership-default");
    }

    private static AppDbContext Metadata() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=localhost;Database=metadata_only").Options);

    private static Migration Migration(string id)
    {
        using var db = Metadata();
        var assembly = db.GetService<IMigrationsAssembly>();
        return assembly.CreateMigration(assembly.Migrations[id], db.Database.ProviderName!);
    }
}
