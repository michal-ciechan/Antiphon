using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public enum ChannelOutboundOrigin { AgentReply, Control }
public enum ChannelOutboundSendOutcome { Published, Deferred }

/// <summary>Stable identity of one agent text window; task id is metadata, never the dedupe key.</summary>
public sealed record ChannelOutboundSource(
    Guid SessionId, long PromptSequence, long FirstTextSequence, long LastTextSequence,
    string SendKind, IReadOnlyList<Guid> CorrelationIds, Guid? SourceTaskId = null);

/// <summary>Server-only facade immediately above the existing messaging producer.</summary>
public sealed class ChannelOutboundService
{
    private readonly AppDbContext _db;
    private readonly IChannelOutboundFileStore _files;
    private readonly IAntiphonMessagingProducer _producer;
    private readonly ChannelOutboundSettings _settings;
    private readonly TimeProvider _clock;

    internal Func<string, Guid, CancellationToken, Task>? ProbeBarrierAsync { get; set; }

    public ChannelOutboundService(AppDbContext db, IChannelOutboundFileStore files,
        IAntiphonMessagingProducer producer, IOptions<ChannelOutboundSettings> settings,
        TimeProvider clock)
    {
        _db = db;
        _files = files;
        _producer = producer;
        _settings = settings.Value;
        _clock = clock;
    }

    /// <summary>Human retry of an ambiguous broker attempt. It may duplicate a prior accepted send.</summary>
    public async Task RetryUncertainAsync(Guid id, bool acknowledgePossibleDuplicate, CancellationToken ct)
    {
        if (!acknowledgePossibleDuplicate)
            throw new ValidationException(nameof(acknowledgePossibleDuplicate),
                "A possible duplicate must be acknowledged before retrying publication.");
        var delivery = await _db.ChannelOutboundDeliveries.SingleOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new NotFoundException(nameof(ChannelOutboundDelivery), id);
        if (delivery.State != ChannelOutboundDeliveryState.PublishUncertain)
            throw new ConflictException("Only an uncertain outbound publication can be retried.",
                "channel_outbound_not_uncertain");
        delivery.State = ChannelOutboundDeliveryState.Ready;
        delivery.LeaseOwner = null;
        delivery.LeaseUntil = null;
        delivery.FailureReason = "Explicit retry requested after possible broker acceptance.";
        delivery.Version++;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ChannelOutboundSendOutcome> SendAsync(
        ChannelReply reply, ChannelOutboundOrigin origin, ChannelOutboundSource? source,
        CancellationToken ct)
    {
        if (origin == ChannelOutboundOrigin.Control)
        {
            await _producer.SendAsync(reply, ct);
            return ChannelOutboundSendOutcome.Published;
        }
        if (source is null || string.IsNullOrWhiteSpace(reply.ConversationId))
            throw new ArgumentException("An agent reply requires a stable source and conversation.");

        var channel = await _db.ChatChannels
            .SingleOrDefaultAsync(c => c.Provider == reply.Channel && c.ExternalId == reply.ConversationId, ct);
        var profileName = channel?.OutboundAgentProfile;
        ChannelOutboundProfile? profile = null;
        string? unavailable = null;
        if (profileName is not null && _settings.Profiles.TryGetValue(profileName, out profile))
        {
            try { await ChatChannelService.ValidateOutboundBindingAsync(_db, _settings, channel!, ct); }
            catch (ValidationException ex) { unavailable = ex.Message; }
        }

        var sourceManifest = profile is not null
            ? await ReadSourceManifestAsync(source.SourceTaskId, ct) : null;
        var qualifies = profile is not null && (profile.Trigger == ChannelOutboundTrigger.EveryAgentReply
            || MatchesMarkdownSources(reply, sourceManifest));
        var hasOlderIntent = channel is not null && await _db.ChannelOutboundDeliveries.AnyAsync(d =>
            d.ChannelId == channel.Id && d.State != ChannelOutboundDeliveryState.Published
            && d.State != ChannelOutboundDeliveryState.Failed, ct);

        if (!qualifies && !hasOlderIntent)
            return await PublishDirectAsync(reply, source, ct);
        if (channel is null)
            throw new InvalidOperationException("A queued reply requires a channel catalog row.");

        var key = SourceKey(source, reply);
        var prior = await _db.ChannelOutboundDeliveries.AsNoTracking()
            .SingleOrDefaultAsync(d => d.SourceKey == key, ct);
        if (prior is not null)
        {
            VerifySamePayload(prior, reply);
            return prior.State == ChannelOutboundDeliveryState.Published
                ? ChannelOutboundSendOutcome.Published : ChannelOutboundSendOutcome.Deferred;
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var id = Guid.NewGuid();
        sourceManifest ??= await ReadSourceManifestAsync(source.SourceTaskId, ct);
        var snapshot = await _files.StageAsync(id, reply, ct, sourceManifest);
        var converter = profile is null || unavailable is not null ? null
            : await _db.Agents.AsNoTracking().SingleOrDefaultAsync(a => a.Id == profile.AgentId, ct);
        var promptPath = profile is null || converter is null || unavailable is not null ? null
            : ChatChannelService.TryGetPromptPath(converter, profile.PromptFile, out var path) ? path : null;
        string promptText;
        try { promptText = promptPath is null ? "" : await File.ReadAllTextAsync(promptPath, ct); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { unavailable = ex.Message; promptText = ""; }
        var promptRevision = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(promptText)))
            .ToLowerInvariant();
        var projectId = profile?.ProjectId ?? await _db.Agents.Where(a => a.Id == channel.AgentId)
            .Join(_db.Boards, a => a.BoardId, b => b.Id, (a, b) => b.ProjectId)
            .FirstOrDefaultAsync(ct);
        var delivery = new ChannelOutboundDelivery
        {
            Id = id, SourceKey = key, ChannelId = channel.Id, ProjectId = projectId,
            InboundAgentId = channel.AgentId ?? Guid.Empty,
            SourceSessionId = source.SessionId, PromptSequence = source.PromptSequence,
            FirstTextSequence = source.FirstTextSequence, LastTextSequence = source.LastTextSequence,
            SendKind = source.SendKind, SourceTaskId = source.SourceTaskId,
            ProfileName = qualifies ? profileName! : "", ConverterAgentId = qualifies ? profile!.AgentId : Guid.Empty,
            PromptRevision = promptRevision, PromptText = promptText,
            Trigger = qualifies ? profile!.Trigger.ToString() : "Passthrough",
            MaxPending = profile?.MaxPending ?? 0,
            InputPath = snapshot.ReplyPath, InputSha256 = snapshot.ReplySha256,
            State = ChannelOutboundDeliveryState.Ready,
            ConversionOutcome = unavailable is not null ? "Fallback"
                : qualifies ? null : "Passthrough",
            FailureReason = unavailable is not null ? "Conversion unavailable: " + Bound(unavailable)
                : null,
            CreatedAt = now, DeadlineAt = now.AddSeconds(profile?.TimeoutSeconds ?? 120),
        };

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // Serialize admission per destination so MaxPending remains a hard bound when
            // independent dispatchers accept replies at the same time.
            var lockBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                "channel-outbound:" + channel.Id.ToString("N")));
            var lockKey = BitConverter.ToInt64(lockBytes, 0);
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockKey})", ct);
            var winner = await _db.ChannelOutboundDeliveries.AsNoTracking()
                .SingleOrDefaultAsync(d => d.SourceKey == key, ct);
            if (winner is not null)
            {
                VerifySamePayload(winner, reply);
                await transaction.CommitAsync(ct);
                return winner.State == ChannelOutboundDeliveryState.Published
                    ? ChannelOutboundSendOutcome.Published : ChannelOutboundSendOutcome.Deferred;
            }
            var pending = profile is null ? 0 : await _db.ChannelOutboundDeliveries.CountAsync(d =>
                d.ChannelId == channel.Id && d.State == ChannelOutboundDeliveryState.Pending, ct);
            var overflow = qualifies && profile is not null && pending >= profile.MaxPending;
            delivery.State = qualifies && !overflow && unavailable is null
                ? ChannelOutboundDeliveryState.Pending : ChannelOutboundDeliveryState.Ready;
            if (overflow)
            {
                delivery.ConversionOutcome = "QueueOverflow";
                delivery.FailureReason = "Conversion queue full; original files retained.";
            }
            _db.ChannelOutboundDeliveries.Add(delivery);
            foreach (var correlationId in source.CorrelationIds)
            {
                var row = _db.SessionQueuedMessages.Local.FirstOrDefault(m => m.Id == correlationId)
                    ?? await _db.SessionQueuedMessages.SingleAsync(m => m.Id == correlationId, ct);
                row.ChannelOutboundDeliveryId = id;
            }
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            if (ProbeBarrierAsync is { } admissionBarrier)
                await admissionBarrier("admission-committed", id, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException
                { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            var winner = await _db.ChannelOutboundDeliveries.AsNoTracking()
                .SingleOrDefaultAsync(d => d.SourceKey == key, ct);
            if (winner is null)
                throw;
            VerifySamePayload(winner, reply);
            return winner.State == ChannelOutboundDeliveryState.Published
                ? ChannelOutboundSendOutcome.Published : ChannelOutboundSendOutcome.Deferred;
        }
        return ChannelOutboundSendOutcome.Deferred;
    }

    internal static bool MatchesMarkdownSources(ChannelReply reply, string? sourceManifestJson)
    {
        if (reply.Attachments.Any(a => a.Content is not null
                && a.Name?.EndsWith(".md", StringComparison.OrdinalIgnoreCase) == true))
            return true;
        if (sourceManifestJson is null)
            return false;
        try
        {
            var manifest = JsonSerializer.Deserialize<DeliverableBundleService.SourceManifest>(
                sourceManifestJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return manifest is { Version: 1, Sources.Count: > 0 }
                && manifest.Sources.Any(s => s.ZipEntry is not null
                    && reply.Attachments.Any(a => a.Content is not null
                        && string.Equals(a.Name, s.StoredFile, StringComparison.Ordinal)));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<string?> ReadSourceManifestAsync(Guid? taskId, CancellationToken ct)
    {
        if (taskId is not Guid id)
            return null;
        var dir = await _db.AgentTasks.AsNoTracking().Where(t => t.Id == id)
            .Select(t => t.DeliverableBundleDir).FirstOrDefaultAsync(ct);
        if (dir is null)
            return null;
        var path = Path.Combine(dir, DeliverableBundleService.SourceManifestName);
        if (!File.Exists(path))
            return null;
        if (new FileInfo(path).Length > 256 * 1024)
            throw new InvalidDataException("The source manifest exceeds the staging budget.");
        return await File.ReadAllTextAsync(path, ct);
    }

    private async Task<ChannelOutboundSendOutcome> PublishDirectAsync(
        ChannelReply reply, ChannelOutboundSource source, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var correlations = new List<SessionQueuedMessage>();
        foreach (var id in source.CorrelationIds)
        {
            var row = _db.SessionQueuedMessages.Local.FirstOrDefault(m => m.Id == id)
                ?? await _db.SessionQueuedMessages.SingleAsync(m => m.Id == id, ct);
            correlations.Add(row);
            row.ChannelReplySettledAt = now;
        }
        if (correlations.Count > 0)
            await _db.SaveChangesAsync(ct);
        try
        {
            await _producer.SendAsync(reply, ct);
            return ChannelOutboundSendOutcome.Published;
        }
        catch
        {
            foreach (var row in correlations)
                row.ChannelReplySettledAt = null;
            if (correlations.Count > 0)
                await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private static string SourceKey(ChannelOutboundSource source, ChannelReply reply)
    {
        var value = JsonSerializer.SerializeToUtf8Bytes(new
        {
            source.SessionId, source.PromptSequence, source.FirstTextSequence,
            source.LastTextSequence, source.SendKind, reply.Channel, reply.ConversationId,
        });
        return Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    }

    private static void VerifySamePayload(ChannelOutboundDelivery prior, ChannelReply reply)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(reply, global::Antiphon.Messaging.MessagingJson.Options);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(hash, prior.InputSha256, StringComparison.Ordinal))
            throw new InvalidOperationException("The source window was replayed with different outbound content.");
    }

    private static string Bound(string value) => value.Length <= 450 ? value : value[..450];

    public static string AnnotateFallback(string? text, bool hasAttachments)
    {
        var note = hasAttachments
            ? "Conversion unavailable; original attachments retained."
            : "Conversion unavailable; original reply sent.";
        return string.IsNullOrWhiteSpace(text) ? note : text + "\n\n" + note;
    }
}
