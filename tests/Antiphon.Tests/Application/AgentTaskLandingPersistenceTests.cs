using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
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
public sealed class AgentTaskLandingPersistenceTests
{
    [Test]
    public async Task C448_V31_ConcurrentOperationsAreFenced()
    {
        await using var store = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(store.ConnectionString);
        var id = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        await using (var seed = new AppDbContext(options))
        {
            seed.AgentTasks.Add(new AgentTask { Id = id, RootTaskId = id, CreatedAt = DateTime.UtcNow });
            await seed.SaveChangesAsync();
            seed.AgentTaskLandings.Add(new AgentTaskLanding
            {
                Id = operationId, TaskId = id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await seed.SaveChangesAsync();
        }
        await using (var duplicate = new AppDbContext(options))
        {
            duplicate.AgentTaskLandings.Add(new AgentTaskLanding
            {
                Id = Guid.NewGuid(), TaskId = id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            var error = await Should.ThrowAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
            ((PostgresException)error.InnerException!).SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        }
        await using var first = new AppDbContext(options);
        await using var stale = new AppDbContext(options);
        var a = await first.AgentTaskLandings.SingleAsync(x => x.Id == operationId);
        var b = await stale.AgentTaskLandings.SingleAsync(x => x.Id == operationId);
        a.ConcurrencyToken.ShouldBe(b.ConcurrencyToken);
        a.LastReason = "first";
        a.ConcurrencyToken = Guid.NewGuid();
        await first.SaveChangesAsync();
        b.LastReason = "stale";
        b.ConcurrencyToken = Guid.NewGuid();
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        await using var observer = new AppDbContext(options);
        (await observer.AgentTaskLandings.SingleAsync(x => x.Id == operationId)).LastReason.ShouldBe("first");
    }

    [Test]
    public async Task C448_V31_MigrationAndConcurrentOperations()
    {
        var name = "test_c448_upgrade_" + Guid.NewGuid().ToString("N");
        await using (var maintenance = new NpgsqlConnection(TestDbFixture.MaintenanceConnectionString))
        {
            await maintenance.OpenAsync();
            // Name is generated here from a fixed prefix and a hexadecimal GUID.
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", maintenance);
            await create.ExecuteNonQueryAsync();
        }
        var connection = new NpgsqlConnectionStringBuilder(TestDbFixture.ConnectionString) { Database = name }.ConnectionString;
        await using var owned = new IsolatedTestSchema(name, connection);
        var options = TestDbFixture.CreateDbContextOptions(connection);
        await using var db = new AppDbContext(options);
        var migrations = db.Database.GetMigrations().ToArray();
        migrations[^1].ShouldEndWith("_AddAgentTaskLandingEvidence");
        await db.GetService<IMigrator>().MigrateAsync(migrations[^2]);
        (await db.Database.GetAppliedMigrationsAsync()).ShouldNotContain(migrations[^1]);
        var intact = Guid.NewGuid();
        var missing = Guid.NewGuid();
        foreach (var id in new[] { intact, missing })
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "AgentTasks" ("Id", "RootTaskId", "Depth", "Title", "Goal", "Kind", "Role",
                    "ModelLevel", "Attempt", "MaxAttempts", "WorkingDirectory", "Ephemeral", "Status", "ReplyTo",
                    "ConcurrencyToken", "CreatedAt", "TokensIn", "TokensOut", "CostUsd", "Workspace", "WorktreeBranch", "WorktreePath")
                VALUES ({id}, {id}, 0, 'legacy', 'legacy fixture', 0, 0, 0, 0, 1, 'fixture', false,
                    {(int)AgentTaskStatus.Succeeded}, 0, {Guid.NewGuid()}, {DateTime.UtcNow}, 0, 0, 0,
                    {(int)WorkspaceMode.Worktree}, 'feat/legacy', {(id == intact ? "intact-fixture" : "missing-fixture")})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "AgentTaskEvents" ("Id", "AgentTaskId", "Type", "Detail", "At")
                VALUES ({Guid.NewGuid()}, {id}, {(int)AgentTaskEventType.Landed}, 'landed pushed worktree removed', {DateTime.UtcNow})
                """);
        }
        await db.GetService<IMigrator>().MigrateAsync(migrations[^1]);
        await using var observer = new AppDbContext(options);
        (await observer.AgentTasks.CountAsync(t => t.Id == intact || t.Id == missing)).ShouldBe(2);
        (await observer.AgentTasks.Where(t => t.Id == intact || t.Id == missing).Select(t => t.ActiveLandingId).ToListAsync())
            .ShouldAllBe(value => value == null);
        (await observer.AgentTaskLandings.CountAsync()).ShouldBe(0);
        (await observer.AgentTaskEvents.CountAsync(e => (e.AgentTaskId == intact || e.AgentTaskId == missing)
            && e.Type == AgentTaskEventType.Landed)).ShouldBe(2);
    }
}
