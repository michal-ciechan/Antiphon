using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Messaging.Service;

/// <summary>Consumes the inbound topic and persists each message to the Postgres inbox (idempotent).</summary>
public sealed class InboxConsumerService(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaSettings> kafka,
    JsonSerializerOptions json,
    ILogger<InboxConsumerService> logger) : BackgroundService
{
    private readonly KafkaSettings _kafka = kafka.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => ConsumeLoopAsync(stoppingToken), stoppingToken);

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _kafka.BootstrapServers,
            GroupId = $"{_kafka.ConsumerGroup}-inbox",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_kafka.InboundTopic);
        logger.LogInformation("[inbox] consuming {Topic}", _kafka.InboundTopic);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                }
                catch (ConsumeException ex)
                {
                    logger.LogWarning(ex, "[inbox] consume error");
                    continue;
                }

                if (result?.Message?.Value is null)
                    continue;

                ChannelMessage? message;
                try
                {
                    message = JsonSerializer.Deserialize<ChannelMessage>(result.Message.Value, json);
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "[inbox] could not parse envelope, skipping");
                    continue;
                }

                if (message is not null)
                    await PersistAsync(message, result.Message.Value, ct);
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task PersistAsync(ChannelMessage message, string envelopeJson, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();

        var exists = await db.Inbox.AnyAsync(
            x => x.Channel == message.Channel && x.ChannelMessageId == message.ChannelMessageId, ct);
        if (exists)
            return;

        db.Inbox.Add(new InboxMessage
        {
            Id = Guid.NewGuid(),
            Channel = message.Channel,
            ChannelMessageId = message.ChannelMessageId,
            ConversationId = message.Conversation.Id,
            ConversationTitle = message.Conversation.Title,
            AuthorDisplay = message.Author.DisplayName,
            Text = message.Text,
            MentionsMe = message.Mentions.Any(m => m.IsMe),
            HasAttachments = message.Attachments.Count > 0,
            ReplyHandle = message.ReplyHandle,
            Status = InboxStatus.Pending,
            ReceivedAt = message.Timestamp.ToUniversalTime(),
            EnvelopeJson = envelopeJson,
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("[inbox] stored {Channel} {MessageId}", message.Channel, message.ChannelMessageId);
    }
}
