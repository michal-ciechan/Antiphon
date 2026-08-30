using Antiphon.Messaging;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0245 S2b: persist a uniquely-deduped, agent-independent
/// <see cref="ChannelIngressIncident"/> from a gateway operational event and raise a Critical
/// alert. Detection only — never restarts AppHost.
/// </summary>
public sealed class ChannelIngressIncidentService(
    AppDbContext db,
    IAlertService alerts,
    TimeProvider time,
    ILogger<ChannelIngressIncidentService> logger)
{
    public async Task<bool> PersistAsync(InboundUnconsumedEvent evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var exists = await db.ChannelIngressIncidents.AnyAsync(
            i => i.Topic == evt.Topic && i.Partition == evt.Partition && i.Offset == evt.Offset, ct);
        if (exists)
            return false;

        var now = time.GetUtcNow().UtcDateTime;
        db.ChannelIngressIncidents.Add(new ChannelIngressIncident
        {
            Id = Guid.NewGuid(),
            Provider = evt.Provider,
            ConversationId = evt.ConversationId,
            OriginalMessageId = evt.OriginalMessageId,
            Topic = evt.Topic,
            Partition = evt.Partition,
            Offset = evt.Offset,
            FirstSeenAt = evt.FirstSeenAt.ToUniversalTime(),
            DetectedAt = evt.DetectedAt.ToUniversalTime(),
            Acknowledged = evt.Acknowledged,
            AcknowledgementError = evt.AcknowledgementError,
            AppHostHealth = evt.AppHostHealth,
            CreatedAt = now,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return false;
        }

        await alerts.RaiseAsync(new AlertRaise(
            AlertSeverity.Critical,
            Source: "ingress",
            Title: "InboundUnconsumed: channel message waiting",
            Detail: $"{evt.Provider} conversation {evt.ConversationId} message {evt.OriginalMessageId} "
                + $"unconsumed at {evt.Topic}/{evt.Partition}:{evt.Offset} since {evt.FirstSeenAt:o}. "
                + (evt.Acknowledged ? "Acknowledged to the chat." : "Acknowledgement not yet delivered."),
            DedupKey: $"ingress:unconsumed:{evt.Topic}:{evt.Partition}:{evt.Offset}"), ct);

        logger.LogWarning(
            "Inbound unconsumed {Provider} {Conversation} {MessageId} {Topic}/{Partition}:{Offset} ack={Ack}",
            evt.Provider, evt.ConversationId, evt.OriginalMessageId, evt.Topic, evt.Partition, evt.Offset, evt.Acknowledged);
        return true;
    }
}
