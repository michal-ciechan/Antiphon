using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class StandingSessionQueueSwitchTests
{
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
