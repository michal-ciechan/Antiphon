using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using Confluent.Kafka;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
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
    private readonly ChannelOutboundSettings _outboundSettings;
    private readonly TimeProvider _clock;
    private readonly ILogger<ChannelOutboundDeliveryPump> _logger;
    private readonly Guid _owner = Guid.NewGuid();

    // Test-only, per-instance stop point. Production leaves this null. The callback runs
    // only after the named durable write has completed, so a killed probe cannot rely
    // on in-memory state when recovery starts in a new process.
    internal Func<string, Guid, CancellationToken, Task>? ProbeBarrierAsync { get; set; }

    public ChannelOutboundDeliveryPump(AppDbContext db, OutboundConversionTaskRunner runner,
        IChannelOutboundFileStore files, IAntiphonMessagingProducer producer,
        IOptions<AntiphonMessagingOptions> messaging, TimeProvider clock,
        ILogger<ChannelOutboundDeliveryPump> logger,
        IOptions<ChannelOutboundSettings>? outboundSettings = null)
    {
        _db = db;
        _runner = runner;
        _files = files;
        _producer = producer;
        _messaging = messaging.Value;
        _outboundSettings = outboundSettings?.Value ?? new ChannelOutboundSettings();
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
            {
                if (ProbeBarrierAsync is { } observationBarrier)
                    await observationBarrier("before-conversion-observation", delivery.Id, ct);
                await ObserveConversionAsync(delivery, ct);
            }
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
        // The shared connection owns this lock across the runner's task-creation
        // transaction. Two server instances therefore cannot both observe a free seat.
        var lockKey = BitConverter.ToInt64(SHA256.HashData(
            Encoding.UTF8.GetBytes("channel-outbound:global-conversion")), 0);
        await _db.Database.OpenConnectionAsync(ct);
        var locked = false;
        try
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_lock({lockKey})", ct);
            locked = true;
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
                if (ProbeBarrierAsync is { } beforeCreateBarrier)
                    await beforeCreateBarrier("before-conversion-create", delivery.Id, ct);
                // The advisory lock and the final pre-create work can outlive the
                // frozen deadline. Do not create a worker after that wait.
                if (delivery.DeadlineAt <= UtcNow())
                {
                    Fallback(delivery, "Conversion deadline elapsed before worker creation.");
                    await _db.SaveChangesAsync(ct);
                    return;
                }
                await _runner.CreateAsync(delivery, ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _db.ChangeTracker.Clear();
                var current = await _db.ChannelOutboundDeliveries.SingleAsync(d => d.Id == delivery.Id, ct);
                Fallback(current, "Conversion worker unavailable: " + Bound(ex.Message));
                await _db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            try
            {
                if (locked)
                    await _db.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT pg_advisory_unlock({lockKey})", CancellationToken.None);
            }
            finally { await _db.Database.CloseConnectionAsync(); }
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
            if (ProbeBarrierAsync is { } readyBarrier)
                await readyBarrier("ready-committed", delivery.Id, ct);
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
            reply = reply with { Text = ChannelOutboundService.AnnotateFallback(reply.Text,
                reply.Attachments.Count > 0) };
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
        delivery.Version++;
        await _db.SaveChangesAsync(ct);
        if (ProbeBarrierAsync is { } publishingBarrier)
            await publishingBarrier("publishing-committed", delivery.Id, ct);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            delivery.PublicationAttempts++;
            delivery.Version++;
            await _db.SaveChangesAsync(ct);
            if (ProbeBarrierAsync is { } beforeCallBarrier)
                await beforeCallBarrier("before-producer-call", delivery.Id, ct);
            var callAt = UtcNow();
            if (!await _db.ChannelOutboundDeliveries.AsNoTracking().AnyAsync(d =>
                    d.Id == delivery.Id && d.Version == delivery.Version
                    && d.LeaseOwner == _owner && d.LeaseUntil > callAt
                    && d.State == ChannelOutboundDeliveryState.Publishing, ct))
                return;
            try
            {
                await _producer.SendAsync(reply, ct);
                break;
            }
            catch (ProduceException<string, string> ex)
                when (ex.Error.Code == ErrorCode.Local_QueueFull)
            {
                if (attempt == 3)
                {
                    delivery.State = ChannelOutboundDeliveryState.Failed;
                    delivery.FailureReason = "Broker queue refused the sealed reply three times: "
                        + Bound(ex.Message);
                    delivery.Version++;
                    await _db.SaveChangesAsync(CancellationToken.None);
                    return;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                delivery.State = ChannelOutboundDeliveryState.PublishUncertain;
                delivery.FailureReason = "Broker acceptance unknown: " + Bound(ex.Message);
                delivery.Version++;
                await _db.SaveChangesAsync(CancellationToken.None);
                return;
            }
        }

        if (ProbeBarrierAsync is { } acceptedBarrier)
            await acceptedBarrier("producer-accepted", delivery.Id, ct);

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
        if (ProbeBarrierAsync is { } publishedBarrier)
            await publishedBarrier("published-committed", delivery.Id, ct);
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
        if (delivery.ProfileName.Length > 0
            && (channel.OutboundAgentProfile != delivery.ProfileName
                || !_outboundSettings.Profiles.TryGetValue(delivery.ProfileName, out var activeProfile)
                || activeProfile.AgentId != delivery.ConverterAgentId
                || activeProfile.ProjectId != delivery.ProjectId)
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
        if (HasCompleteSourceAttachments(task, manifest, reply.Attachments))
            task.DeliverableDeliveredAt = delivery.PublishedAt;
    }

    internal static bool HasCompleteSourceAttachments(AgentTask task,
        DeliverableBundleService.SourceManifest manifest,
        IReadOnlyList<OutboundAttachment> attachments)
    {
        if (manifest.Sources is not { Count: > 0 and <= 256 })
            return false;
        const long maxSourceBytes = 64L * 1024 * 1024;
        long sourceBytes = 0;
        foreach (var source in manifest.Sources)
        {
            if (source.Length < 0 || source.Length > maxSourceBytes - sourceBytes)
                return false;
            sourceBytes += source.Length;
        }
        var required = manifest.Sources.Select(s => s.StoredFile)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var files = DeliverableBundleService.ListAttachableFiles(task);
        if (files.Count != required.Length)
            return false;
        if (!required.All(name => files.Any(path =>
                string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase))))
            return false;
        foreach (var source in manifest.Sources)
        {
            var file = files.SingleOrDefault(path =>
                string.Equals(Path.GetFileName(path), source.StoredFile, StringComparison.OrdinalIgnoreCase));
            var attached = attachments.Where(a => a.Source is not null && a.Content is not null
                && string.Equals(a.Source, file, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            if (file is null || attached.Length != 1)
                return false;
            var content = attached[0].Content!;
            if (source.ZipEntry is null)
            {
                if (content.LongLength != source.Length || !MatchesHash(content, source.Sha256))
                    return false;
                continue;
            }
            try
            {
                using var archive = new ZipArchive(new MemoryStream(content, writable: false),
                    ZipArchiveMode.Read);
                var entries = archive.Entries.Where(entry =>
                    string.Equals(entry.FullName, source.ZipEntry, StringComparison.Ordinal))
                    .Take(2).ToArray();
                if (entries.Length != 1 || entries[0].Length != source.Length)
                    return false;
                using var stream = entries[0].Open();
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long length = 0;
                int read;
                while ((read = stream.Read(buffer)) > 0)
                {
                    if (read > source.Length - length)
                        return false;
                    hash.AppendData(buffer, 0, read);
                    length += read;
                }
                if (length != source.Length || !string.Equals(
                        Convert.ToHexString(hash.GetHashAndReset()), source.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                return false;
            }
        }
        return true;
    }

    private static bool MatchesHash(byte[] content, string expected) =>
        string.Equals(Convert.ToHexString(SHA256.HashData(content)), expected,
            StringComparison.OrdinalIgnoreCase);

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
