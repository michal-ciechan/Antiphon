using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Antiphon.Server.Application.Services;

/// <summary>Observation seam for committed publication boundaries. It never chooses an outcome.</summary>
public class ChannelOutboundBoundary
{
    public virtual Task ReachAsync(string boundary, Guid publicationId, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// Owns the broker acceptance boundary for a frozen outbound envelope. A conditional database claim
/// spends an attempt before I/O; an outcome commit is fenced by that claim's owner.
/// </summary>
public sealed class ChannelOutboundPublicationService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IAntiphonMessagingProducer _producer;
    private readonly ChannelBridgeSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ChannelOutboundBoundary _boundary;
    private readonly ILogger<ChannelOutboundPublicationService> _logger;

    public ChannelOutboundPublicationService(
        IServiceScopeFactory scopes, IAntiphonMessagingProducer producer,
        IOptions<ChannelBridgeSettings> settings, ChannelOutboundClock clock,
        ChannelOutboundBoundary boundary, ILogger<ChannelOutboundPublicationService> logger)
    {
        _scopes = scopes;
        _producer = producer;
        _settings = settings.Value;
        _clock = clock.Provider;
        _boundary = boundary;
        _logger = logger;
    }

    private DateTime UtcNow() => _clock.GetUtcNow().UtcDateTime;

    /// <summary>Keep a manually restarted dispatcher's supplied producer as its I/O seam.</summary>
    public ChannelOutboundPublicationService WithProducer(IAntiphonMessagingProducer producer) =>
        new(_scopes, producer, Options.Create(_settings), new ChannelOutboundClock(_clock),
            _boundary, _logger);

    public async Task<bool> PublishAsync(
        string path, Guid sessionId, long promptSequence, long firstTextSequence,
        long lastTextSequence, string originalResponse, ChannelReply envelope,
        IReadOnlyList<Guid> sourceIds, IReadOnlyList<Guid>? bundleTaskIds, CancellationToken ct)
    {
        var id = await MaterializeAsync(path, sessionId, promptSequence, firstTextSequence,
            lastTextSequence, originalResponse, envelope, sourceIds, bundleTaskIds, ct);
        return await AttemptAsync(id, ct);
    }

    private async Task<Guid> MaterializeAsync(
        string path, Guid sessionId, long promptSequence, long firstTextSequence,
        long lastTextSequence, string originalResponse, ChannelReply envelope,
        IReadOnlyList<Guid> sourceIds, IReadOnlyList<Guid>? bundleTaskIds, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var current = await db.ChannelOutboundPublications.AsNoTracking().Where(p =>
                p.SessionId == sessionId && p.PromptSequence == promptSequence
                && p.FirstTextSequence == firstTextSequence && p.LastTextSequence == lastTextSequence
                && p.Path == path && p.Provider == envelope.Channel
                && p.ConversationId == envelope.ConversationId)
            .Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        if (current is Guid existing)
            return existing;

        var agentId = await db.Agents.Where(a => a.PersistentSessionId == sessionId.ToString("D"))
            .Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
        if (agentId is null)
            agentId = await db.ChatChannels.Where(c => c.Provider == envelope.Channel
                    && c.ExternalId == envelope.ConversationId)
                .Select(c => c.AgentId).FirstOrDefaultAsync(ct);
        var record = new ChannelOutboundPublication
        {
            Id = Guid.NewGuid(), SessionId = sessionId, AgentId = agentId,
            PromptSequence = promptSequence, FirstTextSequence = firstTextSequence,
            LastTextSequence = lastTextSequence, Path = path,
            Provider = envelope.Channel, ConversationId = envelope.ConversationId ?? string.Empty,
            ReplyHandle = envelope.ReplyHandle, OriginalResponse = originalResponse,
            EnvelopeJson = JsonSerializer.Serialize(envelope),
            BundleTaskIdsJson = JsonSerializer.Serialize(bundleTaskIds ?? []),
            CreatedAt = UtcNow(), NextAttemptAt = UtcNow(),
            Sources = sourceIds.Distinct().Select(sourceId => new ChannelOutboundPublicationSource
            { PublicationId = Guid.Empty, QueueMessageId = sourceId, Path = path,
                FirstTextSequence = firstTextSequence, LastTextSequence = lastTextSequence }).ToList(),
        };
        foreach (var source in record.Sources)
            source.PublicationId = record.Id;
        db.ChannelOutboundPublications.Add(record);
        await _boundary.ReachAsync("before-publication-commit", record.Id, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            await using var rereadScope = _scopes.CreateAsyncScope();
            var reread = rereadScope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await reread.ChannelOutboundPublications.Where(p =>
                    p.SessionId == sessionId && p.PromptSequence == promptSequence
                    && p.FirstTextSequence == firstTextSequence && p.LastTextSequence == lastTextSequence
                    && p.Path == path && p.Provider == envelope.Channel
                    && p.ConversationId == envelope.ConversationId)
                .Select(p => p.Id).SingleAsync(ct);
        }
        await _boundary.ReachAsync("publication-committed", record.Id, ct);
        return record.Id;
    }

    public async Task<bool> AttemptAsync(Guid publicationId, CancellationToken ct)
    {
        var owner = Guid.NewGuid();
        var now = UtcNow();
        await using (var scope = _scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var claimed = await db.ChannelOutboundPublications
                .Where(p => p.Id == publicationId && (p.State == "Pending" || p.State == "Unknown")
                    && p.CreatedAt > now.AddMinutes(-_settings.PendingReplyTtlMinutes)
                    && p.AttemptCount < _settings.OutboundMaxAttempts
                    && (p.NextAttemptAt == null || p.NextAttemptAt <= now))
                .ExecuteUpdateAsync(set => set
                    .SetProperty(p => p.State, "Publishing")
                    .SetProperty(p => p.AttemptOwner, owner)
                    .SetProperty(p => p.AttemptCount, p => p.AttemptCount + 1)
                    .SetProperty(p => p.AttemptExpiresAt, now.AddSeconds(_settings.OutboundAttemptLeaseSeconds)), ct);
            if (claimed != 1)
                return false;
        }
        await _boundary.ReachAsync("attempt-committed", publicationId, ct);
        ChannelReply envelope;
        await using (var scope = _scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var json = await db.ChannelOutboundPublications.Where(p => p.Id == publicationId)
                .Select(p => p.EnvelopeJson).SingleAsync(ct);
            envelope = JsonSerializer.Deserialize<ChannelReply>(json)
                ?? throw new InvalidOperationException("Frozen channel envelope was null.");
        }

        var producerAccepted = false;
        try
        {
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(_settings.OutboundSendTimeoutSeconds), _clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            await _boundary.ReachAsync("producer-entered", publicationId, linked.Token);
            await _producer.SendAsync(envelope, linked.Token);
            producerAccepted = true;
            await _boundary.ReachAsync("producer-accepted", publicationId, linked.Token);
            await MarkPublishedAsync(publicationId, owner, ct);
            await _boundary.ReachAsync("outcome-committed", publicationId, ct);
            await RepairMetadataAsync(publicationId, ct);
            return true;
        }
        catch (Exception ex)
        {
            // The producer exception alone cannot establish non-acceptance. Even a caller cancellation
            // can arrive after broker acceptance. Preserve the same identity for a bounded retry.
            try
            {
                await MarkFailureAsync(publicationId, owner, ex,
                    ex is ChannelPublicationRefusedException,
                    producerAccepted ? "outcome-commit" : "producer-send", CancellationToken.None);
            }
            catch (Exception persistenceError)
            {
                _logger.LogError(persistenceError,
                    "Channel publication {PublicationId} outcome could not be committed; lease recovery retains it",
                    publicationId);
            }
            if (ex is OperationCanceledException && ct.IsCancellationRequested)
                throw;
            _logger.LogError(ex, "Channel publication {PublicationId} outcome is unknown", publicationId);
            return false;
        }
    }

    private async Task MarkPublishedAsync(Guid id, Guid owner, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var row = await db.ChannelOutboundPublications.Include(p => p.Sources)
            .SingleAsync(p => p.Id == id && p.State == "Publishing" && p.AttemptOwner == owner, ct);
        var now = UtcNow();
        row.State = "Published";
        row.PublishedAt = now;
        row.AttemptOwner = null;
        row.AttemptExpiresAt = null;
        row.NextAttemptAt = null;
        if (row.Path != "trailing")
        {
            var ids = row.Sources.Select(s => s.QueueMessageId).ToList();
            var sources = await db.SessionQueuedMessages.Where(m => ids.Contains(m.Id)).ToListAsync(ct);
            foreach (var source in sources)
                source.ChannelReplySettledAt = now;
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task MarkFailureAsync(Guid id, Guid owner, Exception exception,
        bool definiteRefusal, string stage, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var row = await db.ChannelOutboundPublications.Include(p => p.Sources)
            .SingleOrDefaultAsync(p => p.Id == id && p.State == "Publishing" && p.AttemptOwner == owner, ct);
        if (row is null)
            return;
        var exhausted = row.AttemptCount >= _settings.OutboundMaxAttempts
            || row.CreatedAt <= UtcNow().AddMinutes(-_settings.PendingReplyTtlMinutes);
        row.State = exhausted ? "Held" : definiteRefusal ? "Pending" : "Unknown";
        row.AttemptOwner = null;
        row.AttemptExpiresAt = null;
        row.NextAttemptAt = row.State == "Held" ? null : UtcNow().AddSeconds(_settings.OutboundRetrySeconds);
        row.LastFailure = definiteRefusal
            ? ((ChannelPublicationRefusedException)exception).Code
            : exception.GetType().Name;
        row.FailureStage = stage;
        var firstHeldIncident = row.State == "Held" && row.IncidentId is null;
        if (row.State == "Unknown" || row.State == "Held")
        {
            await RecordIncidentIfNeededAsync(db, row, ct);
            await _boundary.ReachAsync("before-incident-commit", row.Id, ct);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        if (firstHeldIncident)
            await NotifyAfterIncidentAsync(row);
    }

    private async Task HoldExpiredAsync(Guid id, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var row = await db.ChannelOutboundPublications.Include(p => p.Sources)
            .SingleOrDefaultAsync(p => p.Id == id
                && (p.State == "Pending" || p.State == "Unknown"), ct);
        if (row is null || row.CreatedAt > UtcNow().AddMinutes(-_settings.PendingReplyTtlMinutes))
            return;
        row.State = "Held";
        row.NextAttemptAt = null;
        row.FailureStage = "age-limit";
        row.LastFailure = "Original obligation age limit reached";
        var firstHeldIncident = row.IncidentId is null;
        await RecordIncidentIfNeededAsync(db, row, ct);
        await _boundary.ReachAsync("before-incident-commit", row.Id, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        if (firstHeldIncident)
            await NotifyAfterIncidentAsync(row);
    }

    private async Task NotifyAfterIncidentAsync(ChannelOutboundPublication row)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var channelId = await db.ChatChannels.AsNoTracking()
                .Where(c => c.Provider == row.Provider && c.ExternalId == row.ConversationId)
                .Select(c => (Guid?)c.Id).FirstOrDefaultAsync();
            if (channelId is not Guid id)
                return;
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(_settings.OutboundSendTimeoutSeconds), _clock);
            var detail = row.LastFailure == "MsgSizeTooLarge"
                ? "The broker refused this reply because its envelope was too large."
                : "This reply may already have been published.";
            await scope.ServiceProvider.GetRequiredService<ChatChannelService>().SendAsync(
                id, $"[Antiphon] {detail} Incident {row.IncidentId} needs attention.", timeout.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Channel publication {PublicationId} incident notice could not be sent", row.Id);
        }
    }

    private async Task RecordIncidentIfNeededAsync(AppDbContext db, ChannelOutboundPublication row, CancellationToken ct)
    {
        if (row.IncidentId is not null)
            return;
        var sourceIds = string.Join(",", row.Sources.Select(s => s.QueueMessageId));
        var definiteRefusal = row.LastFailure == "MsgSizeTooLarge";
        var outcome = definiteRefusal
            ? "the broker refused this envelope; it was not published"
            : "publication may have happened";
        var message = $"Channel publication {row.Id} for session {row.SessionId}, sources {sourceIds}, "
            + $"target {row.Provider}:{row.ConversationId}, stage {row.FailureStage}, "
            + $"attempt {row.AttemptCount}: {outcome}; state {row.State}.";
        var now = UtcNow();
        row.IncidentId = Guid.NewGuid();
        db.AgentIncidents.Add(new AgentIncident
        {
            Id = row.IncidentId.Value, AgentId = row.AgentId, SessionId = row.SessionId,
            Kind = AgentIncidentKind.ChannelReplyLost, Severity = AlertSeverity.Critical,
            Message = message, FailureReason = definiteRefusal ? "BrokerRefused" : "PublicationUnknown",
            CreatedAt = now,
        });
        db.Alerts.Add(new Alert
        {
            Id = Guid.NewGuid(), AgentId = row.AgentId, SessionId = row.SessionId,
            Severity = AlertSeverity.Critical, Source = "bridge",
            Title = definiteRefusal ? "ChannelReplyLost: broker refused" : "ChannelReplyLost: publication uncertain",
            Detail = message, DedupKey = $"bridge:outbound:{row.Id}", CreatedAt = now,
        });
        await Task.CompletedTask;
    }

    public async Task<int> RecoverDueAsync(CancellationToken ct, Guid? sessionId = null)
    {
        var now = UtcNow();
        List<ChannelOutboundPublication> due;
        await using (var scope = _scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            due = await db.ChannelOutboundPublications.AsNoTracking()
                .Where(p => (sessionId == null || p.SessionId == sessionId) &&
                    ((p.State == "Publishing" && p.AttemptExpiresAt <= now)
                    || ((p.State == "Pending" || p.State == "Unknown")
                        && (p.NextAttemptAt == null || p.NextAttemptAt <= now))))
                .OrderBy(p => p.CreatedAt).Take(_settings.OutboundPageSize).ToListAsync(ct);
        }
        foreach (var row in due)
        {
            if (row.State == "Publishing" && row.AttemptOwner is Guid owner)
            {
                await MarkFailureAsync(row.Id, owner,
                    new TimeoutException("Publication attempt expired without a durable outcome."),
                    false, "attempt-lease-expired", ct);
                continue;
            }
            if (row.CreatedAt <= now.AddMinutes(-_settings.PendingReplyTtlMinutes))
            {
                await HoldExpiredAsync(row.Id, ct);
                continue;
            }
            await AttemptAsync(row.Id, ct);
        }
        await using (var scope = _scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var published = await db.ChannelOutboundPublications.AsNoTracking()
                .Where(p => (sessionId == null || p.SessionId == sessionId)
                    && p.State == "Published" && p.MetadataStampedAt == null)
                .OrderBy(p => p.PublishedAt).Take(_settings.OutboundPageSize)
                .Select(p => p.Id).ToListAsync(ct);
            foreach (var id in published)
                await RepairMetadataAsync(id, ct);
        }
        return due.Count;
    }

    public async Task RepairMetadataAsync(Guid id, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.ChannelOutboundPublications.SingleAsync(p => p.Id == id, ct);
            if (row.State != "Published" || row.MetadataStampedAt is not null)
                return;
            var envelope = JsonSerializer.Deserialize<ChannelReply>(row.EnvelopeJson)!;
            var channels = scope.ServiceProvider.GetRequiredService<ChatChannelService>();
            await channels.StampLastReplyAsync(row.Provider, row.ConversationId, envelope.Text ?? "", ct);
            var taskIds = JsonSerializer.Deserialize<List<Guid>>(row.BundleTaskIdsJson) ?? [];
            if (taskIds.Count > 0)
            {
                var included = envelope.Attachments.Select(a => a.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var tasks = await db.AgentTasks.Where(t => taskIds.Contains(t.Id)).ToListAsync(ct);
                foreach (var task in tasks)
                    if (DeliverableBundleService.ListAttachableFiles(task).Any(included.Contains))
                        task.DeliverableDeliveredAt = row.PublishedAt;
            }
            row.MetadataStampedAt = UtcNow();
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Channel publication {PublicationId} metadata repair failed", id);
        }
    }
}
