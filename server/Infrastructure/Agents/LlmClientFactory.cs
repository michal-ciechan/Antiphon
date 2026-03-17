using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Antiphon.Server.Application.Settings;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>
/// Creates IChatClient instances for different providers/models based on routing config (FR19, FR44).
/// Respects project-level model routing overrides. If a model is unavailable, throws
/// so that the stage fails with a clear error.
/// </summary>
public class LlmClientFactory
{
    private readonly LlmSettings _settings;
    private readonly ILogger<LlmClientFactory> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public LlmClientFactory(
        IOptions<LlmSettings> settings,
        ILogger<LlmClientFactory> logger,
        IHttpClientFactory httpClientFactory)
    {
        _settings = settings.Value;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Resolves the IChatClient for a given model name, applying project-level overrides (FR44).
    /// If modelName is null, uses the default model from settings.
    /// </summary>
    public IChatClient CreateClient(string? modelName = null, string? providerOverride = null)
    {
        var resolvedModel = modelName ?? _settings.DefaultModel;
        var resolvedProvider = providerOverride ?? _settings.DefaultProvider;

        if (string.IsNullOrEmpty(resolvedModel))
        {
            throw new InvalidOperationException(
                "No LLM model configured. Set a default model in Llm:DefaultModel or specify a model in the workflow stage.");
        }

        var provider = _settings.Providers.FirstOrDefault(
            p => p.Name.Equals(resolvedProvider, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            throw new InvalidOperationException(
                $"LLM provider '{resolvedProvider}' is not configured. " +
                $"Available providers: {string.Join(", ", _settings.Providers.Select(p => p.Name))}");
        }

        if (string.IsNullOrEmpty(provider.ApiKey))
        {
            throw new InvalidOperationException(
                $"API key not configured for provider '{resolvedProvider}'.");
        }

        // Validate the model is in the provider's allowed list (if specified)
        if (provider.Models.Count > 0 && !provider.Models.Any(
            m => m.Equals(resolvedModel, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Model '{resolvedModel}' is not available for provider '{resolvedProvider}'. " +
                $"Available models: {string.Join(", ", provider.Models)}");
        }

        _logger.LogInformation(
            "Creating IChatClient for provider={Provider}, model={Model}",
            resolvedProvider, resolvedModel);

        return CreateProviderClient(provider, resolvedModel);
    }

    private IChatClient CreateProviderClient(LlmProviderSettings provider, string model)
    {
        var httpClient = _httpClientFactory.CreateClient($"llm-{provider.Name}");

        if (!string.IsNullOrEmpty(provider.BaseUrl))
        {
            httpClient.BaseAddress = new Uri(provider.BaseUrl);
        }

        // Return a placeholder adapter. In production, replace with the provider-specific
        // IChatClient (e.g., from Microsoft.Extensions.AI.OpenAI or an Anthropic package).
        return new PlaceholderChatClient(httpClient, provider.ApiKey, model, provider.Name);
    }
}

/// <summary>
/// Placeholder IChatClient implementation for the MVP. Throws NotImplementedException to indicate
/// that a real provider-specific SDK needs to be wired in. This compiles against the
/// Microsoft.Extensions.AI 10.x IChatClient interface and can be swapped out by registering
/// the real provider SDK in LlmClientFactory.CreateProviderClient().
/// </summary>
internal sealed class PlaceholderChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _providerName;

    public PlaceholderChatClient(HttpClient httpClient, string apiKey, string model, string providerName)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _model = model;
        _providerName = providerName;
    }

    public ChatClientMetadata Metadata => new(_providerName, null, _model);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(
            $"Direct API calls to '{_providerName}' are not yet implemented. " +
            $"Wire in the provider-specific IChatClient implementation (e.g., from Microsoft.Extensions.AI.OpenAI " +
            $"or a community Anthropic package) in LlmClientFactory.CreateProviderClient().");
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(
            $"Streaming API calls to '{_providerName}' are not yet implemented. " +
            $"Wire in the provider-specific IChatClient implementation.");
    }

    public object? GetService(Type serviceType, object? key = null)
    {
        if (serviceType == typeof(IChatClient))
            return this;
        return null;
    }

    public void Dispose()
    {
        // HttpClient lifetime is managed by IHttpClientFactory
    }
}
