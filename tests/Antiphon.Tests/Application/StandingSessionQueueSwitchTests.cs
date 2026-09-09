using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class StandingSessionQueueSwitchTests
{
    [Test]
    public async Task Target_late_confirmation_and_open_task_guards_remain_intact()
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var f = new StandingRecoveryFixture(adapter);
        await f.SeedAsync();
        adapter.RegisterOnStart = f.Harness.Provider.GetRequiredService<AgentSessionRuntime>();
        var now = DateTime.UtcNow;
        var confirmed = new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = f.A.Id, Sequence = 1,
            Body = "The old target prompt already reached its owning transcript", CreatedAt = now.AddMinutes(-2),
            DeliveryAttempts = 1, LastDeliveryBaselineSequence = 0, LastDeliveryStartedAt = now.AddSeconds(-10), DeliveryVerdict = DeliveryVerdict.NoSubmitOutput };
        var parked = new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = f.A.Id, Sequence = 2,
            Body = "An unconfirmed parked target prompt", CreatedAt = now.AddMinutes(-1), DeliveryAttempts = int.MaxValue,
            LastDeliveryBaselineSequence = 2, LastDeliveryStartedAt = now.AddSeconds(-5), DeliveryVerdict = DeliveryVerdict.NoSubmitOutput };
        await using (var db = f.Db())
        {
            db.SessionQueuedMessages.AddRange(confirmed, parked);
            db.TranscriptEntries.AddRange(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = f.A.Id, Sequence = 1,
                Kind = TranscriptKinds.UserPrompt, Text = confirmed.Body, Timestamp = now, CreatedAt = now },
                new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = f.A.Id, Sequence = 2,
                    Kind = TranscriptKinds.TurnEnd, StopReason = "end_turn", Timestamp = now, CreatedAt = now });
            var historical = Guid.NewGuid(); var child = Guid.NewGuid();
            db.AgentTasks.AddRange(new AgentTask { Id = historical, RootTaskId = historical, AgentId = f.Agent.Id,
                AgentSessionId = f.A.Id, Title = "Completed history", Goal = "Synthetic", WorkingDirectory = f.Root,
                Status = AgentTaskStatus.Succeeded, CreatedAt = now },
                new AgentTask { Id = child, RootTaskId = child, ParentSessionId = f.A.Id, Title = "Independent child",
                    Goal = "Synthetic", WorkingDirectory = f.Root, Status = AgentTaskStatus.Working, CreatedAt = now });
            await db.SaveChangesAsync();
        }
        await f.StartAsync(new(ResumeSessionId: f.A.Id)); await f.IdleAsync();
        await f.Harness.Provider.GetRequiredService<SessionMessageQueueService>().FlushStrandedQueuesAsync(default);
        adapter.Inputs.ShouldBeEmpty();
        await using var verify = f.Db();
        var done = (await verify.SessionQueuedMessages.FindAsync(confirmed.Id))!;
        done.AgentSessionId.ShouldBe(f.A.Id); done.Status.ShouldBe(QueuedMessageStatus.Sent);
        done.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed); done.DeliveryAttempts.ShouldBe(1);
        done.LastDeliveryBaselineSequence.ShouldBe(0);
        var waiting = (await verify.SessionQueuedMessages.FindAsync(parked.Id))!;
        waiting.Status.ShouldBe(QueuedMessageStatus.Pending); waiting.DeliveryAttempts.ShouldBe(int.MaxValue);
        waiting.LastDeliveryBaselineSequence.ShouldBe(2);
    }

    [Test]
    [Arguments(AgentTaskStatus.Dispatched)]
    [Arguments(AgentTaskStatus.Working)]
    [Arguments(AgentTaskStatus.Blocked)]
    public async Task Open_execution_on_either_history_or_current_target_refuses_selection(AgentTaskStatus status)
    {
        foreach (var shape in new[] { "source", "target", "same-current" })
        {
            var adapter = new FakeAgentProtocolAdapter();
            await using var f = new StandingRecoveryFixture(adapter);
            await f.SeedAsync(held: true);
            var taskId = Guid.NewGuid();
            var execution = shape == "target" ? f.A.Id : f.B.Id;
            await using (var db = f.Db())
            {
                db.AgentTasks.Add(new AgentTask { Id = taskId, RootTaskId = taskId, AgentId = f.Agent.Id,
                    AgentSessionId = execution, Title = "Open execution", Goal = "Synthetic assignment",
                    WorkingDirectory = f.Root, Status = status, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            var target = shape == "same-current" ? f.B.Id : f.A.Id;
            (await Should.ThrowAsync<ConflictException>(() => f.StartAsync(new(ResumeSessionId: target))))
                .Code.ShouldBe("standing_resume_work_in_flight");
            adapter.Started.ShouldBeFalse();
            await using var verify = f.Db();
            (await verify.Agents.FindAsync(f.Agent.Id))!.PersistentSessionId.ShouldBe(f.B.Id.ToString("D"));
            (await verify.AgentTasks.FindAsync(taskId))!.AgentSessionId.ShouldBe(execution);
            (await verify.AgentSupervisionStates.FindAsync(f.Agent.Id))!.ContinuityHeldAt.ShouldNotBeNull();
        }
    }

    [Test]
    public async Task Any_prior_delivery_evidence_refuses_switch_and_fresh()
    {
        Action<SessionQueuedMessage>[] evidence = [m => m.DeliveryAttempts = 1,
            m => m.LastDeliveryBaselineSequence = 0, m => m.LastDeliveryStartedAt = DateTime.UtcNow,
            m => m.DeliveryVerdict = DeliveryVerdict.NoSubmitOutput, m => m.DeliveryVerdictAt = DateTime.UtcNow,
            m => m.SentAt = DateTime.UtcNow, m => m.ChannelReplySettledAt = DateTime.UtcNow];
        foreach (var fresh in new[] { false, true })
        foreach (var stamp in evidence)
        {
            var adapter = new FakeAgentProtocolAdapter();
            await using var f = new StandingRecoveryFixture(adapter);
            await f.SeedAsync(held: true);
            var row = new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = f.B.Id,
                Body = "Ambiguous input", Sequence = 1, CreatedAt = DateTime.UtcNow };
            stamp(row);
            await using (var db = f.Db()) { db.SessionQueuedMessages.Add(row); await db.SaveChangesAsync(); }
            var error = await Should.ThrowAsync<ConflictException>(() => f.StartAsync(fresh ? new(Fresh: true) : new(ResumeSessionId: f.A.Id)));
            error.Code.ShouldBe("standing_resume_delivery_pending");
            adapter.Started.ShouldBeFalse();
            await using var verify = f.Db();
            var stored = (await verify.SessionQueuedMessages.FindAsync(row.Id))!;
            stored.AgentSessionId.ShouldBe(f.B.Id);
            stored.LastDeliveryBaselineSequence.ShouldBe(row.LastDeliveryBaselineSequence);
            stored.DeliveryAttempts.ShouldBe(row.DeliveryAttempts);
            stored.DeliveryVerdict.ShouldBe(row.DeliveryVerdict);
            (await verify.Agents.FindAsync(f.Agent.Id))!.PersistentSessionId.ShouldBe(f.B.Id.ToString("D"));
            (await verify.AgentSupervisionStates.FindAsync(f.Agent.Id))!.ContinuityHeldAt.ShouldNotBeNull();
            (await verify.AgentSessions.CountAsync(s => s.StandingAgentId == f.Agent.Id)).ShouldBe(2);
        }
    }

    [Test]
    public async Task Only_unattempted_messages_move_atomically_and_keep_order_and_routing()
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var f = new StandingRecoveryFixture(adapter);
        await f.SeedAsync();
        var now = DateTime.UtcNow;
        var existing = new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = f.A.Id, Sequence = 8,
            Body = "Existing", CreatedAt = now, HoldUntil = now.AddDays(1) };
        var move = new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = f.B.Id, Sequence = 1,
            Body = "Safe", CreatedAt = now, HoldUntil = now.AddDays(2), Origin = QueuedMessageOrigin.Channel,
            ConversationKey = "fake:recovery" };
        var sent = new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = f.B.Id, Sequence = 2,
            Body = "Sent", CreatedAt = now, Status = QueuedMessageStatus.Sent, SentAt = now };
        await using (var db = f.Db()) { db.SessionQueuedMessages.AddRange(existing, move, sent); await db.SaveChangesAsync(); }
        await f.StartAsync(new(ResumeSessionId: f.A.Id)); await f.IdleAsync();
        await using var verify = f.Db();
        var moved = (await verify.SessionQueuedMessages.FindAsync(move.Id))!;
        moved.AgentSessionId.ShouldBe(f.A.Id); moved.Sequence.ShouldBe(9);
        moved.ConversationKey.ShouldBe("fake:recovery");
        moved.HoldUntil!.Value.ShouldBe(move.HoldUntil!.Value, TimeSpan.FromMilliseconds(1));
        (await verify.SessionQueuedMessages.FindAsync(existing.Id))!.Sequence.ShouldBe(8);
        (await verify.SessionQueuedMessages.FindAsync(sent.Id))!.AgentSessionId.ShouldBe(f.B.Id);
        adapter.SentInput.ShouldBeEmpty();
    }
}
