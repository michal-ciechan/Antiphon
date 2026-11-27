using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Gateway;
using Antiphon.Messaging.Service;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Testcontainers.Redpanda;
using TUnit.Core;

namespace Antiphon.Messaging.Tests.Service;

/// <summary>
/// CARD-0503: <see cref="InboxConsumerService"/> is a <see cref="BackgroundService"/>, so an
/// uncaught persist exception faults ExecuteAsync and the default
/// BackgroundServiceExceptionBehavior.StopHost takes the whole am-service process down — the
/// observed production crash-loop. The ingress-side sink catch (GatewayTests) does not cover
/// this path: it is a different service in a different process role.
/// </summary>
[NotInParallel]
public sealed class InboxConsumerServiceTests
{
    private static RedpandaContainer? _broker;

    [Before(Class)]
    public static async Task StartAsync()
    {
        if (Environment.GetEnvironmentVariable("ANTIPHON_BROKER_TESTS") != "1") return;
        _broker = new RedpandaBuilder("docker.redpanda.com/redpandadata/redpanda:v25.3.4").Build();
        await _broker.StartAsync();
    }

    [After(Class)]
    public static async Task StopAsync()
    {
        if (_broker is not null) await _broker.DisposeAsync();
    }

    [Test]
    public async Task Two_chats_with_the_same_message_id_are_stored_after_broker_publication()
    {
        if (_broker is null) Skip.Test("set ANTIPHON_BROKER_TESTS=1 with Docker running");
        var bootstrap = _broker!.GetBootstrapAddress();
        var topic = $"c1248-inbound-{Guid.NewGuid():N}";
        using (var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrap }).Build())
            await admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
        using (var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = bootstrap }).Build())
        {
            foreach (var chat in new[] { "chat-1", "chat-2", "chat-2" })
                await producer.ProduceAsync(topic, new Message<string, string>
                {
                    Key = chat,
                    Value = JsonSerializer.Serialize(EfInboxReceiptStoreTests.Sample("1068", chat), MessagingJson.Options),
                });
        }

        await using var store = await EfInboxReceiptStoreTests.StoreHarness.CreateAsync();
        var sink = new ObservedSink(store.Store);
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddLogging();
        builder.Services.AddSingleton(MessagingJson.Options);
        builder.Services.AddSingleton<IInboundReceiptSink>(sink);
        builder.Services.AddSingleton(Options.Create(new AntiphonGatewayOptions
        {
            BootstrapServers = bootstrap, InboundTopic = topic, ConsumerGroup = $"c1248-{Guid.NewGuid():N}",
        }));
        builder.Services.AddHostedService<InboxConsumerService>();
        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            await sink.Completed.Task.WaitAsync(TimeSpan.FromSeconds(45));
        }
        finally
        {
            await host.StopAsync(TimeSpan.FromSeconds(15));
        }

        var rows = await store.LoadAsync();
        rows.Count.ShouldBe(2);
        rows.Single(x => x.ConversationId == "chat-1").Offset.ShouldBe(0);
        rows.Single(x => x.ConversationId == "chat-2").Offset.ShouldBe(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Outbound_replay_after_provider_acceptance_never_calls_the_provider_twice(bool lostResponse)
    {
        if (_broker is null) Skip.Test("set ANTIPHON_BROKER_TESTS=1 with Docker running");
        var bootstrap = _broker!.GetBootstrapAddress();
        var topic = $"outbound-{Guid.NewGuid():N}";
        using (var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrap }).Build())
            await admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = bootstrap }).Build();
        var reply = new ChannelReply { Channel = "telegram", ConversationId = "11", AnsweredMessageIds = ["12"] };
        await producer.ProduceAsync(topic, new Message<string, string>
        {
            Key = "11", Value = JsonSerializer.Serialize(reply, MessagingJson.Options),
        });
        var afterJournal = new PauseAfterAcceptanceCommit();
        await using var store = await EfInboxReceiptStoreTests.StoreHarness.CreateAsync(interceptor: afterJournal);
        await store.Store.RecordAsync(EfInboxReceiptStoreTests.Sample("12", "11"), "{}", "in", 0, 0, CancellationToken.None);
        afterJournal.Armed = !lostResponse;
        var adapter = new AcceptanceProbe(lostResponse);
        GatewayOutboundService NewConsumer(string group) => new([adapter], Options.Create(new AntiphonGatewayOptions
        {
            BootstrapServers = bootstrap, ConsumerGroup = group, OutboundTopic = topic,
        }), Microsoft.Extensions.Logging.Abstractions.NullLogger<GatewayOutboundService>.Instance, store.Store);
        // A new group reads the exact same record even if the first host committed its offset.
        // The second record is a barrier proving the replay was actually examined.
        var group = Guid.NewGuid().ToString("N");
        using (var first = NewConsumer(group))
        {
            await first.StartAsync(CancellationToken.None);
            try
            {
                await adapter.Accepted.Task.WaitAsync(TimeSpan.FromSeconds(45));
                if (!lostResponse)
                    await afterJournal.Committed.Task.WaitAsync(TimeSpan.FromSeconds(15));
                using var observer = new ConsumerBuilder<string, string>(new ConsumerConfig
                {
                    BootstrapServers = bootstrap, GroupId = group + "-outbound", EnableAutoCommit = false,
                }).Build();
                var partition = new TopicPartition(topic, new Partition(0));
                observer.Committed([partition], TimeSpan.FromSeconds(5)).Single().Offset.ShouldBe(Offset.Unset);
            }
            finally { await first.StopAsync(CancellationToken.None); }
        }
        await producer.ProduceAsync(topic, new Message<string, string>
        {
            Key = "11", Value = JsonSerializer.Serialize(reply with { AnsweredMessageIds = ["13"] }, MessagingJson.Options),
        });
        using (var replay = NewConsumer(Guid.NewGuid().ToString("N")))
        {
            await replay.StartAsync(CancellationToken.None);
            try { await adapter.Barrier.Task.WaitAsync(TimeSpan.FromSeconds(45)); }
            finally { await replay.StopAsync(CancellationToken.None); }
        }
        adapter.Calls.ShouldBe(1, "one broker operation can enter the provider only once");
        (await store.LoadAsync()).Single().Status.ShouldBe(lostResponse ? InboxStatus.Pending : InboxStatus.Answered);
        // Reconcile the interrupted operation using independently observed native acceptance.
        (await store.Store.ConfirmSendAsync(topic + "/0/0", SendResult.Sent("14"), CancellationToken.None)).ShouldBeTrue();
        (await store.Store.ConfirmSendAsync(topic + "/0/0", SendResult.Sent("15"), CancellationToken.None)).ShouldBeFalse();
        (await store.LoadAsync()).Single().Status.ShouldBe(InboxStatus.Answered);
        adapter.Calls.ShouldBe(1);
    }

    private sealed class PauseAfterAcceptanceCommit : Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor
    {
        public bool Armed;
        public TaskCompletionSource Committed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task TransactionCommittedAsync(System.Data.Common.DbTransaction transaction,
            Microsoft.EntityFrameworkCore.Diagnostics.TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Armed && Committed.TrySetResult()) await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    [Test]
    public async Task Provider_receipt_feed_exports_only_accepted_answers_and_survives_replay()
    {
        if (_broker is null) Skip.Test("set ANTIPHON_BROKER_TESTS=1 with Docker running");
        var bootstrap = _broker!.GetBootstrapAddress();
        var topic = "outbound-" + Guid.NewGuid().ToString("N");
        var options = Options.Create(new AntiphonGatewayOptions { BootstrapServers = bootstrap, OutboundTopic = topic });
        var feed = ProviderAnswerReceipt.Topic(topic);
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrap }).Build();
        await admin.CreateTopicsAsync([new TopicSpecification { Name = feed, NumPartitions = 1, ReplicationFactor = 1 }]);
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = bootstrap }).Build();
        await using var store = await EfInboxReceiptStoreTests.StoreHarness.CreateAsync();
        var adapter = new AcceptanceProbe(false);
        using var gateway = new GatewayOutboundService([adapter], options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GatewayOutboundService>.Instance, store.Store);
        var reply = new ChannelReply { Channel = "telegram", ConversationId = "11", AnsweredMessageIds = ["12"] };
        await gateway.DispatchAsync(reply with { AnsweredMessageIds = ["refused"] }, CancellationToken.None);
        await gateway.DispatchAsync(reply, CancellationToken.None);
        var exporter = new ProviderAnswerPublisher(store.Services.GetRequiredService<IServiceScopeFactory>(), producer, options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ProviderAnswerPublisher>.Instance);
        (await exporter.PublishPageAsync(CancellationToken.None)).ShouldBe(1);
        (await exporter.PublishPageAsync(CancellationToken.None)).ShouldBe(0);
        using var reader = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrap, GroupId = Guid.NewGuid().ToString("N"), EnableAutoCommit = false,
        }).Build();
        reader.Assign(new TopicPartitionOffset(feed, 0, Offset.Beginning));
        var record = reader.Consume(TimeSpan.FromSeconds(10));
        var acceptance = JsonSerializer.Deserialize<ProviderAnswerReceipt>(record.Message.Value, MessagingJson.Options)!;
        acceptance.ChannelMessageId.ShouldBe("12");
        reader.QueryWatermarkOffsets(new TopicPartition(feed, 0), TimeSpan.FromSeconds(5)).High.Value.ShouldBe(1L);
    }

    private sealed class AcceptanceProbe(bool loseFirstResponse) : IChannelAdapter
    {
        public int Calls;
        public TaskCompletionSource Accepted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Barrier { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Channel => "telegram";
        public ChannelCapabilities Capabilities => new() { Channel = Channel };
        public IAsyncEnumerable<ChannelMessage> ReceiveAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public async Task<SendResult> SendAsync(ChannelReply reply, CancellationToken cancellationToken)
        {
            if (reply.AnsweredMessageIds.Single() == "refused") return SendResult.Failed("synthetic refusal");
            if (reply.AnsweredMessageIds.Single() == "13") Barrier.TrySetResult();
            else
            {
                var attempt = Interlocked.Increment(ref Calls);
                Accepted.TrySetResult();
                if (loseFirstResponse && attempt == 1)
                    await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            return SendResult.Sent("14");
        }
    }

    private sealed class ObservedSink(IInboundReceiptSink inner) : IInboundReceiptSink
    {
        private int _calls;
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task RecordAsync(ChannelMessage message, string envelopeJson, string topic, int partition,
            long offset, CancellationToken cancellationToken)
        {
            await inner.RecordAsync(message, envelopeJson, topic, partition, offset, cancellationToken);
            if (Interlocked.Increment(ref _calls) == 3) Completed.TrySetResult();
        }
    }

    [Test]
    public async Task Duplicate_key_persist_failure_does_not_stop_the_host()
    {
        if (_broker is null) Skip.Test("set ANTIPHON_BROKER_TESTS=1 with Docker running");

        var bootstrap = _broker!.GetBootstrapAddress();
        var topic = $"c0503-inbound-{Guid.NewGuid():N}";
        using (var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrap }).Build())
        {
            await admin.CreateTopicsAsync(
                [new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
        }

        using (var producer = new ProducerBuilder<string, string>(
                   new ProducerConfig { BootstrapServers = bootstrap }).Build())
        {
            foreach (var channelMessageId in new[] { "662", "663" })
            {
                await producer.ProduceAsync(topic, new Message<string, string>
                {
                    Key = "chat-1",
                    Value = JsonSerializer.Serialize(
                        EfInboxReceiptStoreTests.Sample(channelMessageId), MessagingJson.Options),
                });
            }
        }

        var sink = new FirstThrowSink(EfInboxReceiptStoreTests.DuplicateInboxException());
        var logs = new List<string>();

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddLogging();
        builder.Services.AddSingleton<ILogger<InboxConsumerService>>(new ListLogger<InboxConsumerService>(logs));
        builder.Services.AddSingleton(MessagingJson.Options);
        builder.Services.AddSingleton<IInboundReceiptSink>(sink);
        builder.Services.AddSingleton(Options.Create(new AntiphonGatewayOptions
        {
            BootstrapServers = bootstrap,
            InboundTopic = topic,
            ConsumerGroup = $"c0503-{Guid.NewGuid():N}",
        }));
        builder.Services.AddHostedService<InboxConsumerService>();

        using var host = builder.Build();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        await host.StartAsync();

        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline
               && !lifetime.ApplicationStopping.IsCancellationRequested
               && sink.Recorded.Count == 0)
        {
            await Task.Delay(100);
        }

        var hostStopped = lifetime.ApplicationStopping.IsCancellationRequested;
        await host.StopAsync(TimeSpan.FromSeconds(15));

        hostStopped.ShouldBeFalse("a persist failure faulted the BackgroundService and stopped the host");
        sink.Calls.ShouldBe(2);
        sink.Recorded.ShouldBe(["663"]);
        logs.ShouldContain(l => l.Contains("[inbox] duplicate receipt") && l.Contains("662"));
    }

    [Test]
    public async Task Non_duplicate_persist_failure_stops_the_host()
    {
        if (_broker is null) Skip.Test("set ANTIPHON_BROKER_TESTS=1 with Docker running");

        var bootstrap = _broker!.GetBootstrapAddress();
        var topic = $"c0503-inbound-{Guid.NewGuid():N}";
        using (var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrap }).Build())
        {
            await admin.CreateTopicsAsync(
                [new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
        }

        using (var producer = new ProducerBuilder<string, string>(
                   new ProducerConfig { BootstrapServers = bootstrap }).Build())
        {
            await producer.ProduceAsync(topic, new Message<string, string>
            {
                Key = "chat-1",
                Value = JsonSerializer.Serialize(
                    EfInboxReceiptStoreTests.Sample("662"), MessagingJson.Options),
            });
        }

        var sink = new FirstThrowSink(EfInboxReceiptStoreTests.InboxConstraintFailureWithoutUniqueSqlState());
        var logs = new List<string>();

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddLogging();
        builder.Services.AddSingleton<ILogger<InboxConsumerService>>(new ListLogger<InboxConsumerService>(logs));
        builder.Services.AddSingleton(MessagingJson.Options);
        builder.Services.AddSingleton<IInboundReceiptSink>(sink);
        builder.Services.AddSingleton(Options.Create(new AntiphonGatewayOptions
        {
            BootstrapServers = bootstrap,
            InboundTopic = topic,
            ConsumerGroup = $"c0503-{Guid.NewGuid():N}",
        }));
        builder.Services.AddHostedService<InboxConsumerService>();

        using var host = builder.Build();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        await host.StartAsync();

        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline && !lifetime.ApplicationStopping.IsCancellationRequested)
            await Task.Delay(100);

        var hostStopped = lifetime.ApplicationStopping.IsCancellationRequested;
        await host.StopAsync(TimeSpan.FromSeconds(15));

        hostStopped.ShouldBeTrue(
            "a non-duplicate persist failure must still crash-stop the host so Kafka does not auto-commit a dropped message");
        sink.Calls.ShouldBe(1);
        sink.Recorded.ShouldBeEmpty();
        logs.ShouldNotContain(l => l.Contains("[inbox] persist failed"));
    }

    private sealed class FirstThrowSink(Exception first) : IInboundReceiptSink
    {
        public int Calls;
        public List<string> Recorded { get; } = [];

        public Task RecordAsync(
            ChannelMessage message, string envelopeJson, string topic, int partition, long offset,
            CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref Calls);
            if (n == 1)
                throw first;
            Recorded.Add(message.ChannelMessageId);
            return Task.CompletedTask;
        }
    }

    private sealed class ListLogger<T>(List<string> sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (sink) sink.Add(formatter(state, exception));
        }
    }
}
