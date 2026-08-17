using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Dtos;
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

    public ChannelBridgeService(
        IAntiphonMessagingConsumer consumer,
        SessionMessageQueueService queue,
        ChannelInboundDebouncer debouncer,
        IEventBus eventBus,
        IServiceScopeFactory scopeFactory,
        IOptions<Settings.ChannelBridgeSettings> settings,
        TimeProvider timeProvider,
        ILogger<ChannelBridgeService> logger)
    {
        _consumer = consumer;
        _queue = queue;
        _debouncer = debouncer;
        _eventBus = eventBus;
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Channel bridge started; consuming inbound channel messages");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var message in _consumer.ConsumeAsync(stoppingToken))
                    await HandleInboundAsync(message, stoppingToken);

                return; // stream completed (only fakes do this) — done.
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

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        // Drain buffered (debounced) messages so a shutdown never eats a half-window of chat.
        await _debouncer.FlushAllAsync();
    }

    /// <summary>One inbound message: catalog it, then route it if its channel is bound. Internal for tests.</summary>
    internal async Task HandleInboundAsync(ChannelMessage message, CancellationToken ct)
    {
        // Our own bot's outbound messages echo back through getUpdates in group chats — never route those.
        if (message.Author.IsSelf)
            return;

        ChatChannel channel;
        bool duplicate;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var channels = scope.ServiceProvider.GetRequiredService<ChatChannelService>();
            (channel, duplicate) = await channels.UpsertFromInboundAsync(message, ct);
        }

        await PublishChannelChangedAsync(channel.Id, ct);

        if (duplicate)
        {
            _logger.LogDebug("Skipping duplicate message {MessageId} on channel {ChannelId}",
                message.ChannelMessageId, channel.Id);
            return;
        }

        if (!channel.Enabled || channel.AgentId is not Guid agentId)
            return;
        // Attachment-only messages (a bare photo/document) are deliverable — the file IS the
        // message (live miss 2026-07-29: Ola's UTR photo was dropped here and the agent asked her
        // for what she'd just sent). Only a message with neither text nor attachments is noise.
        if (string.IsNullOrWhiteSpace(message.Text) && message.Attachments.Count == 0)
        {
            _logger.LogInformation(
                "Channel {ChannelId} message {MessageId} has no text and no attachments; not routed",
                channel.Id, message.ChannelMessageId);
            return;
        }

        var sessionId = await EnsureAgentSessionAsync(agentId, ct);
        if (sessionId is not Guid liveSessionId)
        {
            _logger.LogWarning(
                "Channel {ChannelId} is bound to agent {AgentId} but no session became ready; message {MessageId} not routed",
                channel.Id, agentId, message.ChannelMessageId);
            await RaiseBridgeDropAlertAsync(channel, agentId, ct);
            return;
        }

        // Hand off to the same-sender debouncer: rapid-fire messages merge into one prompt after a
        // quiet window (0 = flush synchronously right here). The flush callback runs OUTSIDE this
        // awaited consume loop, so it owns its own failure alerting — degradation on a broken flush
        // is dropped-with-alert, never a silent unobserved-task fault.
        await _debouncer.AddAsync(
            message,
            batch => FlushLaneAsync(channel, agentId, liveSessionId, batch),
            ct);
        _logger.LogDebug(
            "Buffered {Provider} message {MessageId} on channel {ChannelId} for session {SessionId}",
            channel.Provider, message.ChannelMessageId, channel.Id, liveSessionId);
    }

    // Routes one debounced batch (1..n same-sender messages) into the session: single truthful
    // envelope header (first message's metadata), one line per message text; attachments are saved
    // to the agent's inbox and referenced by path so the (vision-capable) agent can Read them.
    private async Task FlushLaneAsync(
        ChatChannel channel, Guid agentId, Guid sessionId, IReadOnlyList<ChannelInboundDebouncer.Buffered> batch)
    {
        try
        {
            var first = batch[0].Message;
            var newest = batch[^1].Message;
            var inboxDir = await ResolveInboxDirAsync(agentId);
            var text = string.Join("\n", batch.Select(b => RenderMessageBody(b.Message, inboxDir)));
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
            await _queue.EnqueueAsync(
                sessionId, prompt, MessageSendMode.WhenIdle, CancellationToken.None,
                origin: QueuedMessageOrigin.Channel,
                conversationKey: $"{channel.Provider}:{newest.Conversation.Id}");
            _logger.LogInformation(
                "Routed {Count} {Provider} message(s) on channel {ChannelId} to agent {AgentId} session {SessionId}",
                batch.Count, channel.Provider, channel.Id, agentId, sessionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Debounced flush failed to route {Count} message(s) on channel {ChannelId}",
                batch.Count, channel.Id);
            await RaiseBridgeDropAlertAsync(channel, agentId, CancellationToken.None);
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
    private string RenderMessageBody(ChannelMessage message, string? inboxDir)
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
                var fileName = SafeFileName(message, attachment, i);
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
    private string SafeFileName(ChannelMessage message, Attachment attachment, int index)
    {
        var original = Path.GetFileName(attachment.Name ?? "");
        foreach (var c in Path.GetInvalidFileNameChars())
            original = original.Replace(c, '_');
        if (string.IsNullOrWhiteSpace(original))
            original = "attachment" + (attachment.Kind == AttachmentKind.Image ? ".jpg" : ".bin");
        var stamp = _timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmmss");
        return $"{stamp}-{message.ChannelMessageId}-{index}-{original}";
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
                    Title: "Inbound channel message dropped",
                    Detail: $"Channel '{channel.Title ?? channel.ExternalId}' ({channel.Provider}) is bound to an "
                        + "agent whose session never became ready; the message was not routed.",
                    DedupKey: $"bridge:drop:{channel.Id}",
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
                    await control.StartAsync(agentId, new StartAgentRequest(), ct);
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
