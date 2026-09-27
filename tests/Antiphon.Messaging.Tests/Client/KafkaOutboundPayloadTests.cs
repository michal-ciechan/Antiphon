using System.Text.Json;
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
