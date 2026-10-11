using Antiphon.Server.Application.Dtos;
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
    public async Task Released_seat_window_open_question_enqueues_different_text()
    {
        foreach (var status in new[] { AgentTaskStatus.Working, AgentTaskStatus.Dispatched })
            await AssertOpenQuestionEnqueuesAsync(status, "retained released-seat answer", "Proceed as planned (Recommended)");
    }

    [Test]
    public async Task Released_seat_window_open_question_enqueues_the_same_text()
    {
        const string accepted = "retained released-seat answer";
        foreach (var status in new[] { AgentTaskStatus.Working, AgentTaskStatus.Dispatched })
            await AssertOpenQuestionEnqueuesAsync(status, accepted, accepted);
    }

    [Test]
    public async Task Released_seat_window_without_an_open_question_keeps_the_accepted_answer()
    {
        const string accepted = "retained released-seat answer";
        await AssertAcceptedAnswerGuardAsync(accepted, "a different in-turn reply", closedTool: false);
        await AssertAcceptedAnswerGuardAsync(accepted, accepted, closedTool: false);
        await AssertAcceptedAnswerGuardAsync(accepted, "a different in-turn reply", closedTool: true);
    }

    [Test]
    public async Task C1069_cancelled_question_identical_resend_is_suppressed()
    {
        const string accepted = "retained released-seat answer";
        foreach (var shape in new[] { "turn-end", "interrupt" })
            await AssertCancelledQuestionGuardAsync(shape, accepted, accepted);
    }

    [Test]
    public async Task C1069_cancelled_question_different_text_is_answer_already_accepted()
    {
        const string accepted = "retained released-seat answer";
        foreach (var shape in new[] { "turn-end", "interrupt" })
            await AssertCancelledQuestionGuardAsync(shape, accepted, "a different in-turn reply");
    }

    [Test]
    public async Task C1069_previous_turn_unmatched_question_is_not_open()
    {
        const string accepted = "retained released-seat answer";
        const string answer = "a different in-turn reply";
        await using var h = await CreateReplyHarnessAsync();
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.ToolCall,
            toolName: GrokQuestionTool.AskUserQuestionName,
            toolUseId: "call-question-c1069-previous");
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.TurnEnd, stopReason: TranscriptKinds.StopReasons.EndTurn);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, "the next turn");
        var task = await SeedTaskAsync(h, AgentTaskStatus.Working);
        var stamp = await StampReleasedSeatWindowAsync(task.Id, accepted);

        var refused = await Should.ThrowAsync<ConflictException>(() =>
            h.Provider.GetRequiredService<AgentTaskReplyService>()
                .AnswerAsync(task.Id, answer, CancellationToken.None));

        refused.Code.ShouldBe("answer_already_accepted");
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId)).ShouldBe(0);
        AssertWindowUnchanged(await db.AgentTasks.SingleAsync(t => t.Id == task.Id), stamp);
    }

    [Test]
    public async Task C1069_question_closed_before_enqueue_takes_the_accepted_answer_guard()
    {
        const string toolUseId = "call-question-c1069-race";
        const string accepted = "retained released-seat answer";
        var runner = new ClosingOnLaterPullRunner(toolUseId);
        await using var h = await CreateReplyHarnessAsync(services =>
            services.AddSingleton<ISessionRunnerClient>(runner));
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.ToolCall,
            toolName: GrokQuestionTool.AskUserQuestionName,
            toolUseId: toolUseId);
        var task = await SeedTaskAsync(h, AgentTaskStatus.Working);
        var stamp = await StampReleasedSeatWindowAsync(task.Id, accepted);

        var refused = await Should.ThrowAsync<ConflictException>(() =>
            h.Provider.GetRequiredService<AgentTaskReplyService>()
                .AnswerAsync(task.Id, "a different in-turn reply", CancellationToken.None));

        refused.Code.ShouldBe("answer_already_accepted");
        runner.Pulls.ShouldBeGreaterThanOrEqualTo(2);
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId)).ShouldBe(0);
        (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId
            && t.Kind == TranscriptKinds.ToolResult && t.ToolUseId == toolUseId)).ShouldBe(1);
        AssertWindowUnchanged(await db.AgentTasks.SingleAsync(t => t.Id == task.Id), stamp);
    }

    [Test]
    public async Task C1069_live_question_in_the_current_turn_uses_the_overlay()
    {
        await using var h = await CreateReplyHarnessAsync();
        await h.InsertTurnAsync("an earlier prompt", "an earlier answer");
        const string toolUseId = "call-question-c1069-live";
        const string answer = "Proceed as planned (Recommended)";
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.ToolCall,
            toolName: GrokQuestionTool.AskUserQuestionName,
            toolUseId: toolUseId);
        var task = await SeedTaskAsync(h, AgentTaskStatus.Working);
        var stamp = await StampReleasedSeatWindowAsync(task.Id, "retained released-seat answer");
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

        h.Adapter.SubmittedBodies.ShouldBe([answer]);
        await using var db = CreateContext();
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        queued.Status.ShouldBe(QueuedMessageStatus.Sent);
        queued.Body.ShouldBe(answer);
        AssertWindowUnchanged(await db.AgentTasks.SingleAsync(t => t.Id == task.Id), stamp);
    }

    private static async Task AssertCancelledQuestionGuardAsync(string shape, string accepted, string answer)
    {
        await using var h = await CreateReplyHarnessAsync();
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.ToolCall,
            toolName: GrokQuestionTool.AskUserQuestionName,
            toolUseId: "call-question-c1069-" + shape);
        if (shape == "turn-end")
        {
            await h.InsertTranscriptEntryAsync(
                TranscriptKinds.TurnEnd, stopReason: TranscriptKinds.StopReasons.Cancelled);
        }
        else
        {
            await h.InsertTranscriptEntryAsync(
                TranscriptKinds.UserPrompt,
                TranscriptKinds.InterruptedPromptPrefix + " by user]");
        }

        var task = await SeedTaskAsync(h, AgentTaskStatus.Working);
        var stamp = await StampReleasedSeatWindowAsync(task.Id, accepted);
        var same = answer == accepted;
        if (same)
        {
            await h.Provider.GetRequiredService<AgentTaskReplyService>()
                .AnswerAsync(task.Id, answer, CancellationToken.None);
        }
        else
        {
            var refused = await Should.ThrowAsync<ConflictException>(() =>
                h.Provider.GetRequiredService<AgentTaskReplyService>()
                    .AnswerAsync(task.Id, answer, CancellationToken.None));
            refused.Code.ShouldBe("answer_already_accepted", shape);
        }

        h.Adapter.SubmittedBodies.ShouldBeEmpty(shape);
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId)).ShouldBe(0, shape);
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Replied)).ShouldBe(0, shape);
        AssertWindowUnchanged(await db.AgentTasks.SingleAsync(t => t.Id == task.Id), stamp);
    }

    private static async Task AssertOpenQuestionEnqueuesAsync(
        AgentTaskStatus status, string accepted, string answer)
    {
        await using var h = await CreateReplyHarnessAsync();
        var toolUseId = "call-question-c1069-" + status;
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.ToolCall,
            toolName: GrokQuestionTool.AskUserQuestionName,
            toolUseId: toolUseId);
        var task = await SeedTaskAsync(h, status);
        var stamp = await StampReleasedSeatWindowAsync(task.Id, accepted);
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

        h.Adapter.SubmittedBodies.ShouldBe([answer], status.ToString());
        await using var db = CreateContext();
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        queued.Status.ShouldBe(QueuedMessageStatus.Sent, status.ToString());
        queued.Origin.ShouldBe(QueuedMessageOrigin.Delegation);
        queued.Body.ShouldBe(answer, status.ToString());
        queued.Body.ShouldNotContain(DelegationReportFormatter.TaskMarker(task.Id));
        var stored = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(status, "overlay reply is not a state change");
        stored.RepliedAt.ShouldNotBeNull();
        stored.RepliedAtSequence.ShouldBeNull();
        AssertWindowUnchanged(stored, stamp);
        var replied = await db.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Replied);
        replied.Detail.ShouldBe(BlockedQuestion.OverlayRepliedDetail);
    }

    private static async Task AssertAcceptedAnswerGuardAsync(
        string accepted, string answer, bool closedTool)
    {
        await using var h = await CreateReplyHarnessAsync();
        if (closedTool)
        {
            const string toolUseId = "call-question-c1069-guard";
            await h.InsertTranscriptEntryAsync(
                TranscriptKinds.ToolCall,
                toolName: GrokQuestionTool.AskUserQuestionName,
                toolUseId: toolUseId);
            await h.InsertTranscriptEntryAsync(
                TranscriptKinds.ToolResult,
                $"{GrokQuestionTool.CompletedAnswerPrefix} already answered",
                toolName: GrokQuestionTool.AskUserQuestionName,
                toolUseId: toolUseId);
        }

        var task = await SeedTaskAsync(h, AgentTaskStatus.Working);
        var stamp = await StampReleasedSeatWindowAsync(task.Id, accepted);
        var same = answer == accepted;
        if (same)
        {
            await h.Provider.GetRequiredService<AgentTaskReplyService>()
                .AnswerAsync(task.Id, answer, CancellationToken.None);
        }
        else
        {
            var refused = await Should.ThrowAsync<ConflictException>(() =>
                h.Provider.GetRequiredService<AgentTaskReplyService>()
                    .AnswerAsync(task.Id, answer, CancellationToken.None));
            refused.Code.ShouldBe("answer_already_accepted");
        }

        h.Adapter.SubmittedBodies.ShouldBeEmpty();
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId)).ShouldBe(0);
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Replied)).ShouldBe(0);
        AssertWindowUnchanged(await db.AgentTasks.SingleAsync(t => t.Id == task.Id), stamp);
    }

    private sealed record ReleasedSeatWindow(
        int Attempt,
        string Answer,
        Guid AnswerId,
        Guid RoundId,
        Guid ReleaseId,
        int TargetAttempt,
        DateTime AcceptedAt);

    private static async Task<ReleasedSeatWindow> StampReleasedSeatWindowAsync(Guid taskId, string answer)
    {
        await using var db = CreateContext();
        var stored = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        stored.Attempt = 2;
        stored.ReleasedSeatAnswer = answer;
        stored.ReleasedSeatAnswerId = Guid.NewGuid();
        stored.ReleasedSeatAnswerRoundId = Guid.NewGuid();
        stored.ReleasedSeatAnswerReleaseId = Guid.NewGuid();
        stored.ReleasedSeatAnswerTargetAttempt = stored.Attempt;
        stored.ReleasedSeatAnswerAcceptedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await db.Entry(stored).ReloadAsync();
        return new ReleasedSeatWindow(
            stored.Attempt,
            stored.ReleasedSeatAnswer!,
            stored.ReleasedSeatAnswerId!.Value,
            stored.ReleasedSeatAnswerRoundId!.Value,
            stored.ReleasedSeatAnswerReleaseId!.Value,
            stored.ReleasedSeatAnswerTargetAttempt!.Value,
            stored.ReleasedSeatAnswerAcceptedAt!.Value);
    }

    private static void AssertWindowUnchanged(AgentTask stored, ReleasedSeatWindow stamp)
    {
        stored.Attempt.ShouldBe(stamp.Attempt);
        stored.ReleasedSeatAnswer.ShouldBe(stamp.Answer);
        stored.ReleasedSeatAnswerId.ShouldBe(stamp.AnswerId);
        stored.ReleasedSeatAnswerRoundId.ShouldBe(stamp.RoundId);
        stored.ReleasedSeatAnswerReleaseId.ShouldBe(stamp.ReleaseId);
        stored.ReleasedSeatAnswerTargetAttempt.ShouldBe(stamp.TargetAttempt);
        stored.ReleasedSeatAnswerAcceptedAt.ShouldBe(stamp.AcceptedAt);
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

    private static async Task<BridgeQueueHarness> CreateReplyHarnessAsync(
        Action<IServiceCollection>? configure = null)
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
                configure?.Invoke(services);
            },
        });
        return h;
    }

    /// <summary>
    /// The decision catch-up sees the question still open. The next pull persists its ToolResult
    /// before enqueue, which is the closure race.
    /// </summary>
    private sealed class ClosingOnLaterPullRunner(string toolUseId) : ISessionRunnerClient
    {
        private int _openPulls;
        public int Pulls { get; private set; }

        public async Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct)
        {
            Pulls++;
            await using var db = CreateContext();
            var hasCall = await db.TranscriptEntries.AnyAsync(
                t => t.AgentSessionId == sessionId
                    && t.Kind == TranscriptKinds.ToolCall
                    && t.ToolUseId == toolUseId, ct);
            var hasResult = await db.TranscriptEntries.AnyAsync(
                t => t.AgentSessionId == sessionId
                    && t.Kind == TranscriptKinds.ToolResult
                    && t.ToolUseId == toolUseId, ct);
            if (!hasCall || hasResult || _openPulls == 0)
            {
                if (hasCall && !hasResult)
                    _openPulls++;
                return new SessionRunnerTranscriptDto(sessionId, [], 0);
            }

            var row = new SessionRunnerTranscriptEvent(
                sessionId,
                1000,
                TranscriptKinds.ToolResult,
                "c1069-race-result",
                null,
                DateTimeOffset.UtcNow,
                "user",
                "closed before enqueue",
                GrokQuestionTool.AskUserQuestionName,
                null,
                toolUseId,
                false,
                null);
            return new SessionRunnerTranscriptDto(sessionId, [row], 1000);
        }

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) =>
            throw new NotSupportedException();
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
