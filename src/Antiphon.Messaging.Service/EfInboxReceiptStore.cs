using Antiphon.Messaging;
using Antiphon.Messaging.Gateway;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Messaging.Service;

public sealed class EfInboxReceiptStore(
    IServiceScopeFactory scopeFactory,
    ILogger<EfInboxReceiptStore> logger) : IInboxReceiptStore, IInboundReceiptSink
{
    public async Task RecordAsync(
        ChannelMessage message, string envelopeJson, string topic, int partition, long offset, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
        // Ingress and the Kafka consumer both own this receipt. Insert or backfill its coordinates
        // in one statement so racing writers never provoke a database error for a redelivery.
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Inbox" ("Id", "Channel", "ConversationId", "ChannelMessageId", "ConversationTitle",
                "AuthorDisplay", "Text", "MentionsMe", "HasAttachments", "ReplyHandle", "Status",
                "ReceivedAt", "EnvelopeJson", "Topic", "Partition", "Offset", "AckAttemptCount")
            VALUES ({Guid.NewGuid()}, {message.Channel}, {message.Conversation.Id}, {message.ChannelMessageId},
                {message.Conversation.Title}, {message.Author.DisplayName}, {message.Text}, {message.Mentions.Any(m => m.IsMe)},
                {message.Attachments.Count > 0}, {message.ReplyHandle}, {nameof(InboxStatus.Pending)},
                {message.Timestamp.ToUniversalTime()}, {envelopeJson}, {topic}, {partition}, {offset}, 0)
            ON CONFLICT ("Channel", "ConversationId", "ChannelMessageId") DO UPDATE SET
                "Topic" = excluded."Topic", "Partition" = excluded."Partition", "Offset" = excluded."Offset"
            WHERE "Inbox"."Offset" IS NULL
            """, cancellationToken);
        if (changed == 0)
            logger.LogDebug("[inbox] duplicate receipt for {Channel} {MessageId} ignored",
                message.Channel, message.ChannelMessageId);
    }

    public async Task<IReadOnlyList<InboundReceipt>> GetOverdueAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
        return await db.Inbox.AsNoTracking()
            .Where(x => x.ReceivedAt <= cutoff && x.Topic != null && x.Offset != null)
            .Select(x => new InboundReceipt
            {
                Id = x.Id,
                Channel = x.Channel,
                ChannelMessageId = x.ChannelMessageId,
                ConversationId = x.ConversationId,
                ReplyHandle = x.ReplyHandle,
                FirstSeenAt = x.ReceivedAt,
                Topic = x.Topic!,
                Partition = x.Partition ?? 0,
                Offset = x.Offset!.Value,
                AcknowledgedAt = x.AcknowledgedAt,
                OperationalEventPublishedAt = x.OperationalEventPublishedAt,
                NextAckAttemptAt = x.NextAckAttemptAt,
                AckAttemptCount = x.AckAttemptCount,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task MarkAcknowledgedAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
        await db.Inbox.Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.AcknowledgedAt, at)
                .SetProperty(x => x.NextAckAttemptAt, (DateTimeOffset?)null), cancellationToken);
    }

    public async Task MarkOperationalEventPublishedAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
        await db.Inbox.Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.OperationalEventPublishedAt, at), cancellationToken);
    }

    public async Task ScheduleAckRetryAsync(Guid id, DateTimeOffset nextAttemptAt, int attemptCount, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
        await db.Inbox.Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.NextAckAttemptAt, nextAttemptAt)
                .SetProperty(x => x.AckAttemptCount, attemptCount), cancellationToken);
    }
}
