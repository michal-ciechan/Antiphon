using System.Text.Json;
using System.Text;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Messaging.Gateway;
using Antiphon.Messaging.Slack;
using Antiphon.Messaging.Tests.FakeSlack;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging.Abstractions;
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

    [Test]
    public async Task Near_default_serialized_reply_crosses_the_real_broker_with_exact_bytes_and_key()
    {
        var topic = "c0418-large-out-" + Guid.NewGuid().ToString("N");
        var cap = AntiphonMessagingOptions.MaxMessageBytesDefault;
        using (var admin = new AdminClientBuilder(new AdminClientConfig
               { BootstrapServers = _broker!.GetBootstrapAddress() }).Build())
            await admin.CreateTopicsAsync([new TopicSpecification
            {
                Name = topic, NumPartitions = 1, ReplicationFactor = 1,
                Configs = new Dictionary<string, string>
                {
                    ["max.message.bytes"] = (cap + 64 * 1024).ToString(),
                },
            }]);

        var source = new byte[13 * 1024 * 1024 + 512 * 1024];
        Random.Shared.NextBytes(source);
        using var raw = JsonDocument.Parse("""{"note":"zażółć ✨"}""");
        var reply = new ChannelReply
        {
            Channel = "slack", ConversationId = "C0418-large", ReplyHandle = "C0418-large|thread-✨",
            ReplyToMessageId = "parent-✨", Text = "Source and conversion ✨",
            RawOverrides = raw.RootElement.Clone(),
            Attachments = [new OutboundAttachment
            {
                Kind = AttachmentKind.File, Name = "źródło.md", Mime = "text/markdown",
                Caption = "original ✨", Content = source,
            }],
        };
        var expected = JsonSerializer.Serialize(reply, MessagingJson.Options);
        var wireBytes = Encoding.UTF8.GetBytes(expected);
        wireBytes.Length.ShouldBeGreaterThan(18 * 1024 * 1024);
        wireBytes.Length.ShouldBeLessThan(cap - 64 * 1024);

        using (var producer = new KafkaAntiphonMessagingProducer(Options.Create(
                   new AntiphonMessagingOptions
                   {
                       BootstrapServers = _broker!.GetBootstrapAddress(), OutboundTopic = topic,
                       MaxMessageBytes = cap,
                   })))
            await producer.SendAsync(reply);

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _broker!.GetBootstrapAddress(),
            GroupId = "c0418-large-group-" + Guid.NewGuid().ToString("N"),
            AutoOffsetReset = AutoOffsetReset.Earliest, EnableAutoCommit = false,
            FetchMaxBytes = cap + 64 * 1024, MaxPartitionFetchBytes = cap + 64 * 1024,
        }).Build();
        consumer.Subscribe(topic);
        var record = consumer.Consume(TimeSpan.FromSeconds(60));
        record.ShouldNotBeNull();
        record.Topic.ShouldBe(topic);
        record.Message.Key.ShouldBe(reply.ConversationId);
        Encoding.UTF8.GetBytes(record.Message.Value).ShouldBe(wireBytes);
        var received = JsonSerializer.Deserialize<ChannelReply>(record.Message.Value,
            MessagingJson.Options)!;
        received.ReplyHandle.ShouldBe(reply.ReplyHandle);
        received.ReplyToMessageId.ShouldBe(reply.ReplyToMessageId);
        received.RawOverrides?.GetProperty("note").GetString().ShouldBe("zażółć ✨");
        received.Attachments.ShouldHaveSingleItem().Content.ShouldBe(source);
    }

    [Test]
    public async Task Broker_key_uses_conversation_then_reply_handle_then_empty_string()
    {
        var topic = "c0418-keys-" + Guid.NewGuid().ToString("N");
        using (var admin = new AdminClientBuilder(new AdminClientConfig
               { BootstrapServers = _broker!.GetBootstrapAddress() }).Build())
            await admin.CreateTopicsAsync([new TopicSpecification
                { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
        var options = Options.Create(new AntiphonMessagingOptions
        {
            BootstrapServers = _broker!.GetBootstrapAddress(), OutboundTopic = topic,
        });
        using (var producer = new KafkaAntiphonMessagingProducer(options))
        {
            await producer.SendAsync(new ChannelReply
                { Channel = "slack", ConversationId = "conversation", ReplyHandle = "handle" });
            await producer.SendAsync(new ChannelReply
                { Channel = "slack", ReplyHandle = "handle" });
            await producer.SendAsync(new ChannelReply { Channel = "slack" });
        }
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _broker!.GetBootstrapAddress(),
            GroupId = "c0418-key-group-" + Guid.NewGuid().ToString("N"),
            AutoOffsetReset = AutoOffsetReset.Earliest, EnableAutoCommit = false,
        }).Build();
        consumer.Subscribe(topic);
        foreach (var key in new[] { "conversation", "handle", "" })
        {
            var record = consumer.Consume(TimeSpan.FromSeconds(30));
            record.ShouldNotBeNull();
            record.Topic.ShouldBe(topic);
            record.Message.Key.ShouldBe(key);
        }
    }

    [Test]
    public async Task Broker_gateway_and_slack_upload_frozen_thread_and_exact_files()
    {
        var topic = "c0418-gateway-" + Guid.NewGuid().ToString("N");
        using (var admin = new AdminClientBuilder(new AdminClientConfig
               { BootstrapServers = _broker!.GetBootstrapAddress() }).Build())
            await admin.CreateTopicsAsync([new TopicSpecification
                { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        using var http = new HttpClient();
        var slack = new SlackChannelAdapter(http, new SlackSettings
        {
            ApiBaseUrl = fake.ApiBaseUrl, BotToken = fake.BotToken,
            AppToken = fake.AppToken, ErrorBackoffSeconds = 0,
        }, NullLogger<SlackChannelAdapter>.Instance);
        var gateway = new GatewayOutboundService([slack], Options.Create(new AntiphonGatewayOptions
        {
            BootstrapServers = _broker!.GetBootstrapAddress(), OutboundTopic = topic,
            ConsumerGroup = "c0418-gateway-group-" + Guid.NewGuid().ToString("N"),
            AutoOffsetReset = "Earliest",
        }), NullLogger<GatewayOutboundService>.Instance);
        await gateway.StartAsync(CancellationToken.None);
        try
        {
            var files = new[]
            {
                ("requirements.md", "# Requirements ✨\r\n"u8.ToArray()),
                ("design.md", "# Design\n"u8.ToArray()),
                ("external-api.md", "# External API\n"u8.ToArray()),
                ("current-snapshots.md", "# Current snapshots\n"u8.ToArray()),
                ("combined.pdf", "%PDF-1.4 synthetic bytes"u8.ToArray()),
            };
            var reply = new ChannelReply
            {
                Channel = "slack", ConversationId = "C0418",
                ReplyHandle = "C0418|1700000000.000100", Text = "Sources and converted file",
                Attachments = files.Select(f => new OutboundAttachment
                {
                    Kind = AttachmentKind.File, Name = f.Item1,
                    Mime = f.Item1.EndsWith(".pdf", StringComparison.Ordinal)
                        ? "application/pdf" : "text/markdown",
                    Content = f.Item2,
                }).ToArray(),
            };
            using (var producer = new KafkaAntiphonMessagingProducer(Options.Create(
                       new AntiphonMessagingOptions
                       {
                           BootstrapServers = _broker.GetBootstrapAddress(), OutboundTopic = topic,
                       })))
                await producer.SendAsync(reply);

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (fake.UploadedFiles.Count < files.Length)
            {
                deadline.Token.ThrowIfCancellationRequested();
                await Task.Delay(50, deadline.Token);
            }
            var sent = fake.SentMessages.ShouldHaveSingleItem();
            sent.Channel.ShouldBe("C0418");
            sent.ThreadTs.ShouldBe("1700000000.000100");
            var uploaded = fake.UploadedFiles;
            uploaded.Count.ShouldBe(files.Length);
            for (var i = 0; i < files.Length; i++)
            {
                uploaded[i].Title.ShouldBe(files[i].Item1);
                uploaded[i].Bytes.ShouldBe(files[i].Item2);
                uploaded[i].ChannelId.ShouldBe("C0418");
                uploaded[i].ThreadTs.ShouldBe("1700000000.000100");
            }
        }
        finally { await gateway.StopAsync(CancellationToken.None); }
    }
}
