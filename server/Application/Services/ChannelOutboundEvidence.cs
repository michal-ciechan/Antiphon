using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;

namespace Antiphon.Server.Application.Services;

/// <summary>Shared discovery/retention eligibility. A closed machine source is not a settlement.</summary>
internal static class ChannelOutboundEvidence
{
    internal static IQueryable<SessionQueuedMessage> DiscoverySources(AppDbContext db) =>
        db.SessionQueuedMessages.Where(m => m.Status == QueuedMessageStatus.Sent
            && m.ChannelReplySettledAt == null && m.ChannelOutboundDeliveryId == null
            && m.ChannelReplyDiscoveryClosedAt == null
            && (m.Origin == QueuedMessageOrigin.Channel && m.ConversationKey != null
                || m.Origin == QueuedMessageOrigin.Delegation || m.Origin == QueuedMessageOrigin.Check
                || m.Origin == QueuedMessageOrigin.System || m.Origin == QueuedMessageOrigin.Scheduled));

    internal static IQueryable<ChannelOutboundDelivery> ProtectedDeliveries(AppDbContext db) =>
        db.ChannelOutboundDeliveries.Where(d =>
            d.State != ChannelOutboundDeliveryState.Published && d.State != ChannelOutboundDeliveryState.Suppressed
            || d.State == ChannelOutboundDeliveryState.Published && d.MetadataAppliedAt == null
            || d.CaptureJson != null && d.RootDeliveryId == null && d.TailClosedAt == null);

    internal static IQueryable<Guid> ProtectedSessions(AppDbContext db) =>
        DiscoverySources(db).Select(m => m.AgentSessionId)
            .Union(ProtectedDeliveries(db).Select(d => d.SourceSessionId));

    internal static IQueryable<SessionQueuedMessage> ProtectedSources(AppDbContext db)
    {
        var sources = DiscoverySources(db);
        var deliveries = ProtectedDeliveries(db);
        return db.SessionQueuedMessages.Where(m => sources.Any(s => s.Id == m.Id)
            || deliveries.Any(d => d.Id == m.ChannelOutboundDeliveryId)
            // Settled channel context is still needed to route an undiscovered machine answer.
            || m.Origin == QueuedMessageOrigin.Channel
                && sources.Any(s => s.AgentSessionId == m.AgentSessionId
                    && s.Origin != QueuedMessageOrigin.Channel && m.Sequence < s.Sequence
                    && (m.LastDeliveryStartedAt ?? m.SentAt ?? m.CreatedAt)
                        <= (s.LastDeliveryStartedAt ?? s.SentAt ?? s.CreatedAt)));
    }
}
