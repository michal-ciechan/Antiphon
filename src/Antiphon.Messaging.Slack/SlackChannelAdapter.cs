using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Antiphon.Messaging.Slack;

/// <summary>
/// <see cref="IChannelAdapter"/> for Slack, talking the raw Web API over <see cref="HttpClient"/>
/// and Socket Mode over a <see cref="ClientWebSocket"/> (no third-party client) — the sibling of
/// <c>TelegramChannelAdapter</c>.
///
/// Inbound: <c>apps.connections.open</c> yields a WebSocket URL down which Slack pushes event
/// envelopes; each is ACKED IMMEDIATELY on receipt, before any normalization, because Slack
/// redelivers unacked envelopes after ~3s and a slow ack turns one message into a redelivery storm.
/// At-least-once delivery is fine: the server dedups on <c>ChatChannel.LastChannelMessageId</c>.
///
/// Outbound: <see cref="ChannelReply"/> is denormalized to <c>chat.postMessage</c> (plus the
/// external-upload trio for attachments), merging any raw overrides last.
///
/// Socket Mode, rather than the Events API, because this gateway has no public HTTPS ingress —
/// see docs/superpowers/plans/2026-08-20-card-0107-slack-channel-plan.md §2.
/// </summary>
public sealed class SlackChannelAdapter : IChannelAdapter, IAsyncDisposable
{
    private const string ChannelKey = "slack";

    private readonly HttpClient _http;
    private readonly SlackSettings _settings;
    private readonly ILogger<SlackChannelAdapter> _logger;
    private readonly HashSet<string> _allowed;

    private readonly ConcurrentDictionary<string, SlackUser> _users = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string?> _conversationTitles = new(StringComparer.Ordinal);

    private ClientWebSocket? _socket;
    private string? _botUserId;

    public SlackChannelAdapter(HttpClient http, SlackSettings settings, ILogger<SlackChannelAdapter> logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
        _allowed = [.. settings.AllowedConversationIds];
        _botUserId = string.IsNullOrEmpty(settings.BotUserId) ? null : settings.BotUserId;
    }

    public string Channel => ChannelKey;

    /// <summary>Our own bot user id, from settings or resolved via <c>auth.test</c>. Null until resolved.</summary>
    public string? BotUserId => _botUserId;

    public ChannelCapabilities Capabilities => new()
    {
        Channel = ChannelKey,
        Mentions = true,
        Attachments = true,
        Edit = true,
        Delete = true,
        Reactions = true,
        // First adapter to set it: a Slack thread stays inside its parent conversation and the
        // thread_ts rides ReplyHandle, so replies land in the thread of the message they answer.
        Threads = true,
        // The Web API has no typing indicator for bots (users.setPresence is not it).
        TypingIndicator = false,
        // Producers send standard Markdown; this adapter renders it to mrkdwn itself.
        MarkdownFlavor = "Markdown",
        // Aligned to ChannelBridge:MaxReplyChars and the preamble's "4000 characters, phone-sized"
        // contract — that contract is Antiphon's, not Slack's (chat.postMessage allows far more).
        MaxTextLength = 4000,
        AttachmentKinds =
        [
            AttachmentKind.Image, AttachmentKind.Video, AttachmentKind.Audio,
            AttachmentKind.Voice, AttachmentKind.File, AttachmentKind.Other,
        ],
    };

    private TimeSpan ErrorBackoff => TimeSpan.FromSeconds(Math.Max(0, _settings.ErrorBackoffSeconds));

    private TimeSpan CapRetryAfter(int seconds) =>
        TimeSpan.FromSeconds(Math.Clamp(seconds, 0, Math.Max(0, _settings.MaxRetryAfterSeconds)));

    // ---------------------------------------------------------------- inbound

    public async IAsyncEnumerable<ChannelMessage> ReceiveAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                SocketOutcome outcome;
                try
                {
                    outcome = await PumpOnceAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    yield break;
                }
                catch (Exception ex)
                {
                    // Everything that is not OUR cancellation is transient: a WebSocketException from
                    // a recycled/half-open connection, a non-ok apps.connections.open, and — the one
                    // that has bitten this codebase before — an HttpClient TIMEOUT, which arrives as a
                    // TaskCanceledException (an OperationCanceledException) with nothing cancelled.
                    // Treating that as shutdown silently ends the ingress stream and the channel goes
                    // deaf with zero log output (live miss 2026-07-31, AZ Care).
                    if (ex is SlackTransientException)
                        _logger.LogWarning("[slack] {Reason}; reconnecting in {Backoff}s", ex.Message, ErrorBackoff.TotalSeconds);
                    else
                        _logger.LogWarning(ex, "[slack] socket pump failed; reconnecting in {Backoff}s", ErrorBackoff.TotalSeconds);

                    await DropSocketAsync();
                    if (!await DelayQuietAsync(ErrorBackoff, cancellationToken))
                        yield break;
                    continue;
                }

                foreach (var message in outcome.Messages)
                    yield return message;

                if (outcome.Reconnect)
                    await DropSocketAsync();
            }
        }
        finally
        {
            await DropSocketAsync();
        }
    }

    /// <summary>
    /// One turn of the Socket Mode loop: ensure a connection, read one envelope, ACK it, and
    /// normalize it if it carries a message event. Returns the messages to yield plus whether the
    /// connection should be reopened (Slack asks for that routinely — it is not an error).
    /// </summary>
    private async Task<SocketOutcome> PumpOnceAsync(CancellationToken ct)
    {
        var socket = await EnsureConnectedAsync(ct);

        var (frame, closed) = await ReceiveTextAsync(socket, ct);
        if (closed)
        {
            _logger.LogInformation("[slack] socket closed by the peer; reopening");
            return SocketOutcome.Reopen;
        }
        if (frame is null)
            return SocketOutcome.Empty;   // a binary/ping frame — nothing to do

        JsonDocument doc;
        try { doc = JsonDocument.Parse(frame); }
        catch (JsonException)
        {
            _logger.LogWarning("[slack] ignoring a non-JSON socket frame ({Bytes} bytes)", frame.Length);
            return SocketOutcome.Empty;
        }

        using (doc)
        {
            var root = doc.RootElement;

            // ACK FIRST — before normalization, before attachment hydration, before anything that
            // can fail or block. Slack redelivers what it has not seen acked within ~3 seconds.
            if (GetString(root, "envelope_id") is { Length: > 0 } envelopeId)
                await AckAsync(socket, envelopeId, ct);

            switch (GetString(root, "type"))
            {
                case "hello":
                    _logger.LogInformation("[slack] socket mode connection is live");
                    return SocketOutcome.Empty;

                case "disconnect":
                    // Slack recycles connections routinely (reason "warning"/"refresh_requested").
                    _logger.LogInformation("[slack] disconnect requested ({Reason}); reopening", GetString(root, "reason") ?? "unspecified");
                    return SocketOutcome.Reopen;

                case "events_api":
                    break;

                default:
                    // slash_commands / interactive envelopes: acked (so Slack stops resending) but
                    // not handled in v1 — the app subscribes to message.* events only.
                    return SocketOutcome.Empty;
            }

            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                return SocketOutcome.Empty;

            var message = await TryNormalizeAsync(payload, ct);
            if (message is null)
                return SocketOutcome.Empty;

            // The reply-loop guard. Slack — unlike Telegram's getUpdates — delivers the bot's OWN
            // chat.postMessage back down this same stream, so an unguarded echo is an infinite loop
            // (agent reply -> event -> inbound -> routed to the agent -> reply -> ...).
            // ChannelBridgeService drops IsSelf too; this is the belt to that pair of braces.
            if (message.Author.IsSelf)
            {
                _logger.LogDebug("[slack] dropping our own echo in {Conversation}", message.Conversation.Id);
                return SocketOutcome.Empty;
            }

            if (message.Attachments.Count > 0)
                message = message with { Attachments = await HydrateAttachmentsAsync(message.Attachments, ct) };

            return new SocketOutcome([message], Reconnect: false);
        }
    }

    /// <summary>Result of one socket turn. <see cref="Reconnect"/> means drop and reopen the socket.</summary>
    private readonly record struct SocketOutcome(IReadOnlyList<ChannelMessage> Messages, bool Reconnect)
    {
        public static SocketOutcome Empty => new([], false);
        public static SocketOutcome Reopen => new([], true);
    }

    private async Task<ClientWebSocket> EnsureConnectedAsync(CancellationToken ct)
    {
        if (_socket is { State: WebSocketState.Open })
            return _socket;

        await DropSocketAsync();

        // Identity is resolved per connection, not per frame: a failing auth.test must not turn
        // into one HTTP call per inbound message.
        await EnsureIdentityAsync(ct);

        var url = await OpenSocketUrlAsync(ct);
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(Math.Max(0, _settings.KeepAliveSeconds));
        // Without a keepalive TIMEOUT a half-open connection parks in ReceiveAsync forever and the
        // channel goes quietly deaf — the one stall the receive loop cannot otherwise observe.
        if (_settings.KeepAliveTimeoutSeconds > 0)
            socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(_settings.KeepAliveTimeoutSeconds);

        try
        {
            await socket.ConnectAsync(new Uri(url), ct);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        _socket = socket;
        _logger.LogInformation("[slack] socket mode connection opened");
        return socket;
    }

    private async Task<string> OpenSocketUrlAsync(CancellationToken ct)
    {
        // The APP-level token (xapp-…, connections:write) opens the socket; the bot token cannot.
        using var resp = await SendApiAsync(
            "apps.connections.open",
            new StringContent("", Encoding.UTF8, "application/x-www-form-urlencoded"),
            _settings.AppToken,
            ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (JsonException)
        {
            throw new SlackTransientException($"apps.connections.open HTTP {(int)resp.StatusCode}: non-JSON body");
        }

        using (doc)
        {
            if (!IsOk(doc.RootElement))
                throw new SlackTransientException($"apps.connections.open failed: {body}");
            var url = GetString(doc.RootElement, "url");
            if (string.IsNullOrEmpty(url))
                throw new SlackTransientException("apps.connections.open returned no url");
            return url;
        }
    }

    /// <summary>
    /// Resolves our own bot user id via <c>auth.test</c> when it isn't configured. Best effort:
    /// a failure is logged and retried on the next reconnect, because the <c>bot_id</c> half of the
    /// echo guard works without it — but it is worth configuring <c>Slack__BotUserId</c> so the
    /// guard is complete even when the Web API is unreachable.
    /// </summary>
    private async Task EnsureIdentityAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_botUserId))
            return;

        try
        {
            using var resp = await SendApiAsync("auth.test", EmptyForm(), _settings.BotToken, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            if (!IsOk(doc.RootElement))
            {
                _logger.LogWarning("[slack] auth.test failed: {Body}", body);
                return;
            }
            _botUserId = GetString(doc.RootElement, "user_id");
            _logger.LogInformation("[slack] bot identity resolved: {BotUserId}", _botUserId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[slack] auth.test failed; the own-user echo guard falls back to bot_id until it succeeds");
        }
    }

    private async Task AckAsync(WebSocket socket, string envelopeId, CancellationToken ct)
    {
        var ack = JsonSerializer.Serialize(new Dictionary<string, object?> { ["envelope_id"] = envelopeId });
        await socket.SendAsync(Encoding.UTF8.GetBytes(ack), WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private static async Task<(string? Text, bool Closed)> ReceiveTextAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var accumulated = new MemoryStream();
        var isText = true;

        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
                return (null, true);
            if (result.MessageType != WebSocketMessageType.Text)
                isText = false;
            accumulated.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                break;
        }

        return isText ? (Encoding.UTF8.GetString(accumulated.ToArray()), false) : (null, false);
    }

    private async Task DropSocketAsync()
    {
        var socket = _socket;
        _socket = null;
        if (socket is null)
            return;
        try
        {
            if (socket.State == WebSocketState.Open)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnecting", CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[slack] closing the socket failed (continuing)");
        }
        finally
        {
            socket.Dispose();
        }
    }

    // ---------------------------------------------------------------- normalization

    private async Task<ChannelMessage?> TryNormalizeAsync(JsonElement payload, CancellationToken ct)
    {
        if (GetString(payload, "type") is not "event_callback")
            return null;
        if (!payload.TryGetProperty("event", out var ev) || ev.ValueKind != JsonValueKind.Object)
            return null;
        if (GetString(ev, "type") is not "message")
            return null;

        // v1 takes plain messages and file shares. message_changed / message_deleted / channel_join
        // / bot_message wrap the message differently and are deliberately deferred (plan §10).
        var subtype = GetString(ev, "subtype");
        if (subtype is not (null or "file_share"))
            return null;

        var channelId = GetString(ev, "channel");
        if (string.IsNullOrEmpty(channelId))
            return null;

        // Fail-closed allowlist on top of Slack's own containment (the bot only hears conversations
        // it has been invited to) — the twin of Telegram's AllowedChatIds drop.
        if (_allowed.Count > 0 && !_allowed.Contains(channelId))
            return null;

        var ts = GetString(ev, "ts") ?? "";
        var threadTs = GetString(ev, "thread_ts");
        var userId = GetString(ev, "user");
        var botId = GetString(ev, "bot_id");

        var rawText = GetString(ev, "text");
        // Pre-resolve every mentioned id so the (synchronous) normalizer can render @names.
        foreach (var mentioned in SlackTextNormalizer.MentionedUserIds(rawText))
            await ResolveUserAsync(mentioned, ct);
        var (text, mentions) = SlackTextNormalizer.Normalize(
            rawText, id => _users.TryGetValue(id, out var u) ? u.DisplayName : null, _botUserId);

        var author = await BuildAuthorAsync(userId, botId, ct);

        return new ChannelMessage
        {
            Id = Guid.NewGuid().ToString("n"),
            Channel = ChannelKey,
            ChannelMessageId = ts,
            Conversation = new Conversation
            {
                Id = channelId,
                Kind = ConversationKindOf(GetString(ev, "channel_type"), channelId),
                Title = await ResolveConversationTitleAsync(channelId, ct),
            },
            Author = author,
            Timestamp = ParseTs(ts),
            Text = text,
            Mentions = mentions,
            Attachments = ExtractAttachments(ev),
            // In Slack the thread's parent IS the message whose ts is thread_ts.
            ReplyTo = threadTs is { Length: > 0 } && !string.Equals(threadTs, ts, StringComparison.Ordinal)
                ? new ReplyReference { ChannelMessageId = threadTs }
                : null,
            // "C123|1700000000.000100" in a thread, bare "C123" otherwise — see plan §5.
            ReplyHandle = threadTs is { Length: > 0 } ? $"{channelId}|{threadTs}" : channelId,
            Raw = payload.Clone(),
        };
    }

    private async Task<Participant> BuildAuthorAsync(string? userId, string? botId, CancellationToken ct)
    {
        var isSelf = !string.IsNullOrEmpty(botId)
            || (_botUserId is { Length: > 0 } me && string.Equals(userId, me, StringComparison.Ordinal));

        // Don't spend a users.info call resolving our own echo — it is about to be dropped.
        var user = !isSelf && !string.IsNullOrEmpty(userId) ? await ResolveUserAsync(userId, ct) : null;

        return new Participant
        {
            Id = userId ?? botId ?? "",
            DisplayName = user?.DisplayName,
            Username = user?.Username,
            IsSelf = isSelf,
        };
    }

    private static IReadOnlyList<Attachment> ExtractAttachments(JsonElement ev)
    {
        if (!ev.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<Attachment>();
        foreach (var file in files.EnumerateArray())
        {
            var id = GetString(file, "id");
            if (string.IsNullOrEmpty(id))
                continue;
            var mime = GetString(file, "mimetype");
            list.Add(new Attachment
            {
                Kind = KindOf(mime, GetString(file, "subtype")),
                Name = GetString(file, "name") ?? GetString(file, "title"),
                Mime = mime,
                Size = GetLong(file, "size"),
                ChannelRef = id,
                // Authenticated URL — useless to consumers behind the bus, which is why the bytes
                // are inlined below while we still hold the bot token. url_private serves those
                // bytes to a Bearer-authenticated GET exactly as url_private_download does; only the
                // Content-Disposition differs, so one URL covers both the contract and the fetch.
                Url = GetString(file, "url_private"),
            });
        }
        return list;
    }

    private static AttachmentKind KindOf(string? mime, string? subtype)
    {
        if (string.Equals(subtype, "slack_audio", StringComparison.Ordinal))
            return AttachmentKind.Voice;
        if (mime is null)
            return AttachmentKind.File;
        if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return AttachmentKind.Image;
        if (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return AttachmentKind.Video;
        if (mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return AttachmentKind.Audio;
        return AttachmentKind.File;
    }

    /// <summary>
    /// Downloads each attachment's bytes from <c>url_private_download</c> (Bearer bot token) and
    /// inlines them as <see cref="Attachment.Content"/>. Over-cap files, and ANY failure, pass
    /// through metadata-only — an inbound message must never be lost to a broken download.
    /// </summary>
    private async Task<IReadOnlyList<Attachment>> HydrateAttachmentsAsync(IReadOnlyList<Attachment> attachments, CancellationToken ct)
    {
        if (_settings.MaxInlineAttachmentBytes <= 0)
            return attachments;

        var hydrated = new List<Attachment>(attachments.Count);
        foreach (var attachment in attachments)
        {
            if (attachment.Size is { } declared && declared > _settings.MaxInlineAttachmentBytes)
            {
                _logger.LogInformation(
                    "[slack] attachment {Ref} is {Bytes} bytes — over the {Max} inline cap; keeping metadata only",
                    attachment.ChannelRef, declared, _settings.MaxInlineAttachmentBytes);
                hydrated.Add(attachment);
                continue;
            }

            try
            {
                hydrated.Add(await TryDownloadAsync(attachment, ct) ?? attachment);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[slack] attachment download failed for {Ref}; keeping metadata only", attachment.ChannelRef);
                hydrated.Add(attachment);
            }
        }
        return hydrated;
    }

    private async Task<Attachment?> TryDownloadAsync(Attachment attachment, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(attachment.Url))
            return null;

        using var req = new HttpRequestMessage(HttpMethod.Get, attachment.Url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.BotToken);
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("[slack] file download for {Ref} returned HTTP {Status}", attachment.ChannelRef, (int)resp.StatusCode);
            return null;
        }

        // An unauthenticated url_private answers 200 with Slack's HTML sign-in page rather than the
        // file — never inline that as if it were the attachment.
        if (resp.Content.Headers.ContentType?.MediaType is "text/html" && attachment.Mime is not "text/html")
        {
            _logger.LogWarning("[slack] file download for {Ref} returned an HTML page (auth?); keeping metadata only", attachment.ChannelRef);
            return null;
        }

        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        if (bytes.LongLength > _settings.MaxInlineAttachmentBytes)
        {
            _logger.LogInformation(
                "[slack] attachment {Ref} downloaded {Bytes} bytes — over the inline cap; keeping metadata only",
                attachment.ChannelRef, bytes.LongLength);
            return null;
        }

        return attachment with { Content = bytes, Size = attachment.Size ?? bytes.Length };
    }

    // ---------------------------------------------------------------- directory lookups

    private async Task<SlackUser?> ResolveUserAsync(string userId, CancellationToken ct)
    {
        if (_users.TryGetValue(userId, out var cached))
            return cached;

        try
        {
            using var resp = await SendApiAsync("users.info", Form(("user", userId)), _settings.BotToken, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            if (!IsOk(doc.RootElement) || !doc.RootElement.TryGetProperty("user", out var user))
            {
                _logger.LogDebug("[slack] users.info failed for {User}: {Body}", userId, body);
                return null;   // NOT cached: a transient failure must not pin an unknown name forever
            }

            var username = GetString(user, "name");
            var display = user.TryGetProperty("profile", out var profile) && profile.ValueKind == JsonValueKind.Object
                ? Coalesce(GetString(profile, "display_name"), GetString(profile, "real_name"))
                : null;

            var resolved = new SlackUser(Coalesce(display, GetString(user, "real_name"), username), username);
            _users[userId] = resolved;
            return resolved;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[slack] users.info failed for {User}", userId);
            return null;
        }
    }

    private async Task<string?> ResolveConversationTitleAsync(string channelId, CancellationToken ct)
    {
        if (_conversationTitles.TryGetValue(channelId, out var cached))
            return cached;

        try
        {
            using var resp = await SendApiAsync("conversations.info", Form(("channel", channelId)), _settings.BotToken, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            if (!IsOk(doc.RootElement) || !doc.RootElement.TryGetProperty("channel", out var channel))
            {
                _logger.LogDebug("[slack] conversations.info failed for {Channel}: {Body}", channelId, body);
                return null;
            }

            // A DM has no name; the prompt envelope renders "direct message" for those anyway.
            var title = GetString(channel, "name");
            _conversationTitles[channelId] = title;
            return title;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[slack] conversations.info failed for {Channel}", channelId);
            return null;
        }
    }

    // ---------------------------------------------------------------- outbound

    public async Task<SendResult> SendAsync(ChannelReply reply, CancellationToken cancellationToken)
    {
        var (channelId, threadTs) = ResolveTarget(reply);
        if (string.IsNullOrEmpty(channelId))
            return SendResult.Failed("Reply has no ConversationId or ReplyHandle.");

        // Text first (its own message), then each attachment. A reply may be either or both;
        // failing the text fails the whole send (the attachments would lack context).
        SendResult? last = null;
        if (reply.Text is not null)
        {
            last = await SendTextAsync(reply, channelId, threadTs, cancellationToken);
            if (!last.Ok)
                return last;
        }

        foreach (var attachment in reply.Attachments)
        {
            last = await SendAttachmentAsync(attachment, channelId, threadTs, cancellationToken);
            if (!last.Ok)
                return last;
        }

        return last ?? SendResult.Failed("Reply has neither text nor attachments.");
    }

    /// <summary>
    /// Resolves the reply's target channel and thread.
    ///
    /// <see cref="ChannelReply.ReplyHandle"/> is preferred over <see cref="ChannelReply.ConversationId"/>
    /// here — the INVERSE of the Telegram adapter's precedence — because the dispatcher sets BOTH and
    /// only the handle carries the thread ("C123|1700000000.000100"). Taking ConversationId first
    /// would silently drop every reply out of its thread and into the channel root.
    /// </summary>
    private static (string ChannelId, string? ThreadTs) ResolveTarget(ChannelReply reply)
    {
        var raw = !string.IsNullOrEmpty(reply.ReplyHandle) ? reply.ReplyHandle : reply.ConversationId;
        if (string.IsNullOrEmpty(raw))
            return ("", null);

        var channelId = raw;
        string? threadTs = null;
        var pipe = raw.IndexOf('|');
        if (pipe > 0)
        {
            channelId = raw[..pipe];
            threadTs = raw[(pipe + 1)..];
        }

        // In Slack, "replying to a message" IS threading onto it — message ts values are thread keys.
        if (!string.IsNullOrEmpty(reply.ReplyToMessageId))
            threadTs = reply.ReplyToMessageId;

        return (channelId, string.IsNullOrEmpty(threadTs) ? null : threadTs);
    }

    private async Task<SendResult> SendTextAsync(ChannelReply reply, string channelId, string? threadTs, CancellationToken ct)
    {
        var payloadJson = BuildPostMessagePayload(reply, channelId, threadTs);
        var maxAttempts = Math.Max(0, _settings.SendRetryAttempts) + 1;

        // The outbound consumer auto-commits, so a transient blip (429/5xx/network) would silently
        // drop the reply. Retry those a bounded number of times; fail fast on a permanent error.
        for (var attempt = 1; ; attempt++)
        {
            var (result, retryDelay) = await TryPostJsonAsync(
                "chat.postMessage", payloadJson, ReadPostedTs, lastAttempt: attempt >= maxAttempts, ct);
            if (result is not null)
                return result;

            _logger.LogWarning("[slack] chat.postMessage transient failure; retry {Attempt}/{Max} after {Delay}s",
                attempt, maxAttempts - 1, retryDelay.GetValueOrDefault().TotalSeconds);
            await Task.Delay(retryDelay.GetValueOrDefault(ErrorBackoff), ct);
        }
    }

    private string BuildPostMessagePayload(ChannelReply reply, string channelId, string? threadTs)
    {
        var payload = new Dictionary<string, object?> { ["channel"] = channelId };

        if (reply.Text is not null)
        {
            // Render the reply kind visibly: interim progress notes and blocking questions read
            // differently from a final answer. The markers are plain emoji, safe in mrkdwn.
            var text = reply.Kind switch
            {
                ChannelReplyKind.Progress => $"⏳ {reply.Text}",
                ChannelReplyKind.Question => $"❓ {reply.Text}",
                _ => reply.Text,
            };
            payload["text"] = ShouldFormat(reply) ? SlackMrkdwnRenderer.ToMrkdwn(text) : text;
        }

        if (threadTs is not null)
            payload["thread_ts"] = threadTs;

        // Merge raw channel passthrough LAST (blocks, unfurl_links, a thread_ts override, ...).
        if (reply.RawOverrides is { ValueKind: JsonValueKind.Object } overrides)
            foreach (var prop in overrides.EnumerateObject())
                payload[prop.Name] = prop.Value;

        return JsonSerializer.Serialize(payload);
    }

    private bool ShouldFormat(ChannelReply reply)
    {
        if (reply.Text is null || !string.Equals(_settings.Formatting, "Markdown", StringComparison.OrdinalIgnoreCase))
            return false;

        // RawOverrides own the formatting when they replace the text outright.
        return reply.RawOverrides is not { ValueKind: JsonValueKind.Object } overrides
            || !overrides.TryGetProperty("text", out _);
    }

    /// <summary>
    /// One attachment through Slack's external-upload flow: <c>files.getUploadURLExternal</c> →
    /// PUT/POST the bytes to that URL → <c>files.completeUploadExternal</c> with the channel (and
    /// thread). <c>files.upload</c> is deprecated and closed to new apps, so this is the only path.
    /// </summary>
    private async Task<SendResult> SendAttachmentAsync(OutboundAttachment attachment, string channelId, string? threadTs, CancellationToken ct)
    {
        if (attachment.Content is null && string.IsNullOrEmpty(attachment.Source))
            return SendResult.Failed("Attachment has neither Content nor Source.");

        // Slack has no "fetch this URL for me" send (Telegram's sendDocument does). Rather than have
        // the gateway fetch an arbitrary producer-supplied URL, a Source-only attachment is posted as
        // a link so the reply is never silently lost.
        if (attachment.Content is null)
            return await SendSourceAsLinkAsync(attachment, channelId, threadTs, ct);

        var maxAttempts = Math.Max(0, _settings.SendRetryAttempts) + 1;
        for (var attempt = 1; ; attempt++)
        {
            var (result, retryDelay) = await TryUploadOnceAsync(
                attachment, channelId, threadTs, lastAttempt: attempt >= maxAttempts, ct);
            if (result is not null)
                return result;

            _logger.LogWarning("[slack] file upload transient failure; retry {Attempt}/{Max} after {Delay}s",
                attempt, maxAttempts - 1, retryDelay.GetValueOrDefault().TotalSeconds);
            await Task.Delay(retryDelay.GetValueOrDefault(ErrorBackoff), ct);
        }
    }

    private async Task<(SendResult? Result, TimeSpan? RetryDelay)> TryUploadOnceAsync(
        OutboundAttachment attachment, string channelId, string? threadTs, bool lastAttempt, CancellationToken ct)
    {
        var bytes = attachment.Content!;
        var name = string.IsNullOrEmpty(attachment.Name) ? "file" : attachment.Name;

        try
        {
            // 1. Reserve an upload URL.
            using var reserveResp = await SendApiAsync(
                "files.getUploadURLExternal",
                Form(("filename", name), ("length", bytes.Length.ToString(CultureInfo.InvariantCulture))),
                _settings.BotToken,
                ct);
            var reserveBody = await reserveResp.Content.ReadAsStringAsync(ct);

            string uploadUrl, fileId;
            using (var doc = ParseOrNull(reserveBody))
            {
                if (doc is null)
                    return Retryable(lastAttempt, $"Slack files.getUploadURLExternal HTTP {(int)reserveResp.StatusCode}: non-JSON body", reserveResp);
                if (!IsOk(doc.RootElement))
                    return Classify(doc.RootElement, reserveResp, lastAttempt, $"Slack files.getUploadURLExternal failed: {reserveBody}");

                uploadUrl = GetString(doc.RootElement, "upload_url") ?? "";
                fileId = GetString(doc.RootElement, "file_id") ?? "";
            }
            if (uploadUrl.Length == 0 || fileId.Length == 0)
                return (SendResult.Failed($"Slack files.getUploadURLExternal returned no upload_url/file_id: {reserveBody}"), null);

            // 2. POST the bytes to the one-shot URL (multipart, field name "file").
            using var form = new MultipartFormDataContent();
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue(attachment.Mime ?? "application/octet-stream");
            form.Add(part, "file", name);
            using var uploadResp = await _http.PostAsync(uploadUrl, form, ct);
            if (!uploadResp.IsSuccessStatusCode)
                return Retryable(lastAttempt, $"Slack file upload HTTP {(int)uploadResp.StatusCode}", uploadResp);

            // 3. Complete, which is also what shares the file into the channel/thread.
            var complete = new Dictionary<string, object?>
            {
                ["files"] = new[] { new Dictionary<string, object?> { ["id"] = fileId, ["title"] = name } },
                ["channel_id"] = channelId,
            };
            if (threadTs is not null)
                complete["thread_ts"] = threadTs;
            if (!string.IsNullOrEmpty(attachment.Caption))
                complete["initial_comment"] = attachment.Caption;

            var (result, retry) = await TryPostJsonAsync(
                "files.completeUploadExternal", JsonSerializer.Serialize(complete), _ => fileId, lastAttempt, ct);
            return (result, retry);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Includes HttpClient TIMEOUT TaskCanceledExceptions (an OCE without our token
            // cancelled) — a transient transport failure to retry, not a shutdown.
            return lastAttempt ? (SendResult.Failed(ex.Message), null) : (null, ErrorBackoff);
        }
    }

    private Task<SendResult> SendSourceAsLinkAsync(OutboundAttachment attachment, string channelId, string? threadTs, CancellationToken ct)
    {
        _logger.LogInformation(
            "[slack] attachment {Name} has no inline bytes; posting its Source as a link (Slack cannot fetch a URL on send)",
            attachment.Name ?? attachment.Source);

        var caption = string.IsNullOrEmpty(attachment.Caption) ? null : attachment.Caption;
        var label = attachment.Name ?? attachment.Source!;
        var text = caption is null ? $"<{attachment.Source}|{label}>" : $"{SlackMrkdwnRenderer.Escape(caption)}\n<{attachment.Source}|{label}>";

        var payload = new Dictionary<string, object?> { ["channel"] = channelId, ["text"] = text };
        if (threadTs is not null)
            payload["thread_ts"] = threadTs;

        return SendPostMessageWithRetryAsync(JsonSerializer.Serialize(payload), ct);
    }

    private async Task<SendResult> SendPostMessageWithRetryAsync(string payloadJson, CancellationToken ct)
    {
        var maxAttempts = Math.Max(0, _settings.SendRetryAttempts) + 1;
        for (var attempt = 1; ; attempt++)
        {
            var (result, retryDelay) = await TryPostJsonAsync(
                "chat.postMessage", payloadJson, ReadPostedTs, lastAttempt: attempt >= maxAttempts, ct);
            if (result is not null)
                return result;
            await Task.Delay(retryDelay.GetValueOrDefault(ErrorBackoff), ct);
        }
    }

    /// <summary>One Web API POST. Returns a terminal <see cref="SendResult"/>, or (null, delay) when the
    /// failure is transient and the caller should retry after <c>delay</c>.</summary>
    private async Task<(SendResult? Result, TimeSpan? RetryDelay)> TryPostJsonAsync(
        string method, string payloadJson, Func<JsonElement, string?> readId, bool lastAttempt, CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
            using var resp = await SendApiAsync(method, content, _settings.BotToken, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            using var doc = ParseOrNull(body);
            if (doc is null)   // non-JSON (e.g. a proxy 5xx page) — transient
                return Retryable(lastAttempt, $"Slack {method} HTTP {(int)resp.StatusCode}: non-JSON body", resp);

            if (IsOk(doc.RootElement))
                return (SendResult.Sent(readId(doc.RootElement)), null);

            return Classify(doc.RootElement, resp, lastAttempt, $"Slack {method} failed: {body}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Includes HttpClient TIMEOUT TaskCanceledExceptions — transient, not shutdown.
            return lastAttempt ? (SendResult.Failed(ex.Message), null) : (null, ErrorBackoff);
        }
    }

    private (SendResult? Result, TimeSpan? RetryDelay) Classify(
        JsonElement root, HttpResponseMessage resp, bool lastAttempt, string failureMessage)
    {
        var error = GetString(root, "error");
        var retryAfter = ReadRetryAfter(resp);
        if (lastAttempt || !IsTransient(error, (int)resp.StatusCode, retryAfter))
            return (SendResult.Failed(failureMessage), null);
        return (null, retryAfter is { } ra ? CapRetryAfter(ra) : ErrorBackoff);
    }

    private (SendResult? Result, TimeSpan? RetryDelay) Retryable(bool lastAttempt, string failureMessage, HttpResponseMessage resp)
    {
        if (lastAttempt)
            return (SendResult.Failed(failureMessage), null);
        var retryAfter = ReadRetryAfter(resp);
        return (null, retryAfter is { } ra ? CapRetryAfter(ra) : ErrorBackoff);
    }

    // Slack signals rate limiting with HTTP 429 + a Retry-After header and error "ratelimited";
    // the rest are its documented transient server-side failures. Everything else (channel_not_found,
    // not_in_channel, invalid_auth, msg_too_long, ...) is permanent — retrying just burns the budget.
    private static readonly HashSet<string> TransientErrors = new(StringComparer.Ordinal)
    {
        "ratelimited", "rate_limited", "service_unavailable", "internal_error", "fatal_error", "request_timeout",
    };

    private static bool IsTransient(string? error, int statusCode, int? retryAfter) =>
        retryAfter is not null
        || statusCode == 429
        || statusCode is >= 500 and <= 599
        || (error is not null && TransientErrors.Contains(error));

    private static int? ReadRetryAfter(HttpResponseMessage resp)
    {
        if (resp.Headers.RetryAfter?.Delta is { } delta)
            return (int)Math.Ceiling(delta.TotalSeconds);
        if (resp.Headers.TryGetValues("Retry-After", out var values)
            && int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            return seconds;
        return null;
    }

    private static string? ReadPostedTs(JsonElement root) => GetString(root, "ts");

    // ---------------------------------------------------------------- plumbing

    private Task<HttpResponseMessage> SendApiAsync(string method, HttpContent content, string token, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_settings.ApiBaseUrl}/{method}") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return SendAndDisposeRequestAsync(request, ct);
    }

    private async Task<HttpResponseMessage> SendAndDisposeRequestAsync(HttpRequestMessage request, CancellationToken ct)
    {
        // The caller owns the response; the request (and the content it borrowed) is ours.
        using (request)
            return await _http.SendAsync(request, ct);
    }

    private static FormUrlEncodedContent EmptyForm() => new(Array.Empty<KeyValuePair<string, string>>());

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) =>
        new(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));

    private static JsonDocument? ParseOrNull(string json)
    {
        try { return JsonDocument.Parse(json); }
        catch (JsonException) { return null; }
    }

    private static bool IsOk(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;

    private static ConversationKind ConversationKindOf(string? channelType, string channelId) => channelType switch
    {
        "im" => ConversationKind.Direct,
        "mpim" or "group" => ConversationKind.Group,
        "channel" => ConversationKind.Channel,
        // channel_type is absent on some event shapes; the id prefix is Slack's own convention.
        _ => channelId.StartsWith('D') ? ConversationKind.Direct
            : channelId.StartsWith('G') ? ConversationKind.Group
            : ConversationKind.Channel,
    };

    /// <summary>Slack timestamps are "seconds.microseconds" strings, and are also message ids.</summary>
    private static DateTimeOffset ParseTs(string? ts) =>
        ts is { Length: > 0 } && double.TryParse(ts, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(seconds * 1000))
            : DateTimeOffset.UtcNow;

    private static string? Coalesce(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

    private static string? GetString(JsonElement obj, string prop) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static long? GetLong(JsonElement obj, string prop) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(prop, out var v)
        && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l)
            ? l
            : null;

    private static async Task<bool> DelayQuietAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync() => await DropSocketAsync();

    private sealed record SlackUser(string? DisplayName, string? Username);
}

/// <summary>
/// A failure that must be retried rather than surfaced with a stack trace: a non-ok
/// <c>apps.connections.open</c>, a malformed handshake response. Logged as a one-line warning,
/// then backed off exactly like a socket death.
/// </summary>
public sealed class SlackTransientException(string message) : Exception(message);
