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
        logs.ShouldContain(l => l.Contains("[inbox] persist failed") && l.Contains("662"));
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
