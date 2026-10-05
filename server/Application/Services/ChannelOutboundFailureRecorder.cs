using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>The loss outcome, incident, alert and dedupe witness have one commit.</summary>
public sealed class ChannelOutboundFailureRecorder(AppDbContext db, TimeProvider clock,
    IEventBus? events = null, IAlertRouter? router = null, ChatChannelService? channels = null,
    ILogger<ChannelOutboundFailureRecorder>? logger = null)
{
    public async Task<bool> RecordDeliveryAsync(ChannelOutboundDelivery expected,
        ChannelOutboundDeliveryState state, string reason, CancellationToken ct)
    {
        if (state is not (ChannelOutboundDeliveryState.Failed or ChannelOutboundDeliveryState.PublishUncertain))
            throw new ArgumentOutOfRangeException(nameof(state));
        Alert? alert = null;
        db.ChangeTracker.Clear();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var current = await db.ChannelOutboundDeliveries.FromSqlInterpolated(
                $"SELECT * FROM \"ChannelOutboundDeliveries\" WHERE \"Id\" = {expected.Id} FOR UPDATE")
                .SingleAsync(ct);
            if (current.Version != expected.Version || current.State != expected.State
                || current.LeaseOwner != expected.LeaseOwner || current.LeaseOwner is null
                || current.LeaseUntil is null || current.LeaseUntil <= clock.GetUtcNow().UtcDateTime)
                return false;
            if (current.State == state && current.FailureEpisode > 0
                && current.FailureReportedEpisode == current.FailureEpisode)
                return false;
            var members = await db.SessionQueuedMessages.Where(m => m.ChannelOutboundDeliveryId == current.Id).ToListAsync(ct);
            current.FailureEpisode = Math.Max(1, current.FailureEpisode);
            current.State = state;
            current.FailureReason = ColumnText.Clip(reason, 500);
            current.NextAttemptAt = null;
            current.FailureReportedEpisode = current.FailureEpisode;
            current.Version++;
            if (state == ChannelOutboundDeliveryState.Failed)
                foreach (var member in members) member.ChannelReplySettledAt = clock.GetUtcNow().UtcDateTime;
            var owner = await db.Agents.Where(a => a.Id == current.InboundAgentId).Select(a => (Guid?)a.Id).SingleOrDefaultAsync(ct);
            var outcome = state == ChannelOutboundDeliveryState.PublishUncertain
                ? "Broker acceptance is unknown; the reply may have published."
                : "The reply could not be published.";
            var message = $"{outcome} Delivery {current.Id}; session {current.SourceSessionId}; channel {current.ChannelId}; "
                + $"sources {string.Join(", ", members.Select(m => m.Id))}; episode {current.FailureEpisode}. {reason}";
            alert = AddLoss(owner, current.SourceSessionId, message, state.ToString(),
                $"channel-outbound:{current.Id}:{current.FailureEpisode}");
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            throw new ChannelOutboundFailureRecordingException(ex);
        }
        await NotifyAsync(alert, expected.ChannelId, ct);
        return true;
    }

    /// <summary>Source settlement is the durable dedupe witness for losses before capture.</summary>
    public async Task<bool> RecordSourcesAsync(Guid sessionId, IReadOnlyList<SessionQueuedMessage> sources,
        string reason, string message, DateTime? sentBefore, CancellationToken ct)
    {
        if (sources.Count == 0) return false;
        db.ChangeTracker.Clear();
        Alert? alert = null;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Match capture's destination-before-member lock order. Lock all destinations
            // in a stable order for a batch spanning more than one conversation.
            var keys = sources.Select(s => s.ConversationKey).OfType<string>().Distinct().ToArray();
            var destinations = await db.ChatChannels.Where(c => keys.Contains(c.Provider + ":" + c.ExternalId))
                .OrderBy(c => c.Id).ToListAsync(ct);
            foreach (var destination in destinations)
            {
                var key = BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes("channel-outbound:" + destination.Id.ToString("N"))), 0);
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
            }
            var current = new List<SessionQueuedMessage>();
            foreach (var id in sources.Select(s => s.Id).Distinct().Order())
            {
                var row = await db.SessionQueuedMessages.FromSqlInterpolated(
                    $"SELECT * FROM \"SessionQueuedMessages\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
                if (row?.AgentSessionId == sessionId && row.ChannelOutboundDeliveryId is null
                    && row.Status == QueuedMessageStatus.Sent
                    && row.ChannelReplySettledAt is null && row.ChannelReplyDiscoveryClosedAt is null
                    && (sentBefore is null || (row.SentAt ?? row.CreatedAt) < sentBefore))
                    current.Add(row);
            }
            if (current.Count == 0) return false;
            var sessionText = sessionId.ToString("D");
            var owner = await db.Agents.Where(a => a.PersistentSessionId == sessionText)
                .Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct)
                ?? destinations.Select(c => c.AgentId).FirstOrDefault(id => id != null);
            foreach (var row in current) row.ChannelReplySettledAt = clock.GetUtcNow().UtcDateTime;
            alert = AddLoss(owner, sessionId,
                $"Session {sessionId}; sources {string.Join(", ", current.Select(m => m.Id))}. {message}",
                reason, $"channel-source:{current[0].Id}");
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
        await NotifyAsync(alert, null, ct);
        return true;
    }

    private Alert AddLoss(Guid? owner, Guid session, string message, string reason, string dedup)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        db.AgentIncidents.Add(new AgentIncident { Id = Guid.NewGuid(), AgentId = owner, SessionId = session,
            Kind = AgentIncidentKind.ChannelReplyLost, Severity = AlertSeverity.Critical,
            Message = ColumnText.Clip(message, AgentIncident.MessageMaxLength), FailureReason = reason, CreatedAt = now });
        var alert = new Alert { Id = Guid.NewGuid(), AgentId = owner, SessionId = session,
            Severity = AlertSeverity.Critical, Source = "supervisor", Title = "ChannelReplyLost: outbound reply",
            Detail = ColumnText.Clip(message, Alert.DetailMaxLength), DedupKey = dedup, CreatedAt = now };
        db.Alerts.Add(alert);
        return alert;
    }

    private async Task NotifyAsync(Alert alert, Guid? channelId, CancellationToken ct)
    {
        // These projections cannot roll back the committed attention data.
        try
        {
            if (events is not null) await events.PublishToAllAsync("AlertRaised", new {
                id = alert.Id, severity = alert.Severity.ToString(), source = alert.Source,
                title = alert.Title, detail = alert.Detail, agentId = alert.AgentId, createdAt = alert.CreatedAt }, ct);
            if (router is not null) await router.RouteAsync(alert.Id, ct);
        }
        catch (Exception ex) { logger?.LogWarning(ex, "Outbound loss alert projection failed for {AlertId}", alert.Id); }
        try
        {
            if (channelId is Guid id && channels is not null)
                await channels.SendAsync(id, "[Antiphon] " + alert.Detail, new(), ct);
        }
        catch (Exception ex) { logger?.LogWarning(ex, "Outbound loss notice failed for {AlertId}", alert.Id); }
    }
}

internal sealed class ChannelOutboundFailureRecordingException(Exception inner)
    : Exception("The outbound loss transaction did not commit; the obligation remains recoverable.", inner);
