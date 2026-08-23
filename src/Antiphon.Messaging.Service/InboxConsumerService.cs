using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Gateway;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Messaging.Service;

/// <summary>Consumes the inbound topic and persists each message to the Postgres inbox (idempotent).</summary>
public sealed class InboxConsumerService(
    IServiceScopeFactory scopeFactory,
    IOptions<AntiphonGatewayOptions> options,
    JsonSerializerOptions json,
    ILogger<InboxConsumerService> logger) : BackgroundService
{
    private readonly AntiphonGatewayOptions _options = options.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => ConsumeLoopAsync(stoppingToken), stoppingToken);

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = $"{_options.ConsumerGroup}-inbox",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
            // Match the bus cap (20 MB) — inbound messages may carry attachment payloads too.
            MaxPartitionFetchBytes = _options.MaxMessageBytes,
            FetchMaxBytes = Math.Max(_options.MaxMessageBytes, 50 * 1024 * 1024),
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_options.ResolveInboundTopic());
        logger.LogInformation("[inbox] consuming {Topic}", _options.ResolveInboundTopic());

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
