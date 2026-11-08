using System.Text.Json.Nodes;
using Antiphon.Messaging.Telegram;
using Antiphon.Messaging.Tests.FakeTelegram;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Messaging.Tests;

/// <summary>
/// Integration tests for <see cref="TelegramChannelAdapter"/> against the <see cref="FakeTelegramServer"/>.
/// These are the "rest of the tests" that run entirely offline against the (conformance-verified) fake.
/// </summary>
public sealed class TelegramChannelAdapterTests
{
    private static TelegramSettings Settings(FakeTelegramServer fake) => new()
    {
        ApiBaseUrl = fake.BaseUrl,
        BotToken = fake.BotToken,
        BotUsername = "school_revision_bot",
        LongPollTimeoutSeconds = 0,
    };

    private static TelegramChannelAdapter NewAdapter(FakeTelegramServer fake) =>
        new(new HttpClient(), Settings(fake), NullLogger<TelegramChannelAdapter>.Instance);

    [Test]
    public async Task Normalizes_inbound_text_message()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        fake.EnqueueTextMessage(chatId: 555, fromId: 999, text: "hello bot", username: "alice", firstName: "Alice");

        var msg = await FirstMessageAsync(NewAdapter(fake));

        msg.ShouldNotBeNull();
        msg!.Channel.ShouldBe("telegram");
        msg.Text.ShouldBe("hello bot");
        msg.Conversation.Id.ShouldBe("555");
        msg.Conversation.Kind.ShouldBe(ConversationKind.Direct);
        msg.Author.Id.ShouldBe("999");
        msg.Author.Username.ShouldBe("alice");
        msg.ReplyHandle.ShouldBe("555");
        fake.WebhookDeleted.ShouldBeTrue();   // getUpdates path clears any webhook first
    }

    [Test]
    public async Task Send_delivers_to_telegram_and_returns_message_id()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();

        var result = await NewAdapter(fake).SendAsync(
            new ChannelReply { Channel = "telegram", ConversationId = "555", Text = "hi back" },
            CancellationToken.None);

        result.Ok.ShouldBeTrue();
        result.ChannelMessageId.ShouldNotBeNullOrEmpty();
        var sent = fake.SentMessages.ShouldHaveSingleItem();
        sent.ChatId.ShouldBe("555");
        sent.Text.ShouldBe("hi back");
    }

    [Test]
    public async Task Send_renders_markdown_to_telegram_html()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();

        var result = await NewAdapter(fake).SendAsync(
            new ChannelReply { Channel = "telegram", ConversationId = "555", Text = "**bold** and `code`" },
            CancellationToken.None);

        result.Ok.ShouldBeTrue();
        var sent = fake.SentMessages.ShouldHaveSingleItem();
        sent.Text.ShouldBe("<b>bold</b> and <code>code</code>");
        sent.RawBody!["parse_mode"]!.GetValue<string>().ShouldBe("HTML");
    }

    [Test]
    public async Task Plain_formatting_mode_sends_raw_text_without_parse_mode()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        var settings = Settings(fake);
        settings.Formatting = "Plain";
        var adapter = new TelegramChannelAdapter(new HttpClient(), settings, NullLogger<TelegramChannelAdapter>.Instance);

        var result = await adapter.SendAsync(
            new ChannelReply { Channel = "telegram", ConversationId = "555", Text = "**not converted**" },
            CancellationToken.None);

        result.Ok.ShouldBeTrue();
        var sent = fake.SentMessages.ShouldHaveSingleItem();
        sent.Text.ShouldBe("**not converted**");
        sent.RawBody!.ContainsKey("parse_mode").ShouldBeFalse();
    }

    [Test]
    public async Task Rejected_entities_fall_back_to_a_plain_resend()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        // Telegram's rejection for malformed markup — the adapter must not lose the reply over it.
        fake.EnqueueSendBadRequest("Bad Request: can't parse entities: unexpected end tag at byte offset 7");

        var result = await NewAdapter(fake).SendAsync(
            new ChannelReply { Channel = "telegram", ConversationId = "555", Text = "**tricky** input" },
            CancellationToken.None);

        result.Ok.ShouldBeTrue("the plain resend must deliver");
        var sent = fake.SentMessages.ShouldHaveSingleItem();
        sent.Text.ShouldBe("**tricky** input", "the fallback sends the ORIGINAL text, unconverted");
        sent.RawBody!.ContainsKey("parse_mode").ShouldBeFalse();
    }

    [Test]
    public async Task Raw_override_parse_mode_suppresses_conversion()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        using var overrides = System.Text.Json.JsonDocument.Parse("""{"parse_mode":"MarkdownV2"}""");

        var result = await NewAdapter(fake).SendAsync(
            new ChannelReply
            {
                Channel = "telegram", ConversationId = "555",
                Text = "*already MarkdownV2*", RawOverrides = overrides.RootElement.Clone(),
            },
            CancellationToken.None);

        result.Ok.ShouldBeTrue();
        var sent = fake.SentMessages.ShouldHaveSingleItem();
        sent.Text.ShouldBe("*already MarkdownV2*");
        sent.RawBody!["parse_mode"]!.GetValue<string>().ShouldBe("MarkdownV2");
    }

    [Test]
    public async Task Send_to_invalid_chat_returns_failed()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();

        var result = await NewAdapter(fake).SendAsync(
            new ChannelReply { Channel = "telegram", ConversationId = "0", Text = "nope" },
            CancellationToken.None);

        result.Ok.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrEmpty();
    }

    // ---------- attachments (sendDocument) ----------

    [Test]
    public async Task Inline_content_attachment_uploads_as_multipart_document()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        var bytes = "%PDF-1.4 tiny invoice"u8.ToArray();

        var result = await NewAdapter(fake).SendAsync(
            new ChannelReply
            {
                Channel = "telegram", ConversationId = "555", Text = "Here's the invoice",
                Attachments =
                [
                    new OutboundAttachment
                    {
                        Kind = AttachmentKind.File, Content = bytes,
                        Name = "invoice.pdf", Mime = "application/pdf",
                    },
                ],
            },
            CancellationToken.None);

        result.Ok.ShouldBeTrue();
        fake.SentMessages.ShouldHaveSingleItem().Text.ShouldBe("Here's the invoice");
        var doc = fake.SentDocuments.ShouldHaveSingleItem();
        doc.ChatId.ShouldBe("555");
        doc.FileName.ShouldBe("invoice.pdf");
        doc.Mime.ShouldBe("application/pdf");
        doc.Bytes.ShouldBe(bytes);
    }

    [Test]
    public async Task Attachment_only_reply_sends_no_text_message()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();

        var result = await NewAdapter(fake).SendAsync(
            new ChannelReply
            {
                Channel = "telegram", ConversationId = "555",
                Attachments =
                [
                    new OutboundAttachment { Kind = AttachmentKind.File, Content = [1, 2, 3], Name = "a.bin" },
                ],
            },
            CancellationToken.None);

        result.Ok.ShouldBeTrue();
        fake.SentMessages.ShouldBeEmpty("no text → no sendMessage bubble");
        fake.SentDocuments.ShouldHaveSingleItem().FileName.ShouldBe("a.bin");
    }

    [Test]
    public async Task Source_attachment_without_content_goes_as_json_string_document()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();

        var result = await NewAdapter(fake).SendAsync(
            new ChannelReply
            {
                Channel = "telegram", ConversationId = "555",
                Attachments =
                [
                    new OutboundAttachment
                    {
                        Kind = AttachmentKind.File, Source = "https://example.com/report.pdf",
                        Caption = "the report",
                    },
                ],
            },
            CancellationToken.None);

        result.Ok.ShouldBeTrue();
        var doc = fake.SentDocuments.ShouldHaveSingleItem();
        doc.Source.ShouldBe("https://example.com/report.pdf");
        doc.Caption.ShouldBe("the report");
        doc.Bytes.ShouldBeNull();
    }

    [Test]
    public async Task Multiple_attachments_send_one_document_each_in_order()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();

        var result = await NewAdapter(fake).SendAsync(
            new ChannelReply
            {
                Channel = "telegram", ConversationId = "555", Text = "two files",
                Attachments =
                [
                    new OutboundAttachment { Kind = AttachmentKind.File, Content = [1], Name = "one.txt" },
                    new OutboundAttachment { Kind = AttachmentKind.File, Content = [2], Name = "two.txt" },
                ],
            },
            CancellationToken.None);

        result.Ok.ShouldBeTrue();
        fake.SentDocuments.Select(d => d.FileName).ShouldBe(["one.txt", "two.txt"]);
    }

    [Test]
    public async Task Document_send_retries_a_rate_limit_then_delivers()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        // No text on the reply, so the enqueued send fault hits the sendDocument call.
        fake.EnqueueSendRateLimit(retryAfterSeconds: 0);

        var result = await NewAdapter(fake).SendAsync(
            new ChannelReply
            {
                Channel = "telegram", ConversationId = "555",
                Attachments =
                [
                    new OutboundAttachment { Kind = AttachmentKind.File, Content = [9], Name = "retry.bin" },
                ],
            },
            CancellationToken.None);

        result.Ok.ShouldBeTrue("the 429 is transient — the retry must deliver");
        fake.SentDocuments.ShouldHaveSingleItem().FileName.ShouldBe("retry.bin");
    }

    [Test]
    public async Task Attachment_with_neither_content_nor_source_fails_cleanly()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();

        var result = await NewAdapter(fake).SendAsync(
            new ChannelReply
            {
                Channel = "telegram", ConversationId = "555",
                Attachments = [new OutboundAttachment { Kind = AttachmentKind.File }],
            },
            CancellationToken.None);

        result.Ok.ShouldBeFalse();
        result.Error.ShouldContain("neither Content nor Source");
        fake.SentDocuments.ShouldBeEmpty();
    }

    // ---------- inbound attachments (getFile + download hydration) ----------

    [Test]
    public async Task Inbound_photo_bytes_are_downloaded_and_inlined()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic
        fake.RegisterFile("photo-1", bytes, "photos/utr.jpg");
        fake.EnqueuePhotoMessage(chatId: 555, fromId: 999, fileId: "photo-1", fileSize: bytes.Length);

        var msg = await FirstMessageAsync(NewAdapter(fake));

        msg.ShouldNotBeNull();
        msg!.Text.ShouldBeNull("a bare photo has no text");
        var att = msg.Attachments.ShouldHaveSingleItem();
        att.Kind.ShouldBe(AttachmentKind.Image);
        att.Content.ShouldBe(bytes);
        att.Name.ShouldBe("utr.jpg", "the name falls back to the resolved file_path's file name");
    }

    [Test]
    public async Task Inbound_document_with_caption_keeps_text_and_inlines_bytes()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        var bytes = "%PDF-1.4 inbound"u8.ToArray();
        fake.RegisterFile("doc-1", bytes);
        fake.EnqueueDocumentMessage(chatId: 555, fromId: 999, fileId: "doc-1", fileName: "utr-letter.pdf", fileSize: bytes.Length, caption: "here's my UTR");

        var msg = await FirstMessageAsync(NewAdapter(fake));

        msg.ShouldNotBeNull();
        msg!.Text.ShouldBe("here's my UTR");
        var att = msg.Attachments.ShouldHaveSingleItem();
        att.Kind.ShouldBe(AttachmentKind.File);
        att.Name.ShouldBe("utr-letter.pdf");
        att.Mime.ShouldBe("application/pdf");
        att.Content.ShouldBe(bytes);
    }

    [Test]
    public async Task Oversized_inbound_attachment_keeps_metadata_only()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        var settings = Settings(fake);
        settings.MaxInlineAttachmentBytes = 2;   // tiny cap — the 3-byte file must not download
        var adapter = new TelegramChannelAdapter(new HttpClient(), settings, NullLogger<TelegramChannelAdapter>.Instance);
        fake.RegisterFile("big-1", [1, 2, 3]);
        fake.EnqueueDocumentMessage(chatId: 555, fromId: 999, fileId: "big-1", fileName: "big.bin", fileSize: 3);

        var msg = await FirstMessageAsync(adapter);

        var att = msg!.Attachments.ShouldHaveSingleItem();
        att.Content.ShouldBeNull("over-cap files must pass through metadata-only");
        att.ChannelRef.ShouldBe("big-1");
    }

    [Test]
    public async Task Failed_getFile_never_loses_the_message()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        // No RegisterFile — getFile returns Telegram's invalid-file_id 400.
        fake.EnqueuePhotoMessage(chatId: 555, fromId: 999, fileId: "ghost", fileSize: 3, caption: "look at this");

        var msg = await FirstMessageAsync(NewAdapter(fake));

        msg.ShouldNotBeNull("a broken download must never drop the inbound message");
        msg!.Text.ShouldBe("look at this");
        msg.Attachments.ShouldHaveSingleItem().Content.ShouldBeNull();
    }

    // ---------- over-cap inbound attachments (CARD-1799) ----------

    // The 2026-11-08 Family file: 18,430,415 bytes against the default 14 MiB cap.
    private const int FamilyPptxBytes = 18_430_415;

    [Test]
    public async Task Over_cap_document_is_marked_with_the_limit_and_never_fetched()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        fake.EnqueueDocumentMessage(chatId: 555, fromId: 999, fileId: "pptx-1", fileName: "Homework.pptx",
            mime: "application/vnd.openxmlformats-officedocument.presentationml.presentation", fileSize: FamilyPptxBytes);

        var msg = await FirstMessageAsync(NewAdapter(fake));

        var att = msg!.Attachments.ShouldHaveSingleItem();
        att.Content.ShouldBeNull();
        att.InlineLimitBytes.ShouldBe(14L * 1024 * 1024, "the bridge needs the cap to name the reason");
        att.Name.ShouldBe("Homework.pptx");
        att.Size.ShouldBe(FamilyPptxBytes);
        fake.GetFileCalls.ShouldBe(0, "a declared over-cap file must never be downloaded");
    }

    [Test]
    public async Task Over_cap_document_gets_one_automatic_reply_in_the_senders_chat()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        fake.EnqueueDocumentMessage(chatId: 555, fromId: 999, fileId: "pptx-1", fileName: "Homework.pptx",
            fileSize: FamilyPptxBytes, caption: "Make it pdf printable");

        var msg = await FirstMessageAsync(NewAdapter(fake));

        var sent = fake.SentMessages.ShouldHaveSingleItem();
        sent.ChatId.ShouldBe("555");
        sent.RawBody!["reply_to_message_id"]!.GetValue<long>().ShouldBe(long.Parse(msg!.ChannelMessageId));
        sent.Text.ShouldBe(
            "File too large (17.6 MiB): the limit is 14 MiB, so it was not delivered. "
            + "Send a smaller file, compress it, or split it.");
        sent.RawBody.ContainsKey("parse_mode").ShouldBeFalse("the notice is plain text");
        sent.Text!.ShouldNotContain("Homework", Case.Insensitive, "the notice never echoes the sender's content");
        sent.Text.ShouldNotContain("printable", Case.Insensitive, "the notice never echoes the sender's content");
    }

    [Test]
    public async Task Over_cap_notice_in_a_forum_topic_stays_in_that_topic()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        fake.EnqueueUpdate(new JsonObject
        {
            ["message"] = new JsonObject
            {
                ["message_id"] = 4242,
                ["message_thread_id"] = 77,
                ["is_topic_message"] = true,
                ["from"] = new JsonObject { ["id"] = 999, ["is_bot"] = false, ["first_name"] = "Ola" },
                ["chat"] = new JsonObject { ["id"] = -100123, ["type"] = "supergroup", ["title"] = "Family" },
                ["date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["document"] = new JsonObject { ["file_id"] = "big", ["file_name"] = "big.pptx", ["file_size"] = FamilyPptxBytes },
            },
        });

        await FirstMessageAsync(NewAdapter(fake));

        var sent = fake.SentMessages.ShouldHaveSingleItem();
        sent.ChatId.ShouldBe("-100123");
        sent.RawBody!["message_thread_id"]!.GetValue<long>().ShouldBe(77);
        sent.RawBody["reply_to_message_id"]!.GetValue<long>().ShouldBe(4242);
    }

    [Test]
    public async Task Over_cap_size_found_only_by_getFile_is_marked_and_answered()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        var settings = Settings(fake);
        settings.MaxInlineAttachmentBytes = 2;
        var adapter = new TelegramChannelAdapter(new HttpClient(), settings, NullLogger<TelegramChannelAdapter>.Instance);
        fake.RegisterFile("nosize", [1, 2, 3]);
        // No file_size in the update: only getFile reveals the file is over the cap.
        fake.EnqueueUpdate(new JsonObject
        {
            ["message"] = new JsonObject
            {
                ["message_id"] = 9001,
                ["from"] = new JsonObject { ["id"] = 999, ["is_bot"] = false, ["first_name"] = "Ola" },
                ["chat"] = new JsonObject { ["id"] = 555, ["type"] = "private", ["first_name"] = "Ola" },
                ["date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["document"] = new JsonObject { ["file_id"] = "nosize", ["file_name"] = "n.bin" },
            },
        });

        var msg = await FirstMessageAsync(adapter);

        var att = msg!.Attachments.ShouldHaveSingleItem();
        att.Content.ShouldBeNull();
        att.InlineLimitBytes.ShouldBe(2);
        att.Size.ShouldBe(3, "the size getFile resolved is kept for the note");
        fake.SentMessages.ShouldHaveSingleItem().Text.ShouldBe(
            "File too large (3 bytes): the limit is 2 bytes, so it was not delivered. "
            + "Send a smaller file, compress it, or split it.");
    }

    [Test]
    public async Task Edited_over_cap_message_is_answered_only_once()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        JsonObject Message(string caption) => new()
        {
            ["message_id"] = 1784,
            ["from"] = new JsonObject { ["id"] = 999, ["is_bot"] = false, ["first_name"] = "Ola" },
            ["chat"] = new JsonObject { ["id"] = 555, ["type"] = "private", ["first_name"] = "Ola" },
            ["date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["caption"] = caption,
            ["document"] = new JsonObject { ["file_id"] = "big", ["file_name"] = "big.pptx", ["file_size"] = FamilyPptxBytes },
        };
        fake.EnqueueUpdate(new JsonObject { ["message"] = Message("first") });
        fake.EnqueueUpdate(new JsonObject { ["edited_message"] = Message("edited caption") });

        var messages = await MessagesAsync(NewAdapter(fake), count: 2);

        messages.Count.ShouldBe(2);
        messages.ShouldAllBe(m => m.Attachments.Single().InlineLimitBytes == 14L * 1024 * 1024);
        fake.SentMessages.Count.ShouldBe(1, "an edit of the same message must not send a second notice");
    }

    [Test]
    public async Task Inlined_failed_and_disabled_downloads_send_no_notice()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        var bytes = "%PDF-1.4 small"u8.ToArray();
        fake.RegisterFile("small", bytes);
        fake.EnqueueDocumentMessage(chatId: 555, fromId: 999, fileId: "small", fileName: "s.pdf", fileSize: bytes.Length);
        fake.EnqueuePhotoMessage(chatId: 555, fromId: 999, fileId: "ghost", fileSize: 3);

        var messages = await MessagesAsync(NewAdapter(fake), count: 2);

        messages[0].Attachments.Single().Content.ShouldBe(bytes);
        messages[0].Attachments.Single().InlineLimitBytes.ShouldBeNull();
        messages[1].Attachments.Single().Content.ShouldBeNull();
        messages[1].Attachments.Single().InlineLimitBytes.ShouldBeNull("a failed download is not an over-cap file");

        // Downloads disabled (cap 0): a big file is metadata-only by configuration, not over a limit.
        await using var off = new FakeTelegramServer();
        await off.StartAsync();
        var settings = Settings(off);
        settings.MaxInlineAttachmentBytes = 0;
        off.EnqueueDocumentMessage(chatId: 555, fromId: 999, fileId: "big", fileName: "big.pptx", fileSize: FamilyPptxBytes);
        var disabled = await FirstMessageAsync(
            new TelegramChannelAdapter(new HttpClient(), settings, NullLogger<TelegramChannelAdapter>.Instance));

        disabled!.Attachments.Single().InlineLimitBytes.ShouldBeNull();
        fake.SentMessages.ShouldBeEmpty();
        off.SentMessages.ShouldBeEmpty();
    }

    [Test]
    public async Task A_failed_over_cap_notice_never_loses_the_message()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        fake.EnqueueSendBadRequest("Bad Request: not enough rights to send text messages to the chat");
        fake.EnqueueDocumentMessage(chatId: 555, fromId: 999, fileId: "big", fileName: "big.pptx",
            fileSize: FamilyPptxBytes, caption: "still here");

        var msg = await FirstMessageAsync(NewAdapter(fake));

        msg.ShouldNotBeNull("a rejected notice must never drop the inbound message");
        msg!.Text.ShouldBe("still here");
        msg.Attachments.Single().InlineLimitBytes.ShouldBe(14L * 1024 * 1024);
        fake.SendCalls.ShouldBe(1, "the notice is best-effort: one attempt, no retry loop on ingress");
    }

    private static async Task<List<ChannelMessage>> MessagesAsync(TelegramChannelAdapter adapter, int count)
    {
        var list = new List<ChannelMessage>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await foreach (var msg in adapter.ReceiveAsync(cts.Token))
        {
            list.Add(msg);
            if (list.Count == count)
                break;
        }
        return list;
    }

    private static async Task<ChannelMessage?> FirstMessageAsync(TelegramChannelAdapter adapter)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await foreach (var msg in adapter.ReceiveAsync(cts.Token))
            return msg;
        return null;
    }
}
