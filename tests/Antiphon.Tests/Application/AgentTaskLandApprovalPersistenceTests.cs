using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class AgentTaskLandApprovalPersistenceTests
{
    private static readonly string Sha = new('a', 40);

    [Test]
    public async Task C488_ApprovalRoundTrip()
    {
        await using var store = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(store.ConnectionString);
        var taskId = Guid.NewGuid();
        var evidenceId = Guid.NewGuid();
        await using (var db = new AppDbContext(options))
        {
            db.AgentTasks.Add(new AgentTask { Id = taskId, RootTaskId = taskId, CreatedAt = DateTime.UtcNow,
                Workspace = WorkspaceMode.Worktree, Status = AgentTaskStatus.Succeeded, WorktreeBranch = "feat/x",
                RepoPath = "C:/repo", WorktreePath = "C:/tree" });
            await db.SaveChangesAsync();
            db.AgentTaskLandRequests.Add(new AgentTaskLandRequest
            {
                Id = Guid.NewGuid(), TaskId = taskId, RequestedAt = DateTime.UtcNow,
                LastEvaluatedAt = DateTime.UtcNow, LastProgressAt = DateTime.UtcNow,
                SchemaVersion = 2, ExpectedSourceSha = Sha, ReviewEvidenceId = evidenceId,
                ApprovalKind = LandApprovalKind.ReviewEvidence, ApprovedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using var observer = new AppDbContext(options);
        var stored = await observer.AgentTaskLandRequests.SingleAsync(r => r.TaskId == taskId);
        stored.ExpectedSourceSha.ShouldBe(Sha);
        stored.ReviewEvidenceId.ShouldBe(evidenceId);
        stored.SchemaVersion.ShouldBe(2);
    }

    [Test]
    public async Task C488_ExpectedShaPersists()
    {
        await C488_ApprovalRoundTrip();
    }

    [Test]
    public async Task C488_MigrationDoesNotInventApproval()
    {
        var name = "test_c488_upgrade_" + Guid.NewGuid().ToString("N");
        await using (var maintenance = new NpgsqlConnection(TestDbFixture.MaintenanceConnectionString))
        {
            await maintenance.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", maintenance);
            await create.ExecuteNonQueryAsync();
        }
        var connection = new NpgsqlConnectionStringBuilder(TestDbFixture.ConnectionString) { Database = name }.ConnectionString;
        await using var owned = new IsolatedTestSchema(name, connection);
        var options = TestDbFixture.CreateDbContextOptions(connection);
        await using var db = new AppDbContext(options);
        var migrations = db.Database.GetMigrations().ToArray();
        var freshness = Array.FindIndex(migrations, m => m.EndsWith("_AddLandSourceFreshnessApproval", StringComparison.Ordinal));
        freshness.ShouldBeGreaterThan(0);
        await db.GetService<IMigrator>().MigrateAsync(migrations[freshness - 1]);
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AgentTasks" ("Id", "RootTaskId", "Depth", "Title", "Goal", "Kind", "Role",
                "ModelLevel", "Attempt", "MaxAttempts", "WorkingDirectory", "Ephemeral", "Status", "ReplyTo",
                "ConcurrencyToken", "CreatedAt", "TokensIn", "TokensOut", "CostUsd", "Workspace")
            VALUES ({id}, {id}, 0, 'legacy', 'legacy', 0, 0, 0, 0, 1, 'fixture', false,
                {(int)AgentTaskStatus.Succeeded}, 0, {Guid.NewGuid()}, {DateTime.UtcNow}, 0, 0, 0,
                {(int)WorkspaceMode.Worktree})
            """);
        var requestId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AgentTaskLandRequests" ("Id", "TaskId", "RequestedAt", "ReplyTo", "State", "IsPending",
                "Attempt", "LastEvaluatedAt", "LastProgressAt", "HighestProgress", "HoldEpisode", "ConcurrencyToken")
            VALUES ({requestId}, {id}, {DateTime.UtcNow}, 0, 0, TRUE, 0, {DateTime.UtcNow}, {DateTime.UtcNow}, -2, 0, {Guid.NewGuid()})
            """);
        await db.GetService<IMigrator>().MigrateAsync(migrations[^1]);
        await db.GetService<IMigrator>().MigrateAsync(migrations[^1]);
        await using var observer = new AppDbContext(options);
        var stored = await observer.AgentTaskLandRequests.SingleAsync(r => r.Id == requestId);
        stored.ExpectedSourceSha.ShouldBeNull();
        stored.ReviewEvidenceId.ShouldBeNull();
        stored.SchemaVersion.ShouldBe(1);
    }

    [Test]
    public async Task C488_MigrationLegacyAndV2Constraints()
    {
        await C488_V2DatabaseRequiresApprovalFields();
        await C488_V2DatabaseRequiresFullOids();
        await C488_V2DatabaseRequiresApprovalEquality();
    }

    [Test]
    public async Task C488_V2DatabaseRequiresApprovalFields()
    {
        await using var store = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(store.ConnectionString));
        var id = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask { Id = id, RootTaskId = id, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var error = await Should.ThrowAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AgentTaskLandings" ("Id", "TaskId", "SchemaVersion", "Active", "ConcurrencyToken",
                "Phase", "Publication", "Cleanup", "Mode", "CreatedAt", "UpdatedAt",
                "RepositoryPath", "CommonDirectory", "WorktreePath", "GitDirectory",
                "SourceFullRef", "OriginalSourceSha", "TargetFullRef", "TargetBeforeSha",
                "RemoteName", "DestinationFullRef", "RemoteFingerprint", "RecoveryRefPrefix",
                "SourcePinned", "TargetPinned", "PreparedPinned", "VerificationPassed",
                "TargetCheckoutRecorded", "DirectoryRemoved", "RegistrationRemoved", "BranchRemoved")
            VALUES ({Guid.NewGuid()}, {id}, 2, TRUE, {Guid.NewGuid()},
                0, 0, 0, 0, {DateTime.UtcNow}, {DateTime.UtcNow},
                'repo', 'common', 'tree', 'git',
                'refs/heads/source', {Sha}, 'refs/heads/master', {Sha},
                'origin', 'refs/heads/master', {new string('c', 64)}, 'refs/antiphon/land/x',
                FALSE, FALSE, FALSE, FALSE, FALSE, FALSE, FALSE, FALSE)
            """));
        error.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        error.ConstraintName.ShouldBe("CK_AgentTaskLandings_V2Approval");
    }

    [Test]
    public async Task C488_V2DatabaseRequiresFullOids()
    {
        await using var store = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(store.ConnectionString));
        var id = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask { Id = id, RootTaskId = id, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var error = await Should.ThrowAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AgentTaskLandRequests" ("Id", "TaskId", "RequestedAt", "ReplyTo", "State", "IsPending",
                "Attempt", "LastEvaluatedAt", "LastProgressAt", "HighestProgress", "HoldEpisode",
                "ConcurrencyToken", "SchemaVersion", "ExpectedSourceSha", "ResolvedSourceSha")
            VALUES ({Guid.NewGuid()}, {id}, {DateTime.UtcNow}, 0, 0, TRUE, 0, {DateTime.UtcNow}, {DateTime.UtcNow},
                -2, 0, {Guid.NewGuid()}, 2, {Sha}, 'deadbee')
            """));
        error.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        error.ConstraintName.ShouldBe("CK_AgentTaskLandRequests_OidShape");
    }

    [Test]
    public async Task C488_V2DatabaseRequiresApprovalEquality()
    {
        await using var store = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(store.ConnectionString));
        var id = Guid.NewGuid();
        var opId = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask { Id = id, RootTaskId = id, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var error = await Should.ThrowAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AgentTaskLandings" ("Id", "TaskId", "SchemaVersion", "Active", "ConcurrencyToken",
                "Phase", "Publication", "Cleanup", "Mode", "CreatedAt", "UpdatedAt",
                "RepositoryPath", "CommonDirectory", "WorktreePath", "GitDirectory",
                "SourceFullRef", "OriginalSourceSha", "ReviewedSourceSha", "ApprovalLandRequestId",
                "TargetFullRef", "TargetBeforeSha", "RemoteName", "DestinationFullRef", "RemoteFingerprint",
                "RecoveryRefPrefix", "SourcePinned", "TargetPinned", "PreparedPinned", "VerificationPassed",
                "TargetCheckoutRecorded", "DirectoryRemoved", "RegistrationRemoved", "BranchRemoved")
            VALUES ({opId}, {id}, 2, TRUE, {Guid.NewGuid()},
                0, 0, 0, 0, {DateTime.UtcNow}, {DateTime.UtcNow},
                'repo', 'common', 'tree', 'git',
                'refs/heads/source', {Sha}, {new string('b', 40)}, {Guid.NewGuid()},
                'refs/heads/master', {Sha}, 'origin', 'refs/heads/master', {new string('c', 64)},
                {"refs/antiphon/land/" + id.ToString("N") + "/" + opId.ToString("N")},
                FALSE, FALSE, FALSE, FALSE, FALSE, FALSE, FALSE, FALSE)
            """));
        error.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        error.ConstraintName.ShouldBe("CK_AgentTaskLandings_V2ApprovalEquality");
    }
}
