using Antiphon.Messaging;
using Antiphon.Messaging.Gateway;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Messaging.Service;

public sealed partial class EfInboxReceiptStore(
    IServiceScopeFactory scopeFactory,
    ILogger<EfInboxReceiptStore> logger) : IInboxReceiptStore, IInboundReceiptSink, IOutboundOperationJournal
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
        await ApplyAnswersAsync(db, message.Channel, message.Conversation.Id, message.ChannelMessageId, cancellationToken);
        if (changed == 0)
            logger.LogDebug("[inbox] duplicate receipt for {Channel} {MessageId} ignored",
                message.Channel, message.ChannelMessageId);
    }

    public async Task RecordAnswerAsync(ChannelReply reply, SendResult accepted, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
        await RecordAnswerIdsAsync(db, reply.Channel, reply.ConversationId, reply.AnsweredMessageIds,
            accepted.ChannelMessageId, DateTimeOffset.UtcNow, ct);
    }

    private static async Task RecordAnswerIdsAsync(MessagingDbContext db, string channel, string? conversation,
        IReadOnlyList<string> ids, string? providerId, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var native in ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "InboxAnswerReceipts" ("Channel", "ConversationId", "ChannelMessageId", "AnsweredAt", "OutboundMessageId")
                VALUES ({channel}, {conversation}, {native}, {now}, {providerId})
                ON CONFLICT ("Channel", "ConversationId", "ChannelMessageId") DO NOTHING
                """, ct);
            await ApplyAnswersAsync(db, channel, conversation, native, ct);
        }
    }

    // Each producer commits its own evidence before projecting. Either arrival order converges.
    // The sweep repairs interruption between recording evidence and updating the inbox.
    internal static Task<int> ApplyAnswersAsync(MessagingDbContext db, string? channel, string? conversation,
        string? native, CancellationToken ct)
    {
        var pending = db.Inbox.Where(i => i.Status == InboxStatus.Pending);
        if (channel != null)
            pending = pending.Where(i => i.Channel == channel && i.ConversationId == conversation && i.ChannelMessageId == native);
        var ids = pending.Where(i => db.InboxAnswerReceipts.Any(a => a.Channel == i.Channel
            && a.ConversationId == i.ConversationId && a.ChannelMessageId == i.ChannelMessageId))
            .OrderBy(i => i.Id).Take(256).Select(i => i.Id);
        return db.Inbox.Where(i => ids.Contains(i.Id) && i.Status == InboxStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InboxStatus.Answered)
                .SetProperty(i => i.AnsweredAt, i => db.InboxAnswerReceipts.Where(a => a.Channel == i.Channel
                    && a.ConversationId == i.ConversationId && a.ChannelMessageId == i.ChannelMessageId)
                    .Select(a => (DateTimeOffset?)a.AnsweredAt).First()), ct);
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
