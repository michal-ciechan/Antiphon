using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[Timeout(60_000)] // CARD-0824 provisional: isolated clone and subject-specific migration round trips.
public sealed class C0824RefOnlySnapshotMigrationTests
{
    private const string Previous = "20261105121558_Card0824RefOnlyDesktopCleanup";
    private const string Debt = "20261106005946_AddRefOnlySettlementDebtWorkspaceSnapshot";
    private const string Park = "20261106032116_AddRefOnlyParkWorkspaceSnapshot";
    private const string Telemetry = "20261107034430_HostLoadTelemetryS1";
    private const string Membership = "20261107040142_HostLoadSnapshotMembership";
    private const string Catchup = "20261107104114_HostLoadRetentionCatchup";
    private const string PromptStamp = "20261108121426_Card1773TranscriptPromptStampIndex";
    private IsolatedTestSchema? _historical;

    [Before(Class)]
    public static Task WarmDatabase() => TestDbFixture.Lifecycle.EnsureReadyAsync();

    [Before(Test)]
    public async Task PrepareHistoricalBaseline()
    {
        var name = TestContext.Current!.Metadata.TestDetails.TestName;
        if (name == nameof(Migration_chain_is_linear_and_snapshot_matches_the_final_model)) return;
        // These round trips exercise the historical migrations. Starting from
        // the current template would first hit the later telemetry Down guard.
        using var setup = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        _historical = await MigrationTestDatabase.CreateAsync(
            name.StartsWith(nameof(Additive_upgrade_and_empty_downgrade_preserve_legacy_rows), StringComparison.Ordinal) ? Previous : Park,
            setup.Token);
    }

    [After(Test)]
    public async Task ReleaseHistoricalBaseline()
    {
        if (_historical is not null) await _historical.DisposeAsync();
    }

    [Test]
    public void Migration_chain_is_linear_and_snapshot_matches_the_final_model()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only").Options);
        var chain = db.Database.GetMigrations().ToArray();
        chain.TakeLast(6).ShouldBe(new[] { Debt, Park, Telemetry, Membership, Catchup, PromptStamp }, "linear-after-master-with-original-ids");
        db.Database.HasPendingModelChanges().ShouldBeFalse("final-model-matches-generated-snapshot");
    }

    [Test]
    public async Task Additive_upgrade_and_empty_downgrade_preserve_legacy_rows(CancellationToken ct)
    {
        // A fresh migrated clone is necessary: Up/Down are the subject, never the shared template.
        var schema = _historical!;
        await using var db = NewDb(schema);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous, ct);
        (await SnapshotColumnCountAsync(db, ct)).ShouldBe(0);
        var id = await InsertLegacyRowsAsync(db, ct);
        await migrator.MigrateAsync(Debt, ct);
        (await SnapshotColumnCountAsync(db, ct)).ShouldBe(1, "debt-up-only");
        await migrator.MigrateAsync(Park, ct);
        (await SnapshotColumnCountAsync(db, ct)).ShouldBe(2, "both-additive-nullable-columns");
        (await db.AgentTaskSyncDebts.AsNoTracking().SingleAsync(d => d.Id == id, ct))
            .WorkspaceSnapshotJson.ShouldBeNull("old-writer-debt-default-remains-null");
        (await db.AgentTaskParks.AsNoTracking().SingleAsync(p => p.Id == id, ct))
            .WorkspaceSnapshotJson.ShouldBeNull("old-writer-park-default-remains-null");
        await AssertLegacyRowsAsync(db, id, ct);
        await migrator.MigrateAsync(Previous, ct);
        (await SnapshotColumnCountAsync(db, ct)).ShouldBe(0, "empty-snapshot-columns-can-be-removed");
        await AssertLegacyRowsAsync(db, id, ct);
        await migrator.MigrateAsync(Park, ct);
        // Finish at the current model before the existing no-pending assertion.
        // Only Up follows the historical round trip; no newer Down is bypassed.
        await migrator.MigrateAsync(cancellationToken: ct);
        (await db.Database.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();
        db.Database.HasPendingModelChanges().ShouldBeFalse();
        await AssertLegacyRowsAsync(db, id, ct);
    }

    [Test]
    [Arguments("debt")]
    [Arguments("park")]
    public async Task Downgrade_refuses_recorded_snapshot_and_preserves_its_bytes(string kind, CancellationToken ct)
    {
        var schema = _historical!;
        await using var db = NewDb(schema);
        var id = await InsertLegacyRowsAsync(db, ct);
        var table = kind == "debt" ? "AgentTaskSyncDebts" : "AgentTaskParks";
        var migration = kind == "debt" ? Debt : Park;
        const string snapshot = "{\"episode\":\"must-survive-downgrade\",\"revision\":7}";
        await db.Database.ExecuteSqlRawAsync(
            $"UPDATE \"{table}\" SET \"WorkspaceSnapshotJson\" = {{0}} WHERE \"Id\" = {{1}}", [snapshot, id], ct);
        (await db.Database.SqlQueryRaw<string>(
            $"SELECT \"WorkspaceSnapshotJson\" AS \"Value\" FROM \"{table}\" WHERE \"Id\" = {{0}}", id)
            .SingleAsync(ct)).ShouldBe(snapshot, "recorded-data-reached-before-down");

        var refusal = await Should.ThrowAsync<PostgresException>(
            () => db.GetService<IMigrator>().MigrateAsync(Previous, ct), "down-must-not-silently-discard-recorded-episode");
        refusal.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        refusal.MessageText.ShouldBe("card0824_refonly_snapshot_downgrade_blocked");
        (await db.Database.GetAppliedMigrationsAsync(ct)).ShouldContain(migration, "refused-migration-stays-applied");
        (await db.Database.SqlQueryRaw<string>(
            $"SELECT \"WorkspaceSnapshotJson\" AS \"Value\" FROM \"{table}\" WHERE \"Id\" = {{0}}", id)
            .SingleAsync(ct)).ShouldBe(snapshot, "failed-down-preserves-exact-snapshot-bytes");
        await AssertLegacyRowsAsync(db, id, ct);

        // Drain only this clone's recorded episode, then demonstrate the legitimate rollback path.
        await db.Database.ExecuteSqlRawAsync(
            $"UPDATE \"{table}\" SET \"WorkspaceSnapshotJson\" = NULL WHERE \"Id\" = {{0}}", [id], ct);
        await db.GetService<IMigrator>().MigrateAsync(Previous, ct);
        (await SnapshotColumnCountAsync(db, ct)).ShouldBe(0);
        await AssertLegacyRowsAsync(db, id, ct);
        await db.GetService<IMigrator>().MigrateAsync(Park, ct);
        await db.GetService<IMigrator>().MigrateAsync(cancellationToken: ct);
        (await db.Database.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();
        db.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    private static AppDbContext NewDb(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private static Task<int> SnapshotColumnCountAsync(AppDbContext db, CancellationToken ct) =>
        db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM information_schema.columns
            WHERE table_schema = 'public' AND column_name = 'WorkspaceSnapshotJson'
              AND table_name IN ('AgentTaskSyncDebts', 'AgentTaskParks') AND is_nullable = 'YES' AND data_type = 'text'
            """).SingleAsync(ct);

    private static async Task<Guid> InsertLegacyRowsAsync(AppDbContext db, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var now = new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AgentTaskSyncDebts" ("Id", "TaskId", "Attempt", "SettlementEventId", "State", "ReasonCode",
                "Attempts", "Revision", "CreatedAt", "UpdatedAt")
            VALUES ({id}, {id}, {1}, {Guid.NewGuid()}, {0}, {"legacy-debt"}, {0}, {0L}, {now}, {now})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AgentTaskParks" ("Id", "TaskId", "Attempt", "BlockEventId", "TaskConcurrencyToken", "Workspace",
                "BlockedAt", "State", "ReasonCode", "LegacyDiscovery", "Revision", "CreatedAt", "UpdatedAt", "SyncState", "SyncAttempts")
            VALUES ({id}, {id}, {1}, {Guid.NewGuid()}, {Guid.NewGuid()}, {1}, {now}, {0}, {"legacy-park"}, {false}, {0L}, {now}, {now}, {0}, {0})
            """, ct);
        return id;
    }

    private static async Task AssertLegacyRowsAsync(AppDbContext db, Guid id, CancellationToken ct)
    {
        // Previous-binary projections deliberately omit the additive fields, including after Down.
        (await db.Database.SqlQueryRaw<string>(
            "SELECT \"ReasonCode\" AS \"Value\" FROM \"AgentTaskSyncDebts\" WHERE \"Id\" = {0}", id).SingleAsync(ct))
            .ShouldBe("legacy-debt");
        (await db.Database.SqlQueryRaw<string>(
            "SELECT \"ReasonCode\" AS \"Value\" FROM \"AgentTaskParks\" WHERE \"Id\" = {0}", id).SingleAsync(ct))
            .ShouldBe("legacy-park");
    }
}
