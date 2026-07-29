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

    private static async Task<ChannelMessage?> FirstMessageAsync(TelegramChannelAdapter adapter)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await foreach (var msg in adapter.ReceiveAsync(cts.Token))
            return msg;
        return null;
    }
}
