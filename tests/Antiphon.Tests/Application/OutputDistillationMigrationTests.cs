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
[NotInParallel]
public class OutputDistillationMigrationTests
{
    [Test]
    public async Task Upgrade_preserves_historical_outcomes_and_nullable_deadlines()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var id = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask { Id = id, RootTaskId = id, Title = "legacy", Goal = "legacy",
            WorkingDirectory = Path.GetTempPath(), CreatedAt = DateTime.UtcNow, Result = "raw authoritative report" });
        for (var n = 0; n < 12; n++)
            db.OutputDistillations.Add(new OutputDistillationRecord { Id = Guid.NewGuid(), TaskId = id,
                Outcome = (DistillationOutcome)n, Mode = OutputDistillerMode.Shadow, CostUsd = 0.001m,
                CreatedAt = DateTime.UtcNow, FeedbackNote = "original" });
        await db.SaveChangesAsync();
        var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        migrations[^1].ShouldEndWith("AddDistillationDeadlines");
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[^2]);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"OutputDistillations\" SET \"FeedbackNote\" = 'legacy write' WHERE \"TaskId\" = {id}");
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        var rows = await db.OutputDistillations.Where(r => r.TaskId == id).OrderBy(r => r.Outcome).ToListAsync();
        rows.Select(r => (int)r.Outcome).ShouldBe(Enumerable.Range(0, 12));
        foreach (var row in rows)
        {
            row.FeedbackNote.ShouldBe("legacy write");
            row.CostUsd.ShouldBe(0.001m);
            row.DeadlineAt.ShouldBeNull();
            row.RequestedAt.ShouldBeNull();
            row.ExpiryPhase.ShouldBeNull();
        }
        var task = await db.AgentTasks.SingleAsync(t => t.Id == id);
        task.ExecutionDeadlineAt.ShouldBeNull();
        task.Result.ShouldBe("raw authoritative report");
    }
}
