using System.Text.Json;
using Antiphon.Messaging.Gateway;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Messaging.Service;

internal sealed class ProviderAnswerPublisher(IServiceScopeFactory scopes, IProducer<string, string> producer,
    IOptions<AntiphonGatewayOptions> options, ILogger<ProviderAnswerPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await PublishPageAsync(stoppingToken) == 128) continue;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Publishing durable provider receipts failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    internal async Task<int> PublishPageAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
        var pending = await db.InboxAnswerReceipts.AsNoTracking().Where(a => a.PublishedAt == null)
            .OrderBy(a => a.Channel).ThenBy(a => a.ConversationId).ThenBy(a => a.ChannelMessageId)
            .Take(128).Select(a => new ProviderAnswerReceipt
            {
                Channel = a.Channel, ConversationId = a.ConversationId, ChannelMessageId = a.ChannelMessageId,
                AnsweredAt = a.AnsweredAt,
            }).ToListAsync(ct);
        foreach (var receipt in pending)
        {
            await producer.ProduceAsync(ProviderAnswerReceipt.Topic(options.Value.ResolveOutboundTopic()),
                new Message<string, string> { Key = receipt.ConversationId, Value = JsonSerializer.Serialize(receipt, MessagingJson.Options) }, ct);
            var publishedAt = DateTimeOffset.UtcNow;
            await db.InboxAnswerReceipts.Where(a => a.Channel == receipt.Channel && a.ConversationId == receipt.ConversationId
                && a.ChannelMessageId == receipt.ChannelMessageId && a.PublishedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.PublishedAt, publishedAt), ct);
        }
        return pending.Count;
    }
}
