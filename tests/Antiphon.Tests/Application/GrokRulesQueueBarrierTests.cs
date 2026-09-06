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
    [Arguments("flush")]
    [Arguments("now")]
    [Arguments("send_now")]
    [Arguments("expired_hold")]
    public async Task Closed_rules_barrier_holds_ordinary_input_on_each_delivery_entry_point(string entryPoint)
    {
        await using var h = await BridgeQueueHarness.CreateAsync();
        await using var db = BridgeQueueHarness.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
        session.AgentKind = AgentKind.Grok;
        session.GrokRulesState = GrokRulesState.Pending;
        await db.SaveChangesAsync();
        var id = await h.SeedPendingMessageAsync("ordinary task goal");
        if (entryPoint == "expired_hold")
            await db.SessionQueuedMessages.Where(m => m.Id == id).ExecuteUpdateAsync(s =>
                s.SetProperty(m => m.HoldUntil, DateTime.UtcNow.AddMinutes(-1)));
        Exception? failure = null;
        try
        {
            if (entryPoint == "now") await h.Queue.EnqueueAsync(h.SessionId, "urgent task goal", MessageSendMode.Now, CancellationToken.None);
            else if (entryPoint == "send_now") await h.Queue.SendNowAsync(h.SessionId, id, CancellationToken.None);
            else await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        }
        catch (Exception ex) { failure = ex; }
        h.Adapter.SubmittedBodies.ShouldBeEmpty($"{entryPoint} must not bypass initialization");
        var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
        row.Status.ShouldBe(QueuedMessageStatus.Pending);
        row.Body.ShouldBe("ordinary task goal");
        row.DeliveryAttempts.ShouldBe(0);
        if (entryPoint is "now" or "send_now")
            failure.ShouldBeOfType<ConflictException>().Code.ShouldBe("grok_rules_initialization_pending");
        else failure.ShouldBeNull();
    }
}
