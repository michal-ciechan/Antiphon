using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>Short, recoverable preparation/publish steps. No task wait or broker call holds a DB transaction.</summary>
public sealed class ChannelOutboundDeliveryPump
{
    private readonly AppDbContext _db;
    private readonly OutboundConversionTaskRunner _runner;
    private readonly IChannelOutboundFileStore _files;
    private readonly IAntiphonMessagingProducer _producer;
    private readonly AntiphonMessagingOptions _messaging;
    private readonly TimeProvider _clock;
    private readonly ILogger<ChannelOutboundDeliveryPump> _logger;
    private readonly Guid _owner = Guid.NewGuid();

    public ChannelOutboundDeliveryPump(AppDbContext db, OutboundConversionTaskRunner runner,
        IChannelOutboundFileStore files, IAntiphonMessagingProducer producer,
        IOptions<AntiphonMessagingOptions> messaging, TimeProvider clock,
        ILogger<ChannelOutboundDeliveryPump> logger)
    {
        _db = db;
        _runner = runner;
        _files = files;
        _producer = producer;
        _messaging = messaging.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<int> TickAsync(CancellationToken ct)
    {
        var now = UtcNow();
        var candidates = await _db.ChannelOutboundDeliveries.AsNoTracking()
            .Where(d => (d.State == ChannelOutboundDeliveryState.Pending
                    || d.State == ChannelOutboundDeliveryState.Converting
                    || d.State == ChannelOutboundDeliveryState.Ready
                    || d.State == ChannelOutboundDeliveryState.Publishing)
                && (d.LeaseUntil == null || d.LeaseUntil <= now))
            .OrderBy(d => d.CreatedAt).ThenBy(d => d.Id)
            .Take(32).Select(d => new { d.Id, d.Version }).ToListAsync(ct);
        var processed = 0;
        foreach (var candidate in candidates)
        {
            if (await ClaimAsync(candidate.Id, candidate.Version, ct))
            {
                processed++;
                await ProcessClaimAsync(candidate.Id, ct);
            }
        }
        return processed;
    }

    private async Task<bool> ClaimAsync(Guid id, long version, CancellationToken ct)
    {
        var now = UtcNow();
        return await _db.ChannelOutboundDeliveries
            .Where(d => d.Id == id && d.Version == version
                && (d.LeaseUntil == null || d.LeaseUntil <= now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Version, d => d.Version + 1)
                .SetProperty(d => d.LeaseOwner, _owner)
                .SetProperty(d => d.LeaseUntil, now.AddMinutes(5)), ct) == 1;
    }

    private async Task ProcessClaimAsync(Guid id, CancellationToken ct)
    {
        _db.ChangeTracker.Clear();
        var delivery = await _db.ChannelOutboundDeliveries.SingleAsync(d => d.Id == id, ct);
        try
        {
            if (delivery.State == ChannelOutboundDeliveryState.Publishing)
            {
                delivery.State = ChannelOutboundDeliveryState.PublishUncertain;
                delivery.FailureReason = "Publication began before the server stopped; broker acceptance is unknown.";
                delivery.Version++;
                await _db.SaveChangesAsync(ct);
                return;
            }
            if (delivery.State == ChannelOutboundDeliveryState.Pending)
            {
                await PrepareAsync(delivery, ct);
                // Creation may commit on another context or clear this context after a failure.
                // The next tick always reloads the durable state before taking another step.
                return;
            }
            if (delivery.State == ChannelOutboundDeliveryState.Converting)
                await ObserveConversionAsync(delivery, ct);
            if (delivery.State == ChannelOutboundDeliveryState.Ready)
                await PublishReadyAsync(delivery, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Outbound delivery {DeliveryId} lost its preparation lease", id);
            _db.ChangeTracker.Clear();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Outbound delivery {DeliveryId} preparation failed", id);
            _db.ChangeTracker.Clear();
            var current = await _db.ChannelOutboundDeliveries.SingleOrDefaultAsync(d => d.Id == id, ct);
            if (current?.LeaseOwner == _owner && current.State != ChannelOutboundDeliveryState.Publishing)
            {
                current.State = ChannelOutboundDeliveryState.Failed;
                current.FailureReason = Bound(ex.Message);
                current.Version++;
                await _db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            _db.ChangeTracker.Clear();
            await _db.ChannelOutboundDeliveries.Where(d => d.Id == id && d.LeaseOwner == _owner
                    && d.State != ChannelOutboundDeliveryState.Publishing)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.LeaseOwner, (Guid?)null)
                    .SetProperty(d => d.LeaseUntil, (DateTime?)null), CancellationToken.None);
        }
    }

    private async Task PrepareAsync(ChannelOutboundDelivery delivery, CancellationToken ct)
    {
        if (!await RevalidateAsync(delivery, beforePublish: false, ct))
            return;
        if (delivery.DeadlineAt <= UtcNow())
        {
            Fallback(delivery, "Conversion deadline elapsed in queue.");
            await _db.SaveChangesAsync(ct);
            return;
        }
        if (await _db.ChannelOutboundDeliveries.CountAsync(d =>
                d.State == ChannelOutboundDeliveryState.Converting, ct) >= 2
            || await _db.ChannelOutboundDeliveries.AnyAsync(d =>
                d.State == ChannelOutboundDeliveryState.Converting
                && d.ConverterAgentId == delivery.ConverterAgentId, ct)
            || await _db.AgentTasks.AnyAsync(t => t.OutboundDeliveryId != null
                && t.AgentId == delivery.ConverterAgentId
                && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working), ct))
            return;
        try
        {
            await _runner.CreateAsync(delivery, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _db.ChangeTracker.Clear();
            var current = await _db.ChannelOutboundDeliveries.SingleAsync(d => d.Id == delivery.Id, ct);
            Fallback(current, "Conversion worker unavailable: " + Bound(ex.Message));
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task ObserveConversionAsync(ChannelOutboundDelivery delivery, CancellationToken ct)
    {
        if (delivery.DeadlineAt <= UtcNow())
        {
            if (delivery.ConversionTaskId is Guid taskId)
                await _db.AgentTasks.Where(t => t.Id == taskId && t.Status == AgentTaskStatus.Queued)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, AgentTaskStatus.Canceled)
                        .SetProperty(t => t.CompletedAt, UtcNow())
                        .SetProperty(t => t.FailureReason, "Outbound conversion deadline elapsed."), ct);
            Fallback(delivery, "Conversion deadline elapsed; late output will be ignored.");
            await _db.SaveChangesAsync(ct);
            return;
        }
        var task = delivery.ConversionTaskId is Guid id
            ? await _db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct) : null;
        if (task is null || task.Status is AgentTaskStatus.Failed or AgentTaskStatus.Blocked or AgentTaskStatus.Canceled)
        {
            Fallback(delivery, "Conversion worker did not complete successfully.");
            await _db.SaveChangesAsync(ct);
            return;
        }
        if (task.Status != AgentTaskStatus.Succeeded)
            return;
        try
        {
            var sealedReply = await _files.ValidateAndSealAsync(delivery.Id,
                delivery.InputPath, delivery.InputSha256, _messaging.MaxMessageBytes, ct);
            delivery.OutputPath = sealedReply.ReplyPath;
            delivery.OutputSha256 = sealedReply.ReplySha256;
            delivery.ConversionOutcome = sealedReply.Outcome;
            delivery.State = ChannelOutboundDeliveryState.Ready;
            delivery.Version++;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Fallback(delivery, "Conversion result invalid: " + Bound(ex.Message));
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task PublishReadyAsync(ChannelOutboundDelivery delivery, CancellationToken ct)
    {
        if (!await RevalidateAsync(delivery, beforePublish: true, ct))
            return;
        if (await _db.ChannelOutboundDeliveries.AnyAsync(d => d.ChannelId == delivery.ChannelId
            && (d.CreatedAt < delivery.CreatedAt || d.CreatedAt == delivery.CreatedAt && d.Id.CompareTo(delivery.Id) < 0)
            && d.State != ChannelOutboundDeliveryState.Published
            && d.State != ChannelOutboundDeliveryState.Failed, ct))
            return;

        var reply = await _files.ReadReplyAsync(delivery.OutputPath ?? delivery.InputPath,
            delivery.OutputSha256 ?? delivery.InputSha256, ct);
        if (delivery.ConversionOutcome is "Fallback" or "Expired" or "QueueOverflow")
            reply = reply with { Text = ChannelOutboundService.AnnotateFallback(reply.Text) };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(reply, global::Antiphon.Messaging.MessagingJson.Options);
        if (bytes.Length > _messaging.MaxMessageBytes)
        {
            delivery.State = ChannelOutboundDeliveryState.Failed;
            delivery.FailureReason = "The frozen reply exceeds the messaging size cap.";
            delivery.Version++;
            await _db.SaveChangesAsync(ct);
            return;
        }
        delivery.State = ChannelOutboundDeliveryState.Publishing;
        delivery.PublicationAttempts++;
        delivery.Version++;
        await _db.SaveChangesAsync(ct);
        try
        {
            await _producer.SendAsync(reply, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            delivery.State = ChannelOutboundDeliveryState.PublishUncertain;
            delivery.FailureReason = "Broker acceptance unknown: " + Bound(ex.Message);
            delivery.Version++;
            await _db.SaveChangesAsync(CancellationToken.None);
            return;
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        delivery.State = ChannelOutboundDeliveryState.Published;
        delivery.PublishedAt = UtcNow();
        delivery.Version++;
        var rows = await _db.SessionQueuedMessages
            .Where(m => m.ChannelOutboundDeliveryId == delivery.Id).ToListAsync(ct);
        foreach (var row in rows)
            row.ChannelReplySettledAt = delivery.PublishedAt;
        var channel = await _db.ChatChannels.SingleAsync(c => c.Id == delivery.ChannelId, ct);
        channel.LastReplyAt = delivery.PublishedAt;
        channel.LastReplyPreview = reply.Text is { Length: > 200 } text ? text[..200] : reply.Text;
        channel.UpdatedAt = delivery.PublishedAt.Value;
        await StampCompleteSourceAsync(delivery, reply, ct);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<bool> RevalidateAsync(ChannelOutboundDelivery delivery,
        bool beforePublish, CancellationToken ct)
    {
        var channel = await _db.ChatChannels.AsNoTracking().SingleOrDefaultAsync(c => c.Id == delivery.ChannelId, ct);
        if (channel is null || !channel.Enabled || channel.AgentId != delivery.InboundAgentId)
        {
            delivery.State = ChannelOutboundDeliveryState.Held;
            delivery.FailureReason = "Channel was disabled or rebound before outbound publication.";
            delivery.Version++;
            await _db.SaveChangesAsync(ct);
            return false;
        }
        var project = await _db.Agents.Where(a => a.Id == channel.AgentId)
            .Join(_db.Boards, a => a.BoardId, b => b.Id, (a, b) => (Guid?)b.ProjectId)
            .FirstOrDefaultAsync(ct);
        if (project != delivery.ProjectId)
        {
            delivery.State = ChannelOutboundDeliveryState.Held;
            delivery.FailureReason = "Channel project binding changed before outbound publication.";
            delivery.Version++;
            await _db.SaveChangesAsync(ct);
            return false;
        }
        if (delivery.ProfileName.Length > 0 && channel.OutboundAgentProfile != delivery.ProfileName
            && delivery.ConversionOutcome != "Revoked")
        {
            delivery.OutputPath = null;
            delivery.OutputSha256 = null;
            delivery.ConversionOutcome = "Revoked";
            delivery.State = ChannelOutboundDeliveryState.Ready;
            delivery.Version++;
            await _db.SaveChangesAsync(ct);
            return false;
        }
        return true;
    }

    private async Task StampCompleteSourceAsync(ChannelOutboundDelivery delivery,
        ChannelReply reply, CancellationToken ct)
    {
        if (delivery.SourceTaskId is not Guid id)
            return;
        var task = await _db.AgentTasks.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (task?.DeliverableBundleDir is not { } dir)
            return;
        var manifestPath = Path.Combine(dir, DeliverableBundleService.SourceManifestName);
        if (!File.Exists(manifestPath))
            return;
        DeliverableBundleService.SourceManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<DeliverableBundleService.SourceManifest>(
                await File.ReadAllTextAsync(manifestPath, ct), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return;
        }
        if (manifest?.Complete != true)
            return;
        var attached = reply.Attachments.Select(a => a.Source).Where(s => s is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (DeliverableBundleService.ListAttachableFiles(task).All(attached.Contains))
            task.DeliverableDeliveredAt = delivery.PublishedAt;
    }

    private static void Fallback(ChannelOutboundDelivery delivery, string reason)
    {
        delivery.State = ChannelOutboundDeliveryState.Ready;
        delivery.OutputPath = null;
        delivery.OutputSha256 = null;
        delivery.ConversionOutcome = "Fallback";
        delivery.FailureReason = Bound(reason);
        delivery.Version++;
    }

    private static string Bound(string value) => value.Length <= 500 ? value : value[..500];
    private DateTime UtcNow() => _clock.GetUtcNow().UtcDateTime;
}
