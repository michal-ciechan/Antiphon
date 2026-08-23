using System.Runtime.CompilerServices;
using System.Text.Json;
using Antiphon.Messaging.Gateway;
using Antiphon.Messaging.Gateway.Testing;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Messaging.Tests.Gateway;

public sealed class GatewayTests
{
    [Test]
    public async Task Ingress_pump_restarts_after_a_faulted_ReceiveAsync_and_logs_it()
    {
        var adapter = new FaultingAdapter();
        var logs = new List<string>();
        var options = Options.Create(new AntiphonGatewayOptions
        {
            IngressRestartBackoff = TimeSpan.FromMilliseconds(30),
        });
        var sut = new GatewayIngressService([adapter], new CapturingProducer(), options, new ListLogger<GatewayIngressService>(logs));

        using var cts = new CancellationTokenSource();
        await sut.StartAsync(cts.Token);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && adapter.Starts < 2)
            await Task.Delay(20);

        await cts.CancelAsync();
        await sut.StopAsync(CancellationToken.None);

        adapter.Starts.ShouldBeGreaterThanOrEqualTo(2);
        logs.ShouldContain(l => l.Contains("[ingress]") && l.Contains("receive stream faulted"));
    }

    [Test]
    public async Task Ingress_produce_key_is_conversation_id()
    {
        var message = SampleMessage(conversationId: "chat-42");
        var adapter = new OneShotAdapter(message);
        var producer = new CapturingProducer();
        var options = Options.Create(new AntiphonGatewayOptions());
        var sut = new GatewayIngressService([adapter], producer, options, new ListLogger<GatewayIngressService>([]));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.StartAsync(cts.Token);

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && producer.Records.Count == 0)
            await Task.Delay(20);

        await cts.CancelAsync();
        await sut.StopAsync(CancellationToken.None);

        var record = producer.Records.ShouldHaveSingleItem();
        record.Key.ShouldBe("chat-42");
        record.Topic.ShouldBe("channels.inbound");
        var parsed = JsonSerializer.Deserialize<ChannelMessage>(record.Value, MessagingJson.Options)!;
        parsed.Conversation.Id.ShouldBe("chat-42");
    }

    [Test]
    public async Task Outbound_logs_an_unknown_channel_rather_than_silently_dropping()
    {
        var logs = new List<string>();
        var sut = new GatewayOutboundService(
            adapters: [],
            Options.Create(new AntiphonGatewayOptions()),
            new ListLogger<GatewayOutboundService>(logs));

        await sut.DispatchAsync(new ChannelReply { Channel = "discord", Text = "hi" }, CancellationToken.None);

        logs.ShouldContain(l => l.Contains("[outbound]") && l.Contains("no adapter registered for channel") && l.Contains("discord"));
    }

    [Test]
    public void Options_bind_from_the_Kafka_section_name_the_Service_uses()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:BootstrapServers"] = "broker:19092",
                ["Kafka:InboundTopic"] = "channels.inbound",
                ["Kafka:OutboundTopic"] = "channels.outbound",
                ["Kafka:ConsumerGroup"] = "antiphon-messaging-service",
                ["Kafka:MaxMessageBytes"] = "20971520",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAntiphonGateway(config, "Kafka");
        using var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<IOptions<AntiphonGatewayOptions>>().Value;
        opts.BootstrapServers.ShouldBe("broker:19092");
        opts.InboundTopic.ShouldBe("channels.inbound");
        opts.OutboundTopic.ShouldBe("channels.outbound");
        opts.ConsumerGroup.ShouldBe("antiphon-messaging-service");
        opts.MaxMessageBytes.ShouldBe(20 * 1024 * 1024);
        opts.TopicLayout.ShouldBe(TopicLayout.Shared);
        opts.Security.SecurityProtocol.ShouldBe("Plaintext");
        opts.AutoOffsetReset.ShouldBe("Earliest");
    }

    [Test]
    public void PerProvider_topic_layout_throws_naming_the_follow_up()
    {
        var options = new AntiphonGatewayOptions { TopicLayout = TopicLayout.PerProvider };
        var ex = Should.Throw<NotSupportedException>(() => options.ResolveInboundTopic());
        ex.Message.ShouldContain("PerProvider");
        ex.Message.ShouldContain("future follow-up");
        Should.Throw<NotSupportedException>(() => options.ResolveOutboundTopic())
            .Message.ShouldContain("Tier 2");
    }

    [Test]
    public async Task InMemoryGatewayBus_produce_key_is_conversation_id_and_replies_can_be_pushed()
    {
        var bus = new InMemoryGatewayBus();
        var inbound = SampleMessage(conversationId: "c-9");
        bus.ProduceInbound(inbound);

        var produced = bus.ProducedInbound.ShouldHaveSingleItem();
        produced.Key.ShouldBe("c-9");
        produced.Message.ShouldBe(inbound);

        bus.PushReply(new ChannelReply { Channel = "telegram", ConversationId = "c-9", Text = "pong" });
        bus.Complete();

        var replies = new List<ChannelReply>();
        await foreach (var reply in bus.ConsumeRepliesAsync())
            replies.Add(reply);
        replies.ShouldHaveSingleItem().Text.ShouldBe("pong");
    }

    private static ChannelMessage SampleMessage(string conversationId) => new()
    {
        Id = "id-1",
        Channel = "telegram",
        ChannelMessageId = "1",
        Conversation = new Conversation { Id = conversationId, Kind = ConversationKind.Direct },
        Author = new Participant { Id = "user-1" },
        Timestamp = DateTimeOffset.UnixEpoch,
        Text = "hello",
        ReplyHandle = conversationId,
        Raw = JsonDocument.Parse("{}").RootElement.Clone(),
    };

    private sealed class FaultingAdapter : IChannelAdapter
    {
        public int Starts;

        public string Channel => "telegram";
        public ChannelCapabilities Capabilities => new() { Channel = Channel };

        public async IAsyncEnumerable<ChannelMessage> ReceiveAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Starts);
            await Task.Yield();
            throw new InvalidOperationException("boom");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        public Task<SendResult> SendAsync(ChannelReply reply, CancellationToken cancellationToken) =>
            Task.FromResult(SendResult.Sent());
    }

    private sealed class OneShotAdapter(ChannelMessage message) : IChannelAdapter
    {
        public string Channel => message.Channel;
        public ChannelCapabilities Capabilities => new() { Channel = Channel };

        public async IAsyncEnumerable<ChannelMessage> ReceiveAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return message;
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public Task<SendResult> SendAsync(ChannelReply reply, CancellationToken cancellationToken) =>
            Task.FromResult(SendResult.Sent());
    }

    private sealed class ListLogger<T>(List<string> sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (sink)
                sink.Add(formatter(state, exception));
        }
    }

    private sealed class CapturingProducer : IProducer<string, string>
    {
        public List<(string Topic, string Key, string Value)> Records { get; } = [];

        public Handle Handle => throw new NotImplementedException();
        public string Name => "capturing";

        public Task<DeliveryResult<string, string>> ProduceAsync(
            string topic, Message<string, string> message, CancellationToken cancellationToken = default)
        {
            Records.Add((topic, message.Key ?? "", message.Value ?? ""));
            return Task.FromResult(new DeliveryResult<string, string>
            {
                Topic = topic,
                Message = message,
                Status = PersistenceStatus.Persisted,
            });
        }

        public int AddBrokers(string brokers) => throw new NotImplementedException();
        public void Produce(string topic, Message<string, string> message, Action<DeliveryReport<string, string>>? deliveryHandler = null) => throw new NotImplementedException();
        public void Produce(TopicPartition topicPartition, Message<string, string> message, Action<DeliveryReport<string, string>>? deliveryHandler = null) => throw new NotImplementedException();
        public Task<DeliveryResult<string, string>> ProduceAsync(TopicPartition topicPartition, Message<string, string> message, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public int Poll(TimeSpan timeout) => 0;
        public int Flush(TimeSpan timeout) => 0;
        public void Flush(CancellationToken cancellationToken = default) { }
        public void InitTransactions(TimeSpan timeout) => throw new NotImplementedException();
        public void BeginTransaction() => throw new NotImplementedException();
        public void CommitTransaction(TimeSpan timeout) => throw new NotImplementedException();
        public void CommitTransaction() => throw new NotImplementedException();
        public void AbortTransaction(TimeSpan timeout) => throw new NotImplementedException();
        public void AbortTransaction() => throw new NotImplementedException();
        public void SendOffsetsToTransaction(IEnumerable<TopicPartitionOffset> offsets, IConsumerGroupMetadata groupMetadata, TimeSpan timeout) => throw new NotImplementedException();
        public void SetSaslCredentials(string username, string password) => throw new NotImplementedException();
        public void Dispose() { }
    }
}
