using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1082 V-13. The debt migration is applied and matches the model. A missing migration or
/// a dropped unique index fails this method.
/// </summary>
[Category("Integration")]
public sealed class SettlementSyncDebtSchemaTests
{
    [Test]
    public async Task C1082_SchemaMigrationMatchesModel()
    {
        ((int)AgentTaskSyncDebtState.Pending).ShouldBe(0);
        ((int)AgentTaskSyncDebtState.Ready).ShouldBe(1);
        ((int)AgentTaskSyncDebtState.Held).ShouldBe(2);
        ((int)AgentTaskSyncDebtState.Superseded).ShouldBe(3);

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var applied = await db.Database.GetAppliedMigrationsAsync();
        applied.ShouldContain(name => name.EndsWith("_AddAgentTaskSyncDebts", StringComparison.Ordinal));
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        db.Database.HasPendingModelChanges().ShouldBeFalse();

        var now = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
        var row = new AgentTaskSyncDebt
        {
            Id = Guid.NewGuid(),
            TaskId = Guid.NewGuid(),
            Attempt = 1,
            SettlementEventId = Guid.NewGuid(),
            RunnerId = "server2",
            WorktreePath = "/tmp/desktop-wt",
            RemoteWorktreePath = "/work/worktrees/task-abcdef01",
            RepositoryPath = "/tmp/repo",
            FullRef = "refs/heads/feat/card-task-abcdef01",
            BaselineSha = new string('a', 40),
            SourceSha = new string('b', 40),
            DesktopBeforeSha = new string('a', 40),
            EndpointFingerprint = new string('c', 64),
            State = AgentTaskSyncDebtState.Pending,
            ReasonCode = "runner_sync_lease_busy",
            Attempts = 0,
            NextAttemptAt = now,
            SourceReadyAt = null,
            ConfirmedSha = null,
            Revision = 4,
            CreatedAt = now,
            UpdatedAt = now.AddMinutes(1),
        };
        db.AgentTaskSyncDebts.Add(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var stored = await db.AgentTaskSyncDebts.SingleAsync(d => d.Id == row.Id);
        stored.TaskId.ShouldBe(row.TaskId);
        stored.Attempt.ShouldBe(1);
        stored.SettlementEventId.ShouldBe(row.SettlementEventId);
        stored.RunnerId.ShouldBe("server2");
        stored.WorktreePath.ShouldBe(row.WorktreePath);
        stored.RemoteWorktreePath.ShouldBe(row.RemoteWorktreePath);
        stored.RepositoryPath.ShouldBe("/tmp/repo");
        stored.FullRef.ShouldBe(row.FullRef);
        stored.BaselineSha.ShouldBe(row.BaselineSha);
        stored.SourceSha.ShouldBe(row.SourceSha);
        stored.DesktopBeforeSha.ShouldBe(row.DesktopBeforeSha);
        stored.EndpointFingerprint.ShouldBe(row.EndpointFingerprint);
        stored.State.ShouldBe(AgentTaskSyncDebtState.Pending);
        stored.ReasonCode.ShouldBe("runner_sync_lease_busy");
        stored.Attempts.ShouldBe(0);
        stored.NextAttemptAt.ShouldBe(now);
        stored.SourceReadyAt.ShouldBeNull();
        stored.ConfirmedSha.ShouldBeNull();
        stored.Revision.ShouldBe(4);
        stored.CreatedAt.ShouldBe(now);
        stored.UpdatedAt.ShouldBe(now.AddMinutes(1));
        (await db.Database.SqlQueryRaw<int>(
            """SELECT "State" AS "Value" FROM "AgentTaskSyncDebts" WHERE "Id" = {0}""", row.Id).SingleAsync())
            .ShouldBe(0);

        var nextAttempt = new AgentTaskSyncDebt
        {
            Id = Guid.NewGuid(),
            TaskId = row.TaskId,
            Attempt = 2,
            SettlementEventId = Guid.NewGuid(),
            State = AgentTaskSyncDebtState.Ready,
            ReasonCode = "runner_sync_lease_busy",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AgentTaskSyncDebts.Add(nextAttempt);
        await db.SaveChangesAsync();

        db.AgentTaskSyncDebts.Add(new AgentTaskSyncDebt
        {
            Id = Guid.NewGuid(),
            TaskId = row.TaskId,
            Attempt = 1,
            SettlementEventId = Guid.NewGuid(),
            State = AgentTaskSyncDebtState.Pending,
            ReasonCode = "runner_sync_lease_busy",
            CreatedAt = now,
            UpdatedAt = now,
        });
        var error = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = error.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.ShouldBe("IX_AgentTaskSyncDebts_TaskId_Attempt");
    }
}
