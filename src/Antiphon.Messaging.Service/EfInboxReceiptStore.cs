using Antiphon.Messaging;
using Antiphon.Messaging.Gateway;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Messaging.Service;

public sealed class EfInboxReceiptStore(IServiceScopeFactory scopeFactory) : IInboxReceiptStore, IInboundReceiptSink
{
    public async Task RecordAsync(
        ChannelMessage message, string envelopeJson, string topic, int partition, long offset, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
        var existing = await db.Inbox.FirstOrDefaultAsync(
            x => x.Channel == message.Channel && x.ChannelMessageId == message.ChannelMessageId, cancellationToken);
        if (existing is not null)
        {
            if (existing.Offset is null)
            {
                existing.Topic = topic;
                existing.Partition = partition;
                existing.Offset = offset;
                await db.SaveChangesAsync(cancellationToken);
            }
            return;
        }

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
            Topic = topic,
            Partition = partition,
            Offset = offset,
        });
        await db.SaveChangesAsync(cancellationToken);
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
