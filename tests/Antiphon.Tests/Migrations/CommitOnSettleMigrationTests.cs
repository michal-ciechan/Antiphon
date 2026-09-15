using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Migrations;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Shouldly;

namespace Antiphon.Tests.Migrations;

[Category("Integration")]
public class CommitOnSettleMigrationTests
{
    [Test]
    public void AddCommitOnSettlePolicy_is_additive()
    {
        var operations = new AddCommitOnSettlePolicy().UpOperations;
        operations.Count.ShouldBe(3);
        operations.ShouldAllBe(o => o is AddColumnOperation);
        var columns = operations.Cast<AddColumnOperation>().ToArray();
        columns.ShouldAllBe(c => c.IsNullable);
        columns.Select(c => c.Table + "." + c.Name).Order().ShouldBe(new[] {
            "AgentTasks.CommitBaselineSha", "AgentTasks.CommitOnSettle", "Projects.CommitOnSettle" });
    }

    [Test]
    public async Task Existing_rows_resolve_to_inherit()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var id = Guid.NewGuid();
        var template = new Antiphon.Server.Domain.Entities.AgentTask { Id = Guid.NewGuid(), Title = "legacy", Goal = "legacy", WorkingDirectory = "legacy", CreatedAt = DateTime.UtcNow };
        template.RootTaskId = template.Id;
        db.AgentTasks.Add(template); await db.SaveChangesAsync();
        // Copy the pre-card column set with raw SQL. No new policy column is supplied.
        var columns = db.Model.FindEntityType(typeof(Antiphon.Server.Domain.Entities.AgentTask))!.GetProperties()
            .Where(p => p.Name is not ("CommitOnSettle" or "CommitBaselineSha")).Select(p => p.Name).ToArray();
        var target = string.Join(", ", columns.Select(c => "\"" + c + "\""));
        var source = string.Join(", ", columns.Select(c => c is "Id" or "RootTaskId" ? "{0}" : "\"" + c + "\""));
        await db.Database.ExecuteSqlRawAsync("INSERT INTO \"AgentTasks\" (" + target + ") SELECT " + source + " FROM \"AgentTasks\" WHERE \"Id\" = {1}", id, template.Id);
        var task = await db.AgentTasks.SingleAsync(t => t.Id == id);
        task.CommitOnSettle.ShouldBeNull(); task.CommitBaselineSha.ShouldBeNull();
        CommitOnSettlePolicyResolver.Resolve(task.CommitOnSettle, null, true).ShouldBe(EffectiveCommitOnSettle.Tier1);
    }
}
