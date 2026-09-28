using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Messaging.Gateway;
using Antiphon.Tests.TestHelpers;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Testcontainers.Redpanda;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[NotInParallel]
public sealed class ChannelOutboundTransportTests
{
    private static RedpandaContainer? _broker;

    [Before(Class)]
    public static async Task StartBrokerAsync()
    {
        _broker = new RedpandaBuilder("docker.redpanda.com/redpandadata/redpanda:v25.3.4").Build();
        await _broker.StartAsync();
    }

    [After(Class)]
    public static async Task StopBrokerAsync()
    {
        if (_broker is not null) await _broker.DisposeAsync();
    }

    [Test]
    [Arguments("ready")]
    [Arguments("consumer-late")]
    [Arguments("broker-refuses")]
    public async Task C519_Reply_crosses_the_real_broker_and_reaches_the_adapter(string condition)
    {
        var bootstrap = _broker!.GetBootstrapAddress();
        var topic = $"c519-outbound-{Guid.NewGuid():N}";
        var group = $"c519-gateway-{Guid.NewGuid():N}";
        using var admin = new AdminClientBuilder(new Confluent.Kafka.AdminClientConfig
        { BootstrapServers = bootstrap }).Build();
        await admin.CreateTopicsAsync([new TopicSpecification
        {
            Name = topic, NumPartitions = 1, ReplicationFactor = 1,
            Configs = condition == "broker-refuses"
                ? new Dictionary<string, string> { ["max.message.bytes"] = "512" }
                : new Dictionary<string, string>(),
        }]);
        using var kafka = new KafkaAntiphonMessagingProducer(Options.Create(
            new AntiphonMessagingOptions
            {
                BootstrapServers = bootstrap, OutboundTopic = topic,
                MaxMessageBytes = 20 * 1024 * 1024,
            }));
        var producer = new RefuseOnceThenKafka(kafka);
        await using var fixture = await ChannelOutboundFixture.CreateAsync(producer);
        var (sourceId, answer, _) = await fixture.CompleteSourceTurnAsync("main");
        var pdf = Path.Combine(fixture.Harness.TempRoot, "main-original.pdf");
        var expectedBytes = Enumerable.Range(0, 2048).Select(i => (byte)(i % 251)).ToArray();
        await File.WriteAllBytesAsync(pdf, expectedBytes);
        var adapter = new RecordingAdapter();
        using var gateway = BuildGateway(bootstrap, topic, group, adapter);
        if (condition == "ready") await gateway.StartAsync();

        await fixture.DispatchAsync();
        var refused = await fixture.ReadAsync(sourceId);
        refused.Publication!.State.ShouldBe("Pending");
        refused.Source.ChannelReplySettledAt.ShouldBeNull();
        adapter.Received.ShouldBeEmpty();
        producer.Calls.ShouldBe(1);

        await fixture.RestartAsync();
        fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        await fixture.StartWorkerAsync();
        if (condition == "broker-refuses")
        {
            var brokerRefused = await fixture.ReadAsync(sourceId);
            brokerRefused.Publication!.State.ShouldBe("Pending");
            brokerRefused.Publication.LastFailure.ShouldBe("MsgSizeTooLarge");
            adapter.Received.ShouldBeEmpty();
            var frozenBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(
                JsonSerializer.Deserialize<ChannelReply>(brokerRefused.Publication.EnvelopeJson)!,
                Antiphon.Messaging.MessagingJson.Options));
            frozenBytes.ShouldBeGreaterThan(512);
            var resource = new ConfigResource { Type = ResourceType.Topic, Name = topic };
            await admin.AlterConfigsAsync(new Dictionary<ConfigResource, List<ConfigEntry>>
            {
                [resource] = [new ConfigEntry { Name = "max.message.bytes", Value = "20971520" }],
            });
            await gateway.StartAsync();
            await fixture.AdvanceAndScanAsync(TimeSpan.FromSeconds(30));
        }
        else if (condition == "consumer-late")
        {
            (await fixture.ReadAsync(sourceId)).Publication!.State.ShouldBe("Published");
            adapter.Received.ShouldBeEmpty();
            await gateway.StartAsync();
        }

        var receipt = await adapter.NextAsync();
        receipt.Text.ShouldContain(answer);
        receipt.Channel.ShouldBe("telegram");
        receipt.Kind.ShouldBe(ChannelReplyKind.Answer);
        receipt.ConversationId.ShouldBe(refused.Source.ConversationKey!.Split(':', 2)[1]);
        receipt.Attachments.Count.ShouldBe(1);
        receipt.Attachments[0].Name.ShouldBe("main-original.pdf");
        receipt.Attachments[0].Mime.ShouldBe("application/pdf");
        receipt.Attachments[0].Content.ShouldBe(expectedBytes);
        var final = await fixture.ReadAsync(sourceId);
        final.Publication!.State.ShouldBe("Published");
        final.Publication.PublishedAt.ShouldNotBeNull();
        final.Publication.Sources.Select(s => s.QueueMessageId).ShouldContain(sourceId);
        final.Source.ChannelReplySettledAt.ShouldNotBeNull();
        JsonSerializer.Serialize(receipt, Antiphon.Messaging.MessagingJson.Options).ShouldBe(
            JsonSerializer.Serialize(JsonSerializer.Deserialize<ChannelReply>(
                final.Publication.EnvelopeJson)!, Antiphon.Messaging.MessagingJson.Options));
        await fixture.AdvanceAndScanAsync(TimeSpan.FromSeconds(30));
        adapter.Received.Count.ShouldBe(1);
        producer.Calls.ShouldBe(condition == "broker-refuses" ? 3 : 2);
        await gateway.StopAsync();
    }

    private static IHost BuildGateway(string bootstrap, string topic, string group, RecordingAdapter adapter)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddLogging();
        builder.Services.AddSingleton<IChannelAdapter>(adapter);
        builder.Services.AddSingleton(Options.Create(new AntiphonGatewayOptions
        {
            BootstrapServers = bootstrap, OutboundTopic = topic, ConsumerGroup = group,
            AutoOffsetReset = "Earliest", MaxMessageBytes = 20 * 1024 * 1024,
        }));
        builder.Services.AddHostedService<GatewayOutboundService>();
        return builder.Build();
    }

    private sealed class RefuseOnceThenKafka(KafkaAntiphonMessagingProducer kafka)
        : IAntiphonMessagingProducer
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
                throw new ChannelPublicationRefusedException("ScriptedFirst",
                    new InvalidOperationException("First attempt refused before broker entry."));
            return kafka.SendAsync(reply, cancellationToken);
        }
    }

    private sealed class RecordingAdapter : IChannelAdapter
    {
        private readonly Channel<ChannelReply> _signals =
            System.Threading.Channels.Channel.CreateUnbounded<ChannelReply>();
        public ConcurrentQueue<ChannelReply> Received { get; } = new();
        public string Channel => "telegram";
        public ChannelCapabilities Capabilities => new() { Channel = Channel, Attachments = true };

        public async IAsyncEnumerable<ChannelMessage> ReceiveAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<SendResult> SendAsync(ChannelReply reply, CancellationToken cancellationToken)
        {
            Received.Enqueue(reply);
            _signals.Writer.TryWrite(reply);
            return Task.FromResult(SendResult.Sent(Guid.NewGuid().ToString("N")));
        }

        public Task<ChannelReply> NextAsync() =>
            _signals.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
    }
}
