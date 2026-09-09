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
    [Arguments(false)]
    [Arguments(true)]
    public async Task A_corrupt_current_pointer_cannot_transfer_another_owners_pending_input(bool fresh)
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var f = new StandingRecoveryFixture(adapter); await f.SeedAsync(held: true);
        var messageId = Guid.NewGuid();
        await using (var db = f.Db())
        {
            (await db.AgentSessions.FindAsync(f.B.Id))!.StandingAgentId = Guid.NewGuid();
            db.SessionQueuedMessages.Add(new SessionQueuedMessage { Id = messageId, AgentSessionId = f.B.Id,
                Body = "Another owner's pending input", Sequence = 1, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        (await Should.ThrowAsync<ConflictException>(() => f.StartAsync(fresh ? new(Fresh: true) : new(ResumeSessionId: f.A.Id))))
            .Code.ShouldBe("standing_resume_not_owned");
        adapter.Started.ShouldBeFalse();
        await using var verify = f.Db();
        (await verify.SessionQueuedMessages.FindAsync(messageId))!.AgentSessionId.ShouldBe(f.B.Id);
        (await verify.Agents.FindAsync(f.Agent.Id))!.PersistentSessionId.ShouldBe(f.B.Id.ToString("D"));
        (await verify.AgentSupervisionStates.FindAsync(f.Agent.Id))!.ContinuityHeldAt.ShouldNotBeNull();
    }

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
            m => m.SentAt = DateTime.UtcNow, m => m.ChannelReplySettledAt = DateTime.UtcNow,
            m => m.CanceledAt = DateTime.UtcNow, m => m.DeliveryAttempts = int.MaxValue];
        foreach (var fresh in new[] { false, true })
        foreach (var stamp in evidence)
        {
            var adapter = new FakeAgentProtocolAdapter();
            await using var f = new StandingRecoveryFixture(adapter);
            await f.SeedAsync(held: true);
            var row = new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = f.B.Id,
                Body = "Ambiguous input", Sequence = 1, CreatedAt = DateTime.UtcNow };
            stamp(row);
            var safe = new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = f.B.Id,
                Body = "Safe but must not move partially", Sequence = 2, CreatedAt = DateTime.UtcNow };
            string before;
            await using (var db = f.Db())
            {
                db.SessionQueuedMessages.AddRange(row, safe); await db.SaveChangesAsync(); await db.Entry(row).ReloadAsync();
                before = System.Text.Json.JsonSerializer.Serialize(db.Entry(row).CurrentValues.Properties
                    .ToDictionary(p => p.Name, p => db.Entry(row).CurrentValues[p]));
            }
            var error = await Should.ThrowAsync<ConflictException>(() => f.StartAsync(fresh ? new(Fresh: true) : new(ResumeSessionId: f.A.Id)));
            error.Code.ShouldBe("standing_resume_delivery_pending");
            adapter.Started.ShouldBeFalse();
            await using var verify = f.Db();
            var stored = (await verify.SessionQueuedMessages.FindAsync(row.Id))!;
            System.Text.Json.JsonSerializer.Serialize(verify.Entry(stored).CurrentValues.Properties
                .ToDictionary(p => p.Name, p => verify.Entry(stored).CurrentValues[p])).ShouldBe(before);
            stored.AgentSessionId.ShouldBe(f.B.Id);
            stored.LastDeliveryBaselineSequence.ShouldBe(row.LastDeliveryBaselineSequence);
            stored.DeliveryAttempts.ShouldBe(row.DeliveryAttempts);
            stored.DeliveryVerdict.ShouldBe(row.DeliveryVerdict);
            (await verify.SessionQueuedMessages.FindAsync(safe.Id))!.AgentSessionId.ShouldBe(f.B.Id);
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
        adapter.RegisterOnStart = f.Harness.Provider.GetRequiredService<AgentSessionRuntime>();
        adapter.OnSubmitted = async body =>
        {
            await using var db = f.Db();
            var sequence = await db.TranscriptEntries.Where(e => e.AgentSessionId == f.A.Id).MaxAsync(e => (long?)e.Sequence) ?? 0;
            db.TranscriptEntries.AddRange(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = f.A.Id, Sequence = ++sequence,
                Kind = TranscriptKinds.UserPrompt, Text = body, Timestamp = DateTime.UtcNow, CreatedAt = DateTime.UtcNow },
                new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = f.A.Id, Sequence = ++sequence,
                    Kind = TranscriptKinds.TurnEnd, StopReason = "end_turn", Timestamp = DateTime.UtcNow, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        };
        var now = DateTime.UtcNow;
        var existing = new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = f.A.Id, Sequence = 8,
            Body = "Existing", CreatedAt = now, HoldUntil = now.AddDays(1) };
        var move = new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = f.B.Id, Sequence = 1,
            Body = "Safe", CreatedAt = now, HoldUntil = now.AddDays(2), Origin = QueuedMessageOrigin.Channel,
            ConversationKey = "fake:recovery" };
        var sent = new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = f.B.Id, Sequence = 2,
            Body = "Sent", CreatedAt = now, Status = QueuedMessageStatus.Sent, SentAt = now };
        var extras = new[] { QueuedMessageOrigin.Ui, QueuedMessageOrigin.Delegation, QueuedMessageOrigin.Scheduled }
            .Select((origin, i) => new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = f.B.Id,
                Sequence = i + 3, Body = $"Safe {origin}", CreatedAt = now, HoldUntil = now.AddDays(2), Origin = origin,
                NoteHeader = "synthetic header", ContentDigest = "synthetic digest" }).ToArray();
        var canceled = new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = f.B.Id, Sequence = 6,
            Body = "Canceled", CreatedAt = now, Status = QueuedMessageStatus.Canceled, CanceledAt = now };
        var rules = new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = f.B.Id, Sequence = 7,
            Body = "Rules", CreatedAt = now, RulesRefreshKey = "synthetic refresh", HoldUntil = now.AddDays(2) };
        await using (var db = f.Db())
        { db.SessionQueuedMessages.AddRange(existing, move, sent, canceled, rules); db.SessionQueuedMessages.AddRange(extras); await db.SaveChangesAsync(); }
        const string initial = "Explicit recovery input once";
        await f.StartAsync(new(ResumeSessionId: f.A.Id, Prompt: initial)); await f.IdleAsync();
        await using var verify = f.Db();
        var moved = (await verify.SessionQueuedMessages.FindAsync(move.Id))!;
        moved.AgentSessionId.ShouldBe(f.A.Id); moved.Sequence.ShouldBe(9);
        moved.ConversationKey.ShouldBe("fake:recovery");
        moved.HoldUntil!.Value.ShouldBe(move.HoldUntil!.Value, TimeSpan.FromMilliseconds(1));
        (await verify.SessionQueuedMessages.FindAsync(existing.Id))!.Sequence.ShouldBe(8);
        (await verify.SessionQueuedMessages.FindAsync(sent.Id))!.AgentSessionId.ShouldBe(f.B.Id);
        (await verify.SessionQueuedMessages.FindAsync(canceled.Id))!.AgentSessionId.ShouldBe(f.B.Id);
        (await verify.SessionQueuedMessages.FindAsync(rules.Id))!.AgentSessionId.ShouldBe(f.B.Id);
        for (var i = 0; i < extras.Length; i++)
        {
            var preserved = (await verify.SessionQueuedMessages.FindAsync(extras[i].Id))!;
            preserved.AgentSessionId.ShouldBe(f.A.Id); preserved.Sequence.ShouldBe(10 + i);
            preserved.Body.ShouldBe(extras[i].Body); preserved.Origin.ShouldBe(extras[i].Origin);
            preserved.NoteHeader.ShouldBe(extras[i].NoteHeader); preserved.ContentDigest.ShouldBe(extras[i].ContentDigest);
            preserved.HoldUntil!.Value.ShouldBe(extras[i].HoldUntil!.Value, TimeSpan.FromMilliseconds(1));
        }
        adapter.SubmittedBodies.ShouldBe(new[] { initial }, "future-held input must stay pending at launch");
        var ordinary = new[] { existing, move }.Concat(extras).ToArray();
        var ids = ordinary.Select(m => m.Id).ToArray();
        await verify.SessionQueuedMessages.Where(m => ids.Contains(m.Id))
            .ExecuteUpdateAsync(m => m.SetProperty(x => x.HoldUntil, DateTime.UtcNow.AddMinutes(-1)));
        var queue = f.Harness.Provider.GetRequiredService<SessionMessageQueueService>();
        foreach (var message in ordinary) await queue.FlushSessionAsync(f.A.Id, default);
        adapter.SubmittedBodies.ShouldBe(new[] { initial }.Concat(ordinary.Select(m => m.Body)).ToArray());
        verify.ChangeTracker.Clear();
        foreach (var message in ordinary)
        {
            var delivered = (await verify.SessionQueuedMessages.FindAsync(message.Id))!;
            delivered.Status.ShouldBe(QueuedMessageStatus.Sent); delivered.DeliveryAttempts.ShouldBe(1);
            delivered.LastDeliveryBaselineSequence.ShouldNotBeNull();
            (await verify.TranscriptEntries.AnyAsync(e => e.AgentSessionId == f.A.Id && e.Kind == TranscriptKinds.UserPrompt
                && e.Sequence > delivered.LastDeliveryBaselineSequence && e.Text == message.Body)).ShouldBeTrue();
        }
    }
}
