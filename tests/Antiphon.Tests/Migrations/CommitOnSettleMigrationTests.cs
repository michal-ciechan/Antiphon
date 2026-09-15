using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Migrations;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Migrations;

[Category("Unit")]
public sealed class CommitOnSettleMigrationTests
{
    [Test]
    public void AddCommitOnSettlePolicy_is_additive()
    {
        var operations = new AddCommitOnSettlePolicy().UpOperations;
        operations.Count.ShouldBe(3);
        operations.ShouldAllBe(o => o is AddColumnOperation);
        var columns = operations.Cast<AddColumnOperation>().ToArray();
        columns.ShouldContain(c => c.Table == "Projects" && c.Name == "CommitOnSettle" && c.IsNullable);
        columns.ShouldContain(c => c.Table == "AgentTasks" && c.Name == "CommitOnSettle" && c.IsNullable);
        columns.ShouldContain(c => c.Table == "AgentTasks" && c.Name == "CommitBaselineSha" && c.IsNullable);
    }

    [Test]
    public void AddCommitOnSettleReviewRepairs_is_additive()
    {
        var operations = new AddCommitOnSettleReviewRepairs().UpOperations;
        operations.Count.ShouldBe(3);
        operations.ShouldAllBe(o => o is AddColumnOperation);
        var columns = operations.Cast<AddColumnOperation>().ToArray();
        columns.ShouldContain(c => c.Table == "AgentTasks" && c.Name == "CommitBaselineUpstreamSha" && c.IsNullable);
        columns.ShouldContain(c => c.Table == "AgentTasks" && c.Name == "CompletionNoteBody" && c.IsNullable);
        columns.ShouldContain(c => c.Table == "AgentTasks" && c.Name == "CompletionNoteHeader" && c.IsNullable);
    }

    [Test]
    public async Task Existing_rows_resolve_to_inherit()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(isolated.ConnectionString));
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "legacy",
            Goal = "legacy",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Custom,
            ModelLevel = AgentModelLevel.High,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = isolated.ConnectionString,
            Status = AgentTaskStatus.Queued,
            CreatedAt = DateTime.UtcNow,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();

        await db.Entry(task).ReloadAsync();
        task.CommitOnSettle.ShouldBeNull();
        CommitOnSettlePolicyResolver.Resolve(task.CommitOnSettle, project: null, global: new DelegationSettings().CommitOnSettle)
            .ShouldBe(CommitOnSettleEffective.Tier1);
    }
}
