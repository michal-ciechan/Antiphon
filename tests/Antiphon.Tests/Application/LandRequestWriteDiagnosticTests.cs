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

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandRequestWriteDiagnosticTests
{
    [Test]
    public async Task C883_WriterStampComesFromCommittedToken()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var accepted = await h.RequestAsync();
        await using (var db = h.CreateContext())
        {
            var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == accepted.RequestId);
            row.LastWriterOperation.ShouldBe("admission", "D.AdmissionStamped");
            row.LastWriterToken.ShouldBe(row.ConcurrencyToken, "D.AdmissionTokenLinked");
            row.LastWriterAt.ShouldNotBeNull("D.AdmissionTimeStamped");
        }
        await using (var db = h.CreateContext())
        {
            var monitor = new AgentTaskLandMonitorService(db, h.Clock,
                Options.Create(new DelegationSettings()), h.Events);
            await monitor.SweepAsync(CancellationToken.None);
        }
        await using (var db = h.CreateContext())
        {
            var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == accepted.RequestId);
            row.LastWriterOperation.ShouldBe("monitor-sweep", "D.MonitorStamped");
            row.LastWriterToken.ShouldBe(row.ConcurrencyToken, "D.MonitorTokenLinked");
        }
    }

    [Test]
    public async Task C883_ConflictNamesActualEntityAndTokens()
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (_, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var accepted = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        fixture.Interceptor.RequestId = accepted.RequestId;
        var failure = await Should.ThrowAsync<DbUpdateConcurrencyException>(() => h.RunQueuedAsync());
        await h.FailAsync(failure);
        await using var db = h.CreateContext();
        var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == accepted.RequestId);
        row.TerminalFailureCode.ShouldBe("landing_concurrency_conflict", "D.ConcurrencyCodePersisted");
        var terminal = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.Id == row.TerminalEventId);
        terminal.Detail.ShouldContain("entity=AgentTaskLandRequest", Case.Sensitive, "D.EntryEntityNamed");
        terminal.Detail.ShouldContain($"row={accepted.RequestId:N}", Case.Sensitive, "D.EntryRowNamed");
        terminal.Detail.ShouldContain("originalToken=", Case.Sensitive, "D.OriginalTokenNamed");
        terminal.Detail.ShouldContain("attemptedToken=", Case.Sensitive, "D.AttemptedTokenNamed");
        terminal.Detail.ShouldContain("observedDatabaseWriter=", Case.Sensitive, "D.ObservedWriterNamed");
        terminal.Detail.ShouldNotContain("fixture-request-save-conflict", Case.Sensitive, "D.RawExceptionHidden");
    }

    [Test]
    public async Task C883_MigrationPreservesLegacyRows()
    {
        var name = "test_c883_upgrade_" + Guid.NewGuid().ToString("N");
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
        var position = Array.FindIndex(migrations,
            m => m.EndsWith("_AddLandRequestWriterProvenance", StringComparison.Ordinal));
        position.ShouldBeGreaterThan(0, "D.MigrationExists");
        migrations[position - 1].EndsWith("_AddReviewSourceClean", StringComparison.Ordinal)
            .ShouldBeTrue("D.ActualImmediatePredecessor");
        await db.GetService<IMigrator>().MigrateAsync(migrations[position - 1]);
        var taskId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId, RootTaskId = taskId, Title = "legacy", Goal = "legacy",
            Role = AgentTaskRole.Code, Kind = AgentTaskKind.Worker, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = Path.GetTempPath(), RepoPath = Path.GetTempPath(),
            Status = AgentTaskStatus.Succeeded, ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = now, CompletedAt = now,
        });
        await db.SaveChangesAsync();
        var requestId = Guid.NewGuid();
        var token = Guid.NewGuid();
        var expected = new string('a', 40);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AgentTaskLandRequests" ("Id", "TaskId", "RequestedAt", "ReplyTo", "State", "IsPending",
                "Attempt", "LastEvaluatedAt", "LastProgressAt", "HighestProgress", "HoldEpisode", "ConcurrencyToken",
                "SchemaVersion", "ExpectedSourceSha", "ApprovalKind", "ApprovedAt", "RecoveryLocalBeforeSha")
            VALUES ({requestId}, {taskId}, {now}, 0, 0, FALSE, 1, {now}, {now}, -2, 0, {token},
                2, {expected}, {(int)LandApprovalKind.ExplicitCaller}, {now}, {expected})
            """);
        await db.GetService<IMigrator>().MigrateAsync(migrations[position]);
        await using var observer = new AppDbContext(options);
        var saved = await observer.AgentTaskLandRequests.SingleAsync(r => r.Id == requestId);
        saved.ConcurrencyToken.ShouldBe(token, "D.LegacyTokenUnchanged");
        saved.ExpectedSourceSha.ShouldBe(expected, "D.LegacyApprovalUnchanged");
        saved.RecoveryLocalBeforeSha.ShouldBe(expected, "D.LegacyProgressUnchanged");
        saved.LastWriterOperation.ShouldBeNull("D.LegacyWriterNull");
        saved.LastWriterToken.ShouldBeNull("D.LegacyWriterTokenNull");
        saved.LastWriterAt.ShouldBeNull("D.LegacyWriterAtNull");
        saved.RecoveryWitnessRequestId.ShouldBeNull("D.LegacyWitnessNull");
        var witness = Guid.NewGuid();
        saved.RecoveryWitnessRequestId = witness;
        saved.ConcurrencyToken = Guid.NewGuid();
        saved.LastWriterToken = saved.ConcurrencyToken;
        saved.LastWriterOperation = "source-checkpoint";
        saved.LastWriterAt = now;
        await observer.SaveChangesAsync();
        observer.ChangeTracker.Clear();
        saved = await observer.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == requestId);
        saved.RecoveryWitnessRequestId.ShouldBe(witness, "D.NewWitnessRoundTrips");
        saved.LastWriterToken.ShouldBe(saved.ConcurrencyToken, "D.NewWriterTokenRoundTrips");
    }
}
