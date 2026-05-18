using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Agents;

public sealed class AgentDraftGenerator : IAgentDraftGenerator
{
    private const string DraftSystemPrompt = """
        You draft Antiphon agent definitions from a short user description.
        Return only JSON with exactly these fields:
        {
          "name": "short human-readable agent name",
          "workingDirectory": "absolute local working directory if provided or confidently implied, otherwise empty string",
          "details": "clear operational notes for this agent",
          "assignmentPolicy": "AutoPick"
        }
        Use assignmentPolicy "AutoPick" unless the user explicitly asks for manual confirmation or pause.
        Keep details concise and do not invent repository paths that were not provided.
        """;

    private readonly LlmSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AgentDraftGenerator> _logger;

    public AgentDraftGenerator(
        IOptions<LlmSettings> settings,
        IHttpClientFactory httpClientFactory,
        ILogger<AgentDraftGenerator> logger)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<AgentDraftSuggestion?> GenerateDraftAsync(string description, CancellationToken ct)
    {
        foreach (var candidate in GetCandidates())
        {
            try
            {
                var text = await GenerateTextAsync(candidate, description, ct);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var suggestion = TryParseSuggestion(text);
                if (suggestion is not null)
                    return suggestion;

                _logger.LogWarning(
                    "Agent draft model response did not contain parseable JSON for provider={Provider} model={Model}.",
                    candidate.ProviderName,
                    candidate.Model);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Agent draft model call failed for provider={Provider} model={Model}.",
                    candidate.ProviderName,
                    candidate.Model);
            }
        }

        return null;
    }

    private IEnumerable<LlmCandidate> GetCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var providerName in OrderedProviderNames())
        {
            if (!_settings.Providers.TryGetValue(providerName, out var provider))
                continue;
            if (string.IsNullOrWhiteSpace(provider.ApiKey))
                continue;

            foreach (var model in OrderedModels(providerName, provider))
            {
                if (string.IsNullOrWhiteSpace(model))
                    continue;

                var key = $"{providerName}:{model}";
                if (!seen.Add(key))
                    continue;

                yield return new LlmCandidate(providerName, provider, model);
            }
        }
    }

    private IEnumerable<string> OrderedProviderNames()
    {
        if (!string.IsNullOrWhiteSpace(_settings.DefaultProvider))
            yield return _settings.DefaultProvider;

        foreach (var providerName in _settings.Providers.Keys.OrderBy(name => name))
        {
            if (!providerName.Equals(_settings.DefaultProvider, StringComparison.OrdinalIgnoreCase))
                yield return providerName;
        }
    }

    private IEnumerable<string> OrderedModels(string providerName, LlmProviderSettings provider)
    {
        if (providerName.Equals(_settings.DefaultProvider, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(_settings.DefaultModel))
        {
            yield return _settings.DefaultModel;
        }

        foreach (var model in provider.Models.OrderBy(PreferredModelRank).ThenBy(model => model))
            yield return model;
    }

    private async Task<string?> GenerateTextAsync(LlmCandidate candidate, string description, CancellationToken ct)
    {
        return candidate.ProviderName.Equals("anthropic", StringComparison.OrdinalIgnoreCase)
            ? await GenerateAnthropicTextAsync(candidate, description, ct)
            : await GenerateOpenAiCompatibleTextAsync(candidate, description, ct);
    }

    private async Task<string?> GenerateOpenAiCompatibleTextAsync(
        LlmCandidate candidate,
        string description,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();
        var endpoint = BuildEndpoint(candidate.Provider.BaseUrl, "https://api.openai.com", "/v1/chat/completions");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", candidate.Provider.ApiKey);
        request.Content = JsonContent(new
        {
            model = candidate.Model,
            messages = new[]
            {
                new { role = "system", content = DraftSystemPrompt },
                new { role = "user", content = description }
            },
            temperature = 0,
            max_tokens = 700
        });

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "OpenAI-compatible draft request failed for provider={Provider} model={Model} status={StatusCode}.",
                candidate.ProviderName,
                candidate.Model,
                (int)response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var choices = document.RootElement.TryGetProperty("choices", out var choicesElement)
            ? choicesElement
            : default;
        if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            return null;

        var firstChoice = choices[0];
        if (!firstChoice.TryGetProperty("message", out var message))
            return null;

        return GetString(message, "content");
    }

    private async Task<string?> GenerateAnthropicTextAsync(
        LlmCandidate candidate,
        string description,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();
        var endpoint = BuildEndpoint(candidate.Provider.BaseUrl, "https://api.anthropic.com", "/v1/messages");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("x-api-key", candidate.Provider.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = JsonContent(new
        {
            model = candidate.Model,
            max_tokens = 700,
            system = DraftSystemPrompt,
            messages = new[]
            {
                new { role = "user", content = description }
            }
        });

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Anthropic draft request failed for provider={Provider} model={Model} status={StatusCode}.",
                candidate.ProviderName,
                candidate.Model,
                (int)response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!document.RootElement.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return string.Join(
            string.Empty,
            content.EnumerateArray()
                .Select(item => GetString(item, "text"))
                .Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    private static AgentDraftSuggestion? TryParseSuggestion(string text)
    {
        var json = ExtractJson(text);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            return new AgentDraftSuggestion(
                GetString(root, "name"),
                GetString(root, "workingDirectory"),
                GetString(root, "details"),
                ParseAssignmentPolicy(GetString(root, "assignmentPolicy")));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : string.Empty;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static AgentAssignmentPolicy? ParseAssignmentPolicy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return Enum.TryParse<AgentAssignmentPolicy>(value, ignoreCase: true, out var policy)
            ? policy
            : null;
    }

    private static StringContent JsonContent(object payload)
    {
        return new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");
    }

    private static string BuildEndpoint(string configuredBaseUrl, string defaultBaseUrl, string endpointPath)
    {
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl) ? defaultBaseUrl : configuredBaseUrl;
        var trimmed = baseUrl.TrimEnd('/');
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) &&
            endpointPath.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase))
        {
            return $"{trimmed}{endpointPath[3..]}";
        }

        return $"{trimmed}{endpointPath}";
    }

    private static int PreferredModelRank(string model)
    {
        if (model.Contains("mini", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (model.Contains("4o", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (model.Contains("sonnet", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (model.Contains("gpt-5", StringComparison.OrdinalIgnoreCase))
            return 3;

        return 4;
    }

    private sealed record LlmCandidate(
        string ProviderName,
        LlmProviderSettings Provider,
        string Model);
}
