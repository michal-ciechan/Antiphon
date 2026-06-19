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
        MarkdownFlavor = "MarkdownV2",
        MaxTextLength = 4096,
        AttachmentKinds =
        [
            AttachmentKind.Image, AttachmentKind.Video, AttachmentKind.Audio,
            AttachmentKind.Voice, AttachmentKind.File, AttachmentKind.Sticker,
            AttachmentKind.Location, AttachmentKind.Contact,
        ],
    };

    private string Url(string method) => $"{_settings.ApiBaseUrl}/bot{_settings.BotToken}/{method}";

    public async IAsyncEnumerable<ChannelMessage> ReceiveAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // getUpdates returns 409 if a webhook is set — clear it first (idempotent).
        await TryDeleteWebhookAsync(cancellationToken);

        long offset = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            IReadOnlyList<ChannelMessage> batch;
            try
            {
                (batch, offset) = await FetchBatchAsync(offset, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[telegram] getUpdates failed; retrying in 3s");
                if (!await DelayQuietAsync(TimeSpan.FromSeconds(3), cancellationToken))
                    yield break;
                continue;
            }

            foreach (var message in batch)
                yield return message;
        }
    }

    private async Task<(IReadOnlyList<ChannelMessage> Messages, long NextOffset)> FetchBatchAsync(long offset, CancellationToken ct)
    {
        var url = $"{Url("getUpdates")}?timeout={_settings.LongPollTimeoutSeconds.ToString(CultureInfo.InvariantCulture)}&offset={offset.ToString(CultureInfo.InvariantCulture)}";
        using var resp = await _http.GetAsync(url, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var messages = new List<ChannelMessage>();
        var next = offset;

        if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
        {
            _logger.LogWarning("[telegram] getUpdates not ok: {Body}", json);
            return (messages, next);
        }
        if (!doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
            return (messages, next);

        foreach (var update in result.EnumerateArray())
        {
            if (update.TryGetProperty("update_id", out var uid) && uid.TryGetInt64(out var id))
                next = id + 1;

            var message = TryNormalize(update);
            if (message is not null)
                messages.Add(message);
        }

        return (messages, next);
    }

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

        var payload = new Dictionary<string, object?>
        {
            ["chat_id"] = long.TryParse(target, out var chatId) ? chatId : target,
        };
        if (reply.Text is not null)
            payload["text"] = reply.Text;
        if (!string.IsNullOrEmpty(reply.ReplyToMessageId))
            payload["reply_to_message_id"] = long.TryParse(reply.ReplyToMessageId, out var rid) ? rid : reply.ReplyToMessageId;

        // Merge raw channel passthrough (parse_mode, disable_notification, ...).
        if (reply.RawOverrides is { ValueKind: JsonValueKind.Object } overrides)
        {
            foreach (var prop in overrides.EnumerateObject())
                payload[prop.Name] = prop.Value;
        }

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        try
        {
            using var resp = await _http.PostAsync(Url("sendMessage"), content, cancellationToken);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            {
                var sentId = doc.RootElement.TryGetProperty("result", out var result) && GetLong(result, "message_id") is { } sid
                    ? sid.ToString(CultureInfo.InvariantCulture)
                    : null;
                return SendResult.Sent(sentId);
            }
            return SendResult.Failed($"Telegram sendMessage failed: {body}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return SendResult.Failed(ex.Message);
        }
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
