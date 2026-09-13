using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using MessagingJson = Antiphon.Messaging.MessagingJson;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public enum ChannelOutboundPublishStatus
{
    Published,
    Deferred,
    Failed,
}

public sealed record ChannelOutboundPublishResult(
    ChannelOutboundPublishStatus Status,
    Guid? DeliveryId = null,
    string? Failure = null);

public sealed record ChannelOutboundRequest(
    ChannelReply Reply,
    ChannelOutboundOrigin Origin,
    ChannelOutboundSendKind SendKind,
    Guid? SessionId,
    long? PromptSequence,
    long? TextWindowStart,
    long? TextWindowEnd,
    Guid? ChannelId,
    Guid? ProjectId,
    IReadOnlyList<Guid> CorrelationIds,
    IReadOnlyList<Guid> SourceTaskIds);

/// <summary>
/// CARD-0418: the one server-side preparation point above <see cref="IAntiphonMessagingProducer"/>.
/// Direct producer completion still means Kafka accepted the publication.
/// </summary>
public sealed class ChannelOutboundService
{
    internal Func<string, Guid, CancellationToken, Task>? TestBarrier { get; set; }
    internal bool TestPauseBeforeCommit { get; set; }

    private readonly AppDbContext _db;
    private readonly IAntiphonMessagingProducer _producer;
    private readonly IChannelOutboundFileStore _store;
    private readonly ChannelOutboundPolicy _policy;
    private readonly OutboundConversionTaskRunner? _runner;
    private readonly OutboundConversionManifestValidator _validator;
    private readonly ChannelOutboundSettings _settings;
    private readonly AntiphonMessagingOptions _messaging;
    private readonly ChannelBridgeSettings _bridge;
    private readonly TimeProvider _clock;
    private readonly ILogger<ChannelOutboundService> _logger;

    public ChannelOutboundService(
        AppDbContext db,
        IAntiphonMessagingProducer producer,
        IChannelOutboundFileStore store,
        ChannelOutboundPolicy policy,
        OutboundConversionManifestValidator validator,
        IOptions<ChannelOutboundSettings> settings,
        IOptions<AntiphonMessagingOptions> messaging,
        IOptions<ChannelBridgeSettings> bridge,
        TimeProvider clock,
        ILogger<ChannelOutboundService> logger,
        OutboundConversionTaskRunner? runner = null)
    {
        _db = db;
        _producer = producer;
        _store = store;
        _policy = policy;
        _runner = runner;
        _validator = validator;
        _settings = settings.Value;
        _messaging = messaging.Value;
        _bridge = bridge.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ChannelOutboundPublishResult> SendAsync(ChannelOutboundRequest request, CancellationToken ct)
    {
        if (request.Origin == ChannelOutboundOrigin.Control || !await ShouldConvertAsync(request, ct))
            return await PublishImmediateAsync(request.Reply, ct);

        return await AdmitDeferredAsync(request, ct);
    }

    public Task<bool> WouldConvertAsync(ChannelOutboundRequest request, CancellationToken ct) =>
        request.Origin == ChannelOutboundOrigin.Control
            ? Task.FromResult(false)
            : ShouldConvertAsync(request, ct);

    public async Task<int> PumpOnceAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var due = await _db.ChannelOutboundDeliveries
            .Where(d => d.State == ChannelOutboundDeliveryState.Pending
                || d.State == ChannelOutboundDeliveryState.Converting
                || d.State == ChannelOutboundDeliveryState.Ready)
            .OrderBy(d => d.CreatedAt)
            .Take(16)
            .ToListAsync(ct);
        var handled = 0;
        foreach (var delivery in due)
        {
            ct.ThrowIfCancellationRequested();
            if (!await TryClaimAsync(delivery, now, ct))
                continue;
            try
            {
                await AdvanceAsync(delivery, ct);
                handled++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Outbound delivery {Id} pump step failed", delivery.Id);
            }
        }

        return handled;
    }

    public static string SourceKey(ChannelOutboundRequest request) =>
        string.Join('|',
            request.SessionId?.ToString("N") ?? "none",
            request.PromptSequence?.ToString() ?? "0",
            request.TextWindowStart?.ToString() ?? "0",
            request.TextWindowEnd?.ToString() ?? "0",
            request.SendKind.ToString(),
            request.Reply.Channel,
            request.Reply.ConversationId ?? "");

    private async Task<bool> ShouldConvertAsync(ChannelOutboundRequest request, CancellationToken ct)
    {
        if (request.ChannelId is not Guid channelId)
            return false;
        var channel = await _db.ChatChannels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == channelId, ct);
        if (channel is null || !channel.Enabled || string.IsNullOrWhiteSpace(channel.OutboundAgentProfile))
            return false;
        var profile = _policy.TryGetProfile(channel.OutboundAgentProfile);
        if (profile is null)
            return false;
        var hasManifest = request.SourceTaskIds.Count > 0 && await HasSourceManifestAsync(request.SourceTaskIds, ct);
        return _policy.MatchesTrigger(profile, request.Reply, hasManifest);
    }

    private async Task<bool> HasSourceManifestAsync(IReadOnlyList<Guid> sourceTaskIds, CancellationToken ct)
    {
        var dirs = await _db.AgentTasks.AsNoTracking()
            .Where(t => sourceTaskIds.Contains(t.Id) && t.DeliverableBundleDir != null)
            .Select(t => t.DeliverableBundleDir)
            .ToListAsync(ct);
        return dirs.Any(d => d is not null && File.Exists(Path.Combine(d, SourceBundleManifest.FileName)));
    }

    private async Task<ChannelOutboundPublishResult> AdmitDeferredAsync(ChannelOutboundRequest request, CancellationToken ct)
    {
        var key = SourceKey(request);
        var existing = await _db.ChannelOutboundDeliveries.FirstOrDefaultAsync(d => d.SourceKey == key, ct);
        if (existing is not null)
            return new ChannelOutboundPublishResult(ChannelOutboundPublishStatus.Deferred, existing.Id);

        var channel = request.ChannelId is Guid id
            ? await _db.ChatChannels.FirstOrDefaultAsync(c => c.Id == id, ct)
            : null;
        var profileName = channel?.OutboundAgentProfile;
        var profile = _policy.TryGetProfile(profileName);
        if (channel is null || profile is null)
            return await PublishImmediateAsync(request.Reply, ct);

        try
        {
            await _policy.ValidateBindingAsync(channel, profileName!, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogInformation(ex, "Outbound profile {Profile} failed validation; publishing originals", profileName);
            return await PublishImmediateAsync(Annotate(request.Reply, OutboundConversionManifestValidator.FallbackAnnotation), ct);
        }

        var pendingForProfile = await _db.ChannelOutboundDeliveries.CountAsync(
            d => d.ProfileName == profileName
                && (d.State == ChannelOutboundDeliveryState.Pending
                    || d.State == ChannelOutboundDeliveryState.Converting),
            ct);
        if (pendingForProfile >= profile.MaxPending)
        {
            return await PublishImmediateAsync(Annotate(request.Reply, OutboundConversionManifestValidator.FallbackAnnotation), ct);
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var deliveryId = Guid.NewGuid();
        var frozenJson = JsonSerializer.Serialize(request.Reply, MessagingJson.Options);
        var inputHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(frozenJson)));
        var staged = await BuildStagedInputAsync(deliveryId, request, frozenJson, inputHash, ct);
        await _store.StageInputAsync(deliveryId, staged, ct);

        var delivery = new ChannelOutboundDelivery
        {
            Id = deliveryId,
            SourceKey = key,
            SessionId = request.SessionId,
            PromptSequence = request.PromptSequence,
            TextWindowStart = request.TextWindowStart,
            TextWindowEnd = request.TextWindowEnd,
            SendKind = request.SendKind,
            ChannelProvider = request.Reply.Channel,
            ConversationId = request.Reply.ConversationId ?? "",
            ReplyHandle = request.Reply.ReplyHandle,
            ReplyToMessageId = request.Reply.ReplyToMessageId,
            ChannelId = request.ChannelId,
            ProjectId = profile.ProjectId,
            Origin = request.Origin,
            State = ChannelOutboundDeliveryState.Pending,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
            DeadlineAt = now.AddSeconds(profile.TimeoutSeconds),
            ProfileName = profileName,
            ProfileSnapshotJson = JsonSerializer.Serialize(profile),
            InputHash = inputHash,
            FrozenReplyJson = frozenJson,
        };

        if (TestPauseBeforeCommit)
            await (TestBarrier?.Invoke("IntentCommit", deliveryId, ct) ?? Task.CompletedTask);

        _db.ChannelOutboundDeliveries.Add(delivery);
        if (request.CorrelationIds.Count > 0)
        {
            var rows = await _db.SessionQueuedMessages
                .Where(m => request.CorrelationIds.Contains(m.Id))
                .ToListAsync(ct);
            foreach (var row in rows)
                row.OutboundDeliveryId = deliveryId;
        }

        await _db.SaveChangesAsync(ct);
        if (TestBarrier is not null)
            await TestBarrier("IntentCommit", deliveryId, ct);
        return new ChannelOutboundPublishResult(ChannelOutboundPublishStatus.Deferred, deliveryId);
    }

    private async Task<ChannelOutboundStagedInput> BuildStagedInputAsync(
        Guid deliveryId,
        ChannelOutboundRequest request,
        string frozenJson,
        string inputHash,
        CancellationToken ct)
    {
        var files = new List<ChannelOutboundStagedFile>();
        foreach (var attachment in request.Reply.Attachments)
        {
            if (attachment.Content is not { Length: > 0 })
                continue;
            var name = SanitizeName(attachment.Name ?? "attachment.bin");
            files.Add(new ChannelOutboundStagedFile
            {
                SafeName = name,
                Mime = attachment.Mime ?? "application/octet-stream",
                Length = attachment.Content.LongLength,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(attachment.Content)),
                Bytes = attachment.Content,
            });
        }

        var workerDir = await _store.WorkerDirectoryAsync(deliveryId, ct);
        var requestJson = JsonSerializer.Serialize(new OutboundConversionRequestV1
        {
            Version = 1,
            DeliveryId = deliveryId,
            Text = request.Reply.Text,
            OutputDirectory = Path.Combine(workerDir, "output"),
            Attachments = files.Select(f => new OutboundConversionFileDescriptor
            {
                Path = f.SafeName,
                Mime = f.Mime,
                Length = f.Length,
                Sha256 = f.Sha256,
            }).ToList(),
            RoutingReference = new Dictionary<string, string>
            {
                ["channel"] = request.Reply.Channel,
                ["conversationId"] = request.Reply.ConversationId ?? "",
            },
        }, OutboundConversionManifestValidator.JsonOptions);

        return new ChannelOutboundStagedInput
        {
            FrozenReplyJson = frozenJson,
            InputHash = inputHash,
            RequestJson = requestJson,
            Files = files,
        };
    }

    private async Task<bool> TryClaimAsync(ChannelOutboundDelivery delivery, DateTime now, CancellationToken ct)
    {
        if (delivery.LeaseExpiresAt is DateTime lease && lease > now && delivery.LeaseOwner is not null)
            return false;
        var owner = Environment.MachineName + ":" + Environment.ProcessId;
        var affected = await _db.ChannelOutboundDeliveries
            .Where(d => d.Id == delivery.Id && d.Version == delivery.Version
                && (d.LeaseExpiresAt == null || d.LeaseExpiresAt <= now))
            .ExecuteUpdateAsync(u => u
                .SetProperty(d => d.LeaseOwner, owner)
                .SetProperty(d => d.LeaseExpiresAt, now.AddSeconds(_settings.LeaseSeconds))
                .SetProperty(d => d.Version, delivery.Version + 1)
                .SetProperty(d => d.UpdatedAt, now), ct);
        if (affected != 1)
            return false;
        delivery.Version++;
        delivery.LeaseOwner = owner;
        delivery.LeaseExpiresAt = now.AddSeconds(_settings.LeaseSeconds);
        return true;
    }

    private async Task AdvanceAsync(ChannelOutboundDelivery delivery, CancellationToken ct)
    {
        if (delivery.DeadlineAt <= _clock.GetUtcNow().UtcDateTime
            && delivery.State is ChannelOutboundDeliveryState.Pending)
        {
            await FallbackReadyAsync(delivery, "deadline", ct);
            await PublishReadyAsync(delivery, ct);
            return;
        }

        switch (delivery.State)
        {
            case ChannelOutboundDeliveryState.Pending:
                await ConvertOrFallbackAsync(delivery, ct);
                break;
            case ChannelOutboundDeliveryState.Converting:
                await ObserveConversionAsync(delivery, ct);
                break;
            case ChannelOutboundDeliveryState.Ready:
                await PublishReadyAsync(delivery, ct);
                break;
        }
    }

    private async Task ConvertOrFallbackAsync(ChannelOutboundDelivery delivery, CancellationToken ct)
    {
        if (TestBarrier is not null)
            await TestBarrier("TaskCreation", delivery.Id, ct);
        var taskId = _runner is null ? null : await _runner.TryDispatchAsync(delivery, ct);
        if (taskId is null)
            await FallbackReadyAsync(delivery, "converter-unavailable", ct);
    }

    private async Task ObserveConversionAsync(ChannelOutboundDelivery delivery, CancellationToken ct)
    {
        if (delivery.ConversionTaskId is not Guid taskId)
        {
            await FallbackReadyAsync(delivery, "missing-task", ct);
            return;
        }

        var task = await _db.AgentTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
        {
            await FallbackReadyAsync(delivery, "missing-task", ct);
            return;
        }

        if (task.Status is AgentTaskStatus.Queued or AgentTaskStatus.Dispatched or AgentTaskStatus.Working)
            return;
        if (task.Status != AgentTaskStatus.Succeeded)
        {
            await FallbackReadyAsync(delivery, "task-" + task.Status, ct);
            return;
        }

        var outputDir = _store.OutputDirectory(delivery.Id);
        var manifestPath = Path.Combine(outputDir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            await FallbackReadyAsync(delivery, "missing-output", ct);
            return;
        }

        OutboundConversionOutputV1? output;
        try
        {
            output = JsonSerializer.Deserialize<OutboundConversionOutputV1>(
                await File.ReadAllTextAsync(manifestPath, ct),
                OutboundConversionManifestValidator.JsonOptions);
        }
        catch (Exception)
        {
            await FallbackReadyAsync(delivery, "malformed-output", ct);
            return;
        }

        if (output is null)
        {
            await FallbackReadyAsync(delivery, "malformed-output", ct);
            return;
        }

        var validation = _validator.ValidateOutput(delivery.Id, outputDir, output);
        var original = JsonSerializer.Deserialize<ChannelReply>(delivery.FrozenReplyJson ?? "{}", MessagingJson.Options)
            ?? throw new InvalidOperationException("frozen reply missing");
        ChannelReply payload;
        if (!validation.Ok || output.Disposition == "unchanged")
        {
            payload = validation.Ok
                ? original
                : Annotate(original, OutboundConversionManifestValidator.FallbackAnnotation);
            delivery.ConversionSucceeded = false;
            delivery.FailureReason = validation.Reason;
        }
        else
        {
            payload = await SealConvertedAsync(original, output, validation, ct);
            delivery.ConversionSucceeded = true;
        }

        if (!TryValidatePayloadBudget(payload, out var budgetFailure))
        {
            payload = Annotate(original, OutboundConversionManifestValidator.FallbackAnnotation);
            delivery.ConversionSucceeded = false;
            delivery.FailureReason = budgetFailure;
            if (!TryValidatePayloadBudget(payload, out _))
            {
                delivery.State = ChannelOutboundDeliveryState.Failed;
                delivery.FailureReason = "payload-over-cap";
                delivery.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                await _db.SaveChangesAsync(ct);
                return;
            }
        }

        await _store.SealOutputAsync(delivery.Id, payload, ct);
        delivery.SealedPayloadJson = JsonSerializer.Serialize(payload, MessagingJson.Options);
        delivery.SealedPayloadHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(delivery.SealedPayloadJson)));
        delivery.OutputManifestJson = await File.ReadAllTextAsync(manifestPath, ct);
        delivery.State = ChannelOutboundDeliveryState.Ready;
        delivery.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        if (TestBarrier is not null)
            await TestBarrier("Ready", delivery.Id, ct);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<ChannelReply> SealConvertedAsync(
        ChannelReply original,
        OutboundConversionOutputV1 output,
        OutboundConversionValidationResult validation,
        CancellationToken ct)
    {
        var extras = new List<OutboundAttachment>();
        foreach (var (descriptor, bytes) in validation.Files)
        {
            extras.Add(new OutboundAttachment
            {
                Kind = AttachmentKind.File,
                Name = Path.GetFileName(descriptor.Path),
                Mime = descriptor.Mime,
                Content = bytes,
            });
        }

        var attachments = original.Attachments.Concat(extras).ToList();
        return original with
        {
            Text = output.ReplacementText ?? original.Text,
            Attachments = attachments,
        };
    }

    private async Task FallbackReadyAsync(ChannelOutboundDelivery delivery, string reason, CancellationToken ct)
    {
        var original = JsonSerializer.Deserialize<ChannelReply>(delivery.FrozenReplyJson ?? "{}", MessagingJson.Options)
            ?? new ChannelReply { Channel = delivery.ChannelProvider, ConversationId = delivery.ConversationId };
        var payload = Annotate(original, OutboundConversionManifestValidator.FallbackAnnotation);
        await _store.SealOutputAsync(delivery.Id, payload, ct);
        delivery.SealedPayloadJson = JsonSerializer.Serialize(payload, MessagingJson.Options);
        delivery.SealedPayloadHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(delivery.SealedPayloadJson)));
        delivery.ConversionSucceeded = false;
        delivery.FailureReason = reason;
        delivery.State = ChannelOutboundDeliveryState.Ready;
        delivery.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);
    }

    private async Task PublishReadyAsync(ChannelOutboundDelivery delivery, CancellationToken ct)
    {
        var payload = await _store.ReadSealedPayloadAsync(delivery.Id, ct)
            ?? JsonSerializer.Deserialize<ChannelReply>(delivery.SealedPayloadJson ?? "{}", MessagingJson.Options);
        if (payload is null)
        {
            delivery.State = ChannelOutboundDeliveryState.Failed;
            delivery.FailureReason = "missing-sealed-payload";
            delivery.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
            await _db.SaveChangesAsync(ct);
            return;
        }

        if (!await BindingStillValidAsync(delivery, ct))
        {
            delivery.State = ChannelOutboundDeliveryState.Held;
            delivery.FailureReason = "binding-revoked";
            delivery.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
            await _db.SaveChangesAsync(ct);
            return;
        }

        delivery.State = ChannelOutboundDeliveryState.Publishing;
        delivery.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);
        if (TestBarrier is not null)
            await TestBarrier("Publishing", delivery.Id, ct);

        try
        {
            delivery.PublishAttempts++;
            await _producer.SendAsync(payload, ct);
            var now = _clock.GetUtcNow().UtcDateTime;
            delivery.State = ChannelOutboundDeliveryState.Published;
            delivery.PublishedAt = now;
            delivery.UpdatedAt = now;
            await SettleCorrelationsAsync(delivery, now, payload, ct);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (delivery.PublishAttempts >= _settings.MaxPublishAttempts)
            {
                delivery.State = ChannelOutboundDeliveryState.Failed;
                delivery.FailureReason = Trim(ex.Message);
            }
            else
            {
                delivery.State = ChannelOutboundDeliveryState.PublishUncertain;
                delivery.FailureReason = Trim(ex.Message);
            }

            delivery.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task SettleCorrelationsAsync(
        ChannelOutboundDelivery delivery, DateTime now, ChannelReply payload, CancellationToken ct)
    {
        var rows = await _db.SessionQueuedMessages
            .Where(m => m.OutboundDeliveryId == delivery.Id)
            .ToListAsync(ct);
        foreach (var row in rows)
            row.ChannelReplySettledAt = now;

        var sourceIds = rows.Where(r => r.SourceTaskId is not null).Select(r => r.SourceTaskId!.Value).Distinct().ToList();
        if (sourceIds.Count == 0)
            return;
        var tasks = await _db.AgentTasks.Where(t => sourceIds.Contains(t.Id)).ToListAsync(ct);
        var attached = new HashSet<string>(
            payload.Attachments.Select(a => a.Name ?? ""), StringComparer.OrdinalIgnoreCase);
        foreach (var task in tasks)
        {
            var files = DeliverableBundleService.ListAttachableFiles(task);
            if (files.Count == 0)
                continue;
            var required = files.Select(Path.GetFileName).ToList();
            delivery.SourceComplete = required.All(name => attached.Contains(name ?? ""));
            if (files.Any(f => attached.Contains(Path.GetFileName(f) ?? "")))
                task.DeliverableDeliveredAt = now;
        }
    }

    private async Task<bool> BindingStillValidAsync(ChannelOutboundDelivery delivery, CancellationToken ct)
    {
        if (delivery.ChannelId is not Guid channelId || string.IsNullOrWhiteSpace(delivery.ProfileName))
            return false;
        var channel = await _db.ChatChannels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == channelId, ct);
        if (channel is null || !channel.Enabled)
            return false;
        if (!string.Equals(channel.OutboundAgentProfile, delivery.ProfileName, StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            await _policy.ValidateBindingAsync(channel, delivery.ProfileName, ct);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<ChannelOutboundPublishResult> PublishImmediateAsync(ChannelReply reply, CancellationToken ct)
    {
        if (!TryValidatePayloadBudget(reply, out var failure))
            return new ChannelOutboundPublishResult(ChannelOutboundPublishStatus.Failed, Failure: failure);
        await _producer.SendAsync(reply, ct);
        return new ChannelOutboundPublishResult(ChannelOutboundPublishStatus.Published);
    }

    private bool TryValidatePayloadBudget(ChannelReply reply, out string? failure)
    {
        var raw = reply.Attachments.Sum(a => a.Content?.LongLength ?? 0);
        if (raw > _bridge.MaxAttachmentBytes)
        {
            failure = "raw-attachment-cap";
            return false;
        }

        var bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(reply, MessagingJson.Options));
        if (bytes > _messaging.MaxMessageBytes)
        {
            failure = "serialized-cap";
            return false;
        }

        failure = null;
        return true;
    }

    private static ChannelReply Annotate(ChannelReply reply, string note)
    {
        var text = string.IsNullOrWhiteSpace(reply.Text) ? note : reply.Text + "\n\n" + note;
        return reply with { Text = text };
    }

    private static string SanitizeName(string name)
    {
        var file = Path.GetFileName(name.Replace('\\', '/'));
        return string.IsNullOrWhiteSpace(file) ? "attachment.bin" : file;
    }

    private static string Trim(string text)
    {
        var one = text.ReplaceLineEndings(" ").Trim();
        return one.Length <= 300 ? one : one[..300];
    }
}