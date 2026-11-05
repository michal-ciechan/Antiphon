using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Server.Application.Services;

public sealed partial class ChannelBridgeService
{
    private const int WithheldIncidentPageSize = 64;

    /// <summary>
    /// Pages an aged queued channel withhold. This loop does not take the session queue lock
    /// and does not flush, so a stuck drain cannot hide the incident.
    /// </summary>
    internal async Task RunWithheldIncidentLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await ScanWithheldChannelIncidentsAsync(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Withheld channel incident scan failed; retrying");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), _timeProvider, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    internal async Task ScanWithheldChannelIncidentsAsync(CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var threshold = TimeSpan.FromMinutes(Math.Max(0, _settings.AgedHoldIncidentMinutes));
        var agedAtOrBefore = now - threshold;
        List<Guid> inboundIds;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            inboundIds = await (
                from inbound in db.ChannelInbounds.AsNoTracking()
                join message in db.SessionQueuedMessages.AsNoTracking()
                    on inbound.QueueMessageId equals message.Id
                join session in db.AgentSessions.AsNoTracking()
                    on message.AgentSessionId equals session.Id
                where inbound.AgentId != null
                    && inbound.InputWithheldIncidentAt == null
                    && message.Origin == QueuedMessageOrigin.Channel
                    && message.Status == QueuedMessageStatus.Pending
                    && message.DeliveryAttempts == 0
                    && message.LastDeliveryStartedAt == null
                    && message.WithholdCount > 0
                    && message.FirstWithheldAt != null
                    && message.FirstWithheldAt <= agedAtOrBefore
                    && session.Status == SessionStatus.Running
                orderby message.FirstWithheldAt
                select inbound.Id).Take(WithheldIncidentPageSize).ToListAsync(ct);
        }

        foreach (var inboundId in inboundIds)
            await RecordInputWithheldIncidentAsync(inboundId, ct);
    }

    private async Task RecordInputWithheldIncidentAsync(Guid inboundId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var inbound = await db.ChannelInbounds.FromSqlInterpolated(
            $"SELECT * FROM \"ChannelInbounds\" WHERE \"Id\" = {inboundId} FOR UPDATE").SingleAsync(ct);
        if (inbound.InputWithheldIncidentAt is not null || inbound.QueueMessageId is null || inbound.AgentId is null)
            return;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var threshold = TimeSpan.FromMinutes(Math.Max(0, _settings.AgedHoldIncidentMinutes));
        var message = await db.SessionQueuedMessages.SingleOrDefaultAsync(m => m.Id == inbound.QueueMessageId, ct);
        if (message is null
            || message.Origin != QueuedMessageOrigin.Channel
            || message.Status != QueuedMessageStatus.Pending
            || message.DeliveryAttempts != 0
            || message.LastDeliveryStartedAt is not null
            || message.WithholdCount <= 0
            || message.FirstWithheldAt is null
            || now - message.FirstWithheldAt.Value < threshold)
            return;

        var running = await db.AgentSessions.AsNoTracking()
            .AnyAsync(s => s.Id == message.AgentSessionId && s.Status == SessionStatus.Running, ct);
        if (!running)
            return;

        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            AgentId = inbound.AgentId,
            Kind = AgentIncidentKind.ChannelReplyLost,
            Severity = AlertSeverity.Critical,
            FailureReason = "ChannelInputWithheld",
            CreatedAt = now,
            Message = ColumnText.Clip(
                $"Inbound message {inbound.Provider}:{inbound.ConversationId}/{inbound.NativeMessageId} is withheld: {message.LastWithholdReason}",
                AgentIncident.MessageMaxLength),
        });
        inbound.InputWithheldIncidentAt = now;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
