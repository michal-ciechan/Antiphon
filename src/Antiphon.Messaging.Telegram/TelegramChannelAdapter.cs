using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Antiphon.Messaging.Telegram;

/// <summary>
/// <see cref="IChannelAdapter"/> for Telegram, talking the raw Bot API over <see cref="HttpClient"/>
/// (no third-party client). Inbound updates are long-polled via <c>getUpdates</c> and normalized to
/// <see cref="ChannelMessage"/> with the complete native <c>Update</c> preserved in <see cref="ChannelMessage.Raw"/>.
/// Outbound <see cref="ChannelReply"/> is denormalized to <c>sendMessage</c>, merging any raw overrides.
/// </summary>
public sealed class TelegramChannelAdapter : IChannelAdapter
{
    private const string ChannelKey = "telegram";

    private readonly HttpClient _http;
    private readonly TelegramSettings _settings;
    private readonly ILogger<TelegramChannelAdapter> _logger;
    private readonly HashSet<long> _allowed;

    public TelegramChannelAdapter(HttpClient http, TelegramSettings settings, ILogger<TelegramChannelAdapter> logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
        _allowed = [.. settings.AllowedChatIds];
    }

    public string Channel => ChannelKey;

    public ChannelCapabilities Capabilities => new()
    {
        Channel = ChannelKey,
        Mentions = true,
        Attachments = true,
        Edit = true,
        Delete = true,
        Reactions = true,
        Threads = false,
        TypingIndicator = true,
        // Producers send standard Markdown; this adapter renders it to Telegram HTML itself
        // (TelegramMarkdownRenderer) — see docs/telegram.md.
        MarkdownFlavor = "Markdown",
        MaxTextLength = 4096,
        AttachmentKinds =
        [
            AttachmentKind.Image, AttachmentKind.Video, AttachmentKind.Audio,
            AttachmentKind.Voice, AttachmentKind.File, AttachmentKind.Sticker,
            AttachmentKind.Location, AttachmentKind.Contact,
        ],
    };

    private string Url(string method) => $"{_settings.ApiBaseUrl}/bot{_settings.BotToken}/{method}";

    private TimeSpan ErrorBackoff => TimeSpan.FromSeconds(Math.Max(0, _settings.ErrorBackoffSeconds));
    private TimeSpan CapRetryAfter(int seconds) => TimeSpan.FromSeconds(Math.Clamp(seconds, 0, Math.Max(0, _settings.MaxRetryAfterSeconds)));

    public async IAsyncEnumerable<ChannelMessage> ReceiveAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // getUpdates returns 409 if a webhook is set — clear it first (idempotent).
        await TryDeleteWebhookAsync(cancellationToken);

        long offset = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            PollOutcome outcome;
            try
            {
                outcome = await FetchBatchAsync(offset, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
            catch (Exception ex)
            {
                // Network/transport failure — back off so we don't hammer Telegram or spin the CPU.
                // This MUST include OperationCanceledExceptions whose cause is not our token:
                // HttpClient throws TaskCanceledException (an OCE) on TIMEOUT, and treating that as
                // shutdown silently ended the ingress stream — the gateway stopped receiving
                // Telegram messages forever with zero log output (live miss 2026-07-31, AZ Care).
                _logger.LogWarning(ex, "[telegram] getUpdates failed; backing off {Backoff}s", ErrorBackoff.TotalSeconds);
                if (!await DelayQuietAsync(ErrorBackoff, cancellationToken))
                    yield break;
                continue;
            }

            offset = outcome.NextOffset;
            foreach (var message in outcome.Messages)
                yield return message;

            // ok:false (409/401/429/...) returns a backoff so the loop paces itself instead of tight-looping.
            if (outcome.Backoff is { } delay && delay > TimeSpan.Zero && !await DelayQuietAsync(delay, cancellationToken))
                yield break;
        }
    }

    private async Task<PollOutcome> FetchBatchAsync(long offset, CancellationToken ct)
    {
        var url = $"{Url("getUpdates")}?timeout={_settings.LongPollTimeoutSeconds.ToString(CultureInfo.InvariantCulture)}&offset={offset.ToString(CultureInfo.InvariantCulture)}";
        using var resp = await _http.GetAsync(url, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException)
        {
            // e.g. a proxy 5xx HTML page — treat as transient and back off.
            _logger.LogWarning("[telegram] getUpdates HTTP {Status} with non-JSON body; backing off {Backoff}s", (int)resp.StatusCode, ErrorBackoff.TotalSeconds);
            return new PollOutcome([], offset, ErrorBackoff);
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
            {
                var (code, desc, retryAfter) = ReadError(root);
                var backoff = retryAfter is { } ra ? CapRetryAfter(ra) : ErrorBackoff;
                if (code == 409)
                    _logger.LogWarning("[telegram] getUpdates 409 Conflict — another instance is polling this bot token; backing off {Backoff}s. {Desc}", backoff.TotalSeconds, desc);
                else
                    _logger.LogWarning("[telegram] getUpdates not ok (code {Code}); backing off {Backoff}s. {Desc}", code, backoff.TotalSeconds, desc);
                return new PollOutcome([], offset, backoff);
            }

            if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
                return new PollOutcome([], offset, null);

            var messages = new List<ChannelMessage>();
            var next = offset;
            foreach (var update in result.EnumerateArray())
            {
                if (update.TryGetProperty("update_id", out var uid) && uid.TryGetInt64(out var id))
                    next = id + 1;

                var message = TryNormalize(update);
                if (message is not null)
                    messages.Add(message);
            }

            // Inline attachment bytes while we still hold channel credentials — consumers behind
            // the bus can't call getFile. Best-effort: a failed download keeps the metadata.
            for (var i = 0; i < messages.Count; i++)
                if (messages[i].Attachments.Count > 0)
                    messages[i] = messages[i] with { Attachments = await HydrateAttachmentsAsync(messages[i].Attachments, ct) };

            return new PollOutcome(messages, next, null);
        }
    }

    /// <summary>
    /// Downloads each attachment's bytes via <c>getFile</c> + the file endpoint and inlines them as
    /// <see cref="Attachment.Content"/>. Files over <see cref="TelegramSettings.MaxInlineAttachmentBytes"/>
    /// (or any download failure) pass through metadata-only — an inbound message must never be lost
    /// to a broken download.
    /// </summary>
    private async Task<IReadOnlyList<Attachment>> HydrateAttachmentsAsync(
        IReadOnlyList<Attachment> attachments, CancellationToken ct)
    {
        if (_settings.MaxInlineAttachmentBytes <= 0)
            return attachments;

        var hydrated = new List<Attachment>(attachments.Count);
        foreach (var attachment in attachments)
        {
            if (attachment.Size is { } declared && declared > _settings.MaxInlineAttachmentBytes)
            {
                _logger.LogInformation(
                    "[telegram] attachment {Ref} is {Bytes} bytes — over the {Max} inline cap; keeping metadata only",
                    attachment.ChannelRef, declared, _settings.MaxInlineAttachmentBytes);
                hydrated.Add(attachment);
                continue;
            }

            try
            {
                var downloaded = await TryDownloadAsync(attachment, ct);
                hydrated.Add(downloaded ?? attachment);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[telegram] attachment download failed for {Ref}; keeping metadata only", attachment.ChannelRef);
                hydrated.Add(attachment);
            }
        }
        return hydrated;
    }

    private async Task<Attachment?> TryDownloadAsync(Attachment attachment, CancellationToken ct)
    {
        // getFile resolves the file_id to a short-lived file_path on Telegram's file endpoint.
        using var resp = await _http.GetAsync($"{Url("getFile")}?file_id={Uri.EscapeDataString(attachment.ChannelRef)}", ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!(root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            || !root.TryGetProperty("result", out var result))
        {
            _logger.LogWarning("[telegram] getFile failed for {Ref}: {Body}", attachment.ChannelRef, json);
            return null;
        }

        var filePath = GetString(result, "file_path");
        if (string.IsNullOrEmpty(filePath))
            return null;
        if (GetLong(result, "file_size") is { } actual && actual > _settings.MaxInlineAttachmentBytes)
        {
            _logger.LogInformation(
                "[telegram] attachment {Ref} resolved to {Bytes} bytes — over the inline cap; keeping metadata only",
                attachment.ChannelRef, actual);
            return null;
        }

        var bytes = await _http.GetByteArrayAsync($"{_settings.ApiBaseUrl}/file/bot{_settings.BotToken}/{filePath}", ct);
        if (bytes.Length > _settings.MaxInlineAttachmentBytes)
            return null;

        return attachment with
        {
            Content = bytes,
            Size = attachment.Size ?? bytes.Length,
            Name = attachment.Name ?? Path.GetFileName(filePath),
        };
    }

    /// <summary>Result of one getUpdates poll. <see cref="Backoff"/> is non-null when Telegram returned a
    /// retryable error (so the loop should pace itself); null means continue immediately.</summary>
    private readonly record struct PollOutcome(IReadOnlyList<ChannelMessage> Messages, long NextOffset, TimeSpan? Backoff);

    private static (int? Code, string? Desc, int? RetryAfter) ReadError(JsonElement root)
    {
        var retryAfter = root.TryGetProperty("parameters", out var p) && p.ValueKind == JsonValueKind.Object
            ? GetInt(p, "retry_after")
            : null;
        return (GetInt(root, "error_code"), GetString(root, "description"), retryAfter);
    }

    private static bool IsTransient(int? code, int? retryAfter)
        => retryAfter is not null || code == 429 || code is >= 500 and <= 599;

    private ChannelMessage? TryNormalize(JsonElement update)
    {
        JsonElement m = default;
        var found = false;
        foreach (var field in new[] { "message", "edited_message", "channel_post", "edited_channel_post" })
        {
            if (update.TryGetProperty(field, out m))
            {
                found = true;
                break;
            }
        }
        if (!found || m.ValueKind != JsonValueKind.Object)
            return null;

        if (!m.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("id", out var chatIdEl) || !chatIdEl.TryGetInt64(out var chatId))
            return null;

        if (_allowed.Count > 0 && !_allowed.Contains(chatId))
            return null;

        var conversation = new Conversation
        {
            Id = chatId.ToString(CultureInfo.InvariantCulture),
            Kind = ChatKind(chat),
            Title = GetString(chat, "title") ?? GetString(chat, "first_name"),
        };

        var text = GetString(m, "text") ?? GetString(m, "caption");

        return new ChannelMessage
        {
            Id = Guid.NewGuid().ToString("n"),
            Channel = ChannelKey,
            ChannelMessageId = GetLong(m, "message_id")?.ToString(CultureInfo.InvariantCulture) ?? "",
            Conversation = conversation,
            Author = BuildAuthor(m),
            Timestamp = GetLong(m, "date") is { } unix ? DateTimeOffset.FromUnixTimeSeconds(unix) : DateTimeOffset.UtcNow,
            Text = text,
            Mentions = ExtractMentions(m, text),
            Attachments = ExtractAttachments(m),
            ReplyTo = ExtractReplyTo(m),
            ReplyHandle = conversation.Id,
            Raw = update.Clone(),
        };
    }

    private Participant BuildAuthor(JsonElement m)
    {
        if (!m.TryGetProperty("from", out var from) || from.ValueKind != JsonValueKind.Object)
            return new Participant { Id = "" };

        var name = string.Join(' ', new[] { GetString(from, "first_name"), GetString(from, "last_name") }
            .Where(s => !string.IsNullOrEmpty(s)));
        var username = GetString(from, "username");

        return new Participant
        {
            Id = GetLong(from, "id")?.ToString(CultureInfo.InvariantCulture) ?? "",
            DisplayName = string.IsNullOrEmpty(name) ? username : name,
            Username = username,
            IsSelf = _settings.BotUsername is { Length: > 0 } self && string.Equals(username, self, StringComparison.OrdinalIgnoreCase),
        };
    }

    private IReadOnlyList<Mention> ExtractMentions(JsonElement m, string? text)
    {
        if (!m.TryGetProperty("entities", out var entities) || entities.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<Mention>();
        foreach (var e in entities.EnumerateArray())
        {
            switch (GetString(e, "type"))
            {
                case "mention" when text is not null
                    && GetInt(e, "offset") is { } off && GetInt(e, "length") is { } len
                    && off >= 0 && len > 0 && off + len <= text.Length:
                {
                    var handle = text.Substring(off, len);   // includes leading '@'
                    var uname = handle.TrimStart('@');
                    list.Add(new Mention
                    {
                        Id = uname,
                        DisplayName = handle,
                        IsMe = _settings.BotUsername is { Length: > 0 } self && string.Equals(uname, self, StringComparison.OrdinalIgnoreCase),
                    });
                    break;
                }

                case "text_mention" when e.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object:
                    list.Add(new Mention
                    {
                        Id = GetLong(user, "id")?.ToString(CultureInfo.InvariantCulture) ?? "",
                        DisplayName = GetString(user, "first_name"),
                    });
                    break;
            }
        }
        return list;
    }

    private static IReadOnlyList<Attachment> ExtractAttachments(JsonElement m)
    {
        var list = new List<Attachment>();

        if (m.TryGetProperty("photo", out var photos) && photos.ValueKind == JsonValueKind.Array && photos.GetArrayLength() > 0)
        {
            var largest = photos.EnumerateArray().Last();   // last entry = highest resolution
            list.Add(new Attachment
            {
                Kind = AttachmentKind.Image,
                ChannelRef = GetString(largest, "file_id") ?? "",
                Size = GetLong(largest, "file_size"),
            });
        }

        AddFile(m, "document", AttachmentKind.File, list);
        AddFile(m, "video", AttachmentKind.Video, list);
        AddFile(m, "audio", AttachmentKind.Audio, list);
        AddFile(m, "voice", AttachmentKind.Voice, list);
        AddFile(m, "sticker", AttachmentKind.Sticker, list);
        return list;
    }

    private static void AddFile(JsonElement m, string prop, AttachmentKind kind, List<Attachment> list)
    {
        if (!m.TryGetProperty(prop, out var f) || f.ValueKind != JsonValueKind.Object)
            return;
        var fileId = GetString(f, "file_id");
        if (fileId is null)
            return;
        list.Add(new Attachment
        {
            Kind = kind,
            ChannelRef = fileId,
            Name = GetString(f, "file_name"),
            Mime = GetString(f, "mime_type"),
            Size = GetLong(f, "file_size"),
        });
    }

    private static ReplyReference? ExtractReplyTo(JsonElement m)
    {
        if (!m.TryGetProperty("reply_to_message", out var r) || r.ValueKind != JsonValueKind.Object)
            return null;
        if (GetLong(r, "message_id") is not { } mid)
            return null;
        var excerpt = GetString(r, "text") ?? GetString(r, "caption");
        if (excerpt is { Length: > 160 })
            excerpt = excerpt[..160];
        return new ReplyReference { ChannelMessageId = mid.ToString(CultureInfo.InvariantCulture), Excerpt = excerpt };
    }

    public async Task<SendResult> SendAsync(ChannelReply reply, CancellationToken cancellationToken)
    {
        var target = reply.ConversationId ?? reply.ReplyHandle;
        if (string.IsNullOrEmpty(target))
            return SendResult.Failed("Reply has no ConversationId or ReplyHandle.");

        // Text first (its own bubble), then each attachment as a document. A reply may be either
        // or both; failing the text fails the whole send (the attachments would lack context).
        SendResult? last = null;
        if (reply.Text is not null)
        {
            last = await SendTextAsync(reply, target, cancellationToken);
            if (!last.Ok)
                return last;
        }

        foreach (var attachment in reply.Attachments)
        {
            last = await SendDocumentAsync(attachment, target, cancellationToken);
            if (!last.Ok)
                return last;
        }

        return last ?? SendResult.Failed("Reply has neither text nor attachments.");
    }

    private async Task<SendResult> SendTextAsync(ChannelReply reply, string target, CancellationToken cancellationToken)
    {
        var formatted = ShouldFormat(reply);
        var payloadJson = BuildSendPayload(reply, target, formatted);
        var maxAttempts = Math.Max(0, _settings.SendRetryAttempts) + 1;

        // The outbound consumer auto-commits, so a transient blip (429/5xx/network) would silently drop
        // the reply. Retry those a bounded number of times, honoring retry_after; fail fast on 4xx.
        for (var attempt = 1; ; attempt++)
        {
            var (result, retryDelay) = await TrySendOnceAsync(payloadJson, lastAttempt: attempt >= maxAttempts, cancellationToken);
            if (result is not null)
            {
                // Formatting must never cost a delivery: if Telegram rejects the rendered HTML
                // entities, resend the original text plain (with a fresh retry budget).
                if (!result.Ok && formatted && IsEntityParseError(result.Error))
                {
                    _logger.LogWarning(
                        "[telegram] rendered HTML rejected; resending as plain text. {Error}", result.Error);
                    formatted = false;
                    payloadJson = BuildSendPayload(reply, target, htmlFormatting: false);
                    attempt = 0;
                    continue;
                }
                return result;
            }

            _logger.LogWarning("[telegram] sendMessage transient failure; retry {Attempt}/{Max} after {Delay}s",
                attempt, maxAttempts - 1, retryDelay.GetValueOrDefault().TotalSeconds);
            await Task.Delay(retryDelay.GetValueOrDefault(ErrorBackoff), cancellationToken);
        }
    }

    /// <summary>
    /// One attachment → one <c>sendDocument</c>. Inline bytes (<see cref="OutboundAttachment.Content"/>,
    /// how files arrive over Kafka) go as multipart; a bare <see cref="OutboundAttachment.Source"/>
    /// (URL or Telegram file_id) goes as the JSON string form Telegram fetches itself.
    /// </summary>
    private async Task<SendResult> SendDocumentAsync(OutboundAttachment attachment, string target, CancellationToken cancellationToken)
    {
        if (attachment.Content is null && string.IsNullOrEmpty(attachment.Source))
            return SendResult.Failed("Attachment has neither Content nor Source.");

        var maxAttempts = Math.Max(0, _settings.SendRetryAttempts) + 1;
        for (var attempt = 1; ; attempt++)
        {
            var (result, retryDelay) = await TrySendDocumentOnceAsync(
                attachment, target, lastAttempt: attempt >= maxAttempts, cancellationToken);
            if (result is not null)
                return result;

            _logger.LogWarning("[telegram] sendDocument transient failure; retry {Attempt}/{Max} after {Delay}s",
                attempt, maxAttempts - 1, retryDelay.GetValueOrDefault().TotalSeconds);
            await Task.Delay(retryDelay.GetValueOrDefault(ErrorBackoff), cancellationToken);
        }
    }

    private async Task<(SendResult? Result, TimeSpan? RetryDelay)> TrySendDocumentOnceAsync(
        OutboundAttachment attachment, string target, bool lastAttempt, CancellationToken ct)
    {
        try
        {
            // HttpContent is single-use — build it fresh per attempt.
            using var content = BuildDocumentContent(attachment, target);
            using var resp = await _http.PostAsync(Url("sendDocument"), content, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            JsonDocument doc;
            try { doc = JsonDocument.Parse(body); }
            catch (JsonException)
            {
                return lastAttempt
                    ? (SendResult.Failed($"Telegram sendDocument HTTP {(int)resp.StatusCode}: non-JSON body"), null)
                    : (null, ErrorBackoff);
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
                {
                    var sentId = root.TryGetProperty("result", out var result) && GetLong(result, "message_id") is { } sid
                        ? sid.ToString(CultureInfo.InvariantCulture)
                        : null;
                    return (SendResult.Sent(sentId), null);
                }

                var (code, _, retryAfter) = ReadError(root);
                if (lastAttempt || !IsTransient(code, retryAfter))
                    return (SendResult.Failed($"Telegram sendDocument failed: {body}"), null);

                return (null, retryAfter is { } ra ? CapRetryAfter(ra) : ErrorBackoff);
            }
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

    private static HttpContent BuildDocumentContent(OutboundAttachment attachment, string target)
    {
        if (attachment.Content is { } bytes)
        {
            var form = new MultipartFormDataContent
            {
                { new StringContent(target), "chat_id" },
            };
            if (!string.IsNullOrEmpty(attachment.Caption))
                form.Add(new StringContent(attachment.Caption), "caption");
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                attachment.Mime ?? "application/octet-stream");
            form.Add(file, "document", attachment.Name ?? "file");
            return form;
        }

        var payload = new Dictionary<string, object?>
        {
            ["chat_id"] = long.TryParse(target, out var chatId) ? chatId : target,
            ["document"] = attachment.Source,
        };
        if (!string.IsNullOrEmpty(attachment.Caption))
            payload["caption"] = attachment.Caption;
        return new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
    }

    private bool ShouldFormat(ChannelReply reply)
    {
        if (reply.Text is null || !string.Equals(_settings.Formatting, "Markdown", StringComparison.OrdinalIgnoreCase))
            return false;

        // RawOverrides own the formatting when they set parse_mode or replace the text outright.
        if (reply.RawOverrides is { ValueKind: JsonValueKind.Object } overrides
            && (overrides.TryGetProperty("parse_mode", out _) || overrides.TryGetProperty("text", out _)))
            return false;

        return true;
    }

    // Telegram's description is "Bad Request: can't parse entities: ...". Match without the
    // apostrophe: the raw body we surface may carry it JSON-escaped (') depending on the
    // serializer, and "parse entities" alone uniquely identifies the error family.
    private static bool IsEntityParseError(string? error) =>
        error?.Contains("parse entities", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>One sendMessage attempt. Returns a terminal <see cref="SendResult"/>, or (null, delay) when the
    /// failure is transient and the caller should retry after <c>delay</c>.</summary>
    private async Task<(SendResult? Result, TimeSpan? RetryDelay)> TrySendOnceAsync(string payloadJson, bool lastAttempt, CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(Url("sendMessage"), content, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            JsonDocument doc;
            try { doc = JsonDocument.Parse(body); }
            catch (JsonException)
            {
                // Non-JSON (e.g. proxy 5xx) — transient.
                return lastAttempt
                    ? (SendResult.Failed($"Telegram sendMessage HTTP {(int)resp.StatusCode}: non-JSON body"), null)
                    : (null, ErrorBackoff);
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
                {
                    var sentId = root.TryGetProperty("result", out var result) && GetLong(result, "message_id") is { } sid
                        ? sid.ToString(CultureInfo.InvariantCulture)
                        : null;
                    return (SendResult.Sent(sentId), null);
                }

                var (code, _, retryAfter) = ReadError(root);
                if (lastAttempt || !IsTransient(code, retryAfter))
                    return (SendResult.Failed($"Telegram sendMessage failed: {body}"), null);

                return (null, retryAfter is { } ra ? CapRetryAfter(ra) : ErrorBackoff);
            }
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

    private string BuildSendPayload(ChannelReply reply, string target, bool htmlFormatting)
    {
        var payload = new Dictionary<string, object?>
        {
            ["chat_id"] = long.TryParse(target, out var chatId) ? chatId : target,
        };
        if (reply.Text is not null)
        {
            // Render the reply kind visibly: interim progress notes and blocking questions read
            // differently from a final answer. The markers are plain emoji, safe under both modes.
            var text = reply.Kind switch
            {
                ChannelReplyKind.Progress => $"⏳ {reply.Text}",
                ChannelReplyKind.Question => $"❓ {reply.Text}",
                _ => reply.Text,
            };
            if (htmlFormatting)
            {
                payload["text"] = TelegramMarkdownRenderer.ToHtml(text);
                payload["parse_mode"] = "HTML";
            }
            else
            {
                payload["text"] = text;
            }
        }
        if (!string.IsNullOrEmpty(reply.ReplyToMessageId))
            payload["reply_to_message_id"] = long.TryParse(reply.ReplyToMessageId, out var rid) ? rid : reply.ReplyToMessageId;

        // Merge raw channel passthrough (parse_mode, disable_notification, ...).
        if (reply.RawOverrides is { ValueKind: JsonValueKind.Object } overrides)
        {
            foreach (var prop in overrides.EnumerateObject())
                payload[prop.Name] = prop.Value;
        }

        return JsonSerializer.Serialize(payload);
    }

    private async Task TryDeleteWebhookAsync(CancellationToken ct)
    {
        try
        {
            using var _ = await _http.GetAsync(Url("deleteWebhook"), ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[telegram] deleteWebhook failed (continuing)");
        }
    }

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

    private static ConversationKind ChatKind(JsonElement chat) => GetString(chat, "type") switch
    {
        "private" => ConversationKind.Direct,
        "group" or "supergroup" => ConversationKind.Group,
        "channel" => ConversationKind.Channel,
        _ => ConversationKind.Direct,
    };

    private static string? GetString(JsonElement obj, string prop) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static long? GetLong(JsonElement obj, string prop) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(prop, out var v)
        && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l)
            ? l
            : null;

    private static int? GetInt(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)
            ? i
            : null;
}
