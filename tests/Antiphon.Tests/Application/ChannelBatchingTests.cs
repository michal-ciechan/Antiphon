using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// PR 9's batched delivery (OpenClaw 'collect' at turn end): a contiguous head run of
/// Channel-origin messages from the SAME conversation coalesces into one delivery under the batch
/// markers; UI/System origins and conversation changes break the run; failures revert the whole
/// run; the dispatcher settles every constituent correlation with ONE reply; a whole-turn NO_REPLY
/// settles them with none.
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class ChannelBatchingTests
{
    private const string ConvKey = "telegram:-100777";

    private static AppDbContext CreateContext() => BridgeQueueHarness.CreateContext();

    private static Task<BridgeQueueHarness> CreateHarnessAsync(bool batching = true) =>
        BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            Bridge = new ChannelBridgeSettings { Enabled = true, BatchingEnabled = batching, DebounceWindowMs = 0 },
        });

    private static async Task EnqueueChannelAsync(BridgeQueueHarness h, string body, string conversationKey = ConvKey)
        => await h.Queue.EnqueueAsync(
            h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None,
            origin: QueuedMessageOrigin.Channel, conversationKey: conversationKey);

    [Test]
    public async Task Multiple_pending_channel_messages_coalesce_into_one_batched_delivery_all_rows_sent()
    {
        await using var h = await CreateHarnessAsync();
        await h.MarkWorkingAsync(); // hold messages pending until the turn ends

        await EnqueueChannelAsync(h, "[T] first message");
        await EnqueueChannelAsync(h, "[T] second message");
        await EnqueueChannelAsync(h, "[T] third message");

        await h.InsertTranscriptEntryAsync(Antiphon.SessionRunner.Contracts.TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var body = h.Adapter.SubmittedBodies.ShouldHaveSingleItem();
        body.ShouldBe(
            ChannelPromptFormat.BatchContextMarker + "\n"
            + "[T] first message\n[T] second message\n\n"
            + ChannelPromptFormat.BatchCurrentMarker + "\n"
            + "[T] third message");

        await using var db = CreateContext();
        (await db.SessionQueuedMessages.Where(m => m.AgentSessionId == h.SessionId).ToListAsync())
            .ShouldAllBe(m => m.Status == QueuedMessageStatus.Sent);
    }

    [Test]
    public async Task Batched_reply_fans_out_once_to_the_conversation()
    {
        await using var h = await CreateHarnessAsync();
        await h.MarkWorkingAsync();

        // Two tracked channel messages coalesce into one turn; the one reply settles both.
        var prompt1 = "[Telegram \"Family\" — Mike 10:00] where are my keys";
        var prompt2 = "[Telegram \"Family\" — Mike 10:00] also what's for dinner";
        foreach (var p in new[] { prompt1, prompt2 })
        {
            h.Dispatcher.Track(h.SessionId, new ChannelReplyDispatcher.PendingChannelReply(
                Guid.NewGuid(), "telegram", "-100777", "-100777", p, DateTime.UtcNow));
            await EnqueueChannelAsync(h, p);
        }

        await h.InsertTranscriptEntryAsync(Antiphon.SessionRunner.Contracts.TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        var batchBody = h.Adapter.SubmittedBodies.ShouldHaveSingleItem();

        // The agent answers the batched turn; the dispatcher must consume BOTH correlations and
        // send exactly ONE reply.
        await h.InsertTurnAsync(batchBody, "Keys are on the hook; dinner is pasta.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.ShouldHaveSingleItem().ConversationId.ShouldBe("-100777");
        h.Dispatcher.PendingCount(h.SessionId).ShouldBe(0, "both correlations settled by the one reply");
    }

    [Test]
    public async Task Ui_enqueued_messages_stay_one_per_turn_even_with_batching_on()
    {
        await using var h = await CreateHarnessAsync();
        await h.MarkWorkingAsync();

        await h.Queue.EnqueueAsync(h.SessionId, "operator note one", MessageSendMode.WhenIdle, CancellationToken.None);
        await h.Queue.EnqueueAsync(h.SessionId, "operator note two", MessageSendMode.WhenIdle, CancellationToken.None);

        await h.InsertTranscriptEntryAsync(Antiphon.SessionRunner.Contracts.TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Adapter.SubmittedBodies.ShouldBe(["operator note one"], "UI messages never coalesce");
    }

    [Test]
    public async Task Mixed_origin_queue_preserves_order_ui_message_breaks_the_run()
    {
        await using var h = await CreateHarnessAsync();
        await h.MarkWorkingAsync();

        await EnqueueChannelAsync(h, "[T] chat one");
        await h.Queue.EnqueueAsync(h.SessionId, "operator interjection", MessageSendMode.WhenIdle, CancellationToken.None);
        await EnqueueChannelAsync(h, "[T] chat two");

        await h.InsertTranscriptEntryAsync(Antiphon.SessionRunner.Contracts.TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        // The head run is just the first chat message — the UI message right behind it must NOT be
        // absorbed, and FIFO order must hold across the origins.
        h.Adapter.SubmittedBodies.ShouldBe(["[T] chat one"]);
    }

    [Test]
    public async Task Cross_conversation_pending_messages_deliver_one_per_turn()
    {
        await using var h = await CreateHarnessAsync();
        await h.MarkWorkingAsync();

        await EnqueueChannelAsync(h, "[T] family chat", "telegram:-100777");
        await EnqueueChannelAsync(h, "[T] ops chat", "telegram:-100888");

        await h.InsertTranscriptEntryAsync(Antiphon.SessionRunner.Contracts.TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Adapter.SubmittedBodies.ShouldBe(["[T] family chat"],
            "different conversations never coalesce — one reply cannot honestly answer two chats");
    }

    [Test]
    public async Task Failed_batch_delivery_reverts_all_rows_and_records_one_incident()
    {
        await using var h = await CreateHarnessAsync();
        await h.MarkWorkingAsync();

        await EnqueueChannelAsync(h, "[T] doomed one");
        await EnqueueChannelAsync(h, "[T] doomed two");
        h.Adapter.EchoTypedInputToScreen = false; // wedge the composer

        await h.InsertTranscriptEntryAsync(Antiphon.SessionRunner.Contracts.TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        await using var db = CreateContext();
        var rows = await db.SessionQueuedMessages.Where(m => m.AgentSessionId == h.SessionId).ToListAsync();
        rows.Count.ShouldBe(2);
        rows.ShouldAllBe(m => m.Status == QueuedMessageStatus.Pending, "the WHOLE batch reverts together");
        (await db.AgentIncidents.CountAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed))
            .ShouldBe(1, "one failed delivery = one incident, not one per constituent");
    }

    [Test]
    public async Task Now_mode_failure_still_works_with_null_message_ids()
    {
        await using var h = await CreateHarnessAsync();
        h.Adapter.EchoTypedInputToScreen = false;

        await Should.ThrowAsync<ConflictException>(() =>
            h.Queue.EnqueueAsync(h.SessionId, "send now", MessageSendMode.Now, CancellationToken.None));

        await using var db = CreateContext();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed))
            .ShouldBeTrue("the null-message-ids failure path must keep recording incidents");
    }

    [Test]
    public async Task No_reply_turn_produces_no_channel_reply()
    {
        await using var h = await CreateHarnessAsync();

        var prompt = "[Telegram \"Family\" — Mike 10:00] system note ack test";
        h.Dispatcher.Track(h.SessionId, new ChannelReplyDispatcher.PendingChannelReply(
            Guid.NewGuid(), "telegram", "-100777", "-100777", prompt, DateTime.UtcNow));

        await h.InsertTurnAsync(prompt, "NO_REPLY");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.ShouldBeEmpty("a whole-turn NO_REPLY is the silent-turn contract");
        h.Dispatcher.PendingCount(h.SessionId).ShouldBe(0, "the correlation is consumed, not leaked");
    }

    [Test]
    public async Task No_reply_with_surrounding_prose_is_still_delivered()
    {
        await using var h = await CreateHarnessAsync();

        var prompt = "[Telegram \"Family\" — Mike 10:01] tricky no-reply mention";
        h.Dispatcher.Track(h.SessionId, new ChannelReplyDispatcher.PendingChannelReply(
            Guid.NewGuid(), "telegram", "-100777", "-100777", prompt, DateTime.UtcNow));

        await h.InsertTurnAsync(prompt, "Sure — I'll reply NO_REPLY when there's nothing to say.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.ShouldHaveSingleItem();
    }

    [Test]
    public async Task Batching_disabled_setting_restores_single_message_delivery()
    {
        await using var h = await CreateHarnessAsync(batching: false);
        await h.MarkWorkingAsync();

        await EnqueueChannelAsync(h, "[T] first");
        await EnqueueChannelAsync(h, "[T] second");

        await h.InsertTranscriptEntryAsync(Antiphon.SessionRunner.Contracts.TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Adapter.SubmittedBodies.ShouldBe(["[T] first"], "kill-switch: exact pre-epic one-per-turn behaviour");
    }
}
