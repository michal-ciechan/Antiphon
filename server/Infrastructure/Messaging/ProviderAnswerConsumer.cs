using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Infrastructure.Data;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Messaging;

internal sealed class ProviderAnswerConsumer(IServiceScopeFactory scopes, IOptions<AntiphonMessagingOptions> options,
    ILogger<ProviderAnswerConsumer> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => ConsumeAsync(stoppingToken), stoppingToken);

    private async Task ConsumeAsync(CancellationToken ct)
    {
        var settings = options.Value;
        var config = new ConsumerConfig
        {
            BootstrapServers = settings.BootstrapServers, GroupId = settings.ConsumerGroup + "-provider-acceptances",
            EnableAutoCommit = false, EnableAutoOffsetStore = false, AutoOffsetReset = AutoOffsetReset.Earliest,
        };
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var consumer = new ConsumerBuilder<string, string>(config).Build();
                consumer.Subscribe(ProviderAnswerReceipt.Topic(settings.OutboundTopic));
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var record = consumer.Consume(TimeSpan.FromMilliseconds(500));
                        if (record == null) continue;
                        ProviderAnswerReceipt? receipt = null;
                        try
                        {
                            if (record.Message?.Value is { } json)
                                receipt = JsonSerializer.Deserialize<ProviderAnswerReceipt>(json, Antiphon.Messaging.MessagingJson.Options);
                        }
                        catch (JsonException) { logger.LogWarning("Malformed provider receipt at {Position}", record.TopicPartitionOffset); }
                        if (receipt is { Channel.Length: > 0, ConversationId.Length: > 0, ChannelMessageId.Length: > 0 })
                        {
                            using var scope = scopes.CreateScope();
                            await RecordAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), receipt, ct);
                        }
                        consumer.Commit(record);
                    }
                }
                finally { consumer.Close(); }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Provider receipt consumption interrupted");
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }

    internal static Task RecordAsync(AppDbContext db, ProviderAnswerReceipt receipt, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ChannelProviderAnswers" ("Provider", "ConversationId", "NativeMessageId", "AnsweredAt")
            VALUES ({receipt.Channel}, {receipt.ConversationId}, {receipt.ChannelMessageId}, {receipt.AnsweredAt.UtcDateTime})
            ON CONFLICT ("Provider", "ConversationId", "NativeMessageId") DO NOTHING
            """, ct);
}
