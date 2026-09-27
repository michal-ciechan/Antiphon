using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
public class AgentTaskWorktreeBaseMigrationTests
{
    [Test]
    public async Task T0442_V18_upgrade_preserves_historical_base_and_roundtrips_intent()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var historicalSha = new string('a', 40);
        var historical = TaskRow("historical");
        historical.WorktreeBranch = "feat/card-task-historical";
        historical.WorktreeBaseSha = historicalSha;
        historical.MergeTargetRef = "release";
        db.AgentTasks.Add(historical);
        await db.SaveChangesAsync();

        var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var currentIndex = Array.FindIndex(migrations,
            m => m.EndsWith("_AddCardWorktreeBaseContinuity", StringComparison.Ordinal));
        currentIndex.ShouldBeGreaterThan(0);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[currentIndex - 1]);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"AgentTasks\" SET \"Title\" = 'historical before upgrade' WHERE \"Id\" = {historical.Id}");
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();

        var loaded = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == historical.Id);
        loaded.Title.ShouldBe("historical before upgrade");
        loaded.WorktreeBaseSha.ShouldBe(historicalSha);
        loaded.MergeTargetRef.ShouldBe("release");
        loaded.RequestedWorktreeBaseMode.ShouldBe(RequestedWorktreeBaseMode.Auto);
        loaded.RequestedWorktreeBaseTaskId.ShouldBeNull();
        loaded.WorktreeBaseTaskId.ShouldBeNull();
        loaded.WorktreeBaseBranch.ShouldBeNull();
        loaded.WorktreeBasePreviewJson.ShouldBeNull();

        var observedAt = DateTime.UtcNow;
        var preview = "{\"observedAt\":\"" + observedAt.ToString("O") + "\","
            + "\"decision\":{\"sourceTaskId\":\"" + historical.Id.ToString("D") + "\"}}";
        var target = TaskRow("target");
        target.RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Target;
        target.WorktreeBasePreviewJson = preview;
        var task = TaskRow("task");
        task.RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Task;
        task.RequestedWorktreeBaseTaskId = historical.Id;
        task.WorktreeBaseTaskId = historical.Id;
        task.WorktreeBaseBranch = historical.WorktreeBranch;
        task.WorktreeBaseSha = historicalSha;
        task.WorktreeBasePreviewJson = preview;
        db.AgentTasks.AddRange(target, task);
        await db.SaveChangesAsync();

        await using var provider = AgentTaskDispatchBaseGuardTests.CreateProvider(
            schema.ConnectionString, Path.GetTempPath());
        await using var scope = provider.CreateAsyncScope();
        var detail = scope.ServiceProvider.GetRequiredService<AgentTaskService>();
        var targetDetail = await detail.GetAsync(target.Id, CancellationToken.None);
        targetDetail.RequestedWorktreeBaseMode.ShouldBe(RequestedWorktreeBaseMode.Target);
        targetDetail.RequestedWorktreeBaseTaskId.ShouldBeNull();
        targetDetail.WorktreeBasePreviewJson.ShouldBe(preview);
        var taskDetail = await detail.GetAsync(task.Id, CancellationToken.None);
        taskDetail.RequestedWorktreeBaseMode.ShouldBe(RequestedWorktreeBaseMode.Task);
        taskDetail.RequestedWorktreeBaseTaskId.ShouldBe(historical.Id);
        taskDetail.WorktreeBaseTaskId.ShouldBe(historical.Id);
        taskDetail.WorktreeBaseBranch.ShouldBe(historical.WorktreeBranch);
        taskDetail.WorktreeBaseSha.ShouldBe(historicalSha);
        taskDetail.WorktreeBasePreviewJson.ShouldBe(preview);
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }

    private static AgentTask TaskRow(string title)
    {
        var id = Guid.NewGuid();
        return new AgentTask
        {
            Id = id, RootTaskId = id, Title = title, Goal = title,
            Role = AgentTaskRole.Code, AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Low, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = Path.GetTempPath(), RepoPath = Path.GetTempPath(),
            Status = AgentTaskStatus.Succeeded, ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow.AddHours(-1), CompletedAt = DateTime.UtcNow,
        };
    }
}
