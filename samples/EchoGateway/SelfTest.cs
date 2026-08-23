using Antiphon.Messaging;
using Antiphon.Messaging.Gateway;
using Antiphon.Messaging.Gateway.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EchoGateway;

/// <summary>
/// Kafka-free round-trip: stdin line → <see cref="IChannelAdapter"/> →
/// <see cref="InMemoryGatewayBus"/> → reply printed to stdout. Also resolves
/// the adapter through <c>AddAntiphonGateway</c> so the host wiring is proven
/// without opening a broker connection.
/// </summary>
internal static class SelfTest
{
    public static async Task<int> RunAsync()
    {
        const string body = "hello from echo";
        var input = new StringReader(body + Environment.NewLine);
        var output = new StringWriter();
        var adapter = new EchoChannelAdapter(input, output);
        var bus = new InMemoryGatewayBus();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        ChannelMessage? inbound = null;
        await foreach (var message in adapter.ReceiveAsync(cts.Token))
        {
            inbound = message;
            bus.ProduceInbound(message);
            break;
        }

        if (inbound is null
            || inbound.Channel != EchoChannelAdapter.ChannelKey
            || inbound.Text != body
            || inbound.Author.IsSelf
            || inbound.Conversation.Id != EchoChannelAdapter.ConversationId)
        {
            Console.Error.WriteLine("self-test failed: inbound ChannelMessage was not produced correctly");
            return 1;
        }

        var produced = bus.ProducedInbound;
        if (produced.Count != 1 || produced[0].Key != EchoChannelAdapter.ConversationId)
        {
            Console.Error.WriteLine("self-test failed: InMemoryGatewayBus produce key was not Conversation.Id");
            return 1;
        }

        var reply = new ChannelReply
        {
            Channel = EchoChannelAdapter.ChannelKey,
            ConversationId = inbound.Conversation.Id,
            ReplyHandle = inbound.ReplyHandle,
            Text = inbound.Text,
        };
        bus.PushReply(reply);

        var send = await adapter.SendAsync(reply, cts.Token);
        if (!send.Ok)
        {
            Console.Error.WriteLine("self-test failed: SendAsync " + send.Error);
            return 1;
        }

        var printed = output.ToString();
        if (!printed.Contains(body, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("self-test failed: stdout did not echo the inbound text");
            return 1;
        }

        var services = new ServiceCollection();
        services.AddSingleton<IChannelAdapter>(adapter);
        services.AddAntiphonGateway(o =>
        {
            o.BootstrapServers = "127.0.0.1:1";
            o.ConsumerGroup = "echo-gateway-self-test";
        });
        using (var provider = services.BuildServiceProvider())
        {
            var resolved = provider.GetServices<IChannelAdapter>().Single();
            if (resolved.Channel != EchoChannelAdapter.ChannelKey)
            {
                Console.Error.WriteLine("self-test failed: AddAntiphonGateway did not keep the echo adapter");
                return 1;
            }
        }

        Console.WriteLine("self-test ok: echo round-trip via IChannelAdapter + InMemoryGatewayBus");
        Console.WriteLine("printed: " + printed.Trim());
        return 0;
    }
}
