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
/// Jev System One over HTTP (review engine design 7.3), for every dialect: <c>typesafe-native</c>,
/// <c>litellm-systemone</c> (the enterprise default, D-29), <c>litellm-passthrough</c> and
/// <c>openrouter-systemone</c>. The body is the same for all of them:
/// <c>{ model, state, questions: { name: { type, instructions, choices | levels } } }</c>.
/// Never <c>/v1/decisions</c>. The key is sent as a bearer token and never logged.
/// </summary>
public sealed class JevSystemOneClient(HttpClient http, IOptions<ReviewDecisionSettings> settings) : IDecisionModel
{
    public const string CostHeader = "x-litellm-response-cost";

    public bool Serves(string dialect) => JevDialects.All.Contains(dialect, StringComparer.OrdinalIgnoreCase);

    public async Task<DecisionResult> DecideAsync(DecisionRequest request, DecisionOptions options, CancellationToken ct)
    {
        if (!settings.Value.Providers.TryGetValue(options.ProviderId, out var provider))
            throw new DecisionProviderException(DecisionFailureKind.NotConfigured, $"Provider '{options.ProviderId}' is not configured.");
        var endpoint = Endpoint(provider)
            ?? throw new DecisionProviderException(DecisionFailureKind.NotConfigured, $"Provider '{options.ProviderId}' has no base URL.");
        if (string.IsNullOrWhiteSpace(provider.ApiKey))
            throw new DecisionProviderException(DecisionFailureKind.NotConfigured, $"Provider '{options.ProviderId}' has no API key.");
        var model = options.Model ?? provider.Model
            ?? throw new DecisionProviderException(DecisionFailureKind.NotConfigured, $"Provider '{options.ProviderId}' names no model.");

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(BuildBody(request, model).ToJsonString(), Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new DecisionProviderException(DecisionFailureKind.Unavailable, $"Transport failure: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new DecisionProviderException(DecisionFailureKind.Timeout, "The HTTP client timed out.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
                throw new DecisionProviderException(Classify(status), $"HTTP {status}: {ErrorText(body)}", status);

            JsonObject root;
            try
            {
                root = JsonNode.Parse(body) as JsonObject
                    ?? throw new DecisionProviderException(DecisionFailureKind.SchemaFailure, "The response is not a JSON object.");
            }
            catch (JsonException ex)
            {
                throw new DecisionProviderException(DecisionFailureKind.SchemaFailure, $"The response is not JSON: {ex.Message}");
            }

            if (root["answers"] is not JsonObject answers)
                throw new DecisionProviderException(DecisionFailureKind.SchemaFailure, "The response has no 'answers' object.");

            var resolved = root["model"]?.GetValue<string>() ?? model;
            var servedBy = new DecisionServedBy(options.ProviderId, "Http", resolved);
            var usage = ParseUsage(root["usage"]);
            var cost = ParseCost(response, usage, provider);
            var parsed = new Dictionary<string, DecisionAnswer>(StringComparer.Ordinal);
            foreach (var question in request.Questions)
                parsed[question.Name] = ParseAnswer(question, answers[question.Name], servedBy);
            return new DecisionResult(parsed, usage, cost, "actual", servedBy);
        }
    }

    /// <summary>
    /// The request body for <c>POST /v1/systemone</c> (LiteLLM 1.110 / TypeSafe). A choice's
    /// <c>criteria</c> is a map of option value to description (null when the value is enough).
    /// A score's <c>criteria</c> is the level labels, lowest first. The OpenAI
    /// <c>choices</c>/<c>levels</c> shape is rejected with 400 before Jev is called.
    /// </summary>
    public static JsonObject BuildBody(DecisionRequest request, string model)
    {
        var questions = new JsonObject();
        foreach (var question in request.Questions)
        {
            var item = new JsonObject
            {
                ["type"] = question.Kind switch
                {
                    DecisionQuestionKind.Noul => "noul",
                    DecisionQuestionKind.Choice => "choice",
                    _ => "score",
                },
                ["instructions"] = question.Instructions,
            };
            if (question.Kind == DecisionQuestionKind.Choice)
            {
                var criteria = new JsonObject();
                foreach (var option in question.Options)
                    criteria[option.Value] = string.IsNullOrWhiteSpace(option.Description) ? null : option.Description;
                item["criteria"] = criteria;
            }
            else if (question.Kind == DecisionQuestionKind.Score)
            {
                item["criteria"] = new JsonArray(question.Options.Select(o => (JsonNode)o.Value).ToArray());
            }

            questions[question.Name] = item;
        }

        return new JsonObject { ["model"] = model, ["state"] = request.State, ["questions"] = questions };
    }

    private static Uri? Endpoint(ReviewDecisionProvider provider)
    {
        var dialect = provider.Dialect.ToLowerInvariant();
        var root = provider.BaseUrl?.TrimEnd('/');
        var path = dialect switch
        {
            JevDialects.TypeSafeNative => (root ?? "https://api.typesafe.ai") + "/v1/systemone",
            JevDialects.OpenRouterSystemOne => (root ?? "https://openrouter.ai/api") + "/v1/systemone",
            JevDialects.LiteLlmSystemOne when root is not null => root + "/v1/systemone",
            JevDialects.LiteLlmPassthrough when root is not null => root + "/typesafe/v1/systemone",
            _ => null,
        };
        return path is null ? null : new Uri(path, UriKind.Absolute);
    }

    /// <summary>Design 7.4: 429/529 back off then fall back; 400/401/403/422 (and 413/415) fail loudly; 5xx falls back.</summary>
    private static DecisionFailureKind Classify(int status) => status switch
    {
        429 or 529 => DecisionFailureKind.RateLimited,
        400 or 401 or 403 or 413 or 415 or 422 => DecisionFailureKind.Rejected,
        408 or 504 => DecisionFailureKind.Timeout,
        _ => DecisionFailureKind.Unavailable,
    };

    /// <summary>The LiteLLM <c>{"error":{"message"}}</c> envelope or the first 300 characters of the body.</summary>
    private static string ErrorText(string body)
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

    private static DecisionUsage ParseUsage(JsonNode? usage)
    {
        static long Read(JsonNode? node, params string[] names)
        {
            foreach (var name in names)
            {
                if (node?[name] is JsonValue value && value.TryGetValue<long>(out var n))
                    return n;
            }

            return 0;
        }

        return new DecisionUsage(
            Read(usage, "input_tokens", "prompt_tokens"),
            Read(usage, "output_tokens", "completion_tokens"));
    }

    /// <summary>The proxy's cost header when present (LiteLLM), else the provider's price table (design 7.3).</summary>
    private static decimal ParseCost(HttpResponseMessage response, DecisionUsage usage, ReviewDecisionProvider provider)
    {
        if (response.Headers.TryGetValues(CostHeader, out var values)
            && decimal.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var header))
            return header;
        return (usage.InputTokens * provider.InputPricePerMillionUsd + usage.OutputTokens * provider.OutputPricePerMillionUsd) / 1_000_000m;
    }

    /// <summary>
    /// Reads one answer. The field names are not settled until the first live call (OQ-2), so this
    /// accepts <c>value</c>/<c>answer</c>, <c>probabilities</c>/<c>distribution</c> and
    /// <c>probability</c>/<c>p_yes</c>. A missing answer key is a refusal (LiteLLM rule).
    /// </summary>
    private static DecisionAnswer ParseAnswer(DecisionQuestion question, JsonNode? node, DecisionServedBy servedBy)
    {
        var refused = new DecisionAnswer(question.Name, question.Kind, null, null, null, Refused: true, servedBy);
        if (node is null)
            return refused;
        if (node is JsonObject obj && obj["refused"] is JsonValue r && r.TryGetValue<bool>(out var isRefused) && isRefused)
            return refused;

        var probabilities = new Dictionary<string, double>(StringComparer.Ordinal);
        if (node is JsonObject withDistribution && (withDistribution["probabilities"] ?? withDistribution["distribution"]) is JsonObject distribution)
        {
            foreach (var (key, value) in distribution)
            {
                if (value is JsonValue v && v.TryGetValue<double>(out var p))
                    probabilities[key] = p;
            }
        }

        if (question.Kind == DecisionQuestionKind.Noul)
        {
            double? yes = node switch
            {
                JsonValue v when v.TryGetValue<double>(out var p) => p,
                JsonObject o when Number(o["noul"] ?? o["probability"] ?? o["p_yes"] ?? o["value"] ?? o["answer"]) is { } p => p,
                _ => probabilities.TryGetValue("yes", out var py) ? py : probabilities.TryGetValue("true", out var pt) ? pt : null,
            };
            if (yes is not { } pYes || pYes is < 0 or > 1)
            {
                if (node is JsonObject o && Text(o["value"] ?? o["answer"]) is { } word && TryYesNo(word, out var isYes))
                    pYes = isYes ? 1 : 0;
                else
                    throw new DecisionProviderException(DecisionFailureKind.SchemaFailure, $"Answer '{question.Name}' has no probability of yes.");
            }

            var map = new Dictionary<string, double> { ["yes"] = pYes, ["no"] = 1 - pYes };
            return new DecisionAnswer(question.Name, question.Kind, pYes >= 0.5 ? "yes" : "no", Math.Max(pYes, 1 - pYes), map, false, servedBy);
        }

        var valueText = node switch
        {
            JsonValue v => v.ToString(),
            JsonObject o => Text(o["choice"] ?? o["score"] ?? o["value"] ?? o["answer"] ?? o["label"]),
            _ => null,
        };
        if (valueText is not null && question.Kind == DecisionQuestionKind.Score && int.TryParse(valueText, out var scoreIndex)
            && scoreIndex >= 0 && scoreIndex < question.Options.Count)
            valueText = question.Options[scoreIndex].Value;
        if (valueText is null && probabilities.Count > 0)
            valueText = probabilities.MaxBy(p => p.Value).Key;
        if (valueText is null || question.Options.All(o => o.Value != valueText))
            throw new DecisionProviderException(DecisionFailureKind.SchemaFailure, $"Answer '{question.Name}' is not one of its options.");
        double? confidence = node is JsonObject c && Number(c["confidence"]) is { } given
            ? given
            : probabilities.TryGetValue(valueText, out var pv) ? pv : null;
        return new DecisionAnswer(question.Name, question.Kind, valueText, confidence, probabilities.Count > 0 ? probabilities : null, false, servedBy);
    }

    private static double? Number(JsonNode? node) => node is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

    private static string? Text(JsonNode? node) => node is JsonValue v
        ? v.TryGetValue<string>(out var s) ? s : v.ToString()
        : null;

    private static bool TryYesNo(string word, out bool yes)
    {
        yes = word.Equals("yes", StringComparison.OrdinalIgnoreCase) || word.Equals("true", StringComparison.OrdinalIgnoreCase);
        return yes || word.Equals("no", StringComparison.OrdinalIgnoreCase) || word.Equals("false", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Placeholder for decisions over standing seats (design 7.2, <c>decision-request/1</c> ABF). Seats do
/// not exist yet, so every call reports not configured and the chain moves on.
/// </summary>
public sealed class StandingSeatDecisionModel : IDecisionModel
{
    public bool Serves(string dialect) => dialect.Equals(ReviewDecisionSettings.SeatDialect, StringComparison.OrdinalIgnoreCase);

    public Task<DecisionResult> DecideAsync(DecisionRequest request, DecisionOptions options, CancellationToken ct) =>
        throw new DecisionProviderException(DecisionFailureKind.NotConfigured,
            $"Standing-seat decisions are not configured yet (provider '{options.ProviderId}').");
}
