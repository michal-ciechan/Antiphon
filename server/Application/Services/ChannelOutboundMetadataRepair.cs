using Antiphon.Messaging;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Repairs projections of committed acceptance. This service cannot publish.</summary>
public sealed class ChannelOutboundMetadataRepair(AppDbContext db, IChannelOutboundFileStore files,
    TimeProvider clock)
{
    public async Task RepairAsync(Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var delivery = await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == id, ct);
        if (delivery.State != ChannelOutboundDeliveryState.Published || delivery.MetadataAppliedAt != null)
            return;
        var acceptedAt = delivery.PublishedAt
            ?? throw new InvalidDataException("A published delivery has no acceptance time.");
        var reply = await files.ReadReplyAsync(delivery.OutputPath ?? delivery.InputPath,
            delivery.OutputSha256 ?? delivery.InputSha256, ct);
        if (delivery.ConversionOutcome is "Fallback" or "Expired" or "QueueOverflow")
            reply = reply with { Text = ChannelOutboundService.AnnotateFallback(reply.Text, reply.Attachments.Count > 0) };
        IReadOnlyList<ChannelReplyBundleSnapshot> bundles = [];
        if (delivery.CaptureJson is not null)
        {
            var snapshot = await files.TryAdoptAsync(id, delivery.CaptureJson, ct)
                ?? throw new IOException("The accepted delivery snapshot is missing.");
            bundles = snapshot.Bundles;
        }

        // File I/O is complete before the short projection transaction. The conditional
        // channel update is the ordering fence even for two repair owners racing.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var current = await db.ChannelOutboundDeliveries.FromSqlInterpolated(
                $"SELECT * FROM \"ChannelOutboundDeliveries\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleAsync(ct);
        if (current.State != ChannelOutboundDeliveryState.Published || current.MetadataAppliedAt != null
            || current.Version != delivery.Version)
            return;
        var preview = reply.Text is { Length: > 200 } text ? text[..200] : reply.Text;
        await db.ChatChannels.Where(c => c.Id == delivery.ChannelId
                && (c.LastReplyAt == null || c.LastReplyAt <= acceptedAt)
                && !db.ChannelOutboundDeliveries.Any(d => d.ChannelId == c.Id
                    && d.State == ChannelOutboundDeliveryState.Published
                    && (d.PublishedAt > acceptedAt || d.PublishedAt == acceptedAt
                        && (d.CreatedAt > delivery.CreatedAt || d.CreatedAt == delivery.CreatedAt && d.Id.CompareTo(id) > 0))))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastReplyAt, acceptedAt)
                .SetProperty(c => c.LastReplyPreview, preview)
                .SetProperty(c => c.UpdatedAt, c => c.UpdatedAt > acceptedAt ? c.UpdatedAt : acceptedAt), ct);
        foreach (var bundle in bundles)
        {
            if (!ChannelOutboundDeliveryPump.HasCompleteFrozenSourceAttachments(bundle.Directory, bundle.Manifest, reply.Attachments))
                continue;
            await db.AgentTasks.Where(t => t.Id == bundle.TaskId && t.DeliverableDeliveredAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.DeliverableDeliveredAt, acceptedAt), ct);
        }
        current.MetadataAppliedAt = clock.GetUtcNow().UtcDateTime;
        current.Version++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
