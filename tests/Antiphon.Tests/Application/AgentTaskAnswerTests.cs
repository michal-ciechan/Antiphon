using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0033 S2: the first direct pins on <c>AnswerAsync</c> origin, round and the Replied record.</summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class AgentTaskAnswerTests
{
    private static AppDbContext CreateContext() => BridgeQueueHarness.CreateContext();

    [Test]
    public async Task answers_a_blocked_task_and_enqueues_marker_plus_text_WhenIdle()
    {
        await using var h = await CreateHarnessAsync();
        await h.InsertTurnAsync("an earlier prompt", "an earlier answer");
        await h.MarkWorkingAsync();
        var task = await SeedBlockedAsync(h);
        await AddEventAsync(task.Id, AgentTaskEventType.Blocked, "Delegate asked: Should I continue?");

        await h.Provider.GetRequiredService<AgentTaskReplyService>()
            .AnswerAsync(task.Id, "yes, continue", AnswerOrigin.Web, round: 1, CancellationToken.None);

        await using var db = CreateContext();
        var stored = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Working);
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        queued.Status.ShouldBe(QueuedMessageStatus.Pending);
        queued.Origin.ShouldBe(QueuedMessageOrigin.Delegation);
        queued.Body.ShouldContain(DelegationReportFormatter.TaskMarker(task.Id));
        queued.Body.ShouldContain("yes, continue");
        var replied = await db.AgentTaskEvents.SingleAsync(e =>
            e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Replied);
        replied.Detail.ShouldBe("Answered via Web (round 1): yes, continue");
    }

    [Test]
    public async Task refuses_a_task_that_is_not_blocked()
    {
        await using var h = await CreateHarnessAsync();
        var task = await SeedAsync(h, AgentTaskStatus.Succeeded, h.SessionId);

        var refused = await Should.ThrowAsync<ConflictException>(() =>
            h.Provider.GetRequiredService<AgentTaskReplyService>()
                .AnswerAsync(task.Id, "too late", AnswerOrigin.Web, null, CancellationToken.None));
        refused.Message.ShouldContain("not waiting for an answer");
    }

    [Test]
    public async Task refuses_when_the_session_is_gone()
    {
        await using var h = await CreateHarnessAsync();
        var task = await SeedAsync(h, AgentTaskStatus.Blocked, sessionId: null);

        var refused = await Should.ThrowAsync<ConflictException>(() =>
            h.Provider.GetRequiredService<AgentTaskReplyService>()
                .AnswerAsync(task.Id, "hello", AnswerOrigin.Cli, null, CancellationToken.None));
        refused.Message.ShouldBe("The delegate's session is no longer available.");
    }

    [Test]
    public async Task refuses_a_stale_round()
    {
        await using var h = await CreateHarnessAsync();
        var task = await SeedBlockedAsync(h);
        await AddEventAsync(task.Id, AgentTaskEventType.Blocked, "Delegate asked: First?", DateTime.UtcNow.AddMinutes(-10));
        await AddEventAsync(task.Id, AgentTaskEventType.Replied, "Answered via Web (round 1): first answer", DateTime.UtcNow.AddMinutes(-8));
        await AddEventAsync(task.Id, AgentTaskEventType.Blocked, "Delegate asked: Second?", DateTime.UtcNow.AddMinutes(-1));

        var refused = await Should.ThrowAsync<ConflictException>(() =>
            h.Provider.GetRequiredService<AgentTaskReplyService>()
                .AnswerAsync(task.Id, "stale", AnswerOrigin.Channel, round: 1, CancellationToken.None));
        refused.Message.ShouldContain("round 2");
        refused.Message.ShouldContain("round 1");
        await using var db = CreateContext();
        (await db.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Blocked);
        (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId)).ShouldBe(0);
    }

    [Test]
    public async Task a_null_round_skips_the_stale_guard()
    {
        await using var h = await CreateHarnessAsync();
        var task = await SeedBlockedAsync(h);
        await AddEventAsync(task.Id, AgentTaskEventType.Blocked, "Delegate asked: First?", DateTime.UtcNow.AddMinutes(-10));
        await AddEventAsync(task.Id, AgentTaskEventType.Blocked, "Delegate asked: Second?", DateTime.UtcNow.AddMinutes(-1));

        await h.Provider.GetRequiredService<AgentTaskReplyService>()
            .AnswerAsync(task.Id, "from the cli", AnswerOrigin.Cli, round: null, CancellationToken.None);

        await using var db = CreateContext();
        (await db.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Working);
        var replied = await db.AgentTaskEvents.SingleAsync(e =>
            e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Replied);
        replied.Detail.ShouldBe("Answered via Cli (round 2): from the cli");
    }

    [Test]
    public async Task blocked_event_detail_carries_the_question()
    {
        BlockedQuestion.BlockedEventDetail("Findings.\n\nShip it now?")
            .ShouldBe("Delegate asked: Ship it now?");
    }

    private static async Task<BridgeQueueHarness> CreateHarnessAsync()
    {
        return await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = false,
            ConfigureServices = services =>
            {
                services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
                services.AddSingleton<DelegationWorkspaceResolver>();
                services.AddSingleton<AgentTaskReplyService>();
                services.AddScoped<AgentTaskService>();
            },
        });
    }

    private static Task<AgentTask> SeedBlockedAsync(BridgeQueueHarness h) =>
        SeedAsync(h, AgentTaskStatus.Blocked, h.SessionId);

    private static async Task<AgentTask> SeedAsync(
        BridgeQueueHarness h, AgentTaskStatus status, Guid? sessionId)
    {
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "Answer seed",
            Goal = "Do the thing.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Docs,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = h.TempRoot,
            AgentSessionId = sessionId,
            Status = status,
            CreatedAt = now,
            DispatchedAt = now,
        };
        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task AddEventAsync(
        Guid taskId, AgentTaskEventType type, string detail, DateTime? at = null)
    {
        await using var db = CreateContext();
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = taskId,
            Type = type,
            Detail = detail,
            At = at ?? DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
