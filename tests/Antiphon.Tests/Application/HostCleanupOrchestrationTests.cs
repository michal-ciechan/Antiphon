using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class HostCleanupOrchestrationTests
{
    [Test]
    public void Cleanup_model_has_namespace_uniqueness_and_candidate_custody()
    {
        using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var run = db.Model.FindEntityType("Antiphon.Server.Domain.Entities.HostCleanupRun");
        run.ShouldNotBeNull("C826.cleanup-run-model");
        run.GetTableName().ShouldBe("HostCleanupRuns");
        run.GetIndexes().ShouldContain(index => index.IsUnique &&
            index.Properties.Select(p => p.Name).SequenceEqual(new[] { "StorageId", "LocalDate" }) &&
            index.GetFilter() == "\"Daily\" = TRUE", "C826.daily-namespace-uniqueness");
        var candidate = db.Model.FindEntityType("Antiphon.Server.Domain.Entities.HostCleanupCandidate");
        candidate.ShouldNotBeNull("C826.cleanup-candidate-model");
        candidate.GetForeignKeys().ShouldContain(key => key.PrincipalEntityType == run &&
            key.DeleteBehavior == DeleteBehavior.Restrict, "C826.protected-candidate-custody");
        foreach (var name in new[] { "HostCleanupHold", "HostMaintenanceActivity" })
            db.Model.FindEntityType("Antiphon.Server.Domain.Entities." + name)
                .ShouldNotBeNull("C826.cleanup-owned-table:" + name);
    }

    [Test]
    public async Task AddHostCleanup_upgrades_current_master_without_changing_existing_rows()
    {
        await using var schema = await MigrationTestDatabase.CreateReversibleTipAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var migrations = db.Database.GetMigrations().ToArray();
        var cleanup = Array.FindIndex(migrations, name => name.EndsWith("_AddHostCleanup"));
        cleanup.ShouldBeGreaterThan(0, "C826.cleanup-migration-present");
        migrations[cleanup - 1].ShouldBe("20261002101556_AddAgentTaskEventInputBody",
            "C826.cleanup-migration-after-current-master");
        var migrator = db.GetService<IMigrator>();
        var taskId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var at = DateTime.UtcNow;
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId, RootTaskId = taskId, Title = "cleanup upgrade sentinel",
            Goal = "preserve existing rows", Status = AgentTaskStatus.Succeeded,
            CreatedAt = at, WorkingDirectory = "/virtual/cleanup-upgrade",
        });
        await db.SaveChangesAsync();
        // Park has InputBody and IsLandTerminal, and not the later nullable load columns.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AgentTaskEvents" ("Id", "AgentTaskId", "Type", "Detail", "InputBody", "At", "IsLandTerminal")
            VALUES ({eventId}, {taskId}, {(int)AgentTaskEventType.Created}, {"existing event"}, {"existing input body"}, {at}, {false})
            """);
        db.ChangeTracker.Clear();
        // Host-load Downs throw. Seed at the last reversible migration, then rewind
        // to the populated predecessor before exercising the upgrade.
        await migrator.MigrateAsync(migrations[cleanup - 1]);
        await migrator.MigrateAsync(migrations[cleanup]);
        var sentinel = await db.AgentTaskEvents.AsNoTracking().Where(e => e.Id == eventId)
            .Select(e => new { e.InputBody, e.Detail }).SingleAsync();
        sentinel.InputBody.ShouldBe("existing input body", "C826.upgrade-preserves-input-body");
        sentinel.Detail.ShouldBe("existing event", "C826.upgrade-preserves-event");
        (await db.AgentTasks.AsNoTracking().Where(t => t.Id == taskId).Select(t => t.Title).SingleAsync())
            .ShouldBe("cleanup upgrade sentinel", "C826.upgrade-preserves-task");
        db.Database.HasPendingModelChanges().ShouldBeFalse("C826.cleanup-snapshot-agrees");
    }
}
