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
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AgentTasks" ("Id", "RootTaskId", "Depth", "Title", "Goal", "Kind", "Role", "ModelLevel", "WorkingDirectory", "Status", "ReplyTo", "MaxAttempts", "CreatedAt", "ConcurrencyToken")
            VALUES ({id}, {id}, 0, 'legacy', 'legacy', 0, 0, 0, 'legacy', 0, 0, 2, {DateTime.UtcNow}, {Guid.NewGuid()})
            """);
        var task = await db.AgentTasks.SingleAsync(t => t.Id == id);
        task.CommitOnSettle.ShouldBeNull(); task.CommitBaselineSha.ShouldBeNull();
        CommitOnSettlePolicyResolver.Resolve(task.CommitOnSettle, null, true).ShouldBe(EffectiveCommitOnSettle.Tier1);
    }
}
