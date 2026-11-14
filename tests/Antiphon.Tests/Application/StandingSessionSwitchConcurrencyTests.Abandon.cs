using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Agents;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class StandingSessionSwitchConcurrencyTests
{
    [Test]
    [Arguments(SessionStatus.Stopped)]
    [Arguments(SessionStatus.Failed)]
    public async Task Operator_abandon_of_unstartable_composerless_input_unblocks_history_switch(SessionStatus status)
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var f = new StandingRecoveryFixture(adapter);
        await f.SeedAsync();
        var row = SentAttempt(f.B.Id);
        var other = SentAttempt(f.A.Id);
        await using (var db = f.Db())
        {
            var source = (await db.AgentSessions.FindAsync(f.B.Id))!;
            source.Status = status;
            source.AgentKind = AgentKind.Grok;
            db.SessionQueuedMessages.AddRange(row, other);
            await db.SaveChangesAsync();
        }
        var refusal = await Should.ThrowAsync<ConflictException>(() => f.StartAsync(new(ResumeSessionId: f.A.Id)));
        refusal.Code.ShouldBe("standing_resume_delivery_pending");
        refusal.Message.ShouldContain(row.Id.ToString());
        refusal.Message.ShouldNotContain(other.Id.ToString());
        refusal.Message.ShouldContain($"GET /api/sessions/{f.B.Id}/messages");
        refusal.Message.ShouldContain("/abandon");
        var queue = f.Harness.Provider.GetRequiredService<SessionMessageQueueService>();
        var visible = (await queue.GetQueueAsync(f.B.Id, default)).Messages.ShouldHaveSingleItem();
        visible.Id.ShouldBe(row.Id);
        visible.DeliveryUnconfirmed.ShouldBeTrue();
        await queue.AbandonAsync(f.B.Id, row.Id, "History is unavailable; give up waiting for this input", "test operator", default);
        await f.StartAsync(new(ResumeSessionId: f.A.Id));
        await f.IdleAsync();
        adapter.StartedSessionId.ShouldBe(f.A.Id);
        adapter.Inputs.ShouldBeEmpty();
        await using var verify = f.Db();
        (await verify.SessionQueuedMessages.FindAsync(row.Id))!.Status.ShouldBe(QueuedMessageStatus.Canceled);
        (await verify.SessionQueuedMessages.FindAsync(other.Id))!.Status.ShouldBe(QueuedMessageStatus.Sent);
    }

    [Test]
    public async Task Zero_attempt_sent_row_does_not_block_switch()
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var f = new StandingRecoveryFixture(adapter);
        await f.SeedAsync();
        var row = SentAttempt(f.B.Id);
        row.DeliveryAttempts = 0;
        await using (var db = f.Db()) { db.SessionQueuedMessages.Add(row); await db.SaveChangesAsync(); }
        await f.StartAsync(new(ResumeSessionId: f.A.Id));
        await f.IdleAsync();
        adapter.StartedSessionId.ShouldBe(f.A.Id);
    }
}
