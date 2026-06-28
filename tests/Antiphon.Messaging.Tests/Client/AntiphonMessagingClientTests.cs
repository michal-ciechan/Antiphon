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
}
