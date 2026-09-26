using System.Runtime.CompilerServices;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Messaging.Client;

/// <summary>Kafka inbound records are committed only after the bridge acknowledges a durable disposition.</summary>
public sealed class KafkaAntiphonMessagingConsumer : IAntiphonMessagingConsumer
{
    private readonly AntiphonMessagingOptions _options;
    private readonly ILogger<KafkaAntiphonMessagingConsumer> _logger;
    private readonly Func<ConsumerConfig, IConsumer<string, string>> _buildConsumer;

    public KafkaAntiphonMessagingConsumer(
        IOptions<AntiphonMessagingOptions> options,
        ILogger<KafkaAntiphonMessagingConsumer> logger)
        : this(options, logger, config => new ConsumerBuilder<string, string>(config).Build()) { }

    /// <summary>Injectable construction keeps the actual commit and configuration contract observable.</summary>
    public KafkaAntiphonMessagingConsumer(
        IOptions<AntiphonMessagingOptions> options,
        ILogger<KafkaAntiphonMessagingConsumer> logger,
        Func<ConsumerConfig, IConsumer<string, string>> buildConsumer)
    {
        _options = options.Value;
        _logger = logger;
        _buildConsumer = buildConsumer;
    }

    public async IAsyncEnumerable<ChannelMessage> ConsumeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var delivery in ConsumeDeliveriesAsync(cancellationToken))
            if (delivery.Message is { } message)
                yield return message;
    }

    public async IAsyncEnumerable<InboundDelivery> ConsumeDeliveriesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            MaxPartitionFetchBytes = _options.MaxMessageBytes,
            FetchMaxBytes = Math.Max(_options.MaxMessageBytes, 50 * 1024 * 1024),
        };

        using var consumer = _buildConsumer(config);
        consumer.Subscribe(_options.InboundTopic);
        var pending = new Dictionary<TopicPartition, SortedDictionary<long, bool>>();
        var assignment = consumer.Assignment.ToHashSet();
        var generation = 0L;
        _logger.LogInformation("[antiphon] consuming {Topic} as {Group}",
            _options.InboundTopic, _options.ConsumerGroup);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await PollAsync(consumer, cancellationToken);
                if (result is null)
                    continue;
                var currentAssignment = consumer.Assignment.ToHashSet();
                if (!assignment.SetEquals(currentAssignment))
                {
                    generation++;
                    pending.Clear();
                    assignment = currentAssignment;
                }
                if (result.IsPartitionEOF)
                    continue;

                var partition = result.TopicPartition;
                if (!pending.TryGetValue(partition, out var offsets))
                    pending[partition] = offsets = new SortedDictionary<long, bool>();
                var offset = result.Offset.Value;
                offsets.TryAdd(offset, false);
                var recordGeneration = generation;
                var (message, diagnostic) = Deserialize(result.Message?.Value);

                yield return new InboundDelivery(message, diagnostic, (_, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    if (recordGeneration != generation || !consumer.Assignment.Contains(partition))
                        throw new InvalidOperationException("Inbound record assignment was revoked.");
                    if (!pending.TryGetValue(partition, out var live) || !live.ContainsKey(offset))
                        return Task.CompletedTask;
                    live[offset] = true;
                    var next = live.First().Key;
                    foreach (var item in live)
                    {
                        if (item.Key != next || !item.Value)
                            break;
                        next++;
                    }
                    if (next == live.First().Key)
                        return Task.CompletedTask;
                    consumer.Commit([new TopicPartitionOffset(partition, new Offset(next))]);
                    foreach (var key in live.Keys.Where(k => k < next).ToArray())
                        live.Remove(key);
                    return Task.CompletedTask;
                });
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    private static async Task<ConsumeResult<string, string>?> PollAsync(
        IConsumer<string, string> consumer, CancellationToken ct)
    {
        try { return await Task.Run(() => consumer.Consume(TimeSpan.FromMilliseconds(500)), ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return null; }
    }

    private (ChannelMessage?, string?) Deserialize(string? value)
    {
        if (value is null)
            return (null, "null-value");
        try
        {
            var message = JsonSerializer.Deserialize<ChannelMessage>(
                value, global::Antiphon.Messaging.MessagingJson.Options);
            if (message is null || string.IsNullOrWhiteSpace(message.Channel)
                || string.IsNullOrWhiteSpace(message.ChannelMessageId)
                || string.IsNullOrWhiteSpace(message.Conversation?.Id)
                || string.IsNullOrWhiteSpace(message.Author?.Id))
                return (null, "missing-identity");
            return (message, null);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[antiphon] malformed inbound record requires disposition");
            return (null, "malformed-json");
        }
    }
}
