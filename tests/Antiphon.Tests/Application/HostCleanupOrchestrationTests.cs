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
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var migrations = db.Database.GetMigrations().ToArray();
        var cleanup = Array.FindIndex(migrations, name => name.EndsWith("_AddHostCleanup"));
        cleanup.ShouldBeGreaterThan(0, "C826.cleanup-migration-present");
        migrations[cleanup - 1].ShouldBe("20261002101556_AddAgentTaskEventInputBody",
            "C826.cleanup-migration-after-current-master");
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[cleanup - 1]);
        var taskId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId, RootTaskId = taskId, Title = "cleanup upgrade sentinel",
            Goal = "preserve existing rows", Status = AgentTaskStatus.Succeeded,
            CreatedAt = DateTime.UtcNow, WorkingDirectory = "/virtual/cleanup-upgrade",
        });
        // Use the current entity so the newly landed InputBody column is part of the sentinel.
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = eventId, AgentTaskId = taskId, Type = AgentTaskEventType.Created,
            Detail = "existing event", InputBody = "existing input body", At = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await migrator.MigrateAsync(migrations[cleanup]);
        var sentinel = await db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.Id == eventId);
        sentinel.InputBody.ShouldBe("existing input body", "C826.upgrade-preserves-input-body");
        sentinel.Detail.ShouldBe("existing event", "C826.upgrade-preserves-event");
        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId)).Title
            .ShouldBe("cleanup upgrade sentinel", "C826.upgrade-preserves-task");
        db.Database.HasPendingModelChanges().ShouldBeFalse("C826.cleanup-snapshot-agrees");
    }
}
