using Antiphon.Server.Application.Services;
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
[NotInParallel]
public class CapacityRecoveryCompatibilityTests
{
    [Test]
    public async Task Card0412_V22_migrate_forward_preserves_legacy_holds()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CapacityRecoveryTestSupport.CreateContext(schema);
        var holdId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            Title = "legacy-failed",
            Goal = "legacy",
            Status = AgentTaskStatus.Failed,
            AgentKind = AgentKind.ClaudeCode,
            CreatedAt = now.AddDays(-10),
            CompletedAt = now.AddDays(-9),
            FailureReason = "WallParked",
            ConcurrencyToken = Guid.NewGuid(),
        });
        db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = holdId,
            Kind = AgentKind.ClaudeCode,
            ModelAlias = "opus",
            Source = ModelAvailabilitySource.Manual,
            DisabledUntil = now.AddDays(3),
            HitAt = now.AddDays(-1),
            Reason = "manual timed",
            ClearedAt = null,
        });
        var clearedId = Guid.NewGuid();
        db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = clearedId,
            Kind = AgentKind.ClaudeCode,
            ModelAlias = "sonnet",
            Source = ModelAvailabilitySource.AutoDetected,
            DisabledUntil = now.AddHours(-2),
            HitAt = now.AddHours(-3),
            Reason = "already cleared",
            ClearedAt = now.AddHours(-1),
        });
        await db.SaveChangesAsync();

        var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var position = Array.FindIndex(migrations, m => m.EndsWith("_Card0412CapacityRecovery", StringComparison.Ordinal));
        position.ShouldBeGreaterThan(0);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[position - 1]);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"ModelAvailabilityHolds\" SET \"Reason\" = 'legacy write' WHERE \"Id\" = {holdId}");
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();

        var live = await db.ModelAvailabilityHolds.SingleAsync(h => h.Id == holdId);
        live.DisabledUntil.ShouldNotBeNull();
        live.Source.ShouldBe(ModelAvailabilitySource.Manual);
        live.ClearCause.ShouldBeNull();
        live.ReleasePendingAt.ShouldBeNull();
        live.Reason.ShouldBe("legacy write");
        var cleared = await db.ModelAvailabilityHolds.SingleAsync(h => h.Id == clearedId);
        cleared.ClearedAt.ShouldNotBeNull();
        cleared.ClearCause.ShouldBeNull();
        cleared.ReleasePendingAt.ShouldBeNull();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Failed);
        task.FailureReason.ShouldBe("WallParked");
    }

    [Test]
    public async Task Card0412_V23_legacy_available_for_stranded_current_session()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                DefinitionName = "fake",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = Path.GetTempPath(),
                Cols = 120,
                Rows = 30,
                CreatedAt = now.AddHours(-4),
                StartedAt = now.AddHours(-4),
                LastSeenAt = now,
            });
            db.ApiErrorRecoveries.Add(new ApiErrorRecovery
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                StubSequence = 2,
                Classification = ApiErrorClassification.Wall,
                DetectedAt = now.AddHours(-3),
                ResolvedAt = now.AddHours(-3),
                ResolvedReason = ApiErrorRecoveryReasons.WallParked,
            });
            await db.SaveChangesAsync();
        }

        var (service, _, _) = CapacityRecoveryTestSupport.CreateService(schema, batch: 100);
        var first = await service.ReconcileCompatibilityAsync(CancellationToken.None);
        first.ShouldBeGreaterThanOrEqualTo(1);
        var second = await service.ReconcileCompatibilityAsync(CancellationToken.None);
        await using var verify = CapacityRecoveryTestSupport.CreateContext(schema);
        var wait = await verify.CapacityRecoveryWaits.SingleAsync(
            w => w.ConsumerKey == $"session:{sessionId:N}");
        wait.CompatibilityResult.ShouldBe(CapacityRecoveryCompatibilityResult.LegacyAvailable);
        (await verify.CapacityRecoveryWaits.CountAsync(w => w.ConsumerKey == $"session:{sessionId:N}"))
            .ShouldBe(1);
        second.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task Card0412_V23_older_than_180_minutes_is_included()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                DefinitionName = "fake",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = Path.GetTempPath(),
                Cols = 120,
                Rows = 30,
                CreatedAt = now.AddHours(-6),
                StartedAt = now.AddHours(-6),
                LastSeenAt = now.AddHours(-5),
            });
            db.ApiErrorRecoveries.Add(new ApiErrorRecovery
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                StubSequence = 1,
                Classification = ApiErrorClassification.Wall,
                DetectedAt = now.AddHours(-5),
                ResolvedReason = ApiErrorRecoveryReasons.WallModelPaused,
                ResolvedAt = now.AddHours(-5),
            });
            await db.SaveChangesAsync();
        }

        var (service, _, _) = CapacityRecoveryTestSupport.CreateService(schema);
        await service.ReconcileCompatibilityAsync(CancellationToken.None);
        await using var verify = CapacityRecoveryTestSupport.CreateContext(schema);
        (await verify.CapacityRecoveryWaits.CountAsync(w => w.SessionId == sessionId)).ShouldBe(1);
    }

    [Test]
    public async Task Card0412_V23_reconcile_runs_compatibility()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                DefinitionName = "fake",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = Path.GetTempPath(),
                Cols = 120,
                Rows = 30,
                CreatedAt = now.AddHours(-4),
                StartedAt = now.AddHours(-4),
                LastSeenAt = now,
            });
            db.ApiErrorRecoveries.Add(new ApiErrorRecovery
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                StubSequence = 2,
                Classification = ApiErrorClassification.Wall,
                DetectedAt = now.AddHours(-3),
                ResolvedAt = now.AddHours(-3),
                ResolvedReason = ApiErrorRecoveryReasons.WallParked,
            });
            await db.SaveChangesAsync();
        }

        var (service, _, _) = CapacityRecoveryTestSupport.CreateService(schema, jitterSeconds: 0);
        await service.ReconcileAsync(CancellationToken.None);
        await using var verify = CapacityRecoveryTestSupport.CreateContext(schema);
        var wait = await verify.CapacityRecoveryWaits.SingleAsync(
            w => w.ConsumerKey == $"session:{sessionId:N}");
        wait.CompatibilityResult.ShouldBe(CapacityRecoveryCompatibilityResult.LegacyAvailable);
    }
}
