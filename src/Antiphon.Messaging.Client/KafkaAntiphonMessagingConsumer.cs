using System.Runtime.CompilerServices;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Messaging.Client;

/// <summary>Kafka-backed <see cref="IAntiphonMessagingConsumer"/>. Polls the inbound topic and yields parsed
/// <see cref="ChannelMessage"/>s; malformed payloads are logged and skipped.</summary>
public sealed class KafkaAntiphonMessagingConsumer : IAntiphonMessagingConsumer
{
    private readonly AntiphonMessagingOptions _options;
    private readonly ILogger<KafkaAntiphonMessagingConsumer> _logger;

    public KafkaAntiphonMessagingConsumer(IOptions<AntiphonMessagingOptions> options, ILogger<KafkaAntiphonMessagingConsumer> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async IAsyncEnumerable<ChannelMessage> ConsumeAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_options.InboundTopic);
        _logger.LogInformation("[antiphon] consuming {Topic} as {Group}", _options.InboundTopic, _options.ConsumerGroup);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await PollAsync(consumer, cancellationToken);
                if (result?.Message?.Value is null)
                    continue;

                var message = TryDeserialize(result.Message.Value);
                if (message is not null)
                    yield return message;
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    private static async Task<ConsumeResult<string, string>?> PollAsync(IConsumer<string, string> consumer, CancellationToken ct)
    {
        try
        {
            return await Task.Run(() => consumer.Consume(TimeSpan.FromMilliseconds(500)), ct);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private ChannelMessage? TryDeserialize(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<ChannelMessage>(value, MessagingJson.Options);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[antiphon] could not parse inbound message; skipping");
            return null;
        }
    }
}
