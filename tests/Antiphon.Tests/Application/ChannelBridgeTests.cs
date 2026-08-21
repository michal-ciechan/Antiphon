using Antiphon.Messaging;
using Antiphon.Messaging.Client.Testing;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// The channel bridge loop, offline: inbound <see cref="ChannelMessage"/>s (via the in-memory messaging
/// fake) discover <see cref="ChatChannel"/> rows; a channel bound to an agent routes messages into the
/// agent's session queue; and on turn end the <see cref="ChannelReplyDispatcher"/> matches the turn back
/// to the channel and produces a typed <see cref="ChannelReply"/> to the outbound topic.
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class ChannelBridgeTests
{
    [Test]
    public async Task First_inbound_message_discovers_the_channel_unrouted()
    {
        await using var h = await HarnessAsync();

        var msg = TelegramText(chatId: h.ChatId, "hello there", title: "Family");
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

        // Scoped to this harness's chat id, as the harness itself does: GetAllAsync returns every
        // channel in the shared database, so a bare ShouldHaveSingleItem() also asserts that no
        // other test left one behind — which is not what this test is about.
        var channels = await h.Channels().GetAllAsync(CancellationToken.None);
        var channel = channels.Where(c => c.ExternalId == h.ChatId).ShouldHaveSingleItem();
        channel.Provider.ShouldBe("telegram");
        channel.ExternalId.ShouldBe(h.ChatId);
        channel.Title.ShouldBe("Family");
        channel.AgentId.ShouldBeNull();
        channel.MessageCount.ShouldBe(1);
        channel.LastMessagePreview.ShouldBe("hello there");
        h.Adapter.SentInput.ShouldBeEmpty("unbound channels must not route");
        h.EventBus.PublishedEvents.ShouldContain(e => e.EventName == "ChannelChanged");
    }

    [Test]
    public async Task Bound_channel_routes_the_message_into_the_agent_session()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();

        var msg = TelegramText(h.ChatId, "What's for dinner?", title: "Family", author: "Mike");
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

        h.Adapter.SentInput.ShouldContain("What's for dinner?");
        h.Adapter.SentInput.ShouldContain("Mike", customMessage: "the prompt must carry the author context");
        (await h.Dispatcher.PendingCountAsync(h.SessionId)).ShouldBe(1, "a reply correlation must be tracked");
    }

    [Test]
    public async Task Turn_end_sends_the_agents_answer_down_the_channel()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();

        var msg = TelegramText(h.ChatId, "What's for dinner?", title: "Family");
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);
        var deliveredPrompt = h.Adapter.Inputs[0];

        await h.InsertTurnAsync(deliveredPrompt, "Pasta tonight — Ola already started the sauce.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var reply = h.Messaging.SentReplies.ShouldHaveSingleItem();
        reply.Channel.ShouldBe("telegram");
        reply.ConversationId.ShouldBe(h.ChatId);
        reply.Kind.ShouldBe(ChannelReplyKind.Answer);
        reply.Text.ShouldBe("Pasta tonight — Ola already started the sauce.");
        (await h.Dispatcher.PendingCountAsync(h.SessionId)).ShouldBe(0);
    }

    // Live failure 2026-07-24 (Antiphon-Family, Ola's Apple Music question): Claude wrote the
    // turn as UserPrompt, TurnEnd, AssistantText, TurnEnd — the stop marker BEFORE the text. The
    // dispatch on the first (text-less) TurnEnd consumed the correlations, so when the text
    // arrived there was nothing left to match and the reply never reached the chat.
    [Test]
    public async Task A_turn_whose_stop_marker_precedes_the_text_still_replies()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();

        var msg = TelegramText(h.ChatId, "How do I stop Apple Music autoplaying?", title: "Family");
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

        await h.InsertEntryAsync(TranscriptKinds.UserPrompt, h.Adapter.Inputs[0]);
        await h.InsertEntryAsync(TranscriptKinds.TurnEnd, null, stopReason: "end_turn");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.ShouldBeEmpty();
        (await h.Dispatcher.PendingCountAsync(h.SessionId))
            .ShouldBe(1, "a text-less TurnEnd must leave the correlation pending, not consume it");

        // The reply text lands after the stop marker; its arrival re-triggers dispatch.
        await h.InsertEntryAsync(TranscriptKinds.AssistantText, "Turn off the car's Bluetooth autoplay setting.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.ShouldHaveSingleItem().Text
            .ShouldBe("Turn off the car's Bluetooth autoplay setting.");
        (await h.Dispatcher.PendingCountAsync(h.SessionId)).ShouldBe(0);
    }

    // Live failure 2026-07-29 (AZ Care, first message to the freshly-bound agent): the turn had
    // interim narration between tool calls, then TurnEnd, then the REAL answer one second later,
    // then a second TurnEnd. Dispatch on the first TurnEnd found the narration (non-empty text),
    // consumed the correlations and sent it — so when the answer arrived nothing was pending and it
    // was dropped. Trailing text for an already-dispatched turn must go out as a follow-up reply.
    [Test]
    public async Task Text_arriving_after_an_interim_reply_was_sent_still_reaches_the_chat()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();

        var msg = TelegramText(h.ChatId, "answers Olas question", title: "AZ Care");
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

        await h.InsertEntryAsync(TranscriptKinds.UserPrompt, h.Adapter.Inputs[0]);
        await h.InsertEntryAsync(TranscriptKinds.AssistantText, "I don't see Ola's question — checking the message bus.");
        await h.InsertEntryAsync(TranscriptKinds.TurnEnd, null, stopReason: "end_turn");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.ShouldHaveSingleItem().Text
            .ShouldBe("I don't see Ola's question — checking the message bus.");
        (await h.Dispatcher.PendingCountAsync(h.SessionId)).ShouldBe(0, "the interim dispatch consumed the correlation");

        // The real answer lands after dispatch already consumed the correlation; its arrival
        // re-triggers dispatch, which must deliver it as a follow-up to the same conversation.
        await h.InsertEntryAsync(TranscriptKinds.AssistantText, "Ola's question: stop Apple Music autoplaying — delete the Music app.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        await h.InsertEntryAsync(TranscriptKinds.TurnEnd, null, stopReason: "end_turn");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(2, "the trailing text must be sent, and only once");
        h.Messaging.SentReplies[1].Text
            .ShouldBe("Ola's question: stop Apple Music autoplaying — delete the Music app.");
        h.Messaging.SentReplies[1].ConversationId.ShouldBe(h.ChatId);
    }

    // The follow-up path must not resurrect a finished turn: once a NEW prompt starts the next
    // turn, trailing-text delivery for the old one is closed off (that text would be stale).
    [Test]
    public async Task Follow_up_stops_once_the_next_turn_starts()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();

        var msg = TelegramText(h.ChatId, "first question", title: "AZ Care");
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

        await h.InsertTurnAsync(h.Adapter.Inputs[0], "Answered.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        h.Messaging.SentReplies.Count.ShouldBe(1);

        // Next turn begins (human typed in the terminal); text after it belongs to that turn.
        await h.InsertEntryAsync(TranscriptKinds.UserPrompt, "run the tests please");
        await h.InsertEntryAsync(TranscriptKinds.AssistantText, "All green.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(1, "text after the next prompt must not be sent as a follow-up");
    }

    // CARD-0068 / live miss 2026-08-17: PersistTranscriptAsync (catch-up / reconnect) commits
    // trailing AssistantText and a newer UserPrompt in ONE SaveChanges with identical CreatedAt.
    // DispatchFollowUpAsync used to ask "is PromptSeq still the latest UserPrompt in the session?"
    // and drop the watermark without sending — after that batch there is no later moment at which
    // the text exists without the next prompt. The question it must ask is ExtractTurnResponseAsync's
    // sequence window: PromptSeq < seq < nextPromptSeq, lower-bounded at MaxTextSeq.
    [Test]
    public async Task Trailing_text_still_follow_ups_when_the_next_prompt_landed_in_the_same_batch()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();

        var msg = TelegramText(h.ChatId, "who is coming to the wedding?", title: "Family");
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

        await h.InsertEntryAsync(TranscriptKinds.UserPrompt, h.Adapter.Inputs[0]);
        await h.InsertEntryAsync(TranscriptKinds.AssistantText, "Transcribed. Let me save it properly first.");
        await h.InsertEntryAsync(TranscriptKinds.TurnEnd, null, stopReason: "end_turn");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.ShouldHaveSingleItem().Text
            .ShouldBe("Transcribed. Let me save it properly first.");
        (await h.Dispatcher.PendingCountAsync(h.SessionId)).ShouldBe(0);

        const string realAnswer = "The guest list: Alice, Bob, Carol — 42 people.";
        await h.InsertTranscriptEntriesInOneBatchAsync(
            (TranscriptKinds.AssistantText, realAnswer, null),
            (TranscriptKinds.UserPrompt, "run the tests please", null));
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(2,
            "in-window trailing text must follow-up even though a newer prompt shares the batch");
        h.Messaging.SentReplies[1].Text.ShouldBe(realAnswer);
        h.Messaging.SentReplies[1].ConversationId.ShouldBe(h.ChatId);

        // Text after the newer prompt still must not go out as a follow-up of the settled turn.
        await h.InsertEntryAsync(TranscriptKinds.AssistantText, "All green.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        h.Messaging.SentReplies.Count.ShouldBe(2, "text after the next prompt must not be sent as a follow-up");
    }

    [Test]
    public async Task A_response_ending_in_a_question_is_typed_as_question()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();

        var msg = TelegramText(h.ChatId, "Book the dentist", title: "Family");
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

        await h.InsertTurnAsync(h.Adapter.Inputs[0], "I can do Tuesday 10:00 or Thursday 15:30. Which works?");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.ShouldHaveSingleItem().Kind.ShouldBe(ChannelReplyKind.Question);
    }

    [Test]
    public async Task A_turn_the_bridge_did_not_start_sends_no_reply()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();

        var msg = TelegramText(h.ChatId, "hello", title: "Family");
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

        // A human typed directly into the terminal: the turn's prompt matches no pending correlation.
        await h.InsertTurnAsync("run the tests please", "All green.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.ShouldBeEmpty();
        (await h.Dispatcher.PendingCountAsync(h.SessionId)).ShouldBe(1, "the channel's correlation must survive for ITS turn");
    }

    [Test]
    public async Task Redelivered_message_is_not_routed_twice()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();

        var msg = TelegramText(h.ChatId, "ping", title: "Family");
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None); // Kafka at-least-once redelivery

        h.Adapter.Inputs.Count(i => i.Contains("ping")).ShouldBe(1);
        (await h.Dispatcher.PendingCountAsync(h.SessionId)).ShouldBe(1);
    }

    // CARD-0119: a Slack DM's `D…` conversation id is stable for the life of the (user, bot-user)
    // pair, and UpsertFromInboundAsync looks the row up by (Provider, ExternalId) — so a second,
    // DISTINCT message must reuse the row, not fragment the DM's history across two of them.
    [Test]
    public async Task A_second_distinct_message_on_the_same_conversation_reuses_the_row()
    {
        await using var h = await HarnessAsync();
        // Unique per run: this DM id shares the database with every other test running right now.
        var dmId = $"D{Guid.NewGuid():N}"[..11].ToUpperInvariant();
        // TelegramText mints a fresh ChannelMessageId per call, so the two messages are distinct
        // and the Kafka redelivery-dedup path is NOT what makes this pass.
        ChannelMessage Dm(string text) => TelegramText(dmId, text) with
        {
            Channel = "slack",
            Conversation = new Conversation { Id = dmId, Kind = ConversationKind.Direct, Title = "Mike" },
        };

        await h.Bridge.HandleInboundAsync(Dm("first message"), CancellationToken.None);
        var first = (await h.Channels().GetAllAsync(CancellationToken.None))
            .Where(c => c.Provider == "slack" && c.ExternalId == dmId).ShouldHaveSingleItem();

        await h.Bridge.HandleInboundAsync(Dm("second message"), CancellationToken.None);

        var second = (await h.Channels().GetAllAsync(CancellationToken.None))
            .Where(c => c.Provider == "slack" && c.ExternalId == dmId).ShouldHaveSingleItem();
        second.Id.ShouldBe(first.Id, "one DM is one channel row");
        second.CreatedAt.ShouldBe(first.CreatedAt, "the row must be reused, not recreated");
        second.MessageCount.ShouldBe(2);
        second.Kind.ShouldBe(ChatChannelKind.Direct);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        await db.ChatChannels.Where(c => c.ExternalId == dmId).ExecuteDeleteAsync();
    }

    [Test]
    public async Task Own_bot_messages_are_ignored()
    {
        await using var h = await HarnessAsync();

        var msg = TelegramText(h.ChatId, "echo of our own reply") with
        {
            Author = new Participant { Id = "bot", IsSelf = true },
        };
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

        // The bot's own echo must not create a channel for THIS chat; other tests' channels in the
        // shared database are none of this test's business.
        (await h.Channels().GetAllAsync(CancellationToken.None))
            .ShouldNotContain(c => c.ExternalId == h.ChatId);
    }

    // PR 9: rapid-fire same-sender messages debounce into ONE routed prompt — single truthful
    // envelope header, one line per message, one reply correlation for the whole flush.
    [Test]
    public async Task Rapid_fire_same_sender_inbound_merges_within_window()
    {
        await using var h = await HarnessAsync(debounceWindowMs: 150);
        await h.BindChannelAsync();

        await h.Bridge.HandleInboundAsync(TelegramText(h.ChatId, "line one", title: "Family", author: "Mike"), CancellationToken.None);
        await h.Bridge.HandleInboundAsync(TelegramText(h.ChatId, "line two", title: "Family", author: "Mike"), CancellationToken.None);
        await h.Bridge.HandleInboundAsync(TelegramText(h.ChatId, "line three", title: "Family", author: "Mike"), CancellationToken.None);

        // Real-clock debounce (150ms window): wait for the flush to land.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && h.Adapter.SubmittedBodies.Count == 0)
            await Task.Delay(25);

        var body = h.Adapter.SubmittedBodies.ShouldHaveSingleItem();
        body.ShouldContain("[Telegram \"Family\" — Mike ");
        body.ShouldContain("] line one\nline two\nline three");
        body.Split("[Telegram").Length.ShouldBe(2, "exactly ONE envelope header for the merged flush");
        (await h.Dispatcher.PendingCountAsync(h.SessionId)).ShouldBe(1, "one correlation per flush, not per message");
    }

    // PR 8: channel-routed messages carry the batching metadata (origin + conversation key) and
    // the frozen ChannelPromptFormat envelope; UI enqueues stay Ui-origin with no key.
    [Test]
    public async Task Bridge_enqueues_with_channel_origin_and_conversation_key()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();
        // Park the session as working so the message stays queued (rows are inspectable).
        await h.MarkSessionWorkingAsync();

        var msg = TelegramText(h.ChatId, "check the origin", title: "Family", author: "Mike");
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        row.Origin.ShouldBe(QueuedMessageOrigin.Channel);
        row.ConversationKey.ShouldBe($"telegram:{h.ChatId}");
        row.Body.ShouldContain("[Telegram \"Family\" — Mike ");
        row.Body.ShouldContain("] check the origin");
    }

    [Test]
    public async Task Ui_enqueue_keeps_ui_origin()
    {
        await using var h = await HarnessAsync();
        await h.MarkSessionWorkingAsync();

        var queue = h.Provider.GetRequiredService<SessionMessageQueueService>();
        await queue.EnqueueAsync(h.SessionId, "typed in the web ui", MessageSendMode.WhenIdle, CancellationToken.None);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        row.Origin.ShouldBe(QueuedMessageOrigin.Ui);
        row.ConversationKey.ShouldBeNull();
    }

    [Test]
    [Arguments("Done. The bill is paid.", ChannelReplyKind.Answer)]
    [Arguments("Which day suits you?", ChannelReplyKind.Question)]
    [Arguments("Two options:\n1. Tuesday\n2. Thursday\nWhich one should I book?", ChannelReplyKind.Question)]
    [Arguments("Is it done? Yes — all sorted, nothing left to do.", ChannelReplyKind.Answer)]
    public async Task ClassifyKind_tells_answers_from_questions(string text, ChannelReplyKind expected)
    {
        ChannelReplyDispatcher.ClassifyKind(text).ShouldBe(expected);
        await Task.CompletedTask;
    }

    // ---------- attachments ([[attach: path]] markers → inline Kafka attachments) ----------

    [Test]
    public async Task An_attach_marker_sends_the_file_inline_and_strips_the_marker_line()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();

        var pdf = Path.Combine(Path.GetTempPath(), $"bridge-test-{Guid.NewGuid():n}.pdf");
        var bytes = "%PDF-1.4 fake invoice"u8.ToArray();
        await File.WriteAllBytesAsync(pdf, bytes);
        try
        {
            var msg = TelegramText(h.ChatId, "send me the invoice", title: "AZ Care");
            await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

            await h.InsertTurnAsync(h.Adapter.Inputs[0], $"Here's the invoice 🩵\n[[attach: {pdf}]]");
            await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

            var reply = h.Messaging.SentReplies.ShouldHaveSingleItem();
            reply.Text.ShouldBe("Here's the invoice 🩵");
            var attachment = reply.Attachments.ShouldHaveSingleItem();
            attachment.Kind.ShouldBe(AttachmentKind.File);
            attachment.Name.ShouldBe(Path.GetFileName(pdf));
            attachment.Mime.ShouldBe("application/pdf");
            attachment.Content.ShouldBe(bytes);
        }
        finally
        {
            File.Delete(pdf);
        }
    }

    [Test]
    public async Task A_marker_only_reply_sends_the_document_with_no_text()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();

        var png = Path.Combine(Path.GetTempPath(), $"bridge-test-{Guid.NewGuid():n}.png");
        await File.WriteAllBytesAsync(png, [1, 2, 3]);
        try
        {
            var msg = TelegramText(h.ChatId, "chart please", title: "AZ Care");
            await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

            await h.InsertTurnAsync(h.Adapter.Inputs[0], $"[[attach: {png}]]");
            await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

            var reply = h.Messaging.SentReplies.ShouldHaveSingleItem();
            reply.Text.ShouldBeNull("a marker-only reply has no text — the file IS the reply");
            reply.Attachments.ShouldHaveSingleItem().Kind.ShouldBe(AttachmentKind.Image);
        }
        finally
        {
            File.Delete(png);
        }
    }

    [Test]
    public async Task A_missing_attachment_file_becomes_a_visible_note_not_a_lost_reply()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();

        var msg = TelegramText(h.ChatId, "send the report", title: "AZ Care");
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

        await h.InsertTurnAsync(h.Adapter.Inputs[0], "Here you go:\n[[attach: C:\\nope\\missing.pdf]]");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var reply = h.Messaging.SentReplies.ShouldHaveSingleItem();
        reply.Attachments.ShouldBeEmpty();
        reply.Text.ShouldNotBeNull();
        reply.Text.ShouldContain("attachment not found");
    }

    [Test]
    public async Task An_oversized_attachment_is_skipped_with_a_note()
    {
        await using var h = await HarnessAsync(maxAttachmentBytes: 16);
        await h.BindChannelAsync();

        var big = Path.Combine(Path.GetTempPath(), $"bridge-test-{Guid.NewGuid():n}.bin");
        await File.WriteAllBytesAsync(big, new byte[64]);
        try
        {
            var msg = TelegramText(h.ChatId, "send it", title: "AZ Care");
            await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

            await h.InsertTurnAsync(h.Adapter.Inputs[0], $"Sending.\n[[attach: {big}]]");
            await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

            var reply = h.Messaging.SentReplies.ShouldHaveSingleItem();
            reply.Attachments.ShouldBeEmpty();
            reply.Text.ShouldNotBeNull();
            reply.Text.ShouldContain("over the");
        }
        finally
        {
            File.Delete(big);
        }
    }

    [Test]
    [Arguments("no markers here", 0)]
    [Arguments("[[attach: C:\\a.pdf]]", 1)]
    [Arguments("text\n[[attach: C:\\a.pdf]]\nmore\n[[ATTACH: C:\\b with space.png ]]", 2)]
    [Arguments("inline [[attach: C:\\a.pdf]] not on its own line", 0)]
    public async Task ExtractAttachments_finds_only_whole_line_markers(string text, int expected)
    {
        ChannelContracts.ExtractAttachments(text).AttachmentPaths.Count.ShouldBe(expected);
        await Task.CompletedTask;
    }

    // ---------- inbound attachments (photo/file → saved to agent inbox, referenced by path) ----------

    // Live failure 2026-07-29 (AZ Care): Ola sent her UTR as a bare photo — no caption. The bridge
    // dropped attachment-only messages, so the agent asked her for the UTR she'd just sent.
    [Test]
    public async Task An_attachment_only_message_routes_with_the_saved_file_path()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();

        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG magic
        var msg = TelegramText(h.ChatId, text: null!, title: "AZ Care", author: "Ola") with
        {
            Text = null,
            Attachments =
            [
                new Attachment { Kind = AttachmentKind.Image, ChannelRef = "photo-1", Name = "utr.jpg", Content = bytes },
            ],
        };
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

        var prompt = h.Adapter.SubmittedBodies.ShouldHaveSingleItem();
        prompt.ShouldContain("[photo attached: ");
        prompt.ShouldContain("utr.jpg");
        (await h.Dispatcher.PendingCountAsync(h.SessionId)).ShouldBe(1, "the photo message owes a reply like any other");

        var path = prompt.Split("[photo attached: ")[1].Split(']')[0];
        File.Exists(path).ShouldBeTrue("the bytes must be on disk for the agent to Read");
        (await File.ReadAllBytesAsync(path)).ShouldBe(bytes);
        path.ShouldContain(Path.Combine(".antiphon", "inbox"));
        File.Delete(path);
    }

    [Test]
    public async Task A_captioned_document_routes_with_text_and_file_path()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();

        var bytes = "%PDF-1.4 UTR letter"u8.ToArray();
        var msg = TelegramText(h.ChatId, "here's my UTR letter", title: "AZ Care", author: "Ola") with
        {
            Attachments =
            [
                new Attachment { Kind = AttachmentKind.File, ChannelRef = "doc-1", Name = "utr-letter.pdf", Mime = "application/pdf", Content = bytes },
            ],
        };
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

        var prompt = h.Adapter.SubmittedBodies.ShouldHaveSingleItem();
        prompt.ShouldContain("here's my UTR letter");
        prompt.ShouldContain("[file attached: ");
        var path = prompt.Split("[file attached: ")[1].Split(']')[0];
        (await File.ReadAllBytesAsync(path)).ShouldBe(bytes);
        File.Delete(path);
    }

    [Test]
    public async Task A_metadata_only_attachment_becomes_a_visible_could_not_import_note()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();

        // The gateway couldn't download (over cap / getFile failure): Content is null.
        var msg = TelegramText(h.ChatId, text: null!, title: "AZ Care", author: "Ola") with
        {
            Text = null,
            Attachments = [new Attachment { Kind = AttachmentKind.Image, ChannelRef = "photo-x" }],
        };
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

        var prompt = h.Adapter.SubmittedBodies.ShouldHaveSingleItem();
        prompt.ShouldContain("[photo attached — could not be imported");
    }

    [Test]
    public async Task A_message_with_neither_text_nor_attachments_is_not_routed()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();

        var msg = TelegramText(h.ChatId, text: null!, title: "AZ Care") with { Text = null };
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

        h.Adapter.SubmittedBodies.ShouldBeEmpty();
        (await h.Dispatcher.PendingCountAsync(h.SessionId)).ShouldBe(0);
    }

    [Test]
    public async Task Attachment_file_names_from_the_channel_are_sanitized()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();

        var msg = TelegramText(h.ChatId, text: null!, title: "AZ Care", author: "Ola") with
        {
            Text = null,
            Attachments =
            [
                new Attachment { Kind = AttachmentKind.File, ChannelRef = "evil-1", Name = @"..\..\evil<>.pdf", Content = [1, 2, 3] },
            ],
        };
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

        var prompt = h.Adapter.SubmittedBodies.ShouldHaveSingleItem();
        var path = prompt.Split("[file attached: ")[1].Split(']')[0];
        path.ShouldContain(Path.Combine(".antiphon", "inbox"), customMessage: "traversal in channel names must never escape the inbox");
        Path.GetFileName(path).ShouldNotContain("..");
        File.Exists(path).ShouldBeTrue();
        File.Delete(path);
    }

    // ---------- harness ----------

    private static ChannelMessage TelegramText(
        string chatId, string text, string? title = null, string? author = null) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Channel = "telegram",
        ChannelMessageId = Guid.NewGuid().ToString("n")[..12],
        Conversation = new Conversation { Id = chatId, Kind = ConversationKind.Group, Title = title },
        Author = new Participant { Id = "1001", DisplayName = author ?? "Tester" },
        Timestamp = DateTimeOffset.UtcNow,
        Text = text,
        ReplyHandle = chatId,
        Raw = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone(),
    };

    private static async Task<Harness> HarnessAsync(int debounceWindowMs = 0, long maxAttachmentBytes = 14 * 1024 * 1024)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
        var eventBus = new MockEventBus();
        var messaging = new FakeAntiphonMessagingClient();
        services.AddSingleton(eventBus);
        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings()));
        // DebounceWindowMs 0 = passthrough: these tests assert synchronous routing; the debounce
        // behaviour has its own suites (ChannelInboundDebouncerTests + the rapid-fire bridge tests).
        services.AddSingleton(Options.Create(new ChannelBridgeSettings
        {
            Enabled = true,
            DebounceWindowMs = debounceWindowMs,
            MaxAttachmentBytes = maxAttachmentBytes,
        }));
        services.AddSingleton<ChannelInboundDebouncer>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddScoped<ChatChannelService>();
        services.AddSingleton(sp => new ChannelReplyDispatcher(
            sp.GetRequiredService<IServiceScopeFactory>(),
            messaging,
            sp.GetRequiredService<IOptions<ChannelBridgeSettings>>(),
            sp.GetRequiredService<TimeProvider>(),
            NullLogger<ChannelReplyDispatcher>.Instance));
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var chatId = $"-100{Random.Shared.Next(100000, 999999)}";
        var now = DateTime.UtcNow;

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Agents.Add(new Agent
            {
                Id = agentId,
                Name = $"BridgeTestAgent-{agentId:N}"[..30],
                Slug = $"bridge-test-{agentId:N}"[..20],
                WorkingDirectory = Path.GetTempPath(),
                Status = AgentStatus.Running,
                PersistentSessionId = sessionId.ToString("D"),
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                CardId = null,
                DefinitionName = "fake",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = Path.GetTempPath(),
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            });
            await db.SaveChangesAsync();
        }

        var runtime = provider.GetRequiredService<AgentSessionRuntime>();
        var adapter = new FakeAgentProtocolAdapter();
        runtime.Register(sessionId, adapter);

        var dispatcher = provider.GetRequiredService<ChannelReplyDispatcher>();
        var bridge = new ChannelBridgeService(
            messaging,
            provider.GetRequiredService<SessionMessageQueueService>(),
            provider.GetRequiredService<ChannelInboundDebouncer>(),
            eventBus,
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IOptions<ChannelBridgeSettings>>(),
            provider.GetRequiredService<TimeProvider>(),
            NullLogger<ChannelBridgeService>.Instance);

        return new Harness(provider, bridge, dispatcher, messaging, adapter, eventBus, sessionId, agentId, chatId);
    }

    private sealed record Harness(
        ServiceProvider Provider,
        ChannelBridgeService Bridge,
        ChannelReplyDispatcher Dispatcher,
        FakeAntiphonMessagingClient Messaging,
        FakeAgentProtocolAdapter Adapter,
        MockEventBus EventBus,
        Guid SessionId,
        Guid AgentId,
        string ChatId) : IAsyncDisposable
    {
        public ChatChannelService Channels()
        {
            var scope = Provider.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ChatChannelService>();
        }

        public async Task BindChannelAsync()
        {
            // Discover the channel with a throwaway message, then bind it to the test agent.
            await Bridge.HandleInboundAsync(
                new ChannelMessage
                {
                    Id = Guid.NewGuid().ToString("n"),
                    Channel = "telegram",
                    ChannelMessageId = "seed",
                    Conversation = new Conversation { Id = ChatId, Kind = ConversationKind.Group, Title = "Family" },
                    Author = new Participant { Id = "1001" },
                    Timestamp = DateTimeOffset.UtcNow,
                    Text = null, // no text → recorded but never routed
                    ReplyHandle = ChatId,
                    Raw = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone(),
                },
                CancellationToken.None);

            await using var scope = Provider.CreateAsyncScope();
            var channels = scope.ServiceProvider.GetRequiredService<ChatChannelService>();
            var channel = (await channels.GetAllAsync(CancellationToken.None))
                .Single(c => c.ExternalId == ChatId);
            await channels.UpdateAsync(
                channel.Id, new UpdateChatChannelRequest(AgentId: AgentId), CancellationToken.None);
        }

        public async Task MarkSessionWorkingAsync()
        {
            await using var scope = Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var baseSeq = (await db.TranscriptEntries
                .Where(t => t.AgentSessionId == SessionId)
                .MaxAsync(t => (long?)t.Sequence)) ?? 0;
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = SessionId, Sequence = baseSeq + 1,
                Kind = TranscriptKinds.AssistantText, Text = "working", CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        public async Task InsertEntryAsync(string kind, string? text, string? stopReason = null)
        {
            await using var scope = Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var baseSeq = (await db.TranscriptEntries
                .Where(t => t.AgentSessionId == SessionId)
                .MaxAsync(t => (long?)t.Sequence)) ?? 0;
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = SessionId, Sequence = baseSeq + 1,
                Kind = kind, Text = text, StopReason = stopReason, CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        /// <summary>
        /// PersistTranscriptAsync's batch: consecutive sequences, one CreatedAt, one SaveChanges.
        /// Do not use <see cref="InsertEntryAsync"/> here — that saves per row and would invent a
        /// gap the production catch-up path does not have.
        /// </summary>
        public async Task InsertTranscriptEntriesInOneBatchAsync(
            params (string Kind, string? Text, string? StopReason)[] entries)
        {
            if (entries.Length == 0)
                return;

            await using var scope = Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var baseSeq = (await db.TranscriptEntries
                .Where(t => t.AgentSessionId == SessionId)
                .MaxAsync(t => (long?)t.Sequence)) ?? 0;
            var now = DateTime.UtcNow;
            for (var i = 0; i < entries.Length; i++)
            {
                var (kind, text, stopReason) = entries[i];
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = SessionId,
                    Sequence = baseSeq + i + 1,
                    Kind = kind,
                    Text = text,
                    StopReason = stopReason,
                    CreatedAt = now,
                });
            }
            await db.SaveChangesAsync();
        }

        public async Task InsertTurnAsync(string prompt, string response)
        {
            await using var scope = Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var baseSeq = (await db.TranscriptEntries
                .Where(t => t.AgentSessionId == SessionId)
                .MaxAsync(t => (long?)t.Sequence)) ?? 0;
            var now = DateTime.UtcNow;
            db.TranscriptEntries.AddRange(
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = SessionId, Sequence = baseSeq + 1,
                    Kind = TranscriptKinds.UserPrompt, Text = prompt, CreatedAt = now,
                },
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = SessionId, Sequence = baseSeq + 2,
                    Kind = TranscriptKinds.AssistantText, Text = response, CreatedAt = now,
                },
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = SessionId, Sequence = baseSeq + 3,
                    Kind = TranscriptKinds.TurnEnd, StopReason = "end_turn", CreatedAt = now,
                });
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
            {
                await db.ChatChannels.Where(c => c.ExternalId == ChatId).ExecuteDeleteAsync();
                await db.AgentSessions.Where(s => s.Id == SessionId).ExecuteDeleteAsync();
                await db.Agents.Where(a => a.Id == AgentId).ExecuteDeleteAsync();
            }
            await Provider.DisposeAsync();
        }
    }
}
