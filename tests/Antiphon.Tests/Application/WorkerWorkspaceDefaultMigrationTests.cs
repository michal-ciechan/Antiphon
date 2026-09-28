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
public sealed class WorkerWorkspaceDefaultMigrationTests
{
    [Test]
    public async Task C458_UpgradePreservesLegacyRowsAndAddsNullColumns()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (projectId, tasks) = await SeedAsync(schema);
        await RoundTripAsync(schema, projectId, tasks);
    }

    [Test]
    public async Task C458_DownThenUpPreservesLegacyWorkspace()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (projectId, tasks) = await SeedAsync(schema);
        await RoundTripAsync(schema, projectId, tasks);
    }

    private static async Task<(Guid ProjectId, (Guid Id, WorkspaceMode Mode)[] Tasks)> SeedAsync(IsolatedTestSchema schema)
    {
        await using var db = NewDb(schema);
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(), Name = $"C458-{Guid.NewGuid():N}", GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = "/tmp/c458", BaseBranch = "release", ConstitutionPath = "AGENTS.md",
            DefaultLaunchEnvJson = "{\"KEEP\":\"yes\"}", CommitOnSettle = false,
            GitHubIntegrationEnabled = true, NotificationsEnabled = true, CreatedAt = now, UpdatedAt = now,
        };
        db.Projects.Add(project);
        var specs = new[]
        {
            (WorkspaceMode.Shared, AgentTaskStatus.Queued, true),
            (WorkspaceMode.Worktree, AgentTaskStatus.Working, true),
            (WorkspaceMode.ReadOnly, AgentTaskStatus.Succeeded, false),
        };
        var tasks = specs.Select((spec, index) =>
        {
            var id = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id, RootTaskId = id, ProjectId = spec.Item3 ? project.Id : null,
                Title = $"legacy-{index}", Goal = "preserve", Workspace = spec.Item1,
                Status = spec.Item2, WorkingDirectory = "/tmp/c458", RepoPath = "/tmp/c458",
                WorktreeBranch = spec.Item1 == WorkspaceMode.Worktree ? "legacy-branch" : null,
                CreatedAt = now,
            });
            return (id, spec.Item1);
        }).ToArray();
        await db.SaveChangesAsync();
        return (project.Id, tasks);
    }

    private static async Task RoundTripAsync(IsolatedTestSchema schema, Guid projectId,
        (Guid Id, WorkspaceMode Mode)[] tasks)
    {
        await using (var db = NewDb(schema))
        {
            var migrations = db.Database.GetMigrations().ToArray();
            migrations.Last().ShouldContain("Card0458WorkerWorkspaceDefault");
            var migrator = db.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(migrations[^2]);
            var oldColumns = await ColumnsAsync(db);
            oldColumns.ShouldNotContain("DefaultWorkerWorkspace");
            oldColumns.ShouldNotContain("WorkspaceSource");
            await migrator.MigrateAsync(migrations[^1]);
        }

        await using var fresh = NewDb(schema);
        var columns = await ColumnsAsync(fresh);
        columns.ShouldContain("DefaultWorkerWorkspace");
        columns.ShouldContain("WorkspaceSource");
        var project = await fresh.Projects.SingleAsync(p => p.Id == projectId);
        project.DefaultWorkerWorkspace.ShouldBeNull();
        project.BaseBranch.ShouldBe("release");
        project.DefaultLaunchEnvJson.ShouldBe("{\"KEEP\":\"yes\"}");
        project.CommitOnSettle.ShouldBe(false);
        foreach (var (id, mode) in tasks)
        {
            var task = await fresh.AgentTasks.SingleAsync(t => t.Id == id);
            task.Workspace.ShouldBe(mode);
            task.WorkspaceSource.ShouldBeNull();
            task.RepoPath.ShouldBe("/tmp/c458");
            if (mode == WorkspaceMode.Worktree) task.WorktreeBranch.ShouldBe("legacy-branch");
        }
    }

    private static async Task<string[]> ColumnsAsync(AppDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT column_name FROM information_schema.columns WHERE table_name IN ('Projects', 'AgentTasks')";
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        return names.ToArray();
    }

    private static AppDbContext NewDb(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
}
