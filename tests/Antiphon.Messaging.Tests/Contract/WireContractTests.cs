using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Messaging.Tests.Contract;

/// <summary>
/// Pins the Kafka wire contract: live-captured bytes stay identical, unknown enums map to the
/// declared sentinel, unknown properties are ignored, and the <c>required</c> set cannot grow
/// without this test going red.
/// </summary>
public sealed class WireContractTests
{
    [Test]
    public void Live_channel_message_round_trips_wire_identical()
        => AssertRoundTrip<ChannelMessage>("live-channel-message.json");

    [Test]
    public void Live_channel_reply_round_trips_wire_identical()
        => AssertRoundTrip<ChannelReply>("live-channel-reply.json");

    [Test]
    public void Unknown_channel_reply_kind_maps_to_answer()
    {
        const string json = """{"channel":"telegram","kind":"BrandNewKind","text":"hi"}""";
        var reply = JsonSerializer.Deserialize<ChannelReply>(json, MessagingJson.Options)!;
        reply.Kind.ShouldBe(ChannelReplyKind.Answer);
        reply.Channel.ShouldBe("telegram");
    }

    [Test]
    public void Unknown_attachment_kind_maps_to_other()
    {
        const string json = """{"kind":"Hologram","channelRef":"file-1"}""";
        var attachment = JsonSerializer.Deserialize<Attachment>(json, MessagingJson.Options)!;
        attachment.Kind.ShouldBe(AttachmentKind.Other);
    }

    [Test]
    public void Unknown_conversation_kind_maps_to_group()
    {
        const string json = """{"id":"1","kind":"Forum"}""";
        var conversation = JsonSerializer.Deserialize<Conversation>(json, MessagingJson.Options)!;
        conversation.Kind.ShouldBe(ConversationKind.Group);
    }

    [Test]
    public void Unknown_property_on_channel_reply_is_ignored()
    {
        const string json = """{"channel":"telegram","text":"hi","notAField":true}""";
        var reply = JsonSerializer.Deserialize<ChannelReply>(json, MessagingJson.Options)!;
        reply.Channel.ShouldBe("telegram");
        reply.Text.ShouldBe("hi");
    }

    [Test]
    public void Unknown_property_on_channel_message_is_ignored()
    {
        const string json = """
            {"id":"1","channel":"telegram","channelMessageId":"2","conversation":{"id":"c","kind":"Direct"},
             "author":{"id":"a"},"timestamp":"1970-01-01T00:00:00+00:00","replyHandle":"c","raw":{},"extra":1}
            """;
        var message = JsonSerializer.Deserialize<ChannelMessage>(json, MessagingJson.Options)!;
        message.Id.ShouldBe("1");
        message.Channel.ShouldBe("telegram");
    }

    [Test]
    public void Required_members_match_the_documented_contract()
    {
        RequiredNames(typeof(ChannelMessage)).ShouldBe(
        [
            "Id", "Channel", "ChannelMessageId", "Conversation", "Author", "Timestamp", "ReplyHandle", "Raw",
        ], ignoreOrder: true);
        RequiredNames(typeof(Conversation)).ShouldBe(["Id", "Kind"], ignoreOrder: true);
        RequiredNames(typeof(Participant)).ShouldBe(["Id"], ignoreOrder: true);
        RequiredNames(typeof(Mention)).ShouldBe(["Id"], ignoreOrder: true);
        RequiredNames(typeof(Attachment)).ShouldBe(["Kind", "ChannelRef"], ignoreOrder: true);
        RequiredNames(typeof(ReplyReference)).ShouldBe(["ChannelMessageId"], ignoreOrder: true);
        RequiredNames(typeof(ChannelReply)).ShouldBe(["Channel"], ignoreOrder: true);
        RequiredNames(typeof(OutboundAttachment)).ShouldBe(["Kind"], ignoreOrder: true);
    }

    private static void AssertRoundTrip<T>(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        var originalBytes = File.ReadAllBytes(path);
        var original = Encoding.UTF8.GetString(originalBytes);
        var parsed = JsonSerializer.Deserialize<T>(original, MessagingJson.Options);
        if (parsed is null)
            throw new Exception($"deserialize returned null for {fileName}");
        var round = JsonSerializer.Serialize(parsed, MessagingJson.Options);
        round.ShouldBe(original, $"wire bytes changed for {fileName} — S1 must not change the bus");
    }

    private static string[] RequiredNames(Type type) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetCustomAttribute<RequiredMemberAttribute>() is not null)
            .Select(p => p.Name)
            .ToArray();
}
