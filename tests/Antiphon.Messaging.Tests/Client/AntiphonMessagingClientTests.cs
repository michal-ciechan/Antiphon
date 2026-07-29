using System.Text.Json;
using Antiphon.Messaging.Client;
using Antiphon.Messaging.Client.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Messaging.Tests.Client;

/// <summary>
/// Tests for the consumer-facing client: the in-memory fake behaves as a producer/consumer, and the
/// canonical wire JSON matches the bridge's format (camelCase + string enums) so consumers and the bridge agree.
/// </summary>
public sealed class AntiphonMessagingClientTests
{
    [Test]
    public async Task Fake_captures_sent_replies()
    {
        var fake = new FakeAntiphonMessagingClient();

        await fake.SendAsync(new ChannelReply { Channel = "telegram", ConversationId = "555", Text = "hi" });

        var reply = fake.SentReplies.ShouldHaveSingleItem();
        reply.ConversationId.ShouldBe("555");
        reply.Text.ShouldBe("hi");
    }

    [Test]
    public async Task Fake_streams_injected_inbound()
    {
        var fake = new FakeAntiphonMessagingClient();
        fake.InjectTelegramText(chatId: "777", text: "/link ABC123", kind: ConversationKind.Group, username: "alice", conversationTitle: "Maths Crew");
        fake.Complete();

        var received = new List<ChannelMessage>();
        await foreach (var m in fake.ConsumeAsync())
            received.Add(m);

        var msg = received.ShouldHaveSingleItem();
        msg.Channel.ShouldBe("telegram");
        msg.Text.ShouldBe("/link ABC123");
        msg.Conversation.Id.ShouldBe("777");
        msg.Conversation.Kind.ShouldBe(ConversationKind.Group);
        msg.Conversation.Title.ShouldBe("Maths Crew");
        msg.ReplyHandle.ShouldBe("777");
    }

    [Test]
    public void Inbound_wire_json_is_camelCase_with_string_enums()
    {
        var msg = new ChannelMessage
        {
            Id = "id1",
            Channel = "telegram",
            ChannelMessageId = "42",
            Conversation = new Conversation { Id = "777", Kind = ConversationKind.Group, Title = "Maths Crew" },
            Author = new Participant { Id = "1001", Username = "alice" },
            Timestamp = DateTimeOffset.UnixEpoch,
            Text = "hello",
            ReplyHandle = "777",
            Raw = JsonDocument.Parse("{}").RootElement.Clone(),
        };

        var json = JsonSerializer.Serialize(msg, MessagingJson.Options);

        json.ShouldContain("\"channelMessageId\":");   // camelCase property names
        json.ShouldContain("\"conversation\":");
        json.ShouldContain("\"kind\":\"Group\"");        // enum serialized as its name
        json.ShouldNotContain("\"ChannelMessageId\"", Case.Sensitive);   // never PascalCase

        var back = JsonSerializer.Deserialize<ChannelMessage>(json, MessagingJson.Options)!;
        back.Conversation.Kind.ShouldBe(ConversationKind.Group);
        back.ChannelMessageId.ShouldBe("42");
    }

    [Test]
    public void Outbound_wire_json_is_camelCase()
    {
        var reply = new ChannelReply { Channel = "telegram", ConversationId = "777", Text = "hi", ReplyToMessageId = "42" };

        var json = JsonSerializer.Serialize(reply, MessagingJson.Options);

        json.ShouldContain("\"channel\":\"telegram\"");
        json.ShouldContain("\"conversationId\":\"777\"");
        json.ShouldContain("\"replyToMessageId\":\"42\"");
    }

    [Test]
    public void Attachment_bytes_round_trip_the_wire_as_base64()
    {
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0xFF, 0x00, 0x7F }; // %PDF + non-ASCII
        var reply = new ChannelReply
        {
            Channel = "telegram",
            ConversationId = "777",
            Attachments =
            [
                new OutboundAttachment
                {
                    Kind = AttachmentKind.File, Content = bytes,
                    Name = "invoice.pdf", Mime = "application/pdf", Source = @"C:\work\invoice.pdf",
                },
            ],
        };

        var json = JsonSerializer.Serialize(reply, MessagingJson.Options);
        json.ShouldContain("\"content\":\"" + Convert.ToBase64String(bytes) + "\"", Case.Sensitive);

        var back = JsonSerializer.Deserialize<ChannelReply>(json, MessagingJson.Options)!;
        var att = back.Attachments.ShouldHaveSingleItem();
        att.Content.ShouldBe(bytes);
        att.Kind.ShouldBe(AttachmentKind.File);
        att.Name.ShouldBe("invoice.pdf");
        att.Mime.ShouldBe("application/pdf");
    }

    [Test]
    public void Old_payloads_without_attachment_fields_still_deserialize()
    {
        // A pre-attachment ChannelReply as older producers wrote it — the new fields must default.
        const string json = """{"channel":"telegram","conversationId":"777","text":"hi"}""";

        var back = JsonSerializer.Deserialize<ChannelReply>(json, MessagingJson.Options)!;
        back.Attachments.ShouldBeEmpty();
        back.Text.ShouldBe("hi");
    }

    [Test]
    public void A_20mb_capped_attachment_message_fits_the_bus_limit()
    {
        // The contract behind MaxAttachmentBytes (14 MB raw): base64 + envelope must stay under the
        // 20 MB bus cap the producer/consumer/topics are configured with.
        var reply = new ChannelReply
        {
            Channel = "telegram",
            ConversationId = "777",
            Text = new string('x', 4000),
            Attachments =
            [
                new OutboundAttachment { Kind = AttachmentKind.File, Content = new byte[14 * 1024 * 1024], Name = "max.bin" },
            ],
        };

        var json = JsonSerializer.Serialize(reply, MessagingJson.Options);
        json.Length.ShouldBeLessThan(AntiphonMessagingOptions.MaxMessageBytesDefault,
            "a max-size attachment must serialize under the 20 MB Kafka message cap");
    }
}
