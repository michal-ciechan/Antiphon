using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
public sealed class GrokRulesChannelTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task Internal_refresh_cannot_publish_even_with_channel_correlation_or_system_text_enabled(bool late, bool machine)
    {
        await using var h = await BridgeQueueHarness.CreateAsync(new() { Bridge = new ChannelBridgeSettings {
            Enabled = true, DebounceWindowMs = 0, MachineTurnTextOrigins = [QueuedMessageOrigin.System] } });
        var chat = await h.BindChannelAsync();
        if (machine)
        {
            await h.SeedChannelCorrelationAsync("genuine channel request", $"telegram:{chat}");
            await h.InsertTurnAsync("genuine channel request", "genuine answer");
            await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
            h.Messaging.SentReplies.Count.ShouldBe(1);
        }
        var id = Guid.NewGuid();
        var body = GrokRulesRefreshService.Header(id) + "\nread standing rules";
        await using var db = BridgeQueueHarness.CreateContext();
        db.SessionQueuedMessages.Add(new SessionQueuedMessage { Id = id, AgentSessionId = h.SessionId,
            Sequence = 2, Origin = QueuedMessageOrigin.System, Status = QueuedMessageStatus.Sent,
            CreatedAt = DateTime.UtcNow, SentAt = DateTime.UtcNow, DeliveryAttempts = 1,
            RulesRefreshKey = "launch:" + Guid.NewGuid().ToString("N"), Body = body });
        await db.SaveChangesAsync();
        if (!machine) await h.SeedChannelCorrelationAsync(body, $"telegram:{chat}");
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, body);
        if (!late) await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "ANTIPHON_RULES_ACK internal report-like prose");
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        if (late)
        {
            await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "ANTIPHON_RULES_ACK internal report-like prose");
            await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        }
        h.Messaging.SentReplies.Count.ShouldBe(machine ? 1 : 0, "internal rules text must never become a channel reply");
        (await db.AgentIncidents.CountAsync(i => i.SessionId == h.SessionId)).ShouldBe(0);
        await h.SeedChannelCorrelationAsync("later genuine task", $"telegram:{chat}");
        await h.InsertTurnAsync("later genuine task", "later genuine answer");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        h.Messaging.SentReplies.Last().Text.ShouldBe("later genuine answer");
    }
}
