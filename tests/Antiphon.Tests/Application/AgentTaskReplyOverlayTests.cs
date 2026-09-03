using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0241 S3: <c>-Reply</c> on a running task with an open question-tool is Now (no marker,
/// row persisted) rather than a 409. Blocked stays WhenIdle + marker. Refine is unchanged.
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class AgentTaskReplyOverlayTests
{
    private static AppDbContext CreateContext() => BridgeQueueHarness.CreateContext();

    [Test]
    public async Task Blocked_answer_still_queues_WhenIdle_with_the_task_marker()
    {
        await using var h = await CreateReplyHarnessAsync();
        await h.InsertTurnAsync("an earlier prompt", "an earlier answer");
        await h.MarkWorkingAsync();
        var task = await SeedTaskAsync(h, AgentTaskStatus.Blocked);

        await h.Provider.GetRequiredService<AgentTaskReplyService>()
            .AnswerAsync(task.Id, "yes, continue", CancellationToken.None);

        await using var db = CreateContext();
        var stored = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Working);
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        queued.Status.ShouldBe(QueuedMessageStatus.Pending, "Blocked stays WhenIdle even when the session is live");
        queued.Origin.ShouldBe(QueuedMessageOrigin.Delegation);
        queued.Body.ShouldContain(DelegationReportFormatter.TaskMarker(task.Id));
        queued.Body.ShouldContain("yes, continue");
        h.Adapter.Inputs.ShouldBeEmpty("WhenIdle must not type into a working session");
    }

    [Test]
    public async Task Working_with_open_question_tool_types_Now_without_a_marker_and_confirms_on_ToolResult()
    {
        await using var h = await CreateReplyHarnessAsync();
        await h.InsertTurnAsync("an earlier prompt", "an earlier answer");
        const string toolUseId = "call-question-overlay-1";
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.ToolCall,
            toolName: GrokQuestionTool.AskUserQuestionName,
            toolUseId: toolUseId);
        var task = await SeedTaskAsync(h, AgentTaskStatus.Working);
        const string answer = "Proceed as planned (Recommended)";
        h.Adapter.OnSubmitted = async submitted =>
        {
            await h.InsertTranscriptEntryAsync(
                TranscriptKinds.ToolResult,
                $"{GrokQuestionTool.CompletedAnswerPrefix} \"q\"=\"{submitted}\". You can now continue.",
                toolName: GrokQuestionTool.AskUserQuestionName,
                toolUseId: toolUseId);
        };

        await h.Provider.GetRequiredService<AgentTaskReplyService>()
            .AnswerAsync(task.Id, answer, CancellationToken.None);

        h.Adapter.Inputs.ShouldBe([answer, "\r"]);
        h.Adapter.SubmittedBodies.ShouldBe([answer]);
        await using var db = CreateContext();
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        queued.Status.ShouldBe(QueuedMessageStatus.Sent);
        queued.Origin.ShouldBe(QueuedMessageOrigin.Delegation);
        queued.Body.ShouldBe(answer);
        queued.Body.ShouldNotContain(DelegationReportFormatter.TaskMarker(task.Id));
        var stored = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Working, "overlay reply is not a state change");
        stored.RepliedAt.ShouldNotBeNull();
        stored.RepliedAtSequence.ShouldBeNull();
    }

    [Test]
    public async Task Working_without_an_open_question_tool_is_a_409_pointing_at_Refine()
    {
        await using var h = await CreateReplyHarnessAsync();
        var task = await SeedTaskAsync(h, AgentTaskStatus.Working);

        var refused = await Should.ThrowAsync<ConflictException>(() =>
            h.Provider.GetRequiredService<AgentTaskReplyService>()
                .AnswerAsync(task.Id, "Proceed as planned (Recommended)", CancellationToken.None));
        refused.Message.ShouldContain("Refine");
        refused.Message.ShouldContain("Blocked");
        refused.Message.ShouldContain("ask_user_question");
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId)).ShouldBe(0);
    }

    [Test]
    public async Task Dispatched_without_an_open_question_tool_is_the_same_409()
    {
        await using var h = await CreateReplyHarnessAsync();
        var task = await SeedTaskAsync(h, AgentTaskStatus.Dispatched);

        var refused = await Should.ThrowAsync<ConflictException>(() =>
            h.Provider.GetRequiredService<AgentTaskReplyService>()
                .AnswerAsync(task.Id, "steer this", CancellationToken.None));
        refused.Message.ShouldContain("Refine");
    }

    [Test]
    public async Task Closed_question_tool_is_not_open()
    {
        await using var h = await CreateReplyHarnessAsync();
        const string toolUseId = "call-question-closed-1";
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.ToolCall,
            toolName: GrokQuestionTool.AskUserQuestionName,
            toolUseId: toolUseId);
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.ToolResult,
            $"{GrokQuestionTool.CompletedAnswerPrefix} already answered",
            toolName: GrokQuestionTool.AskUserQuestionName,
            toolUseId: toolUseId);
        var task = await SeedTaskAsync(h, AgentTaskStatus.Working);

        await Should.ThrowAsync<ConflictException>(() =>
            h.Provider.GetRequiredService<AgentTaskReplyService>()
                .AnswerAsync(task.Id, "Proceed as planned (Recommended)", CancellationToken.None));
    }

    [Test]
    public async Task Refine_on_Working_is_still_WhenIdle_with_BuildRefinement()
    {
        await using var h = await CreateReplyHarnessAsync();
        await h.InsertTurnAsync("an earlier prompt", "an earlier answer");
        await h.MarkWorkingAsync();
        var task = await SeedTaskAsync(h, AgentTaskStatus.Working);
        const string message = "Skip slice 3 — it landed elsewhere.";

        await h.Provider.GetRequiredService<AgentTaskReplyService>()
            .RefineAsync(task.Id, message, CancellationToken.None);

        await using var db = CreateContext();
        (await db.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Working);
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        queued.Status.ShouldBe(QueuedMessageStatus.Pending);
        queued.Body.ShouldContain(DelegationReportFormatter.TaskMarker(task.Id));
        queued.Body.ShouldContain(message);
        queued.Body.ShouldContain("do NOT end your turn just to acknowledge");
    }

    [Test]
    public async Task Refine_on_Blocked_is_still_refused()
    {
        await using var h = await CreateReplyHarnessAsync();
        var task = await SeedTaskAsync(h, AgentTaskStatus.Blocked);

        var refused = await Should.ThrowAsync<ConflictException>(() =>
            h.Provider.GetRequiredService<AgentTaskReplyService>()
                .RefineAsync(task.Id, "a refinement", CancellationToken.None));
        refused.Message.ShouldContain("ANSWER");
    }

    private static async Task<BridgeQueueHarness> CreateReplyHarnessAsync()
    {
        var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
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
        return h;
    }

    private static async Task<AgentTask> SeedTaskAsync(BridgeQueueHarness h, AgentTaskStatus status)
    {
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "Overlay reply seed",
            Goal = "Do the thing.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Docs,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = h.TempRoot,
            AgentSessionId = h.SessionId,
            Status = status,
            CreatedAt = now,
            DispatchedAt = now,
        };
        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }
}
