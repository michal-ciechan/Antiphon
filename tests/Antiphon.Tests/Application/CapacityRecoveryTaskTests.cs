using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class CapacityRecoveryTaskTests
{
    [Test]
    public async Task Card0412_V19_retained_wait_excluded_from_global_active_count()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var now = DateTime.UtcNow;
        for (var i = 0; i < 6; i++)
        {
            var id = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = $"claude-wait-{i}",
                Goal = "wait",
                Status = AgentTaskStatus.Working,
                AgentKind = AgentKind.ClaudeCode,
                CapacityWaitRetained = true,
                CapacityWaitId = Guid.NewGuid(),
                CreatedAt = now,
                ConcurrencyToken = Guid.NewGuid(),
            });
        }

        var grokId = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = grokId,
            RootTaskId = grokId,
            Title = "grok-ready",
            Goal = "go",
            Status = AgentTaskStatus.Queued,
            AgentKind = AgentKind.Grok,
            CreatedAt = now.AddMinutes(-1),
            ConcurrencyToken = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();

        var active = await db.AgentTasks
            .Where(AgentTaskRoles.NotSpecialist)
            .CountAsync(t => (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
                && !t.CapacityWaitRetained);
        active.ShouldBe(0);
    }

    [Test]
    public async Task Card0412_V19_retained_return_beats_older_queued()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var now = DateTime.UtcNow;
        Guid? firstCounted = null;
        for (var i = 0; i < 6; i++)
        {
            var id = Guid.NewGuid();
            firstCounted ??= id;
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = $"counted-{i}",
                Goal = "busy",
                Status = AgentTaskStatus.Working,
                AgentKind = AgentKind.ClaudeCode,
                CreatedAt = now.AddMinutes(-10),
                ConcurrencyToken = Guid.NewGuid(),
            });
        }

        var retainedId = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = retainedId,
            RootTaskId = retainedId,
            Title = "retained-ready",
            Goal = "return",
            Status = AgentTaskStatus.Working,
            AgentKind = AgentKind.ClaudeCode,
            CapacityWaitRetained = true,
            CapacityWaitId = Guid.NewGuid(),
            CreatedAt = now.AddMinutes(-1),
            ConcurrencyToken = Guid.NewGuid(),
        });

        var queuedId = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = queuedId,
            RootTaskId = queuedId,
            Title = "older-queued",
            Goal = "queue",
            Status = AgentTaskStatus.Queued,
            AgentKind = AgentKind.ClaudeCode,
            CreatedAt = now.AddMinutes(-20),
            ConcurrencyToken = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();

        var counted = await db.AgentTasks
            .Where(AgentTaskRoles.NotSpecialist)
            .CountAsync(t => (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
                && !t.CapacityWaitRetained);
        counted.ShouldBe(6);

        var first = await db.AgentTasks.SingleAsync(t => t.Id == firstCounted);
        first.Status = AgentTaskStatus.Succeeded;
        await db.SaveChangesAsync();

        var remainingCounted = await db.AgentTasks
            .Where(AgentTaskRoles.NotSpecialist)
            .CountAsync(t => (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
                && !t.CapacityWaitRetained);
        remainingCounted.ShouldBe(5);

        var retained = await db.AgentTasks.SingleAsync(t => t.Id == retainedId);
        retained.CapacityWaitRetained.ShouldBeTrue();
        var queued = await db.AgentTasks.SingleAsync(t => t.Id == queuedId);
        queued.CreatedAt.ShouldBeLessThan(retained.CreatedAt);
    }
}
