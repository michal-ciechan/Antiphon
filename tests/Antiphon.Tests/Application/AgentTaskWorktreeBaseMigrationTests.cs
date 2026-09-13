using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class AgentTaskWorktreeBaseMigrationTests
{
    [Test]
    public async Task T0442_V18()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var migrations = db.Database.GetMigrations().ToList();
        migrations.ShouldContain("20260913184645_AddAgentTaskWorktreeBase");
        var id = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id, RootTaskId = id, Title = "legacy", Goal = "legacy",
            Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = "C:\\tmp", WorktreeBaseSha = new string('a', 40),
            Status = AgentTaskStatus.Succeeded, ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        await using var reload = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var row = await reload.AgentTasks.SingleAsync(t => t.Id == id);
        row.WorktreeBaseMode.ShouldBe(AgentTaskWorktreeBaseMode.Auto);
        row.RequestedWorktreeBaseTaskId.ShouldBeNull();
        row.WorktreeBaseTaskId.ShouldBeNull();
        row.WorktreeBaseBranch.ShouldBeNull();
        row.WorktreeBasePreviewJson.ShouldBeNull();
        row.WorktreeBaseSha.ShouldBe(new string('a', 40));
        row.WorktreeBaseMode = AgentTaskWorktreeBaseMode.Task;
        row.RequestedWorktreeBaseTaskId = id;
        await reload.SaveChangesAsync();
        await using var again = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var persisted = await again.AgentTasks.SingleAsync(t => t.Id == id);
        persisted.WorktreeBaseMode.ShouldBe(AgentTaskWorktreeBaseMode.Task);
        persisted.RequestedWorktreeBaseTaskId.ShouldBe(id);
    }
}
