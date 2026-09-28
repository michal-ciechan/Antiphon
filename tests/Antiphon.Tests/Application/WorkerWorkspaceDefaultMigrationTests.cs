using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using System.Text.Json;
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
        var projectId = Guid.NewGuid();
        var tasks = new[]
        {
            (Guid.NewGuid(), WorkspaceMode.Shared, AgentTaskStatus.Queued, true),
            (Guid.NewGuid(), WorkspaceMode.Worktree, AgentTaskStatus.Working, true),
            (Guid.NewGuid(), WorkspaceMode.ReadOnly, AgentTaskStatus.Succeeded, false),
        };
        await using (var db = NewDb(schema))
        {
            var migrations = db.Database.GetMigrations().ToArray();
            migrations.Last().ShouldContain("Card0458WorkerWorkspaceDefault");
            var migrator = db.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(migrations[^2]);
            var oldColumns = await ColumnsAsync(db);
            oldColumns.ShouldNotContain("DefaultWorkerWorkspace");
            oldColumns.ShouldNotContain("WorkspaceSource");

            var now = DateTime.UtcNow;
            const string launchEnv = "{\"KEEP\":\"yes\"}";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Projects" ("Id", "Name", "GitRepositoryUrl", "LocalRepositoryPath",
                    "BaseBranch", "ConstitutionPath", "GitHubIntegrationEnabled", "NotificationsEnabled",
                    "DefaultLaunchEnvJson", "CommitOnSettle", "CreatedAt", "UpdatedAt")
                VALUES ({projectId}, {"C458-legacy-" + projectId.ToString("N")}, {"https://example.test/repo.git"},
                    {"/tmp/c458"}, {"release"}, {"AGENTS.md"}, {true}, {true},
                    {launchEnv}::jsonb, {false}, {now}, {now})
                """);
            for (var index = 0; index < tasks.Length; index++)
            {
                var (id, mode, status, linked) = tasks[index];
                var branch = mode == WorkspaceMode.Worktree ? "legacy-branch" : null;
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "AgentTasks" ("Id", "RootTaskId", "ProjectId", "Depth", "Title", "Goal",
                        "Kind", "Role", "ModelLevel", "Attempt", "MaxAttempts", "Workspace",
                        "WorkingDirectory", "RepoPath", "WorktreeBranch", "Ephemeral", "Status", "ReplyTo",
                        "ConcurrencyToken", "CreatedAt", "TokensIn", "TokensOut", "CostUsd")
                    VALUES ({id}, {id}, CASE WHEN {linked} THEN {projectId} ELSE NULL END, {0},
                        {"legacy-" + index}, {"preserve"}, {0}, {0}, {0}, {1}, {2}, {(int)mode},
                        {"/tmp/c458"}, {"/tmp/c458"}, {branch},
                        {false}, {(int)status}, {0}, {Guid.NewGuid()}, {now}, {0L}, {0L}, {0m})
                    """);
            }
            await migrator.MigrateAsync(migrations[^1]);
        }

        await using var fresh = NewDb(schema);
        var columns = await ColumnsAsync(fresh);
        columns.ShouldContain("DefaultWorkerWorkspace");
        columns.ShouldContain("WorkspaceSource");
        await fresh.Database.OpenConnectionAsync();
        await using (var command = fresh.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = $"""
                SELECT "GitRepositoryUrl", "LocalRepositoryPath", "BaseBranch", "ConstitutionPath",
                    "GitHubIntegrationEnabled", "NotificationsEnabled", "DefaultLaunchEnvJson"::text,
                    "CommitOnSettle", "DefaultWorkerWorkspace"
                FROM "Projects" WHERE "Id" = '{projectId}'
                """;
            await using var reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).ShouldBeTrue();
            reader.GetString(0).ShouldBe("https://example.test/repo.git");
            reader.GetString(1).ShouldBe("/tmp/c458");
            reader.GetString(2).ShouldBe("release");
            reader.GetString(3).ShouldBe("AGENTS.md");
            reader.GetBoolean(4).ShouldBeTrue();
            reader.GetBoolean(5).ShouldBeTrue();
            using (var env = JsonDocument.Parse(reader.GetString(6)))
                env.RootElement.GetProperty("KEEP").GetString().ShouldBe("yes");
            reader.GetBoolean(7).ShouldBeFalse();
            (await reader.IsDBNullAsync(8)).ShouldBeTrue();
            (await reader.ReadAsync()).ShouldBeFalse();
        }
        foreach (var (id, mode, status, linked) in tasks)
        {
            await using var command = fresh.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"""
                SELECT "ProjectId", "Workspace", "Status", "WorkingDirectory", "RepoPath",
                    "WorktreeBranch", "WorkspaceSource", "Title", "Goal"
                FROM "AgentTasks" WHERE "Id" = '{id}'
                """;
            await using var reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).ShouldBeTrue();
            if (linked) reader.GetGuid(0).ShouldBe(projectId);
            else (await reader.IsDBNullAsync(0)).ShouldBeTrue();
            reader.GetInt32(1).ShouldBe((int)mode);
            reader.GetInt32(2).ShouldBe((int)status);
            reader.GetString(3).ShouldBe("/tmp/c458");
            reader.GetString(4).ShouldBe("/tmp/c458");
            if (mode == WorkspaceMode.Worktree) reader.GetString(5).ShouldBe("legacy-branch");
            else (await reader.IsDBNullAsync(5)).ShouldBeTrue();
            (await reader.IsDBNullAsync(6)).ShouldBeTrue();
            reader.GetString(7).ShouldStartWith("legacy-");
            reader.GetString(8).ShouldBe("preserve");
            (await reader.ReadAsync()).ShouldBeFalse();
        }
        (await fresh.Projects.AsNoTracking().SingleAsync(p => p.Id == projectId))
            .DefaultWorkerWorkspace.ShouldBeNull();
        foreach (var (id, mode, _, _) in tasks)
        {
            var task = await fresh.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
            task.Workspace.ShouldBe(mode);
            task.WorkspaceSource.ShouldBeNull();
        }
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
        using (var env = JsonDocument.Parse(project.DefaultLaunchEnvJson))
            env.RootElement.GetProperty("KEEP").GetString().ShouldBe("yes");
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
