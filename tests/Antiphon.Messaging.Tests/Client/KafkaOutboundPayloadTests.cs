using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Options;
using Shouldly;
using Testcontainers.Redpanda;
using TUnit.Core;

namespace Antiphon.Messaging.Tests.Client;

[Category("Integration")]
[NotInParallel]
public sealed class KafkaOutboundPayloadTests
{
    private static RedpandaContainer? _broker;

    [Before(Class)]
    public static async Task StartAsync()
    {
        _broker = new RedpandaBuilder("docker.redpanda.com/redpandadata/redpanda:v25.3.4").Build();
        await _broker.StartAsync();
    }

    [After(Class)]
    public static async Task StopAsync()
    {
        if (_broker is not null) await _broker.DisposeAsync();
    }

    [Test]
    public async Task Validated_reply_bytes_and_frozen_thread_cross_the_real_broker()
    {
        var topic = "c0418-out-" + Guid.NewGuid().ToString("N");
        using (var admin = new AdminClientBuilder(new AdminClientConfig
               { BootstrapServers = _broker!.GetBootstrapAddress() }).Build())
            await admin.CreateTopicsAsync([new TopicSpecification
                { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
        var options = Options.Create(new AntiphonMessagingOptions
        {
            BootstrapServers = _broker!.GetBootstrapAddress(), OutboundTopic = topic,
        });
        var original = new byte[] { 0, 10, 255, 42 };
        var reply = new ChannelReply
        {
            Channel = "slack", ConversationId = "C0418", ReplyHandle = "thread-1",
            Text = "four sources ✨", Attachments = [new OutboundAttachment
            {
                Kind = AttachmentKind.File, Name = "source.md", Mime = "text/markdown",
                Content = original,
            }],
        };
        var expected = JsonSerializer.Serialize(reply, MessagingJson.Options);
        System.Text.Encoding.UTF8.GetByteCount(expected).ShouldBeLessThan(options.Value.MaxMessageBytes);
        using (var producer = new KafkaAntiphonMessagingProducer(options))
            await producer.SendAsync(reply);

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _broker.GetBootstrapAddress(),
            GroupId = "c0418-out-group-" + Guid.NewGuid().ToString("N"),
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();
        consumer.Subscribe(topic);
        var record = consumer.Consume(TimeSpan.FromSeconds(30));
        record.ShouldNotBeNull();
        record.Topic.ShouldBe(topic);
        record.Message.Key.ShouldBe("C0418");
        record.Message.Value.ShouldBe(expected);
        var roundTrip = JsonSerializer.Deserialize<ChannelReply>(record.Message.Value,
            MessagingJson.Options)!;
        roundTrip.ReplyHandle.ShouldBe("thread-1");
        roundTrip.Text.ShouldBe("four sources ✨");
        roundTrip.Attachments.ShouldHaveSingleItem().Content.ShouldBe(original);
    }
}
