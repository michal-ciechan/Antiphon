using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using System.Collections.Concurrent;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The inbound half of the channel bridge: consumes normalized <see cref="ChannelMessage"/>s from the
/// messaging gateway's inbound topic (Telegram today; any provider the gateway grows), upserts the
/// <see cref="ChatChannel"/> catalog, and — for channels bound to an agent — ensures the agent's
/// session is running and queues the message text into it ("wait until idle", so it never interrupts
/// mid-turn work). Reply routing back down the channel is the <see cref="ChannelReplyDispatcher"/>'s job.
///
/// Hosted only when <c>ChannelBridge:Enabled</c> is true; consume failures back off and retry so a
/// broker outage degrades to "messages arrive late", never a crashed server.
/// </summary>
public sealed class ChannelBridgeService : BackgroundService
{
    private static readonly TimeSpan ConsumeRetryBackoff = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SessionPollInterval = TimeSpan.FromSeconds(2);

    private readonly IAntiphonMessagingConsumer _consumer;
    private readonly SessionMessageQueueService _queue;
    private readonly ChannelInboundDebouncer _debouncer;
    private readonly IEventBus _eventBus;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Settings.ChannelBridgeSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChannelBridgeService> _logger;
    private readonly ChannelInboundWakeSignal _wakeSignal;
    private readonly ConcurrentDictionary<Guid, byte> _buffered = new();
    private readonly SemaphoreSlim _drainGate = new(1, 1);
    private long _lastInboundScanSequence;
    private int _completedDrainIterations;

    internal int CompletedDrainIterations => Volatile.Read(ref _completedDrainIterations);

    public ChannelBridgeService(
        IAntiphonMessagingConsumer consumer,
        SessionMessageQueueService queue,
        ChannelInboundDebouncer debouncer,
        IEventBus eventBus,
        IServiceScopeFactory scopeFactory,
        IOptions<Settings.ChannelBridgeSettings> settings,
        TimeProvider timeProvider,
        ILogger<ChannelBridgeService> logger,
        ChannelInboundWakeSignal? wakeSignal = null)
    {
        _consumer = consumer;
        _queue = queue;
        _debouncer = debouncer;
        _eventBus = eventBus;
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _wakeSignal = wakeSignal ?? new ChannelInboundWakeSignal();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Channel bridge started; consuming inbound channel messages");
        using var workerCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var worker = RunWakeWorkerAsync(workerCts.Token);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await foreach (var delivery in _consumer.ConsumeDeliveriesAsync(stoppingToken))
                    {
                        if (delivery.Message is not { } message)
                        {
                            _logger.LogWarning("Discarding malformed inbound broker record: {Diagnostic}", delivery.Diagnostic);
                            await delivery.AcknowledgeAsync($"malformed:{delivery.Diagnostic}", stoppingToken);
                            continue;
                        }
                        var accepted = await AcceptInboundAsync(message, stoppingToken);
                        await delivery.AcknowledgeAsync(accepted is null ? "ignored" : "accepted", stoppingToken);
                        if (accepted is Guid id)
                            _wakeSignal.Signal(id);
                    }

                    return; // stream completed (only fakes do this).
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Channel bridge consume loop failed; retrying in {Backoff}s",
                        ConsumeRetryBackoff.TotalSeconds);
                    try { await Task.Delay(ConsumeRetryBackoff, _timeProvider, stoppingToken); }
                    catch (OperationCanceledException) { return; }
                }
            }
        }
        finally
        {
            await workerCts.CancelAsync();
            await worker;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        // Drain buffered (debounced) messages so a shutdown never eats a half-window of chat.
        await _debouncer.FlushAllAsync();
    }

    /// <summary>Direct entry point used by callers and tests; completes this input's first drain.</summary>
    internal async Task HandleInboundAsync(ChannelMessage message, CancellationToken ct)
    {
        if (await AcceptInboundAsync(message, ct) is Guid id)
            await ProcessInboundAsync(id, ct);
    }

    private async Task<Guid?> AcceptInboundAsync(ChannelMessage message, CancellationToken ct)
    {
        if (message.Author.IsSelf)
            return null;
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.ChannelInbounds.AsNoTracking().FirstOrDefaultAsync(i =>
            i.Provider == message.Channel && i.ConversationId == message.Conversation.Id
            && i.NativeMessageId == message.ChannelMessageId, ct);
        if (existing is not null)
            return existing.QueueMessageId is null && existing.EnvelopeJson is not null ? existing.Id : null;

        var channels = scope.ServiceProvider.GetRequiredService<ChatChannelService>();
        var (channel, legacyDuplicate) = await channels.UpsertFromInboundAsync(message, ct);
        var deliverable = !legacyDuplicate && channel.Enabled && channel.AgentId is not null
            && (!string.IsNullOrWhiteSpace(message.Text) || message.Attachments.Count > 0);
        var inbound = new ChannelInbound
        {
            Id = Guid.NewGuid(), Provider = message.Channel,
            ConversationId = message.Conversation.Id,
            NativeMessageId = message.ChannelMessageId,
            EnvelopeJson = deliverable ? JsonSerializer.Serialize(message, Antiphon.Messaging.MessagingJson.Options) : null,
            AgentId = deliverable ? channel.AgentId : null,
            ChatChannelId = deliverable ? channel.Id : null,
            AcceptedAt = _timeProvider.GetUtcNow().UtcDateTime,
        };
        db.ChannelInbounds.Add(inbound);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await PublishChannelChangedAsync(channel.Id, ct);
        return deliverable ? inbound.Id : null;
    }

    private async Task RunWakeWorkerAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await DrainPendingAsync(ct);
                    Interlocked.Increment(ref _completedDrainIterations);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Durable channel inbound drain failed; retrying");
                }
                using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var signal = _wakeSignal.Reader.WaitToReadAsync(waitCts.Token).AsTask();
                var tick = Task.Delay(TimeSpan.FromSeconds(10), _timeProvider, waitCts.Token);
                await Task.WhenAny(signal, tick);
                await waitCts.CancelAsync();
                while (_wakeSignal.Reader.TryRead(out _)) { }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    internal async Task DrainPendingAsync(CancellationToken ct)
    {
        await _drainGate.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var page = await db.ChannelInbounds.AsNoTracking()
                .Where(i => i.AcceptanceSequence > _lastInboundScanSequence
                    && i.AgentId != null && i.QueueMessageId == null && i.EnvelopeJson != null)
                .OrderBy(i => i.AcceptanceSequence)
                .Take(64).Select(i => new { i.Id, i.AcceptanceSequence }).ToListAsync(ct);
            foreach (var inbound in page)
            {
                try { await ProcessInboundAsync(inbound.Id, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Pending channel inbound {InboundId} remains for a later scan", inbound.Id);
                }
                _lastInboundScanSequence = inbound.AcceptanceSequence;
            }
            if (page.Count == 0)
                _lastInboundScanSequence = 0; // next pass revisits held and failed rows

            // A crash or uncertain terminal write after queue ownership must not strand a
            // Channel row on a non-AlwaysOn agent. The queue keeps its original attempt floor
            // and performs receipt/cap checks before any new input.
            var ownedSessions = await db.SessionQueuedMessages.AsNoTracking()
                .Where(q => q.SourceChannelInboundId != null
                    && q.Status == QueuedMessageStatus.Pending
                    && q.AgentSession.Status == SessionStatus.Running)
                .OrderBy(q => q.CreatedAt).Take(64)
                .Select(q => q.AgentSessionId).Distinct().ToListAsync(ct);
            foreach (var sessionId in ownedSessions)
                await _queue.FlushSessionAsync(sessionId, ct);
        }
        finally { _drainGate.Release(); }
    }

    private async Task ProcessInboundAsync(Guid inboundId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var inbound = await db.ChannelInbounds.AsNoTracking().SingleOrDefaultAsync(i => i.Id == inboundId, ct);
        if (inbound?.EnvelopeJson is null || inbound.QueueMessageId is not null
            || inbound.AgentId is not Guid agentId || inbound.ChatChannelId is not Guid channelId)
            return;
        await using var claim = await AgentWakeClaim.TryAcquireAsync(db, agentId, ct);
        if (claim is null)
            return; // another bridge instance owns this agent; its lease ends with its DB connection
        inbound = await db.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == inboundId, ct);
        if (inbound.QueueMessageId is not null)
            return;
        var message = JsonSerializer.Deserialize<ChannelMessage>(inbound.EnvelopeJson, Antiphon.Messaging.MessagingJson.Options)!;
        var channel = await db.ChatChannels.AsNoTracking().SingleAsync(c => c.Id == channelId, ct);
        Guid? sessionId;
        try { sessionId = await EnsureAgentSessionAsync(agentId, ct); }
        catch (ConflictException ex) when (ex.Code == HerdrSupervisionStateService.HeldCode)
        {
            await RecordWakeIncidentAsync(inboundId, agentId, "HerdrSupervisionHeld", ex.Message, ct);
            await RaiseBridgeDropAlertAsync(channel, agentId, ct);
            return;
        }
        catch (ModelDisabledException ex)
        {
            await NotifyHeldAgentInboundAsync(channel, message, agentId, ex, ct);
            await RaiseBridgeDropAlertAsync(channel, agentId, ct);
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Channel inbound {InboundId} remains pending after wake refusal", inboundId);
            return;
        }
        if (sessionId is not Guid liveSessionId)
        {
            await RecordWakeIncidentAsync(inboundId, agentId, "ChannelWakeTimeout",
                "Agent session did not reach Running before the wake deadline", ct);
            await RaiseBridgeDropAlertAsync(channel, agentId, ct);
            return;
        }
        if (!_buffered.TryAdd(inboundId, 0))
            return;
        try
        {
            await _debouncer.AddAsync(message,
                batch => FlushLaneAsync(channel, agentId, liveSessionId, batch), ct);
        }
        catch
        {
            _buffered.TryRemove(inboundId, out _);
            throw;
        }
    }

    private async Task RecordWakeIncidentAsync(Guid inboundId, Guid agentId,
        string reason, string detail, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var inbound = await db.ChannelInbounds.FromSqlInterpolated(
            $"SELECT * FROM \"ChannelInbounds\" WHERE \"Id\" = {inboundId} FOR UPDATE").SingleAsync(ct);
        if (reason == "ChannelWakeTimeout" && inbound.WakeTimeoutIncidentAt is not null)
            return;
        if (reason == "HerdrSupervisionHeld")
        {
            var heldAt = await db.AgentSupervisionStates.AsNoTracking()
                .Where(s => s.AgentId == agentId).Select(s => s.HerdrFailureHeldAt).FirstOrDefaultAsync(ct);
            if (heldAt is not null && await db.AgentIncidents.AnyAsync(i => i.AgentId == agentId
                && i.Kind == AgentIncidentKind.ChannelReplyLost && i.FailureReason == reason
                && i.CreatedAt >= heldAt, ct))
                return;
        }
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(), AgentId = agentId, Kind = AgentIncidentKind.ChannelReplyLost,
            Severity = AlertSeverity.Critical, FailureReason = reason, CreatedAt = now,
            Message = ColumnText.Clip($"Inbound message {inbound.Provider}:{inbound.ConversationId}/{inbound.NativeMessageId} is pending and has not reached the agent: {detail}", AgentIncident.MessageMaxLength),
        });
        if (reason == "ChannelWakeTimeout") inbound.WakeTimeoutIncidentAt = now;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private sealed class AgentWakeClaim(AppDbContext db, long key) : IAsyncDisposable
    {
        public static async Task<AgentWakeClaim?> TryAcquireAsync(AppDbContext db, Guid agentId, CancellationToken ct)
        {
            var key = BitConverter.ToInt64(agentId.ToByteArray(), 0);
            await db.Database.OpenConnectionAsync(ct);
            try
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = "SELECT pg_try_advisory_lock(@key)";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "key";
                parameter.Value = key;
                command.Parameters.Add(parameter);
                if (await command.ExecuteScalarAsync(ct) is true)
                    return new AgentWakeClaim(db, key);
                await db.Database.CloseConnectionAsync();
                return null;
            }
            catch
            {
                await db.Database.CloseConnectionAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT pg_advisory_unlock(@key)";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "key";
            parameter.Value = key;
            command.Parameters.Add(parameter);
            try { await command.ExecuteScalarAsync(); }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }

    // Routes one debounced batch (1..n same-sender messages) into the session: single truthful
    // envelope header (first message's metadata), one line per message text; attachments are saved
    // to the agent's inbox and referenced by path so the (vision-capable) agent can Read them.
    private async Task FlushLaneAsync(
        ChatChannel channel, Guid agentId, Guid sessionId, IReadOnlyList<ChannelInboundDebouncer.Buffered> batch)
    {
        var memberIds = new List<Guid>();
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var item in batch)
            {
                var id = await db.ChannelInbounds.AsNoTracking()
                    .Where(i => i.Provider == item.Message.Channel
                        && i.ConversationId == item.Message.Conversation.Id
                        && i.NativeMessageId == item.Message.ChannelMessageId)
                    .Select(i => i.Id).SingleAsync();
                memberIds.Add(id);
            }
            var existingOwner = await db.ChannelInbounds.AsNoTracking()
                .Where(i => i.Id == memberIds[0]).Select(i => i.QueueMessageId).SingleAsync();
            if (existingOwner is not null)
                return;
            var first = batch[0].Message;
            var newest = batch[^1].Message;
            var inboxDir = await ResolveInboxDirAsync(agentId);
            var text = string.Join("\n", batch.Select((b, index) =>
                RenderMessageBody(b.Message, inboxDir, memberIds[index])));
            var prompt = ChannelPromptFormat.Format(
                channel,
                first.Author.DisplayName ?? first.Author.Username ?? first.Author.Id,
                first.Author.Username,
                first.Timestamp,
                text,
                TimeZoneInfo.Local);

            // THE enqueue IS the reply correlation (CARD-0067). The persisted row carries everything
            // the reply needs — Body (the prompt to match a turn against), Origin=Channel and
            // ConversationKey ('{provider}:{conversationId}') — and ChannelReplyDispatcher resolves
            // the target from it at dispatch time. There used to be a Track() call right here, writing
            // the route back OUT into process memory a moment before the route IN was committed to
            // Postgres: two stores for the two halves of one round trip. A hard restart on 2026-08-17
            // between the two lost four live correlations and a family's guest list. It also removes an
            // ordering hazard rather than working around one — the row exists before the queue types a
            // single keystroke, so an idle agent that answers inside EnqueueAsync can no longer finish
            // its turn before the correlation is recorded.
            Guid? queueId = null;
            await _queue.EnqueueAsync(
                sessionId, prompt, MessageSendMode.WhenIdle, CancellationToken.None,
                origin: QueuedMessageOrigin.Channel,
                conversationKey: $"{channel.Provider}:{newest.Conversation.Id}",
                deliverIfIdle: false,
                sourceChannelInboundId: memberIds[0],
                channelMemberInboundIds: memberIds,
                onCreated: id => queueId = id);
            if (queueId is null)
                throw new InvalidOperationException("Channel queue insert did not identify its owner.");
            await _queue.FlushSessionAsync(sessionId, CancellationToken.None);
            _logger.LogInformation(
                "Routed {Count} {Provider} message(s) on channel {ChannelId} to agent {AgentId} session {SessionId}",
                batch.Count, channel.Provider, channel.Id, agentId, sessionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Debounced flush left {Count} durable message(s) pending on channel {ChannelId}",
                batch.Count, channel.Id);
            await RaiseBridgeDropAlertAsync(channel, agentId, CancellationToken.None);
        }
        finally
        {
            foreach (var id in memberIds)
                _buffered.TryRemove(id, out _);
        }
    }

    /// <summary>The agent's attachment inbox (<c>&lt;workingDirectory&gt;\.antiphon\inbox</c>); null when unresolvable.</summary>
    private async Task<string?> ResolveInboxDirAsync(Guid agentId)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var workingDirectory = await db.Agents.AsNoTracking()
                .Where(a => a.Id == agentId)
                .Select(a => a.WorkingDirectory)
                .FirstOrDefaultAsync();
            return string.IsNullOrWhiteSpace(workingDirectory)
                ? null
                : Path.Combine(workingDirectory, ".antiphon", "inbox");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve inbox dir for agent {AgentId}", agentId);
            return null;
        }
    }

    /// <summary>
    /// One message's deliverable body: its text line(s) plus one bracketed line per attachment.
    /// Inlined bytes are written into the agent's inbox and referenced by absolute path (the agent
    /// Reads them — photos included, it has vision); metadata-only attachments (download failed or
    /// over the inline cap) become a visible note so the sender's file is never silently ignored.
    /// </summary>
    private string RenderMessageBody(ChannelMessage message, string? inboxDir, Guid inboundId)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(message.Text))
            parts.Add(message.Text.Trim());

        for (var i = 0; i < message.Attachments.Count; i++)
        {
            var attachment = message.Attachments[i];
            var word = AttachmentWord(attachment.Kind);
            if (attachment.Content is not { Length: > 0 } bytes)
            {
                parts.Add($"[{word} attached — could not be imported (no content relayed)]");
                continue;
            }
            if (inboxDir is null)
            {
                parts.Add($"[{word} attached — could not be saved (agent has no working directory)]");
                continue;
            }

            try
            {
                Directory.CreateDirectory(inboxDir);
                var fileName = SafeFileName(message, attachment, i, inboundId);
                var path = Path.Combine(inboxDir, fileName);
                File.WriteAllBytes(path, bytes);
                parts.Add($"[{word} attached: {path}]");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save inbound attachment {Ref} to {Dir}", attachment.ChannelRef, inboxDir);
                parts.Add($"[{word} attached — could not be saved]");
            }
        }

        return string.Join("\n", parts);
    }

    private static string AttachmentWord(AttachmentKind kind) => kind switch
    {
        AttachmentKind.Image => "photo",
        AttachmentKind.Video => "video",
        AttachmentKind.Audio or AttachmentKind.Voice => "audio",
        _ => "file",
    };

    // <utc-stamp>-<msgid>-<n>-<original name> keeps inbox files unique, ordered, and traceable to
    // their message. Original names are untrusted channel data — strip anything path-flavoured.
    private string SafeFileName(ChannelMessage message, Attachment attachment, int index, Guid inboundId)
    {
        // Attachment names come from another machine: both path separators must be removed
        // even when this server runs on a platform where backslash is a legal filename byte.
        var original = Path.GetFileName((attachment.Name ?? "").Replace('\\', '/'));
        foreach (var c in Path.GetInvalidFileNameChars())
            original = original.Replace(c, '_');
        if (string.IsNullOrWhiteSpace(original))
            original = "attachment" + (attachment.Kind == AttachmentKind.Image ? ".jpg" : ".bin");
        return $"{inboundId:N}-{index}-{original}";
    }

    /// <summary>
    /// CARD-0281: a held model is not a consume-loop crash. One Critical ChannelReplyLost /
    /// ProviderCapacity incident per hold episode, plus the same capacity notice the withhold
    /// path sends, then today's drop path.
    /// </summary>
    private async Task NotifyHeldAgentInboundAsync(
        ChatChannel channel, ChannelMessage message, Guid agentId, ModelDisabledException ex,
        CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var already = await db.AgentIncidents.AsNoTracking().AnyAsync(
                i => i.AgentId == agentId
                    && i.Kind == AgentIncidentKind.ChannelReplyLost
                    && i.FailureReason == "ProviderCapacity"
                    && i.CreatedAt >= ex.Hold.HitAt, ct);
            if (already)
                return;
            if (!already)
            {
                var supervisor = scope.ServiceProvider.GetService<AgentSupervisorService>();
                if (supervisor is not null)
                {
                    var noticeWhy =
                        $"the {ex.Hold.Kind} provider is held ({ex.Hold.ModelAlias}); no fallback declared";
                    await supervisor.RecordIncidentAsync(
                        agentId,
                        null,
                        AgentIncidentKind.ChannelReplyLost,
                        AlertSeverity.Critical,
                        ColumnText.Clip(
                            $"Inbound on {channel.Provider}:{message.Conversation.Id} is pending and has not reached the agent "
                            + $"because {noticeWhy}.",
                            AgentIncident.MessageMaxLength),
                        failureReason: "ProviderCapacity",
                        ct: ct);
                    await db.SaveChangesAsync(ct);
                }
            }

            var channels = scope.ServiceProvider.GetService<ChatChannelService>();
            if (channels is null)
                return;
            var notice = ProviderCapacityNotice.Format(
                ex.Hold.Kind,
                ex.Hold.ModelAlias,
                status: null,
                reasonPhrase: ex.Hold.Reason,
                fallbackDeclared: false);
            try
            {
                await channels.SendAsync(
                    channel.Id,
                    notice,
                    new ChannelSendOptions(ReplyHandle: channel.ReplyHandle ?? message.ReplyHandle),
                    ct);
            }
            catch (ConflictException cex) when (cex.Code == "channel_disabled")
            {
                _logger.LogWarning(
                    "Provider-capacity notice not sent: channel {ChannelId} is disabled", channel.Id);
            }
        }
        catch (Exception notifyEx) when (notifyEx is not OperationCanceledException)
        {
            _logger.LogWarning(notifyEx,
                "Held-agent inbound notice for channel {ChannelId} failed", channel.Id);
        }
    }

    private async Task RaiseBridgeDropAlertAsync(ChatChannel channel, Guid agentId, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IAlertService>().RaiseAsync(
                new AlertRaise(
                    AlertSeverity.Warning,
                    Source: "bridge",
                    Title: "Inbound channel message pending",
                    Detail: $"Channel '{channel.Title ?? channel.ExternalId}' ({channel.Provider}) is bound to an "
                        + "agent whose session has not become ready; the message is pending and has not reached the agent.",
                    DedupKey: $"bridge:pending:{channel.Id}",
                    AgentId: agentId),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Bridge drop alert failed");
        }
    }

    /// <summary>
    /// The bound agent's live Running session id, starting the agent when it has none. Waits out
    /// Starting → Running plus a settle delay for fresh starts (TUI boot). Null on timeout.
    /// </summary>
    private async Task<Guid?> EnsureAgentSessionAsync(Guid agentId, CancellationToken ct)
    {
        var deadline = _timeProvider.GetUtcNow().AddSeconds(_settings.AgentStartTimeoutSeconds);
        var startAttempted = false;
        var startedFresh = false;

        while (_timeProvider.GetUtcNow() < deadline && !ct.IsCancellationRequested)
        {
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var agent = await db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, ct);
                if (agent is null)
                    return null;

                // A Running pointer is not permission to bypass operator/supervisor intent.
                // Check before the fast path as well as relying on StartAsync's reservation gate.
                var supervision = await db.AgentSupervisionStates.AsNoTracking()
                    .SingleOrDefaultAsync(s => s.AgentId == agentId, ct);
                if (supervision?.Suspended == true || supervision?.LivenessLatchedAt is not null)
                    throw new ConflictException("Channel wake is held by operator intent.", "standing_start_intent_revoked");
                if (supervision?.HerdrFailureHeldAt is not null)
                    throw new ConflictException("Channel wake is held by Herdr supervision.", HerdrSupervisionStateService.HeldCode);
                if (supervision?.ContinuityHeldAt is not null)
                    throw new ConflictException("Channel wake requires a continuity decision.", StandingContinuityState.HeldCode);

                if (Guid.TryParse(agent.PersistentSessionId, out var sessionId))
                {
                    var status = await db.AgentSessions
                        .Where(s => s.Id == sessionId)
                        .Select(s => (SessionStatus?)s.Status)
                        .FirstOrDefaultAsync(ct);

                    if (status == SessionStatus.Running)
                    {
                        if (startedFresh)
                            await Task.Delay(
                                TimeSpan.FromSeconds(_settings.AgentReadyDelaySeconds), _timeProvider, ct);
                        return sessionId;
                    }
                    if (status == SessionStatus.Failed && startAttempted)
                        return null;
                    if (status is SessionStatus.Starting)
                    {
                        await Task.Delay(SessionPollInterval, _timeProvider, ct);
                        continue;
                    }
                }

                if (!startAttempted)
                {
                    startAttempted = true;
                    startedFresh = true;
                    var control = scope.ServiceProvider.GetRequiredService<AgentControlService>();
                    // IgnoreSubscriptionQuota: a channel inbound cannot pick another provider.
                    await control.StartAsync(
                        agentId, new StartAgentRequest(IgnoreSubscriptionQuota: true), ct,
                        automatic: true);
                    _logger.LogInformation("Started agent {AgentId} to receive a channel message", agentId);
                }
            }

            await Task.Delay(SessionPollInterval, _timeProvider, ct);
        }

        return null;
    }

    private async Task PublishChannelChangedAsync(Guid channelId, CancellationToken ct)
    {
        try
        {
            await _eventBus.PublishToAllAsync("ChannelChanged", new { channelId }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed to publish ChannelChanged for {ChannelId}", channelId);
        }
    }
}
