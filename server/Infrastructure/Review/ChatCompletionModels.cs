using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Review;

/// <summary>
/// OpenAI-compatible chat completions over HTTP (review engine design 7.2), dialect <c>openai-chat</c>:
/// <c>POST {baseUrl}/v1/chat/completions</c> with a bearer key. The enterprise LiteLLM proxy takes the same
/// virtual key as Jev, so its provider names <c>Review:Secrets:JevApiKey</c> by default. The cost is the
/// proxy's <c>x-litellm-response-cost</c> header, else the provider's price table. The key is never logged.
/// </summary>
public sealed class OpenAiChatCompletionModel(HttpClient http, IOptions<ReviewCompletionSettings> settings) : ICompletionModel
{
    public const string CostHeader = "x-litellm-response-cost";

    public bool Serves(string dialect) => dialect.Equals(ReviewCompletionDialects.OpenAiChat, StringComparison.OrdinalIgnoreCase);

    public async Task<CompletionResult> CompleteAsync(CompletionRequest request, CompletionOptions options, CancellationToken ct)
    {
        var provider = CompletionHttp.Provider(settings.Value, options.ProviderId);
        if (string.IsNullOrWhiteSpace(provider.BaseUrl))
            throw CompletionHttp.NotConfigured(options.ProviderId, "has no base URL");
        if (string.IsNullOrWhiteSpace(provider.ApiKey))
            throw CompletionHttp.NotConfigured(options.ProviderId, "has no API key");
        var model = options.Model ?? provider.Model;
        if (string.IsNullOrWhiteSpace(model))
            throw CompletionHttp.NotConfigured(options.ProviderId, "names no model");

        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = request.Instructions },
                new JsonObject { ["role"] = "user", ["content"] = CompletionText.UserMessage(request) }),
        };
        if (provider.MaxOutputTokens > 0)
            body["max_tokens"] = provider.MaxOutputTokens;

        using var message = new HttpRequestMessage(HttpMethod.Post, ChatEndpoint(provider.BaseUrl))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);

        var (root, response) = await CompletionHttp.SendAsync(http, message, ct);
        using (response)
        {
            var choice = root["choices"] is JsonArray { Count: > 0 } choices ? choices[0] as JsonObject : null;
            if (choice is null)
                throw new CompletionProviderException(DecisionFailureKind.SchemaFailure, "The response has no choices.");
            var text = Content(choice["message"]?["content"]);
            if (string.Equals(choice["finish_reason"]?.GetValue<string>(), "length", StringComparison.Ordinal))
                throw new CompletionProviderException(DecisionFailureKind.SchemaFailure, "The answer was cut off at the output limit.");
            if (string.IsNullOrWhiteSpace(text))
                throw new CompletionProviderException(DecisionFailureKind.SchemaFailure, "The answer is empty.");

            var usage = new DecisionUsage(CompletionHttp.Long(root["usage"], "prompt_tokens"), CompletionHttp.Long(root["usage"], "completion_tokens"));
            var cost = response.Headers.TryGetValues(CostHeader, out var values)
                && decimal.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var header)
                    ? header
                    : CompletionHttp.PriceTable(usage, provider);
            var resolved = root["model"] is JsonValue m && m.TryGetValue<string>(out var name) ? name : model;
            return new CompletionResult(text, usage, cost, "actual", new DecisionServedBy(options.ProviderId, "Http", resolved));
        }
    }

    /// <summary><c>{root}/v1/chat/completions</c>; a root that already ends in <c>/v1</c> or the full path is accepted.</summary>
    public static Uri ChatEndpoint(string baseUrl)
    {
        var root = baseUrl.Trim().TrimEnd('/');
        if (root.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return new Uri(root, UriKind.Absolute);
        return new Uri(root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? root + "/chat/completions" : root + "/v1/chat/completions",
            UriKind.Absolute);
    }

    /// <summary>The message content: a string, or an array of parts whose text is joined.</summary>
    private static string? Content(JsonNode? content) => content switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonArray parts => string.Concat(parts.OfType<JsonObject>()
            .Where(p => p["type"]?.GetValue<string>() is null or "text")
            .Select(p => p["text"] is JsonValue t && t.TryGetValue<string>(out var s) ? s : "")),
        _ => null,
    };
}

/// <summary>
/// The Anthropic Messages API over HTTP (review engine design 7.2), dialect <c>anthropic-messages</c>:
/// <c>POST {baseUrl}/v1/messages</c> with <c>x-api-key</c> (or a bearer token for a gateway) and
/// <c>anthropic-version</c>. The default model is Claude Haiku 5.5. The answer is the text blocks;
/// a refusal or an answer cut off at <c>max_tokens</c> is a schema failure, so the chain moves on. The
/// cost is the provider's price table. The key is never logged.
/// </summary>
public sealed class AnthropicMessagesCompletionModel(HttpClient http, IOptions<ReviewCompletionSettings> settings) : ICompletionModel
{
    public const string DefaultBaseUrl = "https://api.anthropic.com";
    public const string DefaultApiVersion = "2026-10-21";
    public const int DefaultMaxTokens = 16_000;

    public bool Serves(string dialect) => dialect.Equals(ReviewCompletionDialects.AnthropicMessages, StringComparison.OrdinalIgnoreCase);

    public async Task<CompletionResult> CompleteAsync(CompletionRequest request, CompletionOptions options, CancellationToken ct)
    {
        var provider = CompletionHttp.Provider(settings.Value, options.ProviderId);
        if (string.IsNullOrWhiteSpace(provider.ApiKey))
            throw CompletionHttp.NotConfigured(options.ProviderId, "has no API key");
        var model = options.Model ?? provider.Model;
        if (string.IsNullOrWhiteSpace(model))
            throw CompletionHttp.NotConfigured(options.ProviderId, "names no model");

        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = provider.MaxOutputTokens > 0 ? provider.MaxOutputTokens : DefaultMaxTokens,
            ["system"] = request.Instructions,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = CompletionText.UserMessage(request) }),
        };
        if (!string.IsNullOrWhiteSpace(provider.Effort))
            body["output_config"] = new JsonObject { ["effort"] = provider.Effort.Trim().ToLowerInvariant() };

        using var message = new HttpRequestMessage(HttpMethod.Post, MessagesEndpoint(provider.BaseUrl))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (string.Equals(provider.AuthScheme, "bearer", StringComparison.OrdinalIgnoreCase))
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        else
            message.Headers.TryAddWithoutValidation("x-api-key", provider.ApiKey);
        message.Headers.TryAddWithoutValidation("anthropic-version", string.IsNullOrWhiteSpace(provider.ApiVersion) ? DefaultApiVersion : provider.ApiVersion);

        var (root, response) = await CompletionHttp.SendAsync(http, message, ct);
        using (response)
        {
            var stop = root["stop_reason"] is JsonValue s && s.TryGetValue<string>(out var reason) ? reason : null;
            if (stop == "refusal")
            {
                var category = root["stop_details"]?["category"] is JsonValue c && c.TryGetValue<string>(out var cat) ? cat : "unspecified";
                throw new CompletionProviderException(DecisionFailureKind.SchemaFailure, $"The model declined the request ({category}).");
            }

            if (stop == "max_tokens")
                throw new CompletionProviderException(DecisionFailureKind.SchemaFailure, "The answer was cut off at max_tokens.");
            var text = root["content"] is JsonArray blocks
                ? string.Concat(blocks.OfType<JsonObject>()
                    .Where(b => b["type"]?.GetValue<string>() == "text")
                    .Select(b => b["text"] is JsonValue t && t.TryGetValue<string>(out var part) ? part : ""))
                : null;
            if (string.IsNullOrWhiteSpace(text))
                throw new CompletionProviderException(DecisionFailureKind.SchemaFailure, "The answer has no text.");

            var u = root["usage"];
            var usage = new DecisionUsage(
                CompletionHttp.Long(u, "input_tokens") + CompletionHttp.Long(u, "cache_creation_input_tokens") + CompletionHttp.Long(u, "cache_read_input_tokens"),
                CompletionHttp.Long(u, "output_tokens"));
            var resolved = root["model"] is JsonValue m && m.TryGetValue<string>(out var name) ? name : model;
            return new CompletionResult(text, usage, CompletionHttp.PriceTable(usage, provider), "actual",
                new DecisionServedBy(options.ProviderId, "Http", resolved));
        }
    }

    /// <summary><c>{root}/v1/messages</c>; a root that already ends in <c>/v1</c> is accepted.</summary>
    public static Uri MessagesEndpoint(string? baseUrl)
    {
        var root = (string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim()).TrimEnd('/');
        if (root.EndsWith("/v1/messages", StringComparison.OrdinalIgnoreCase))
            return new Uri(root, UriKind.Absolute);
        return new Uri(root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? root + "/messages" : root + "/v1/messages", UriKind.Absolute);
    }
}

/// <summary>What the HTTP completion adapters share: the provider lookup, the send, error mapping and the price table.</summary>
internal static class CompletionHttp
{
    public static ReviewCompletionProvider Provider(ReviewCompletionSettings settings, string providerId) =>
        settings.Providers.TryGetValue(providerId, out var provider)
            ? provider
            : throw NotConfigured(providerId, "is not configured (Review:Completions:Providers)");

    public static CompletionProviderException NotConfigured(string providerId, string what) =>
        new(DecisionFailureKind.NotConfigured, $"Completion provider '{providerId}' {what}.");

    /// <summary>Sends the request and returns the JSON body. Every failure is a <see cref="CompletionProviderException"/>.</summary>
    public static async Task<(JsonObject Root, HttpResponseMessage Response)> SendAsync(HttpClient http, HttpRequestMessage message, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new CompletionProviderException(DecisionFailureKind.Unavailable, $"Transport failure: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new CompletionProviderException(DecisionFailureKind.Timeout, "The HTTP client timed out.");
        }

        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            response.Dispose();
            throw new CompletionProviderException(DecisionFailureKind.Unavailable, $"Transport failure: {ex.Message}");
        }

        var status = (int)response.StatusCode;
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new CompletionProviderException(Classify(status), $"HTTP {status}: {ErrorText(body)}", status);
        }

        try
        {
            if (JsonNode.Parse(body) is JsonObject root)
                return (root, response);
        }
        catch (JsonException ex)
        {
            response.Dispose();
            throw new CompletionProviderException(DecisionFailureKind.SchemaFailure, $"The response is not JSON: {ex.Message}");
        }

        response.Dispose();
        throw new CompletionProviderException(DecisionFailureKind.SchemaFailure, "The response is not a JSON object.");
    }

    /// <summary>Design 7.4: 429/529 back off then fall back; 400/401/402/403/404/413/415/422 fail loudly; a timeout or 5xx falls back.</summary>
    public static DecisionFailureKind Classify(int status) => status switch
    {
        429 or 529 => DecisionFailureKind.RateLimited,
        400 or 401 or 402 or 403 or 404 or 413 or 415 or 422 => DecisionFailureKind.Rejected,
        408 or 504 => DecisionFailureKind.Timeout,
        _ => DecisionFailureKind.Unavailable,
    };

    /// <summary>The <c>{"error":{"message"}}</c> envelope (OpenAI, LiteLLM and Anthropic all use it) or the first 300 characters.</summary>
    public static string ErrorText(string body)
    {
        try
        {
            if (JsonNode.Parse(body) is JsonObject root && root["error"] is JsonObject error && error["message"] is JsonValue text)
                return text.ToString();
        }
        catch (JsonException)
        {
        }

        return body.Length <= 300 ? body : body[..300];
    }

    public static long Long(JsonNode? node, string name) =>
        node?[name] is JsonValue value && value.TryGetValue<long>(out var n) ? n : 0;

    public static decimal PriceTable(DecisionUsage usage, ReviewCompletionProvider provider) =>
        (usage.InputTokens * provider.InputPricePerMillionUsd + usage.OutputTokens * provider.OutputPricePerMillionUsd) / 1_000_000m;
}
