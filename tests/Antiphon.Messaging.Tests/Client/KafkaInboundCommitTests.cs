using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Messaging.Client.Testing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Testcontainers.Redpanda;
using TUnit.Core;

namespace Antiphon.Messaging.Tests.Client;

[Category("Integration")]
[Category("Slow")]
[NotInParallel]
public sealed class KafkaInboundCommitTests
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
    public async Task C593_ManualCommit_UsesAcceptedRecordAndAssignment()
    {
        var (topic, group) = await CreateTopicAsync();
        await ProduceAsync(topic, 0, "one");
        ConsumerConfig? captured = null;
        var client = Client(topic, group, config =>
        {
            captured = config;
            return new ConsumerBuilder<string, string>(config).Build();
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using (var e = client.ConsumeDeliveriesAsync(timeout.Token).GetAsyncEnumerator())
        {
            (await e.MoveNextAsync()).ShouldBeTrue();
            e.Current.Message.ShouldNotBeNull();
            captured!.EnableAutoCommit.ShouldBe(false);
            captured.EnableAutoOffsetStore.ShouldBe(false);
            // Dispose before acknowledgement: no committed offset, so the same native record replays.
        }
        var replay = Client(topic, group);
        await using (var e = replay.ConsumeDeliveriesAsync(timeout.Token).GetAsyncEnumerator())
        {
            (await e.MoveNextAsync()).ShouldBeTrue();
            e.Current.Message!.Text.ShouldBe("one");
            await e.Current.AcknowledgeAsync("accepted", timeout.Token);
            await e.Current.AcknowledgeAsync("accepted", timeout.Token);
        }
        (await CommittedAsync(topic, group, 0)).ShouldBe(1);

        var fake = new FakeAntiphonMessagingClient();
        fake.InjectTelegramText("c593", "replay me");
        fake.Complete();
        await using (var e = fake.ConsumeDeliveriesAsync().GetAsyncEnumerator())
        {
            (await e.MoveNextAsync()).ShouldBeTrue();
            e.Current.Message!.Text.ShouldBe("replay me");
        }
        fake.AcknowledgedCount.ShouldBe(0);
        await using (var e = fake.ConsumeDeliveriesAsync().GetAsyncEnumerator())
        {
            (await e.MoveNextAsync()).ShouldBeTrue();
            e.Current.Message!.Text.ShouldBe("replay me");
            await e.Current.AcknowledgeAsync("accepted");
        }
        fake.AcknowledgedCount.ShouldBe(1);

        var (rebalanceTopic, rebalanceGroup) = await CreateTopicAsync();
        await ProduceAsync(rebalanceTopic, 0, "revoked delivery");
        Action? revoke = null;
        var rebalanceSettings = Options.Create(new AntiphonMessagingOptions
        {
            BootstrapServers = _broker!.GetBootstrapAddress(),
            InboundTopic = rebalanceTopic, ConsumerGroup = rebalanceGroup,
        });
        var rebalanceClient = new KafkaAntiphonMessagingConsumer(rebalanceSettings,
            NullLogger<KafkaAntiphonMessagingConsumer>.Instance, (config, callback) =>
            {
                revoke = callback;
                return new ConsumerBuilder<string, string>(config).Build();
            });
        await using (var e = rebalanceClient.ConsumeDeliveriesAsync(timeout.Token).GetAsyncEnumerator())
        {
            (await e.MoveNextAsync()).ShouldBeTrue();
            revoke.ShouldNotBeNull();
            revoke(); // broker revoked the assignment before this handle was acknowledged
            await Should.ThrowAsync<InvalidOperationException>(
                async () => await e.Current.AcknowledgeAsync("accepted", timeout.Token));
        }
        (await CommittedAsync(rebalanceTopic, rebalanceGroup, 0)).ShouldBeLessThanOrEqualTo(0);

        var (faultTopic, faultGroup) = await CreateTopicAsync();
        await ProduceAsync(faultTopic, 0, "commit response lost");
        CommitFailingConsumerProxy? faultProxy = null;
        var faultClient = Client(faultTopic, faultGroup, config =>
        {
            var proxy = DispatchProxy.Create<IConsumer<string, string>, CommitFailingConsumerProxy>();
            faultProxy = (CommitFailingConsumerProxy)(object)proxy;
            faultProxy.Inner = new ConsumerBuilder<string, string>(config).Build();
            return proxy;
        });
        await using (var e = faultClient.ConsumeDeliveriesAsync(timeout.Token).GetAsyncEnumerator())
        {
            (await e.MoveNextAsync()).ShouldBeTrue();
            faultProxy!.FailNextCommit();
            await Should.ThrowAsync<InvalidOperationException>(
                async () => await e.Current.AcknowledgeAsync("accepted", timeout.Token));
            (await CommittedAsync(faultTopic, faultGroup, 0)).ShouldBeLessThanOrEqualTo(0);
            await e.Current.AcknowledgeAsync("accepted", timeout.Token);
        }
        (await CommittedAsync(faultTopic, faultGroup, 0)).ShouldBe(1);
    }

    [Test]
    public async Task C593_BrokerRestart_ReplaysOnlyUnacknowledgedOffsets()
    {
        var (topic, group) = await CreateTopicAsync(2);
        await ProduceAsync(topic, 0, "first");
        await ProduceAsync(topic, 0, "second");
        await ProduceAsync(topic, 1, "other partition");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var first = Client(topic, group);
        await using (var e = first.ConsumeDeliveriesAsync(timeout.Token).GetAsyncEnumerator())
        {
            (await e.MoveNextAsync()).ShouldBeTrue();
            // Keep partition zero's first record unresolved.
        }
        (await CommittedAsync(topic, group, 0)).ShouldBeLessThanOrEqualTo(0);
        var restart = Client(topic, group);
        await using (var e = restart.ConsumeDeliveriesAsync(timeout.Token).GetAsyncEnumerator())
        {
            var found = false;
            for (var i = 0; i < 3 && !found; i++)
            {
                (await e.MoveNextAsync()).ShouldBeTrue();
                if (e.Current.Message!.Text != "first")
                {
                    await e.Current.AcknowledgeAsync("accepted", timeout.Token);
                    continue;
                }
                found = true;
                await e.Current.AcknowledgeAsync("accepted", timeout.Token);
            }
            found.ShouldBeTrue();
        }
        (await CommittedAsync(topic, group, 0)).ShouldBe(1);

        var (gapTopic, gapGroup) = await CreateTopicAsync();
        await ProduceAsync(gapTopic, 0, "gap first");
        await ProduceAsync(gapTopic, 0, "gap second");
        var gapClient = Client(gapTopic, gapGroup);
        await using (var e = gapClient.ConsumeDeliveriesAsync(timeout.Token).GetAsyncEnumerator())
        {
            (await e.MoveNextAsync()).ShouldBeTrue();
            e.Current.Message!.Text.ShouldBe("gap first");
            var unresolved = e.Current;
            (await e.MoveNextAsync()).ShouldBeTrue();
            e.Current.Message!.Text.ShouldBe("gap second");
            await e.Current.AcknowledgeAsync("accepted", timeout.Token);
            (await CommittedAsync(gapTopic, gapGroup, 0)).ShouldBeLessThanOrEqualTo(0);
            await unresolved.AcknowledgeAsync("accepted", timeout.Token);
        }
        (await CommittedAsync(gapTopic, gapGroup, 0)).ShouldBe(2);

        var (poisonTopic, poisonGroup) = await CreateTopicAsync();
        await ProduceRawAsync(poisonTopic, 0, "{invalid-json");
        await ProduceAsync(poisonTopic, 0, "valid after poison");
        var poisonClient = Client(poisonTopic, poisonGroup);
        await using (var e = poisonClient.ConsumeDeliveriesAsync(timeout.Token).GetAsyncEnumerator())
        {
            (await e.MoveNextAsync()).ShouldBeTrue();
            e.Current.Message.ShouldBeNull();
            e.Current.Diagnostic.ShouldBe("malformed-json");
        }
        (await CommittedAsync(poisonTopic, poisonGroup, 0)).ShouldBeLessThanOrEqualTo(0);
        var poisonReplay = Client(poisonTopic, poisonGroup);
        await using (var e = poisonReplay.ConsumeDeliveriesAsync(timeout.Token).GetAsyncEnumerator())
        {
            (await e.MoveNextAsync()).ShouldBeTrue();
            e.Current.Diagnostic.ShouldBe("malformed-json");
            await e.Current.AcknowledgeAsync("malformed:malformed-json", timeout.Token);
            (await CommittedAsync(poisonTopic, poisonGroup, 0)).ShouldBe(1);
            (await e.MoveNextAsync()).ShouldBeTrue();
            e.Current.Message!.Text.ShouldBe("valid after poison");
            await e.Current.AcknowledgeAsync("accepted", timeout.Token);
        }
        (await CommittedAsync(poisonTopic, poisonGroup, 0)).ShouldBe(2);
    }

    private static KafkaAntiphonMessagingConsumer Client(string topic, string group,
        Func<ConsumerConfig, IConsumer<string, string>>? factory = null)
    {
        var settings = Options.Create(new AntiphonMessagingOptions
        {
            BootstrapServers = _broker!.GetBootstrapAddress(), InboundTopic = topic, ConsumerGroup = group,
        });
        return factory is null
            ? new KafkaAntiphonMessagingConsumer(settings, NullLogger<KafkaAntiphonMessagingConsumer>.Instance)
            : new KafkaAntiphonMessagingConsumer(settings, NullLogger<KafkaAntiphonMessagingConsumer>.Instance, factory);
    }

    private static async Task<(string Topic, string Group)> CreateTopicAsync(int partitions = 1)
    {
        var topic = "c593-" + Guid.NewGuid().ToString("N");
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = _broker!.GetBootstrapAddress() }).Build();
        await admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = partitions, ReplicationFactor = 1 }]);
        return (topic, "c593-group-" + Guid.NewGuid().ToString("N"));
    }

    private static async Task ProduceAsync(string topic, int partition, string text)
    {
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = _broker!.GetBootstrapAddress() }).Build();
        var message = new ChannelMessage
        {
            Id = Guid.NewGuid().ToString("N"), Channel = "telegram", ChannelMessageId = Guid.NewGuid().ToString("N"),
            Conversation = new Conversation { Id = "c593", Kind = ConversationKind.Group },
            Author = new Participant { Id = "sender" }, Timestamp = DateTimeOffset.UtcNow, Text = text,
            ReplyHandle = "c593", Raw = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone(),
        };
        await producer.ProduceAsync(new TopicPartition(topic, partition), new Message<string, string>
        {
            Key = "c593", Value = System.Text.Json.JsonSerializer.Serialize(message, Antiphon.Messaging.MessagingJson.Options),
        });
    }

    private static async Task ProduceRawAsync(string topic, int partition, string value)
    {
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _broker!.GetBootstrapAddress(),
        }).Build();
        await producer.ProduceAsync(new TopicPartition(topic, partition), new Message<string, string>
        {
            Key = "c593", Value = value,
        });
    }

    private static async Task<long> CommittedAsync(string topic, string group, int partition)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = _broker!.GetBootstrapAddress() }).Build();
        var results = await admin.ListConsumerGroupOffsetsAsync(
            [new ConsumerGroupTopicPartitions(group, [new TopicPartition(topic, partition)])]);
        return results.SelectMany(r => r.Partitions).Single().Offset.Value;
    }

    public class CommitFailingConsumerProxy : DispatchProxy
    {
        public IConsumer<string, string> Inner { get; set; } = null!;
        private int _failNextCommit;
        public void FailNextCommit() => Interlocked.Exchange(ref _failNextCommit, 1);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) throw new InvalidOperationException("Consumer method is missing.");
            if (targetMethod.Name == nameof(IConsumer<string, string>.Commit)
                && Interlocked.Exchange(ref _failNextCommit, 0) == 1)
                throw new InvalidOperationException("synthetic broker commit failure");
            try { return targetMethod.Invoke(Inner, args); }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
    }
}
