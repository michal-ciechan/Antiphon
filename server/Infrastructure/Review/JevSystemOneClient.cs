using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Review;

/// <summary>
/// Jev System One over HTTP (review engine design 7.3), for every dialect: <c>typesafe-native</c>,
/// <c>litellm-systemone</c> (the enterprise default, D-29), <c>litellm-passthrough</c> and
/// <c>openrouter-systemone</c>. The body includes the model, shared state and named questions.
/// Choice and score questions carry their options in <c>criteria</c>.
/// </summary>
public sealed class JevSystemOneClient(HttpClient http, IOptions<ReviewDecisionSettings> settings) : IDecisionModel
{
    public const string CostHeader = "x-litellm-response-cost";
    private const int MaxErrorTextLength = 300;
    private const string DiagnosticOmitted = "Provider diagnostic omitted.";

    public bool Serves(string dialect) => JevDialects.All.Contains(dialect, StringComparer.OrdinalIgnoreCase);

    public async Task<DecisionResult> DecideAsync(DecisionRequest request, DecisionOptions options, CancellationToken ct)
    {
        if (!settings.Value.Providers.TryGetValue(options.ProviderId, out var provider))
            throw new DecisionProviderException(DecisionFailureKind.NotConfigured, $"Provider '{options.ProviderId}' is not configured.");
        var endpoint = Endpoint(provider)
            ?? throw new DecisionProviderException(DecisionFailureKind.NotConfigured, $"Provider '{options.ProviderId}' has no base URL.");
        var apiKey = provider.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new DecisionProviderException(DecisionFailureKind.NotConfigured, $"Provider '{options.ProviderId}' has no API key.");
        var model = options.Model ?? provider.Model
            ?? throw new DecisionProviderException(DecisionFailureKind.NotConfigured, $"Provider '{options.ProviderId}' names no model.");

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(BuildBody(request, model).ToJsonString(), Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

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
                throw new DecisionProviderException(Classify(status), $"HTTP {status}: {ErrorText(body, apiKey)}", status);

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

            var resolved = StringValue(root["model"]) ?? model;
            var servedBy = new DecisionServedBy(options.ProviderId, "Http", resolved);
            var usage = ParseUsage(root["usage"] as JsonObject);
            var cost = ParseCost(response, usage, provider);
            var parsed = new Dictionary<string, DecisionAnswer>(StringComparer.Ordinal);
            foreach (var question in request.Questions)
                parsed[question.Name] = ParseAnswer(question, answers[question.Name], servedBy);
            return new DecisionResult(parsed, usage, cost, "actual", servedBy);
        }
    }

    /// <summary>System One uses a choice criteria map and an ordered score criteria array (design 7.3).</summary>
    public static JsonObject BuildBody(DecisionRequest request, string model)
    {
        var questions = new JsonObject();
        foreach (var question in request.Questions)
        {
            var item = new JsonObject
            {
                ["type"] = WireType(question.Kind),
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
                var criteria = new JsonArray();
                foreach (var level in question.Options)
                    criteria.Add(string.IsNullOrWhiteSpace(level.Description) ? level.Value : level.Description);
                item["criteria"] = criteria;
            }
            questions[question.Name] = item;
        }

        return new JsonObject { ["model"] = model, ["state"] = request.State, ["questions"] = questions };
    }

    private static string WireType(DecisionQuestionKind kind) => kind switch
    {
        DecisionQuestionKind.Noul => "noul",
        DecisionQuestionKind.Choice => "choice",
        _ => "score",
    };

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

    /// <summary>Expose a bounded, redacted error message from LiteLLM, TypeSafe or a plain-text proxy.</summary>
    private static string ErrorText(string body, string apiKey)
    {
        const string noMessage = "The provider returned no error message.";
        var redactor = new DiagnosticsRedactor(includePaths: true);
        string Redact(string text) => redactor.Redact(text.Replace(apiKey, "***", StringComparison.Ordinal));

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return BoundErrorText(Redact(ErrorString(root) ?? noMessage));

            var error = ErrorProperty(root, "error");
            var detail = ErrorProperty(root, "detail");
            var message = ErrorString(ErrorProperty(error, "message")) ?? ErrorString(error) ?? ErrorString(detail);
            if (message is not null)
                return BoundErrorText(Redact(message));

            var diagnostic = new StringBuilder(MaxErrorTextLength);
            foreach (var validationMessage in ValidationMessages(detail))
            {
                if (diagnostic.Length > 0)
                    diagnostic.Append("; ");
                diagnostic.Append(Redact(validationMessage));
                if (diagnostic.Length >= MaxErrorTextLength)
                    break;
            }
            return diagnostic.Length == 0 ? noMessage : BoundErrorText(diagnostic.ToString());
        }
        catch (JsonException)
        {
            return DiagnosticOmitted;
        }
    }

    private static IEnumerable<string> ValidationMessages(JsonElement detail)
    {
        if (detail.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in detail.EnumerateArray())
                foreach (var message in ValidationMessages(entry))
                    yield return message;
            yield break;
        }

        if (detail.ValueKind != JsonValueKind.Object)
            yield break;

        var text = ErrorString(ErrorProperty(detail, "msg")) ?? ErrorString(ErrorProperty(detail, "message"));
        if (!string.IsNullOrWhiteSpace(text))
        {
            var location = ErrorProperty(detail, "loc");
            var path = location.ValueKind == JsonValueKind.Array
                ? string.Join('.', location.EnumerateArray()
                    .Where(segment => segment.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                    .Select(segment => ErrorString(segment) ?? segment.GetRawText()))
                : ErrorString(location);
            yield return string.IsNullOrEmpty(path) ? text : $"{path}: {text}";
        }

        foreach (var property in detail.EnumerateObject())
        {
            // TypeSafe may include rejected values and extra context alongside a validation message.
            if (property.Name is "input" or "ctx" or "loc")
                continue;
            foreach (var message in ValidationMessages(property.Value))
                yield return message;
        }
    }

    private static JsonElement ErrorProperty(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    private static string? ErrorString(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String)
            return null;

        try
        {
            return element.GetString();
        }
        catch (InvalidOperationException)
        {
            // JsonDocument accepts escaped lone surrogates until a string is decoded.
            throw new JsonException("The provider diagnostic contains an invalid JSON string.");
        }
    }

    private static string BoundErrorText(string text) => text.Length <= MaxErrorTextLength ? text : text[..MaxErrorTextLength];

    private static DecisionUsage ParseUsage(JsonObject? usage)
    {
        static long Read(JsonObject? node, params string[] names)
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

    /// <summary>Read the System One fields and legacy aliases. An omitted answer is a refusal.</summary>
    private static DecisionAnswer ParseAnswer(DecisionQuestion question, JsonNode? node, DecisionServedBy servedBy)
    {
        var refused = new DecisionAnswer(question.Name, question.Kind, null, null, null, Refused: true, servedBy);
        if (node is null)
            return refused;
        var obj = node as JsonObject;
        if (obj?["refused"] is JsonValue r && r.TryGetValue<bool>(out var isRefused) && isRefused)
            return refused;
        if (obj?["type"] is { } type && StringValue(type) != WireType(question.Kind))
            throw InvalidAnswer(question, "has a mismatched question type");

        var scoreAnswer = question.Kind == DecisionQuestionKind.Score && obj?.ContainsKey("score") == true;
        var probabilities = new Dictionary<string, double>(StringComparer.Ordinal);
        if ((obj?["probabilities"] ?? obj?["distribution"]) is JsonObject distribution)
        {
            foreach (var (key, value) in distribution)
            {
                if (Number(value) is { } probability && probability is >= 0 and <= 1)
                {
                    var label = scoreAnswer && int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                        && index < question.Options.Count ? question.Options[index].Value : key;
                    probabilities[label] = probability;
                }
            }
        }

        if (question.Kind == DecisionQuestionKind.Noul)
        {
            var hasNoul = obj?.ContainsKey("noul") == true;
            double? yes = hasNoul ? Number(obj!["noul"]) : node switch
            {
                JsonValue v when v.TryGetValue<double>(out var p) => p,
                JsonObject o when Number(o["probability"] ?? o["p_yes"] ?? o["value"] ?? o["answer"]) is { } p => p,
                _ => probabilities.TryGetValue("yes", out var py) ? py : probabilities.TryGetValue("true", out var pt) ? pt : null,
            };
            if (yes is not { } pYes || pYes is not (>= 0 and <= 1))
            {
                if (hasNoul || Text(obj?["value"] ?? obj?["answer"]) is not { } word || !TryYesNo(word, out var isYes))
                    throw InvalidAnswer(question, "has no probability of yes");
                pYes = isYes ? 1 : 0;
            }

            var map = new Dictionary<string, double> { ["yes"] = pYes, ["no"] = 1 - pYes };
            return new DecisionAnswer(question.Name, question.Kind, pYes >= 0.5 ? "yes" : "no", Math.Max(pYes, 1 - pYes), map, false, servedBy);
        }

        string? valueText;
        if (scoreAnswer)
        {
            if (Number(obj!["score"]) is not { } score || !double.IsFinite(score) || score < 0 || score > question.Options.Count - 1)
                throw InvalidAnswer(question, "has a score outside its levels");
            var index = (int)Math.Round(score, MidpointRounding.AwayFromZero);
            valueText = question.Options[index].Value;
        }
        else if (question.Kind == DecisionQuestionKind.Choice && obj?.ContainsKey("choice") == true)
        {
            valueText = StringValue(obj["choice"]);
        }
        else
        {
            valueText = node switch
            {
                JsonValue v => v.ToString(),
                JsonObject o => Text(o["value"] ?? o["answer"] ?? o["label"]),
                _ => null,
            };
            if (valueText is null && probabilities.Count > 0)
                valueText = probabilities.MaxBy(p => p.Value).Key;
        }
        if (valueText is null || question.Options.All(o => o.Value != valueText))
            throw InvalidAnswer(question, "is not one of its options");
        var confidenceNode = obj?["confidence"];
        var confidence = Number(confidenceNode);
        if (confidenceNode is not null && confidence is not (>= 0 and <= 1))
            throw InvalidAnswer(question, "has invalid confidence");
        if (confidence is null && probabilities.TryGetValue(valueText, out var probabilityForValue))
            confidence = probabilityForValue;
        return new DecisionAnswer(question.Name, question.Kind, valueText, confidence, probabilities.Count > 0 ? probabilities : null, false, servedBy);
    }

    private static DecisionProviderException InvalidAnswer(DecisionQuestion question, string problem) =>
        new(DecisionFailureKind.SchemaFailure, $"Answer '{question.Name}' {problem}.");

    private static double? Number(JsonNode? node) => node is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

    private static string? StringValue(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

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
