using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class AgentTaskLandMonitoringTests
{
    [Test]
    [Arguments(LandRequestState.Queued)]
    [Arguments(LandRequestState.Held)]
    [Arguments(LandRequestState.Running)]
    [Arguments(LandRequestState.NeedsResolution)]
    public async Task C467_V15_ThresholdsUseMeaningfulProgress(LandRequestState state)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var now = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);
        var clock = new FakeTimeProvider(now);
        var taskId = Guid.NewGuid();
        var request = new AgentTaskLandRequest { Id = Guid.NewGuid(), TaskId = taskId, State = state,
            RequestedAt = now, LastProgressAt = now, LastEvaluatedAt = now, ReplyTo = AgentTaskReplyTo.None };
        db.AgentTasks.Add(new AgentTask { Id = taskId, RootTaskId = taskId, Title = "C467 monitor", Goal = "monitor fixture",
            WorkingDirectory = Path.GetTempPath(), Status = AgentTaskStatus.Succeeded, ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = now, LandRequestedAt = now, CurrentLandRequestId = request.Id });
        db.AgentTaskLandRequests.Add(request);
        await db.SaveChangesAsync();
        var service = new AgentTaskLandMonitorService(db, clock, Options.Create(new DelegationSettings()), new MockEventBus());
        clock.Advance(TimeSpan.FromSeconds(299.999));
        await service.SweepAsync(CancellationToken.None);
        (await db.AgentTaskLandNotifications.CountAsync(n => n.RequestId == request.Id)).ShouldBe(0);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await service.SweepAsync(CancellationToken.None);
        (await db.AgentTaskLandNotifications.CountAsync(n => n.RequestId == request.Id)).ShouldBe(1);
        clock.Advance(TimeSpan.FromSeconds(599.999));
        await service.SweepAsync(CancellationToken.None);
        (await db.AgentTaskLandNotifications.CountAsync(n => n.RequestId == request.Id)).ShouldBe(1);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await service.SweepAsync(CancellationToken.None);
        (await db.AgentTaskLandNotifications.CountAsync(n => n.RequestId == request.Id)).ShouldBe(2);
        await using var restarted = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await new AgentTaskLandMonitorService(restarted, clock, Options.Create(new DelegationSettings()), new MockEventBus()).SweepAsync(CancellationToken.None);
        (await restarted.AgentTaskLandNotifications.CountAsync(n => n.RequestId == request.Id)).ShouldBe(2);
        var saved = await restarted.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id);
        saved.LastProgressAt.ShouldBe(now);
        saved.LastEvaluatedAt.ShouldBe(now.AddMinutes(15));
        saved.Attempt.ShouldBe(0);
    }
}
