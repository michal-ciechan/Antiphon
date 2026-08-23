using EchoGateway;
using Antiphon.Messaging.Gateway.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Messaging.Tests.Gateway;

public sealed class EchoGatewaySampleTests
{
    [Test]
    public async Task Stdin_line_yields_an_echo_channel_message()
    {
        var adapter = new EchoChannelAdapter(new StringReader("hello echo" + Environment.NewLine), new StringWriter());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        ChannelMessage? received = null;
        await foreach (var message in adapter.ReceiveAsync(cts.Token))
        {
            received = message;
            break;
        }

        received.ShouldNotBeNull();
        received.Channel.ShouldBe(EchoChannelAdapter.ChannelKey);
        received.Text.ShouldBe("hello echo");
        received.Conversation.Id.ShouldBe(EchoChannelAdapter.ConversationId);
        received.ReplyHandle.ShouldBe(EchoChannelAdapter.ConversationId);
        received.Author.IsSelf.ShouldBeFalse();
        received.Author.Id.ShouldBe("echo-user");
    }

    [Test]
    public async Task Empty_lines_are_skipped()
    {
        var adapter = new EchoChannelAdapter(
            new StringReader(Environment.NewLine + "   " + Environment.NewLine + "kept" + Environment.NewLine),
            new StringWriter());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        ChannelMessage? received = null;
        await foreach (var message in adapter.ReceiveAsync(cts.Token))
        {
            received = message;
            break;
        }

        received.ShouldNotBeNull();
        received.Text.ShouldBe("kept");
    }

    [Test]
    public async Task Reply_is_printed_to_stdout()
    {
        var output = new StringWriter();
        var adapter = new EchoChannelAdapter(new StringReader(""), output);

        var send = await adapter.SendAsync(
            new ChannelReply
            {
                Channel = EchoChannelAdapter.ChannelKey,
                ConversationId = EchoChannelAdapter.ConversationId,
                ReplyHandle = EchoChannelAdapter.ConversationId,
                Text = "pong",
            },
            CancellationToken.None);

        send.Ok.ShouldBeTrue();
        output.ToString().Trim().ShouldBe("pong");
    }

    [Test]
    public async Task Progress_kind_is_prefixed_and_attachments_are_counted()
    {
        var output = new StringWriter();
        var adapter = new EchoChannelAdapter(new StringReader(""), output);

        var send = await adapter.SendAsync(
            new ChannelReply
            {
                Channel = EchoChannelAdapter.ChannelKey,
                ConversationId = EchoChannelAdapter.ConversationId,
                Kind = ChannelReplyKind.Progress,
                Text = "working",
                Attachments = [new OutboundAttachment { Kind = AttachmentKind.File, Name = "a.txt", Content = [1] }],
            },
            CancellationToken.None);

        send.Ok.ShouldBeTrue();
        output.ToString().Trim().ShouldBe("[Progress] working (1 attachment(s))");
    }

    [Test]
    public async Task InMemoryGatewayBus_round_trip_prints_the_inbound_text()
    {
        var output = new StringWriter();
        var adapter = new EchoChannelAdapter(new StringReader("round-trip" + Environment.NewLine), output);
        var bus = new InMemoryGatewayBus();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        ChannelMessage? inbound = null;
        await foreach (var message in adapter.ReceiveAsync(cts.Token))
        {
            inbound = message;
            bus.ProduceInbound(message);
            break;
        }

        inbound.ShouldNotBeNull();
        var produced = bus.ProducedInbound.ShouldHaveSingleItem();
        produced.Key.ShouldBe(EchoChannelAdapter.ConversationId);

        var reply = new ChannelReply
        {
            Channel = EchoChannelAdapter.ChannelKey,
            ConversationId = inbound.Conversation.Id,
            ReplyHandle = inbound.ReplyHandle,
            Text = inbound.Text,
        };
        bus.PushReply(reply);

        var send = await adapter.SendAsync(reply, cts.Token);
        send.Ok.ShouldBeTrue();
        output.ToString().ShouldContain("round-trip");
    }
}
