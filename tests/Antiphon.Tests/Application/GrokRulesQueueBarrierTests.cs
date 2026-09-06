using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
public sealed class GrokRulesQueueBarrierTests
{
    [Test]
    [Arguments("flush", QueuedMessageOrigin.Ui)]
    [Arguments("flush", QueuedMessageOrigin.Channel)]
    [Arguments("flush", QueuedMessageOrigin.System)]
    [Arguments("flush", QueuedMessageOrigin.Delegation)]
    [Arguments("flush", QueuedMessageOrigin.Check)]
    [Arguments("flush", QueuedMessageOrigin.Supervision)]
    [Arguments("flush", QueuedMessageOrigin.Scheduled)]
    [Arguments("now", QueuedMessageOrigin.Ui)]
    [Arguments("now", QueuedMessageOrigin.Channel)]
    [Arguments("now", QueuedMessageOrigin.System)]
    [Arguments("now", QueuedMessageOrigin.Delegation)]
    [Arguments("now", QueuedMessageOrigin.Check)]
    [Arguments("now", QueuedMessageOrigin.Supervision)]
    [Arguments("now", QueuedMessageOrigin.Scheduled)]
    [Arguments("send_now", QueuedMessageOrigin.Ui)]
    [Arguments("send_now", QueuedMessageOrigin.Channel)]
    [Arguments("send_now", QueuedMessageOrigin.System)]
    [Arguments("send_now", QueuedMessageOrigin.Delegation)]
    [Arguments("send_now", QueuedMessageOrigin.Check)]
    [Arguments("send_now", QueuedMessageOrigin.Supervision)]
    [Arguments("send_now", QueuedMessageOrigin.Scheduled)]
    [Arguments("expired_hold", QueuedMessageOrigin.Ui)]
    [Arguments("expired_hold", QueuedMessageOrigin.Channel)]
    [Arguments("expired_hold", QueuedMessageOrigin.System)]
    [Arguments("expired_hold", QueuedMessageOrigin.Delegation)]
    [Arguments("expired_hold", QueuedMessageOrigin.Check)]
    [Arguments("expired_hold", QueuedMessageOrigin.Supervision)]
    [Arguments("expired_hold", QueuedMessageOrigin.Scheduled)]
    public async Task Closed_rules_barrier_holds_ordinary_input_on_each_delivery_entry_point(string entryPoint, QueuedMessageOrigin origin)
    {
        await using var h = await BridgeQueueHarness.CreateAsync();
        await using var db = BridgeQueueHarness.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
        session.AgentKind = AgentKind.Grok;
        session.GrokRulesState = GrokRulesState.Pending;
        await db.SaveChangesAsync();
        var id = await h.SeedPendingMessageAsync("ordinary task goal", origin: origin);
        if (entryPoint == "expired_hold")
            await db.SessionQueuedMessages.Where(m => m.Id == id).ExecuteUpdateAsync(s =>
                s.SetProperty(m => m.HoldUntil, DateTime.UtcNow.AddMinutes(-1)));
        Exception? failure = null;
        try
        {
            if (entryPoint == "now") await h.Queue.EnqueueAsync(h.SessionId, "urgent task goal", MessageSendMode.Now, CancellationToken.None, origin: origin);
            else if (entryPoint == "send_now") await h.Queue.SendNowAsync(h.SessionId, id, CancellationToken.None);
            else await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        }
        catch (Exception ex) { failure = ex; }
        h.Adapter.SubmittedBodies.ShouldBeEmpty($"{entryPoint} must not bypass initialization");
        var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
        // Boot/expired-hold flush cancels ineligible supervision compacts by their existing
        // cancel-not-strand contract; human/task messages retain their pending work.
        row.Status.ShouldBe(origin == QueuedMessageOrigin.Supervision && entryPoint is "flush" or "expired_hold"
            ? QueuedMessageStatus.Canceled : QueuedMessageStatus.Pending);
        row.Body.ShouldBe("ordinary task goal");
        row.DeliveryAttempts.ShouldBe(0);
        if (entryPoint is "now" or "send_now")
            failure.ShouldBeOfType<ConflictException>().Code.ShouldBe("grok_rules_initialization_pending");
        else failure.ShouldBeNull();
    }
}
