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
    private readonly ChannelReplyDispatcher _dispatcher;
    private readonly SessionMessageQueueService _queue;
    private readonly IEventBus _eventBus;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Settings.ChannelBridgeSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChannelBridgeService> _logger;

    public ChannelBridgeService(
        IAntiphonMessagingConsumer consumer,
        ChannelReplyDispatcher dispatcher,
        SessionMessageQueueService queue,
        IEventBus eventBus,
        IServiceScopeFactory scopeFactory,
        IOptions<Settings.ChannelBridgeSettings> settings,
        TimeProvider timeProvider,
        ILogger<ChannelBridgeService> logger)
    {
        _consumer = consumer;
        _dispatcher = dispatcher;
        _queue = queue;
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
        if (string.IsNullOrWhiteSpace(message.Text))
        {
            _logger.LogInformation(
                "Channel {ChannelId} message {MessageId} has no text (attachment only?); not routed",
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

        var prompt = BuildPrompt(channel, message);
        // Register the reply correlation BEFORE enqueuing: an idle agent gets the message delivered
        // synchronously inside EnqueueAsync, and its turn could complete before a later Track() ran.
        _dispatcher.Track(liveSessionId, new ChannelReplyDispatcher.PendingChannelReply(
            channel.Id,
            channel.Provider,
            message.ReplyHandle,
            message.Conversation.Id,
            prompt,
            _timeProvider.GetUtcNow().UtcDateTime));

        await _queue.EnqueueAsync(liveSessionId, prompt, MessageSendMode.WhenIdle, ct);
        _logger.LogInformation(
            "Routed {Provider} message {MessageId} on channel {ChannelId} to agent {AgentId} session {SessionId}",
            channel.Provider, message.ChannelMessageId, channel.Id, agentId, liveSessionId);
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

    // The channel context header keeps the agent oriented (which chat, who's talking) without any
    // channel-specific machinery leaking into the session.
    private static string BuildPrompt(ChatChannel channel, ChannelMessage message)
    {
        var author = message.Author.DisplayName ?? message.Author.Username ?? message.Author.Id;
        var where = channel.Kind == ChatChannelKind.Direct
            ? "direct message"
            : $"\"{channel.Title ?? channel.ExternalId}\"";
        return $"[{Capitalize(channel.Provider)} {where} — from {author}] {message.Text!.Trim()}";
    }

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

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
