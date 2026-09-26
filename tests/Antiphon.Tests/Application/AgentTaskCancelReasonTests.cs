using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0738 V-1 to V-3: CancelAsync records an optional reason and still stops the delegate.</summary>
[Category("Integration")]
public sealed class AgentTaskCancelReasonTests
{
    [Test]
    public async Task cancel_with_a_reason_records_it_on_the_row_and_in_the_canceled_event()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var task = await SeedTaskAsync(schema, AgentTaskStatus.Queued);
        const string reason = "Card CARD-0091 closed (Done): Live data change; nothing to deploy.";

        await using (var db = CreateContext(schema))
        {
            await CreateService(db, new RecordingSessionStopper()).CancelAsync(task.Id, CancellationToken.None, reason);
        }

        await using var verify = CreateContext(schema);
        var row = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        row.Status.ShouldBe(AgentTaskStatus.Canceled);
        row.FailureReason.ShouldBe(reason);
        var canceled = await verify.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Canceled);
        canceled.Detail.ShouldBe("Canceled: " + reason);
    }

    [Test]
    public async Task cancel_without_a_reason_keeps_the_bare_canceled_event()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var task = await SeedTaskAsync(schema, AgentTaskStatus.Queued);

        await using (var db = CreateContext(schema))
        {
            await CreateService(db, new RecordingSessionStopper()).CancelAsync(task.Id, CancellationToken.None);
        }

        await using var verify = CreateContext(schema);
        var row = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        row.Status.ShouldBe(AgentTaskStatus.Canceled);
        row.FailureReason.ShouldBeNull();
        var canceled = await verify.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Canceled);
        canceled.Detail.ShouldBe("Canceled.");
    }

    [Test]
    public async Task cancel_with_a_reason_still_stops_the_delegate()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var sessionId = await SeedSessionAsync(schema);
        var task = await SeedTaskAsync(schema, AgentTaskStatus.Working, sessionId);
        const string reason = "Card CARD-0001 closed (Canceled): superseded.";
        var stopper = new RecordingSessionStopper();

        await using (var db = CreateContext(schema))
        {
            await CreateService(db, stopper).CancelAsync(task.Id, CancellationToken.None, reason);
        }

        stopper.Killed.ShouldBe([sessionId]);
        await using var verify = CreateContext(schema);
        var row = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        row.FailureReason.ShouldBe(reason);
        row.Status.ShouldBe(AgentTaskStatus.Canceled);
    }

    private static AgentTaskService CreateService(AppDbContext db, RecordingSessionStopper stopper) => new(
        db,
        new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
        Options.Create(new DelegationSettings()),
        new MockEventBus(),
        stopper,
        TimeProvider.System,
        NullLogger<AgentTaskService>.Instance);

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private static async Task<AgentTask> SeedTaskAsync(
        IsolatedTestSchema schema, AgentTaskStatus status, Guid? sessionId = null)
    {
        await using var db = CreateContext(schema);
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "cancel reason",
            Goal = "cancel reason",
            Role = AgentTaskRole.Code,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(),
            Status = status,
            AgentSessionId = sessionId,
            DispatchedAt = status == AgentTaskStatus.Queued ? null : now,
            CreatedAt = now,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task<Guid> SeedSessionAsync(IsolatedTestSchema schema)
    {
        await using var db = CreateContext(schema);
        var now = DateTime.UtcNow;
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = Path.GetTempPath(),
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        };
        db.AgentSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }
}
