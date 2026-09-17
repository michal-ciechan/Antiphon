using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Antiphon.NightlyWatchdog;

/// <summary>
/// CARD-0545 D-5: Telegram Bot API <c>sendMessage</c> called directly (no Windmill, no bus). Plain
/// text, no parse mode, link previews disabled. The response is tier-1 acceptance. An unset,
/// non-numeric or unqualified destination refuses with <c>destination-unauthorized</c> and sends
/// nothing; no default is ever substituted. Errors never carry the bot token.
/// </summary>
public sealed class TelegramBotTransport(HttpClient http, WatchdogOptions options, Func<string?> qualifiedDestinationHash)
    : INotificationTransport
{
    public const string BaseUrl = "https://api.telegram.org";

    public async Task<TransportResult> SendAsync(OutboundMessage message, CancellationToken ct)
    {
        var chatId = options.DestinationChatId;
        if (string.IsNullOrWhiteSpace(chatId)
            || !long.TryParse(chatId.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
            return TransportResult.Fail("destination-unauthorized", "destination chat id is unset or not numeric");
        var qualified = qualifiedDestinationHash();
        if (qualified is not null && !string.Equals(qualified, WatchdogOptions.DestinationHash(chatId), StringComparison.Ordinal))
            return TransportResult.Fail("destination-unauthorized", "destination mismatch: configured chat is not the qualified destination");
        if (string.IsNullOrWhiteSpace(options.TelegramBotToken))
            return TransportResult.Fail("bad-token", "bot token is not configured");

        var token = options.TelegramBotToken;
        var body = new JsonObject
        {
            ["chat_id"] = chatId.Trim(),
            ["text"] = message.Text,
            ["disable_web_page_preview"] = true,
        };
        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/bot{token}/sendMessage")
            {
                Content = JsonContent.Create(body),
            };
            response = await http.SendAsync(request, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return TransportResult.Fail("transport", "request timed out");
        }
        catch (HttpRequestException ex)
        {
            return TransportResult.Fail("transport", Redact($"request failed: {ex.HttpRequestError}", token));
        }

        using (response)
        {
            JsonObject? json = null;
            try { json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) as JsonObject; }
            catch (System.Text.Json.JsonException) { }
            var description = json?["description"]?.GetValue<string>() ?? "";
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                if (json?["ok"]?.GetValue<bool>() == true && json["result"]?["message_id"] is JsonValue id)
                    return TransportResult.Ok(id.ToJsonString().Trim('"'));
                return TransportResult.Fail("api-error", Redact($"telegram returned ok=false: {description}", token));
            }
            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => TransportResult.Fail("bad-token", "telegram rejected the bot token (401)"),
                HttpStatusCode.BadRequest when description.Contains("chat not found", StringComparison.OrdinalIgnoreCase)
                    => TransportResult.Fail("chat-not-found", "telegram: chat not found (400)"),
                HttpStatusCode.Forbidden when description.Contains("blocked", StringComparison.OrdinalIgnoreCase)
                    => TransportResult.Fail("blocked", "telegram: bot was blocked by the user (403)"),
                HttpStatusCode.TooManyRequests => TransportResult.Fail("rate-limited", "telegram rate limit (429)",
                    json?["parameters"]?["retry_after"]?.GetValue<int>()),
                _ when status >= 500 => TransportResult.Fail("transport", $"telegram server error ({status})"),
                _ => TransportResult.Fail("api-error", Redact($"telegram error ({status}): {description}", token)),
            };
        }
    }

    private static string Redact(string text, string token) => text.Replace(token, "***", StringComparison.Ordinal);
}
