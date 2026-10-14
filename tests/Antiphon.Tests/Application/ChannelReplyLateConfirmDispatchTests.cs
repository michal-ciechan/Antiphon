using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1247: the delivery verifier's post-failure grace (<c>GraceConfirmAsync</c>) pulls the
/// runner transcript with NO queue side effects, so the TurnEnd and AssistantText it stores never
/// run turn-end routing. When that same grace then late-confirms a Channel row (Pending to Sent),
/// the reply must be dispatched there and then — not left for some later live event.
///
/// <para>Live miss 2026-10-14 (Family, "Test"): the model answered in 2.8 s, the row was
/// late-confirmed by the verifier, and the reply waited ~88 s for a lagging live AssistantText to
/// re-trigger the dispatcher. None of these tests deliver a live event before the assertion.</para>
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class ChannelReplyLateConfirmDispatchTests
{
    private static Task<BridgeQueueHarness> CreateHarnessAsync() =>
        BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            Outbound = new ChannelOutboundSettings { UnifiedRecoveryEnabled = true },
            AlwaysOn = true,
            Bridge = new ChannelBridgeSettings { Enabled = true, DebounceWindowMs = 0 },
        });

    private static ChannelReplyDispatcher Restarted(BridgeQueueHarness h) => new(
        h.Provider.GetRequiredService<IServiceScopeFactory>(),
        h.Messaging,
        h.Provider.GetRequiredService<IOptions<ChannelBridgeSettings>>(),
        h.Provider.GetRequiredService<TimeProvider>(),
        NullLogger<ChannelReplyDispatcher>.Instance);

    private static async Task<SessionQueuedMessage> RowAsync(Guid id)
    {
        await using var db = BridgeQueueHarness.CreateContext();
        return await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    private static SessionRunnerTranscriptEvent Event(
        Guid sessionId, long seq, string kind, string role, string? text, string? stopReason = null) =>
        new(sessionId, seq, kind, $"late-grace-{seq}", null, DateTimeOffset.UtcNow,
            role, text, null, null, null, null, stopReason);

    /// <summary>One complete turn per (prompt, answer) pair, in order, as the runner holds it.</summary>
    private static SessionRunnerTranscriptDto Turns(Guid sessionId, params (string Prompt, string Answer)[] turns)
    {
        var events = new List<SessionRunnerTranscriptEvent>();
        long seq = 0;
        foreach (var (prompt, answer) in turns)
        {
            events.Add(Event(sessionId, ++seq, TranscriptKinds.UserPrompt, "user", prompt));
            events.Add(Event(sessionId, ++seq, TranscriptKinds.AssistantText, "assistant", answer));
            events.Add(Event(sessionId, ++seq, TranscriptKinds.TurnEnd, "assistant", null,
                TranscriptKinds.StopReasons.EndTurn));
        }

        return new(sessionId, events, seq);
    }

    /// <summary>
    /// The verifier's verdict for a typed Channel row whose UserPrompt the stream has not stored yet.
    /// The grace pull is the ONLY thing that stores the turn; no live event follows.
    /// </summary>
    private static Task VerifierGraceAsync(BridgeQueueHarness h, Guid messageId) =>
        h.Queue.HandleDeliveryFailureAsync(
            h.SessionId, [messageId], DeliveryVerdict.NoTranscriptRecord, CancellationToken.None);

    private static async Task<(string ChatId, Guid MessageId)> SeedTypedChannelRowAsync(
        BridgeQueueHarness h, string prompt)
    {
        var chatId = await h.BindChannelAsync();
        var messageId = await h.SeedPendingMessageAsync(
            prompt,
            deliveryAttempts: 1,
            baselineSequence: await h.CurrentTranscriptMaxSequenceAsync(),
            origin: QueuedMessageOrigin.Channel,
            conversationKey: $"telegram:{chatId}");
        return (chatId, messageId);
    }

    [Test]
    public async Task A_grace_late_confirmed_channel_row_publishes_its_reply_without_a_later_live_event()
    {
        await using var h = await CreateHarnessAsync();
        const string prompt = "[Telegram \"Family\" — Mike 07:00] Test";
        const string answer = "Received: the grace pull alone must publish this reply.";
        var (chatId, messageId) = await SeedTypedChannelRowAsync(h, prompt);
        h.Runner.SetTranscript(Turns(h.SessionId, (prompt, answer)));

        await VerifierGraceAsync(h, messageId);
        await h.DrainOutboundAsync();

        var row = await RowAsync(messageId);
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
        var reply = h.Messaging.SentReplies.ShouldHaveSingleItem(
            "the late-confirmed turn's reply must go out on the grace confirm itself");
        reply.ConversationId.ShouldBe(chatId);
        reply.Text.ShouldBe(answer);
        row.ChannelReplySettledAt.ShouldNotBeNull();
        (await h.Dispatcher.PendingCountAsync(h.SessionId)).ShouldBe(0);
        h.Adapter.Killed.ShouldBeFalse("a verified-late delivery never restarts the session");
        await using var db = BridgeQueueHarness.CreateContext();
        (await db.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId
            && (i.Kind == AgentIncidentKind.DeliveryVerificationFailed
                || i.Kind == AgentIncidentKind.ChannelReplyLost))).ShouldBe(0);
    }

    [Test]
    public async Task A_grace_published_reply_is_not_published_again_by_the_live_path()
    {
        await using var h = await CreateHarnessAsync();
        const string prompt = "[Telegram \"Family\" — Mike 07:01] Grace first, live second";
        const string answer = "Exactly one reply for the grace-then-live order.";
        var (chatId, messageId) = await SeedTypedChannelRowAsync(h, prompt);
        h.Runner.SetTranscript(Turns(h.SessionId, (prompt, answer)));

        await VerifierGraceAsync(h, messageId);
        await h.DrainOutboundAsync();
        h.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe(answer);

        // Every live trigger that can follow: the runtime's own sync, the turn-end and the
        // AssistantText re-dispatch (same OnTurnEndAsync), and a restarted process.
        await h.Runtime.SyncTranscriptAsync(h.SessionId, CancellationToken.None);
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        await Restarted(h).OnTurnEndAsync(h.SessionId, CancellationToken.None);
        await h.DrainOutboundAsync();

        h.Messaging.SentReplies.Where(r => r.ConversationId == chatId).Count().ShouldBe(1);
        (await RowAsync(messageId)).ChannelReplySettledAt.ShouldNotBeNull();
    }

    [Test]
    public async Task A_reply_the_live_path_already_published_is_not_published_again_by_the_grace_confirm()
    {
        await using var h = await CreateHarnessAsync();
        const string prompt = "[Telegram \"Family\" — Mike 07:02] Live first, grace second";
        const string answer = "Exactly one reply for the live-then-grace order.";
        var (chatId, messageId) = await SeedTypedChannelRowAsync(h, prompt);
        h.Runner.SetTranscript(Turns(h.SessionId, (prompt, answer)));

        await h.Runtime.SyncTranscriptAsync(h.SessionId, CancellationToken.None);
        await h.DrainOutboundAsync();
        h.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe(answer);
        (await RowAsync(messageId)).ChannelReplySettledAt.ShouldNotBeNull();

        await VerifierGraceAsync(h, messageId);
        await h.DrainOutboundAsync();

        h.Messaging.SentReplies.Where(r => r.ConversationId == chatId).Count().ShouldBe(1);
        var row = await RowAsync(messageId);
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.ChannelReplySettledAt.ShouldNotBeNull();
    }

    [Test]
    public async Task A_grace_late_confirm_with_no_channel_origin_row_does_not_dispatch()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        const string uiBody = "operator note typed from the UI that the verifier late-confirms";
        const string channelPrompt = "[Telegram \"Family\" — Mike 07:03] owed by an unrelated turn";
        const string channelAnswer = "This reply belongs to a turn the grace confirm did not confirm.";

        // A Ui row (not Channel) is what the grace confirms. The transcript ALSO holds a later,
        // already-answered turn for a Sent Channel correlation, so any dispatcher run would publish.
        var uiId = await h.SeedPendingMessageAsync(
            uiBody, deliveryAttempts: 1, baselineSequence: await h.CurrentTranscriptMaxSequenceAsync());
        var channelId = await h.SeedChannelCorrelationAsync(channelPrompt, $"telegram:{chatId}");
        h.Runner.SetTranscript(Turns(h.SessionId, (uiBody, "noted"), (channelPrompt, channelAnswer)));

        await VerifierGraceAsync(h, uiId);
        await h.DrainOutboundAsync();

        (await RowAsync(uiId)).DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
        h.Messaging.SentReplies.ShouldBeEmpty(
            "a late-confirm that confirmed no Channel row must not run the reply dispatcher");
        (await RowAsync(channelId)).ChannelReplySettledAt.ShouldBeNull();

        // In-test control: the setup IS publishable, so the silence above was the missing dispatch.
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        await h.DrainOutboundAsync();
        h.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe(channelAnswer);
    }
}
