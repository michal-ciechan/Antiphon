using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0505 V-4. The migration adds empty tables and the seed is written by initialization.</summary>
[Category("Integration")]
public sealed class DispatchConcurrencyMigrationTests
{
    [Test]
    public async Task Upgrade_populated_database_preserves_task_rows()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var now = DateTime.UtcNow;
        var projectP = Guid.NewGuid();
        var projectQ = Guid.NewGuid();
        await using (var db = NewDb(schema.ConnectionString))
        {
            await RewindAsync(db);
            db.Projects.AddRange(Project(projectP, "P", now), Project(projectQ, "Q", now));
            await db.SaveChangesAsync();
            foreach (var projectId in new Guid?[] { projectP, projectQ, null })
            {
                foreach (var status in Enum.GetValues<AgentTaskStatus>())
                {
                    var id = Guid.NewGuid();
                    db.AgentTasks.Add(new AgentTask
                    {
                        Id = id,
                        RootTaskId = id,
                        Title = $"{projectId?.ToString() ?? "null"}-{status}",
                        Goal = "preserved",
                        Role = AgentTaskRole.Code,
                        Status = status,
                        ProjectId = projectId,
                        Workspace = WorkspaceMode.Shared,
                        WorkingDirectory = "/tmp/card-0505",
                        CreatedAt = now,
                    });
                }
            }

            var sessionId = Guid.NewGuid();
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                DefinitionName = "claude",
                AgentKind = AgentKind.ClaudeCode,
                SessionBackend = SessionBackend.PtyHost,
                Status = SessionStatus.Running,
                Cwd = "/tmp/card-0505",
                Cols = 80,
                Rows = 24,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            });
            db.HostBudgets.Add(new HostBudget
            {
                HostId = "desktop",
                MaxInFlight = 0,
                Reason = "frozen",
                UpdatedAt = now,
                Revision = 3,
            });
            db.RunnerRoutingSettings.Add(new RunnerRoutingSettings
            {
                Id = RunnerRoutingSettings.SingletonKey,
                Revision = 4,
                GlobalRunnerId = "server2",
                UpdatedAt = now,
                LastReason = "frozen",
                LastProvenance = "Human",
            });
            await db.SaveChangesAsync();
        }

        string[] beforeTasks;
        string[] beforeSessions;
        string beforeBudget;
        string beforeRouting;
        await using (var db = NewDb(schema.ConnectionString))
        {
            beforeTasks = await TaskSnapshotAsync(db);
            beforeSessions = await SessionSnapshotAsync(db);
            beforeBudget = await BudgetSnapshotAsync(db);
            beforeRouting = await RoutingSnapshotAsync(db);
            beforeTasks.Length.ShouldBe(Enum.GetValues<AgentTaskStatus>().Length * 3);
            beforeTasks.Count(row => row.Contains("|Queued|", StringComparison.Ordinal)).ShouldBe(3);
            await db.Database.MigrateAsync();
        }

        await using (var db = NewDb(schema.ConnectionString))
        {
            (await TaskSnapshotAsync(db)).ShouldBe(beforeTasks);
            (await SessionSnapshotAsync(db)).ShouldBe(beforeSessions);
            (await BudgetSnapshotAsync(db)).ShouldBe(beforeBudget);
            (await RoutingSnapshotAsync(db)).ShouldBe(beforeRouting);
            (await db.AgentTasks.CountAsync(task => task.Status == AgentTaskStatus.Queued)).ShouldBe(3);
            (await db.DispatchConcurrencySettings.CountAsync()).ShouldBe(0);
            (await db.DispatchConcurrencyRevisions.CountAsync()).ShouldBe(0);
        }

        await ExpectUniqueAsync(schema.ConnectionString, db =>
        {
            db.DispatchConcurrencySettings.Add(Settings("global", now));
            db.DispatchConcurrencySettings.Add(Settings("global", now));
        }, "IX_DispatchConcurrencySettings_ScopeKey");
        await ExpectUniqueAsync(schema.ConnectionString, db =>
        {
            var row = Settings("probe", now);
            db.DispatchConcurrencySettings.Add(row);
            db.DispatchConcurrencyRevisions.Add(Revision(row.Id, now));
            db.DispatchConcurrencyRevisions.Add(Revision(row.Id, now));
        }, "IX_DispatchConcurrencyRevisions_SettingsId_Revision");

        await using (var db = NewDb(schema.ConnectionString))
        {
            (await db.DispatchConcurrencySettings.CountAsync()).ShouldBe(0, "bound-seed");
            (await db.DispatchConcurrencyRevisions.CountAsync()).ShouldBe(0, "bound-seed");
            var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DispatchConcurrencyTestHost.SeedInstant);
            var read = await new DispatchConcurrencySettingsService(
                db, Options.Create(DispatchConcurrencyTestHost.BoundSettings()), clock, new MockEventBus())
                .GetGlobalAsync(CancellationToken.None);
            read.Seed.MaxParallel.ShouldBe(9, "bound-seed");
            read.Seed.Roles.Single(role => role.Role == "Code").MaxParallel.ShouldBe(5, "bound-seed");
            read.Provenance.ShouldBe("Migration", "bound-seed");
            read.Reason.ShouldBe(DispatchConcurrencySettingsService.MigrationReason, "bound-seed");
            (await db.DispatchConcurrencySettings.CountAsync()).ShouldBe(1, "bound-seed");
            (await db.DispatchConcurrencyRevisions.CountAsync()).ShouldBe(1, "bound-seed");
        }
    }

    [Test]
    public async Task Initialize_from_nondefault_config_then_restart()
    {
        await using var shop = await DispatchConcurrencyShop.Open();
        await using (var db = shop.Db())
        {
            await RewindAsync(db);
            await db.Database.MigrateAsync();
        }

        var imported = await shop.ReadGlobalAsync();
        imported.Seed.MaxParallel.ShouldBe(9, "restart-durable");
        imported.Seed.Roles.Single(role => role.Role == "Code").MaxParallel.ShouldBe(5, "restart-durable");
        imported.Revision.ShouldBe(1, "restart-durable");

        var updated = await shop.PutGlobalAsync(1, """{"mode":"SeparateQueues","maxParallel":8}""", "separate");
        updated.Revision.ShouldBe(2, "restart-durable");
        updated.Effective.Mode.ShouldBe("SeparateQueues", "restart-durable");
        var project = await shop.PutProjectAsync(
            shop.ProjectP, 0, 2, """{"maxQueued":1,"roles":{"Code":{"maxParallel":2}}}""", "project");
        project.Revision.ShouldBe(1, "restart-durable");

        var divergent = new DelegationSettings { MaxOpenTasks = 2 };
        divergent.RolePolicy["Code"].RecommendedInFlight = 1;
        await using (var db = shop.Db())
        {
            var service = shop.Service(db, divergent);
            var again = await service.GetGlobalAsync(CancellationToken.None);
            again.Revision.ShouldBe(2, "restart-durable");
            again.Seed.MaxParallel.ShouldBe(9, "restart-durable");
            again.Seed.Roles.Single(role => role.Role == "Code").MaxParallel.ShouldBe(5, "restart-durable");
            again.Effective.Mode.ShouldBe("SeparateQueues", "restart-durable");
            again.Effective.MaxParallel.ShouldBe(8, "restart-durable");
            var still = await service.GetProjectAsync(shop.ProjectP, CancellationToken.None);
            still.Revision.ShouldBe(1, "restart-durable");
            still.GlobalRevision.ShouldBe(2, "restart-durable");
            still.Effective.MaxQueued.ShouldBe(1, "restart-durable");
            still.Effective.Roles.Single(role => role.Role == "Code").MaxParallel.ShouldBe(2, "restart-durable");
            still.Effective.Roles.Single(role => role.Role == "Code").MaxParallelSource.ShouldBe("project", "restart-durable");
        }

        var clearedProject = await shop.PutProjectAsync(shop.ProjectP, 1, 2, "{}", "clear project");
        clearedProject.Revision.ShouldBe(2, "clear-inherits");
        var cleared = await shop.PutGlobalAsync(2, "{}", "clear global");
        cleared.Revision.ShouldBe(3, "clear-inherits");
        cleared.Seed.MaxParallel.ShouldBe(9, "clear-inherits");
        cleared.Effective.MaxParallel.ShouldBe(9, "clear-inherits");
        cleared.Effective.Roles.Single(role => role.Role == "Code").MaxParallel.ShouldBe(5, "clear-inherits");
        var inherited = await shop.ReadProjectAsync(shop.ProjectP);
        inherited.Revision.ShouldBe(2, "clear-inherits");
        inherited.Effective.MaxParallel.ShouldBe(9, "clear-inherits");
        inherited.Effective.Roles.Single(role => role.Role == "Code").MaxParallel.ShouldBe(5, "clear-inherits");
        inherited.Effective.Roles.Single(role => role.Role == "Code").MaxParallelSource.ShouldBe("default", "clear-inherits");
    }

    [Test]
    public async Task Interrupted_initialization_rolls_back_seed_and_history()
    {
        await using var shop = await DispatchConcurrencyShop.Open();
        await using (var db = shop.Db(new FailFirstCommitInterceptor()))
        {
            var thrown = await Should.ThrowAsync<InvalidOperationException>(
                () => shop.Service(db).EnsureInitializedAsync(CancellationToken.None));
            thrown.Message.ShouldBe("interrupted before import commit", "atomic-import");
        }

        await using (var db = shop.Db())
        {
            (await db.DispatchConcurrencySettings.CountAsync()).ShouldBe(0, "atomic-import");
            (await db.DispatchConcurrencyRevisions.CountAsync()).ShouldBe(0, "atomic-import");
        }

        shop.Bus.PublishedEvents.ShouldBeEmpty("atomic-import");

        var saved = await shop.ReadGlobalAsync();
        saved.Revision.ShouldBe(1, "atomic-import");
        saved.Seed.MaxParallel.ShouldBe(9, "atomic-import");
        saved.Seed.Roles.Single(role => role.Role == "Code").MaxParallel.ShouldBe(5, "atomic-import");
        saved.Provenance.ShouldBe("Migration", "atomic-import");
        await using var read = shop.Db();
        (await read.DispatchConcurrencySettings.CountAsync()).ShouldBe(1, "atomic-import");
        (await read.DispatchConcurrencyRevisions.CountAsync()).ShouldBe(1, "atomic-import");
        shop.Bus.PublishedEvents.Count.ShouldBe(1, "atomic-import");
        shop.Bus.PublishedEvents.Single().EventName.ShouldBe("DispatchConcurrencyChanged", "atomic-import");
    }

    private static AppDbContext NewDb(string connectionString) =>
        new(TestDbFixture.CreateDbContextOptions(connectionString));

    private static async Task RewindAsync(AppDbContext db)
    {
        var migrations = db.Database.GetMigrations().ToArray();
        var index = Array.FindIndex(migrations, name => name.EndsWith("_AddDispatchConcurrencySettings", StringComparison.Ordinal));
        index.ShouldBeGreaterThan(0);
        await db.GetService<IMigrator>().MigrateAsync(migrations[index - 1]);
    }

    private static async Task ExpectUniqueAsync(string connectionString, Action<AppDbContext> arrange, string constraint)
    {
        await using var db = NewDb(connectionString);
        await using var tx = await db.Database.BeginTransactionAsync();
        arrange(db);
        var exception = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = exception.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.ShouldBe(constraint);
        await tx.RollbackAsync();
    }

    private static DispatchConcurrencySettings Settings(string scope, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        ScopeKey = scope,
        OverridesJson = "{}",
        Revision = 1,
        UpdatedAt = now,
        LastReason = "probe",
        LastProvenance = "Human",
    };

    private static DispatchConcurrencyRevision Revision(Guid settingsId, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        SettingsId = settingsId,
        Revision = 1,
        SnapshotJson = "{}",
        CreatedAt = now,
        Reason = "probe",
        Provenance = "Human",
    };

    private static Project Project(Guid id, string name, DateTime now) => new()
    {
        Id = id,
        Name = name,
        GitRepositoryUrl = $"https://example.test/{name}.git",
        CreatedAt = now,
        UpdatedAt = now,
    };

    private static async Task<string[]> TaskSnapshotAsync(AppDbContext db)
    {
        var rows = await db.AgentTasks.AsNoTracking().OrderBy(task => task.Id)
            .Select(task => new { task.Id, task.Status, task.ProjectId, task.Role, task.Title })
            .ToListAsync();
        return rows.Select(task => $"{task.Id:D}|{task.Status}|{task.ProjectId}|{task.Role}|{task.Title}").ToArray();
    }

    private static async Task<string[]> SessionSnapshotAsync(AppDbContext db)
    {
        var rows = await db.AgentSessions.AsNoTracking().OrderBy(session => session.Id)
            .Select(session => new { session.Id, session.Status, session.DefinitionName, session.Cwd })
            .ToListAsync();
        return rows.Select(session => $"{session.Id:D}|{session.Status}|{session.DefinitionName}|{session.Cwd}").ToArray();
    }

    private static async Task<string> BudgetSnapshotAsync(AppDbContext db)
    {
        var budget = await db.HostBudgets.AsNoTracking().SingleAsync();
        return $"{budget.HostId}|{budget.MaxInFlight}|{budget.Reason}|{budget.Revision}";
    }

    private static async Task<string> RoutingSnapshotAsync(AppDbContext db)
    {
        var routing = await db.RunnerRoutingSettings.AsNoTracking().SingleAsync();
        return $"{routing.Id}|{routing.Revision}|{routing.GlobalRunnerId}|{routing.LastReason}";
    }
}
